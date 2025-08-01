using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;
using Autodesk.DesignScript.Runtime;
using Dynamo.Core;
using Dynamo.Graph.Connectors;
using Dynamo.Interfaces;
using Dynamo.Logging;
using Dynamo.Models;
using Dynamo.Utilities;
using DynamoUtilities;

namespace Dynamo.Configuration
{
    static class ExtensionMethods
    {
        /// <summary>
        /// Copy Properties from a PreferenceSettings instance to another iterating the Properties of the destination instance and populate them from their source counterparts, excluding the properties that are obsolete and only read.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        internal static void CopyProperties(this PreferenceSettings source, PreferenceSettings destination)
        {
            var destinationProperties = destination.GetType().GetProperties();

            foreach (var destinationPi in destinationProperties)
            {
                var sourcePi = source.GetType().GetProperty(destinationPi.Name);

                if (destinationPi.GetCustomAttributes(typeof(System.ObsoleteAttribute), true).Length == 0 && destinationPi.CanWrite)
                {
                    destinationPi.SetValue(destination, sourcePi.GetValue(source, null), null);
                }
            }
        }
    }

    /// <summary>
    /// PreferenceSettings is a class for GUI to persist certain settings.
    /// Upon running of the GUI, those settings that are persistent will be loaded
    /// from a XML file from DYNAMO_SETTINGS_FILE.
    /// When GUI is closed, the settings are saved back into the XML file.
    /// </summary>
    public class PreferenceSettings : NotificationObject, IPreferences, IRenderPrecisionPreference, IDisablePackageLoadingPreferences, ILogSource, IHideAutocompleteMethodOptions
    {
        private readonly static Lazy<PreferenceSettings>
            lazy = new Lazy<PreferenceSettings>
            (() => PreferenceSettings.Load(PathManager.Instance.PreferenceFilePath));

        /// <summary>
        /// Return a PreferenceSetting object.  The object returned is based on the following conditions:
        /// 1) if DynamoModel present, the DynamoModel.PreferenceSettings object is returned,
        /// 2) else, if a valid setting xml file exists, the PreferenceSettings object de-serialized from the xml file is returned,
        /// 3) else, if no DynamoModel and no valid xml file exists, a new PreferenceSettings object returned
        /// Note that Instance is a runtime object only.  No changes to the PreferenceSettings will be persisted on disk with condition 2 or 3.
        /// User of Instance must initiate save operations to insure persistence of modifications to the PreferenceSettings model.
        /// In some cases even the save will not guarantee persistence of modifications depending on the startup of DynamoModel.
        /// </summary>
        [XmlIgnore]
        internal static PreferenceSettings Instance { get; set; } = lazy.Value;

        private string numberFormat;
        private string lastUpdateDownloadPath;
        private int maxNumRecentFiles;
        private bool isBackgroundGridVisible;
        private float gridScaleFactor;
        private Configurations.Units currentHostUnits;
        private double defaultScaleFactor;
        private bool disableTrustWarnings = false;
        private bool isNotificationCenterEnabled;
        private bool isEnablePersistExtensionsEnabled;
        private bool isStaticSplashScreenEnabled;
        private bool isTimeStampIncludedInExportFilePath;
        private bool isCreatedFromValidFile = true;
        private string backupLocation;
        private string templateFilePath;
        private bool isMLAutocompleteTOUApproved;

        #region Constants
        /// <summary>
        /// Indicates the maximum number of files shown in Recent Files
        /// </summary>
        internal const int DefaultMaxNumRecentFiles = 10;

        /// <summary>
        /// The default time interval between backup files. 5 minutes.
        /// </summary>
        internal const int DefaultBackupInterval = 300000;

        /// <summary>
        /// The old time interval between backup files. 1 minute.
        /// </summary>
        private const int OldDefaultBackupInterval = 60000;

        /// <summary>
        /// Indicates the default render precision, i.e. the maximum number of tessellation divisions
        /// </summary>
        internal const int DefaultRenderPrecision = 128;

        /// <summary>
        /// Temp PreferenceSetting Location for testing
        /// </summary>
        public static string DynamoTestPath = null;

        /// <summary>
        /// Default date format
        /// </summary>
        public const string DefaultDateFormat = "MMMM dd, yyyy h:mm tt";

        /// <summary>
        /// Default time
        /// </summary>
        public static readonly DateTime DynamoDefaultTime = new DateTime(1977, 4, 12, 12, 12, 0, 0);

        internal static readonly IEnumerable<string> InitialExperimentalLib_Namespaces =
        [
            "ProtoGeometry.dll:Autodesk.DesignScript.Geometry.PanelSurface"
            ];
        #endregion

        // The following settings are persistent between Dynamo sessions and are user-controllable
        #region Collect Information settings

        /// <summary>
        /// Indicates first run
        /// </summary>
        public bool IsFirstRun { get; set; }

        /// <summary>
        /// This defines if the user export file path would include timestamp
        /// </summary>
        public bool IsTimeStampIncludedInExportFilePath
        {
            get
            {
                return isTimeStampIncludedInExportFilePath;
            }
            set
            {
                isTimeStampIncludedInExportFilePath = value;
            }
        }

        /// <summary>
        /// Indicates whether ADP analytics reporting is approved or not.
        /// Note that the getter will communicate to a analytics server which might be slow.
        /// This API should only be used in UI scenarios (not in performance sensitive areas)
        /// </summary>
        [XmlIgnore]
        [Obsolete("API obsolete - This is an internal API and should not be used.")]
        public bool IsADPAnalyticsReportingApproved
        {
            get
            {
                return AnalyticsService.IsADPOptedIn;
            }
            set { throw new Exception("do not use"); }
        }
        #endregion

