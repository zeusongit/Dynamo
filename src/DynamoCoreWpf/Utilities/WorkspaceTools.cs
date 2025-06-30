using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.DesignScript.Runtime;
using Dynamo.Controls;
using Dynamo.Graph.Connectors;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Workspaces;
using Dynamo.Models;
using Dynamo.Search.SearchElements;
using Dynamo.ViewModels;
using static Dynamo.Wpf.Utilities.ClaudeClient;

namespace Dynamo.Wpf.Utilities
{
    internal class WorkspaceTools
    {
        private static DynamoModel? _dynamoModel;
        private static WorkspaceModel? _currentWorkspace;
        private static Dictionary<string, NodeModel> _createdNodes = new Dictionary<string, NodeModel>();
        private static bool initialized = false;

        /// <summary>
        /// Initialize from Extension parameters
        /// </summary>
        public static string Initialize(DynamoViewModel dynamoViewModel)
        {
            if (initialized) return string.Empty;
            try
            {
                initialized = true;
                _currentWorkspace = dynamoViewModel.CurrentSpace;
                _dynamoModel = dynamoViewModel.Model;

                return $"DynamoWorkspaceTools initialized successfully from Extension. Workspace: {_currentWorkspace?.Name ?? "Unknown"}";
            }
            catch (Exception ex)
            {
                return $"Failed to initialize from Extension: {ex.Message}";
            }
        }

        /// <summary>
        /// Get information about the current workspace
        /// </summary>
        [IsVisibleInDynamoLibrary(true)]
        public static string GetWorkspaceInfo()
        {
            try
            {

                try
                {
                    var allNodes = _currentWorkspace.Nodes.ToList();
                    var allConnectors = _currentWorkspace.Connectors.ToList();

                    var workspaceInfo = new
                    {
                        WorkspaceName = _currentWorkspace.Name ?? "Unnamed",
                        WorkspaceGuid = _currentWorkspace.Guid.ToString(),
                        TotalNodes = allNodes.Count,
                        TotalConnectors = allConnectors.Count,
                        TrackedNodes = _createdNodes.Count,
                        ModelInitialized = _dynamoModel != null,
                        Nodes = allNodes.Select(n => new
                        {
                            Id = n.GUID.ToString(),
                            Type = n.GetType().Name,
                            Name = n.Name ?? "Unnamed",
                            Position = new { X = n.X, Y = n.Y },
                            InputPorts = n.InPorts.Count,
                            OutputPorts = n.OutPorts.Count
                        }).ToList(),
                        Connectors = allConnectors.Select(c => new
                        {
                            Id = c.GUID.ToString(),
                            SourceNode = c.Start?.Owner?.GUID.ToString() ?? "Unknown",
                            TargetNode = c.End?.Owner?.GUID.ToString() ?? "Unknown"
                        }).ToList()
                    };

                    return JsonSerializer.Serialize(workspaceInfo, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });
                }
                catch (Exception ex)
                {
                    return $"Error accessing workspace data: {ex.Message}. Try using the extension from within Dynamo.";
                }
            }
            catch (Exception ex)
            {
                return $"Error getting workspace info: {ex.Message}";
            }
        }

        public static string CreateNode(string creationName, double x, double y, string? initialValue = null)
        {
            try
            {
                NodeModel nodeModel = null;


                DynamoModel.OnRequestDispatcherInvoke(new Action(() =>
                {
                    var id = Guid.NewGuid();
                    nodeModel = _dynamoModel.CreateNodeFromNameOrType(id, creationName, false);
                    nodeModel.X = x;
                    nodeModel.Y = y;

                    _currentWorkspace.AddAndRegisterNode(nodeModel);

                    SetNodeValue(id.ToString(), initialValue);
                }));

                if (nodeModel is null)
                {
                    return $"Error creating node: {creationName}";
                }
                return $"Created node '{creationName}' at position ({x}, {y}). Node ID: {nodeModel.GUID}";
            }
            catch (Exception e)
            {
                return $"Error creating node: {e.Message}";
            }

        }

        public static string CreateMultipleNodes(NodeCreationInfo[] nodes)
        {
            try
            {
                List<string> messages = [];
                foreach (var nodeElement in nodes)
                {
                    string creationName = nodeElement.NodeType;
                    double x = nodeElement.X;
                    double y = nodeElement.Y;
                    string? initialValue = nodeElement.InitialValue;

                    string result = CreateNode(creationName, x, y, initialValue);
                    messages.Add(result);
                    if (result.StartsWith("Error"))
                    {
                        return $"Failed to create node: {result}";
                    }
                }
                return string.Join("\n", messages);
            }
            catch (Exception ex)
            {
                return $"Error creating multiple nodes: {ex.Message}";
            }
        }

