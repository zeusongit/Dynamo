using System.Reflection;
using Dynamo.Extensions;
using Dynamo.Models;

namespace Dynamo.MCP
{
    public class McpExtension : IExtension
    {
        public bool readyCalled = false;
        public void Dispose()
        {
        }

        public string UniqueId => "A9E38F7D-F264-4575-8DBC-0BF5FC7CF3D5";
        public string Name => "MCPExtension";
        public void Startup(StartupParams sp)
        {
           
        }

        public void Ready(ReadyParams rp)
        {
            // Get DynamoModel instance
            var dynamoModel = GetDynamoModelFromReadyParams(rp);

            // Start the MCP server automatically
            var serverResult = DynamoMcpServer.StartMcpServer();

            this.readyCalled = true;
        }

        public static DynamoModel? GetDynamoModelFromReadyParams(ReadyParams readyParams)
        {
            // Use reflection to access the private 'dynamoModel' field
            var field = typeof(ReadyParams).GetField("dynamoModel", BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(readyParams) as DynamoModel;
        }

    public void Shutdown()
        {
        }
    }
}
