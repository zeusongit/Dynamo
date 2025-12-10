using System.Collections.Generic;
using Dynamo.Graph;

namespace Dynamo.Core.Clipboard
{
    /// <summary>
    /// Interface for clipboard operations that can work across Dynamo instances.
    /// This abstraction allows the core to define clipboard operations while
    /// platform-specific implementations handle the actual system clipboard interaction.
    /// </summary>
    public interface IClipboardService
    {
        /// <summary>
        /// Copies the provided items to the system clipboard in a format
        /// that can be pasted in other Dynamo instances.
        /// </summary>
        /// <param name="clipboardData">The serialized clipboard data</param>
        void CopyToSystemClipboard(ClipboardData clipboardData);

        /// <summary>
        /// Retrieves clipboard data from the system clipboard if it contains
        /// valid Dynamo clipboard data.
        /// </summary>
        /// <returns>The clipboard data, or null if clipboard doesn't contain Dynamo data</returns>
        ClipboardData GetFromSystemClipboard();

        /// <summary>
        /// Checks if the system clipboard contains valid Dynamo clipboard data.
        /// </summary>
        /// <returns>True if the clipboard contains Dynamo data</returns>
        bool HasDynamoClipboardData();

        /// <summary>
        /// Gets the raw JSON string from the system clipboard if it contains Dynamo data.
        /// Used for validation and debugging purposes.
        /// </summary>
        /// <returns>The raw JSON string, or null if not available</returns>
        string GetRawClipboardJson();
    }

    /// <summary>
    /// Null implementation of clipboard service for environments without system clipboard access.
    /// </summary>
    public class NullClipboardService : IClipboardService
    {
        /// <inheritdoc/>
        public void CopyToSystemClipboard(ClipboardData clipboardData)
        {
            // No-op in null implementation
        }

        /// <inheritdoc/>
        public ClipboardData GetFromSystemClipboard()
        {
            return null;
        }

        /// <inheritdoc/>
        public bool HasDynamoClipboardData()
        {
            return false;
        }

        /// <inheritdoc/>
        public string GetRawClipboardJson()
        {
            return null;
        }
    }
}

