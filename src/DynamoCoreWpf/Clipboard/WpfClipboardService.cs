using System;
using System.Windows;
using Dynamo.Core.Clipboard;
using Dynamo.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Dynamo.Wpf.Clipboard
{
    /// <summary>
    /// WPF implementation of the clipboard service that uses the Windows system clipboard.
    /// </summary>
    public class WpfClipboardService : IClipboardService
    {
        /// <summary>
        /// Custom clipboard format name for Dynamo data
        /// </summary>
        public const string DynamoClipboardFormatName = "DynamoClipboardData";

        private readonly ILogger logger;
        private readonly ClipboardSerializer serializer;

        /// <summary>
        /// Creates a new WpfClipboardService
        /// </summary>
        /// <param name="serializer">The serializer to use for clipboard data</param>
        /// <param name="logger">Logger for error reporting</param>
        public WpfClipboardService(ClipboardSerializer serializer, ILogger logger)
        {
            this.serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            this.logger = logger;
        }

        /// <inheritdoc/>
        public void CopyToSystemClipboard(ClipboardData clipboardData)
        {
            if (clipboardData == null)
            {
                logger?.Log("CopyToSystemClipboard: clipboardData is null");
                return;
            }

            try
            {
                logger?.Log($"CopyToSystemClipboard: Starting copy of {clipboardData.Nodes?.Count ?? 0} nodes");
                
                var json = serializer.SerializeToJson(clipboardData);
                
                if (string.IsNullOrEmpty(json))
                {
                    logger?.Log("CopyToSystemClipboard: Serialization returned empty JSON");
                    return;
                }
                
                logger?.Log($"CopyToSystemClipboard: Serialized to {json.Length} chars");
                
                // Use SetText directly for maximum compatibility across applications
                System.Windows.Clipboard.SetText(json);
                
                // Verify it was set correctly
                var verifyText = System.Windows.Clipboard.GetText();
                logger?.Log($"CopyToSystemClipboard: Verification - clipboard now has {verifyText?.Length ?? 0} chars");
                
                if (string.IsNullOrEmpty(verifyText))
                {
                    logger?.LogWarning("CopyToSystemClipboard: WARNING - Clipboard appears empty after SetText!", WarningLevel.Moderate);
                }
                else if (verifyText.Length != json.Length)
                {
                    logger?.LogWarning($"CopyToSystemClipboard: WARNING - Length mismatch! Set {json.Length}, got back {verifyText.Length}", WarningLevel.Moderate);
                }
                else
                {
                    logger?.Log($"CopyToSystemClipboard: SUCCESS - Copied {clipboardData.Nodes.Count} nodes to system clipboard");
                }
            }
            catch (Exception ex)
            {
                logger?.LogError($"CopyToSystemClipboard: FAILED - {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public ClipboardData GetFromSystemClipboard()
        {
            try
            {
                var json = GetRawClipboardJson();
                if (string.IsNullOrEmpty(json))
                    return null;

                return serializer.DeserializeFromJson(json);
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"Failed to get clipboard data: {ex.Message}", WarningLevel.Moderate);
                return null;
            }
        }

        /// <inheritdoc/>
        public bool HasDynamoClipboardData()
        {
            try
            {
                var containsText = System.Windows.Clipboard.ContainsText();
                logger?.Log($"HasDynamoClipboardData: ContainsText={containsText}");
                
                // Check text content for Dynamo format (most reliable cross-application)
                if (containsText)
                {
                    var text = System.Windows.Clipboard.GetText();
                    logger?.Log($"HasDynamoClipboardData: GetText returned {text?.Length ?? 0} chars");
                    
                    if (string.IsNullOrEmpty(text))
                    {
                        logger?.Log("HasDynamoClipboardData: Text is empty despite ContainsText=true");
                        return false;
                    }
                    
                    var isDynamo = IsDynamoClipboardJson(text);
                    logger?.Log($"HasDynamoClipboardData: IsDynamoJson={isDynamo}");
                    return isDynamo;
                }

                return false;
            }
            catch (Exception ex)
            {
                // Clipboard access can fail if another application has it locked
                logger?.Log($"HasDynamoClipboardData: Exception - {ex.Message}");
                return false;
            }
        }

        /// <inheritdoc/>
        public string GetRawClipboardJson()
        {
            try
            {
                // Get text from clipboard
                if (System.Windows.Clipboard.ContainsText())
                {
                    var text = System.Windows.Clipboard.GetText();
                    if (IsDynamoClipboardJson(text))
                    {
                        return text;
                    }
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Checks if the given text is valid Dynamo clipboard JSON
        /// </summary>
        private bool IsDynamoClipboardJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            // Quick check before attempting JSON parse - must start with { and contain our format ID
            var formatId = ClipboardData.ClipboardFormatId;
            var trimmedText = text.TrimStart();
            
            if (!trimmedText.StartsWith("{") || !text.Contains(formatId))
            {
                return false;
            }

            try
            {
                var obj = JObject.Parse(text);
                var hasFormat = obj[formatId] != null;
                return hasFormat;
            }
            catch (JsonException)
            {
                return false;
            }
            catch (Exception)
            {
                // Catch any other parsing exceptions
                return false;
            }
        }
    }
}