        #region UI & Graphics settings

        /// <summary>
        /// The width of the library pane.
        /// </summary>
        public int LibraryWidth { get; set; }

        /// <summary>
        /// The locale of Dynamo UI, serialize locale instead of language name as ease of conversion back and forth
        /// </summary>
        public string Locale { get; set; }

        /// <summary>
        /// Contains the currently selected unit used for scaling the graphic helpers (grids, axes)
        /// </summary>
        public string GraphicScaleUnit { get; set; }

        /// <summary>
        /// The height of the console display.
        /// </summary>
        public int ConsoleHeight { get; set; }

        /// <summary>
        /// Indicates if preview bubbles should be displayed on nodes.
        /// </summary>
        public bool ShowPreviewBubbles { get; set; }

        /// <summary>
        /// Indicates if groups should display the default description.
        /// </summary>
        public bool ShowDefaultGroupDescription { get; set; }

        /// <summary>
        /// Indicates if Host units should be used for graphic helpers for Dynamo Revit
        /// </summary>
        public bool UseHostScaleUnits { get; set; }

        /// <summary>
        /// Indicates if code block node line numbers should  be displayed.
        /// </summary>
        public bool ShowCodeBlockLineNumber { get; set; }

        /// <summary>
        /// Should connectors be visible?
        /// </summary>
        public bool ShowConnector { get; set; }

        /// <summary>
        /// Should connector tooltip be visible?
        /// </summary>
        public bool ShowConnectorToolTip { get; set; }

        /// <summary>
        /// Indicates the zoom scale of the library
        /// </summary>
        public int LibraryZoomScale { get; set; }

        /// <summary>
        /// Indicates the zoom scale of the Python editor
        /// </summary>
        public int PythonScriptZoomScale { get; set; }

        /// <summary>
        /// The types of connector: Bezier or Polyline.
        /// </summary>
        public ConnectorType ConnectorType { get; set; }

        /// <summary>
        /// Collection of pairs [BackgroundPreviewName;isActive]
        /// </summary>
        public List<BackgroundPreviewActiveState> BackgroundPreviews { get; set; }

        /// <summary>
        /// Returns active state of specified background preview
        /// </summary>
        /// <param name="name">Background preview name</param>
        /// <returns>The active state</returns>
        public bool GetIsBackgroundPreviewActive(string name)
        {
            var pair = GetBackgroundPreviewData(name);

            return pair.IsActive;
        }

        /// <summary>
        /// Sets active state of specified background preview
        /// </summary>
        /// <param name="name">Background preview name</param>
        /// <param name="value">Active state</param>
        public void SetIsBackgroundPreviewActive(string name, bool value)
        {
            var pair = GetBackgroundPreviewData(name);

            pair.IsActive = value;
        }

        private BackgroundPreviewActiveState GetBackgroundPreviewData(string name)
        {
            // find or create BackgroundPreviewActiveState instance in list by name
            var pair = BackgroundPreviews.FirstOrDefault(p => p.Name == name)
                ?? new BackgroundPreviewActiveState { Name = name };
            if (!BackgroundPreviews.Contains(pair))
            {
                BackgroundPreviews.Add(pair);
            }

            return pair;
        }

        /// <summary>
        /// Should the background grid be shown?
        /// </summary>
        public bool IsBackgroundGridVisible
        {
            get
            {
                return isBackgroundGridVisible;
            }
            set
            {
                if (value == isBackgroundGridVisible) return;
                isBackgroundGridVisible = value;

                RaisePropertyChanged(nameof(IsBackgroundGridVisible));
            }
        }

        /// <summary>
        /// Sets the background grid element scale
        /// </summary>
        public float GridScaleFactor
        {
            get
            {
                return gridScaleFactor;
            }
            set
            {
                if (value == gridScaleFactor) return;
                gridScaleFactor = value;

                RaisePropertyChanged(nameof(GridScaleFactor));
            }
        }

        /// <summary>
        /// The current Host document units. Will be updated the first time Dynamo is started
        /// </summary>
        internal Configurations.Units CurrentHostUnits
        {
            get
            {
                return currentHostUnits;
            }
            set
            {
                if (value == currentHostUnits) return;
                currentHostUnits = value;

                RaisePropertyChanged(nameof(CurrentHostUnits));
            }
        }

        /// <summary>
        /// Default geometry scale factor for a new workspace
        /// </summary>
        public double DefaultScaleFactor
        {
            get
            {
                return defaultScaleFactor;
            }
            set
            {
                if (value == defaultScaleFactor) return;
                defaultScaleFactor = value;

                RaisePropertyChanged(nameof(DefaultScaleFactor));
            }
        }


        /// <summary>
        /// Indicates whether background preview is active or not.
        /// </summary>
        [Obsolete("Property will be deprecated in a future version of Dynamo, please use BackgroundPreviews")]
        public bool IsBackgroundPreviewActive
        {
            get
            {
                return GetIsBackgroundPreviewActive("IsBackgroundPreviewActive");
            }
            set
            {
                SetIsBackgroundPreviewActive("IsBackgroundPreviewActive", value);
            }
        }

        /// <summary>
        /// Indicate which render precision will be used
        /// </summary>
        public int RenderPrecision { get; set; }

        /// <summary>
        /// Indicates whether surface and solid edges will
        /// be rendered.
        /// </summary>
        public bool ShowEdges { get; set; }

        /// <summary>
        /// Indicates whether background preview use instancing when rendering geometry.
        /// be rendered.
        /// </summary>
        public bool UseRenderInstancing { get; set; }

