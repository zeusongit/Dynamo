using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dynamo.Core;
using Dynamo.Engine;
using Dynamo.Graph;
using Dynamo.Graph.Annotations;
using Dynamo.Graph.Connectors;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Nodes.CustomNodes;
using Dynamo.Graph.Nodes.NodeLoaders;
using Dynamo.Graph.Notes;
using Dynamo.Graph.Workspaces;
using Dynamo.Library;
using Dynamo.Logging;
using Dynamo.Selection;
using Dynamo.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Dynamo.Core.Clipboard
{
    /// <summary>
    /// Handles pasting clipboard data into a workspace with lazy connector resolution.
    /// </summary>
    public class ClipboardPasteHandler
    {
        private readonly NodeFactory nodeFactory;
        private readonly LibraryServices libraryServices;
        private readonly CustomNodeManager customNodeManager;
        private readonly EngineController engineController;
        private readonly ILogger logger;
        private readonly bool isTestMode;

        // Lazy connector resolution - maps old GUIDs to new models
        private readonly Dictionary<Guid, ModelBase> guidMapping = new Dictionary<Guid, ModelBase>();
        
        // Pending connectors to be created after all nodes exist
        private readonly List<PendingConnector> pendingConnectors = new List<PendingConnector>();

        /// <summary>
        /// Creates a new ClipboardPasteHandler
        /// </summary>
        public ClipboardPasteHandler(
            NodeFactory nodeFactory,
            LibraryServices libraryServices,
            CustomNodeManager customNodeManager,
            EngineController engineController,
            ILogger logger,
            bool isTestMode)
        {
            this.nodeFactory = nodeFactory ?? throw new ArgumentNullException(nameof(nodeFactory));
            this.libraryServices = libraryServices ?? throw new ArgumentNullException(nameof(libraryServices));
            this.customNodeManager = customNodeManager ?? throw new ArgumentNullException(nameof(customNodeManager));
            this.engineController = engineController;
            this.logger = logger;
            this.isTestMode = isTestMode;
        }

        /// <summary>
        /// Pastes clipboard data into the specified workspace at the target point.
        /// </summary>
        /// <param name="clipboardData">The clipboard data to paste</param>
        /// <param name="workspace">The target workspace</param>
        /// <param name="targetPoint">The target position for pasting</param>
        /// <param name="useOffset">Whether to use paste offset</param>
        /// <returns>Result of the paste operation</returns>
        public ClipboardPasteResult Paste(
            ClipboardData clipboardData,
            WorkspaceModel workspace,
            Point2D targetPoint,
            bool useOffset = true)
        {
            var result = new ClipboardPasteResult { Success = true };
            
            guidMapping.Clear();
            pendingConnectors.Clear();

            if (useOffset)
            {
                workspace.IncrementPasteOffset();
            }

            DynamoSelection.Instance.ClearSelection();

            var createdModels = new List<ModelBase>();

            try
            {
                // Phase 1: Create nodes
                var newNodes = CreateNodesFromClipboard(clipboardData, workspace, result);
                createdModels.AddRange(newNodes);

                // Phase 2: Create notes
                var newNotes = CreateNotesFromClipboard(clipboardData);
                createdModels.AddRange(newNotes);

                // Calculate position offset
                var locatableModels = newNodes.Cast<ModelBase>().Concat(newNotes);
                double shiftX = 0, shiftY = 0;
                var offset = useOffset ? workspace.CurrentPasteOffset : 0;
                
                if (locatableModels.Any())
                {
                    shiftX = targetPoint.X - locatableModels.Min(m => m.X);
                    shiftY = targetPoint.Y - locatableModels.Min(m => m.Y);

                    foreach (var model in locatableModels)
                    {
                        model.X = model.X + shiftX + offset;
                        model.Y = model.Y + shiftY + offset;
                    }
                }

                // Phase 3: Add nodes to workspace
                using (workspace.BeginDelayedGraphExecution())
                {
                    foreach (var node in newNodes)
                    {
                        workspace.AddAndRegisterNode(node, false);
                    }

                    foreach (var note in newNotes)
                    {
                        workspace.AddNote(note, false);
                    }

                    // Phase 4: Lazy connector resolution - create connectors now that all nodes exist
                    var (newConnectors, connectorMapping) = ResolveConnectors(clipboardData, workspace);
                    createdModels.AddRange(newConnectors);

                    // Phase 5: Create connector pins
                    var newPins = CreateConnectorPinsFromClipboard(clipboardData, connectorMapping, shiftX, shiftY, offset);
                    createdModels.AddRange(newPins);

                    // Phase 6: Create annotations
                    var newAnnotations = CreateAnnotationsFromClipboard(clipboardData, workspace, newNodes, newNotes);
                    foreach (var annotation in newAnnotations)
                    {
                        workspace.AddAnnotation(annotation);
                        createdModels.Add(annotation);
                    }
                }

                // Select all created models
                DynamoSelection.Instance.ClearSelectionDisabled = true;
                foreach (var model in createdModels.Where(m => m is ISelectable))
                {
                    DynamoSelection.Instance.Selection.AddUnique(model as ISelectable);
                }
                DynamoSelection.Instance.ClearSelectionDisabled = false;

                // Record for undo
                workspace.RecordCreatedModels(createdModels);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                logger?.LogError($"Failed to paste from clipboard: {ex.Message}");
            }

            return result;
        }

        private List<NodeModel> CreateNodesFromClipboard(
            ClipboardData clipboardData, 
            WorkspaceModel workspace,
            ClipboardPasteResult result)
        {
            var newNodes = new List<NodeModel>();
            var settings = GetDeserializerSettings(workspace);

            // Build a lookup for node view data from NodeViews (handle duplicates by taking first)
            // Normalize GUIDs to format without dashes for consistent lookup
            var nodeViewLookup = new Dictionary<string, ClipboardNodeViewData>(StringComparer.OrdinalIgnoreCase);
            foreach (var viewData in clipboardData.NodeViews.Where(v => !string.IsNullOrEmpty(v.Id)))
            {
                if (Guid.TryParse(viewData.Id, out var guid))
                {
                    var normalizedId = guid.ToString("N");
                    if (!nodeViewLookup.ContainsKey(normalizedId))
                    {
                        nodeViewLookup[normalizedId] = viewData;
                    }
                }
            }
            
            // Build a lookup for node dependencies from clipboard data
            // Normalize GUIDs to format without dashes for consistent lookup
            var nodeDependencies = new Dictionary<string, ClipboardDependencyData>(StringComparer.OrdinalIgnoreCase);
            foreach (var dep in clipboardData.Dependencies)
            {
                foreach (var nodeId in dep.Nodes)
                {
                    // Store with normalized GUID (no dashes, lowercase)
                    if (Guid.TryParse(nodeId, out var guid))
                    {
                        nodeDependencies[guid.ToString("N")] = dep;
                    }
                }
            }

            foreach (var nodeData in clipboardData.Nodes)
            {
                try
                {
                    var nodeJson = nodeData as JObject;
                    if (nodeJson == null)
                    {
                        nodeJson = JObject.FromObject(nodeData);
                    }

                    // Generate new GUID for the pasted node
                    var originalGuidStr = nodeJson["Id"].Value<string>();
                    var originalGuid = Guid.Parse(originalGuidStr);
                    var newGuid = Guid.NewGuid();
                    nodeJson["Id"] = newGuid.ToString();

                    // Get position from JSON or NodeViews
                    var posX = nodeJson["X"]?.Value<double>() ?? 0;
                    var posY = nodeJson["Y"]?.Value<double>() ?? 0;
                    
                    // Get view data for this node (for frozen/preview state)
                    // Use normalized GUID for lookup (no dashes)
                    ClipboardNodeViewData nodeViewData = null;
                    var normalizedOriginalGuid = originalGuid.ToString("N");
                    if (nodeViewLookup.TryGetValue(normalizedOriginalGuid, out var viewData))
                    {
                        nodeViewData = viewData;
                        // Use view data positions as fallback
                        if (posX == 0 && posY == 0)
                        {
                            posX = viewData.X;
                            posY = viewData.Y;
                        }
                    }

                    var jsonString = nodeJson.ToString();
                    var processedJson = SerializationExtensions.ReplaceTypeDeclarations(jsonString, fromServer: true);
                    processedJson = SerializationExtensions.DeserializeIntegerSliderTo64BitType(processedJson);

                    var node = JsonConvert.DeserializeObject<NodeModel>(processedJson, settings);
                    
                    if (node != null)
                    {
                        node.GUID = newGuid;
                        
                        // Explicitly set position (X, Y might not deserialize due to ShouldSerializeX/Y returning false)
                        node.X = posX;
                        node.Y = posY;
                        
                        // Apply node view state (frozen, preview, input/output)
                        if (nodeViewData != null)
                        {
                            node.IsFrozen = nodeViewData.Excluded;
                            // Use UpdateValue for IsVisible (ShowGeometry) as it has a private setter
                            node.UpdateValue(new UpdateValueParams("IsVisible", nodeViewData.ShowGeometry.ToString()));
                            node.IsSetAsInput = nodeViewData.IsSetAsInput;
                            node.IsSetAsOutput = nodeViewData.IsSetAsOutput;
                        }
                        
                        guidMapping[originalGuid] = node;
                        newNodes.Add(node);

                        // Check if this is a DummyNode or a Function with missing definition
                        bool isMissingDependency = false;
                        
                        if (node is DummyNode)
                        {
                            isMissingDependency = true;
                            result.DummyNodeIds.Add(newGuid.ToString());
                        }
                        else if (node is Function functionNode)
                        {
                            // Check if custom node definition is missing or is a proxy
                            if (functionNode.Definition == null || functionNode.Definition.IsProxy)
                            {
                                isMissingDependency = true;
                                result.DummyNodeIds.Add(newGuid.ToString());
                            }
                        }
                        
                        if (isMissingDependency)
                        {
                            // Extract package info from node JSON if available
                            var packageName = nodeJson["PackageName"]?.Value<string>();
                            var packageVersion = nodeJson["PackageVersion"]?.Value<string>();
                            
                            // Also check the clipboard Dependencies list using original GUID (normalized)
                            if (string.IsNullOrEmpty(packageName) && nodeDependencies.TryGetValue(normalizedOriginalGuid, out var depData))
                            {
                                packageName = depData.Name;
                                packageVersion = depData.Version;
                            }
                            
                            if (!string.IsNullOrEmpty(packageName))
                            {
                                // Check if we already have this dependency
                                var existingDep = result.MissingDependencies
                                    .FirstOrDefault(d => d.Name == packageName && d.Version == packageVersion);
                                
                                if (existingDep == null)
                                {
                                    existingDep = new ClipboardDependencyData
                                    {
                                        Name = packageName,
                                        Version = packageVersion,
                                        ReferenceType = "Package"
                                    };
                                    result.MissingDependencies.Add(existingDep);
                                }
                                existingDep.Nodes.Add(newGuid.ToString());
                            }
                            else
                            {
                                // If no package info, still report as unknown dependency
                                var nodeName = nodeJson["Name"]?.Value<string>() ?? "Unknown";
                                var existingDep = result.MissingDependencies
                                    .FirstOrDefault(d => d.Name == nodeName && d.ReferenceType == "CustomNode");
                                
                                if (existingDep == null)
                                {
                                    existingDep = new ClipboardDependencyData
                                    {
                                        Name = nodeName,
                                        ReferenceType = "CustomNode"
                                    };
                                    result.MissingDependencies.Add(existingDep);
                                }
                                existingDep.Nodes.Add(newGuid.ToString());
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning($"Failed to deserialize node from clipboard: {ex.Message}", 
                        WarningLevel.Moderate);
                }
            }

            return newNodes;
        }

        private List<NoteModel> CreateNotesFromClipboard(ClipboardData clipboardData)
        {
            var newNotes = new List<NoteModel>();

            foreach (var noteData in clipboardData.Notes)
            {
                var originalGuid = Guid.Parse(noteData.Id);
                var newGuid = Guid.NewGuid();

                NodeModel pinnedNode = null;
                if (!string.IsNullOrEmpty(noteData.PinnedNodeId) && 
                    Guid.TryParse(noteData.PinnedNodeId, out var pinnedGuid))
                {
                    if (guidMapping.TryGetValue(pinnedGuid, out var pinnedModel))
                    {
                        pinnedNode = pinnedModel as NodeModel;
                    }
                }

                var note = new NoteModel(noteData.X, noteData.Y, noteData.Text, newGuid, pinnedNode);
                guidMapping[originalGuid] = note;
                newNotes.Add(note);
            }

            return newNotes;
        }

        private (List<ConnectorModel> connectors, Dictionary<string, ConnectorModel> connectorMapping) ResolveConnectors(ClipboardData clipboardData, WorkspaceModel workspace)
        {
            var newConnectors = new List<ConnectorModel>();
            var connectorMapping = new Dictionary<string, ConnectorModel>(StringComparer.OrdinalIgnoreCase);

            foreach (var connectorData in clipboardData.Connectors)
            {
                try
                {
                    var connectorJson = connectorData as JObject;
                    if (connectorJson == null)
                    {
                        connectorJson = JObject.FromObject(connectorData);
                    }

                    var originalConnectorId = connectorJson["Id"]?.Value<string>() ?? "";
                    var startPortId = connectorJson["Start"].Value<string>();
                    var endPortId = connectorJson["End"].Value<string>();

                    // Find the start and end nodes/ports
                    var startPort = FindPort(startPortId, workspace, isOutput: true);
                    var endPort = FindPort(endPortId, workspace, isOutput: false);

                    if (startPort != null && endPort != null)
                    {
                        var connector = ConnectorModel.Make(
                            startPort.Owner, 
                            endPort.Owner, 
                            startPort.Index, 
                            endPort.Index);
                        
                        if (connector != null)
                        {
                            newConnectors.Add(connector);
                            // Map original connector ID to new connector
                            if (!string.IsNullOrEmpty(originalConnectorId))
                            {
                                connectorMapping[originalConnectorId] = connector;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning($"Failed to create connector: {ex.Message}", WarningLevel.Moderate);
                }
            }

            return (newConnectors, connectorMapping);
        }

        private PortModel FindPort(string portGuid, WorkspaceModel workspace, bool isOutput)
        {
            if (!Guid.TryParse(portGuid, out var guid))
                return null;

            // First, check in newly created nodes
            foreach (var kvp in guidMapping)
            {
                if (kvp.Value is NodeModel node)
                {
                    var ports = isOutput ? node.OutPorts : node.InPorts;
                    foreach (var port in ports)
                    {
                        if (port.GUID == guid)
                            return port;
                    }
                }
            }

            // Fall back to existing workspace nodes
            foreach (var node in workspace.Nodes)
            {
                var ports = isOutput ? node.OutPorts : node.InPorts;
                foreach (var port in ports)
                {
                    if (port.GUID == guid)
                        return port;
                }
            }

            return null;
        }

        private List<AnnotationModel> CreateAnnotationsFromClipboard(
            ClipboardData clipboardData,
            WorkspaceModel workspace,
            List<NodeModel> newNodes,
            List<NoteModel> newNotes)
        {
            var newAnnotations = new List<AnnotationModel>();

            // Sort annotations by nested groups first
            var sortedAnnotations = clipboardData.Annotations
                .OrderByDescending(a => a.HasNestedGroups)
                .ToList();

            foreach (var annotationData in sortedAnnotations)
            {
                if (guidMapping.ContainsKey(Guid.Parse(annotationData.Id)))
                    continue;

                var annotationNodes = new List<NodeModel>();
                var annotationNotes = new List<NoteModel>();
                var nestedAnnotations = new List<AnnotationModel>();

                foreach (var nodeIdStr in annotationData.Nodes)
                {
                    if (Guid.TryParse(nodeIdStr, out var nodeId) && guidMapping.TryGetValue(nodeId, out var model))
                    {
                        if (model is NodeModel nodeModel)
                            annotationNodes.Add(nodeModel);
                        else if (model is NoteModel noteModel)
                            annotationNotes.Add(noteModel);
                        else if (model is AnnotationModel nestedAnnotation)
                            nestedAnnotations.Add(nestedAnnotation);
                    }
                }

                var annotation = new AnnotationModel(annotationNodes, annotationNotes, nestedAnnotations)
                {
                    GUID = Guid.NewGuid(),
                    AnnotationText = annotationData.Title,
                    AnnotationDescriptionText = annotationData.DescriptionText,
                    FontSize = annotationData.FontSize,
                    GroupStyleId = annotationData.GroupStyleId,
                    Background = annotationData.Background,
                    WidthAdjustment = annotationData.WidthAdjustment,
                    HeightAdjustment = annotationData.HeightAdjustment,
                    UserSetWidth = annotationData.UserSetWidth,
                    UserSetHeight = annotationData.UserSetHeight,
                    IsOptionalInPortsCollapsed = annotationData.IsOptionalInPortsCollapsed,
                    IsUnconnectedOutPortsCollapsed = annotationData.IsUnconnectedOutPortsCollapsed
                };

                guidMapping[Guid.Parse(annotationData.Id)] = annotation;
                newAnnotations.Add(annotation);
            }

            return newAnnotations;
        }

        private List<ConnectorPinModel> CreateConnectorPinsFromClipboard(
            ClipboardData clipboardData,
            Dictionary<string, ConnectorModel> connectorMapping,
            double shiftX,
            double shiftY,
            double offset)
        {
            var newPins = new List<ConnectorPinModel>();
            
            if (clipboardData.ConnectorPins == null || !clipboardData.ConnectorPins.Any())
                return newPins;

            foreach (var pinData in clipboardData.ConnectorPins)
            {
                try
                {
                    // Find the connector this pin belongs to using the original connector ID
                    var normalizedConnectorId = pinData.ConnectorId?.Replace("-", "") ?? "";
                    if (!connectorMapping.TryGetValue(normalizedConnectorId, out var connector))
                    {
                        logger?.LogWarning($"Could not find connector for pin: {pinData.ConnectorId}", WarningLevel.Moderate);
                        continue;
                    }

                    // Create the pin with adjusted position
                    var newGuid = Guid.NewGuid();
                    var newX = pinData.X + shiftX + offset;
                    var newY = pinData.Y + shiftY + offset;
                    
                    var pin = new ConnectorPinModel(newX, newY, newGuid, connector.GUID);
                    
                    // Add pin to the connector
                    connector.AddPin(pin);
                    newPins.Add(pin);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning($"Failed to create connector pin: {ex.Message}", WarningLevel.Moderate);
                }
            }

            return newPins;
        }

        private JsonSerializerSettings GetDeserializerSettings(WorkspaceModel workspace)
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
                Culture = CultureInfo.InvariantCulture,
                Converters = new List<JsonConverter>
                {
                    new NodeReadConverter(customNodeManager, libraryServices, nodeFactory, isTestMode)
                    {
                        ElementResolver = workspace.ElementResolver
                    },
                    new TypedParameterConverter()
                },
                ReferenceResolverProvider = () => new IdReferenceResolver()
            };
        }
    }

    /// <summary>
    /// Represents a connector that needs to be created after nodes are resolved
    /// </summary>
    internal class PendingConnector
    {
        public Guid StartNodeGuid { get; set; }
        public int StartPortIndex { get; set; }
        public Guid EndNodeGuid { get; set; }
        public int EndPortIndex { get; set; }
    }
}

