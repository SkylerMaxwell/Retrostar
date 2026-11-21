// Retrostar Core monolith. Production C# lives in exactly two files: RetroBar/Retrostar_Core.cs and
// RetroBar/Retrostar_Interface.cs. Each is a sequence of logical modules, each delimited by
// RETROSTAR_SOURCE_BEGIN / RETROSTAR_SOURCE_END with the module's logical path, grouped under [nnnn] category
// markers. Rules and navigation index: Rules_For_Agent_Editors.md.

// [1000] FOUNDATION :: assembly attributes, entry point, path and list helpers, logging

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/AssemblyInfo.cs">
// The original "using System.Windows;" is written out in full below: a using directive here would apply
// to every module in this file. Assembly attributes must come before every namespace, so this module is first.
[assembly: System.Windows.ThemeInfo(
    System.Windows.ResourceDictionaryLocation.None, //where theme specific resource dictionaries are located
                                     //(used if a resource is not found in the page,
                                     // or application resource dictionaries)
    System.Windows.ResourceDictionaryLocation.SourceAssembly //where the generic resource dictionary is located
                                              //(used if a resource is not found in the page,
                                              // app, or any theme specific resource dictionaries)
)]
// <RETROSTAR_SOURCE_END path="RetroBar/AssemblyInfo.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Program.cs">
namespace RetroBar
{
    using System;
    using System.Windows.Forms;

    internal sealed class Program
    {
        private const string MutexName = "RetroBar";
        private const int MutexAttempts = 10;
        private const int MutexWaitMs = 1000;

        private static System.Threading.Mutex _retroBarMutex;