        /// <summary>
        /// Indicates whether show detailed or compact layout during search.
        /// </summary>
        public bool ShowDetailedLayout { get; set; }


        /// <summary>
        /// The last X coordinate of the Dynamo window.
        /// </summary>
        public double WindowX { get; set; }

        /// <summary>
        /// The last Y coordinate of the Dynamo window.
        /// </summary>
        public double WindowY { get; set; }

        /// <summary>
        /// The last width of the Dynamo window.
        /// </summary>
        public double WindowW { get; set; }

        /// <summary>
        /// The last height of the Dynamo window.
        /// </summary>
        public double WindowH { get; set; }

        /// <summary>
        /// Should Dynamo use hardware acceleration if it is supported?
        /// </summary>
        public bool UseHardwareAcceleration { get; set; }

        /// <summary>
        /// Persistence for Dynamo HomePage
        /// </summary>  
        public List<string> HomePageSettings { get; set; }
        #endregion

        #region Dynamo application settings

        /// <summary>
        /// The decimal precision used to display numbers.
        /// </summary>
        public string NumberFormat
        {
            get { return numberFormat; }
            set
            {
                numberFormat = value;
                RaisePropertyChanged("NumberFormat");
            }
        }

        /// <summary>
        /// The maximum number of recent file paths to be saved.
        /// </summary>
        public int MaxNumRecentFiles
        {
            get { return maxNumRecentFiles; }
            set
            {
                if (value > 0)
                {
                    maxNumRecentFiles = value;
                }
                else
                {
                    maxNumRecentFiles = DefaultMaxNumRecentFiles;
                }
                RaisePropertyChanged("MaxNumRecentFiles");
            }
        }

        /// <summary>
        /// A list of recently opened file paths.
        /// </summary>
        public List<string> RecentFiles { get; set; }

        /// <summary>
        /// Backup files path
        /// </summary>
        public string BackupLocation
        {
            get { return backupLocation; }
            set
            {
                backupLocation = value;
                RaisePropertyChanged(nameof(BackupLocation));
            }
        }

        /// <summary>
        /// Template path
        /// </summary>
        public string TemplateFilePath
        {
            get { return templateFilePath; }
            set
            {
                templateFilePath = value;
                RaisePropertyChanged(nameof(TemplateFilePath));
            }
        }

        /// <summary>
        /// A list of backup file paths.
        /// </summary>
        public List<string> BackupFiles { get; set; }

        /// <summary>
        /// A list of folders packages, custom nodes or direct paths to .dll and .ds files.
        /// </summary>
        public List<string> CustomPackageFolders { get; set; }

        /// <summary>
        /// If true, trust warnings for opening .dyn files from untrusted locations will not be shown.
        /// Do not use this property setter, it does nothing. Exists only to support serialization.
        /// </summary>
        public bool DisableTrustWarnings
        {
            get => disableTrustWarnings;
            //no-op
            set { }
        }

        /// <summary>
        /// This represents the user modifiable list of locations.
        /// </summary>
        private List<string> trustedLocations { get; set; } = new List<string>();

        /// <summary>
        /// Return a list of GraphChecksumItems
        /// </summary>
        [Obsolete("This property is not needed anymore in the preference settings and can be removed in a future version of Dynamo.")]
        public List<GraphChecksumItem> GraphChecksumItemsList { get; set; }

