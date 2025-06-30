using System;
using System.Collections.Generic;
using System.IO;
using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using Dynamo.ViewModels;

namespace Dynamo.Wpf.Utilities
{
    internal class ClaudeClient
    {
        public class NodeCreationInfo
        {
            [FunctionProperty("Type of node to create (e.g., 'CoreNodeModels.Input.DoubleInput', 'DSCore.Math.Add')", true)]
            public string NodeType { get; set; }
            [FunctionProperty("X coordinate in the workspace", true)]
            public double X { get; set; }
            [FunctionProperty("Y coordinate in the workspace", true)]
            public double Y { get; set; }
            [FunctionProperty("Initial value for the node (optional)", false)]
            public string? InitialValue { get; set; }
        }
        public class NodeConnectionInfo
        {
            [FunctionProperty("ID of the source node", true)] public string sourceNodeId { get; set; }

            [FunctionProperty("ID of the target node", true)] public string targetNodeId { get; set; }

            [FunctionProperty("Output port index of the source node (default: 0)", false)] public int sourcePortIndex { get; set; }

            [FunctionProperty("Input port index of the target node (default: 0)", false)] public int targetPortIndex { get; set; }
        }
        public static async void DoYourMagic(DynamoViewModel dynamoViewModel, string prompt)
        {
            //prompt = "place all the nodes in dynamo to facilitate creating a line, dividing it, and placing spheres along it.";
            WorkspaceTools.Initialize(dynamoViewModel);
            var client = new AnthropicClient();
            var messages = new List<Message>()
            {
                new Message(RoleType.User, prompt),
            };

            var tools =
                new List<Anthropic.SDK.Common.Tool>{
                Anthropic.SDK.Common.Tool.FromFunc("create_node",
                    (
                        [FunctionParameter("Type of node to create (e.g., 'CoreNodeModels.Input.DoubleInput', 'DSCore.Math.Add')", true)] string nodeType,
                        [FunctionParameter("X coordinate in the workspace", true)] double x,
                        [FunctionParameter("Y coordinate in the workspace", true)] double y,
                        [FunctionParameter("Initial value for the node (optional)", false)] string initialValue
                    ) => WorkspaceTools.CreateNode(nodeType, x, y, initialValue),
                    "Create a new node in the Dynamo workspace at specified coordinates"),

            // create_multiple_nodes
            Anthropic.SDK.Common.Tool.FromFunc("create_multiple_nodes",
                (
                    [FunctionParameter("Array of nodes to create, each with type, coordinates, and optional initial value", true)] NodeCreationInfo[] nodes
                ) => WorkspaceTools.CreateMultipleNodes(nodes),
                "Create multiple nodes in the Dynamo workspace at specified coordinates"),

            // connect_nodes
            /*Anthropic.SDK.Common.Tool.FromFunc("connect_nodes",
                (
                    [FunctionParameter("ID of the source node", true)] string sourceNodeId,
                    [FunctionParameter("ID of the target node", true)] string targetNodeId,
                    [FunctionParameter("Output port index of the source node (default: 0)", false)] int sourcePortIndex,
                    [FunctionParameter("Input port index of the target node (default: 0)", false)] int targetPortIndex
                ) => WorkspaceTools.ConnectNodes(sourceNodeId, targetNodeId, sourcePortIndex, targetPortIndex),
                "Connect the output of one node to the input of another node"),*/

            // connect_multiple_nodes
            Anthropic.SDK.Common.Tool.FromFunc("connect_multiple_nodes",
                (
                    [FunctionParameter("Array of node connections to be created", true)] NodeConnectionInfo[] nodes
                ) => WorkspaceTools.ConnectMultipleNodes(nodes),
                "Connect the output of one node to the input of another node for each node connction"),

            // delete_node
            /*Anthropic.SDK.Common.Tool.FromFunc("delete_node",
                (
                    [FunctionParameter("ID of the node to delete", true)] string nodeId
                ) => string.Empty,
                "Delete a node from the Dynamo workspace"),*/

            // get_workspace_info
            Anthropic.SDK.Common.Tool.FromFunc("get_workspace_info",
                () => WorkspaceTools.GetWorkspaceInfo(),
                "Get detailed information about the current Dynamo workspace including all nodes and connections"),

            // run_graph
            /*Anthropic.SDK.Common.Tool.FromFunc("run_graph",
                () => string.Empty,
                "Run/execute the current Dynamo graph and return execution results"),*/

            // set_node_value
            /*Anthropic.SDK.Common.Tool.FromFunc("set_node_value",
                (
                    [FunctionParameter("ID of the node to modify", true)] string nodeId,
                    [FunctionParameter("New value for the node", true)] string value
                ) => WorkspaceTools.SetNodeValue(nodeId, value),
                "Set the value of an input node in the workspace"),*/

            // get_all_available_nodes
            /*Anthropic.SDK.Common.Tool.FromFunc("get_all_available_nodes",
                () => WorkspaceTools.GetAllAvailableNodes(),
                "Get a list of available nodes that can be created in Dynamo"),*/
        };
            const string reflectionCache = "nodes.json";
            if (!File.Exists(reflectionCache))
            {
                File.WriteAllText(reflectionCache, WorkspaceTools.GetAllAvailableNodes());
            }
            var allnodes = File.ReadAllText(reflectionCache);
            var parameters = new MessageParameters()
            {
                System = new List<SystemMessage>
                {
                    new SystemMessage("You are a dynamo assistant to create graphs in dynamoBIM. Consider the following API documentation: "),
                    new SystemMessage(allnodes),
                },
                Messages = messages,
                MaxTokens = 4096,
                Model = AnthropicModels.Claude4Sonnet,
                Stream = false,
                //Temperature = 1.0m,
                Tools = tools,
                PromptCaching = PromptCacheType.AutomaticToolsAndSystem,
            };
            while (true)
            {
                var res = await client.Messages.GetClaudeMessageAsync(parameters);

                messages.Add(res.Message);

                if (res.ToolCalls == null || res.ToolCalls.Count == 0)
                {
                    break;
                }

                foreach (var toolCall in res.ToolCalls)
                {
                    var response = toolCall.Invoke<string>();

                    messages.Add(new Message(toolCall, response));
                }
            }
        }
    }
}