        /// <summary>
        /// The main entry point for the application
        /// </summary>
        [STAThread]
        public static int Main(string[] args)
        {
            if (!SingleInstanceCheck())
            {
                return 1;
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            App app = new App();
            app.InitializeComponent();

            return app.Run();
        }

        private static bool GetMutex()
        {
            _retroBarMutex = new System.Threading.Mutex(true, MutexName, out bool ok);

            return ok;
        }

        private static bool SingleInstanceCheck()
        {
            for (int i = 0; i < MutexAttempts; i++)
            {
                if (!GetMutex())
                {
                    // Dispose the mutex, otherwise it will never create new
                    _retroBarMutex.Dispose();
                    System.Threading.Thread.Sleep(MutexWaitMs);
                }
                else
                {
                    return true;
                }
            }

            return false;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Program.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Extensions/ManagementExtensions.cs">
// This class is in the global namespace, as it always was, so its callers need no using directive.
// Its original usings (System, System.Collections.Generic, System.IO) are written out in full below.
internal static class ManagementExtensions
{
    public static string InLocalAppData(this string path1, string path2 = "")
    {
        return System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "RetroBar", path1, path2);
    }

    public static void AddFrom(this System.Collections.Generic.List<string> list, string path, string extension, bool skipExisting = false)
    {
        if (!System.IO.Directory.Exists(path))
        {
            return;
        }

        foreach (string file in System.IO.Directory.GetFiles(path, $"*.{extension}"))
        {
            string fileName = System.IO.Path.GetFileNameWithoutExtension(file);
            if (skipExisting && list.Contains(fileName))
            {
                continue;
            }
            list.Add(fileName);
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Extensions/ManagementExtensions.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Utilities/ExePath.cs">
namespace RetroBar.Utilities
{
    using System;
    using System.Text;

    class ExePath
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern uint GetModuleFileName(IntPtr hModule, StringBuilder lpFilename, int nSize);
        static readonly int MAX_PATH = 260;

        internal static string GetExecutablePath()
        {
            var sb = new StringBuilder(MAX_PATH);
            GetModuleFileName(IntPtr.Zero, sb, MAX_PATH);
            return sb.ToString();
        }
	}
}
// <RETROSTAR_SOURCE_END path="RetroBar/Utilities/ExePath.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Utilities/ManagedShellLogger.cs">
namespace RetroBar.Utilities
{
    using ManagedShell.Common.Logging;
    using ManagedShell.Common.Logging.Observers;
    using System;
    using System.IO;

    class ManagedShellLogger : IDisposable
    {
        private string _logPath = "Logs".InLocalAppData();
        private string _logName = DateTime.Now.ToString("yyyy-MM-dd_HHmmssfff");
        private string _logExt = "log";
        private TimeSpan _logRetention = new TimeSpan(7, 0, 0);
        private FileLog _fileLog;

        public ManagedShellLogger()
        {
            SetupLogging();
            Settings.Instance.PropertyChanged += Settings_PropertyChanged;
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.DebugLogging))
            {
                SetSeverity();
            }
        }

        private void SetSeverity()
        {
            // Handle null settings instance in case of an error while initializing settings
            ShellLogger.Severity = Settings.Instance?.DebugLogging == true ? LogSeverity.Debug : LogSeverity.Info;
        }

        private void SetupLogging()
        {
            SetSeverity();

            SetupFileLog();

            ShellLogger.Attach(new ConsoleLog(), true);
        }

        private void SetupFileLog()
        {
            DeleteOldLogFiles();

            try
            {
                _fileLog = new FileLog(Path.Combine(_logPath, $"{_logName}.{_logExt}"));
                _fileLog?.Open();

                ShellLogger.Attach(_fileLog);
            }
            catch (Exception ex)
            {
                ShellLogger.Error($"Unable to open and attach file logger: {ex.Message}");
            }
        }

        private void DeleteOldLogFiles()
        {
            try
            {
                if (!Directory.Exists(_logPath))
                {
                    // Nothing to delete
                    return;
                }

                // look for all of the log files
                DirectoryInfo info = new DirectoryInfo(_logPath);
                FileInfo[] files = info.GetFiles($"*.{_logExt}", SearchOption.TopDirectoryOnly);

                // delete any files that are older than the retention period
                DateTime now = DateTime.Now;
                foreach (FileInfo file in files)
                {
                    if (now.Subtract(file.LastWriteTime) > _logRetention)
                    {
                        file.Delete();
                    }
                }
            }
            catch (Exception ex)
            {
                ShellLogger.Debug($"Unable to delete old log files: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
            _fileLog?.Dispose();
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Utilities/ManagedShellLogger.cs">

// [1100] SETTINGS :: every setting, its default, and settings.json persistence

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Utilities/Settings.cs">
namespace RetroBar.Utilities
{
    using ManagedShell.AppBar;
    using ManagedShell.Common.Helpers;
    using ManagedShell.WindowsTray;
    using RetroBar.StartMenu;
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Runtime.CompilerServices;

    internal class Settings : INotifyPropertyChanged, IMigratableSettings
    {
        private static Settings instance;

        public static Settings Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = _settingsManager.Settings;
                    _isInitializing = false;
                }

                return instance;
            }
        }

        private static string _settingsPath = "settings.json".InLocalAppData();
        private static bool _isInitializing = true;
        private static SettingsManager<Settings> _settingsManager = new(_settingsPath, new Settings());

        private bool _migrationPerformed = false;
        public bool MigrationPerformed { get => _migrationPerformed; }
        public event PropertyChangedEventHandler PropertyChanged;

        // This should not be used directly! Unfortunately it must be public for JsonSerializer.
        public Settings()
        {
            PropertyChanged += Settings_PropertyChanged;
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            _settingsManager.Settings = this;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected void Set<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (!field.Equals(value))
            {
                field = value;
                OnPropertyChanged(propertyName);
            }
        }

        protected void SetEnum<T>(ref T field, T value, [CallerMemberName] string propertyName = "") where T : Enum
        {
            if (!field.Equals(value))
            {
                if (!Enum.IsDefined(typeof(T), value))
                {
                    return;
                }

                field = value;
                OnPropertyChanged(propertyName);
            }
        }

        #region Properties
        private string _language = "System";
        public string Language
        {
            get => _language;
            set => Set(ref _language, value);
        }

        private string _theme = "Windows 95-98";
        public string Theme
        {
            get => _theme;
            set => Set(ref _theme, value);
        }

        private bool _showInputLanguage = false;
        public bool ShowInputLanguage
        {
            get => _showInputLanguage;
            set => Set(ref _showInputLanguage, value);
        }

        private bool _showClock = true;
        public bool ShowClock
        {
            get => _showClock;
            set => Set(ref _showClock, value);
        }

        private bool _overrideClockFormat = false;
        public bool OverrideClockFormat
        {
            get
            {
                return _overrideClockFormat;
            }
            set
            {
                if (_overrideClockFormat != value)
                {
                    _overrideClockFormat = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _clockFormat = "ddd MMM d  h:mm:ss tt";
        public string ClockFormat
        {
            get
            {
                return _clockFormat;
            }
            set
            {
                if (_clockFormat != value)
                {
                    _clockFormat = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _overrideAMPMDesignators = false;
        public bool OverrideAMPMDesignators
        {
            get
            {
                return _overrideAMPMDesignators;
            }
            set
            {
                if (_overrideAMPMDesignators != value)
                {
                    _overrideAMPMDesignators = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _amDesignator = "a.m.";
        public string AMDesignator
        {
            get
            {
                return _amDesignator;
            }
            set
            {
                if (_amDesignator != value)
                {
                    _amDesignator = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _pmDesignator = "p.m.";
        public string PMDesignator
        {
            get
            {
                return _pmDesignator;
            }
            set
            {
                if (_pmDesignator != value)
                {
                    _pmDesignator = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _showDesktopButton = false;
        public bool ShowDesktopButton
        {
            get => _showDesktopButton;
            set => Set(ref _showDesktopButton, value);
        }

        private bool _peekAtDesktop = false;
        public bool PeekAtDesktop
        {
            get => _peekAtDesktop;
            set => Set(ref _peekAtDesktop, value);
        }

        private bool _showMultiMon = false;
        public bool ShowMultiMon
        {
            get => _showMultiMon;
            set => Set(ref _showMultiMon, value);
        }

        private bool _showQuickLaunch = true;
        public bool ShowQuickLaunch
        {
            get => _showQuickLaunch;
            set => Set(ref _showQuickLaunch, value);
        }

        private string _quickLaunchPath = "%appdata%\\Microsoft\\Internet Explorer\\Quick Launch\\User Pinned\\TaskBar";
        public string QuickLaunchPath
        {
            get => _quickLaunchPath;
            set => Set(ref _quickLaunchPath, value);
        }

        private bool _collapseNotifyIcons = false;
        public bool CollapseNotifyIcons
        {
            get => _collapseNotifyIcons;
            set => Set(ref _collapseNotifyIcons, value);
        }

        private List<string> _invertNotifyIcons = new List<string> { NotificationArea.HARDWARE_GUID, NotificationArea.UPDATE_GUID, NotificationArea.MICROPHONE_GUID, NotificationArea.LOCATION_GUID, NotificationArea.MEETNOW_GUID, NotificationArea.NETWORK_GUID, NotificationArea.POWER_GUID, NotificationArea.VOLUME_GUID };
        public List<string> InvertNotifyIcons
        {
            get => _invertNotifyIcons;
            set => Set(ref _invertNotifyIcons, value);
        }

        private List<NotifyIconBehaviorSetting> _notifyIconBehaviors = new List<NotifyIconBehaviorSetting>
        {
            new NotifyIconBehaviorSetting
            {
                Identifier = NotificationArea.HEALTH_GUID,
                Behavior = NotifyIconBehavior.AlwaysShow
            },
            new NotifyIconBehaviorSetting
            {
                Identifier = NotificationArea.POWER_GUID,
                Behavior = NotifyIconBehavior.AlwaysShow
            },
            new NotifyIconBehaviorSetting
            {
                Identifier = NotificationArea.NETWORK_GUID,
                Behavior = NotifyIconBehavior.AlwaysShow
            },
            new NotifyIconBehaviorSetting
            {
                Identifier = NotificationArea.VOLUME_GUID,
                Behavior = NotifyIconBehavior.AlwaysShow
            },
        };
        public List<NotifyIconBehaviorSetting> NotifyIconBehaviors
        {
            get => _notifyIconBehaviors;
            set => Set(ref _notifyIconBehaviors, value);
        }

        private List<string> _notifyIconOrder = new List<string>();
        public List<string> NotifyIconOrder
        {
            get => _notifyIconOrder;
            set => Set(ref _notifyIconOrder, value);
        }

        private bool _allowFontSmoothing = false;
        public bool AllowFontSmoothing
        {
            get => _allowFontSmoothing;
            set => Set(ref _allowFontSmoothing, value);
        }

        private bool _allowFontSmoothingMenu = true;
        public bool AllowFontSmoothingMenu
        {
            get => _allowFontSmoothingMenu;
            set => Set(ref _allowFontSmoothingMenu, value);
        }

        private bool _useSoftwareRendering = false;
        public bool UseSoftwareRendering
        {
            get => _useSoftwareRendering;
            set => Set(ref _useSoftwareRendering, value);
        }

        private AppBarEdge _edge = AppBarEdge.Bottom;
        public AppBarEdge Edge
        {
            get => _edge;
            set => SetEnum(ref _edge, value);
        }

        private int _rowCount = 1;
        public int RowCount
        {
            get => _rowCount;
            set => Set(ref _rowCount, value);
        }

        private int _rowLimit = 5;
        public int RowLimit
        {
            get => _rowLimit;
            set => Set(ref _rowLimit, value);
        }

        private int _taskbarWidth = 1;
        public int TaskbarWidth
        {
            get => _taskbarWidth;
            set => Set(ref _taskbarWidth, value);
        }

        private int _taskbarWidthLimit = 7;
        public int TaskbarWidthLimit
        {
            get => _taskbarWidthLimit;
            set => Set(ref _taskbarWidthLimit, value);
        }

        private List<string> _quickLaunchOrder = [];
        public List<string> QuickLaunchOrder
        {
            get => _quickLaunchOrder;
            set => Set(ref _quickLaunchOrder, value);
        }

        private bool _showTaskThumbnails = false;
        public bool ShowTaskThumbnails
        {
            get => _showTaskThumbnails;
            set => Set(ref _showTaskThumbnails, value);
        }

        private MultiMonOption _multiMonMode = MultiMonOption.AllTaskbars;
        public MultiMonOption MultiMonMode
        {
            get => _multiMonMode;
            set => SetEnum(ref _multiMonMode, value);
        }

        private double _taskbarScale = 1.0;
        public double TaskbarScale
        {
            get => _taskbarScale;
            set => Set(ref _taskbarScale, value);
        }

        private bool _debugLogging = false;
        public bool DebugLogging
        {
            get => _debugLogging;
            set => Set(ref _debugLogging, value);
        }

        private bool _autoHide = false;
        public bool AutoHide
        {
            get => _autoHide;
            set => Set(ref _autoHide, value);
        }

        private bool _lockTaskbar = false;
        public bool LockTaskbar
        {
            get => _lockTaskbar;
            set => Set(ref _lockTaskbar, value);
        }

        private InvertIconsOption _invertIconsMode = EnvironmentHelper.IsWindows10OrBetter ? InvertIconsOption.WhenNeededByTheme : InvertIconsOption.Never;
        public InvertIconsOption InvertIconsMode
        {
            get => _invertIconsMode;
            set => SetEnum(ref _invertIconsMode, value);
        }

        private bool _showTaskBadges = true;
        public bool ShowTaskBadges
        {
            get => _showTaskBadges;
            set => Set(ref _showTaskBadges, value);
        }

        private TaskMiddleClickOption _taskMiddleClickAction = TaskMiddleClickOption.OpenNewInstance;
        public TaskMiddleClickOption TaskMiddleClickAction
        {
            get => _taskMiddleClickAction;
            set => SetEnum(ref _taskMiddleClickAction, value);
        }

        private TaskWheelActionOption _taskWheelAction = TaskWheelActionOption.DoNothing;
        public TaskWheelActionOption TaskWheelAction
        {
            get => _taskWheelAction;
            set => SetEnum(ref _taskWheelAction, value);
        }

        private ClockClickOption _clockClickAction = EnvironmentHelper.IsWindows10OrBetter ? ClockClickOption.OpenNotificationCenter : ClockClickOption.DoNothing;
        public ClockClickOption ClockClickAction
        {
            get
            {
                // On Windows versions prior to 10, neither the Modern calendar nor the Notification Center is available
                if (!EnvironmentHelper.IsWindows10OrBetter && _clockClickAction > ClockClickOption.OpenAeroCalendar)
                {
                    return ClockClickOption.DoNothing;
                }

                return _clockClickAction;
            }
            set => SetEnum(ref _clockClickAction, value);
        }

        private bool _checkForUpdates = false;
        public bool CheckForUpdates
        {
            get => _checkForUpdates;
            set => Set(ref _checkForUpdates, value);
        }

        private bool _showExitMenuItem = true;
        public bool ShowExitMenuItem
        {
            get => _showExitMenuItem;
            set => Set(ref _showExitMenuItem, value);
        }

        private bool _showEndTaskButton = false;
        public bool ShowEndTaskButton
        {
            get => _showEndTaskButton;
            set => Set(ref _showEndTaskButton, value);
        }

        private bool _showStartButtonMultiMon = false;
        public bool ShowStartButtonMultiMon
        {
            get => _showStartButtonMultiMon;
            set => Set(ref _showStartButtonMultiMon, value);
        }

        private bool _autoHideTransparent = false;
        public bool AutoHideTransparent
        {
            get => _autoHideTransparent;
            set => Set(ref _autoHideTransparent, value);
        }

        private bool _slideTaskbarButtons = false;
        public bool SlideTaskbarButtons
        {
            get => _slideTaskbarButtons;
            set => Set(ref _slideTaskbarButtons, value);
        }

        private bool _showClockSeconds = false;
        public bool ShowClockSeconds
        {
            get => _showClockSeconds;
            set => Set(ref _showClockSeconds, value);
        }

        private WinNumHotkeysOption _winNumHotkeysAction = WinNumHotkeysOption.WindowsDefault;
        public WinNumHotkeysOption WinNumHotkeysAction
        {
            get => _winNumHotkeysAction;
            set => SetEnum(ref _winNumHotkeysAction, value);
        }

        #region Start menu

        private bool _useVistaStartMenu = true;
        public bool UseVistaStartMenu
        {
            get => _useVistaStartMenu;
            set => Set(ref _useVistaStartMenu, value);
        }

        private bool _startMenuOpenWithWindowsKey = true;
        public bool StartMenuOpenWithWindowsKey
        {
            get => _startMenuOpenWithWindowsKey;
            set => Set(ref _startMenuOpenWithWindowsKey, value);
        }

        private int _startMenuRecentCount = 9;
        public int StartMenuRecentCount
        {
            get => _startMenuRecentCount;
            set => Set(ref _startMenuRecentCount, Math.Clamp(value, 0, 20));
        }

        private bool _startMenuLargeIcons = true;
        public bool StartMenuLargeIcons
        {
            get => _startMenuLargeIcons;
            set => Set(ref _startMenuLargeIcons, value);
        }

        private bool _startMenuShowSearch = true;
        public bool StartMenuShowSearch
        {
            get => _startMenuShowSearch;
            set => Set(ref _startMenuShowSearch, value);
        }

        private bool _startMenuSearchFiles = true;
        public bool StartMenuSearchFiles
        {
            get => _startMenuSearchFiles;
            set => Set(ref _startMenuSearchFiles, value);
        }

        private bool _startMenuUseWindowsUsage = true;
        public bool StartMenuUseWindowsUsage
        {
            get => _startMenuUseWindowsUsage;
            set => Set(ref _startMenuUseWindowsUsage, value);
        }

        private StartMenuPowerAction _startMenuPowerButtonAction = StartMenuPowerAction.Sleep;
        public StartMenuPowerAction StartMenuPowerButtonAction
        {
            get => _startMenuPowerButtonAction;
            set => SetEnum(ref _startMenuPowerButtonAction, value);
        }

        private List<string> _startMenuPinnedPrograms = new List<string> { StartMenuLauncher.DefaultBrowserKey, StartMenuLauncher.DefaultEmailKey };
        public List<string> StartMenuPinnedPrograms
        {
            get => _startMenuPinnedPrograms;
            set => Set(ref _startMenuPinnedPrograms, value);
        }

        private List<string> _startMenuRecentPrograms = new List<string>();
        public List<string> StartMenuRecentPrograms
        {
            get => _startMenuRecentPrograms;
            set => Set(ref _startMenuRecentPrograms, value);
        }

        private List<string> _startMenuHiddenPrograms = new List<string>();
        /// <summary>Programs removed from the recent list with "Remove from this list".</summary>
        public List<string> StartMenuHiddenPrograms
        {
            get => _startMenuHiddenPrograms;
            set => Set(ref _startMenuHiddenPrograms, value);
        }

        private bool _startMenuShowUserFolder = true;
        public bool StartMenuShowUserFolder
        {
            get => _startMenuShowUserFolder;
            set => Set(ref _startMenuShowUserFolder, value);
        }

        private bool _startMenuShowDocuments = true;
        public bool StartMenuShowDocuments
        {
            get => _startMenuShowDocuments;
            set => Set(ref _startMenuShowDocuments, value);
        }

        private bool _startMenuShowPictures = true;
        public bool StartMenuShowPictures
        {
            get => _startMenuShowPictures;
            set => Set(ref _startMenuShowPictures, value);
        }

        private bool _startMenuShowMusic = true;
        public bool StartMenuShowMusic
        {
            get => _startMenuShowMusic;
            set => Set(ref _startMenuShowMusic, value);
        }

        private bool _startMenuShowDownloads = false;
        public bool StartMenuShowDownloads
        {
            get => _startMenuShowDownloads;
            set => Set(ref _startMenuShowDownloads, value);
        }

        private bool _startMenuShowSearchLink = true;
        public bool StartMenuShowSearchLink
        {
            get => _startMenuShowSearchLink;
            set => Set(ref _startMenuShowSearchLink, value);
        }

        private bool _startMenuShowRecentItems = true;
        public bool StartMenuShowRecentItems
        {
            get => _startMenuShowRecentItems;
            set => Set(ref _startMenuShowRecentItems, value);
        }

        private bool _startMenuShowComputer = true;
        public bool StartMenuShowComputer
        {
            get => _startMenuShowComputer;
            set => Set(ref _startMenuShowComputer, value);
        }

        private bool _startMenuShowNetwork = true;
        public bool StartMenuShowNetwork
        {
            get => _startMenuShowNetwork;
            set => Set(ref _startMenuShowNetwork, value);
        }

        private bool _startMenuShowConnectTo = true;
        public bool StartMenuShowConnectTo
        {
            get => _startMenuShowConnectTo;
            set => Set(ref _startMenuShowConnectTo, value);
        }

        private bool _startMenuShowControlPanel = true;
        public bool StartMenuShowControlPanel
        {
            get => _startMenuShowControlPanel;
            set => Set(ref _startMenuShowControlPanel, value);
        }

        private bool _startMenuShowDefaultPrograms = true;
        public bool StartMenuShowDefaultPrograms
        {
            get => _startMenuShowDefaultPrograms;
            set => Set(ref _startMenuShowDefaultPrograms, value);
        }

        private bool _startMenuShowRun = false;
        public bool StartMenuShowRun
        {
            get => _startMenuShowRun;
            set => Set(ref _startMenuShowRun, value);
        }

        private StartMenuStyleOption _startMenuStyle = StartMenuStyleOption.Vista;
        /// <summary>Whether the Start menu is drawn as Windows Vista's or Windows 7's.</summary>
        public StartMenuStyleOption StartMenuStyle
        {
            get => _startMenuStyle;
            set => SetEnum(ref _startMenuStyle, value);
        }

        private StartMenuPowerAction _windows7PowerButtonAction = StartMenuPowerAction.ShutDown;
        /// <summary>Action of the Windows 7 Start menu's power button; Windows 7 shuts down by default.</summary>
        public StartMenuPowerAction Windows7PowerButtonAction
        {
            get => _windows7PowerButtonAction;
            set => SetEnum(ref _windows7PowerButtonAction, value);
        }

        private bool _startMenuShowDevicesAndPrinters = true;
        /// <summary>Devices and Printers link, shown by the Windows 7 Start menu only.</summary>
        public bool StartMenuShowDevicesAndPrinters
        {
            get => _startMenuShowDevicesAndPrinters;
            set => Set(ref _startMenuShowDevicesAndPrinters, value);
        }

        #endregion

        #region Superbar

        private List<string> _taskbarPinnedItems = new List<string>();
        /// <summary>
        /// Programs pinned to the Windows 7 taskbar, in order, as "file:" (shortcut or program path)
        /// or "app:" (Apps folder ID) keys, the same keys the Start menu uses.
        /// </summary>
        public List<string> TaskbarPinnedItems
        {
            get => _taskbarPinnedItems;
            set => Set(ref _taskbarPinnedItems, value);
        }

        private bool _taskbarPinsImported = false;
        /// <summary>Whether Windows' own taskbar pins have been copied into TaskbarPinnedItems once.</summary>
        public bool TaskbarPinsImported
        {
            get => _taskbarPinsImported;
            set => Set(ref _taskbarPinsImported, value);
        }

        #endregion

        private bool _enableAeroGlass = true;
        public bool EnableAeroGlass
        {
            get => _enableAeroGlass;
            set => Set(ref _enableAeroGlass, value);
        }

        private int _aeroGlassTransparency = 0;
        /// <summary>
        /// How much of the glass tint is taken away, from 0 (the tint each theme was measured with) to 100 (the blur
        /// alone). Governs every glass surface through AeroGlassHelper.TintOpacity.
        /// </summary>
        public int AeroGlassTransparency
        {
            get => _aeroGlassTransparency;
            set => Set(ref _aeroGlassTransparency, Math.Clamp(value, 0, 100));
        }

        private bool _allowBlurBehind = true;
        public bool AllowBlurBehind
        {
            get => _allowBlurBehind;
            set => Set(ref _allowBlurBehind, value);
        }
        
        private bool _allowAlternateMaximizedAppearance = false;
        public bool AllowAlternateMaximizedAppearance
        {
          get => _allowAlternateMaximizedAppearance;
          set => Set(ref _allowAlternateMaximizedAppearance, value);
        }
        #endregion
        
        #region Old Properties
        public bool? MiddleMouseToClose
        {
            get => null;
            set
            {
                // Migrate to TaskMiddleClickAction
                if (value != null)
                {
                    TaskMiddleClickAction = (bool)value ? TaskMiddleClickOption.CloseTask : TaskMiddleClickOption.OpenNewInstance;
                    _migrationPerformed = true;
                }
            }
        }

        public string[] PinnedNotifyIcons
        {
            get => [];
            set
            {
                // Migrate to NotifyIconBehaviors
                if (value.Length > 0)
                {
                    var newSettings = new List<NotifyIconBehaviorSetting>();

                    foreach (var identifier in value)
                    {
                        newSettings.Add(new NotifyIconBehaviorSetting
                        {
                            Identifier = identifier,
                            Behavior = NotifyIconBehavior.AlwaysShow
                        });
                    }

                    NotifyIconBehaviors = newSettings;
                    _migrationPerformed = true;
                }
            }
        }
        #endregion
    }

    #region Enums
    public enum InvertIconsOption
    {
        WhenNeededByTheme,
        Always,
        Never
    }

    public enum MultiMonOption
    {
        AllTaskbars,
        SameAsWindow,
        SameAsWindowAndPrimary
    }

    public enum TaskMiddleClickOption
    {
        DoNothing,
        OpenNewInstance,
        CloseTask
    }
    public enum TaskWheelActionOption
    {
        DoNothing,
        ShowHideWindow
    }

    public enum ClockClickOption
    {
        DoNothing,
        OpenAeroCalendar,
        OpenModernCalendar,
        OpenNotificationCenter,
    }

    public enum WinNumHotkeysOption
    {
        WindowsDefault,
        SwitchTasks,
        InvokeQuickLaunch,
    }

    public enum NotifyIconBehavior
    {
        HideWhenInactive,
        AlwaysHide,
        AlwaysShow,
        Remove
    }

    public enum StartMenuStyleOption
    {
        Vista,
        Windows7
    }
    #endregion

    #region Structs
    public struct NotifyIconBehaviorSetting
    {
        public string Identifier {  get; set; }
        public NotifyIconBehavior Behavior { get; set; }
    }
    #endregion
}
// <RETROSTAR_SOURCE_END path="RetroBar/Utilities/Settings.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Utilities/SettingsManager.cs">
namespace RetroBar.Utilities
{
    using ManagedShell.Common.Helpers;
    using ManagedShell.Common.Logging;
    using System;
    using System.ComponentModel;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text.Json;

    public class SettingsManager<T> : INotifyPropertyChanged where T : IMigratableSettings
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private static JsonSerializerOptions options = new()
        {
            IgnoreReadOnlyProperties = true,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private string _fileName;

        private T _settings;
        public T Settings
        {
            get => _settings;
            set
            {
                _settings = value;
                OnPropertyChanged();
                SaveToFile();
            }
        }

        public SettingsManager(string fileName, T defaultSettings)
        {
            _fileName = fileName;
            _settings = defaultSettings;

            if (!LoadFromFile())
            {
                ShellLogger.Info("SettingsManager: Using default settings");
            }
        }

        private bool LoadFromFile()
        {
            try
            {
                if (!ShellHelper.Exists(_fileName))
                {
                    return false;
                }

                string jsonString = File.ReadAllText(_fileName);
                _settings = JsonSerializer.Deserialize<T>(jsonString);

                if (_settings.MigrationPerformed)
                {
                    // Save post-migration state so that we don't need to migrate every startup
                    SaveToFile();
                }

                return true;
            }
            catch (Exception ex)
            {
                ShellLogger.Error($"SettingsManager: Error loading settings file: {ex.Message}");
                return false;
            }
        }

        private void SaveToFile()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_fileName));

                string jsonString = JsonSerializer.Serialize(Settings, options);
                File.WriteAllText(_fileName, jsonString);
            }
            catch (Exception ex)
            {
                ShellLogger.Error($"SettingsManager: Error saving settings file: {ex.Message}");
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public interface IMigratableSettings
    {
        public bool MigrationPerformed { get; }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Utilities/SettingsManager.cs">

// [1200] THEMES + LANGUAGES :: loading theme and language resource dictionaries by name

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Utilities/DictionaryManager.cs">
namespace RetroBar.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
    using System.Windows;

    public class DictionaryManager : IDisposable
    {
        private const string DICT_DEFAULT = "System";
        private const string DICT_EXT = "xaml";

        private const string LANG_DEFAULT = DICT_DEFAULT;
        private const string LANG_FALLBACK = "English";
        private const string LANG_FOLDER = "Languages";
        private const string LANG_EXT = DICT_EXT;

        public const string THEME_DEFAULT = DICT_DEFAULT;
        private const string THEME_FOLDER = "Themes";
        private const string THEME_EXT = DICT_EXT;

        public DictionaryManager()
        {
            Settings.Instance.PropertyChanged += Settings_PropertyChanged;
        }

        public void SetThemeFromSettings()
        {
            SetTheme(THEME_DEFAULT);

            if (Settings.Instance.Theme.StartsWith(THEME_DEFAULT))
            {
                SetSystemThemeParams();
            }
            else
            {
                ClearSystemThemeParams();
            }

            if (Settings.Instance.Theme != THEME_DEFAULT)
            {
                SetTheme(Settings.Instance.Theme);
            }
        }

        private void SetSystemThemeParams()
        {
            Application.Current.Resources["GlobalFontFamily"] = SystemFonts.CaptionFontFamily;
        }

        private void ClearSystemThemeParams()
        {
            Application.Current.Resources.Remove("GlobalFontFamily");
        }

        private void SetTheme(string theme)
        {
            SetDictionary(theme, THEME_FOLDER, THEME_DEFAULT, THEME_EXT, 0);
        }

        private static Collection<ResourceDictionary> GetMergedDictionaries()
        {
            return Application.Current.Resources.MergedDictionaries;
        }

        private static ResourceDictionary GetActualThemeDictionary()
        {
            foreach (ResourceDictionary rd in GetMergedDictionaries()
                .Where(rd => rd.Source.ToString().Contains($"{THEME_FOLDER}/")))
            {
                return rd;
            }

            return null;
        }

        private void ClearPreviousThemes()
        {
            if (GetActualThemeDictionary() != null)
            {
                _ = GetMergedDictionaries().Remove(GetActualThemeDictionary());
            }
        }

        public void SetLanguageFromSettings()
        {
            SetLanguage(LANG_FALLBACK);
            if (Settings.Instance.Language == LANG_DEFAULT)
            {
                var currentUICulture = System.Globalization.CultureInfo.CurrentUICulture;
                string systemLanguageParent = currentUICulture.Parent.NativeName;
                string systemLanguage = currentUICulture.NativeName;
                ManagedShell.Common.Logging.ShellLogger.Info
                    ($"Loading system language (if available): {systemLanguageParent}, {systemLanguage}");
                SetLanguage(systemLanguageParent);
                SetLanguage(systemLanguage);
            }
            else
            {
                SetLanguage(Settings.Instance.Language);
            }
        }

        private void SetLanguage(string language)
        {
            SetDictionary(language, LANG_FOLDER, LANG_FALLBACK, LANG_EXT, 1);
        }

        private void SetDictionary(string dictionary, string dictFolder, string dictDefault, string dictExtension, int dictType)
        {
            string dictFilePath;

            if (dictionary == dictDefault)
            {
                if (dictType == 0)
                {
                    ClearPreviousThemes();
                }
                dictFilePath = Path.ChangeExtension(Path.Combine(dictFolder, dictDefault), dictExtension);
            }
            else
            {
                // Built-in dictionary
                dictFilePath = Path.ChangeExtension(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dictFolder, dictionary),
                                                    dictExtension);

                if (!File.Exists(dictFilePath))
                {
                    // Installed dictionary in AppData directory
                    dictFilePath = Path.ChangeExtension(dictFolder.InLocalAppData(dictionary), dictExtension);

                    if (!File.Exists(dictFilePath))
                    {
                        // Custom dictionary in app directory
                        dictFilePath =
                            Path.ChangeExtension(Path.Combine(Path.GetDirectoryName(ExePath.GetExecutablePath()), dictFolder, dictionary),
                                                 dictExtension);

                        if (!File.Exists(dictFilePath))
                        {
                            return;
                        }
                    }
                }
            }

            try
            {
                GetMergedDictionaries().Add(new ResourceDictionary()
                {
                    Source = new Uri(dictFilePath, UriKind.RelativeOrAbsolute)
                });
            }
            catch (Exception e)
            {
                ManagedShell.Common.Logging.ShellLogger.Error($"Error loading dictionaries: {e.Message} {e.InnerException?.Message}");
            }
        }

        public string GetThemeInstallDir()
        {
            return THEME_FOLDER.InLocalAppData();
        }

        public List<string> GetThemes()
        {
            return GetDictionaries(THEME_DEFAULT, THEME_FOLDER, THEME_EXT);
        }

        public List<string> GetLanguages()
        {
            List<string> languages = new List<string> { LANG_DEFAULT };
            languages.AddRange(GetDictionaries(LANG_FALLBACK, LANG_FOLDER, LANG_EXT));
            return languages;
        }

        private List<string> GetDictionaries(string dictDefault, string dictFolder, string dictExtension)
        {
            List<string> dictionaries = new List<string> { dictDefault };

            // Built-in dictionaries
            dictionaries.AddFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dictFolder), dictExtension);

            // Installed AppData dictionaries
            dictionaries.AddFrom(dictFolder.InLocalAppData(), dictExtension);

            // Same-folder dictionaries
            // Because RetroBar is published as a single-file app, it gets extracted to a temp directory, so custom dictionaries won't be there.
            // Get the executable path to find the custom dictionaries directory when not a debug build.
            dictionaries.AddFrom(Path.Combine(Path.GetDirectoryName(ExePath.GetExecutablePath()), dictFolder), dictExtension, true);

            return dictionaries;
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.Language))
            {
                SetLanguageFromSettings();
            }
            if (e.PropertyName == nameof(Settings.Theme))
            {
                SetThemeFromSettings();
            }
        }

        public void Dispose()
        {
            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Utilities/DictionaryManager.cs">

// [1300] SHELL INTEGRATION :: taskbar windows per screen, Explorer, the Windows Start menu, updates, clock flyout, volume

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Utilities/WindowManager.cs">
namespace RetroBar.Utilities
{
    using ManagedShell;
    using ManagedShell.AppBar;
    using ManagedShell.Common.Logging;
    using System;
    using System.Collections.Generic;
    using System.Windows.Forms;

    public class WindowManager : IDisposable
    {
        private static object reopenLock = new object();

        private bool _isSettingDisplays;
        private int _pendingDisplayEvents;
        private List<AppBarScreen> _screenState = new List<AppBarScreen>();
        private List<Taskbar> _taskbars = new List<Taskbar>();

        private readonly DictionaryManager _dictionaryManager;
        private readonly ExplorerMonitor _explorerMonitor;
        private readonly StartMenuMonitor _startMenuMonitor;
        private readonly ShellManager _shellManager;
        private readonly Updater _updater;
        private HotkeyManager _hotkeyManager;

        public WindowManager(DictionaryManager dictionaryManager, ExplorerMonitor explorerMonitor, ShellManager shellManager, StartMenuMonitor startMenuMonitor, Updater updater, HotkeyManager hotkeyManager)
        {
            _dictionaryManager = dictionaryManager;
            _explorerMonitor = explorerMonitor;
            _shellManager = shellManager;
            _startMenuMonitor = startMenuMonitor;
            _updater = updater;
            _hotkeyManager = hotkeyManager;

            _shellManager.ExplorerHelper.HideExplorerTaskbar = true;

            openTaskbars();

            _explorerMonitor.ExplorerMonitorStart(this, _shellManager);

            Settings.Instance.PropertyChanged += Settings_PropertyChanged;
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.ShowMultiMon))
            {
                // Update screen state in case it has changed since last checked
                _screenState = AppBarScreen.FromAllScreens();

                if (_screenState.Count < 2)
                {
                    return;
                }

                ReopenTaskbars();
            }
        }

        public void ReopenTaskbars()
        {
            lock (reopenLock)
            {
                closeTaskbars();
                openTaskbars();
            }
        }

        public void NotifyWorkAreaChange()
        {
            ShellLogger.Debug($"WindowManager: Work area change notification received");
            handleDisplayChange();
        }

        public void NotifyDisplayChange(ScreenSetupReason reason)
        {
            ShellLogger.Debug($"WindowManager: Display change notification received ({reason})");
            handleDisplayChange();
        }

        private void handleDisplayChange()
        {
            _pendingDisplayEvents++;

            if (_isSettingDisplays)
            {
                return;
            }

            _isSettingDisplays = true;

            while (_pendingDisplayEvents > 0)
            {
                // Skip re-opening taskbars if the screens haven't changed
                if (!haveDisplaysChanged())
                {
                    _pendingDisplayEvents--;
                    continue;
                }

                ReopenTaskbars();

                _pendingDisplayEvents--;
            }

            _isSettingDisplays = false;
            ShellLogger.Debug($"WindowManager: Finished processing display events");
        }

        public bool IsValidHMonitor(IntPtr hMonitor)
        {
            foreach(var screen in _screenState)
            {
                if (screen.HMonitor == hMonitor)
                {
                    return true;
                }
            }

            return false;
        }

        public Taskbar GetMonitorTaskbar(IntPtr hMonitor)
        {
            foreach (var taskbar in _taskbars)
            {
                if (taskbar.Screen.HMonitor == hMonitor)
                {
                    return taskbar;
                }
            }

            return null;
        }

        public Taskbar GetPrimaryTaskbar()
        {
            foreach (var taskbar in _taskbars)
            {
                if (taskbar.Screen.Primary)
                {
                    return taskbar;
                }
            }

            return null;
        }

        private void closeTaskbars()
        {
            ShellLogger.Debug($"WindowManager: Closing all taskbars");

            foreach (var taskbar in _taskbars)
            {
                taskbar.AllowClose = true;
                taskbar.Close();
            }

            _taskbars.Clear();
        }

        private void openTaskbars()
        {
            _screenState = AppBarScreen.FromAllScreens();

            ShellLogger.Debug($"WindowManager: Opening taskbars");

            if (Settings.Instance.ShowMultiMon)
            {
                foreach (var screen in _screenState)
                {
                    openTaskbar(screen);
                }
            }
            else
            {
                openTaskbar(AppBarScreen.FromPrimaryScreen());
            }
        }

        private void openTaskbar(AppBarScreen screen)
        {
            ShellLogger.Debug($"WindowManager: Opening taskbar on screen {screen.DeviceName}");
            Taskbar taskbar = new Taskbar(this, _dictionaryManager, _shellManager, _startMenuMonitor, _updater, _hotkeyManager, screen, Settings.Instance.Edge, Settings.Instance.AutoHide ? AppBarMode.AutoHide : AppBarMode.Normal);
            taskbar.Show();

            _taskbars.Add(taskbar);
        }

        private bool haveDisplaysChanged()
        {
            resetScreenCache();

            var newScreens = AppBarScreen.FromAllScreens();

            if (_screenState.Count == newScreens.Count)
            {
                bool same = true;
                for (int i = 0; i < newScreens.Count; i++)
                {
                    AppBarScreen current = newScreens[i];
                    if (!(_screenState[i].Bounds == current.Bounds && _screenState[i].DeviceName == current.DeviceName && _screenState[i].Primary == current.Primary && _screenState[i].HMonitor == current.HMonitor))
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                {
                    ShellLogger.Debug("WindowManager: No display changes");
                    return false;
                }
            }

            return true;
        }

        private void resetScreenCache()
        {
            // use reflection to empty screens cache
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var fi = typeof(Screen).GetField("screens", flags) ?? typeof(Screen).GetField("s_screens", flags)
                ?? throw new Exception("Can't find & reset screens cache inside winforms");
            fi.SetValue(null, null);
        }

        public void Dispose()
        {
            _shellManager.ExplorerHelper.HideExplorerTaskbar = false;
            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Utilities/WindowManager.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Utilities/ExplorerMonitor.cs">
namespace RetroBar.Utilities
{
    using ManagedShell;
    using ManagedShell.Common.Helpers;
    using ManagedShell.Common.Logging;
    using ManagedShell.Interop;
    using System;
    using System.Windows.Forms;
    using System.Windows.Threading;

    public class ExplorerMonitor : IDisposable
    {
        private ExplorerMonitorWindow _explorerMonitorWindow;

        public void ExplorerMonitorStart(WindowManager windowManagerRef, ShellManager shellManager)
        {
            if (_explorerMonitorWindow != null) { return; } // Prevent multiple monitors.

            _explorerMonitorWindow = new ExplorerMonitorWindow(windowManagerRef, shellManager); // Start monitoring.
        }

        public void Dispose()
        {
            _explorerMonitorWindow?.Dispose();
        }

        private class ExplorerMonitorWindow : NativeWindow, IDisposable
        {
            private readonly ShellManager _shellManager;
            private readonly WindowManager _windowManagerRef;
            private static readonly int WM_TASKBARCREATEDMESSAGE = NativeMethods.RegisterWindowMessage("TaskbarCreated");

            public ExplorerMonitorWindow(WindowManager windowManagerRef, ShellManager shellManager)
            {
                _shellManager = shellManager;
                _windowManagerRef = windowManagerRef;
                CreateHandle(new CreateParams());
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_TASKBARCREATEDMESSAGE)
                {
                    Dispatcher.CurrentDispatcher.BeginInvoke(() => {
                        try
                        {
                            if (EnvironmentHelper.IsAppRunningAsShell)
                            {
                                // Re-evaluate shell status: some apps may delay the Shell process startup.
                                EnvironmentHelper.IsAppRunningAsShell = NativeMethods.GetShellWindow() == IntPtr.Zero;
                                // Trigger the hide taskbar logic again in case shell status has changed
                                _shellManager.ExplorerHelper.HideExplorerTaskbar = true;
                            }
                            // Reopen taskbars if explorer.exe is restarted.
                            _windowManagerRef.ReopenTaskbars();
                            // Re-initialize the tasks service to prevent leftover File Explorer windows.
                            _shellManager.Tasks.Dispose();
                            _shellManager.Tasks.Initialize(true);
                        }
                        catch (Exception ex)
                        {
                            ShellLogger.Warning($"Error handling TaskbarCreated message on ExplorerMonitor: {ex.Message}");
                        }
                    });
                }

                base.WndProc(ref m); // Call the base class to process other messages so we dont accidentally cause crashes or bugs.
            }

            public void Dispose()
            {
                DestroyHandle();
            }
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Utilities/ExplorerMonitor.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Utilities/StartMenuMonitor.cs">
namespace RetroBar.Utilities
{
    using ManagedShell.AppBar;
    using ManagedShell.Common.Helpers;
    using ManagedShell.Common.Logging;
    using ManagedShell.Common.SupportingClasses;
    using ManagedShell.UWPInterop;
    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Windows;
    using System.Windows.Threading;
    using static ManagedShell.Interop.NativeMethods;

    public class StartMenuMonitor : IDisposable
    {
        private AppVisibilityHelper _appVisibilityHelper;
        private WindowManager _windowManager;
        private DispatcherTimer _poller;
        private bool _isVisible;
        private IntPtr _taskbarHwndActivated;
        private IntPtr _hStartMenu;

        public event EventHandler<StartMenuMonitorEventArgs> StartMenuVisibilityChanged;

        public StartMenuMonitor(AppVisibilityHelper appVisibilityHelper)
        {
            _appVisibilityHelper = appVisibilityHelper;
            setupPoller();
        }

        private void setupPoller()
        {
            if (_poller == null)
            {
                _poller = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };

                _poller.Tick += poller_Tick;
            }

            _poller.Start();
        }

        private void poller_Tick(object sender, EventArgs e)
        {
            bool newIsVisible = false;
            IntPtr startHmonitor = IntPtr.Zero;

            if (EnvironmentHelper.IsWindows8OrBetter && isModernStartMenuOpen())
            {
                // Windows 8+
                newIsVisible = true;
                startHmonitor = hMonitorModernStartMenu();
            }

            if (!newIsVisible && isClassicStartMenuOpen())
            {
                // Windows 7, StartIsBack, Start8+
                newIsVisible = true;
                startHmonitor = hMonitorClassicStartMenu();
            }

            if (!newIsVisible && isOpenShellMenuOpen())
            {
                // Open Shell Menu
                newIsVisible = true;
                relocateStartMenuByClass("OpenShell.CMenuContainer", "OpenShell.CUserWindow");
                startHmonitor = hMonitorOpenShellMenu();
            }

            setVisibility(newIsVisible, startHmonitor);
        }

        private void setVisibility(bool isVisible, IntPtr startHmonitor)
        {
            if (isVisible == _isVisible)
            {
                return;
            }

            _isVisible = isVisible;

            StartMenuMonitorEventArgs args = new StartMenuMonitorEventArgs
            {
                Visible = _isVisible,
                TaskbarHwndActivated = _taskbarHwndActivated,
                StartHmonitor = startHmonitor
            };

            StartMenuVisibilityChanged?.Invoke(this, args);

            if (_taskbarHwndActivated != IntPtr.Zero)
            {
                // Now that it has been consumed, reset to prevent sending stale data
                // if the menu is opened again not by the start button.
                _taskbarHwndActivated = IntPtr.Zero;
            }
            if (!_isVisible && _hStartMenu != IntPtr.Zero)
            {
                _hStartMenu = IntPtr.Zero;
            }
        }

        private bool isModernStartMenuOpen()
        {
            if (_appVisibilityHelper == null)
            {
                ShellLogger.Error("StartMenuMonitor: AppVisibilityHelper is null");
                return false;
            }

            return _appVisibilityHelper.IsLauncherVisible();
        }

        private bool isClassicStartMenuOpen()
        {
            return isVisibleByClass("DV2ControlHost");
        }

        private bool isOpenShellMenuOpen()
        {
            return isVisibleByClass("OpenShell.CMenuContainer");
        }

        private bool isVisibleByClass(string className)
        {
            IntPtr hStartMenu = FindWindowEx(IntPtr.Zero, IntPtr.Zero, className, IntPtr.Zero);

            while (hStartMenu != IntPtr.Zero)
            {
                if (IsWindowVisible(hStartMenu))
                {
                    return true;
                }
                hStartMenu = FindWindowEx(IntPtr.Zero, hStartMenu, className, IntPtr.Zero);
            }

            return false;
        }

        private IntPtr hMonitorModernStartMenu()
        {
            IntPtr hwndForeground = GetForegroundWindow();
            StringBuilder cName = new StringBuilder(256);
            GetClassName(hwndForeground, cName, cName.Capacity);
            if (cName.ToString() == "Windows.UI.Core.CoreWindow")
            {
                // When the modern Start menu opens, it gains focus, so this is probably it
                return MonitorFromWindow(hwndForeground, MONITOR_DEFAULTTONEAREST);
            }
            return IntPtr.Zero;
        }

        private IntPtr hMonitorClassicStartMenu()
        {
            return hMonitorByClass("DV2ControlHost");
        }

        private IntPtr hMonitorOpenShellMenu()
        {
            return hMonitorByClass("OpenShell.CMenuContainer");
        }

        private IntPtr hMonitorByClass(string className)
        {
            IntPtr hStartMenu = FindWindowEx(IntPtr.Zero, IntPtr.Zero, className, IntPtr.Zero);

            if (hStartMenu == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            return MonitorFromWindow(hStartMenu, MONITOR_DEFAULTTONEAREST);
        }

        private void relocateStartMenuByClass(string className, string ownedClassName)
        {
            IntPtr hTaskbar = _taskbarHwndActivated;
            if (hTaskbar == IntPtr.Zero)
            {
                if (!_isVisible && hMonitorByClass(className) is IntPtr hMonitor && _windowManager?.GetMonitorTaskbar(hMonitor) is Taskbar monitorTaskbar)
                {
                    // Only bother doing this if the menu is currently opening
                    hTaskbar = monitorTaskbar.Handle;
                }
                else
                {
                    return;
                }
            }

            IntPtr hStartMenu = _hStartMenu;
            if (hStartMenu == IntPtr.Zero)
            {
                hStartMenu = FindWindowEx(IntPtr.Zero, IntPtr.Zero, className, IntPtr.Zero);
                if (hStartMenu == IntPtr.Zero)
                {
                    return;
                }
                // Remember this hStartMenu in case an additional window opens too with the same class
                _hStartMenu = hStartMenu;
            }
            // Get current window rects
            FlowDirection flowDirection = Application.Current.FindResource("flow_direction") as FlowDirection? ?? FlowDirection.LeftToRight;
            GetWindowRect(hStartMenu, out ManagedShell.Interop.NativeMethods.Rect startMenuRect);
            GetWindowRect(hTaskbar, out ManagedShell.Interop.NativeMethods.Rect taskbarRect);

            int x = 0, y = 0;
            switch (Settings.Instance.Edge)
            {
                case AppBarEdge.Left:
                    // Top right of taskbar is menu top left
                    x = taskbarRect.Right;
                    y = taskbarRect.Top;
                    break;
                case AppBarEdge.Right:
                    // Top left of taskbar is menu top right
                    x = taskbarRect.Left - startMenuRect.Width;
                    y = taskbarRect.Top;
                    break;
                case AppBarEdge.Top:
                    if (flowDirection == FlowDirection.LeftToRight)
                    {
                        // Bottom left of taskbar is menu top left
                        x = taskbarRect.Left;
                    }
                    else
                    {
                        // Bottom right of taskbar is menu top right
                        x = taskbarRect.Right - startMenuRect.Width;
                    }
                    y = taskbarRect.Bottom;
                    break;
                case AppBarEdge.Bottom:
                    if (flowDirection == FlowDirection.LeftToRight)
                    {
                        // Top left of taskbar is menu bottom left
                        x = taskbarRect.Left;
                    }
                    else
                    {
                        // Top right of taskbar is menu bottom right
                        x = taskbarRect.Right - startMenuRect.Width;
                    }
                    y = taskbarRect.Top - startMenuRect.Height;
                    break;
            }

            if (y != startMenuRect.Top || x != startMenuRect.Left)
            {
                SetWindowPos(hStartMenu, IntPtr.Zero, x, y, 0, 0, (int)(SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOZORDER));
            }

            // The menu may have an owned window (such as a user picture) which needs to be moved by the same amount as the menu
            if (string.IsNullOrEmpty(ownedClassName))
            {
                return;
            }
            IntPtr ownedHwnd = FindWindowEx(IntPtr.Zero, IntPtr.Zero, ownedClassName, IntPtr.Zero);
            if (ownedHwnd != IntPtr.Zero)
            {
                ManagedShell.Interop.NativeMethods.Rect ownedRect;
                GetWindowRect(ownedHwnd, out ownedRect);
                int offsetX = x - startMenuRect.Left;
                int offsetY = y - startMenuRect.Top;
                SetWindowPos(ownedHwnd, IntPtr.Zero, ownedRect.Left + offsetX, ownedRect.Top + offsetY, 0, 0, (int)(SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOZORDER));
            }
        }

        private IImmersiveMonitor GetImmersiveMonitor(ManagedShell.UWPInterop.Interfaces.IServiceProvider shell, IntPtr hWnd)
        {
            if (shell.QueryService(ref CLSID_ImmersiveMonitorManager, ref IID_ImmersiveMonitorManager, out object monitorManagerObj) != 0)
            {
                ShellLogger.Warning("StartMenuMonitor: Failed to query for IImmersiveMonitorManager");
                return null;
            }
            IImmersiveMonitorManager monitorManager = (IImmersiveMonitorManager)monitorManagerObj;

            if (monitorManager.GetFromHandle(MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST), out IImmersiveMonitor monitor) != 0)
            {
                ShellLogger.Warning("StartMenuMonitor: Failed to get monitor from taskbar window handle");
                return null;
            }

            return monitor;
        }

        private IImmersiveLauncher_Win10RS1 GetImmersiveLauncher_Win10RS1(IntPtr taskbarHwnd)
        {
            var shell = ImmersiveShellHelper.GetImmersiveShell();
            if (shell.QueryService(ref CLSID_ImmersiveLauncher, ref IID_ImmersiveLauncher_Win10RS1, out object immersiveLauncherObj) != 0)
            {
                ShellLogger.Warning("StartMenuMonitor: Failed to query for IImmersiveLauncher_Win10RS1");
                return null;
            }
            IImmersiveLauncher_Win10RS1 immersiveLauncher = (IImmersiveLauncher_Win10RS1)immersiveLauncherObj;

            IImmersiveMonitor monitor = GetImmersiveMonitor(shell, taskbarHwnd);
            if (monitor == null || immersiveLauncher.ConnectToMonitor(monitor) != 0)
            {
                ShellLogger.Warning("StartMenuMonitor: Failed to connect IImmersiveLauncher_Win10RS1 to monitor");
                return null;
            }

            return immersiveLauncher;
        }

        private IImmersiveLauncher_Win81 GetImmersiveLauncher_Win81(IntPtr taskbarHwnd)
        {
            var shell = ImmersiveShellHelper.GetImmersiveShell();
            if (shell.QueryService(ref CLSID_ImmersiveLauncher, ref IID_ImmersiveLauncher_Win81, out object immersiveLauncherObj) != 0)
            {
                ShellLogger.Warning("StartMenuMonitor: Failed to query for IImmersiveLauncher_Win81");
                return null;
            }
            IImmersiveLauncher_Win81 immersiveLauncher = (IImmersiveLauncher_Win81)immersiveLauncherObj;

            IImmersiveMonitor monitor = GetImmersiveMonitor(shell, taskbarHwnd);
            if (monitor == null || immersiveLauncher.ConnectToMonitor(monitor) != 0)
            {
                ShellLogger.Warning("StartMenuMonitor: Failed to connect IImmersiveLauncher_Win81 to monitor");
                return null;
            }

            return immersiveLauncher;
        }

        internal void ShowStartMenu(IntPtr taskbarHwnd)
        {
            IntPtr openShellHwnd = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "OpenShell.COwnerWindow", IntPtr.Zero);
            _taskbarHwndActivated = taskbarHwnd;

            if (!EnvironmentHelper.IsWindows10OrBetter ||
                openShellHwnd != IntPtr.Zero ||
                FindWindowEx(IntPtr.Zero, IntPtr.Zero, "DV2ControlHost", IntPtr.Zero) != IntPtr.Zero)
            {
                // Always use the Windows key when IImmersiveLauncher or IImmersiveMonitor is unavailable
                // Also use the Windows key when Open Shell Menu or StartIsBack is running, because we cannot otherwise invoke it
                if (openShellHwnd != IntPtr.Zero && _windowManager?.GetPrimaryTaskbar() is Taskbar primaryTaskbar)
                {
                    // Open Shell Menu does not properly handle receiving a non-primary taskbar rect, so give it the primary
                    primaryTaskbar.SetTrayHost();
                }
                ShellHelper.ShowStartMenu();
                return;
            }

            try
            {
                // Allow Explorer to steal focus
                GetWindowThreadProcessId(FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", "Program Manager"), out uint procId);
                AllowSetForegroundWindow(procId);

                if (EnvironmentHelper.IsWindows10RS1OrBetter)
                {
                    IImmersiveLauncher_Win10RS1 immersiveLauncher = GetImmersiveLauncher_Win10RS1(taskbarHwnd);
                    if (immersiveLauncher != null &&
                        immersiveLauncher.ShowStartView(IMMERSIVELAUNCHERSHOWMETHOD.ILSM_STARTBUTTON, IMMERSIVELAUNCHERSHOWFLAGS.ILSF_IGNORE_SET_FOREGROUND_ERROR) == 0)
                    {
                        return;
                    }
                }
                else
                {
                    IImmersiveLauncher_Win81 immersiveLauncher = GetImmersiveLauncher_Win81(taskbarHwnd);
                    if (immersiveLauncher != null &&
                        immersiveLauncher.ShowStartView(IMMERSIVELAUNCHERSHOWMETHOD.ILSM_STARTBUTTON, IMMERSIVELAUNCHERSHOWFLAGS.ILSF_IGNORE_SET_FOREGROUND_ERROR) == 0)
                    {
                        return;
                    }
                }
                ShellLogger.Warning("StartMenuMonitor: Failed to show Start menu via IImmersiveLauncher");
            }
            catch (Exception e)
            {
                ShellLogger.Warning($"StartMenuMonitor: Failed to show Start menu via IImmersiveLauncher: {e}");
            }

            ShellHelper.ShowStartMenu();
        }

        public void SetWindowManager(WindowManager windowManager)
        {
            _windowManager = windowManager;
        }

        public void Dispose()
        {
            _poller?.Stop();
        }

        #region Immersive launcher interfaces

        // Most managed interface definitions c/o https://github.com/MishaProductions/CustomShell/

        enum IMMERSIVE_MONITOR_FILTER_FLAGS
        {
            IMMERSIVE_MONITOR_FILTER_FLAGS_NONE = 0x0,
            IMMERSIVE_MONITOR_FILTER_FLAGS_DISABLE_TRAY = 0x1,
        }

        [ComImport]
        [Guid("880b26f8-9197-43d0-8045-8702d0d72000")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IImmersiveMonitor
        {
            public int GetIdentity(out uint pIdentity);
            public int Append(object unknown);
            public int GetHandle(out nint phMonitor);
            public int IsConnected(out bool pfConnected);
            public int IsPrimary(out bool pfPrimary);
            public int GetTrustLevel(out uint level);
            public int GetDisplayRect(out ManagedShell.Interop.NativeMethods.Rect prcDisplayRect);
            public int GetOrientation(out uint pdwOrientation);
            public int GetWorkArea(out ManagedShell.Interop.NativeMethods.Rect prcWorkArea);
            public int IsEqual(IImmersiveMonitor pMonitor, out bool pfEqual);
            public int GetTrustLevel2(out uint level);
            public int GetEffectiveDpi(out uint dpiX, out uint dpiY);
            public int GetFilterFlags(out IMMERSIVE_MONITOR_FILTER_FLAGS flags);
        }

        enum IMMERSIVE_MONITOR_MOVE_DIRECTION
        {
            IMMD_PREVIOUS = 0,
            IMMD_NEXT = 1
        }

        [ComImport]
        [Guid("4d4c1e64-e410-4faa-bafa-59ca069bfec2")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IImmersiveMonitorManager
        {
            public int GetCount(out uint pcMonitors);
            public int GetConnectedCount(out uint pcMonitors);
            public int GetAt(uint idxMonitor, out IImmersiveMonitor monitor);
            public int GetFromHandle(nint monitor, out IImmersiveMonitor monitor2);
            public int GetFromIdentity(uint identity, out IImmersiveMonitor monitor);
            public int GetImmersiveProxyMonitor(out IImmersiveMonitor monitor);
            public int QueryService(nint monit, ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
            public int QueryServiceByIdentity(uint monit, ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
            public int QueryServiceFromWindow(nint hwnd, ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
            public int QueryServiceFromPoint(nint point, ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
            public int GetNextImmersiveMonitor(IMMERSIVE_MONITOR_MOVE_DIRECTION direction, IImmersiveMonitor monitor, out IImmersiveMonitor monitorout);
            public int GetMonitorArray(out object array);
            public int SetFilter(object filter);
        }

        enum IMMERSIVELAUNCHERSHOWMETHOD
        {
            ILSM_INVALID = 0x0,
            ILSM_HSHELLTASKMAN = 0x1,
            ILSM_IMMERSIVEBACKGROUND = 0x4,
            ILSM_APPCLOSED = 0x6,
            ILSM_STARTBUTTON = 0xB,
            ILSM_RETAILDEMO_EDUCATIONAPP = 0xC,
            ILSM_BACK = 0xD,
            ILSM_SESSIONONUNLOCK = 0xE
        }

        enum IMMERSIVELAUNCHERSHOWFLAGS
        {
            ILSF_NONE = 0x0,
            ILSF_IGNORE_SET_FOREGROUND_ERROR = 0x4,
        }

        enum IMMERSIVELAUNCHERDISMISSMETHOD
        {
            ILDM_INVALID = 0x0,
            ILDM_HSHELLTASKMAN = 0x1,
            ILDM_STARTCHARM = 0x2,
            ILDM_BACKGESTURE = 0x3,
            ILDM_ESCAPEKEY = 0x4,
            ILDM_SHOWDESKTOP = 0x5,
            ILDM_STARTTIP = 0x6,
            ILDM_GENERIC_NONANIMATING = 0x7,
        }

        [ComImport]
        [Guid("d8d60399-a0f1-f987-5551-321fd1b49864")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IImmersiveLauncher_Win10RS1
        {
            public int ShowStartView(IMMERSIVELAUNCHERSHOWMETHOD showMethod, IMMERSIVELAUNCHERSHOWFLAGS showFlags);
            public int Dismiss(IMMERSIVELAUNCHERDISMISSMETHOD dismissMethod);
            public int Dismiss2(IMMERSIVELAUNCHERDISMISSMETHOD dismissMethod);
            public int DismissSynchronouslyWithoutTransition();
            public int IsVisible(out bool p0);
            public int OnStartButtonPressed(IMMERSIVELAUNCHERSHOWMETHOD showMethod, IMMERSIVELAUNCHERDISMISSMETHOD dismissMethod);
            public int SetForeground();
            public int ConnectToMonitor(IImmersiveMonitor monitor);
            public int GetMonitor(out IImmersiveMonitor monitor);
            public int OnFirstSignAnimationFinished();
            public int Prelaunch();
        }

        [ComImport]
        [Guid("93f91f5a-a4ca-4205-9beb-ce4d17c708f9")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IImmersiveLauncher_Win81
        {
            public int ShowStartView(IMMERSIVELAUNCHERSHOWMETHOD showMethod, IMMERSIVELAUNCHERSHOWFLAGS showFlags);
            public int Unknown2();
            public int Unknown3();
            public int Unknown4();
            public int Unknown5();
            public int Dismiss(IMMERSIVELAUNCHERDISMISSMETHOD dismissMethod);
            public int Unknown7();
            public int IsVisible(out bool p0);
            public int Unknown9();
            public int Unknown10();
            public int Unknown11();
            public int Unknown12();
            public int Unknown13();
            public int Unknown14();
            public int Unknown15();
            public int ConnectToMonitor(IImmersiveMonitor monitor);
            public int GetMonitor(out IImmersiveMonitor monitor);
        }

        static Guid CLSID_ImmersiveMonitorManager = new Guid("47094e3a-0cf2-430f-806f-cf9e4f0f12dd");
        static Guid IID_ImmersiveMonitorManager = new Guid("4d4c1e64-e410-4faa-bafa-59ca069bfec2");
        static Guid CLSID_ImmersiveLauncher = new Guid("6f86e01c-c649-4d61-be23-f1322ddeca9d");
        static Guid IID_ImmersiveLauncher_Win10RS1 = new Guid("d8d60399-a0f1-f987-5551-321fd1b49864");
        static Guid IID_ImmersiveLauncher_Win81 = new Guid("93f91f5a-a4ca-4205-9beb-ce4d17c708f9");
        #endregion

        public class StartMenuMonitorEventArgs : LauncherVisibilityEventArgs
        {
            public IntPtr TaskbarHwndActivated;
            public IntPtr StartHmonitor;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Utilities/StartMenuMonitor.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Utilities/Updater.cs">
namespace RetroBar.Utilities
{
    using ManagedShell.Common.Logging;
    using System;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Reflection;
    using System.Threading.Tasks;
    using System.ComponentModel;

    public class Updater : IDisposable
    {
        public bool IsUpdateAvailable { get; private set; }

        public readonly string DownloadUrl = "https://github.com/dremin/RetroBar/releases";

        private readonly HttpClient httpClient = new HttpClient();

        private readonly string _versionUrl = "https://dremin.github.io/updates/retrobar.json";
        private readonly int _initialInterval = 10000;
        private readonly int _updateInterval = 86400000;
        
        private Version _currentVersion;
        private System.Timers.Timer updateCheck;

        public Updater()
        {
            _currentVersion = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version;

            SetTimer();
            Settings.Instance.PropertyChanged += Settings_PropertyChanged;
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.CheckForUpdates))
            {
                SetTimer();
            }
        }

        private void SetTimer()
        {
            if (Settings.Instance.CheckForUpdates)
            {
                if (updateCheck == null)
                {
                    updateCheck = new System.Timers.Timer(_initialInterval);
                    updateCheck.Elapsed += UpdateCheck_Elapsed;
                    updateCheck.AutoReset = true;
                }
                updateCheck.Start();
            }
            else
            {
                updateCheck?.Stop();
            }
        }

        private async void UpdateCheck_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // initial interval is shortly after startup
            // don't check again until the desired time
            updateCheck.Interval = _updateInterval;

            IsUpdateAvailable = await CheckForUpdate();

            if (!IsUpdateAvailable)
            {
                return;
            }

            // if an update has been found, stop checking
            updateCheck.Stop();
        }

        public void Dispose()
        {
            updateCheck?.Stop();
            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
        }

        private async Task<bool> CheckForUpdate()
        {
            try
            {
                VersionInfo versionInfo = await httpClient.GetFromJsonAsync<VersionInfo>(_versionUrl);

                if (Version.TryParse(versionInfo.Version, out Version newVersion))
                {
                    if (newVersion > _currentVersion)
                    {
                        return true;
                    }
                }
                else
                {
                    ShellLogger.Info($"Updater: Unable to parse new version");
                }

            }
            catch (Exception e)
            {
                ShellLogger.Info($"Updater: Unable to check for updates: {e.Message}");
            }

            return false;
        }
    }

    public class VersionInfo
    {
        public string Version { get; set; }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Utilities/Updater.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Utilities/ClockFlyoutLauncher.cs">
namespace RetroBar.Utilities
{
    using System;
    using System.Runtime.InteropServices;
    using ManagedShell.Common.Helpers;
    using static ManagedShell.Interop.NativeMethods;
    using Screen = System.Windows.Forms.Screen;

    internal class ClockFlyoutLauncher
    {
        private const SetWindowPosFlags NoPosFlags = SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOACTIVATE | SetWindowPosFlags.SWP_NOZORDER;

        private static readonly Lazy<Type> AeroClockType = new(() => Type.GetTypeFromCLSID(new("A323554A-0FE1-4E49-AEE1-6722465D799F")));
        private static object _aeroClockInstance;

        [ComImport, Guid("7A5FCA8A-76B1-44C8-A97C-E7173CCA5F4F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAeroClock // For 8+
        {
            [PreserveSig]
            void ShowFlyout(IntPtr hWnd, ref Rect lpRect);
        }

        [ComImport, Guid("4376DF10-A662-420B-B30D-958881461EF9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAeroClockLegacy // For Vista and 7
        {
            [PreserveSig]
            void ShowFlyout(int unk, ref Rect lpRect);
        }

        internal static void ShowAeroClockFlyout(IntPtr taskbarHwnd)
        {
            Screen taskbarScreen = Screen.FromHandle(taskbarHwnd);
            if (!GetWindowRect(taskbarHwnd, out Rect lpRect))
            {
                return;
            }

            _aeroClockInstance ??= Activator.CreateInstance(AeroClockType.Value);

            if (EnvironmentHelper.IsWindows8OrBetter)
            {
                ((IAeroClock)_aeroClockInstance).ShowFlyout(taskbarHwnd, ref lpRect);
            }
            else
            {
                ((IAeroClockLegacy)_aeroClockInstance).ShowFlyout(0, ref lpRect);
            }

            FixAeroClockFlyoutPosition(taskbarScreen);
        }

        private static void FixAeroClockFlyoutPosition(Screen taskbarScreen)
        {
            IntPtr clockFlyoutHwnd = FindWindow("ClockFlyoutWindow", null);
            if (clockFlyoutHwnd == IntPtr.Zero)
            {
                return;
            }

            if (!GetWindowRect(clockFlyoutHwnd, out Rect rect))
            {
                return;
            }

            var wa = taskbarScreen.WorkingArea;
            int newX = rect.Left, newY = rect.Top;

            // Max margin (as used in Windows 7)
            int snap = 15;
            // Vista margin
            int margin = 7;

            // Move to closest edge if the flyout is too close to the screen edge
            if (Math.Abs(newX - wa.Left) <= snap) // Left
            {
                newX = wa.Left + margin;
            }
            else if (Math.Abs(rect.Right - wa.Right) <= snap) // Right
            {
                newX = wa.Right - rect.Width - margin;
            }

            if (Math.Abs(newY - wa.Top) <= snap) // Top
            {
                newY = wa.Top + margin;
            }
            else if (Math.Abs(rect.Bottom - wa.Bottom) <= snap) // Bottom
            {
                newY = wa.Bottom - rect.Height - margin;
            }

            // Keep the flyout inside the working area
            newX = Math.Max(wa.Left + margin, Math.Min(newX, wa.Right - rect.Width - margin));
            newY = Math.Max(wa.Top + margin, Math.Min(newY, wa.Bottom - rect.Height - margin));

            SetWindowPos(clockFlyoutHwnd, IntPtr.Zero, newX, newY, 0, 0, (int)NoPosFlags);
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Utilities/ClockFlyoutLauncher.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Utilities/VolumeChanger.cs">
namespace RetroBar.Utilities
{
    using System;
    using ManagedShell.Interop;

    class VolumeChanger
    {
        private const int APPCOMMAND_VOLUME_DOWN = 0x90000;
        private const int APPCOMMAND_VOLUME_UP = 0xA0000;

        public static void ChangeVolume(IntPtr hWnd, bool upOrDown)
        {
            NativeMethods.SendMessage(hWnd, (int)NativeMethods.WM.APPCOMMAND, hWnd,
                upOrDown ? (IntPtr)APPCOMMAND_VOLUME_UP : (IntPtr)APPCOMMAND_VOLUME_DOWN);
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Utilities/VolumeChanger.cs">

// [1400] INPUT :: hotkeys, tray hotkeys, low-level mouse hook, delayed activation

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Utilities/HotkeyManager.cs">
namespace RetroBar.Utilities
{
    using ManagedShell.Common.Helpers;
    using ManagedShell.Common.Logging;
    using System;
    using System.Collections.Generic;
    using System.Windows.Forms;
    using static ManagedShell.Interop.NativeMethods;

    /// <summary>
    /// Manages application hotkeys by overriding Explorer's built-in Win + optional modifier + VK hotkeys
    /// </summary>
    public class HotkeyManager : IDisposable
    {
        private readonly HotkeyListenerWindow _listenerWindow;

        public HotkeyManager()
        {
            _listenerWindow = new HotkeyListenerWindow(this);

            if (Settings.Instance.WinNumHotkeysAction != WinNumHotkeysOption.WindowsDefault)
            {
                _listenerWindow.RegisterHotkeys();
            }

            Settings.Instance.PropertyChanged += Settings_PropertyChanged;
        }

        public void Dispose()
        {
            if (_listenerWindow.IsRegistered)
            {
                _listenerWindow.UnregisterHotkeys();
            }

            _listenerWindow?.Dispose();
        }

        #region Events
        public class TaskbarHotkeyEventArgs : EventArgs
        {
            public int index;
            public bool isShiftPressed;
        }

        public event EventHandler<TaskbarHotkeyEventArgs> TaskbarHotkeyPressed;

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.WinNumHotkeysAction))
            {
                if (!_listenerWindow.IsRegistered && Settings.Instance.WinNumHotkeysAction != WinNumHotkeysOption.WindowsDefault)
                {
                    _listenerWindow.RegisterHotkeys();
                }
                else if (_listenerWindow.IsRegistered && Settings.Instance.WinNumHotkeysAction == WinNumHotkeysOption.WindowsDefault)
                {
                    _listenerWindow.UnregisterHotkeys();
                }
            }
        }
        #endregion

        private class HotkeyListenerWindow : NativeWindow, IDisposable
        {
            internal bool IsRegistered => _registeredHotkeys.Count > 0;
            private readonly HotkeyManager _manager;
            private readonly HashSet<int> _registeredHotkeys = [];
            private const int WMTRAY_UNREGISTERHOTKEY = (int)WM.USER + 231;
            private List<TrayHotkey.Entry> _trayHotkeyTable;
            private bool _unregisterFromExplorer = true;
            private IntPtr _trayWindow;

            public HotkeyListenerWindow(HotkeyManager manager)
            {
                CreateHandle(new CreateParams());
                _manager = manager;
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == (int)WM.HOTKEY)
                {
                    int hotkeyId = m.WParam.ToInt32();
                    if (_registeredHotkeys.Contains(hotkeyId))
                    {
                        // Determine if this is a Shift+Win+[0-9] hotkey based on the ID
                        bool isShiftPressed = hotkeyId >= 10 && hotkeyId <= 19;
                        int taskIndex = isShiftPressed ? hotkeyId - 10 : hotkeyId; // Extract the original task index (0-9)
                        
                        _manager.TaskbarHotkeyPressed?.Invoke(this, new TaskbarHotkeyEventArgs { 
                            index = taskIndex,
                            isShiftPressed = isShiftPressed 
                        });
                        ShellLogger.Debug($"HotkeyManager: Hotkey pressed: ID={hotkeyId}, TaskIndex={taskIndex}, Shift={isShiftPressed}");
                    }
                }

                base.WndProc(ref m);
            }

            public void RegisterHotkeys()
            {
                ShellLogger.Info("HotkeyManager: Registering override hotkeys");

                try
                {
                    // Initialize hotkey table and tray window
                    LoadExplorerResources();

                    _unregisterFromExplorer = _trayWindow != IntPtr.Zero;
                    if (!_unregisterFromExplorer)
                    {
                        ShellLogger.Info("HotkeyManager: Explorer resources not fully available - hotkeys will be registered but not unregistered from Explorer");
                    }

                    // Register the number keys (Win+[0-9])
                    RegisterWinKey(VK.KEY_1, 0);
                    RegisterWinKey(VK.KEY_2, 1);
                    RegisterWinKey(VK.KEY_3, 2);
                    RegisterWinKey(VK.KEY_4, 3);
                    RegisterWinKey(VK.KEY_5, 4);
                    RegisterWinKey(VK.KEY_6, 5);
                    RegisterWinKey(VK.KEY_7, 6);
                    RegisterWinKey(VK.KEY_8, 7);
                    RegisterWinKey(VK.KEY_9, 8);
                    RegisterWinKey(VK.KEY_0, 9);

                    // Register Shift+Win+[0-9] hotkeys (using IDs 10-19)
                    RegisterWinKey(VK.KEY_1, 10, MOD.SHIFT);
                    RegisterWinKey(VK.KEY_2, 11, MOD.SHIFT);
                    RegisterWinKey(VK.KEY_3, 12, MOD.SHIFT);
                    RegisterWinKey(VK.KEY_4, 13, MOD.SHIFT);
                    RegisterWinKey(VK.KEY_5, 14, MOD.SHIFT);
                    RegisterWinKey(VK.KEY_6, 15, MOD.SHIFT);
                    RegisterWinKey(VK.KEY_7, 16, MOD.SHIFT);
                    RegisterWinKey(VK.KEY_8, 17, MOD.SHIFT);
                    RegisterWinKey(VK.KEY_9, 18, MOD.SHIFT);
                    RegisterWinKey(VK.KEY_0, 19, MOD.SHIFT);
                }
                catch (Exception ex)
                {
                    ShellLogger.Warning($"HotkeyManager: Exception during RegisterHotkeys - {ex.Message}");
                }
            }

            private void LoadExplorerResources()
            {
                // Initialize with empty table as fallback
                _trayHotkeyTable = [];
                _trayWindow = IntPtr.Zero;

                TryFindTrayWindow();
                TryBuildHotkeyTable();
            }

            private void TryBuildHotkeyTable()
            {
                if (_trayWindow == IntPtr.Zero)
                {
                    return;
                }

                try
                {
                    _trayHotkeyTable = TrayHotkey.BuildTable(_trayWindow);
                    ShellLogger.Debug($"HotkeyManager: Found {_trayHotkeyTable.Count} entries in Explorer hotkey table");
                }
                catch (Exception ex)
                {
                    ShellLogger.Warning($"HotkeyManager: Failed to build Explorer hotkey table - {ex.Message}");
                }
            }

            private void TryFindTrayWindow()
            {
                try
                {
                    _trayWindow = WindowHelper.FindWindowsTray(WindowHelper.FindWindowsTray(IntPtr.Zero));
                }
                catch (Exception ex)
                {
                    ShellLogger.Warning($"HotkeyManager: Failed to find real Explorer tray window - {ex.Message}");
                }
            }

            private bool RegisterWinKey(VK key, int taskIndex, MOD additionalModifiers = 0)
            {
                // Combine the base modifiers with any additional modifiers
                MOD modifiers = MOD.WIN | MOD.NOREPEAT | additionalModifiers;

                try
                {
                    // Try to unregister from Explorer if possible
                    if (_unregisterFromExplorer)
                    {
                        TryUnregisterTrayHotkey(key, additionalModifiers);
                    }

                    // Attempt to register the hotkey for RetroBar
                    bool success = RegisterHotKey(
                        Handle,
                        taskIndex,
                        (uint)modifiers,
                        (uint)key);

                    if (success)
                    {
                        _registeredHotkeys.Add(taskIndex);
                        ShellLogger.Debug($"HotkeyManager: Registered hotkey {modifiers}+{key} with ID={taskIndex}");
                    }
                    else
                    {
                        ShellLogger.Warning($"HotkeyManager: Failed to register hotkey {modifiers}+{key} with ID={taskIndex}");
                    }

                    return success;
                }
                catch (Exception ex)
                {
                    ShellLogger.Warning($"HotkeyManager: Exception registering hotkey {modifiers}+{key} - {ex.Message}");
                    return false;
                }
            }

            private void TryUnregisterTrayHotkey(VK key, MOD additionalModifiers = 0)
            {
                try
                {
                    // Combine WIN with any additional modifiers (no need to check for MOD.NOREPEAT here)
                    MOD searchModifier = MOD.WIN | additionalModifiers;

                    // Find key with the specified modifiers in the Explorer hotkey table
                    int trayHotkeyIndex = _trayHotkeyTable.FindIndex(e => e.VirtualKey == (byte)key && e.Modifier == (byte)searchModifier);

                    // Exit if no matching hotkey found
                    if (trayHotkeyIndex < 0) return;

                    // Found a match - send unregister message to Explorer
                    int trayHotkeyId = _trayHotkeyTable[trayHotkeyIndex].Id;
                    SendMessage(_trayWindow, WMTRAY_UNREGISTERHOTKEY, new IntPtr(trayHotkeyId), IntPtr.Zero);
                    ShellLogger.Debug($"HotkeyManager: Sent WMTRAY_UNREGISTERHOTKEY for hotkey ID={trayHotkeyId}");
                }
                catch (Exception ex)
                {
                    ShellLogger.Warning($"HotkeyManager: Exception unregistering Explorer hotkey - {ex.Message}");
                }
            }

            public void UnregisterHotkeys()
            {
                // Only unregister those we actually registered
                ShellLogger.Info("HotkeyManager: Unregistering override hotkeys");
                foreach (int id in _registeredHotkeys)
                {
                    UnregisterHotKey(Handle, id);
                    ShellLogger.Debug($"HotkeyManager: Unregistered hotkey ID={id}");
                }

                _registeredHotkeys.Clear();

                // TODO: Re-register Explorer hotkeys
                // (maybe sending some undocumented message to the tray again, otherwise, restart Explorer process?)
            }

            public void Dispose() => DestroyHandle();
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Utilities/HotkeyManager.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Utilities/TrayHotkey.cs">
namespace RetroBar.Utilities
{
    using ManagedShell.Common.Logging;
    using System.Collections.Generic;
    using System.IO.MemoryMappedFiles;
    using System.IO;
    using System;
    using static ManagedShell.Interop.NativeMethods;

    static class TrayHotkey
    {
        public struct Entry
        {
            public int Id;
            public byte VirtualKey;
            public byte Modifier;
        }

        /// <summary>
        /// Extracts the Windows hotkey table registered by Shell_TrayWnd by reading the binary file of the tray process.
        /// </summary>
        /// <param name="trayHandle">Handle to the tray process</param>
        /// <returns>A list of hotkey entries found in the tray process</returns>
        /// <remarks>
        /// This method works by scanning the .exe for byte patterns that match the structure of hotkey entries.
        /// It identifies sequences of bytes that follow the pattern where:
        /// - Each entry is 8 bytes
        /// - Only byte 0 (virtual key code) and byte 4 (modifier) have non-zero values
        /// - The modifier has the Windows key flag set
        ///
        /// The method finds the longest consecutive sequence of valid entries, which is likely
        /// the hotkey table used by Shell_TrayWnd to register system-wide Windows+Key combinations.
        /// </remarks>
        public static List<Entry> BuildTable(IntPtr trayHandle)
        {
            string shellTrayProcessPath = GetShellTrayProcessPath(trayHandle);
            if (string.IsNullOrEmpty(shellTrayProcessPath))
            {
                throw new FileNotFoundException("Failed to get process path for Shell_TrayWnd");
            }

            using var mmf = MemoryMappedFile.CreateFromFile(shellTrayProcessPath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            using var stream = mmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
            using var reader = new BinaryReader(stream);
            byte[] data = reader.ReadBytes((int)stream.Length);

            var offsets = new List<int>();
            for (int i = 0; i <= data.Length - 8; i += 8)
            {
                if (IsValidEntryAt(data, i))
                    offsets.Add(i);
            }

            int bestStart = 0, bestCount = 0;
            foreach (int start in offsets)
            {
                int count = 1;
                while (offsets.Contains(start + count * 8)) count++;
                if (count > bestCount)
                {
                    bestStart = start;
                    bestCount = count;
                }
            }

            var table = new List<Entry>(bestCount);
            for (int i = 0; i < bestCount; i++)
            {
                int off = bestStart + i * 8;
                table.Add(new Entry
                {
                    Id = i + 500, // Shell_TrayWnd hotkey IDs start at 500
                    VirtualKey = data[off],
                    Modifier = data[off + 4]
                });
            }

            return table;
        }

        /// <summary>
        /// Checks if the bytes at the specified index match the pattern of a Windows hotkey entry in the tray process.
        /// </summary>
        /// <param name="bytes">The byte array to check</param>
        /// <param name="offset">The offset to start checking at</param>
        /// <returns>True if the bytes at offset match the hotkey entry pattern</returns>
        private static bool IsValidEntryAt(byte[] bytes, int offset)
        {
            const int ENTRY_SIZE = 8;
            if (offset % ENTRY_SIZE != 0)
                return false;

            // Only byte 0 (VK code) and byte 4 (modifier) should have non-zero values; the rest must be zero.
            for (int i = 1; i < ENTRY_SIZE; i++)
            {
                if (i != 4 && bytes[offset + i] != 0)
                    return false;
            }

            // Validate modifier: must have the Windows key flag set and high bits should be clear.
            byte modifier = bytes[offset + 4];
            return (modifier & (byte)MOD.WIN) != 0 && (modifier & 0xF0) == 0;
        }

        #region Process-related API calls
        private static readonly string SystemExplorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

        public static string GetShellTrayProcessPath(IntPtr trayHandle)
        {
            IntPtr processHandle = IntPtr.Zero;
            try
            {
                GetWindowThreadProcessId(trayHandle, out uint processId);
                processHandle = OpenProcess(ProcessAccessFlags.QueryLimitedInformation, false, (int)processId);
                if (processHandle == IntPtr.Zero)
                {
                    ShellLogger.Warning($"TrayHotkey: OpenProcess failed (Error: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
                    return null;
                }

                string executablePath = GetExecutablePath(processHandle);
                return !string.IsNullOrEmpty(executablePath) ? executablePath : SystemExplorerPath;
            }
            catch (Exception ex)
            {
                ShellLogger.Warning($"TrayHotkey: Failed to get process path - {ex.Message}");
                return null;
            }
            finally
            {
                if (processHandle != IntPtr.Zero)
                {
                    CloseHandle(processHandle);
                }
            }
        }

        private static string GetExecutablePath(IntPtr processHandle)
        {
            try
            {
                System.Text.StringBuilder stringBuilder = new(4096);
                int size = stringBuilder.Capacity;
                if (QueryFullProcessImageName(processHandle, 0, stringBuilder, ref size))
                {
                    return stringBuilder.ToString(0, size);
                }
            }
            catch (Exception ex)
            {
                ShellLogger.Warning($"TrayHotkey: Error getting executable path - {ex.Message}");
            }
            return null;
        }
        #endregion
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Utilities/TrayHotkey.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Utilities/LowLevelMouseHook.cs">
namespace RetroBar.Utilities
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    using static ManagedShell.Interop.NativeMethods;

    // TODO: This should move to ManagedShell
    public class LowLevelMouseHook : IDisposable
    {
        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProcDelegate callback, IntPtr hInstance, uint threadId);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr idHook, int nCode, uint wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        public delegate IntPtr LowLevelMouseProcDelegate(int code, uint wParam, IntPtr lParam);

        const int WH_MOUSE_LL = 14;

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public int mouseData;
            public int flags;
            public int time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        public class LowLevelMouseEventArgs : HandledEventArgs
        {
            public WM Message;
            public MSLLHOOKSTRUCT HookStruct;
        }

        public event EventHandler<LowLevelMouseEventArgs> LowLevelMouseEvent;

        private IntPtr _hook = IntPtr.Zero;
        private LowLevelMouseProcDelegate _hookDelegate;

        public LowLevelMouseHook() {
            _hookDelegate = MouseHookProc;
        }

        public bool Initialize()
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                _hook = SetWindowsHookEx(WH_MOUSE_LL, _hookDelegate, GetModuleHandle(curModule.ModuleName), 0);

                if (_hook == IntPtr.Zero)
                {
                    return false;
                }

                return true;
            }
        }

        private IntPtr MouseHookProc(int code, uint wParam, IntPtr lParam)
        {
            LowLevelMouseEventArgs args = new LowLevelMouseEventArgs
            {
                Message = (WM)wParam,
                HookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT))
            };

            LowLevelMouseEvent?.Invoke(this, args);

            if (args.Handled)
            {
                return (IntPtr)1;
            }

            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hook == IntPtr.Zero)
            {
                return;
            }

            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Utilities/LowLevelMouseHook.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Utilities/DelayedActivationHandler.cs">
namespace RetroBar.Utilities
{
    using System;
    using System.Windows;
    using System.Windows.Threading;

    public class DelayedActivationHandler : IDisposable
    {
        private bool inDrag;
        private DispatcherTimer activationTimer;
        private Action performAction;

        public DelayedActivationHandler(TimeSpan activationDelay, Action action)
        {
            activationTimer = new DispatcherTimer { Interval = activationDelay };
            activationTimer.Tick += activationTimer_Tick;
            performAction = action;
        }

        public DelayedActivationHandler(Action action) : this(SystemParameters.MouseHoverTime, action) { }

        private void activationTimer_Tick(object sender, EventArgs e)
        {
            if (inDrag)
            {
                performAction?.Invoke();
            }

            activationTimer.Stop();
        }

        public void OnDragEnter(DragEventArgs e)
        {
            // Ignore drag operations from a reorder
            if (!inDrag && !e.Data.GetDataPresent("GongSolutions.Wpf.DragDrop"))
            {
                inDrag = true;
                activationTimer.Start();
            }
        }

        public void OnDragLeave()
        {
            if (inDrag)
            {
                activationTimer.Stop();
                inDrag = false;
            }
        }

        public void Dispose()
        {
            performAction = null;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Utilities/DelayedActivationHandler.cs">

// [1500] NOTIFICATION AREA + TOOLBARS :: tray icon helpers, drag and drop handlers, Quick Launch sorting

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Extensions/NotifyIconExtensions.cs">
namespace RetroBar.Extensions
{
    using ManagedShell.WindowsTray;
    using RetroBar.Utilities;
    using System.Collections.Generic;

    public static class NotifyIconExtensions
    {
        public static string GetInvertIdentifier(this NotifyIcon icon)
        {
            if (icon.GUID != default) return icon.GUID.ToString();
            else return icon.Path + ":" + icon.UID.ToString();
        }

        public static NotifyIconBehavior GetBehavior(this NotifyIcon icon)
        {
            if (Settings.Instance.NotifyIconBehaviors.Find(setting => setting.Identifier == icon.Identifier) is NotifyIconBehaviorSetting iconSetting)
            {
                return iconSetting.Behavior;
            }

            return NotifyIconBehavior.HideWhenInactive;
        }

        public static void SetBehavior(this NotifyIcon icon, NotifyIconBehavior behavior)
        {
            var settings = new List<NotifyIconBehaviorSetting>(Settings.Instance.NotifyIconBehaviors);
            var currentSettingIndex = settings.FindIndex(setting => setting.Identifier == icon.Identifier);

            if (currentSettingIndex >= 0)
            {
                if (behavior == NotifyIconBehavior.HideWhenInactive)
                {
                    // Switching back to default value; remove from settings
                    settings.RemoveAt(currentSettingIndex);
                }
                else
                {
                    settings[currentSettingIndex] = new NotifyIconBehaviorSetting
                    {
                        Identifier = icon.Identifier,
                        Behavior = behavior
                    };
                }
            }
            else
            {
                settings.Add(new NotifyIconBehaviorSetting
                {
                    Identifier = icon.Identifier,
                    Behavior = behavior
                });
            }

            Settings.Instance.NotifyIconBehaviors = settings;

            if (icon.IsPinned != (behavior == NotifyIconBehavior.AlwaysShow))
            {
                // Update pinned values in ManagedShell
                if (behavior == NotifyIconBehavior.AlwaysShow)
                {
                    icon.Pin();
                }
                else
                {
                    icon.Unpin();
                }
            }
            else
            {
                // Trigger a refresh of the collections
                icon.OnPropertyChanged("IsPinned");
            }
        }

        public static bool CanInvert(this NotifyIcon icon) {
            return Settings.Instance.InvertNotifyIcons.Contains(icon.GetInvertIdentifier());
        }

        public static void SetCanInvert(this NotifyIcon icon, bool canInvert)
        {
            var identifier = icon.GetInvertIdentifier();
            var settings = new List<string>(Settings.Instance.InvertNotifyIcons);
            var changed = false;

            if (!canInvert)
            {
                changed = settings.Remove(identifier);
            }
            else if (!settings.Contains(identifier))
            {
                settings.Add(identifier);
                changed = true;
            }

            if (changed)
            {
                Settings.Instance.InvertNotifyIcons = settings;
            }
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Extensions/NotifyIconExtensions.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Utilities/NotifyIconDropHandler.cs">
namespace RetroBar.Utilities
{
    using GongSolutions.Wpf.DragDrop;
    using RetroBar.Controls;

    public class NotifyIconDropHandler : IDropTarget
    {
        private NotifyIconList _notifyIconList;

        public IDropInfo DropInFlight { get; set; }

        public NotifyIconDropHandler(NotifyIconList notifyIconList)
        {
            _notifyIconList = notifyIconList;
        }

        void IDropTarget.DragOver(IDropInfo dropInfo)
        {
            DragDrop.DefaultDropHandler.DragOver(dropInfo);
        }

#if !NETCOREAPP3_1_OR_GREATER
        public void DragEnter(IDropInfo dropInfo)
        {
            DragDrop.DefaultDropHandler.DragEnter(dropInfo);
        }

        public void DragLeave(IDropInfo dropInfo)
        {
            DragDrop.DefaultDropHandler.DragLeave(dropInfo);
        }
#endif

        void IDropTarget.Drop(IDropInfo dropInfo)
        {
            DropInFlight = dropInfo;
            DragDrop.DefaultDropHandler.Drop(dropInfo);

            // Save post-drop state
            _notifyIconList.UpdateIconOrder(dropInfo);
            DropInFlight = null;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Utilities/NotifyIconDropHandler.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Utilities/ToolbarDropHandler.cs">
namespace RetroBar.Utilities
{
    using GongSolutions.Wpf.DragDrop;
    using RetroBar.Controls;

    public class ToolbarDropHandler : IDropTarget
    {
        private Toolbar _toolbar;

        public IDropInfo DropInFlight { get; set; }

        public ToolbarDropHandler(Toolbar toolbar)
        {
            _toolbar = toolbar;
        }

        void IDropTarget.DragOver(IDropInfo dropInfo)
        {
            if (dropInfo.Data is System.Windows.DataObject dataObject)
            {
                if (dataObject != null && dataObject.GetDataPresent(System.Windows.DataFormats.FileDrop))
                {
                    dropInfo.Effects = System.Windows.DragDropEffects.Link;
                }
            }

            DragDrop.DefaultDropHandler.DragOver(dropInfo);
        }

#if !NETCOREAPP3_1_OR_GREATER
        public void DragEnter(IDropInfo dropInfo)
        {
            DragDrop.DefaultDropHandler.DragEnter(dropInfo);
        }

        public void DragLeave(IDropInfo dropInfo)
        {
            DragDrop.DefaultDropHandler.DragLeave(dropInfo);
        }
#endif

        void IDropTarget.Drop(IDropInfo dropInfo)
        {
            if (dropInfo.Data is System.Windows.DataObject dataObject)
            {
                if (dataObject != null && dataObject.ContainsFileDropList())
                {
                    _toolbar.AddToSource(dataObject.GetFileDropList());
                    return;
                }
            }

            // Save before the drop in order to catch any items not yet saved
            _toolbar.SaveItemOrder();
            DropInFlight = dropInfo;

            DragDrop.DefaultDropHandler.Drop(dropInfo);

            // Save post-drop state
            _toolbar.SaveItemOrder();
            DropInFlight = null;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Utilities/ToolbarDropHandler.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Utilities/ToolbarSorter.cs">
namespace RetroBar.Utilities
{
    using ManagedShell.ShellFolders;
    using RetroBar.Controls;
    using System.Collections;
    using System.Collections.Generic;

    public class ToolbarSorter : IComparer
    {
        private Toolbar _toolbar;

        public ToolbarSorter(Toolbar toolbar)
        {
            _toolbar = toolbar;
        }

        public int Compare(object x, object y)
        {
            if (x is ShellItem a && y is ShellItem b)
            {
                List<string> desiredSort = Settings.Instance.QuickLaunchOrder;

                // If reordering, modify the desired sort to reflect the in-flight drop.
                if (_toolbar.DropHandler.DropInFlight != null && 
                    _toolbar.DropHandler.DropInFlight.Data is ShellFile droppedFile && 
                    (droppedFile == a || droppedFile == b))
                {
                    int desiredIndex = _toolbar.DropHandler.DropInFlight.InsertIndex;
                    int currentIndex = desiredSort.IndexOf(droppedFile.Path);

                    if (currentIndex >= 0)
                    {
                        // If we are dragging to a position after what is being removed, update desired position
                        if (desiredIndex > currentIndex)
                        {
                            desiredIndex--;
                        }

                        desiredSort.RemoveAt(currentIndex);
                    }

                    // Safety first!
                    if (desiredIndex > desiredSort.Count)
                    {
                        desiredIndex = desiredSort.Count;
                    }

                    desiredSort.Insert(desiredIndex, droppedFile.Path);
                }

                if (!desiredSort.Contains(a.Path) && !desiredSort.Contains(b.Path))
                {
                    return 0;
                }

                if (!desiredSort.Contains(a.Path))
                {
                    return 1;
                }

                if (!desiredSort.Contains(b.Path))
                {
                    return -1;
                }

                int indexA = desiredSort.IndexOf(a.Path);
                int indexB = desiredSort.IndexOf(b.Path);

                if (indexA < indexB)
                {
                    return -1;
                }

                if (indexA > indexB)
                {
                    return 1;
                }
            }

            return 0;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Utilities/ToolbarSorter.cs">

// [1600] VISUAL EFFECTS :: Retrostar's own Aero glass and the icon invert shader

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Utilities/AeroGlassHelper.cs">
namespace RetroBar.Utilities
{
    using ManagedShell.Common.Logging;
    using System;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Interop;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;
    using System.Windows.Threading;

    /// <summary>
    /// Retrostar's own Aero glass. Nothing here uses a Windows transparency effect: no accent policy, no system
    /// backdrop, no DWM blur-behind. Each glass window owns an AeroGlassSurface, which reads the screen behind the
    /// window with GDI, blurs it in code the way Vista's and Windows 7's glass did, and paints the result as the
    /// window's background; the theme's tint is drawn over it. See Rules_For_Agent_Editors.md, rule 8.
    ///
    /// The screen is read with BitBlt without CAPTUREBLT, which leaves out layered windows. Every glass window is
    /// layered (AllowsTransparency), so a surface never sees itself or another Retrostar window, while screenshots
    /// and recordings, which include layered windows, show the bars as they are. Whether this Windows leaves layered
    /// windows out is checked once at startup (CheckCaptureAsync); if it does not, the glass is drawn over an opaque
    /// backing instead, so it never blurs itself.
    /// </summary>
    internal static class AeroGlassHelper
    {
        // Side of the probe window used by the capture check, in device-independent pixels
        private const double ProbeSize = 6;
        private const int ProbeSettleMilliseconds = 150;
        private const int ProbeAttempts = 3;
        private const int Windows11Build = 22000;

        private static Task<bool> _captureCheck;

        /// <summary>
        /// Opacity of the tint every glass surface paints over the blur, set by the Aero glass transparency slider:
        /// 1 is the tint as each theme was measured, 0 leaves the blur alone. A surface uses it only while its glass is
        /// live; its window's GlassTintOpacity property is set by the surface. See Rules_For_Agent_Editors.md, rule 8.
        /// </summary>
        public static double TintOpacity => 1.0 - Math.Clamp(Settings.Instance.AeroGlassTransparency, 0, 100) / 100.0;

        /// <summary>Whether glass surfaces show what is behind them (live glass); otherwise they are opaque glass.</summary>
        public static bool IsSeeThroughEnabled => Settings.Instance.EnableAeroGlass && Settings.Instance.AllowBlurBehind;

        /// <summary>
        /// Checks once whether reading the screen leaves out layered windows. Must be called on the UI thread;
        /// later calls return the same result.
        /// </summary>
        public static Task<bool> CheckCaptureAsync()
        {
            return _captureCheck ??= RunCaptureCheckAsync();
        }

        /// <summary>
        /// Shows a tiny layered probe window in two colours and reads the screen under it each time. If the colour
        /// read never follows the probe, layered windows are left out of the capture.
        /// </summary>
        private static async Task<bool> RunCaptureCheckAsync()
        {
            Window probe = null;

            try
            {
                Rect area = SystemParameters.WorkArea;
                probe = new Window
                {
                    Title = string.Empty,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Topmost = true,
                    Focusable = false,
                    Width = ProbeSize,
                    Height = ProbeSize,
                    Left = area.Left,
                    Top = area.Top,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Background = Brushes.Red
                };
                probe.SourceInitialized += (sender, e) =>
                {
                    IntPtr hwnd = new WindowInteropHelper((Window)sender).Handle;
                    int exStyle = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
                    Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE,
                        exStyle | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
                };
                probe.Show();

                IntPtr probeHandle = new WindowInteropHelper(probe).Handle;

                for (int attempt = 0; attempt < ProbeAttempts; attempt++)
                {
                    probe.Background = Brushes.Red;
                    int? red = await ReadProbePixelAsync(probeHandle);

                    probe.Background = Brushes.Lime;
                    int? lime = await ReadProbePixelAsync(probeHandle);

                    if (red == null || lime == null)
                    {
                        break;
                    }

                    if (red == lime)
                    {
                        ShellLogger.Info("AeroGlassHelper: The screen is read without layered windows; live glass is available.");
                        return true;
                    }

                    if (IsNear(red.Value, 0xFF0000) && IsNear(lime.Value, 0x00FF00))
                    {
                        break;
                    }

                    // Something behind the probe changed between the reads; try again
                }
            }
            catch (Exception e)
            {
                ShellLogger.Warning($"AeroGlassHelper: Unable to check screen capture: {e.Message}");
            }
            finally
            {
                probe?.Close();
            }

            ShellLogger.Warning("AeroGlassHelper: The screen cannot be read without layered windows; glass is drawn opaque.");
            return false;
        }

        private static async Task<int?> ReadProbePixelAsync(IntPtr probeHandle)
        {
            await Task.Delay(ProbeSettleMilliseconds);

            try
            {
                Native.DwmFlush();
            }
            catch (Exception)
            {
                // Without composition there is nothing to wait for
            }

            if (!Native.GetWindowRect(probeHandle, out Native.RECT rect))
            {
                return null;
            }

            int x = (rect.Left + rect.Right) / 2;
            int y = (rect.Top + rect.Bottom) / 2;

            using ScreenReader reader = new ScreenReader();
            byte[] pixel = reader.Read(x, y, 1, 1);

            if (pixel == null)
            {
                return null;
            }

            return (pixel[2] << 16) | (pixel[1] << 8) | pixel[0];
        }

        private static bool IsNear(int rgb, int expected)
        {
            for (int shift = 0; shift <= 16; shift += 8)
            {
                if (Math.Abs(((rgb >> shift) & 0xFF) - ((expected >> shift) & 0xFF)) > 24)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Turns off Windows 11's rounded corners, accent border and system backdrop on a glass window, so none of
        /// Windows 11's own treatment is ever drawn with the glass. Earlier versions of Windows have none of these.
        /// </summary>
        internal static void PrepareWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || Environment.OSVersion.Version.Build < Windows11Build)
            {
                return;
            }

            // Each attribute exists on some Windows 11 versions only; one that is not supported is simply refused
            SetAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, Native.DWMSBT_NONE);
            SetAttribute(hwnd, Native.DWMWA_MICA_EFFECT, 0);
            SetAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, Native.DWMWCP_DONOTROUND);
            SetAttribute(hwnd, Native.DWMWA_BORDER_COLOR, unchecked((int)Native.DWMWA_COLOR_NONE));
        }

        private static void SetAttribute(IntPtr hwnd, int attribute, int value)
        {
            try
            {
                Native.DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
            }
            catch (Exception e)
            {
                ShellLogger.Debug($"AeroGlassHelper: Unable to set window attribute {attribute}: {e.Message}");
            }
        }

        /// <summary>
        /// Reads part of the screen into 32-bit BGRA pixels, top row first, keeping its bitmap between reads.
        /// Layered windows are left out (no CAPTUREBLT). Not thread-safe; each user owns one.
        /// </summary>
        internal sealed class ScreenReader : IDisposable
        {
            private IntPtr _memoryDc;
            private IntPtr _bitmap;
            private IntPtr _oldBitmap;
            private IntPtr _bits;
            private int _width;
            private int _height;

            public byte[] Read(int x, int y, int width, int height)
            {
                if (width <= 0 || height <= 0)
                {
                    return null;
                }

                IntPtr screenDc = Native.GetDC(IntPtr.Zero);

                if (screenDc == IntPtr.Zero)
                {
                    return null;
                }

                try
                {
                    if (!EnsureBitmap(screenDc, width, height))
                    {
                        return null;
                    }

                    if (!Native.BitBlt(_memoryDc, 0, 0, width, height, screenDc, x, y, Native.SRCCOPY))
                    {
                        return null;
                    }

                    Native.GdiFlush();

                    byte[] pixels = new byte[width * height * 4];
                    Marshal.Copy(_bits, pixels, 0, pixels.Length);
                    return pixels;
                }
                finally
                {
                    Native.ReleaseDC(IntPtr.Zero, screenDc);
                }
            }

            private bool EnsureBitmap(IntPtr screenDc, int width, int height)
            {
                if (_bitmap != IntPtr.Zero && width == _width && height == _height)
                {
                    return true;
                }

                ReleaseBitmap();

                if (_memoryDc == IntPtr.Zero)
                {
                    _memoryDc = Native.CreateCompatibleDC(screenDc);

                    if (_memoryDc == IntPtr.Zero)
                    {
                        return false;
                    }
                }

                Native.BITMAPINFOHEADER header = new Native.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height, // top row first
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = Native.BI_RGB
                };

                _bitmap = Native.CreateDIBSection(screenDc, ref header, Native.DIB_RGB_COLORS, out _bits, IntPtr.Zero, 0);

                if (_bitmap == IntPtr.Zero || _bits == IntPtr.Zero)
                {
                    ReleaseBitmap();
                    return false;
                }

                _oldBitmap = Native.SelectObject(_memoryDc, _bitmap);
                _width = width;
                _height = height;
                return true;
            }

            private void ReleaseBitmap()
            {
                if (_bitmap == IntPtr.Zero)
                {
                    return;
                }

                if (_memoryDc != IntPtr.Zero && _oldBitmap != IntPtr.Zero)
                {
                    Native.SelectObject(_memoryDc, _oldBitmap);
                }

                Native.DeleteObject(_bitmap);
                _bitmap = IntPtr.Zero;
                _oldBitmap = IntPtr.Zero;
                _bits = IntPtr.Zero;
                _width = 0;
                _height = 0;
            }

            public void Dispose()
            {
                ReleaseBitmap();

                if (_memoryDc != IntPtr.Zero)
                {
                    Native.DeleteDC(_memoryDc);
                    _memoryDc = IntPtr.Zero;
                }
            }
        }

        internal static class Native
        {
            public const int GWL_EXSTYLE = -20;
            public const int WS_EX_TRANSPARENT = 0x00000020;
            public const int WS_EX_TOOLWINDOW = 0x00000080;
            public const int WS_EX_NOACTIVATE = 0x08000000;
            public const uint SRCCOPY = 0x00CC0020;
            public const uint BI_RGB = 0;
            public const uint DIB_RGB_COLORS = 0;
            public const int SM_XVIRTUALSCREEN = 76;
            public const int SM_YVIRTUALSCREEN = 77;
            public const int SM_CXVIRTUALSCREEN = 78;
            public const int SM_CYVIRTUALSCREEN = 79;

            // Windows 11 window attributes (dwmapi.h)
            public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
            public const int DWMWA_BORDER_COLOR = 34;
            public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
            public const int DWMWA_MICA_EFFECT = 1029; // Windows 11 21H2, before DWMWA_SYSTEMBACKDROP_TYPE
            public const int DWMWCP_DONOTROUND = 1;
            public const int DWMSBT_NONE = 1;
            public const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

            [StructLayout(LayoutKind.Sequential)]
            public struct RECT
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct BITMAPINFOHEADER
            {
                public uint biSize;
                public int biWidth;
                public int biHeight;
                public ushort biPlanes;
                public ushort biBitCount;
                public uint biCompression;
                public uint biSizeImage;
                public int biXPelsPerMeter;
                public int biYPelsPerMeter;
                public uint biClrUsed;
                public uint biClrImportant;
            }

            [DllImport("user32.dll")]
            public static extern IntPtr GetDC(IntPtr hWnd);

            [DllImport("user32.dll")]
            public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsWindowVisible(IntPtr hWnd);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsIconic(IntPtr hWnd);

            [DllImport("user32.dll")]
            public static extern int GetSystemMetrics(int nIndex);

            [DllImport("user32.dll")]
            public static extern uint GetDpiForWindow(IntPtr hwnd);

            [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
            public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

            [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
            public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

            [DllImport("gdi32.dll")]
            public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

            [DllImport("gdi32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DeleteDC(IntPtr hdc);

            [DllImport("gdi32.dll")]
            public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

            [DllImport("gdi32.dll")]
            public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

            [DllImport("gdi32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DeleteObject(IntPtr ho);

            [DllImport("gdi32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, uint rop);

            [DllImport("gdi32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GdiFlush();

            [DllImport("dwmapi.dll")]
            public static extern int DwmFlush();

            [DllImport("dwmapi.dll")]
            public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        }
    }

    /// <summary>
    /// The glass of one window: what is behind the window, read from the screen, blurred and painted as the window's
    /// background several times a second, with the theme's tint drawn over it by the window itself. The window must
    /// be layered (AllowsTransparency) so it is never read back into its own glass.
    /// The surface sets the window's tint opacity through the callback it was given: AeroGlassHelper.TintOpacity
    /// while the glass is live, 1 otherwise.
    /// </summary>
    internal sealed class AeroGlassSurface : IDisposable
    {
        // How often the screen behind the window is read
        private const int FrameIntervalMilliseconds = 50;

        // Radius of each of the three box blurs, in device-independent pixels: together a Gaussian of about 3.5 px,
        // the softness of Vista's and Windows 7's glass
        private const double BlurRadius = 3;
        private const int BlurPasses = 3;

        private static readonly Brush OpaqueBacking = CreateOpaqueBacking();

        private readonly Window _window;
        private readonly Action<double> _setTintOpacity;
        private readonly ImageBrush _glassBrush;

        private enum Mode
        {
            Off,
            Opaque,
            Live
        }

        private Mode _mode = Mode.Off;
        private int _generation;
        private bool _backgroundSaved;
        private object _savedBackground;
        private CancellationTokenSource _loopCancellation;
        private bool _disposed;

        public AeroGlassSurface(Window window, Action<double> setTintOpacity)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _setTintOpacity = setTintOpacity ?? throw new ArgumentNullException(nameof(setTintOpacity));
            _glassBrush = new ImageBrush
            {
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            };
        }

        /// <summary>
        /// Shows live glass: what is behind the window, blurred. If this Windows cannot read the screen without the
        /// glass windows themselves, the glass is drawn opaque instead. Call on the UI thread once the window has a
        /// handle and again whenever the glass settings change.
        /// </summary>
        public void Start()
        {
            if (_disposed)
            {
                return;
            }

            IntPtr hwnd = new WindowInteropHelper(_window).Handle;

            if (hwnd == IntPtr.Zero || !_window.AllowsTransparency)
            {
                Stop();
                return;
            }

            if (_mode == Mode.Live)
            {
                // Already running; only the tint may have changed
                _setTintOpacity(AeroGlassHelper.TintOpacity);
                return;
            }

            StopLoop();
            AeroGlassHelper.PrepareWindow(hwnd);
            SaveBackground();
            int generation = ++_generation;

            Task<bool> check = AeroGlassHelper.CheckCaptureAsync();

            if (check.IsCompleted)
            {
                BeginLive(hwnd, generation, check.Result);
                return;
            }

            // Only while Retrostar starts: opaque until the capture check is done
            _mode = Mode.Opaque;
            _window.Background = OpaqueBacking;
            _setTintOpacity(1.0);

            Dispatcher dispatcher = _window.Dispatcher;
            _ = check.ContinueWith(task =>
            {
                bool available = task.Status == TaskStatus.RanToCompletion && task.Result;
                _ = dispatcher.BeginInvoke(new Action(() => BeginLive(hwnd, generation, available)));
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// Shows the glass without anything behind it: the tint over an opaque backing, as the glass looks over black.
        /// Used while "Allow blur behind" is off.
        /// </summary>
        public void StartOpaque()
        {
            if (_disposed)
            {
                return;
            }

            StopLoop();
            _generation++;
            SaveBackground();
            _mode = Mode.Opaque;
            _window.Background = OpaqueBacking;
            _setTintOpacity(1.0);
        }

        /// <summary>Removes the glass and gives the window its own background back.</summary>
        public void Stop()
        {
            StopLoop();
            _generation++;
            _mode = Mode.Off;
            _glassBrush.ImageSource = null;
            RestoreBackground();
            _setTintOpacity(1.0);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _disposed = true;
        }

        private void BeginLive(IntPtr hwnd, int generation, bool available)
        {
            if (_disposed || generation != _generation)
            {
                return;
            }

            if (!available)
            {
                _mode = Mode.Opaque;
                _window.Background = OpaqueBacking;
                _setTintOpacity(1.0);
                return;
            }

            // The first frame is drawn at once, so the window never shows unblurred
            using (AeroGlassHelper.ScreenReader reader = new AeroGlassHelper.ScreenReader())
            {
                ulong ignored = 0;
                BitmapSource first = RenderFrame(hwnd, reader, ref ignored, true);

                if (first != null)
                {
                    _glassBrush.ImageSource = first;
                }
            }

            _mode = Mode.Live;
            _window.Background = _glassBrush;
            _setTintOpacity(AeroGlassHelper.TintOpacity);

            CancellationTokenSource cancellation = new CancellationTokenSource();
            _loopCancellation = cancellation;
            Dispatcher dispatcher = _window.Dispatcher;

            _ = Task.Run(() => RunLoop(hwnd, generation, dispatcher, cancellation.Token));
        }

        private async Task RunLoop(IntPtr hwnd, int generation, Dispatcher dispatcher, CancellationToken token)
        {
            using AeroGlassHelper.ScreenReader reader = new AeroGlassHelper.ScreenReader();
            ulong lastSignature = 0;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    BitmapSource frame = RenderFrame(hwnd, reader, ref lastSignature, false);

                    if (frame != null)
                    {
                        _ = dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                        {
                            if (generation == _generation && _mode == Mode.Live)
                            {
                                _glassBrush.ImageSource = frame;
                            }
                        }));
                    }
                }
                catch (Exception e)
                {
                    ShellLogger.Debug($"AeroGlassSurface: Unable to draw glass: {e.Message}");
                }

                try
                {
                    await Task.Delay(FrameIntervalMilliseconds, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Reads the screen behind the window with a margin for the blur, blurs it and crops it to the window.
        /// Returns null when nothing changed since the last frame (unless forced) or the window cannot be read.
        /// </summary>
        private static BitmapSource RenderFrame(IntPtr hwnd, AeroGlassHelper.ScreenReader reader, ref ulong lastSignature, bool force)
        {
            if (!AeroGlassHelper.Native.IsWindowVisible(hwnd) || AeroGlassHelper.Native.IsIconic(hwnd) ||
                !AeroGlassHelper.Native.GetWindowRect(hwnd, out AeroGlassHelper.Native.RECT window))
            {
                return null;
            }

            int width = window.Right - window.Left;
            int height = window.Bottom - window.Top;

            if (width <= 0 || height <= 0)
            {
                return null;
            }

            int radius = Math.Max(1, (int)Math.Round(BlurRadius * GetScale(hwnd)));
            int margin = radius * BlurPasses;

            // The area read: the window plus the blur's reach, kept on the screen
            int screenLeft = AeroGlassHelper.Native.GetSystemMetrics(AeroGlassHelper.Native.SM_XVIRTUALSCREEN);
            int screenTop = AeroGlassHelper.Native.GetSystemMetrics(AeroGlassHelper.Native.SM_YVIRTUALSCREEN);
            int screenRight = screenLeft + AeroGlassHelper.Native.GetSystemMetrics(AeroGlassHelper.Native.SM_CXVIRTUALSCREEN);
            int screenBottom = screenTop + AeroGlassHelper.Native.GetSystemMetrics(AeroGlassHelper.Native.SM_CYVIRTUALSCREEN);

            int left = Math.Max(window.Left - margin, screenLeft);
            int top = Math.Max(window.Top - margin, screenTop);
            int right = Math.Min(window.Right + margin, screenRight);
            int bottom = Math.Min(window.Bottom + margin, screenBottom);

            if (right <= left || bottom <= top)
            {
                return null;
            }

            int readWidth = right - left;
            int readHeight = bottom - top;
            byte[] pixels = reader.Read(left, top, readWidth, readHeight);

            if (pixels == null)
            {
                return null;
            }

            ulong signature = Signature(pixels, window, readWidth, readHeight, radius);

            if (!force && signature == lastSignature)
            {
                return null;
            }

            lastSignature = signature;

            BoxBlur(pixels, readWidth, readHeight, radius, BlurPasses);

            // Crop to the window; parts of the window off the screen take the nearest edge of what was read
            byte[] output = new byte[width * height * 4];
            int stride = width * 4;

            for (int y = 0; y < height; y++)
            {
                int sourceY = Math.Clamp(window.Top + y - top, 0, readHeight - 1);
                int sourceRow = sourceY * readWidth * 4;
                int outputRow = y * stride;

                for (int x = 0; x < width; x++)
                {
                    int sourceX = Math.Clamp(window.Left + x - left, 0, readWidth - 1);
                    int source = sourceRow + sourceX * 4;
                    int target = outputRow + x * 4;
                    output[target] = pixels[source];
                    output[target + 1] = pixels[source + 1];
                    output[target + 2] = pixels[source + 2];
                    output[target + 3] = 0xFF;
                }
            }

            BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, output, stride);
            bitmap.Freeze();
            return bitmap;
        }

        private static double GetScale(IntPtr hwnd)
        {
            try
            {
                uint dpi = AeroGlassHelper.Native.GetDpiForWindow(hwnd);
                return dpi > 0 ? dpi / 96.0 : 1.0;
            }
            catch (Exception)
            {
                // Windows before 10 (1607) has no per-window DPI
                return 1.0;
            }
        }

        /// <summary>A fingerprint of the pixels read and where the window is, to skip frames where nothing changed.</summary>
        private static ulong Signature(byte[] pixels, AeroGlassHelper.Native.RECT window, int width, int height, int radius)
        {
            ulong hash = 14695981039346656037UL;

            for (int i = 0; i + 3 < pixels.Length; i += 4)
            {
                hash ^= BitConverter.ToUInt32(pixels, i);
                hash *= 1099511628211UL;
            }

            hash ^= (ulong)(uint)window.Left | ((ulong)(uint)window.Top << 32);
            hash *= 1099511628211UL;
            hash ^= (ulong)(uint)width | ((ulong)(uint)height << 32);
            hash *= 1099511628211UL;
            hash ^= (ulong)(uint)radius;
            return hash;
        }

        /// <summary>
        /// Blurs 32-bit BGRA pixels in place with repeated box blurs, horizontally then vertically, clamping at the
        /// edges. Three passes make a close approximation of a Gaussian blur.
        /// </summary>
        private static void BoxBlur(byte[] pixels, int width, int height, int radius, int passes)
        {
            byte[] scratch = new byte[pixels.Length];

            for (int pass = 0; pass < passes; pass++)
            {
                BlurLines(pixels, scratch, width, height, radius, 4, width * 4);
                BlurLines(scratch, pixels, height, width, radius, width * 4, 4);
            }
        }

        /// <summary>
        /// One box blur along lines of pixels: each of the lineCount lines has length pixels, step bytes apart, and
        /// lines start lineStep bytes apart.
        /// </summary>
        private static void BlurLines(byte[] source, byte[] target, int length, int lineCount, int radius, int step, int lineStep)
        {
            int window = radius * 2 + 1;
            int last = length - 1;

            for (int line = 0; line < lineCount; line++)
            {
                int start = line * lineStep;

                for (int channel = 0; channel < 3; channel++)
                {
                    int origin = start + channel;
                    int sum = source[origin] * (radius + 1);

                    for (int i = 1; i <= radius; i++)
                    {
                        sum += source[origin + Math.Min(i, last) * step];
                    }

                    for (int i = 0; i < length; i++)
                    {
                        target[origin + i * step] = (byte)(sum / window);

                        int add = Math.Min(i + radius + 1, last);
                        int remove = Math.Max(i - radius, 0);
                        sum += source[origin + add * step] - source[origin + remove * step];
                    }
                }

                for (int i = 0; i < length; i++)
                {
                    target[start + i * step + 3] = 0xFF;
                }
            }
        }

        private void StopLoop()
        {
            if (_loopCancellation != null)
            {
                _loopCancellation.Cancel();
                _loopCancellation.Dispose();
                _loopCancellation = null;
            }
        }

        private void SaveBackground()
        {
            if (_backgroundSaved)
            {
                return;
            }

            _savedBackground = _window.ReadLocalValue(Window.BackgroundProperty);
            _backgroundSaved = true;
        }

        private void RestoreBackground()
        {
            if (!_backgroundSaved)
            {
                return;
            }

            if (_savedBackground == DependencyProperty.UnsetValue)
            {
                _window.ClearValue(Window.BackgroundProperty);
            }
            else
            {
                _window.SetValue(Window.BackgroundProperty, _savedBackground);
            }

            _savedBackground = null;
            _backgroundSaved = false;
        }

        private static Brush CreateOpaqueBacking()
        {
            SolidColorBrush brush = new SolidColorBrush(Colors.Black);
            brush.Freeze();
            return brush;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Utilities/AeroGlassHelper.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Utilities/InvertEffect.cs">
namespace RetroBar.Utilities
{
    using ManagedShell.Common.Logging;
    using System;
    using System.Windows;
    using System.Windows.Media;
    using System.Windows.Media.Effects;

    // Courtesy of https://stackoverflow.com/a/45096471
    public class InvertEffect : ShaderEffect
    {
        private static readonly PixelShader _shader =
            new PixelShader { UriSource = new Uri("pack://application:,,,/Resources/shader_invert.ps") };

        public InvertEffect()
        {
            PixelShader = _shader;
            PixelShader.InvalidPixelShaderEncountered += PixelShader_InvalidPixelShaderEncountered;
            UpdateShaderValue(InputProperty);
        }

        private void PixelShader_InvalidPixelShaderEncountered(object sender, EventArgs e)
        {
            ShellLogger.Error("InvertEffect: The given pixel shader is not valid.");
        }

        public Brush Input
        {
            get { return (Brush)GetValue(InputProperty); }
            set { SetValue(InputProperty, value); }
        }

        public static readonly DependencyProperty InputProperty =
            RegisterPixelShaderSamplerProperty("Input", typeof(InvertEffect), 0);
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Utilities/InvertEffect.cs">

// [1700] SUPERBAR :: the Windows 7 taskbar's pinned programs and the grouping of windows into buttons

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Superbar/TaskbarPins.cs">
namespace RetroBar.Superbar
{
    using ManagedShell.Common.Logging;
    using ManagedShell.WindowsTasks;
    using RetroBar.StartMenu;
    using RetroBar.Utilities;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Windows.Media;

    /// <summary>
    /// A program pinned to the Windows 7 taskbar, resolved from its key.
    /// </summary>
    internal sealed class PinnedProgram
    {
        /// <summary>The key stored in Settings.TaskbarPinnedItems ("file:" or "app:").</summary>
        public string Key { get; init; }

        public string Name { get; init; }

        public ImageSource Icon { get; init; }

        /// <summary>Full path of the program the pin starts, used to match its running windows; null if unknown.</summary>
        public string ProgramPath { get; init; }

        /// <summary>AppUserModelID of the pin, used to match windows of Store apps and of programs that set one.</summary>
        public string AppId { get; init; }

        /// <summary>The entry used to start the program, the same way the Start menu starts it.</summary>
        public StartMenuEntry Entry { get; init; }
    }

    /// <summary>
    /// Reads, resolves and matches the programs pinned to the Windows 7 taskbar.
    /// Pins use the Start menu's keys: "file:" for a shortcut or program and "app:" for an Apps folder ID.
    /// </summary>
    internal static class TaskbarPins
    {
        private const string ExplorerAppId = "Microsoft.Windows.Explorer";
        private const uint SHGFI_DISPLAYNAME = 0x000000200;
        private static readonly Guid PKEY_AppUserModel_ID_FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
        private const uint PKEY_AppUserModel_ID_PropertyId = 5;
        private const ushort VT_LPWSTR = 31;

        /// <summary>The folder where Windows keeps the shortcuts of its own taskbar pins.</summary>
        public static string WindowsPinnedFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");

        /// <summary>
        /// The first time the superbar is used, copies Windows' own taskbar pins into the settings, so the
        /// taskbar starts with the programs the user already pinned. Later changes are Retrostar's own.
        /// </summary>
        public static void ImportWindowsPinsOnce()
        {
            if (Settings.Instance.TaskbarPinsImported)
            {
                return;
            }

            var keys = new List<string>(Settings.Instance.TaskbarPinnedItems ?? new List<string>());

            try
            {
                string folder = WindowsPinnedFolder;

                if (Directory.Exists(folder))
                {
                    foreach (string file in Directory.GetFiles(folder, "*.lnk").OrderBy(f => Path.GetFileName(f), StringComparer.CurrentCultureIgnoreCase))
                    {
                        string key = StartMenuEntry.FilePrefix + file;

                        if (!keys.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)))
                        {
                            keys.Add(key);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ShellLogger.Debug($"TaskbarPins: Unable to read Windows taskbar pins: {e.Message}");
            }

            Settings.Instance.TaskbarPinnedItems = keys;
            Settings.Instance.TaskbarPinsImported = true;
        }

        public static bool IsPinned(string key)
        {
            return key != null && (Settings.Instance.TaskbarPinnedItems ?? new List<string>())
                .Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
        }

        public static void Pin(string key)
        {
            if (string.IsNullOrEmpty(key) || IsPinned(key))
            {
                return;
            }

            var keys = new List<string>(Settings.Instance.TaskbarPinnedItems ?? new List<string>()) { key };
            Settings.Instance.TaskbarPinnedItems = keys;
        }

        public static void Unpin(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            Settings.Instance.TaskbarPinnedItems = (Settings.Instance.TaskbarPinnedItems ?? new List<string>())
                .Where(k => !string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>
        /// The key that pins the program of a running window: its Apps folder ID for a Store app, otherwise its program file.
        /// </summary>
        public static string KeyForWindow(ApplicationWindow window)
        {
            if (window == null)
            {
                return null;
            }

            if (window.IsUWP)
            {
                string appId = window.AppUserModelID;
                return string.IsNullOrEmpty(appId) ? null : StartMenuEntry.AppPrefix + appId;
            }

            string file = window.WinFileName;
            return !string.IsNullOrEmpty(file) && File.Exists(file) ? StartMenuEntry.FilePrefix + file : null;
        }

        /// <summary>
        /// Whether a window belongs to a pinned program. Store apps match by ID; other programs by ID or program file.
        /// </summary>
        public static bool Matches(PinnedProgram pin, ApplicationWindow window)
        {
            if (pin == null || window == null)
            {
                return false;
            }

            string appId = window.AppUserModelID;

            if (!string.IsNullOrEmpty(pin.AppId) && !string.IsNullOrEmpty(appId) &&
                string.Equals(pin.AppId, appId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (window.IsUWP)
            {
                return false;
            }

            return !string.IsNullOrEmpty(pin.ProgramPath) &&
                   string.Equals(pin.ProgramPath, window.WinFileName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolves a pinned key to its name, icon and program. Uses shell COM objects, so it must run on an STA thread.
        /// Returns null for a key whose program no longer exists.
        /// </summary>
        public static PinnedProgram Resolve(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            try
            {
                if (key.StartsWith(StartMenuEntry.AppPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string appId = key.Substring(StartMenuEntry.AppPrefix.Length);
                    string parsingName = "shell:AppsFolder\\" + appId;
                    string name = GetDisplayName(parsingName);

                    if (string.IsNullOrEmpty(name))
                    {
                        // The app is no longer installed
                        return null;
                    }

                    return new PinnedProgram
                    {
                        Key = key,
                        Name = name,
                        AppId = appId,
                        ProgramPath = ProgramPathFromAppId(appId),
                        Icon = ShellIcons.FromParsingName(parsingName, ShellIconSize.Large),
                        Entry = new StartMenuEntry { Kind = StartMenuEntryKind.App, Target = appId, Name = name }
                    };
                }

                if (key.StartsWith(StartMenuEntry.FilePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string path = key.Substring(StartMenuEntry.FilePrefix.Length);

                    if (!File.Exists(path))
                    {
                        return null;
                    }

                    string name = Path.GetFileNameWithoutExtension(path);
                    string programPath = path;
                    string appId = null;

                    if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        programPath = GetShortcutTarget(path);
                        appId = GetShortcutAppId(path);
                    }
                    else if (string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        // Windows 7 names a pinned program after its description, as the shortcut it creates does
                        string description = FileVersionInfo.GetVersionInfo(path).FileDescription;

                        if (!string.IsNullOrWhiteSpace(description))
                        {
                            name = description.Trim();
                        }
                    }

                    if (string.IsNullOrEmpty(programPath) && string.Equals(appId, ExplorerAppId, StringComparison.OrdinalIgnoreCase))
                    {
                        // The File Explorer shortcut points at a shell folder rather than at explorer.exe
                        programPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                    }

                    return new PinnedProgram
                    {
                        Key = key,
                        Name = name,
                        AppId = appId,
                        ProgramPath = string.IsNullOrEmpty(programPath) ? null : programPath,
                        Icon = ShellIcons.FromPath(path, ShellIconSize.Large),
                        Entry = new StartMenuEntry { Kind = StartMenuEntryKind.File, Target = path, Name = name }
                    };
                }
            }
            catch (Exception e)
            {
                ShellLogger.Debug($"TaskbarPins: Unable to resolve {key}: {e.Message}");
            }

            return null;
        }

        private static string GetDisplayName(string parsingName)
        {
            IntPtr pidl = IntPtr.Zero;

            try
            {
                if (StartMenuNative.SHParseDisplayName(parsingName, IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
                {
                    return null;
                }

                var info = new StartMenuNative.SHFILEINFO();

                if (StartMenuNative.SHGetFileInfoPidl(pidl, 0, ref info, (uint)Marshal.SizeOf(info), StartMenuNative.SHGFI_PIDL | SHGFI_DISPLAYNAME) == IntPtr.Zero)
                {
                    return null;
                }

                return string.IsNullOrWhiteSpace(info.szDisplayName) ? null : info.szDisplayName;
            }
            catch (Exception e)
            {
                ShellLogger.Debug($"TaskbarPins: Unable to get the name of {parsingName}: {e.Message}");
                return null;
            }
            finally
            {
                if (pidl != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
        }

        /// <summary>
        /// Desktop programs without an ID of their own are listed in the Apps folder by a known-folder path,
        /// such as "{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\notepad.exe". Returns that program's full path.
        /// </summary>
        private static string ProgramPathFromAppId(string appId)
        {
            if (string.IsNullOrEmpty(appId))
            {
                return null;
            }

            if (Path.IsPathRooted(appId) && appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return appId;
            }

            int close = appId.IndexOf('}');

            if (appId[0] != '{' || close < 0 || close + 2 >= appId.Length || appId[close + 1] != '\\' ||
                !Guid.TryParse(appId.Substring(0, close + 1), out Guid folderId))
            {
                return null;
            }

            if (SHGetKnownFolderPath(folderId, 0, IntPtr.Zero, out IntPtr folderPath) != 0 || folderPath == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                string folder = Marshal.PtrToStringUni(folderPath);
                return string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, appId.Substring(close + 2));
            }
            finally
            {
                Marshal.FreeCoTaskMem(folderPath);
            }
        }

        private static string GetShortcutTarget(string shortcutPath)
        {
            object link = null;

            try
            {
                link = new ShellLinkObject();
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Load(shortcutPath, 0);

                var target = new StringBuilder(1024);
                ((IShellLinkW)link).GetPath(target, target.Capacity, IntPtr.Zero, 0);

                string path = target.ToString();
                return string.IsNullOrWhiteSpace(path) ? null : Environment.ExpandEnvironmentVariables(path);
            }
            catch (Exception e)
            {
                ShellLogger.Debug($"TaskbarPins: Unable to read shortcut {shortcutPath}: {e.Message}");
                return null;
            }
            finally
            {
                if (link != null && Marshal.IsComObject(link))
                {
                    Marshal.FinalReleaseComObject(link);
                }
            }
        }

        private static string GetShortcutAppId(string shortcutPath)
        {
            IPropertyStore store = null;

            try
            {
                Guid iid = typeof(IPropertyStore).GUID;

                if (SHGetPropertyStoreFromParsingName(shortcutPath, IntPtr.Zero, 0, ref iid, out store) != 0 || store == null)
                {
                    return null;
                }

                var key = new PropertyKey { fmtid = PKEY_AppUserModel_ID_FormatId, pid = PKEY_AppUserModel_ID_PropertyId };

                if (store.GetValue(ref key, out PropVariant value) != 0)
                {
                    return null;
                }

                try
                {
                    return value.vt == VT_LPWSTR && value.data != IntPtr.Zero ? Marshal.PtrToStringUni(value.data) : null;
                }
                finally
                {
                    PropVariantClear(ref value);
                }
            }
            catch (Exception e)
            {
                ShellLogger.Debug($"TaskbarPins: Unable to read the app ID of {shortcutPath}: {e.Message}");
                return null;
            }
            finally
            {
                if (store != null && Marshal.IsComObject(store))
                {
                    Marshal.FinalReleaseComObject(store);
                }
            }
        }

        #region Shell interop

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetPropertyStoreFromParsingName(string pszPath, IntPtr pbc, int flags, ref Guid riid, out IPropertyStore ppv);

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant pvar);

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid fmtid;
            public uint pid;
        }

        // PROPVARIANT: a 2-byte type, 6 reserved bytes and a 16-byte (64-bit) or 8-byte (32-bit) value
        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant
        {
            public ushort vt;
            public ushort reserved1;
            public ushort reserved2;
            public ushort reserved3;
            public IntPtr data;
            public IntPtr data2;
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        private interface IPropertyStore
        {
            [PreserveSig]
            int GetCount(out uint cProps);

            [PreserveSig]
            int GetAt(uint iProp, out PropertyKey pkey);

            [PreserveSig]
            int GetValue(ref PropertyKey key, out PropVariant pv);

            [PreserveSig]
            int SetValue(ref PropertyKey key, ref PropVariant propvar);

            [PreserveSig]
            int Commit();
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLinkObject
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        #endregion
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Superbar/TaskbarPins.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Superbar/SuperbarModel.cs">
namespace RetroBar.Superbar
{
    using ManagedShell.Common.Logging;
    using ManagedShell.Interop;
    using ManagedShell.WindowsTasks;
    using RetroBar.StartMenu;
    using RetroBar.Utilities;
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;

    /// <summary>
    /// One button of the Windows 7 taskbar: a pinned program, a running program, or both, with all of its windows.
    /// Themes bind to these properties from the button's template.
    /// </summary>
    internal sealed class SuperbarItem : INotifyPropertyChanged
    {
        // Glow used for icons without a clear colour, a light sky blue
        private static readonly Color DefaultHotTrackColor = Color.FromRgb(0x8C, 0xC8, 0xFF);

        public event PropertyChangedEventHandler PropertyChanged;

        public SuperbarItem(string key)
        {
            Key = key;
        }

        /// <summary>Identity of the button: its pin, or the ID or program file its windows share.</summary>
        public string Key { get; }

        public ObservableCollection<ApplicationWindow> Windows { get; } = new ObservableCollection<ApplicationWindow>();

        public PinnedProgram Pin { get; private set; }

        public bool IsPinned => Pin != null;

        public bool IsRunning => Windows.Count > 0;

        public int WindowCount => Windows.Count;

        /// <summary>Two or more windows: one stacked edge is drawn behind the button.</summary>
        public bool HasStack => Windows.Count >= 2;

        /// <summary>Three or more windows: two stacked edges are drawn behind the button.</summary>
        public bool HasDoubleStack => Windows.Count >= 3;

        public ApplicationWindow FirstWindow => Windows.Count > 0 ? Windows[0] : null;

        public string Name => Pin?.Name ?? FirstWindow?.Title ?? string.Empty;

        /// <summary>Text for the button's tooltip: the window title for one window, otherwise the program name.</summary>
        public string ToolTipText => Windows.Count == 1 ? FirstWindow?.Title ?? Name : Name;

        public ImageSource Icon => Pin?.Icon ?? FirstWindow?.Icon;

        public bool IsActive => Windows.Any(w => w.State == ApplicationWindow.WindowState.Active);

        public bool IsFlashing => !IsActive && Windows.Any(w => w.State == ApplicationWindow.WindowState.Flashing);

        /// <summary>The window whose taskbar overlay icon (badge) is shown on the button.</summary>
        public ApplicationWindow OverlayWindow => Windows.FirstOrDefault(w => w.OverlayIcon != null);

        private ApplicationWindow ProgressWindow => Windows.FirstOrDefault(w => w.ProgressState != NativeMethods.TBPFLAG.TBPF_NOPROGRESS);

        public bool ShowsProgress => ProgressWindow != null;

        public bool IsProgressIndeterminate => ProgressWindow?.ProgressState == NativeMethods.TBPFLAG.TBPF_INDETERMINATE;

        // TBPF_ERROR and TBPF_PAUSED, as defined by ITaskbarList3
        public bool IsProgressError => ProgressWindow is ApplicationWindow w && (int)w.ProgressState == 0x4;

        public bool IsProgressPaused => ProgressWindow is ApplicationWindow w && (int)w.ProgressState == 0x8;

        /// <summary>Progress from 0 to 1; a full bar while the progress is indeterminate.</summary>
        public double ProgressFraction
        {
            get
            {
                ApplicationWindow window = ProgressWindow;

                if (window == null)
                {
                    return 0;
                }

                if (window.ProgressState == NativeMethods.TBPFLAG.TBPF_INDETERMINATE)
                {
                    return 1;
                }

                return Math.Max(0, Math.Min(1, window.ProgressValue / 65534.0));
            }
        }

        private Point _hotTrackCenter = new Point(0.5, 1);
        /// <summary>Centre of the hover glow, relative to the button; follows the mouse across the button.</summary>
        public Point HotTrackCenter
        {
            get => _hotTrackCenter;
            set
            {
                if (_hotTrackCenter != value)
                {
                    _hotTrackCenter = value;
                    OnPropertyChanged();
                }
            }
        }

        private Color _hotTrackColor = DefaultHotTrackColor;
        /// <summary>Colour of the hover glow, taken from the icon's dominant colour as Windows 7 does.</summary>
        public Color HotTrackColor
        {
            get => _hotTrackColor;
            private set
            {
                if (_hotTrackColor != value)
                {
                    _hotTrackColor = value;
                    OnPropertyChanged();
                }
            }
        }

        private ImageSource _hotTrackSource;

        /// <summary>Applies a new pin and set of windows, raising change notifications only for what changed.</summary>
        public void Update(PinnedProgram pin, List<ApplicationWindow> windows)
        {
            bool pinChanged = !ReferenceEquals(Pin, pin);
            Pin = pin;

            bool windowsChanged = false;

            for (int i = Windows.Count - 1; i >= 0; i--)
            {
                if (!windows.Contains(Windows[i]))
                {
                    Windows[i].PropertyChanged -= Window_PropertyChanged;
                    Windows.RemoveAt(i);
                    windowsChanged = true;
                }
            }

            for (int i = 0; i < windows.Count; i++)
            {
                int current = Windows.IndexOf(windows[i]);

                if (current < 0)
                {
                    windows[i].PropertyChanged += Window_PropertyChanged;
                    Windows.Insert(i, windows[i]);
                    windowsChanged = true;
                }
                else if (current != i)
                {
                    Windows.Move(current, i);
                    windowsChanged = true;
                }
            }

            if (pinChanged)
            {
                OnPropertyChanged(nameof(Pin));
                OnPropertyChanged(nameof(IsPinned));
            }

            if (pinChanged || windowsChanged)
            {
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(WindowCount));
                OnPropertyChanged(nameof(HasStack));
                OnPropertyChanged(nameof(HasDoubleStack));
                OnPropertyChanged(nameof(FirstWindow));
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(ToolTipText));
                OnPropertyChanged(nameof(Icon));
                RaiseStateChanged();
                RaiseProgressChanged();
                UpdateHotTrackColor();
            }
        }

        /// <summary>Stops observing the windows; called when the button is removed.</summary>
        public void Detach()
        {
            foreach (ApplicationWindow window in Windows)
            {
                window.PropertyChanged -= Window_PropertyChanged;
            }

            Windows.Clear();
        }

        private void Window_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "State":
                    RaiseStateChanged();
                    break;
                case "Title":
                    OnPropertyChanged(nameof(Name));
                    OnPropertyChanged(nameof(ToolTipText));
                    break;
                case "Icon":
                    OnPropertyChanged(nameof(Icon));
                    UpdateHotTrackColor();
                    break;
                case "OverlayIcon":
                    OnPropertyChanged(nameof(OverlayWindow));
                    break;
                case "ProgressState":
                case "ProgressValue":
                    RaiseProgressChanged();
                    break;
            }
        }

        private void RaiseStateChanged()
        {
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(IsFlashing));
            OnPropertyChanged(nameof(OverlayWindow));
        }

        private void RaiseProgressChanged()
        {
            OnPropertyChanged(nameof(ShowsProgress));
            OnPropertyChanged(nameof(IsProgressIndeterminate));
            OnPropertyChanged(nameof(IsProgressError));
            OnPropertyChanged(nameof(IsProgressPaused));
            OnPropertyChanged(nameof(ProgressFraction));
        }

        private void UpdateHotTrackColor()
        {
            ImageSource icon = Icon;

            if (ReferenceEquals(icon, _hotTrackSource))
            {
                return;
            }

            _hotTrackSource = icon;
            HotTrackColor = GetDominantColor(icon) ?? DefaultHotTrackColor;
        }

        /// <summary>
        /// The colour the hover glow takes: the most common clearly coloured hue of the icon, at full brightness.
        /// Grey icons have none, and get the default glow.
        /// </summary>
        internal static Color? GetDominantColor(ImageSource icon)
        {
            if (icon is not BitmapSource source || source.PixelWidth == 0 || source.PixelHeight == 0)
            {
                return null;
            }

            try
            {
                BitmapSource bitmap = source.Format == PixelFormats.Bgra32
                    ? source
                    : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

                int width = bitmap.PixelWidth;
                int height = bitmap.PixelHeight;
                int stride = width * 4;
                byte[] pixels = new byte[stride * height];
                bitmap.CopyPixels(pixels, stride, 0);

                const int buckets = 24;
                double[] weight = new double[buckets];
                double[] red = new double[buckets];
                double[] green = new double[buckets];
                double[] blue = new double[buckets];

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte a = pixels[i + 3];

                    if (a < 128)
                    {
                        continue;
                    }

                    double b = pixels[i];
                    double g = pixels[i + 1];
                    double r = pixels[i + 2];
                    double max = Math.Max(r, Math.Max(g, b));
                    double min = Math.Min(r, Math.Min(g, b));

                    if (max < 50)
                    {
                        continue;
                    }

                    double saturation = (max - min) / max;

                    if (saturation < 0.25)
                    {
                        continue;
                    }

                    double hue;
                    double delta = max - min;

                    if (max == r)
                    {
                        hue = ((g - b) / delta + 6) % 6;
                    }
                    else if (max == g)
                    {
                        hue = (b - r) / delta + 2;
                    }
                    else
                    {
                        hue = (r - g) / delta + 4;
                    }

                    int bucket = Math.Min(buckets - 1, (int)(hue / 6 * buckets));
                    double w = saturation * (max / 255.0) * (a / 255.0);

                    weight[bucket] += w;
                    red[bucket] += r * w;
                    green[bucket] += g * w;
                    blue[bucket] += b * w;
                }

                int best = 0;

                for (int i = 1; i < buckets; i++)
                {
                    if (weight[i] > weight[best])
                    {
                        best = i;
                    }
                }

                if (weight[best] <= 0)
                {
                    return null;
                }

                double rr = red[best] / weight[best];
                double gg = green[best] / weight[best];
                double bb = blue[best] / weight[best];
                double scale = 255.0 / Math.Max(1, Math.Max(rr, Math.Max(gg, bb)));

                return Color.FromRgb(
                    (byte)Math.Min(255, Math.Round(rr * scale)),
                    (byte)Math.Min(255, Math.Round(gg * scale)),
                    (byte)Math.Min(255, Math.Round(bb * scale)));
            }
            catch (Exception e)
            {
                ShellLogger.Debug($"SuperbarItem: Unable to read icon colours: {e.Message}");
                return null;
            }
        }

        /// <summary>The key that groups an unpinned window: its AppUserModelID, else its program file.</summary>
        public static string GroupKeyFor(ApplicationWindow window)
        {
            string appId = window.AppUserModelID;

            if (!string.IsNullOrEmpty(appId))
            {
                return "id:" + appId.ToLowerInvariant();
            }

            string file = window.WinFileName;

            if (!string.IsNullOrEmpty(file))
            {
                return "exe:" + file.ToLowerInvariant();
            }

            return "hwnd:" + window.Handle.ToInt64();
        }

        public static string GroupKeyFor(PinnedProgram pin)
        {
            return "pin:" + pin.Key.ToLowerInvariant();
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// The buttons of one Windows 7 taskbar: pinned programs first, in their pinned order, then running programs in
    /// the order they started. Each program's windows are combined into one button, as Windows 7 does by default.
    /// </summary>
    internal sealed class SuperbarModel : IDisposable
    {
        public ObservableCollection<SuperbarItem> Items { get; } = new ObservableCollection<SuperbarItem>();

        private readonly ICollectionView _windows;
        private readonly Dictionary<string, PinnedProgram> _pins = new Dictionary<string, PinnedProgram>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _pinnedKeys = new List<string>();
        private readonly List<string> _unpinnedOrder = new List<string>();
        private bool _disposed;
        private int _pinLoadVersion;

        public SuperbarModel(Tasks tasks, Predicate<object> filter)
        {
            TaskbarPins.ImportWindowsPinsOnce();

            _windows = tasks.CreateGroupedWindowsCollection();

            if (_windows != null)
            {
                _windows.Filter = filter;
                _windows.CollectionChanged += Windows_CollectionChanged;
            }

            Settings.Instance.PropertyChanged += Settings_PropertyChanged;

            _pinnedKeys.AddRange(Settings.Instance.TaskbarPinnedItems ?? new List<string>());
            Rebuild();
            _ = LoadPinsAsync();
        }

        /// <summary>Applies the window filter again, after a change to which windows this taskbar shows.</summary>
        public void RefreshFilter()
        {
            _windows?.Refresh();
            Rebuild();
        }

        public bool IsReorderable(SuperbarItem item)
        {
            return item != null && Items.Contains(item);
        }

        /// <summary>
        /// Moves a button while it is dragged. Pinned buttons move among the pinned buttons and running buttons among
        /// the running ones. The order is kept for the session; call SavePinnedOrder when the drag ends.
        /// </summary>
        public void Move(SuperbarItem item, int newIndex)
        {
            int oldIndex = Items.IndexOf(item);

            if (oldIndex < 0)
            {
                return;
            }

            int pinnedCount = Items.Count(i => i.IsPinned);
            int first = item.IsPinned ? 0 : pinnedCount;
            int last = item.IsPinned ? pinnedCount - 1 : Items.Count - 1;
            newIndex = Math.Max(first, Math.Min(last, newIndex));

            if (newIndex == oldIndex)
            {
                return;
            }

            Items.Move(oldIndex, newIndex);

            if (item.IsPinned)
            {
                _pinnedKeys.Clear();
                _pinnedKeys.AddRange(Items.Where(i => i.IsPinned).Select(i => i.Pin.Key));
            }
            else
            {
                _unpinnedOrder.Clear();
                _unpinnedOrder.AddRange(Items.Where(i => !i.IsPinned).Select(i => i.Key));
            }
        }

        /// <summary>Saves the pinned order after a drag, keeping pins that did not resolve in their places at the end.</summary>
        public void SavePinnedOrder()
        {
            List<string> saved = Settings.Instance.TaskbarPinnedItems ?? new List<string>();
            var order = new List<string>(_pinnedKeys);

            foreach (string key in saved)
            {
                if (!order.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)))
                {
                    order.Add(key);
                }
            }

            if (!order.SequenceEqual(saved, StringComparer.OrdinalIgnoreCase))
            {
                Settings.Instance.TaskbarPinnedItems = order;
            }
        }

        private void Windows_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            Rebuild();
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.TaskbarPinnedItems))
            {
                _pinnedKeys.Clear();
                _pinnedKeys.AddRange(Settings.Instance.TaskbarPinnedItems ?? new List<string>());
                Rebuild();
                _ = LoadPinsAsync();
            }
        }

        /// <summary>Resolves pins that have not been resolved yet, off the UI thread, then rebuilds the buttons.</summary>
        private async Task LoadPinsAsync()
        {
            int version = ++_pinLoadVersion;
            List<string> missing = _pinnedKeys.Where(k => !_pins.ContainsKey(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (missing.Count == 0)
            {
                return;
            }

            List<PinnedProgram> resolved;

            try
            {
                resolved = await StaTask.Run(() => missing.Select(TaskbarPins.Resolve).ToList());
            }
            catch (Exception e)
            {
                ShellLogger.Warning($"SuperbarModel: Unable to load pinned programs: {e.Message}");
                return;
            }

            if (_disposed || version != _pinLoadVersion)
            {
                return;
            }

            for (int i = 0; i < missing.Count; i++)
            {
                // A pin that did not resolve is remembered as null, so it is not looked up again
                _pins[missing[i]] = resolved[i];
            }

            Rebuild();
        }

        private void Rebuild()
        {
            if (_disposed)
            {
                return;
            }

            var pinned = new List<PinnedProgram>();

            foreach (string key in _pinnedKeys)
            {
                if (_pins.TryGetValue(key, out PinnedProgram pin) && pin != null && !pinned.Contains(pin))
                {
                    pinned.Add(pin);
                }
            }

            var groupPins = new Dictionary<string, PinnedProgram>(StringComparer.Ordinal);
            var groupWindows = new Dictionary<string, List<ApplicationWindow>>(StringComparer.Ordinal);
            var pinnedGroupKeys = new List<string>();
            var runningGroupKeys = new List<string>();

            foreach (PinnedProgram pin in pinned)
            {
                string key = SuperbarItem.GroupKeyFor(pin);

                if (!groupWindows.ContainsKey(key))
                {
                    groupPins[key] = pin;
                    groupWindows[key] = new List<ApplicationWindow>();
                    pinnedGroupKeys.Add(key);
                }
            }

            if (_windows != null)
            {
                foreach (object entry in _windows)
                {
                    if (entry is not ApplicationWindow window)
                    {
                        continue;
                    }

                    PinnedProgram match = pinned.FirstOrDefault(p => TaskbarPins.Matches(p, window));
                    string key = match != null ? SuperbarItem.GroupKeyFor(match) : SuperbarItem.GroupKeyFor(window);

                    if (!groupWindows.TryGetValue(key, out List<ApplicationWindow> list))
                    {
                        list = new List<ApplicationWindow>();
                        groupWindows[key] = list;
                        groupPins[key] = null;
                        runningGroupKeys.Add(key);
                    }

                    list.Add(window);
                }
            }

            // Running programs keep their place; new ones are added at the end
            _unpinnedOrder.RemoveAll(k => !runningGroupKeys.Contains(k));

            foreach (string key in runningGroupKeys)
            {
                if (!_unpinnedOrder.Contains(key))
                {
                    _unpinnedOrder.Add(key);
                }
            }

            var desired = new List<string>(pinnedGroupKeys);
            desired.AddRange(_unpinnedOrder);

            for (int i = Items.Count - 1; i >= 0; i--)
            {
                if (!groupWindows.ContainsKey(Items[i].Key))
                {
                    Items[i].Detach();
                    Items.RemoveAt(i);
                }
            }

            for (int i = 0; i < desired.Count; i++)
            {
                string key = desired[i];
                SuperbarItem item = null;

                for (int j = i; j < Items.Count; j++)
                {
                    if (Items[j].Key == key)
                    {
                        item = Items[j];

                        if (j != i)
                        {
                            Items.Move(j, i);
                        }

                        break;
                    }
                }

                if (item == null)
                {
                    item = new SuperbarItem(key);
                    Items.Insert(i, item);
                }

                item.Update(groupPins[key], groupWindows[key]);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;

            if (_windows != null)
            {
                _windows.CollectionChanged -= Windows_CollectionChanged;
                _windows.Filter = null;
            }

            foreach (SuperbarItem item in Items)
            {
                item.Detach();
            }

            Items.Clear();
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Superbar/SuperbarModel.cs">

// [2000] START MENU ENGINE :: programs, search, icons, launching, the Windows key; no windows

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/StartMenu/StartMenuNative.cs">
namespace RetroBar.StartMenu
{
    using System;
    using System.Runtime.InteropServices;
    using System.Text;

    /// <summary>
    /// Win32 declarations used by the Start menu. Kept separate from ManagedShell's
    /// NativeMethods so the Start menu does not depend on its internal signatures.
    /// </summary>
    internal static class StartMenuNative
    {
        #region Icons

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        public const uint SHGFI_ICON = 0x000000100;
        public const uint SHGFI_SYSICONINDEX = 0x000004000;
        public const uint SHGFI_LARGEICON = 0x000000000;
        public const uint SHGFI_SMALLICON = 0x000000001;
        public const uint SHGFI_PIDL = 0x000000008;

        public const int SHIL_LARGE = 0x0;      // 32x32 at 96 DPI
        public const int SHIL_SMALL = 0x1;      // 16x16 at 96 DPI
        public const int SHIL_EXTRALARGE = 0x2; // 48x48 at 96 DPI

        public const int ILD_TRANSPARENT = 0x1;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")]
        public static extern IntPtr SHGetFileInfoPidl(IntPtr pidl, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll", EntryPoint = "SHGetImageList")]
        public static extern int SHGetImageList(int iImageList, ref Guid riid, out IntPtr ppv);

        [DllImport("comctl32.dll")]
        public static extern IntPtr ImageList_GetIcon(IntPtr himl, int i, int flags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        public static readonly Guid IID_IImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");

        #endregion

        #region File associations

        public const uint ASSOCF_IS_PROTOCOL = 0x00001000;
        public const uint ASSOCSTR_EXECUTABLE = 2;
        public const uint ASSOCSTR_FRIENDLYAPPNAME = 4;

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        public static extern int AssocQueryString(uint flags, uint str, string pszAssoc, string pszExtra, StringBuilder pszOut, ref uint pcchOut);

        #endregion

        #region Window regions

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

        [DllImport("gdi32.dll")]
        public static extern int CombineRgn(IntPtr hrgnDst, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        public const int RGN_OR = 2;

        #endregion

        #region Windows and focus

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_APPWINDOW = 0x00040000;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;

        #endregion

        #region Session and power

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool LockWorkStation();

        [DllImport("wtsapi32.dll", SetLastError = true)]
        public static extern bool WTSDisconnectSession(IntPtr hServer, int sessionId, bool bWait);

        public static readonly IntPtr WTS_CURRENT_SERVER_HANDLE = IntPtr.Zero;
        public const int WTS_CURRENT_SESSION = -1;

        #endregion

        #region Keyboard hook and input

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;

        public const int VK_CONTROL = 0x11;
        public const int VK_ESCAPE = 0x1B;
        public const int VK_LWIN = 0x5B;
        public const int VK_RWIN = 0x5C;

        // Unassigned virtual key, used to mask a lone Windows key press so Windows does not open its own Start menu
        public const ushort VK_MASK = 0xE8;

        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        public const uint KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        #endregion
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/StartMenu/StartMenuNative.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/StartMenu/StartMenuEntry.cs">
namespace RetroBar.StartMenu
{
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Runtime.CompilerServices;
    using System.Windows.Media;

    internal enum StartMenuEntryKind
    {
        /// <summary>A file or shortcut on disk, opened through the shell.</summary>
        File,
        /// <summary>An entry of the shell Apps folder; Target is its parsing path (an AUMID or known-folder path).</summary>
        App,
        /// <summary>A folder in the All Programs tree.</summary>
        Folder,
        /// <summary>A URI or shell location such as ms-settings: or shell:RecycleBinFolder.</summary>
        Location
    }

    /// <summary>
    /// One item shown in the Start menu: a program, a folder of programs, a search result or a link.
    /// </summary>
    internal class StartMenuEntry : INotifyPropertyChanged
    {
        public const string FilePrefix = "file:";
        public const string AppPrefix = "app:";

        public event PropertyChangedEventHandler PropertyChanged;

        public StartMenuEntryKind Kind { get; init; }

        /// <summary>File path, Apps folder parsing path, or URI depending on Kind.</summary>
        public string Target { get; init; }

        public string Name { get; init; }

        /// <summary>Second line shown under the name, as Vista does for its Internet and E-mail items.</summary>
        public string Subtitle { get; init; }

        /// <summary>Category label shown in bold above the name, used by the default pinned items.</summary>
        public bool IsBold { get; init; }

        public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);

        public bool IsFolder => Kind == StartMenuEntryKind.Folder;

        /// <summary>Pinned-list key that overrides the generated key, used by the default Internet and E-mail items.</summary>
        public string PinKey { get; init; }

        public ObservableCollection<StartMenuEntry> Children { get; } = new ObservableCollection<StartMenuEntry>();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        private ImageSource _icon;
        public ImageSource Icon
        {
            get => _icon;
            set
            {
                if (_icon != value)
                {
                    _icon = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Stable key used to store this entry in the pinned and recent lists.
        /// </summary>
        public string Key
        {
            get
            {
                if (!string.IsNullOrEmpty(PinKey))
                {
                    return PinKey;
                }

                return Kind switch
                {
                    StartMenuEntryKind.App => AppPrefix + Target,
                    StartMenuEntryKind.File => FilePrefix + Target,
                    _ => null
                };
            }
        }

        /// <summary>Whether this entry is a program that can be pinned or tracked as recently used.</summary>
        public bool IsProgram => Kind == StartMenuEntryKind.App || Kind == StartMenuEntryKind.File;

        public StartMenuEntry CloneWithIcon(ImageSource icon)
        {
            return new StartMenuEntry
            {
                Kind = Kind,
                Target = Target,
                Name = Name,
                Subtitle = Subtitle,
                IsBold = IsBold,
                PinKey = PinKey,
                Icon = icon ?? Icon
            };
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/StartMenu/StartMenuEntry.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/StartMenu/ShellIcons.cs">
namespace RetroBar.StartMenu
{
    using ManagedShell.Common.Logging;
    using System;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Interop;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;

    internal enum ShellIconSize
    {
        Small,      // 16 px at 96 DPI
        Large,      // 32 px at 96 DPI
        ExtraLarge  // 48 px at 96 DPI
    }

    /// <summary>
    /// Runs work on a short-lived single-threaded apartment thread, as the shell's COM objects require.
    /// </summary>
    internal static class StaTask
    {
        public static Task<T> Run<T>(Func<T> work)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            Thread thread = new Thread(() =>
            {
                try
                {
                    tcs.SetResult(work());
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            });

            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return tcs.Task;
        }
    }

    /// <summary>
    /// Loads icons from the system image list, which gives correctly sized, alpha-blended icons
    /// for files, folders and Apps folder (including Microsoft Store) entries.
    /// Must be called on an STA thread. Returned images are frozen and safe to use on any thread.
    /// </summary>
    internal static class ShellIcons
    {
        private static readonly object _imageListLock = new object();
        private static readonly IntPtr[] _imageLists = new IntPtr[3];

        public static ImageSource FromPath(string path, ShellIconSize size)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            try
            {
                var info = new StartMenuNative.SHFILEINFO();
                IntPtr result = StartMenuNative.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), StartMenuNative.SHGFI_SYSICONINDEX);

                if (result == IntPtr.Zero)
                {
                    return null;
                }

                return FromSystemIndex(info.iIcon, size);
            }
            catch (Exception e)
            {
                ShellLogger.Debug($"ShellIcons: Unable to get icon for {path}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the icon for any shell parsing name, such as "shell:AppsFolder\{AUMID}".
        /// </summary>
        public static ImageSource FromParsingName(string parsingName, ShellIconSize size)
        {
            if (string.IsNullOrEmpty(parsingName))
            {
                return null;
            }

            IntPtr pidl = IntPtr.Zero;

            try
            {
                if (StartMenuNative.SHParseDisplayName(parsingName, IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
                {
                    return null;
                }

                var info = new StartMenuNative.SHFILEINFO();
                IntPtr result = StartMenuNative.SHGetFileInfoPidl(pidl, 0, ref info, (uint)Marshal.SizeOf(info), StartMenuNative.SHGFI_PIDL | StartMenuNative.SHGFI_SYSICONINDEX);

                if (result == IntPtr.Zero)
                {
                    return null;
                }

                return FromSystemIndex(info.iIcon, size);
            }
            catch (Exception e)
            {
                ShellLogger.Debug($"ShellIcons: Unable to get icon for {parsingName}: {e.Message}");
                return null;
            }
            finally
            {
                if (pidl != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
        }

        private static ImageSource FromSystemIndex(int index, ShellIconSize size)
        {
            IntPtr imageList = GetImageList(size);

            if (imageList == IntPtr.Zero)
            {
                return null;
            }

            IntPtr hIcon = StartMenuNative.ImageList_GetIcon(imageList, index, StartMenuNative.ILD_TRANSPARENT);

            if (hIcon == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                StartMenuNative.DestroyIcon(hIcon);
            }
        }

        private static IntPtr GetImageList(ShellIconSize size)
        {
            int listIndex = size switch
            {
                ShellIconSize.Small => StartMenuNative.SHIL_SMALL,
                ShellIconSize.ExtraLarge => StartMenuNative.SHIL_EXTRALARGE,
                _ => StartMenuNative.SHIL_LARGE
            };

            lock (_imageListLock)
            {
                if (_imageLists[listIndex] == IntPtr.Zero)
                {
                    // The system image list is a process-wide singleton; it is kept for the life of the app.
                    Guid iid = StartMenuNative.IID_IImageList;

                    if (StartMenuNative.SHGetImageList(listIndex, ref iid, out IntPtr list) == 0)
                    {
                        _imageLists[listIndex] = list;
                    }
                }

                return _imageLists[listIndex];
            }
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/StartMenu/ShellIcons.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/StartMenu/AppCatalog.cs">
namespace RetroBar.StartMenu
{
    using ManagedShell.Common.Logging;
    using Microsoft.Win32;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;

    /// <summary>
    /// A snapshot of the installed programs: the All Programs tree built from the Start Menu folders,
    /// the flat list of apps Windows itself shows (including Microsoft Store apps), and usage counts.
    /// </summary>
    internal class AppCatalogSnapshot
    {
        public List<StartMenuEntry> AllProgramsRoot { get; init; } = new List<StartMenuEntry>();

        /// <summary>Every launchable program, used for search and for resolving pinned and recent keys.</summary>
        public List<StartMenuEntry> Programs { get; init; } = new List<StartMenuEntry>();

        /// <summary>Program keys ordered by how often Windows has recorded them being run.</summary>
        public List<string> FrequentKeys { get; init; } = new List<string>();

        public DateTime LoadedAt { get; init; }

        private Dictionary<string, StartMenuEntry> _byKey;

        public StartMenuEntry FindByKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            if (_byKey == null)
            {
                _byKey = new Dictionary<string, StartMenuEntry>(StringComparer.OrdinalIgnoreCase);

                foreach (StartMenuEntry entry in Programs)
                {
                    if (entry.Key != null && !_byKey.ContainsKey(entry.Key))
                    {
                        _byKey.Add(entry.Key, entry);
                    }
                }
            }

            return _byKey.TryGetValue(key, out StartMenuEntry found) ? found : null;
        }
    }

    internal static class AppCatalog
    {
        private const string AppsFolder = "shell:AppsFolder";

        // UserAssist key that records programs launched through the shell, as used by Vista's frequent programs list
        private const string UserAssistKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}\Count";

        private static readonly string[] ProgramExtensions = { ".lnk", ".url", ".appref-ms", ".exe", ".msc", ".website" };

        public static Task<AppCatalogSnapshot> LoadAsync()
        {
            return StaTask.Run(Load);
        }

        private static AppCatalogSnapshot Load()
        {
            List<StartMenuEntry> apps = LoadAppsFolder();
            List<StartMenuEntry> tree = LoadStartMenuTree();

            // Microsoft Store apps have no shortcut in the Start Menu folders, so list them at the top level of All Programs
            foreach (StartMenuEntry app in apps.Where(a => IsPackagedApp(a.Target)))
            {
                tree.Add(app);
            }

            SortLevel(tree);

            List<StartMenuEntry> programs = apps.Count > 0 ? apps : Flatten(tree).ToList();

            return new AppCatalogSnapshot
            {
                AllProgramsRoot = tree,
                Programs = programs,
                FrequentKeys = LoadFrequentKeys(),
                LoadedAt = DateTime.Now
            };
        }

        public static bool IsPackagedApp(string appsFolderPath)
        {
            // Packaged apps are identified by an AUMID ("PackageFamilyName!AppId") instead of a path
            return !string.IsNullOrEmpty(appsFolderPath) && appsFolderPath.Contains('!') && !appsFolderPath.Contains('\\');
        }

        #region Apps folder

        private static List<StartMenuEntry> LoadAppsFolder()
        {
            var entries = new List<StartMenuEntry>();

            if (Environment.OSVersion.Version.Major < 10)
            {
                // The Apps folder does not exist before Windows 8, and does not list Store apps reliably before 10
                return entries;
            }

            object shell = null;

            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");

                if (shellType == null)
                {
                    return entries;
                }

                shell = Activator.CreateInstance(shellType);
                dynamic folder = ((dynamic)shell).NameSpace(AppsFolder);

                if (folder == null)
                {
                    return entries;
                }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Index the collection rather than enumerating it, which late-bound COM supports on every .NET version
                dynamic items = folder.Items();
                int count = items.Count;

                for (int i = 0; i < count; i++)
                {
                    dynamic item = items.Item(i);

                    if (item == null)
                    {
                        continue;
                    }

                    string name = item.Name as string;
                    string path = item.Path as string;

                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path) || !seen.Add(path))
                    {
                        continue;
                    }

                    entries.Add(new StartMenuEntry
                    {
                        Kind = StartMenuEntryKind.App,
                        Target = path,
                        Name = name,
                        Icon = ShellIcons.FromParsingName(AppsFolder + "\\" + path, ShellIconSize.Small)
                    });
                }
            }
            catch (Exception e)
            {
                ShellLogger.Warning($"AppCatalog: Unable to read the Apps folder: {e.Message}");
            }
            finally
            {
                if (shell != null && Marshal.IsComObject(shell))
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }

            return entries;
        }

        #endregion

        #region Start Menu folders

        private static List<StartMenuEntry> LoadStartMenuTree()
        {
            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
            };

            var level = new List<StartMenuEntry>();
            AddFolderContents(level, roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)).ToList(), 0);
            return level;
        }

        /// <summary>
        /// Adds the merged contents of several folders that represent the same Start Menu folder
        /// (the per-user and all-users copies), the way Explorer merges them.
        /// </summary>
        private static void AddFolderContents(List<StartMenuEntry> level, List<string> folders, int depth)
        {
            if (depth > 8)
            {
                return;
            }

            var subfolders = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var seenItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string folder in folders)
            {
                try
                {
                    foreach (string dir in Directory.EnumerateDirectories(folder))
                    {
                        if ((File.GetAttributes(dir) & FileAttributes.Hidden) != 0)
                        {
                            continue;
                        }

                        string name = Path.GetFileName(dir);

                        if (!subfolders.TryGetValue(name, out List<string> paths))
                        {
                            paths = new List<string>();
                            subfolders.Add(name, paths);
                        }

                        paths.Add(dir);
                    }

                    foreach (string file in Directory.EnumerateFiles(folder))
                    {
                        if ((File.GetAttributes(file) & FileAttributes.Hidden) != 0 ||
                            !ProgramExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string name = Path.GetFileNameWithoutExtension(file);

                        if (!seenItems.Add(name))
                        {
                            continue;
                        }

                        level.Add(new StartMenuEntry
                        {
                            Kind = StartMenuEntryKind.File,
                            Target = file,
                            Name = name,
                            Icon = ShellIcons.FromPath(file, ShellIconSize.Small)
                        });
                    }
                }
                catch (Exception e)
                {
                    ShellLogger.Debug($"AppCatalog: Unable to read {folder}: {e.Message}");
                }
            }

            foreach (var subfolder in subfolders)
            {
                var folderEntry = new StartMenuEntry
                {
                    Kind = StartMenuEntryKind.Folder,
                    Target = subfolder.Value[0],
                    Name = subfolder.Key,
                    Icon = ShellIcons.FromPath(subfolder.Value[0], ShellIconSize.Small)
                };

                var children = new List<StartMenuEntry>();
                AddFolderContents(children, subfolder.Value, depth + 1);

                if (children.Count == 0)
                {
                    continue;
                }

                SortLevel(children);

                foreach (StartMenuEntry child in children)
                {
                    folderEntry.Children.Add(child);
                }

                level.Add(folderEntry);
            }
        }

        /// <summary>
        /// Vista lists programs first and folders after them, each alphabetically.
        /// </summary>
        private static void SortLevel(List<StartMenuEntry> level)
        {
            level.Sort((a, b) =>
            {
                if (a.IsFolder != b.IsFolder)
                {
                    return a.IsFolder ? 1 : -1;
                }

                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
        }

        private static IEnumerable<StartMenuEntry> Flatten(IEnumerable<StartMenuEntry> level)
        {
            foreach (StartMenuEntry entry in level)
            {
                if (entry.IsFolder)
                {
                    foreach (StartMenuEntry child in Flatten(entry.Children))
                    {
                        yield return child;
                    }
                }
                else
                {
                    yield return entry;
                }
            }
        }

        #endregion

        #region Usage counts

        private static List<string> LoadFrequentKeys()
        {
            var counts = new List<(string key, int count, long lastRun)>();

            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(UserAssistKey, false);

                if (key == null)
                {
                    return new List<string>();
                }

                foreach (string valueName in key.GetValueNames())
                {
                    // Windows 7 and later store each entry as a 72-byte record: run count at offset 4, last run time at offset 60
                    if (key.GetValue(valueName) is not byte[] data || data.Length < 16)
                    {
                        continue;
                    }

                    string name = Rot13(valueName);

                    if (name.StartsWith("UEME_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    int count = BitConverter.ToInt32(data, 4);
                    long lastRun = data.Length >= 68 ? BitConverter.ToInt64(data, 60) : 0;

                    if (count > 0)
                    {
                        counts.Add((StartMenuEntry.AppPrefix + name, count, lastRun));
                    }
                }
            }
            catch (Exception e)
            {
                ShellLogger.Debug($"AppCatalog: Unable to read usage counts: {e.Message}");
            }

            return counts
                .OrderByDescending(c => c.count)
                .ThenByDescending(c => c.lastRun)
                .Select(c => c.key)
                .ToList();
        }

        private static string Rot13(string value)
        {
            char[] chars = value.ToCharArray();

            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];

                if (c >= 'a' && c <= 'z')
                {
                    chars[i] = (char)('a' + (c - 'a' + 13) % 26);
                }
                else if (c >= 'A' && c <= 'Z')
                {
                    chars[i] = (char)('A' + (c - 'A' + 13) % 26);
                }
            }

            return new string(chars);
        }

        #endregion
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/StartMenu/AppCatalog.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/StartMenu/FileSearch.cs">
namespace RetroBar.StartMenu
{
    using ManagedShell.Common.Logging;
    using System;
    using System.Collections.Generic;
    using System.Data.OleDb;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Searches files and folders through the local Windows Search index, the same index that
    /// Vista's Start menu and Windows 11's search use for local results. Nothing leaves the PC.
    /// </summary>
    internal static class FileSearch
    {
        private const string ConnectionString = "Provider=Search.CollatorDSO;Extended Properties='Application=Windows';";

        public static async Task<List<StartMenuEntry>> SearchAsync(string text, int maxResults, CancellationToken token)
        {
            string term = Sanitize(text);

            if (term.Length == 0)
            {
                return new List<StartMenuEntry>();
            }

            List<(string path, string name)> hits = await Task.Run(() => Query(term, maxResults, token), token).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();

            // Icons come from the shell, which needs an STA thread
            return await StaTask.Run(() =>
            {
                var results = new List<StartMenuEntry>();

                foreach (var hit in hits)
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    results.Add(new StartMenuEntry
                    {
                        Kind = StartMenuEntryKind.File,
                        Target = hit.path,
                        Name = hit.name,
                        Icon = ShellIcons.FromPath(hit.path, ShellIconSize.Small)
                    });
                }

                return results;
            }).ConfigureAwait(false);
        }

        private static List<(string path, string name)> Query(string term, int maxResults, CancellationToken token)
        {
            var hits = new List<(string path, string name)>();

            try
            {
                using var connection = new OleDbConnection(ConnectionString);
                connection.Open();

                // Programs are listed separately, so shortcuts are excluded here.
                string sql =
                    $"SELECT TOP {maxResults * 2} System.ItemPathDisplay, System.ItemNameDisplay " +
                    "FROM SystemIndex " +
                    $"WHERE SCOPE='file:' AND System.ItemNameDisplay LIKE '%{term}%' " +
                    "ORDER BY System.DateModified DESC";

                using var command = new OleDbCommand(sql, connection);
                using OleDbDataReader reader = command.ExecuteReader();

                while (reader != null && reader.Read() && hits.Count < maxResults)
                {
                    token.ThrowIfCancellationRequested();

                    string path = reader.IsDBNull(0) ? null : reader.GetValue(0) as string;
                    string name = reader.IsDBNull(1) ? null : reader.GetValue(1) as string;

                    if (string.IsNullOrEmpty(path) || string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    hits.Add((path, string.IsNullOrEmpty(name) ? Path.GetFileName(path) : name));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                // The Windows Search service may be disabled; program results still work without it
                ShellLogger.Debug($"FileSearch: Windows Search query failed: {e.Message}");
            }

            return hits;
        }

        /// <summary>
        /// Removes characters with special meaning in Windows Search SQL so typed text is matched literally.
        /// </summary>
        private static string Sanitize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var chars = new List<char>(text.Length);

            foreach (char c in text.Trim())
            {
                if (c == '\'' || c == '%' || c == '_' || c == '[' || c == ']' || c == '"' || char.IsControl(c))
                {
                    continue;
                }

                chars.Add(c);
            }

            return new string(chars.ToArray());
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/StartMenu/FileSearch.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/StartMenu/StartMenuLauncher.cs">
namespace RetroBar.StartMenu
{
    using ManagedShell.Common.Logging;
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Windows.Forms;

    internal enum StartMenuPowerAction
    {
        Sleep,
        Hibernate,
        ShutDown
    }

    /// <summary>
    /// Opens programs, documents and locations, and performs the session and power actions of the Start menu.
    /// </summary>
    internal static class StartMenuLauncher
    {
        public const string DefaultBrowserKey = "default:browser";
        public const string DefaultEmailKey = "default:email";

        public static bool Launch(StartMenuEntry entry, bool asAdministrator = false)
        {
            if (entry == null)
            {
                return false;
            }

            switch (entry.Kind)
            {
                case StartMenuEntryKind.App:
                    // Apps folder entries (Store apps included) are launched through Explorer by their parsing path
                    return Start("explorer.exe", "shell:AppsFolder\\" + entry.Target, false);
                case StartMenuEntryKind.File:
                case StartMenuEntryKind.Folder:
                case StartMenuEntryKind.Location:
                    return Start(entry.Target, null, asAdministrator);
                default:
                    return false;
            }
        }

        public static bool Start(string fileName, string arguments = null, bool asAdministrator = false)
        {
            try
            {
                var info = new ProcessStartInfo(fileName)
                {
                    UseShellExecute = true
                };

                if (!string.IsNullOrEmpty(arguments))
                {
                    info.Arguments = arguments;
                }

                if (asAdministrator)
                {
                    info.Verb = "runas";
                }

                Process.Start(info)?.Dispose();
                return true;
            }
            catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
            {
                // The user cancelled the elevation prompt
                return false;
            }
            catch (Exception e)
            {
                ShellLogger.Warning($"StartMenuLauncher: Unable to start {fileName}: {e.Message}");
                return false;
            }
        }

        public static void OpenFileLocation(StartMenuEntry entry)
        {
            if (entry?.Target == null || entry.Kind == StartMenuEntryKind.App || entry.Kind == StartMenuEntryKind.Location)
            {
                return;
            }

            if (entry.Kind == StartMenuEntryKind.Folder)
            {
                Start(entry.Target);
                return;
            }

            Start("explorer.exe", $"/select,\"{entry.Target}\"");
        }

        public static void ShowRunDialog()
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");

                if (shellType != null)
                {
                    dynamic shell = Activator.CreateInstance(shellType);
                    shell.FileRun();
                }
            }
            catch (Exception e)
            {
                ShellLogger.Warning($"StartMenuLauncher: Unable to show the Run dialog: {e.Message}");
            }
        }

        #region Default programs

        /// <summary>
        /// Builds the Internet or E-mail item Vista pins to the top of the menu, using the user's default program.
        /// With asProgram, builds a plain item named after the program, as Windows 7 shows pinned programs.
        /// </summary>
        public static StartMenuEntry CreateDefaultProgramEntry(string key, string category, bool asProgram = false)
        {
            string protocol = key == DefaultBrowserKey ? "http" : key == DefaultEmailKey ? "mailto" : null;

            if (protocol == null)
            {
                return null;
            }

            string executable = QueryAssociation(protocol, StartMenuNative.ASSOCSTR_EXECUTABLE);

            if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
            {
                return null;
            }

            string appName = QueryAssociation(protocol, StartMenuNative.ASSOCSTR_FRIENDLYAPPNAME);

            if (string.IsNullOrEmpty(appName))
            {
                appName = Path.GetFileNameWithoutExtension(executable);
            }

            // Packaged apps cannot be started from their executable, so open them through their protocol instead
            bool isPackaged = executable.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) >= 0;

            return new StartMenuEntry
            {
                Kind = isPackaged ? StartMenuEntryKind.Location : StartMenuEntryKind.File,
                Target = isPackaged ? protocol + ":" : executable,
                Name = asProgram ? appName : category,
                Subtitle = asProgram ? null : appName,
                IsBold = !asProgram,
                PinKey = key,
                Icon = ShellIcons.FromPath(executable, ShellIconSize.Large)
            };
        }

        private static string QueryAssociation(string protocol, uint assocStr)
        {
            try
            {
                uint length = 1024;
                var builder = new StringBuilder((int)length);

                if (StartMenuNative.AssocQueryString(StartMenuNative.ASSOCF_IS_PROTOCOL, assocStr, protocol, "open", builder, ref length) == 0)
                {
                    return builder.ToString();
                }
            }
            catch (Exception e)
            {
                ShellLogger.Debug($"StartMenuLauncher: Unable to query the {protocol} association: {e.Message}");
            }

            return null;
        }

        #endregion

        #region Session and power

        public static void Lock()
        {
            StartMenuNative.LockWorkStation();
        }

        public static void SwitchUser()
        {
            // Disconnecting the session shows the sign-in screen, where another user can be chosen
            if (!StartMenuNative.WTSDisconnectSession(StartMenuNative.WTS_CURRENT_SERVER_HANDLE, StartMenuNative.WTS_CURRENT_SESSION, false))
            {
                Lock();
            }
        }

        public static void LogOff()
        {
            Start("shutdown.exe", "/l");
        }

        public static void Restart()
        {
            Start("shutdown.exe", "/r /t 0");
        }

        public static void ShutDown()
        {
            Start("shutdown.exe", "/s /t 0");
        }

        public static void Sleep()
        {
            System.Windows.Forms.Application.SetSuspendState(PowerState.Suspend, false, false);
        }

        public static void Hibernate()
        {
            System.Windows.Forms.Application.SetSuspendState(PowerState.Hibernate, false, false);
        }

        public static void PerformPowerAction(StartMenuPowerAction action)
        {
            switch (action)
            {
                case StartMenuPowerAction.Hibernate:
                    Hibernate();
                    break;
                case StartMenuPowerAction.ShutDown:
                    ShutDown();
                    break;
                default:
                    Sleep();
                    break;
            }
        }

        #endregion
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/StartMenu/StartMenuLauncher.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/StartMenu/WindowsKeyHook.cs">
namespace RetroBar.StartMenu
{
    using ManagedShell.Common.Logging;
    using System;
    using System.Diagnostics;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Watches the keyboard for a lone press of a Windows key (or Ctrl+Esc) and raises StartRequested
    /// instead of letting Windows open its own Start menu. Windows key shortcuts such as Win+E keep working.
    /// </summary>
    internal sealed class WindowsKeyHook : IDisposable
    {
        // Tags input injected by this hook so it is not processed again
        private static readonly UIntPtr InjectedMarker = new UIntPtr(0x52545354);

        // A fresh press is detected when no Windows key event has arrived for this long, which covers
        // a release that was never seen (for example after Win+L locks the session)
        private const uint StalePressMilliseconds = 1500;

        private readonly StartMenuNative.LowLevelKeyboardProc _proc;
        private IntPtr _hook = IntPtr.Zero;

        private bool _winDown;
        private bool _winUsedInCombination;
        private uint _lastWinEventTime;

        public event EventHandler StartRequested;

        public bool IsInstalled => _hook != IntPtr.Zero;

        public WindowsKeyHook()
        {
            // Keep a reference to the delegate so it is not collected while the hook is installed
            _proc = HookProc;
        }

        public bool Install()
        {
            if (IsInstalled)
            {
                return true;
            }

            using (Process process = Process.GetCurrentProcess())
            using (ProcessModule module = process.MainModule)
            {
                _hook = StartMenuNative.SetWindowsHookEx(StartMenuNative.WH_KEYBOARD_LL, _proc, StartMenuNative.GetModuleHandle(module.ModuleName), 0);
            }

            if (!IsInstalled)
            {
                ShellLogger.Warning($"WindowsKeyHook: Unable to install keyboard hook: {Marshal.GetLastWin32Error()}");
                return false;
            }

            return true;
        }

        public void Uninstall()
        {
            if (!IsInstalled)
            {
                return;
            }

            StartMenuNative.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _winDown = false;
        }

        public void Dispose()
        {
            Uninstall();
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    if (HandleKey((int)wParam, Marshal.PtrToStructure<StartMenuNative.KBDLLHOOKSTRUCT>(lParam)))
                    {
                        // Swallow the event
                        return (IntPtr)1;
                    }
                }
                catch (Exception e)
                {
                    ShellLogger.Error($"WindowsKeyHook: {e.Message}");
                }
            }

            return StartMenuNative.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        /// <returns>True if the key event should be blocked.</returns>
        private bool HandleKey(int message, StartMenuNative.KBDLLHOOKSTRUCT key)
        {
            if (key.dwExtraInfo == InjectedMarker)
            {
                return false;
            }

            bool isDown = message == StartMenuNative.WM_KEYDOWN || message == StartMenuNative.WM_SYSKEYDOWN;
            bool isUp = message == StartMenuNative.WM_KEYUP || message == StartMenuNative.WM_SYSKEYUP;
            bool isWinKey = key.vkCode == StartMenuNative.VK_LWIN || key.vkCode == StartMenuNative.VK_RWIN;

            if (isWinKey)
            {
                if (isDown)
                {
                    if (!_winDown || key.time - _lastWinEventTime > StalePressMilliseconds)
                    {
                        _winUsedInCombination = false;
                    }

                    _winDown = true;
                    _lastWinEventTime = key.time;
                    return false;
                }

                if (isUp)
                {
                    bool wasLonePress = _winDown && !_winUsedInCombination;
                    _winDown = false;
                    _lastWinEventTime = key.time;

                    if (!wasLonePress)
                    {
                        return false;
                    }

                    // Windows opens its Start menu when a Windows key is released without another key being pressed.
                    // Block this release and replay it after an unassigned key, so Windows sees a key combination.
                    SendMaskedRelease((ushort)key.vkCode);
                    StartRequested?.Invoke(this, EventArgs.Empty);
                    return true;
                }

                return false;
            }

            if (isDown)
            {
                if (_winDown)
                {
                    _winUsedInCombination = true;
                    return false;
                }

                if (key.vkCode == StartMenuNative.VK_ESCAPE && (StartMenuNative.GetAsyncKeyState(StartMenuNative.VK_CONTROL) & 0x8000) != 0)
                {
                    // Ctrl+Esc also opens the Start menu
                    StartRequested?.Invoke(this, EventArgs.Empty);
                    return true;
                }
            }

            return false;
        }

        private static void SendMaskedRelease(ushort winKey)
        {
            StartMenuNative.INPUT[] inputs =
            {
                KeyInput(StartMenuNative.VK_MASK, 0),
                KeyInput(StartMenuNative.VK_MASK, StartMenuNative.KEYEVENTF_KEYUP),
                KeyInput(winKey, StartMenuNative.KEYEVENTF_KEYUP | StartMenuNative.KEYEVENTF_EXTENDEDKEY)
            };

            StartMenuNative.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<StartMenuNative.INPUT>());
        }

        private static StartMenuNative.INPUT KeyInput(ushort virtualKey, uint flags)
        {
            return new StartMenuNative.INPUT
            {
                type = StartMenuNative.INPUT_KEYBOARD,
                U = new StartMenuNative.InputUnion
                {
                    ki = new StartMenuNative.KEYBDINPUT
                    {
                        wVk = virtualKey,
                        dwFlags = flags,
                        dwExtraInfo = InjectedMarker
                    }
                }
            };
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/StartMenu/WindowsKeyHook.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/StartMenu/StartMenuManager.cs">
namespace RetroBar.StartMenu
{
    using ManagedShell.Common.Logging;
    using RetroBar.Utilities;
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Windows;

    internal class StartMenuOpenStateEventArgs : EventArgs
    {
        public Taskbar Host { get; init; }
        public bool IsOpen { get; init; }
    }

    /// <summary>
    /// Owns the Vista Start menu: opens it from the Start button or the Windows key, keeps the
    /// program catalog loaded, and reports when the menu opens and closes so the button can follow.
    /// </summary>
    internal sealed class StartMenuManager : IDisposable
    {
        // A click on the Start button first deactivates (closes) an open menu; ignore the click that follows
        private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(350);

        // The program list is reloaded in the background when the menu opens after this long
        private static readonly TimeSpan CatalogLifetime = TimeSpan.FromMinutes(2);

        public static StartMenuManager Instance { get; private set; }

        public event EventHandler<StartMenuOpenStateEventArgs> OpenStateChanged;

        private WindowManager _windowManager;
        private readonly WindowsKeyHook _keyHook = new WindowsKeyHook();
        private VistaStartMenu _menu;
        private AppCatalogSnapshot _catalog;
        private Task _catalogLoad;
        private bool _disposed;

        public bool IsEnabled => Settings.Instance.UseVistaStartMenu;

        public StartMenuManager()
        {
            Instance = this;

            _keyHook.StartRequested += KeyHook_StartRequested;
            Settings.Instance.PropertyChanged += Settings_PropertyChanged;

            UpdateKeyHook();

            if (IsEnabled)
            {
                _ = LoadCatalogAsync();
            }
        }

        public void SetWindowManager(WindowManager windowManager)
        {
            _windowManager = windowManager;
        }

        public bool IsOpenOn(Taskbar host)
        {
            return _menu != null && _menu.IsOpen && _menu.Host == host;
        }

        /// <summary>
        /// Handles a click on a taskbar's Start button.
        /// </summary>
        public void ToggleFromStartButton(Taskbar host)
        {
            if (!IsEnabled || host == null)
            {
                return;
            }

            if (IsOpenOn(host))
            {
                _menu.CloseMenu();
                return;
            }

            if (_menu != null && _menu.Host == host && DateTime.Now - _menu.LastClosed < ReopenGuard)
            {
                // This click is the one that just closed the menu by taking focus from it
                return;
            }

            Open(host);
        }

        public void Open(Taskbar host)
        {
            if (!IsEnabled || host == null || _disposed)
            {
                return;
            }

            if (_menu == null)
            {
                _menu = new VistaStartMenu();
                _menu.MenuClosed += Menu_MenuClosed;
            }
            else if (_menu.IsOpen)
            {
                if (_menu.Host == host)
                {
                    return;
                }

                _menu.CloseMenu();
            }

            _menu.ShowAt(host, _catalog);
            OpenStateChanged?.Invoke(this, new StartMenuOpenStateEventArgs { Host = host, IsOpen = true });

            if (_catalog == null || DateTime.Now - _catalog.LoadedAt > CatalogLifetime)
            {
                _ = LoadCatalogAsync();
            }
        }

        /// <summary>
        /// Empties the recent programs list, including programs Windows has recorded as frequently used.
        /// A cleared program returns to the list when it is next started from the Start menu.
        /// </summary>
        public void ClearRecentPrograms()
        {
            var hidden = new HashSet<string>(Settings.Instance.StartMenuHiddenPrograms ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            foreach (string key in Settings.Instance.StartMenuRecentPrograms ?? new List<string>())
            {
                hidden.Add(key);
            }

            if (_catalog != null)
            {
                foreach (string key in _catalog.FrequentKeys)
                {
                    hidden.Add(key);
                }
            }

            Settings.Instance.StartMenuHiddenPrograms = hidden.ToList();
            Settings.Instance.StartMenuRecentPrograms = new List<string>();
            _ = _menu?.RefreshHomeListsAsync();
        }

        public void Close()
        {
            _menu?.CloseMenu();
        }

        private void Menu_MenuClosed(object sender, EventArgs e)
        {
            OpenStateChanged?.Invoke(this, new StartMenuOpenStateEventArgs { Host = _menu?.Host, IsOpen = false });
        }

        private void KeyHook_StartRequested(object sender, EventArgs e)
        {
            // Raised inside the keyboard hook; do the work after the hook returns
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_menu != null && _menu.IsOpen)
                {
                    _menu.CloseMenu();
                    return;
                }

                Open(_windowManager?.GetPrimaryTaskbar());
            }));
        }

        private async Task LoadCatalogAsync()
        {
            if (_catalogLoad != null)
            {
                await _catalogLoad;
                return;
            }

            Task<AppCatalogSnapshot> load = AppCatalog.LoadAsync();
            _catalogLoad = load;

            try
            {
                AppCatalogSnapshot catalog = await load;

                if (_disposed)
                {
                    return;
                }

                _catalog = catalog;
                _menu?.ApplyCatalog(catalog);
            }
            catch (Exception e)
            {
                ShellLogger.Error($"StartMenuManager: Unable to load programs: {e.Message}");
            }
            finally
            {
                _catalogLoad = null;
            }
        }

        private void UpdateKeyHook()
        {
            if (IsEnabled && Settings.Instance.StartMenuOpenWithWindowsKey)
            {
                _keyHook.Install();
            }
            else
            {
                _keyHook.Uninstall();
            }
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.UseVistaStartMenu) || e.PropertyName == nameof(Settings.StartMenuOpenWithWindowsKey))
            {
                UpdateKeyHook();

                if (!IsEnabled)
                {
                    Close();
                }
                else if (_catalog == null)
                {
                    _ = LoadCatalogAsync();
                }
            }
        }

        public void Dispose()
        {
            _disposed = true;
            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
            _keyHook.StartRequested -= KeyHook_StartRequested;
            _keyHook.Dispose();

            if (_menu != null)
            {
                _menu.MenuClosed -= Menu_MenuClosed;
                _menu.Close();
                _menu = null;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/StartMenu/StartMenuManager.cs">