        // This function is used to deserialize the trusted locations manually
        // so that the TrustedLocation propertie's setter does not need to be public.
        private List<string> DeserializeTrustedLocations(XmlNode preferenceSettingsElement)
        {
            List<string> output = new List<string>();
            try
            {
                var parentNode = preferenceSettingsElement.SelectSingleNode($@"//{nameof(TrustedLocations)}");
                if (parentNode != null)
                {
                    foreach (XmlNode value in parentNode.ChildNodes)
                    {
                        if (!string.IsNullOrEmpty(value?.InnerText))
                        {
                            output.Add(value.InnerText);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return output;
        }

        private bool DeserializeDisableTrustWarnings(XmlNode preferenceSettingsElement)
        {
            try
            {
                return bool.Parse(preferenceSettingsElement.SelectSingleNode($@"//{nameof(DisableTrustWarnings)}").InnerText);

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return false;
        }

        /// <summary>
        /// Manually deserialize some preferences from the PreferencesSettings file.
        /// This is done so that we can avoid exposing these property setters to the public API.
        /// </summary>
        /// <param name="prefsFilePath"></param>
        private void DeserializeInternalPrefs(string prefsFilePath)
        {
            try
            {
                //manually load some xml we don't want to create public setters for.
                var doc = new System.Xml.XmlDocument();
                doc.Load(prefsFilePath);
                var prefs = doc.SelectSingleNode($@"//{nameof(PreferenceSettings)}");

                var deserializedLocations = DeserializeTrustedLocations(prefs);
                SetTrustedLocations(deserializedLocations.Distinct());
                var trustWarningsDisabled = DeserializeDisableTrustWarnings(prefs);
                SetTrustWarningsDisabled(trustWarningsDisabled);
            }
            catch
            { }
        }

        /// <summary>
        /// Manually deserialize some preferences from the PreferencesSettings file.
        /// This is done so that we can avoid exposing these property setters to the public API.
        /// </summary>
        /// <param name="content">The content of the XML file</param>

        private void DeserializeInternalPrefsContent(string content)
        {
            try
            {
                //manually load some xml we don't want to create public setters for.
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(content);
                var prefs = doc.SelectSingleNode($@"//{nameof(PreferenceSettings)}");

                var deserializedLocations = DeserializeTrustedLocations(prefs);
                SetTrustedLocations(deserializedLocations.Distinct());
                var trustWarningsDisabled = DeserializeDisableTrustWarnings(prefs);
                SetTrustWarningsDisabled(trustWarningsDisabled);
            }
            catch
            { }
        }

        /// <summary>
        /// Represents a copy of the list of trusted locations that the user added.
        /// Do not use this list to check if a new path is trusted or not.
        /// To check if a new path is trusted or not please use the IsTrustedLocation API (IsTrustedLocation supports locations)
        /// </summary>
        public List<string> TrustedLocations
        {
            get => trustedLocations.ToList(); //Copy of the internal list
        }

        /// <summary>
        /// A list of packages used by the Package Manager to determine
        /// which packages are marked for deletion.
        /// </summary>
        public List<string> PackageDirectoriesToUninstall { get; set; }

        /// <summary>
        /// Path to the Python (.py) file to use as a starting template when creating a new PythonScript Node.
        /// </summary>
        public string PythonTemplateFilePath
        {
            get { return pythonTemplateFilePath; }
            set { pythonTemplateFilePath = value; }
        }

        /// <summary>
        /// The backing store for the Python template file path. Required as static property cannot implement an interface member.
        /// </summary>
        private static string pythonTemplateFilePath = "";

        /// <summary>
        /// This defines how long (in milliseconds) will the graph be automatically saved.
        /// </summary>
        public int BackupInterval { get; set; }

        /// <summary>
        /// This defines how many files will be backed up.
        /// </summary>
        public int BackupFilesCount { get; set; }

        /// <summary>
        /// Indicates if the user has accepted the terms of
        /// use for downloading packages from package manager.
        /// </summary>
        public bool PackageDownloadTouAccepted { get; set; }

        /// <summary>
        /// Indicates the default state of the "Open in Manual Mode"
        /// checkbox in OpenFileDialog
        /// </summary>
        public bool OpenFileInManualExecutionMode { get; set; }

        /// <summary>
        /// This defines if user wants to see the Iron Python Extension Dialog box on every new session.
        /// </summary>
        [Obsolete("This property is deprecated and will be removed in a future version of Dynamo")]
        public bool IsIronPythonDialogDisabled { get; set; }

        /// <summary>
        /// This defines if user wants to see the whitespaces and tabs in python script editor.
        /// </summary>
        public bool ShowTabsAndSpacesInScriptEditor { get; set; }

        /// <summary>
        /// This defines if user wants to see the enabled node Auto Complete feature for port interaction.
        /// </summary>
        public bool EnableNodeAutoComplete { get; set; }

        /// <summary>
        /// This allows the user to enable or disable the new node auto complete menu.
        /// </summary>
        public bool EnableNewNodeAutoCompleteUI { get; set; }

        /// <summary>
        /// PolyCurve normal and direction behavior has been made predictable in Dynamo 3.0 and has therefore changed. 
        /// This defines whether legacy (pre-3.0) PolyCurve behavior is selected by default.
        /// This flag can be overridden by individual workspaces that have the EnableLegacyPolyCurveBehavior flag defined.
        /// Note: For internal use only and will be removed in a future version of Dynamo.
        /// </summary>
        [IsObsolete("This property will be removed in a future version of Dynamo.")]
        public bool DefaultEnableLegacyPolyCurveBehavior { get; set; }

        /// <summary>
        /// This defines if user wants to hide the nodes below a specific confidenc level.
        /// </summary>
        public bool HideNodesBelowSpecificConfidenceLevel { get; set; }

        /// <summary>
        /// This defines the level of confidence related to the ML recommendation.
        /// </summary>
        public int MLRecommendationConfidenceLevel { get; set; }

        private int mLRecommendationNumberOfResults;
        /// <summary>
        /// This defines the number of results of the  ML recommendation
        /// </summary>
        public int MLRecommendationNumberOfResults
        {
            get => mLRecommendationNumberOfResults;
            set
            {
                if (mLRecommendationNumberOfResults != value)
                {
                    mLRecommendationNumberOfResults = value;
                    AutocompletePreferencesChanged?.Invoke();
                }
            }
        }

        /// <summary>
        /// If true, autocomplete method options are hidden from UI 
        /// </summary>
        public bool HideAutocompleteMethodOptions { get; set; }

        /// <summary>
        /// This defines if user wants to see the enabled Dynamo Notification Center.
        /// </summary>
        public bool EnableNotificationCenter
        {
            get
            {
                return isNotificationCenterEnabled;
            }
            set
            {
                isNotificationCenterEnabled = value;
                RaisePropertyChanged(nameof(EnableNotificationCenter));
            }
        }

        /// <summary>
        /// This defines if user wants the Extensions settings to persist across sessions.
        /// </summary>
        public bool EnablePersistExtensions
        {
            get
            {
                return isEnablePersistExtensionsEnabled;
            }
            set
            {
                isEnablePersistExtensionsEnabled = value;
                RaisePropertyChanged(nameof(EnablePersistExtensions));
            }
        }


        /// <summary>
        /// This defines if the user wants to see the static splash screen again
        /// </summary>
        public bool EnableStaticSplashScreen
        {
            get
            {
                return isStaticSplashScreenEnabled;
            }
            set
            {
                isStaticSplashScreenEnabled = value;
            }
        }

        /// <summary>
        /// This defines if the user is agree to the ML Automcomplete Terms of Use
        /// </summary>
        public bool IsMLAutocompleteTOUApproved
        {
            get
            {
                return isMLAutocompleteTOUApproved;
            }
            set
            {
                isMLAutocompleteTOUApproved = value;
            }
        }        

        /// <summary>
        /// Engine used by default for new Python script and string nodes. If not empty, this takes precedence over any system settings.
        /// </summary>
        public string DefaultPythonEngine
        {
            get
            {
                return defaultPythonEngine;
            }
            set
            {
                defaultPythonEngine = value;
            }
        }

        /// <summary>
        /// Static field backing the DefaultPythonEngine setting property.
        /// </summary>
        private static string defaultPythonEngine;

        internal event Func<string> RequestUserDataFolder;
        internal string OnRequestUserDataFolder()
        {
            return RequestUserDataFolder?.Invoke();
        }

        private string selectedPackagePathForInstall;
        // TODO: Add this to IPreferences in Dynamo 3.0
        /// <summary>
        /// Currently selected package path where all packages downloaded from the Package Manager
        /// will be installed. The default package path for install is the user data directory
        /// currently used by the Dynamo environment.
        /// </summary>
        public string SelectedPackagePathForInstall
        {
            get
            {
                if (string.IsNullOrEmpty(selectedPackagePathForInstall))
                {
                    selectedPackagePathForInstall = OnRequestUserDataFolder();
                }
                return selectedPackagePathForInstall;
            }
            set
            {
                selectedPackagePathForInstall = value;
            }
        }

        /// <summary>
        /// Indicates (if any) which namespaces should not be displayed in the Dynamo node library.
        /// String format: "[library name]:[fully qualified namespace]"
        /// </summary>
        public List<string> NamespacesToExcludeFromLibrary { get; set; }

        /// <summary>
        /// True if the NamespacesToExcludeFromLibrary element is found in DynamoSettings.xml.
        /// </summary>
        [XmlIgnore]
        public bool NamespacesToExcludeFromLibrarySpecified { get; set; }

        /// <summary>
        /// Settings that apply to view extensions.
        /// </summary>
        public List<ViewExtensionSettings> ViewExtensionSettings { get; set; }

        private bool disableBuiltinPackages;
        /// <summary>
        /// If enabled Dynamo Built-In Packages will not be loaded.
        /// </summary>
        public bool DisableBuiltinPackages
        {
            get { return disableBuiltinPackages; }
            set
            {
                disableBuiltinPackages = value;
                RaisePropertyChanged(nameof(DisableBuiltinPackages));
            }
        }

        private bool disableCustomPackageLocations;
        /// <summary>
        /// If enabled user's custom package locations will not be loaded.
        /// </summary>
        public bool DisableCustomPackageLocations
        {
            get { return disableCustomPackageLocations; }
            set
            {
                disableCustomPackageLocations = value;
                RaisePropertyChanged(nameof(DisableCustomPackageLocations));
            }
        }
        /// <summary>
        /// Defines the default run type when opening a workspace
        /// </summary>
        public RunType DefaultRunType { get; set; }

        private NodeAutocompleteSuggestion defaultNodeAutocompleteSuggestion;
        /// <summary>
        /// Defines the default method of the Node Autocomplete
        /// </summary>
        public NodeAutocompleteSuggestion DefaultNodeAutocompleteSuggestion
        {
            get => defaultNodeAutocompleteSuggestion;
            set
            {
                if(defaultNodeAutocompleteSuggestion != value)
                {
                    defaultNodeAutocompleteSuggestion = value;
                    AutocompletePreferencesChanged?.Invoke();
                }
            }
        }

        /// <summary>
        ///     Event that is fired when autocomplete-specific preferences are changed
        /// </summary>
        internal event Action AutocompletePreferencesChanged;

        /// <summary>
        /// Show Run Preview flag.
        /// </summary>
        public bool ShowRunPreview { get; set; }


        /// <summary>
        /// Stores the group styles added in the preference settings
        /// </summary>
        public List<GroupStyleItem> GroupStyleItemsList { get; set; }

        /// <summary>
        /// Limits the size of the tags used by the EntryDictionary
        /// This static property is not serialized and is assigned NodeSearchTagSizeLimit's value
        /// if found at deserialize time.
        /// </summary>
        internal static int nodeSearchTagSizeLimit = 300;

        /// <summary>
        /// Limits the size of the tags used by the EntryDictionary
        /// </summary>
        public int NodeSearchTagSizeLimit
        {
            get { return nodeSearchTagSizeLimit; }
            set { nodeSearchTagSizeLimit = value; }
        }

        /// <summary>
        /// The Version of the IronPython package that Dynamo will download when it is found as missing in graphs.
        /// This property is not serialized and is assigned IronPythonResolveTargetVersion's value
        /// if found at deserialize time.
        /// </summary>
        internal Version ironPythonResolveTargetVersion = new Version(3, 2, 0);

        /// <summary>
        /// The Version of the IronPython package that Dynamo will download when it is found as missing in graphs.
        /// </summary>
        public string IronPythonResolveTargetVersion
        {
            get { return ironPythonResolveTargetVersion.ToString(); }
            set { ironPythonResolveTargetVersion = Version.TryParse(value, out Version newVal) ? newVal : ironPythonResolveTargetVersion; }
        }

        /// <summary>
        /// Stores the notification ids that was read by the user
        /// </summary>
        public List<string> ReadNotificationIds { get; set; }
        #endregion

        #region Dynamo Player and Generative Design settings

        /// <summary>
        /// Enable legacy behavior for Dynamo Player to allow renamed Watch nodes to be seen as graph output.
        /// This flag is for use in the 2024 product release year and can removed for 2025
        /// </summary>
        public bool EnableDynamoPlayerRenamedWatchAsOutput { get; set; }

        /// <summary>
        /// Collections of folders used by individual Dynamo Player or Generative Design as entry points.
        /// </summary>
        public List<DynamoPlayerFolderGroup> DynamoPlayerFolderGroups { get; set; }

        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="PreferenceSettings"/> class.
        /// </summary>
        public PreferenceSettings()
        {
            RecentFiles = new List<string>();
            WindowH = 768;
            WindowW = 1024;
            WindowY = 0.0;
            WindowX = 0.0;
            BackgroundPreviews = new List<BackgroundPreviewActiveState>();

            // Default Settings
            IsFirstRun = true;
            Locale = Configurations.SupportedLocaleDic.FirstOrDefault().Value;
            LibraryWidth = 304;
            ConsoleHeight = 0;
            ShowPreviewBubbles = true;
            ShowCodeBlockLineNumber = true;
            ShowConnector = true;
            ShowConnectorToolTip = true;
            ConnectorType = ConnectorType.BEZIER;
            IsBackgroundGridVisible = true;
            GraphicScaleUnit = Configurations.SupportedUnits.FirstOrDefault().Key.ToString();
            GridScaleFactor = (float)Configurations.SupportedUnits.FirstOrDefault().Value;
            UseHostScaleUnits = true;
            PackageDirectoriesToUninstall = new List<string>();
            NumberFormat = "f3";
            UseHardwareAcceleration = true;
            PackageDownloadTouAccepted = false;
            maxNumRecentFiles = DefaultMaxNumRecentFiles;
            RenderPrecision = DefaultRenderPrecision;
            ShowEdges = false;
            UseRenderInstancing = true;
            OpenFileInManualExecutionMode = false;
            ShowDetailedLayout = true;
            NamespacesToExcludeFromLibrary = new List<string>();
            DefaultRunType = RunType.Automatic;
            DefaultNodeAutocompleteSuggestion = NodeAutocompleteSuggestion.MLRecommendation;
            ShowDefaultGroupDescription = true;

            BackupInterval = DefaultBackupInterval;
            BackupFilesCount = 1;
            BackupFiles = new List<string>();
            BackupLocation = string.Empty;

            TemplateFilePath = string.Empty;

            LibraryZoomScale = 100;
            PythonScriptZoomScale = 100;

            CustomPackageFolders = new List<string>();

            PythonTemplateFilePath = "";
            IsIronPythonDialogDisabled = false;
            ShowTabsAndSpacesInScriptEditor = false;
            EnableNodeAutoComplete = true;
            EnableNewNodeAutoCompleteUI = true;
            DefaultEnableLegacyPolyCurveBehavior = true;
            HideNodesBelowSpecificConfidenceLevel = false;
            MLRecommendationConfidenceLevel = 10;
            MLRecommendationNumberOfResults = 10;
            HideAutocompleteMethodOptions = false;
            EnableNotificationCenter = true;
            isStaticSplashScreenEnabled = true;
            isTimeStampIncludedInExportFilePath = true;
            DefaultPythonEngine = string.Empty;
            ViewExtensionSettings = new List<ViewExtensionSettings>();
            GroupStyleItemsList = new List<GroupStyleItem>();
            ReadNotificationIds = new List<string>();
            EnableDynamoPlayerRenamedWatchAsOutput = false;
            DynamoPlayerFolderGroups = new List<DynamoPlayerFolderGroup>();
            backupLocation = string.Empty;
            GraphChecksumItemsList = new List<GraphChecksumItem>();
            isMLAutocompleteTOUApproved = true;
            HomePageSettings = new List<string>();
        }

        /// <summary>
        /// Saves PreferenceSettings to XML, given a file path.
        /// </summary>
        /// <param name="filePath">Path of the XML File to save to.</param>
        /// <returns>True if file is saved successfully, false if an error occurred.</returns>
        public bool Save(string filePath)
        {
            try
            {
                var serializer = new XmlSerializer(typeof(PreferenceSettings));
                using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                {
                    serializer.Serialize(fs, this);
                    fs.Close(); // Release file lock
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }

            return false;
        }

        /// <summary>
        /// Saves PreferenceSettings in a default directory when no path is
        /// specified.
        /// </summary>
        /// <param name="preferenceFilePath">The file path to save preference
        /// settings to. If this parameter is null or empty string, preference
        /// settings will be saved to the default path.</param>
        /// <returns>True if file is saved successfully, false if an error occurred.</returns>
        public bool SaveInternal(string preferenceFilePath)
        {
            if (!String.IsNullOrEmpty(DynamoTestPath))
            {
                preferenceFilePath = DynamoTestPath;
            }

            return Save(preferenceFilePath);
        }

        /// <summary>
        /// Loads PreferenceSettings from specified XML file if possible,
        /// else initializes PreferenceSettings with default values.
        /// </summary>
        /// <param name="filePath">Path of the XML File</param>
        /// <returns>
        /// Stored PreferenceSettings from xml file or
        /// Default PreferenceSettings if xml file is not found.
        /// </returns>
        public static PreferenceSettings Load(string filePath)
        {
            // Constructor will be called anyway in either condition below so no need to initialize now.
            PreferenceSettings settings = null;

            if (String.IsNullOrEmpty(filePath) || (!File.Exists(filePath)))
                return new PreferenceSettings();

            try
            {
                var serializer = new XmlSerializer(typeof(PreferenceSettings));
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    settings = serializer.Deserialize(fs) as PreferenceSettings;
                    var namespaces = settings?.NamespacesToExcludeFromLibrary;
                    for (var index = 0; index < namespaces?.Count; index++)
                    {
                        if (namespaces[index] == "ProtoGeometry.dll:Autodesk.DesignScript.Geometry.Panel")
                        {
                            namespaces[index] = "ProtoGeometry.dll:Autodesk.DesignScript.Geometry.PanelSurface";
                        }
                    }

                    // If the backup interval is set to OldDefaultBackupInterval (60000ms - 1 minute), reset it to the new default value.
                    var savedBackUpInterval = settings?.BackupInterval;
                    if (savedBackUpInterval == OldDefaultBackupInterval)
                    {
                        settings.BackupInterval = DefaultBackupInterval;
                    }

                    //Do not add invalid paths for recent files list
                    var recentFiles = settings?.RecentFiles;
                    if (recentFiles != null)
                    {
                        settings.RecentFiles = recentFiles.Where(path => !string.IsNullOrEmpty(path) && DynamoUtilities.PathHelper.IsValidPath(path)).ToList();
                    }

                    fs.Close(); // Release file lock
                }
            }
            catch
            {
                if (settings == null)
                {
                    return new PreferenceSettings() { isCreatedFromValidFile = false };
                }
            }
            settings.CustomPackageFolders = settings.CustomPackageFolders.Distinct().ToList();
            settings.GroupStyleItemsList = settings.GroupStyleItemsList.GroupBy(entry => entry.Name).Select(result => result.First()).ToList();
            MigrateStdLibTokenToBuiltInToken(settings);

            settings.DeserializeInternalPrefs(filePath);

            return settings;
        }

        /// <summary>
        /// Loads PreferenceSettings from specified XML file if possible,
        /// else initializes PreferenceSettings with default values.
        /// </summary>
        /// <param name="content">The content of the xml file</param>
        /// <returns></returns>
        public static PreferenceSettings LoadContent(string content)
        {
            // Constructor will be called anyway in either condition below so no need to initialize now.
            PreferenceSettings settings = null;

            if(string.IsNullOrEmpty(content))
                return new PreferenceSettings() { isCreatedFromValidFile = false };

            try
            {
                var serializer = new XmlSerializer(typeof(PreferenceSettings));
                using (TextReader reader = new StringReader(content))
                {
                    settings = serializer.Deserialize(reader) as PreferenceSettings;
                }
            }
            catch
            {
                if (settings == null)
                {
                    return new PreferenceSettings() { isCreatedFromValidFile = false };
                }
            }
                
            settings.CustomPackageFolders = settings.CustomPackageFolders.Distinct().ToList();
            settings.GroupStyleItemsList = settings.GroupStyleItemsList.GroupBy(entry => entry.Name).Select(result => result.First()).ToList();
            MigrateStdLibTokenToBuiltInToken(settings);

            settings.DeserializeInternalPrefsContent(content);

            return settings;
        }

        /// <summary>
        /// Return the predefined Font size values
        /// </summary>
        [XmlIgnore]
        public ObservableCollection<int> PredefinedGroupStyleFontSizes
        {
            get
            {
                return new ObservableCollection<int>
            {
                14,
                18,
                24,
                30,
                36,
                48,
                60,
                72,
                96
            };
            }
        }

        /// <summary>
        /// Checking for invalid values or tainted data and sanitize them
        /// </summary>
        internal void SanitizeValues()
        {
            foreach (var groupStyle in GroupStyleItemsList)
            {
                if (!PredefinedGroupStyleFontSizes.Contains(groupStyle.FontSize))
                {
                    groupStyle.FontSize = GroupStyleItem.DefaultGroupStyleItems.FirstOrDefault().FontSize;
                }
            }
        }

        /// <summary>
        /// Returns the static Python template file path.
        /// When the file exists and is not empty, its contents are used to populate new Python Script nodes added to the Dynamo workspace.
        /// </summary>
        /// <returns></returns>
        public static string GetPythonTemplateFilePath()
        {
            return pythonTemplateFilePath;
        }

        /// <summary>
        /// Provides access to the DefaultPythonEngine setting in a static context. Used from PythonNodeBase.
        /// </summary>
        /// <returns>DefaultPythonEngine setting value</returns>
        internal static string GetDefaultPythonEngine()
        {
            return defaultPythonEngine;
        }

        /// <summary>
        /// Initialize namespaces to exclude from Library based on conditions
        /// </summary>
        internal void InitializeNamespacesToExcludeFromLibrary()
        {
            if (!NamespacesToExcludeFromLibrarySpecified)
            {
                NamespacesToExcludeFromLibrary = InitialExperimentalLib_Namespaces.ToList();
                NamespacesToExcludeFromLibrarySpecified = true;
            }
        }

        /// <summary>
        /// Update namespaces to exclude from Library based on feature flags
        /// </summary>
        internal void UpdateNamespacesToExcludeFromLibrary()
        {
            // Include the TSpline namespace from the library OOTB.
            NamespacesToExcludeFromLibrary.Remove(
                "ProtoGeometry.dll:Autodesk.DesignScript.Geometry.TSpline"
            );
            return;
        }

        //migrate old path token to new path token
        private static void MigrateStdLibTokenToBuiltInToken(PreferenceSettings settings)
        {
            for (var i = 0; i < settings.CustomPackageFolders.Count; i++)
            {
                var path = settings.CustomPackageFolders[i];
                if (path == DynamoModel.StandardLibraryToken)
                {
                    settings.CustomPackageFolders[i] = DynamoModel.BuiltInPackagesToken;
                }
            }
        }

        #region Trust Management API
        /// <summary>
        /// Add a path to the Dynamo's trusted locations
        /// </summary>
        /// <param name="path">The path to be added as a trusted location</param>
        /// <returns>True if the path was successfully added. False otherwise.</returns>
        internal bool AddTrustedLocation(string path)
        {
            try
            {
                PathHelper.ValidateDirectory(path);
                if (isTrustedLocationInternal(path))
                {
                    return false;
                }
                trustedLocations.Add(path);
                return true;
            }
            catch(Exception e)
            {
                OnMessageLogged(LogMessage.Error($"Could not add path {path} to {nameof(TrustedLocations)} due to the following error {e.Message}"));
                return false;
            }
        }

        /// <summary>
        /// Remove a path from the Dynamo's trusted locations
        /// </summary>
        /// <param name="path">The path to be removed from the trusted locations</param>
        /// <returns>The true if the path was removed and false otherwise</returns>
        internal bool RemoveTrustedLocation(string path)
        {
            return trustedLocations.RemoveAll(x => PathHelper.AreDirectoryPathsEqual(x, path)) > 0;
        }

        /// <summary>
        /// Set trusted locations in the PreferenceSettings configuration.
        /// </summary>
        /// <param name="locs"></param>
        internal void SetTrustedLocations(IEnumerable<string> locs)
        {
            trustedLocations.Clear();
            foreach (var loc in locs)
            {
                AddTrustedLocation(loc);
            }
        }

        /// <summary>
        /// Set trusted locations in the PreferenceSettings configuration without checking if inputs are valid.
        /// Use this method only in tests.
        /// </summary>
        /// <param name="locs"></param>
        internal void SetTrustedLocationsUnsafe(IEnumerable<string> locs)
        {
            trustedLocations.Clear();
            trustedLocations.AddRange(locs);
        }

        /// <summary>
        /// Disable Trust Warnings for file security
        /// </summary>
        /// <param name="disabled"></param>
        internal void SetTrustWarningsDisabled(bool disabled)
        {
            disableTrustWarnings = disabled;
        }

        // Add default trusted locations for Autodesk samples.
        // This function should only be called during Dynamo's first run.
        internal void AddDefaultTrustedLocations()
        {
            if (!IsFirstRun) return;
            string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            AddTrustedLocation(Path.Combine(ProgramData, Configurations.AutodeskAsString));

            string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            AddTrustedLocation(Path.Combine(ProgramFiles, Configurations.AutodeskAsString));
        }

        /// <summary>
        /// Returns true if the input "location" is among the stored trusted paths.
        /// Subdirectories of a trusted path are considered trusted.
        /// Does not validate the input for correctness.
        /// </summary>
        /// <param name="location"></param>
        /// <returns></returns>
        private bool isTrustedLocationInternal(string location)
        {
            return TrustedLocations.FirstOrDefault(trustedLoc =>
            {
                // All subdirectories are considered trusted if the parent directory is trusted.
                return PathHelper.AreDirectoryPathsEqual(location, trustedLoc) ||
                    PathHelper.IsSubDirectoryOfDirectory(location, trustedLoc);
            }) != null;
        }

        /// <summary>
        /// Checkes whether the input argument (path) is among Dynamo's trusted locations
        /// Only directories are supported.
        /// Subdirectories of a trusted directory are considered trusted.
        /// </summary>
        /// <param name="location">An absolute path to a folder or file on disk</param>
        /// <returns>True if the path is a trusted location, false otherwise</returns>
        public bool IsTrustedLocation(string location)
        {
            try
            {
                PathHelper.ValidateDirectory(location);
                return isTrustedLocationInternal(location);
            }
            catch
            {
                return false;
            }

        }

        /// <summary>
        /// Different ways to ask the user about display the Trusted location
        /// </summary>
        public enum AskForTrustedLocationResult
        {
            /// <summary>
            /// Ask for the Trusted location
            /// </summary>
            Ask,
            /// <summary>
            /// Don't ask about the Trusted location
            /// </summary>
            DontAsk,
            /// <summary>
            /// Unable to ask about the Trusted location
            /// </summary>
            UnableToAsk
        }

        /// <summary>
        /// AskForTrustedLocation function
        /// </summary>
        /// <param name="isOpenedFile"></param>
        /// <param name="isHomeSpace"></param>
        /// <param name="isShowStartPage"></param>
        /// <param name="isDisableTrustWarnings"></param>
        /// <param name="isFileInTrustedLocation"></param>
        /// <returns></returns>
        public static AskForTrustedLocationResult AskForTrustedLocation(bool isOpenedFile, bool isFileInTrustedLocation, bool isHomeSpace, bool isShowStartPage, bool isDisableTrustWarnings)
        {
            AskForTrustedLocationResult result = AskForTrustedLocationResult.UnableToAsk;
            if (isOpenedFile)
            {
                if (isHomeSpace && !isShowStartPage && !isDisableTrustWarnings && !isFileInTrustedLocation)
                {
                    result = AskForTrustedLocationResult.Ask;
                }
                else
                {
                    result = AskForTrustedLocationResult.DontAsk;
                }
            }
            return result;
        }

        #endregion

        #region ILogSource

        /// <summary>
        /// Log message event
        /// </summary>
        public event Action<ILogMessage> MessageLogged;

        internal void OnMessageLogged(ILogMessage msg)
        {
            MessageLogged?.Invoke(msg);
        }
        #endregion

        /// <summary>
        /// List of static Fields to be excluded for evaluation due to their access level
        /// </summary>
        /// <returns></returns>
        public List<string> StaticFields()
        {
            return typeof(PreferenceSettings).GetMembers(BindingFlags.Static | BindingFlags.NonPublic).OfType<FieldInfo>().Select(field => field.Name).ToList();
        }

        /// <summary>
        /// Indicates when an instance has been created from a preferences file correctly, a support property
        /// </summary>
        [XmlIgnore]
        public bool IsCreatedFromValidFile
        {
            get { return isCreatedFromValidFile; }
        }

    }
}
