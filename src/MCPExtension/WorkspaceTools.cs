using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dynamo.Extensions;
using Dynamo.Graph.Workspaces;

namespace Dynamo.MCP
{
    internal class WorkspaceTools
    {
        private static WorkspaceModel? _currentWorkspace;
        private static ReadyParams? _readyParams;

        /// <summary>
        /// Initialize from Extension parameters
        /// </summary>
        public static string InitializeFromExtension(ReadyParams readyParams)
        {
            try
            {
                _readyParams = readyParams;
                _currentWorkspace = readyParams.CurrentWorkspaceModel as WorkspaceModel;
                
                return $"DynamoWorkspaceTools initialized successfully from Extension. Workspace: {_currentWorkspace?.Name ?? "Unknown"}";
            }
            catch (Exception ex)
            {
                return $"Failed to initialize from Extension: {ex.Message}";
            }
        }


    }
}
