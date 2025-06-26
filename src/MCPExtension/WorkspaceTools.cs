using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using Autodesk.DesignScript.Runtime;
using Dynamo.Extensions;
using Dynamo.Graph;
using Dynamo.Graph.Connectors;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Workspaces;
using Dynamo.Models;
using Dynamo.Search.SearchElements;
using Dynamo.Wpf.Extensions;
using Application = System.Windows.Application;

namespace Dynamo.MCP
{
    internal class WorkspaceTools
    {
        private static DynamoModel? _dynamoModel;
        private static WorkspaceModel? _currentWorkspace;
        private static ViewLoadedParams? _viewLoadedParams;

        /// <summary>
        /// Initialize from Extension parameters
        /// </summary>
        public static string InitializeFromViewExtension(ViewLoadedParams viewLoadedParams)
        {
            try
            {
                _viewLoadedParams = viewLoadedParams;
                _currentWorkspace = viewLoadedParams.CurrentWorkspaceModel as WorkspaceModel;

                // Get DynamoModel from the view context if needed
                if (viewLoadedParams.DynamoWindow?.DataContext is Dynamo.ViewModels.DynamoViewModel dynamoViewModel)
                {
                    _dynamoModel = dynamoViewModel.Model;
                }

                return $"DynamoWorkspaceTools initialized successfully from Extension. Workspace: {_currentWorkspace?.Name ?? "Unknown"}";
            }
            catch (Exception ex)
            {
                return $"Failed to initialize from Extension: {ex.Message}";
            }
        }

        public static string CreateNode(string creationName, double x, double y, string? initialValue = null)
        {
            try
            {
                NodeModel nodeModel = null;


                _viewLoadedParams.DynamoWindow.Dispatcher.Invoke(new Action(() =>
                {
                    nodeModel = _dynamoModel.CreateNodeFromNameOrType(Guid.NewGuid(), creationName, false);
                    nodeModel.X = x;
                    nodeModel.Y = y;
                    
                    _currentWorkspace.AddAndRegisterNode(nodeModel);
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
            SetNodeValueInternal(node, value);

            node.MarkNodeAsModified(true);

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
                    Categories = GroupNodeTypesByCategory(allNodes),
                    AllNodeTypes = allNodes.OrderBy(nt => nt.CreationName).Select(nt => new
                    {
                        CreationName = nt.CreationName,
                        Name = nt.Name,
                        Assembly = nt.Assembly,
                        Category = nt.FullCategoryName
                    }).ToList()
                };

                return JsonSerializer.Serialize(nodeTypesInfo, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
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
