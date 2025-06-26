using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using Autodesk.DesignScript.Runtime;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Workspaces;
using Dynamo.Models;
using Dynamo.Graph.Connectors;
using Dynamo.Core;
using Dynamo.Utilities;
using Dynamo.Graph.Nodes.ZeroTouch;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dynamo.MCP
{
    /// <summary>
    /// MCP Server that provides tools for controlling Dynamo workspace
    /// </summary>
    public static class DynamoMcpServer
    {
        private static HttpListener? _listener;
        private static bool _isRunning = false;
        private static CancellationTokenSource? _cancellationTokenSource;
        private static int _serverPort = 8080;

        /// <summary>
        /// Start the MCP server for Dynamo workspace control
        /// </summary>
        /// <param name="port">Port to run the HTTP server on (default: 8080)</param>
        /// <returns>Status message</returns>
        [IsVisibleInDynamoLibrary(true)]
        public static string StartMcpServer(int port = 8080)
        {
            if (_isRunning)
                return $"MCP Server is already running on port {_serverPort}";

            try
            {
                _serverPort = port;
                _cancellationTokenSource = new CancellationTokenSource();

                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Start();

                // Run the server in background
                Task.Run(async () =>
                {
                    try
                    {
                        while (_listener.IsListening && !_cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            var context = await _listener.GetContextAsync();
                            _ = Task.Run(() => HandleRequest(context), _cancellationTokenSource.Token);
                        }
                    }
                    catch (Exception ex) when (_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        // Expected when stopping
                    }
                });

                _isRunning = true;
                return $"Simple HTTP Server started successfully on http://localhost:{port}. Basic MCP endpoint available at /sse";
            }
            catch (Exception ex)
            {
                return $"Failed to start MCP Server: {ex.Message}";
            }
        }

        private static void HandleRequest(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                // Set CORS headers
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }

                if (request.Url?.AbsolutePath == "/mcp" && request.HttpMethod == "POST")
                {
                    // Check Accept header for Streamable HTTP transport
                    var acceptHeader = request.Headers["Accept"];
                    var supportsSSE = acceptHeader?.Contains("text/event-stream") == true;
                    var supportsJSON = acceptHeader?.Contains("application/json") == true;

                    HandleMcpMessage(request, response);
                }
                else if (request.Url?.AbsolutePath == "/mcp" && request.HttpMethod == "GET")
                {
                    HandleSSEConnection(request, response);
                }
                else
                {
                    // Basic status endpoint
                    response.ContentType = "application/json";
                    var statusMessage = "{\"status\":\"running\",\"port\":" + _serverPort + ",\"endpoints\":[\"/mcp\"]}";
                    var bytes = Encoding.UTF8.GetBytes(statusMessage);
                    response.OutputStream.Write(bytes, 0, bytes.Length);
                    response.Close();
                }
            }
            catch (Exception)
            {
                // Ignore errors in request handling
            }
        }

        private static void HandleSSEConnection(HttpListenerRequest request, HttpListenerResponse response)
        {
            response.ContentType = "text/event-stream";
            response.Headers.Add("Cache-Control", "no-cache");
            response.Headers.Add("Connection", "keep-alive");

            // Keep connection open for server-to-client messages
            // This is used for server-initiated requests/notifications
            var keepAliveMessage = "data: {\"type\":\"keepalive\"}\n\n";
            var bytes = Encoding.UTF8.GetBytes(keepAliveMessage);
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.OutputStream.Flush();
            response.Close();
        }

        private static void HandleMcpMessage(HttpListenerRequest request, HttpListenerResponse response)
        {
            using var reader = new StreamReader(request.InputStream);
            var messageBody = reader.ReadToEnd();

            try
            {
                var mcpRequest = JsonSerializer.Deserialize<JsonElement>(messageBody);
                var method = mcpRequest.GetProperty("method").GetString();
                var id = mcpRequest.TryGetProperty("id", out var idProp) ? idProp.ToString() : null;

                string responseMessage = method switch
                {
                    "initialize" => HandleInitialize(mcpRequest, id),
                    "tools/list" => HandleToolsList(id),
                    "tools/call" => HandleToolCall(mcpRequest, id),
                    _ => CreateErrorResponse(id, -32601, "Method not found")
                };

                response.ContentType = "application/json";
                var bytes = Encoding.UTF8.GetBytes(responseMessage);
                response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                var errorResponse = CreateErrorResponse(null, -32700, "Parse error: " + ex.Message);
                response.ContentType = "application/json";
                var bytes = Encoding.UTF8.GetBytes(errorResponse);
                response.OutputStream.Write(bytes, 0, bytes.Length);
            }

            response.Close();
        }

        private static string HandleInitialize(JsonElement request, string? id)
        {
            var response = new
            {
                jsonrpc = "2.0",
                id = id,
                result = new
                {
                    protocolVersion = "2025-03-26",
                    capabilities = new
                    {
                        tools = new { listChanged = true },
                        resources = new { subscribe = false, listChanged = false },
                        prompts = new { listChanged = false }
                    },
                    serverInfo = new
                    {
                        name = "dynamo-mcp-server",
                        version = "1.0.0"
                    }
                }
            };

            return JsonSerializer.Serialize(response);
        }

        private static string HandleToolsList(string? id)
        {
            var tools = new object[]
            {
                new
                {
                    name = "create_node",
                    description = "Create a new node in the Dynamo workspace at specified coordinates",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            nodeType = new { type = "string", description = "Type of node to create (e.g., 'CoreNodeModels.Input.DoubleInput', 'DSCore.Math.Add')" },
                            x = new { type = "number", description = "X coordinate in the workspace" },
                            y = new { type = "number", description = "Y coordinate in the workspace" },
                            initialValue = new { type = "string", description = "Initial value for the node (optional)" }
                        },
                        required = new[] { "nodeType", "x", "y" }
                    }
                },
                new
                {
                    name = "connect_nodes",
                    description = "Connect the output of one node to the input of another node",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            sourceNodeId = new { type = "string", description = "ID of the source node" },
                            targetNodeId = new { type = "string", description = "ID of the target node" },
                            sourcePortIndex = new { type = "number", description = "Output port index of the source node (default: 0)" },
                            targetPortIndex = new { type = "number", description = "Input port index of the target node (default: 0)" }
                        },
                        required = new[] { "sourceNodeId", "targetNodeId" }
                    }
                },
                new
                {
                    name = "delete_node",
                    description = "Delete a node from the Dynamo workspace",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            nodeId = new { type = "string", description = "ID of the node to delete" }
                        },
                        required = new[] { "nodeId" }
                    }
                },
                new
                {
                    name = "get_workspace_info",
                    description = "Get detailed information about the current Dynamo workspace including all nodes and connections",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new { },
                        required = new string[] { }
                    }
                },
                new
                {
                    name = "run_graph",
                    description = "Run/execute the current Dynamo graph and return execution results",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new { },
                        required = new string[] { }
                    }
                },
                new
                {
                    name = "set_node_value",
                    description = "Set the value of an input node in the workspace",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            nodeId = new { type = "string", description = "ID of the node to modify" },
                            value = new { type = "string", description = "New value for the node" }
                        },
                        required = new[] { "nodeId", "value" }
                    }
                },
                new
                {
                    name = "get_all_available_nodes",
                    description = "Get a list of available nodes that can be created in Dynamo",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new { },
                        required = new string[] { }
                    }
                }
            };

            var response = new
            {
                jsonrpc = "2.0",
                id = id,
                result = new { tools = tools }
            };

            return JsonSerializer.Serialize(response);
        }

        private static string HandleToolCall(JsonElement request, string? id)
        {
            try
            {
                var @params = request.GetProperty("params");
                var name = @params.GetProperty("name").GetString();
                var arguments = @params.TryGetProperty("arguments", out var args) ? args : new JsonElement();

                string result = name switch
                {
                    "create_node" => WorkspaceTools.CreateNode(
                        arguments.GetProperty("nodeType").GetString()!,
                        arguments.GetProperty("x").GetDouble(),
                        arguments.GetProperty("y").GetDouble(),
                        arguments.TryGetProperty("initialValue", out var val) ? val.GetString() : null),
                    "connect_nodes" => WorkspaceTools.ConnectNodes(
                        arguments.GetProperty("sourceNodeId").GetString()!,
                        arguments.GetProperty("targetNodeId").GetString()!,
                       arguments.TryGetProperty("sourcePortIndex", out var spi) ? spi.GetInt32() : 0,
                       arguments.TryGetProperty("targetPortIndex", out var tpi) ? tpi.GetInt32() : 0),
                    //"delete_node" => DynamoWorkspaceTools.DeleteNode(arguments.GetProperty("nodeId").GetString()!),
                    //"get_workspace_info" => DynamoWorkspaceTools.GetWorkspaceInfo(),
                    //"run_graph" => DynamoWorkspaceTools.RunGraph(),
                    "set_node_value" => WorkspaceTools.SetNodeValue(
                        arguments.GetProperty("nodeId").GetString()!,
                       arguments.GetProperty("value").GetString()!),
                    "get_all_available_nodes" => WorkspaceTools.GetAllAvailableNodes(),
                    _ => "Unknown tool: " + name
                };

                var response = new
                {
                    jsonrpc = "2.0",
                    id = id,
                    result = new { content = new[] { new { type = "text", text = result } } }
                };

                return JsonSerializer.Serialize(response);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse(id, -32602, "Invalid params: " + ex.Message);
            }
        }

        private static string CreateErrorResponse(string? id, int code, string message)
        {
            var response = new
            {
                jsonrpc = "2.0",
                id = id,
                error = new { code = code, message = message }
            };
            return JsonSerializer.Serialize(response);
        }

        /// <summary>
        /// Stop the MCP server
        /// </summary>
        /// <returns>Status message</returns>
        [IsVisibleInDynamoLibrary(true)]
        public static string StopMcpServer()
        {
            if (!_isRunning || _listener == null)
                return "MCP Server is not running";

            try
            {
                _cancellationTokenSource?.Cancel();
                _listener?.Stop();
                _listener?.Close();
                _cancellationTokenSource?.Dispose();

                _listener = null;
                _cancellationTokenSource = null;
                _isRunning = false;

                return "MCP Server stopped successfully";
            }
            catch (Exception ex)
            {
                return $"Failed to stop MCP Server: {ex.Message}";
            }
        }

        /// <summary>
        /// Get the current status of the MCP server
        /// </summary>
        /// <returns>Server status</returns>
        [IsVisibleInDynamoLibrary(true)]
        public static string GetMcpServerStatus()
        {
            return _isRunning
                ? $"MCP Server is running on http://localhost:{_serverPort} and ready to accept connections"
                : "MCP Server is stopped";
        }
    }



    // Simplified Dynamo extension - MCP prompts removed for compatibility
}
