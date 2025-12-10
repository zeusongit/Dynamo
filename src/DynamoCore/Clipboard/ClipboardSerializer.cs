using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dynamo.Engine;
using Dynamo.Graph;
using Dynamo.Graph.Annotations;
using Dynamo.Graph.Connectors;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Notes;
using Dynamo.Graph.Workspaces;
using Dynamo.Logging;
using Dynamo.Utilities;
using DynamoUtilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Dynamo.Core.Clipboard
{
    /// <summary>
    /// Handles serialization and deserialization of clipboard data.
    /// Uses the same JSON format as .dyn files for compatibility.
    /// </summary>
    public class ClipboardSerializer
    {
        private readonly EngineController engineController;
        private readonly ILogger logger;
        private readonly string instanceId;

        /// <summary>
        /// Creates a new ClipboardSerializer
        /// </summary>
        /// <param name="engineController">The engine controller for serialization context</param>
        /// <param name="logger">Logger for error reporting</param>
        /// <param name="instanceId">Unique identifier for this Dynamo instance</param>
        public ClipboardSerializer(EngineController engineController, ILogger logger, string instanceId)
        {
            this.engineController = engineController;
            this.logger = logger;
            this.instanceId = instanceId ?? Guid.NewGuid().ToString();
        }

        /// <summary>
        /// Serializes a collection of models to clipboard data format.
        /// </summary>
        /// <param name="models">The models to serialize (nodes, connectors, notes, annotations)</param>
        /// <param name="workspace">The workspace containing the models (used for package info)</param>
        /// <param name="nodeViewData">Optional view data for nodes</param>
        /// <returns>Serialized clipboard data</returns>
        public ClipboardData Serialize(
            IEnumerable<ModelBase> models,
            WorkspaceModel workspace = null,
            IEnumerable<ClipboardNodeViewData> nodeViewData = null)
        {
            var modelList = models.ToList();
            var nodes = modelList.OfType<NodeModel>().ToList();
            var connectors = modelList.OfType<ConnectorModel>().ToList();
            var notes = modelList.OfType<NoteModel>().ToList();
            var annotations = modelList.OfType<AnnotationModel>().ToList();
            var connectorPins = modelList.OfType<ConnectorPinModel>().ToList();

            var clipboardData = new ClipboardData
            {
                Version = AssemblyHelper.GetDynamoVersion().ToString(),
                Timestamp = DateTime.UtcNow,
                SourceInstanceId = instanceId
            };

            // Track package dependencies
            var packageDependencies = new Dictionary<string, ClipboardDependencyData>();

            // Serialize nodes - manually add type information for security
            var settings = GetSerializerSettings();
            
            foreach (var node in nodes)
            {
                var nodeJson = JObject.FromObject(node, JsonSerializer.Create(settings));
                
                // Manually add ConcreteType for type information (safe approach)
                var typeName = node.GetType().AssemblyQualifiedName;
                nodeJson["ConcreteType"] = typeName;
                
                // Explicitly add X, Y positions (they are not serialized by default)
                nodeJson["X"] = node.X;
                nodeJson["Y"] = node.Y;
                
                // Try to get package information for this node
                if (workspace != null)
                {
                    var packageInfo = workspace.GetNodePackage(node);
                    if (packageInfo != null)
                    {
                        var packageKey = $"{packageInfo.Name}@{packageInfo.Version}";
                        if (!packageDependencies.TryGetValue(packageKey, out var depData))
                        {
                            depData = new ClipboardDependencyData
                            {
                                Name = packageInfo.Name,
                                Version = packageInfo.Version?.ToString(),
                                ReferenceType = "Package"
                            };
                            packageDependencies[packageKey] = depData;
                        }
                        depData.Nodes.Add(node.GUID.ToString());
                        
                        // Also add package info to node JSON for reference
                        nodeJson["PackageName"] = packageInfo.Name;
                        nodeJson["PackageVersion"] = packageInfo.Version?.ToString();
                    }
                }
                
                clipboardData.Nodes.Add(nodeJson);
                
                // Add node view data with positions
                clipboardData.NodeViews.Add(new ClipboardNodeViewData
                {
                    Id = node.GUID.ToString(),
                    Name = node.Name,
                    X = node.X,
                    Y = node.Y,
                    ShowGeometry = node.ShouldDisplayPreview,
                    Excluded = node.IsFrozen,
                    IsSetAsInput = node.IsSetAsInput,
                    IsSetAsOutput = node.IsSetAsOutput
                });
            }
            
            // Add collected package dependencies
            clipboardData.Dependencies.AddRange(packageDependencies.Values);

            // Serialize connectors
            foreach (var connector in connectors)
            {
                var connectorData = new JObject
                {
                    ["Start"] = connector.Start.GUID.ToString("N"),
                    ["End"] = connector.End.GUID.ToString("N"),
                    ["Id"] = connector.GUID.ToString("N"),
                    ["IsHidden"] = connector.IsHidden
                };
                clipboardData.Connectors.Add(connectorData);
            }

            // Serialize connector pins
            foreach (var pin in connectorPins)
            {
                clipboardData.ConnectorPins.Add(new ClipboardConnectorPinData
                {
                    Id = pin.GUID.ToString(),
                    X = pin.X,
                    Y = pin.Y,
                    ConnectorId = pin.ConnectorId.ToString()
                });
            }

            // Serialize notes
            foreach (var note in notes)
            {
                clipboardData.Notes.Add(new ClipboardNoteData
                {
                    Id = note.GUID.ToString(),
                    X = note.X,
                    Y = note.Y,
                    Text = note.Text,
                    PinnedNodeId = note.PinnedNode?.GUID.ToString()
                });
            }

            // Serialize annotations
            foreach (var annotation in annotations)
            {
                clipboardData.Annotations.Add(new ClipboardAnnotationData
                {
                    Id = annotation.GUID.ToString(),
                    Title = annotation.AnnotationText,
                    DescriptionText = annotation.AnnotationDescriptionText,
                    IsExpanded = true, // Default to expanded
                    Nodes = annotation.Nodes.Select(n => n.GUID.ToString()).ToList(),
                    HasNestedGroups = annotation.HasNestedGroups,
                    FontSize = annotation.FontSize,
                    GroupStyleId = annotation.GroupStyleId,
                    Background = annotation.Background,
                    WidthAdjustment = annotation.WidthAdjustment,
                    HeightAdjustment = annotation.HeightAdjustment,
                    UserSetWidth = annotation.UserSetWidth,
                    UserSetHeight = annotation.UserSetHeight,
                    IsOptionalInPortsCollapsed = annotation.IsOptionalInPortsCollapsed,
                    IsUnconnectedOutPortsCollapsed = annotation.IsUnconnectedOutPortsCollapsed
                });
            }

            // Add additional node view data if provided (skip duplicates)
            if (nodeViewData != null)
            {
                var existingIds = new HashSet<string>(clipboardData.NodeViews.Select(v => v.Id));
                foreach (var viewData in nodeViewData)
                {
                    if (!existingIds.Contains(viewData.Id))
                    {
                        clipboardData.NodeViews.Add(viewData);
                        existingIds.Add(viewData.Id);
                    }
                }
            }

            return clipboardData;
        }

        /// <summary>
        /// Serializes clipboard data to a JSON string.
        /// </summary>
        /// <param name="clipboardData">The clipboard data to serialize</param>
        /// <returns>JSON string representation</returns>
        public string SerializeToJson(ClipboardData clipboardData)
        {
            var wrapper = new JObject
            {
                [ClipboardData.ClipboardFormatId] = JObject.FromObject(clipboardData, 
                    JsonSerializer.Create(GetSerializerSettings()))
            };

            var json = wrapper.ToString(Formatting.Indented);
            return json;
        }

        /// <summary>
        /// Deserializes clipboard data from a JSON string.
        /// </summary>
        /// <param name="json">The JSON string to deserialize</param>
        /// <returns>The deserialized clipboard data, or null if invalid</returns>
        public ClipboardData DeserializeFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                // Replace ConcreteType with $type for Newtonsoft.Json deserialization
                var processedJson = SerializationExtensions.ReplaceTypeDeclarations(json, fromServer: true);

                var wrapper = JObject.Parse(processedJson);
                var clipboardToken = wrapper[ClipboardData.ClipboardFormatId];

                if (clipboardToken == null)
                {
                    logger?.LogWarning("Invalid clipboard data: missing DynamoClipboard wrapper", 
                        WarningLevel.Moderate);
                    return null;
                }

                var settings = GetDeserializerSettings();
                return clipboardToken.ToObject<ClipboardData>(JsonSerializer.Create(settings));
            }
            catch (JsonException ex)
            {
                logger?.LogWarning($"Failed to deserialize clipboard data: {ex.Message}", 
                    WarningLevel.Moderate);
                return null;
            }
        }

        /// <summary>
        /// Validates clipboard data before pasting.
        /// </summary>
        /// <param name="clipboardData">The clipboard data to validate</param>
        /// <returns>Validation result with any issues found</returns>
        public ClipboardValidationResult Validate(ClipboardData clipboardData)
        {
            var result = new ClipboardValidationResult { IsValid = true };

            if (clipboardData == null)
            {
                result.IsValid = false;
                result.Errors.Add("Clipboard data is null");
                return result;
            }

            // Validate version compatibility
            if (!string.IsNullOrEmpty(clipboardData.Version))
            {
                if (Version.TryParse(clipboardData.Version, out var clipboardVersion))
                {
                    var currentVersion = AssemblyHelper.GetDynamoVersion();
                    if (clipboardVersion.Major > currentVersion.Major)
                    {
                        result.Warnings.Add(
                            $"Clipboard data is from a newer version of Dynamo ({clipboardData.Version}). " +
                            "Some features may not be available.");
                    }
                }
            }

            // Check for reasonable limits (security)
            const int maxNodes = 10000;
            const int maxConnectors = 50000;

            if (clipboardData.Nodes.Count > maxNodes)
            {
                result.IsValid = false;
                result.Errors.Add($"Clipboard contains too many nodes ({clipboardData.Nodes.Count}). Maximum is {maxNodes}.");
            }

            if (clipboardData.Connectors.Count > maxConnectors)
            {
                result.IsValid = false;
                result.Errors.Add($"Clipboard contains too many connectors ({clipboardData.Connectors.Count}). Maximum is {maxConnectors}.");
            }

            return result;
        }

        private JsonSerializerSettings GetSerializerSettings()
        {
            // Use TypeNameHandling.None - we manually add ConcreteType for type info
            return new JsonSerializerSettings
            {
                Error = (sender, args) =>
                {
                    args.ErrorContext.Handled = true;
                    logger?.Log(args.ErrorContext.Error.Message);
                },
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                TypeNameHandling = TypeNameHandling.None,
                Formatting = Formatting.Indented,
                Culture = CultureInfo.InvariantCulture,
                Converters = new List<JsonConverter>
                {
                    new ConnectorConverter(logger),
                    new DummyNodeWriteConverter(),
                    new TypedParameterConverter()
                },
                ReferenceResolverProvider = () => new IdReferenceResolver()
            };
        }

        private JsonSerializerSettings GetDeserializerSettings()
        {
            return new JsonSerializerSettings
            {
                Error = (sender, args) =>
                {
                    args.ErrorContext.Handled = true;
                    logger?.Log(args.ErrorContext.Error.Message);
                },
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                TypeNameHandling = TypeNameHandling.None,
                Formatting = Formatting.Indented,
                Culture = CultureInfo.InvariantCulture
            };
        }
    }

    /// <summary>
    /// Result of clipboard validation
    /// </summary>
    public class ClipboardValidationResult
    {
        /// <summary>
        /// Whether the clipboard data is valid for pasting
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Critical errors that prevent pasting
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// Non-critical warnings about the paste operation
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
