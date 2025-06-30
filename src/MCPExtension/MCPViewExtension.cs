using System.Reflection;
using Dynamo.Models;
using Dynamo.Wpf.Extensions;

namespace Dynamo.MCP
{
    public class McpViewExtension : ViewExtensionBase
    {
        public bool readyCalled = false;
        internal DynamoModel _dynamoModel;
        public override void Dispose()
        {
        }

        public override string UniqueId => "A9E38F7D-F264-4575-8DBC-0BF5FC7CF3D5";
        public override string Name => "MCPViewExtension";

        public override void Startup(ViewStartupParams viewStartupParams)
        {
           
        }

        public override void Loaded(ViewLoadedParams viewLoadedParams)
        {
            // Start the MCP server automatically
            var serverResult = DynamoMcpServer.StartMcpServer();

            WorkspaceTools.InitializeFromViewExtension(viewLoadedParams);

            //_dynamoModel.Logger.LogInfo("MCP", serverResult);

            this.readyCalled = true;
        }

    public override void Shutdown()
        {
            // Stop the MCP server when extension shuts down
            var serverResult = DynamoMcpServer.StopMcpServer();
        }
    }
}