        public static string ConnectMultipleNodes(NodeConnectionInfo[] connections)
        {
            try
            {
                foreach (var connection in connections)
                {
                    string result = ConnectNodes(
                        connection.sourceNodeId, connection.targetNodeId,
                        connection.sourcePortIndex, connection.targetPortIndex);
                    if (result.StartsWith("Error"))
                    {
                        return $"Failed to create node: {result}";
                    }
                }

                return GetWorkspaceInfo();
            }
            catch (Exception ex)
            {
                return $"Error creating multiple nodes: {ex.Message}";
            }
        }

        /// <summary>
        /// Connect two nodes in the workspace
        /// </summary>
        public static string ConnectNodes(string sourceNodeId, string targetNodeId, int sourcePortIndex = 0,
            int targetPortIndex = 0)
        {
            // Parse GUIDs
            if (!Guid.TryParse(sourceNodeId, out Guid sourceGuid) || !Guid.TryParse(targetNodeId, out Guid targetGuid))
            {
                return "Error: Invalid node ID format";
            }

            // Find the source and target nodes
            var sourceNode = _currentWorkspace.Nodes.FirstOrDefault(n => n.GUID == sourceGuid);
            var targetNode = _currentWorkspace.Nodes.FirstOrDefault(n => n.GUID == targetGuid);

            ConnectorModel? connector = null;
            string? errorMessage = null;
            // Use OnRequestDispatcherInvoke for thread-safe ConnectorModel creation
            DynamoModel.OnRequestDispatcherInvoke(() =>
            {
                var guids = _currentWorkspace.Nodes.Select(x => x.GUID).ToList();
                connector = ConnectorModel.Make(sourceNode, targetNode, sourcePortIndex, targetPortIndex);
            });

            if (connector != null)
            {
                return $"Connected node {sourceNodeId}[{sourcePortIndex}] to node {targetNodeId}[{targetPortIndex}]";
            }
            else
            {
                return $"Error: Failed to create connector{(errorMessage != null ? $": {errorMessage}" : "")}";
            }
        }

        /// <summary>
        /// Set the value of an input node
        /// </summary>
        public static string SetNodeValue(string nodeId, string value)
        {
            if (!Guid.TryParse(nodeId, out Guid nodeGuid))
            {
                return "Error: Invalid node ID format";
            }

            // Find the node in the workspace
            var node = _currentWorkspace.Nodes.FirstOrDefault(n => n.GUID == nodeGuid);
            if (node == null)
            {
                return $"Error: Node {nodeId} not found in workspace";
            }

            // Try to set the node value
            DynamoModel.OnRequestDispatcherInvoke(() =>
            {
                SetNodeValueInternal(node, value);

                node.MarkNodeAsModified(true);
            });

            return $"Set node {nodeId} value to '{value}'";
        }

        private static void SetNodeValueInternal(NodeModel node, string value)
        {
            try
            {
                // Try to set value using reflection
                var valueProperty = node.GetType().GetProperty("Value");
                if (valueProperty != null && valueProperty.CanWrite)
                {
                    var propertyType = valueProperty.PropertyType;
                    object? convertedValue = value;

                    if (propertyType == typeof(double) && double.TryParse(value, out double dValue))
                        convertedValue = dValue;
                    else if (propertyType == typeof(int) && int.TryParse(value, out int iValue))
                        convertedValue = iValue;
                    else if (propertyType == typeof(bool) && bool.TryParse(value, out bool bValue))
                        convertedValue = bValue;

                    valueProperty.SetValue(node, convertedValue);
                }
            }
            catch
            {
                // Ignore value setting errors
            }
        }

        public static string GetAllAvailableNodes()
        {
            try
            {
                var allNodes = _dynamoModel.SearchModel.Entries.Where(entry => entry.IsVisibleInSearch).ToList();

                var nodeTypesInfo = new
                {
                    TotalNodeTypes = allNodes.Count,
                    //Categories = GroupNodeTypesByCategory(allNodes),
                    AllNodeTypes = allNodes.OrderBy(nt => nt.CreationName).Select(nt => new
                    {
                        CreationName = nt.CreationName,
                        Category = nt.FullCategoryName
                    }).ToList()
                };

                var nodeInfo =  JsonSerializer.Serialize(nodeTypesInfo, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                return nodeInfo;
            }
            catch (Exception e)
            {
                return $"Error getting available node types: {e.Message}";
            }

        }
        private static Dictionary<string, List<string>> GroupNodeTypesByCategory(List<NodeSearchElement> nodeSearchElements)
        {
            var categories = new Dictionary<string, List<string>>();

            foreach (var nse in nodeSearchElements)
            {
                var category = nse.FullCategoryName;
                if (!categories.ContainsKey(category))
                {
                    categories[category] = new List<string>();
                }
                categories[category].Add(nse.FullName ?? nse.CreationName);
            }

            // Sort each category's node types
            foreach (var category in categories.Keys.ToList())
            {
                categories[category] = categories[category].OrderBy(name => name).ToList();
            }

            return categories;
        }
    }
}
