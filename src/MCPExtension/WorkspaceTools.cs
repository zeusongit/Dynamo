using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.DesignScript.Runtime;
using Dynamo.Extensions;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Workspaces;
using Dynamo.Models;
using Dynamo.Search.SearchElements;
using Application = System.Windows.Application;

namespace Dynamo.MCP
{
    internal class WorkspaceTools
    {
        private static DynamoModel? _dynamoModel;
        private static WorkspaceModel? _currentWorkspace;
        private static ReadyParams? _readyParams;

        /// <summary>
        /// Initialize from Extension parameters
        /// </summary>
        public static string InitializeFromExtension(DynamoModel dynamoModel)
        {
            try
            {
                _dynamoModel = dynamoModel;
                _currentWorkspace = dynamoModel.CurrentWorkspace as WorkspaceModel;
                
                return $"DynamoWorkspaceTools initialized successfully from Extension. Workspace: {_currentWorkspace?.Name ?? "Unknown"}";
            }
            catch (Exception ex)
            {
                return $"Failed to initialize from Extension: {ex.Message}";
            }
        }
        [IsVisibleInDynamoLibrary(true)]
        public static string CreateNode(string creationName, double x, double y, string? initialValue = null)
        {
            try
            {
                NodeModel nodeModel;

                Dispatcher.Invoke(new Action(() =>
                {
                    nodeModel = _dynamoModel.CreateNodeFromNameOrType(Guid.NewGuid(), creationName, true);

                    _currentWorkspace.AddAndRegisterNode(nodeModel);
                }));

                return $"Created node '{creationName}' at position ({x}, {y}). Node ID: {nodeModel.GUID}";
            }
            catch (Exception e)
            {
                return $"Error creating node: {e.Message}";
            }
         
        }

        [IsVisibleInDynamoLibrary(true)]
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
