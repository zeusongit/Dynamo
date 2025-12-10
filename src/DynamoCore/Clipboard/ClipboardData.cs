using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Dynamo.Core.Clipboard
{
    /// <summary>
    /// Represents the data format used for cross-instance clipboard operations.
    /// This format is compatible with the .dyn file format for consistency.
    /// </summary>
    public class ClipboardData
    {
        /// <summary>
        /// Magic identifier to verify this is Dynamo clipboard data
        /// </summary>
        public const string ClipboardFormatId = "DynamoClipboard";

        /// <summary>
        /// Version of the clipboard format
        /// </summary>
        [JsonProperty("Version")]
        public string Version { get; set; }

        /// <summary>
        /// Timestamp when the copy operation occurred
        /// </summary>
        [JsonProperty("Timestamp")]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Identifier of the source Dynamo instance
        /// </summary>
        [JsonProperty("SourceInstanceId")]
        public string SourceInstanceId { get; set; }

        /// <summary>
        /// The serialized nodes
        /// </summary>
        [JsonProperty("Nodes")]
        public List<object> Nodes { get; set; } = new List<object>();

        /// <summary>
        /// The serialized connectors
        /// </summary>
        [JsonProperty("Connectors")]
        public List<object> Connectors { get; set; } = new List<object>();

        /// <summary>
        /// The serialized notes
        /// </summary>
        [JsonProperty("Notes")]
        public List<ClipboardNoteData> Notes { get; set; } = new List<ClipboardNoteData>();

        /// <summary>
        /// The serialized annotations (groups)
        /// </summary>
        [JsonProperty("Annotations")]
        public List<ClipboardAnnotationData> Annotations { get; set; } = new List<ClipboardAnnotationData>();

        /// <summary>
        /// View-specific data for nodes
        /// </summary>
        [JsonProperty("NodeViews")]
        public List<ClipboardNodeViewData> NodeViews { get; set; } = new List<ClipboardNodeViewData>();

        /// <summary>
        /// Connector pins (wire pins)
        /// </summary>
        [JsonProperty("ConnectorPins")]
        public List<ClipboardConnectorPinData> ConnectorPins { get; set; } = new List<ClipboardConnectorPinData>();

        /// <summary>
        /// Package dependencies for the copied nodes
        /// </summary>
        [JsonProperty("Dependencies")]
        public List<ClipboardDependencyData> Dependencies { get; set; } = new List<ClipboardDependencyData>();

        /// <summary>
        /// Element resolver information for name resolution
        /// </summary>
        [JsonProperty("ElementResolver")]
        public object ElementResolver { get; set; }
    }

    /// <summary>
    /// View data for a node in the clipboard
    /// </summary>
    public class ClipboardNodeViewData
    {
        [JsonProperty("Id")]
        public string Id { get; set; }

        [JsonProperty("Name")]
        public string Name { get; set; }

        [JsonProperty("X")]
        public double X { get; set; }

        [JsonProperty("Y")]
        public double Y { get; set; }

        [JsonProperty("ShowGeometry")]
        public bool ShowGeometry { get; set; } = true;

        [JsonProperty("Excluded")]
        public bool Excluded { get; set; }

        [JsonProperty("IsSetAsInput")]
        public bool IsSetAsInput { get; set; }

        [JsonProperty("IsSetAsOutput")]
        public bool IsSetAsOutput { get; set; }
    }

    /// <summary>
    /// Note data for the clipboard
    /// </summary>
    public class ClipboardNoteData
    {
        [JsonProperty("Id")]
        public string Id { get; set; }

        [JsonProperty("X")]
        public double X { get; set; }

        [JsonProperty("Y")]
        public double Y { get; set; }

        [JsonProperty("Text")]
        public string Text { get; set; }

        [JsonProperty("PinnedNodeId")]
        public string PinnedNodeId { get; set; }
    }

    /// <summary>
    /// Connector pin data for the clipboard
    /// </summary>
    public class ClipboardConnectorPinData
    {
        [JsonProperty("Id")]
        public string Id { get; set; }

        [JsonProperty("X")]
        public double X { get; set; }

        [JsonProperty("Y")]
        public double Y { get; set; }

        [JsonProperty("ConnectorId")]
        public string ConnectorId { get; set; }
    }

    /// <summary>
    /// Annotation (group) data for the clipboard
    /// </summary>
    public class ClipboardAnnotationData
    {
        [JsonProperty("Id")]
        public string Id { get; set; }

        [JsonProperty("Title")]
        public string Title { get; set; }

        [JsonProperty("DescriptionText")]
        public string DescriptionText { get; set; }

        [JsonProperty("IsExpanded")]
        public bool IsExpanded { get; set; } = true;

        [JsonProperty("Nodes")]
        public List<string> Nodes { get; set; } = new List<string>();

        [JsonProperty("HasNestedGroups")]
        public bool HasNestedGroups { get; set; }

        [JsonProperty("FontSize")]
        public double FontSize { get; set; }

        [JsonProperty("GroupStyleId")]
        public Guid GroupStyleId { get; set; }

        [JsonProperty("Background")]
        public string Background { get; set; }

        [JsonProperty("WidthAdjustment")]
        public double WidthAdjustment { get; set; }

        [JsonProperty("HeightAdjustment")]
        public double HeightAdjustment { get; set; }

        [JsonProperty("UserSetWidth")]
        public double UserSetWidth { get; set; }

        [JsonProperty("UserSetHeight")]
        public double UserSetHeight { get; set; }

        [JsonProperty("IsOptionalInPortsCollapsed")]
        public bool IsOptionalInPortsCollapsed { get; set; }

        [JsonProperty("IsUnconnectedOutPortsCollapsed")]
        public bool IsUnconnectedOutPortsCollapsed { get; set; }
    }

    /// <summary>
    /// Dependency information for clipboard data
    /// </summary>
    public class ClipboardDependencyData
    {
        [JsonProperty("Name")]
        public string Name { get; set; }

        [JsonProperty("Version")]
        public string Version { get; set; }

        [JsonProperty("ReferenceType")]
        public string ReferenceType { get; set; }

        [JsonProperty("Nodes")]
        public List<string> Nodes { get; set; } = new List<string>();
    }

    /// <summary>
    /// Result of clipboard paste operation
    /// </summary>
    public class ClipboardPasteResult
    {
        /// <summary>
        /// Whether the paste was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// List of missing dependencies that couldn't be resolved
        /// </summary>
        public List<ClipboardDependencyData> MissingDependencies { get; set; } = new List<ClipboardDependencyData>();

        /// <summary>
        /// Nodes that were created as DummyNodes due to missing dependencies
        /// </summary>
        public List<string> DummyNodeIds { get; set; } = new List<string>();

        /// <summary>
        /// Error message if paste failed
        /// </summary>
        public string ErrorMessage { get; set; }
    }
}

