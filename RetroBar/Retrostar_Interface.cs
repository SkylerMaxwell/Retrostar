// Retrostar Interface monolith. Production C# lives in exactly two files: RetroBar/Retrostar_Core.cs and
// RetroBar/Retrostar_Interface.cs. Each is a sequence of logical modules, each delimited by
// RETROSTAR_SOURCE_BEGIN / RETROSTAR_SOURCE_END with the module's logical path, grouped under [nnnn] category
// markers. Rules and navigation index: Rules_For_Agent_Editors.md.

// [5000] APPLICATION :: App lifecycle: startup, theme and language loading, shutdown

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/App.xaml.cs">
namespace RetroBar
{
    using System;
    using ManagedShell;
    using RetroBar.Utilities;
    using System.Windows;
    using ManagedShell.Common.Helpers;
    using ManagedShell.Interop;
    using Application = System.Windows.Application;
    using System.Windows.Interop;
    using System.Windows.Media;
    using ManagedShell.Common.Enums;
    using System.Diagnostics;
    using System.Reflection;
    using ManagedShell.Common.Logging;
    using System.Linq;
    using RetroBar.StartMenu;

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private bool _errorVisible;
        private ManagedShellLogger _logger;
        private WindowManager _windowManager;
        private StartMenuManager _startMenuManager;

        private readonly DictionaryManager _dictionaryManager;
        private readonly ExplorerMonitor _explorerMonitor;
        private readonly ShellManager _shellManager;
        private readonly StartMenuMonitor _startMenuMonitor;
        private readonly Updater _updater;
        private readonly HotkeyManager _hotkeyManager;
        private readonly VistaSoundScheme _vistaSounds = new VistaSoundScheme();

        public App()
        {
            _shellManager = SetupManagedShell();

            _explorerMonitor = new ExplorerMonitor();
            _startMenuMonitor = new StartMenuMonitor(new AppVisibilityHelper(false));
            _dictionaryManager = new DictionaryManager();
            _updater = new Updater();
            _hotkeyManager = new HotkeyManager();

            Settings.Instance.PropertyChanged += Settings_PropertyChanged;
        }

        public void ExitGracefully()
        {
            _shellManager.AppBarManager.SignalGracefulShutdown();
            Current.Shutdown();
        }

        private void App_OnStartup(object sender, StartupEventArgs e)
        {
            if (Settings.Instance.UseSoftwareRendering)
            {
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            }

            _dictionaryManager.SetLanguageFromSettings();
            loadTheme();
            UpdateVistaSounds();
            UpdateVistaIcons();

            // Created before the taskbars so their Start buttons can subscribe to it as they load
            _startMenuManager = new StartMenuManager();

            _windowManager = new WindowManager(_dictionaryManager, _explorerMonitor, _shellManager, _startMenuMonitor, _updater, _hotkeyManager);
            _startMenuMonitor.SetWindowManager(_windowManager);
            _startMenuManager.SetWindowManager(_windowManager);
        }

        private void App_OnExit(object sender, ExitEventArgs e)
        {
            ExitApp();
        }

        private void App_OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            ExitApp();
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.UseSoftwareRendering))
            {
                if (Settings.Instance.UseSoftwareRendering)
                {
                    RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                }
                else
                {
                    RenderOptions.ProcessRenderMode = RenderMode.Default;
                }
            }
            else if (e.PropertyName == nameof(Settings.Theme) || e.PropertyName == nameof(Settings.TaskbarScale))
            {
                setTaskIconSize();
            }
            else if (e.PropertyName == nameof(Settings.UseVistaSounds))
            {
                UpdateVistaSounds();
            }
            else if (e.PropertyName == nameof(Settings.UseVistaIcons))
            {
                UpdateVistaIcons();
            }
        }

        /// <summary>
        /// Vista's icons are in place while the setting is on, and stay when Retrostar closes; turning it off puts
        /// Windows' own back (the uninstaller does the same).
        /// </summary>
        private void UpdateVistaIcons()
        {
            if (Settings.Instance.UseVistaIcons)
            {
                VistaIconScheme.Apply();
            }
            else
            {
                VistaIconScheme.Remove(false);
            }
        }

        /// <summary>The Vista sounds play while Retrostar runs and the setting is on; otherwise Windows' own are in place.</summary>
        private void UpdateVistaSounds()
        {
            if (Settings.Instance.UseVistaSounds)
            {
                _vistaSounds.Apply();
            }
            else
            {
                _vistaSounds.Restore();
            }
        }

        private void loadTheme()
        {
            _dictionaryManager.SetThemeFromSettings();
            setTaskIconSize();
        }

        private void setTaskIconSize()
        {
            bool useLargeIcons = Settings.Instance.TaskbarScale > 1 || (FindResource("UseLargeIcons") as bool? ?? false);

            if (_shellManager.TasksService.TaskIconSize != IconSize.Small != useLargeIcons)
            {
                _shellManager.TasksService.TaskIconSize = useLargeIcons ? IconSize.Large : IconSize.Small;
            }
        }

        private ShellManager SetupManagedShell()
        {
            EnvironmentHelper.IsAppRunningAsShell = NativeMethods.GetShellWindow() == IntPtr.Zero;

            _logger = new ManagedShellLogger();

            ShellConfig config = ShellManager.DefaultShellConfig;
            config.PinnedNotifyIcons = Settings.Instance.NotifyIconBehaviors.Where(setting => setting.Behavior == NotifyIconBehavior.AlwaysShow).Select(setting => setting.Identifier).ToArray();

            return new ShellManager(config);
        }

        public void RestartApp()
        {
            try
            {
                // run the program again
                Process current = new Process();
                current.StartInfo.FileName = ExePath.GetExecutablePath();
                current.Start();

                // close this instance
                ExitGracefully();
            }
            catch
            { }
        }

        private void ExitApp()
        {
            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;

            // First, so that the Windows sounds return even if something below fails. The Vista icons stay: they are
            // removed by turning their setting off or by uninstalling
            _vistaSounds.Dispose();

            _explorerMonitor.Dispose();
            _startMenuManager?.Dispose();
            _windowManager.Dispose();
            _dictionaryManager.Dispose();
            _shellManager.Dispose();
            _startMenuMonitor.Dispose();
            _updater.Dispose();
            _hotkeyManager.Dispose();
            _logger.Dispose();
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            string version = fvi.FileVersion;

            string inner = "";
            if (e.Exception.InnerException != null)
                inner = "\r\n\r\nInner exception:\r\nMessage: " + e.Exception.InnerException.Message + "\r\nTarget Site: " + e.Exception.InnerException.TargetSite + "\r\n\r\n" + e.Exception.InnerException.StackTrace;

            string msg = "Would you like to restart RetroBar?\r\n\r\nPlease submit a bug report with a screenshot of this error. Thanks! \r\nMessage: " + e.Exception.Message + "\r\nTarget Site: " + e.Exception.TargetSite + "\r\nVersion: " + version + "\r\n\r\n" + e.Exception.StackTrace + inner;

            ShellLogger.Error(msg, e.Exception);

            string dMsg;

            if (msg.Length > 1000)
                dMsg = msg.Substring(0, 999) + "...";
            else
                dMsg = msg;

            try
            {
                if (!_errorVisible)
                {
                    _errorVisible = true;

                    // Automatically restart for known render thread failure messages.
                    if (e.Exception.Message.StartsWith("UCEERR_RENDERTHREADFAILURE"))
                    {
                        RestartApp();
                        Environment.FailFast("Automatically restarted RetroBar due to a render thread failure.");
                    }
                    else
                    {
                        if (MessageBox.Show(dMsg, "RetroBar Error", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                        {
                            // it's like getting a morning coffee.
                            RestartApp();
                            Environment.FailFast("User restarted RetroBar due to an exception.");
                        }
                    }

                    _errorVisible = false;
                }
            }
            catch
            {
                // If this fails we're probably up the creek. Abandon ship!
                ExitGracefully();
            }

            e.Handled = true;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/App.xaml.cs">

// [5100] TASKBAR WINDOW :: the taskbar app bar window

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Taskbar.xaml.cs">
namespace RetroBar
{
    using ManagedShell;
    using ManagedShell.AppBar;
    using ManagedShell.Common.Helpers;
    using ManagedShell.Common.Logging;
    using ManagedShell.Interop;
    using ManagedShell.WindowsTasks;
    using ManagedShell.WindowsTray;
    using RetroBar.Utilities;
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using System.Windows.Threading;
    using Application = System.Windows.Application;

    /// <summary>
    /// Interaction logic for Taskbar.xaml
    /// </summary>
    public partial class Taskbar : AppBarWindow
    {
        private DispatcherTimer _maximizedWindowTimer;

        public static readonly DependencyProperty HasMaximizedWindowProperty =
            DependencyProperty.Register("HasMaximizedWindow", typeof(bool), typeof(Taskbar),
                new PropertyMetadata(false));

        public bool HasMaximizedWindow
        {
            get => (bool)GetValue(HasMaximizedWindowProperty);
            private set => SetValue(HasMaximizedWindowProperty, value);
        }

        public static readonly DependencyProperty IsAeroGlassEnabledProperty =
            DependencyProperty.Register("IsAeroGlassEnabled", typeof(bool), typeof(Taskbar),
                new PropertyMetadata(true));

        /// <summary>
        /// Whether the Aero glass effect is turned on. Glass themes bind to this to switch
        /// between the translucent glass look and the opaque (Basic) look.
        /// </summary>
        public bool IsAeroGlassEnabled
        {
            get => (bool)GetValue(IsAeroGlassEnabledProperty);
            private set => SetValue(IsAeroGlassEnabledProperty, value);
        }

        public static readonly DependencyProperty GlassTintOpacityProperty =
            DependencyProperty.Register("GlassTintOpacity", typeof(double), typeof(Taskbar),
                new PropertyMetadata(1.0));

        /// <summary>
        /// Opacity of the theme's glass tint layer: AeroGlassHelper.TintOpacity while the theme shows glass, otherwise 1,
        /// so opaque looks are never faded. Glass themes bind their tint layer's Opacity to this.
        /// </summary>
        public double GlassTintOpacity
        {
            get => (double)GetValue(GlassTintOpacityProperty);
            private set => SetValue(GlassTintOpacityProperty, value);
        }

        public bool IsLocked => Settings.Instance.LockTaskbar;
        public bool IsScaled => DpiScale > 1 || Settings.Instance.TaskbarScale > 1;

        private double _unlockedMargin;
        public double DesiredRowHeight { get; private set; }

        public int Rows
        {
            get => Settings.Instance.RowCount;
            set => Settings.Instance.RowCount = value;
        }

        private bool _startMenuOpen;
        private LowLevelMouseHook _mouseDragHook;
        private Point? _mouseDragStart = null;
        private bool _mouseDragResize = false;
        private readonly DictionaryManager _dictionaryManager;
        private readonly ShellManager _shellManager;
        private readonly StartMenuMonitor _startMenuMonitor;
        private readonly Updater _updater;
        private bool _fullScreenSuppressed;
        private int _openMenus;
        
        public WindowManager windowManager;
        public HotkeyManager hotkeyManager;

        public Taskbar(WindowManager windowManager, DictionaryManager dictionaryManager, ShellManager shellManager, StartMenuMonitor startMenuMonitor, Updater updater, HotkeyManager hotkeyManager, AppBarScreen screen, AppBarEdge edge, AppBarMode mode)
            : base(shellManager.AppBarManager, shellManager.ExplorerHelper, shellManager.FullScreenHelper, screen, edge, mode, 0)
        {
            _dictionaryManager = dictionaryManager;
            _shellManager = shellManager;
            _startMenuMonitor = startMenuMonitor;
            _updater = updater;
            this.windowManager = windowManager;
            this.hotkeyManager = hotkeyManager;

            InitializeComponent();

            IsAeroGlassEnabled = Settings.Instance.EnableAeroGlass;

            _maximizedWindowTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };

            _maximizedWindowTimer.Tick += MaximizedWindowTimer_Tick;

            // Only themes that opt in via AlternatesAppearanceWhenMaximized need the polling.
            if (AlternatesAppearanceWhenMaximized())
            {
                _maximizedWindowTimer.Start();
            }

            DataContext = _shellManager;
            
            StartButton.StartMenuMonitor = startMenuMonitor;
            
            RecalculateSize(false);

            AllowsTransparency = mode == AppBarMode.AutoHide || (Application.Current.FindResource("AllowsTransparency") as bool? ?? false);

            FlowDirection = Application.Current.FindResource("flow_direction") as FlowDirection? ?? FlowDirection.LeftToRight;

            Settings.Instance.PropertyChanged += Settings_PropertyChanged;

            if (Settings.Instance.ShowDesktopButton)
            {
                ShowDesktopButtonTray.Visibility = Visibility.Visible;
            }

            UpdateStartButton();
            UpdateSuperbar();

            AutoHideElement = TaskbarContentControl;

            PropertyChanged += Taskbar_PropertyChanged;

            _startMenuMonitor.StartMenuVisibilityChanged += StartMenuMonitor_StartMenuVisibilityChanged;
            _shellManager.TasksService.WindowActivated += TasksService_WindowActivated;
        }

        private void TasksService_WindowActivated(object sender, ManagedShell.WindowsTasks.WindowEventArgs e)
        {
            // If full-screen is suppressed, and a full-screen window is activated, it's time to un-suppress.

            if (!_fullScreenSuppressed)
            {
                return;
            }

            _fullScreenSuppressed = false;

            if (!HasFullScreenApp())
            {
                return;
            }

            for (int i = 0; i < _fullScreenHelper.FullScreenApps.Count; i++)
            {
                if (_fullScreenHelper.FullScreenApps[i].hWnd == e.Window.Handle)
                {
                    OnFullScreenEnter(_fullScreenHelper.FullScreenApps[i]);
                    return;
                }
            }
        }

        private void StartMenuMonitor_StartMenuVisibilityChanged(object sender, StartMenuMonitor.StartMenuMonitorEventArgs e)
        {
            if (!HasFullScreenApp() || !e.Visible)
            {
                return;
            }

            _fullScreenSuppressed = true;
            OnFullScreenLeave();
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.Theme))
            {
                bool newTransparency = AppBarMode == AppBarMode.AutoHide || (Application.Current.FindResource("AllowsTransparency") as bool? ?? false);

                if (AllowsTransparency != newTransparency && Screen.Primary)
                {
                    // Transparency cannot be changed on an open window.
                    windowManager.ReopenTaskbars();
                    return;
                }

                UpdateBlur();
                PeekDuringAutoHide();
                UpdateSuperbar();
                RecalculateSize();
                UpdateMaximizedWindowTimer();
            }
            else if (e.PropertyName == nameof(Settings.ShowQuickLaunch))
            {
                UpdateQuickLaunch();
            }
            else if (e.PropertyName == nameof(Settings.Edge))
            {
                PeekDuringAutoHide();
                AppBarEdge = Settings.Instance.Edge;
                UpdatePosition();
            }
            else if (e.PropertyName == nameof(Settings.Language))
            {
                FlowDirection newFlowDirection = Application.Current.FindResource("flow_direction") as FlowDirection? ?? FlowDirection.LeftToRight;

                if (FlowDirection != newFlowDirection && Screen.Primary)
                {
                    // It is necessary to reopen the taskbars to refresh menu sizes.
                    windowManager.ReopenTaskbars();
                    return;
                }
            }
            else if (e.PropertyName == nameof(Settings.ShowDesktopButton))
            {
                if (Settings.Instance.ShowDesktopButton)
                {
                    ShowDesktopButtonTray.Visibility = Visibility.Visible;
                }
                else
                {
                    ShowDesktopButtonTray.Visibility = Visibility.Collapsed;
                }
            }
            else if (e.PropertyName == nameof(Settings.TaskbarScale))
            {
                PeekDuringAutoHide();
                RecalculateSize();
                OnPropertyChanged(nameof(IsScaled));
            }
            else if (e.PropertyName == nameof(Settings.AutoHide))
            {
                bool newTransparency = Settings.Instance.AutoHide || (Application.Current.FindResource("AllowsTransparency") as bool? ?? false);

                if (AllowsTransparency == newTransparency)
                {
                    AppBarMode = Settings.Instance.AutoHide ? AppBarMode.AutoHide : AppBarMode.Normal;
                }
                else if (Screen.Primary)
                {
                    // Auto hide requires transparency
                    // Transparency cannot be changed on an open window.
                    windowManager.ReopenTaskbars();
                }
            }
            else if (e.PropertyName == nameof(Settings.LockTaskbar))
            {
                OnPropertyChanged(nameof(IsLocked));
                PeekDuringAutoHide();
                RecalculateSize();
            }
            else if (e.PropertyName == nameof(Settings.RowCount))
            {
                PeekDuringAutoHide();
                RecalculateSize();
                OnPropertyChanged(nameof(Rows));
            }
            else if (e.PropertyName == nameof(Settings.TaskbarWidth))
            {
                PeekDuringAutoHide();
                RecalculateSize();
            }
            else if (e.PropertyName == nameof(Settings.ShowStartButtonMultiMon))
            {
                UpdateStartButton();
            }
            else if (e.PropertyName == nameof(Settings.AutoHideTransparent))
            {
                PeekDuringAutoHide();
            }
            else if (e.PropertyName == nameof(Settings.AllowBlurBehind) || e.PropertyName == nameof(Settings.AeroGlassTransparency) || e.PropertyName == nameof(Settings.AeroGlassBlur))
            {
                UpdateBlur();
            }
            else if (e.PropertyName == nameof(Settings.EnableAeroGlass))
            {
                IsAeroGlassEnabled = Settings.Instance.EnableAeroGlass;
                UpdateBlur();
                UpdateMaximizedWindowTimer();
            }
            else if (e.PropertyName == nameof(Settings.AllowAlternateMaximizedAppearance))
            {
                UpdateMaximizedWindowTimer();
            }
        }

        private void UpdateMaximizedWindowTimer()
        {
            if (AlternatesAppearanceWhenMaximized())
            {
                _maximizedWindowTimer?.Start();
            }
            else
            {
                _maximizedWindowTimer?.Stop();
                HasMaximizedWindow = false;
            }
        }

        private bool AlternatesAppearanceWhenMaximized()
        {
            // While Aero glass is on, glass themes keep their glass when a window is maximized
            if (Settings.Instance.EnableAeroGlass &&
                (Application.Current.FindResource("AllowsTransparency") as bool? ?? false))
            {
                return false;
            }

            return Settings.Instance.AllowAlternateMaximizedAppearance &&
                   Application.Current.TryFindResource("AlternatesAppearanceWhenMaximized") is bool b && b;
        }

        public bool ShowsTask(ApplicationWindow window)
        {
            if (!window.ShowInTaskbar)
            {
                return false;
            }

            if (!Settings.Instance.ShowMultiMon || Settings.Instance.MultiMonMode == MultiMonOption.AllTaskbars)
            {
                return true;
            }

            if (Settings.Instance.MultiMonMode == MultiMonOption.SameAsWindowAndPrimary && Screen.Primary)
            {
                return true;
            }

            IntPtr hMonitor = window.HMonitor;
            if (Screen.Primary && !windowManager.IsValidHMonitor(hMonitor))
            {
                return true;
            }

            return hMonitor == Screen.HMonitor;
        }

        private bool IsAnyWindowMaximizedOnThisScreen()
        {
            foreach (ApplicationWindow window in _shellManager.Tasks.GroupedWindows)
            {
                if (window.ShowStyle == NativeMethods.WindowShowStyle.ShowMaximized && ShowsTask(window))
                {
                    return true;
                }
            }

            return false;
        }

        private void MaximizedWindowTimer_Tick(object sender, EventArgs e)
        {
            HasMaximizedWindow = IsAnyWindowMaximizedOnThisScreen();
        }

        private const int WM_THEMECHANGED = 0x031A;
        private const int WM_DWMCOMPOSITIONCHANGED = 0x031E;
        private const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;

        #region AppBarWindow overrides
        protected override void OnSourceInitialized(object sender, EventArgs e)
        {
            base.OnSourceInitialized(sender, e);

            SetLayoutRounding();
            UpdateBlur();
            UpdateTrayPosition();
        }
            
        protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            base.WndProc(hwnd, msg, wParam, lParam, ref handled);

            if ((msg == (int)NativeMethods.WM.SYSCOLORCHANGE || 
                    msg == (int)NativeMethods.WM.SETTINGCHANGE) && 
                Settings.Instance.Theme.StartsWith(DictionaryManager.THEME_DEFAULT))
            {
                handled = true;

                // If the color scheme changes, re-apply the current theme to get updated colors.
                _dictionaryManager.SetThemeFromSettings();
            }
            else if (msg == (int)NativeMethods.WM.SETTINGCHANGE && wParam == (IntPtr)NativeMethods.SPI.SETWORKAREA && Settings.Instance.ShowMultiMon)
            {
                windowManager.NotifyWorkAreaChange();
            }

            if (msg == WM_DWMCOMPOSITIONCHANGED || msg == WM_DWMCOLORIZATIONCOLORCHANGED || msg == WM_THEMECHANGED ||
                msg == (int)NativeMethods.WM.SETTINGCHANGE)
            {
                // DWM, colour, theme and transparency changes can bring back Windows' own treatments (accent,
                // backdrop, rounded corners); prepare the glass again once the change has settled
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (IsLoaded)
                    {
                        UpdateBlur();
                    }
                }));
            }

            return IntPtr.Zero;
        }

        protected override void CustomClosing()
        {
            if (_maximizedWindowTimer != null)
            {
                _maximizedWindowTimer.Stop();
                _maximizedWindowTimer.Tick -= MaximizedWindowTimer_Tick;
                _maximizedWindowTimer = null;
            }

            if (AllowClose)
            {
                QuickLaunchToolbar.Visibility = Visibility.Collapsed;

                _blurBackdrop?.Dispose();
                _blurBackdrop = null;

                Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
                _startMenuMonitor.StartMenuVisibilityChanged -= StartMenuMonitor_StartMenuVisibilityChanged;
                _shellManager.TasksService.WindowActivated -= TasksService_WindowActivated;
            }
        }

        protected override void SetScreenProperties(ScreenSetupReason reason)
        {
            if (reason == ScreenSetupReason.DpiChange)
            {
                // DPI change is per-monitor, update ourselves
                UpdatePosition();
                SetLayoutRounding();
                UpdateBlur();
                return;
            }

            if (Settings.Instance.ShowMultiMon)
            {
                // Re-create RetroBar windows based on new screen setup
                windowManager.NotifyDisplayChange(reason);
            }
            else
            {
                // Update window as necessary
                base.SetScreenProperties(reason);
            }
        }

        protected override bool ShouldAllowAutoHide()
        {
            return (!_startMenuOpen || !Screen.Primary) && _openMenus < 1 && base.ShouldAllowAutoHide();
        }

        protected override void OnAutoHideAnimationBegin(bool isHiding)
        {
            base.OnAutoHideAnimationBegin(isHiding);

            // Prevent focus indicators and tooltips while hidden
            ResetControlFocus();

            if (!isHiding && Opacity < 1)
            {
                Opacity = 1;
                OnPropertyChanged(nameof(Opacity));
            }
            if (!isHiding)
            {
                StartButton?.UpdateFloatingStartVisible(true);
            }
        }

        protected override void OnAutoHideAnimationComplete(bool isHiding)
        {
            base.OnAutoHideAnimationComplete(isHiding);

            if (isHiding && Settings.Instance.AutoHideTransparent && AllowsTransparency && AllowAutoHide)
            {
                Opacity = 0.01;
                OnPropertyChanged(nameof(Opacity));
            }
            if (isHiding && AllowAutoHide)
            {
                StartButton?.UpdateFloatingStartVisible(false);
            }
        }
        
        protected override void OnFullScreenEnter(FullScreenApp app)
        {
            base.OnFullScreenEnter(app);
            StartButton?.UpdateFloatingStartTopmost(false);
        }

        protected override void OnFullScreenLeave()
        {
            base.OnFullScreenLeave();
            StartButton?.UpdateFloatingStartTopmost(true);
        }
        #endregion

        #region Taskbar events
        private void Taskbar_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DpiScale))
            {
                OnPropertyChanged(nameof(IsScaled));
            }
        }

        private void Taskbar_OnLocationChanged(object sender, EventArgs e)
        {
            UpdateTrayPosition();
            StartButton?.UpdateFloatingStartCoordinates();
        }

        private void Taskbar_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTrayPosition();
            StartButton?.UpdateFloatingStartCoordinates();
        }

        private void Taskbar_Deactivated(object sender, EventArgs e)
        {
            if (AppBarMode != AppBarMode.AutoHide)
            {
                // Prevent focus indicators and tooltips while not the active window
                // When auto-hide is enabled, this is performed by auto-hide events instead
                ResetControlFocus();
            }
        }
        #endregion

        #region Context menu
        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (_updater.IsUpdateAvailable)
            {
                UpdateAvailableMenuItem.Visibility = Visibility.Visible;
            }

            if (NativeMethods.GetAsyncKeyState((int)System.Windows.Forms.Keys.ShiftKey) < 0 && Settings.Instance.ShowExitMenuItem)
            {
                RestartMenuItem.Visibility = Visibility.Visible;
            }
            else
            {
                RestartMenuItem.Visibility = Visibility.Collapsed;
            }
        }

        private void SetTimeMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            ShellHelper.StartProcess("timedate.cpl");
        }

        private void CustomizeNotificationsMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            OpenCustomizeNotifications();
        }

        /// <summary>Opens Properties at the notification icon settings (the menu item, and the Windows 7 overflow's Customize link).</summary>
        internal void OpenCustomizeNotifications()
        {
            PropertiesWindow propWindow = PropertiesWindow.Open(_shellManager.NotificationArea, _dictionaryManager, Screen, DpiScale, Orientation == Orientation.Horizontal ? DesiredHeight : DesiredWidth);
            propWindow.OpenCustomizeNotifications();
        }

        private void TaskManagerMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            ShellHelper.StartTaskManager();
        }

        private void UpdateAvailableMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = _updater.DownloadUrl,
                UseShellExecute = true
            };

            Process.Start(psi);
        }

        private void PropertiesMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            PropertiesWindow.Open(_shellManager.NotificationArea, _dictionaryManager, Screen, DpiScale, Orientation == Orientation.Horizontal ? DesiredHeight : DesiredWidth);
        }

        private void ExitMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            ((App)Application.Current).ExitGracefully();
        }

        private void RestartMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ((App)Application.Current).RestartApp();
        }
        #endregion

        private void RecalculateSize(bool performResize = true)
        {
            _unlockedMargin = Settings.Instance.TaskbarScale * (Application.Current.FindResource("TaskbarUnlockedSize") as double? ?? 0);
            DesiredRowHeight = Settings.Instance.TaskbarScale * (Application.Current.FindResource("TaskbarRowHeight") as double? ?? 0);
            double newWidth = (Settings.Instance.TaskbarScale * (Application.Current.FindResource("TaskbarWidth") as double? ?? 0)) + DesiredRowHeight * (Settings.Instance.TaskbarWidth - 1);
            double newHeight = (Settings.Instance.TaskbarScale * (Application.Current.FindResource("TaskbarHeight") as double? ?? 0)) + DesiredRowHeight * (Rows - 1);

            if (AppBarMode == AppBarMode.AutoHide || !Settings.Instance.LockTaskbar)
            {
                newHeight += _unlockedMargin;
                newWidth += _unlockedMargin;
            }

            bool heightChanged = newHeight != DesiredHeight;
            bool widthChanged = newWidth != DesiredWidth;

            DesiredHeight = newHeight;
            DesiredWidth = newWidth;

            if (!performResize)
            {
                return;
            }

            if ((Orientation == Orientation.Horizontal && heightChanged) || (Orientation == Orientation.Vertical && widthChanged))
            {
                UpdatePosition();
            }
        }

        private void ResetControlFocus()
        {
            FocusDummyButton.MoveFocus(new TraversalRequest(FocusNavigationDirection.Left));
        }

        private void SetLayoutRounding()
        {
            // Layout rounding causes incorrect sizing on non-integer scales
            if (DpiScale % 1 != 0)
            {
                UseLayoutRounding = false;
            }
            else
            {
                UseLayoutRounding = true;
            }
        }

        public void SetStartMenuOpen(bool isOpen)
        {
            bool currentAutoHide = AllowAutoHide;
            _startMenuOpen = isOpen;

            if (AllowAutoHide != currentAutoHide)
            {
                OnPropertyChanged(nameof(AllowAutoHide));
            }
        }

        public void SetTrayHost()
        {
            _shellManager.NotificationArea.SetTrayHostSizeData(new TrayHostSizeData
            {
                edge = (NativeMethods.ABEdge)AppBarEdge,
                rc = WindowRect
            });
        }

        public void AddOpenMenu()
        {
            bool currentAutoHide = AllowAutoHide;
            _openMenus++;

            if (AllowAutoHide != currentAutoHide)
            {
                OnPropertyChanged(nameof(AllowAutoHide));
            }
        }

        public void RemoveOpenMenu()
        {
            bool currentAutoHide = AllowAutoHide;
            _openMenus--;

            if (AllowAutoHide != currentAutoHide)
            {
                OnPropertyChanged(nameof(AllowAutoHide));
            }
        }

        private void UpdateTrayPosition()
        {
            if (Screen.Primary)
            {
                SetTrayHost();
            }
        }

        /// <summary>
        /// Shows the Windows 7 superbar in place of the task list when the theme asks for it (UseSuperbar).
        /// Only the one shown creates task buttons.
        /// </summary>
        private void UpdateSuperbar()
        {
            bool useSuperbar = UsesSuperbar();

            TaskListControl.SetActive(!useSuperbar);
            TaskListControl.Visibility = useSuperbar ? Visibility.Collapsed : Visibility.Visible;
            SuperbarControl.Visibility = useSuperbar ? Visibility.Visible : Visibility.Collapsed;
            SuperbarControl.SetActive(useSuperbar);

            UpdateQuickLaunch();
        }

        private static bool UsesSuperbar()
        {
            return Application.Current.TryFindResource("UseSuperbar") as bool? ?? false;
        }

        /// <summary>
        /// Shows Quick Launch when it is turned on, except under a superbar theme (Windows 7): Windows 7 has no Quick Launch,
        /// pinned programs are full-size superbar buttons and a program opens on its pinned button. The setting is kept
        /// and applies again under every other theme.
        /// </summary>
        private void UpdateQuickLaunch()
        {
            QuickLaunchToolbar.Visibility = Settings.Instance.ShowQuickLaunch && !UsesSuperbar()
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void UpdateStartButton()
        {
            if (!Screen.Primary && !Settings.Instance.ShowStartButtonMultiMon)
            {
                StartButton.Visibility = Visibility.Collapsed;
                return;
            }

            StartButton.Visibility = Visibility.Visible;
        }

        private bool HasFullScreenApp()
        {
            bool hasFullScreenApp = false;

            foreach (var app in _fullScreenHelper.FullScreenApps)
            {
                if (app.screen.DeviceName == Screen.DeviceName || app.screen.IsVirtualScreen)
                {
                    hasFullScreenApp = true;
                    break;
                }
            }

            return hasFullScreenApp;
        }

        /// <summary>Whether the bar shows Aero glass: Aero glass is on and the theme is a glass theme.</summary>
        private bool ShowsGlass()
        {
            return Settings.Instance.EnableAeroGlass &&
                   (Application.Current.FindResource("AllowsTransparency") as bool? ?? false);
        }

        /// <summary>
        /// Shows the glass to match the theme and the glass settings. The glass is Windows' live transparency of this
        /// layered window, with the theme's tint drawn at AeroGlassHelper.TintOpacity; Windows' own blur, tint and
        /// Windows 11 materials are kept off (AeroGlassHelper.PrepareGlassWindow).
        /// </summary>
        private void UpdateBlur()
        {
            // ManagedShell's own blur is the accent policy (Windows 10) or the system backdrop (Windows 11), both
            // Windows' own effects. It always stays off.
            SetBlur(false);

            bool glass = ShowsGlass();
            GlassTintOpacity = glass ? AeroGlassHelper.TintOpacity : 1.0;

            if (glass && AllowsTransparency)
            {
                AeroGlassHelper.PrepareGlassWindow(Handle);
            }

            // The adjustable blur beneath the glass (GlassBlurBackdrop); created the first time it is needed
            double blur = glass && AllowsTransparency ? AeroGlassHelper.BlurStrength : 0;

            if (blur > 0 && _blurBackdrop == null && Handle != IntPtr.Zero)
            {
                _blurBackdrop = new GlassBlurBackdrop(Handle);
            }

            _blurBackdrop?.SetStrength(blur);
        }

        private GlassBlurBackdrop _blurBackdrop;

        #region Unlocked taskbar drag hook
        private void Taskbar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsLocked)
            {
                // Start low-level mouse hook to receive current drag position
                // The hook should be stopped upon mouse up
                StartMouseDragHook();
            }
        }

        private AppBarEdge DragCoordsToScreenEdge(int x, int y)
        {
            // The areas of the screen which determine the dragged-to edge are divided in an X.
            // To determine the edge, split the screen into quadrants, and then split the quadrants diagonally, alternating.
            double relativeX = ((double)x - Screen.Bounds.Left) / Screen.Bounds.Width;
            double relativeY = ((double)y - Screen.Bounds.Top) / Screen.Bounds.Height;

            // We will use the relative coordinates to form quadrants
            // Determine the edge based on the quadrant

            if (relativeX < 0.5 && relativeY < 0.5)
            {
                // top-left quadrant
                if (relativeX >= relativeY)
                {
                    return AppBarEdge.Top;
                }
                else
                {
                    return AppBarEdge.Left;
                }
            }
            else if (relativeX >= 0.5 && relativeY < 0.5)
            {
                // top-right quadrant
                // adjust relativeX to the same base as relativeY
                relativeX -= 0.5;

                if (relativeX + relativeY < 0.5)
                {
                    return AppBarEdge.Top;
                }
                else
                {
                    return AppBarEdge.Right;
                }
            }
            else if (relativeX < 0.5 && relativeY >= 0.5)
            {
                // bottom-left quadrant
                // adjust relativeY to the same base as relativeX
                relativeY -= 0.5;

                if (relativeX + relativeY < 0.5)
                {
                    return AppBarEdge.Left;
                }
                else
                {
                    return AppBarEdge.Bottom;
                }
            }
            else
            {
                // bottom-right quadrant
                if (relativeX >= relativeY)
                {
                    return AppBarEdge.Right;
                }
                else
                {
                    return AppBarEdge.Bottom;
                }
            }
        }

        private void MouseDragHook_LowLevelMouseEvent(object sender, LowLevelMouseHook.LowLevelMouseEventArgs e)
        {
            switch (e.Message)
            {
                case NativeMethods.WM.MOUSEMOVE:
                    if (_mouseDragStart == null)
                    {
                        return;
                    }

                    if (_mouseDragResize)
                    {
                        Dispatcher.BeginInvoke(() => {
                            int mouseX = e.HookStruct.pt.X;
                            int mouseY = e.HookStruct.pt.Y;
                            // Calculate where the resize edge should be, in case the actual resize operation is lagging behind the mouse
                            double scaledRowHeight = DesiredRowHeight * DpiScale;
                            if (Orientation == Orientation.Horizontal)
                            {
                                double taskbarEdge = AppBarEdge == AppBarEdge.Top ? Screen.Bounds.Top + (DesiredHeight * DpiScale) : Screen.Bounds.Bottom - (DesiredHeight * DpiScale);
                                if ((AppBarEdge == AppBarEdge.Top && mouseY < taskbarEdge - SystemParameters.MinimumVerticalDragDistance ||
                                     AppBarEdge == AppBarEdge.Bottom && mouseY > taskbarEdge + SystemParameters.MinimumVerticalDragDistance) &&
                                     Settings.Instance.RowCount > 1)
                                {
                                    // If mouse is inside the taskbar and more than the minimum drag distance away, decrement size
                                    Settings.Instance.RowCount -= 1;
                                }
                                else if ((AppBarEdge == AppBarEdge.Top && mouseY >= taskbarEdge + scaledRowHeight ||
                                          AppBarEdge == AppBarEdge.Bottom && mouseY <= taskbarEdge - scaledRowHeight) &&
                                          Settings.Instance.RowCount < Settings.Instance.RowLimit)
                                {
                                    // If mouse is outside the taskbar and at least one row height away, increment size
                                    Settings.Instance.RowCount += 1;
                                }
                            }
                            else
                            {
                                double taskbarEdge = AppBarEdge == AppBarEdge.Left ? Screen.Bounds.Left + (DesiredWidth * DpiScale) : Screen.Bounds.Right - (DesiredWidth * DpiScale);
                                if ((AppBarEdge == AppBarEdge.Left && mouseX > taskbarEdge + scaledRowHeight ||
                                     AppBarEdge == AppBarEdge.Right && mouseX < taskbarEdge - scaledRowHeight) &&
                                    Settings.Instance.TaskbarWidth < Settings.Instance.TaskbarWidthLimit)
                                {
                                    Settings.Instance.TaskbarWidth += 1;
                                }
                                else if ((AppBarEdge == AppBarEdge.Left && mouseX < taskbarEdge - SystemParameters.MinimumHorizontalDragDistance ||
                                          AppBarEdge == AppBarEdge.Right && mouseX > taskbarEdge + SystemParameters.MinimumHorizontalDragDistance) &&
                                    Settings.Instance.TaskbarWidth > 1)
                                {
                                    Settings.Instance.TaskbarWidth -= 1;
                                }
                            }
                        });
                        return;
                    }

                    if (Math.Abs(e.HookStruct.pt.X - (double)(_mouseDragStart?.X)) <= SystemParameters.MinimumHorizontalDragDistance ||
                        Math.Abs(e.HookStruct.pt.Y - (double)(_mouseDragStart?.Y)) <= SystemParameters.MinimumVerticalDragDistance)
                    {
                        return;
                    }

                    AppBarEdge newEdge = DragCoordsToScreenEdge(e.HookStruct.pt.X, e.HookStruct.pt.Y);
                    if (newEdge != AppBarEdge)
                    {
                        Settings.Instance.Edge = newEdge;
                    }
                    break;
                case NativeMethods.WM.LBUTTONUP:
                case NativeMethods.WM.LBUTTONDOWN:
                case NativeMethods.WM.MBUTTONUP:
                case NativeMethods.WM.MBUTTONDOWN:
                case NativeMethods.WM.RBUTTONUP:
                case NativeMethods.WM.RBUTTONDOWN:
                case NativeMethods.WM.XBUTTONUP:
                case NativeMethods.WM.XBUTTONDOWN:
                    StopMouseDragHook();
                    break;
            }
        }

        private void StartMouseDragHook()
        {
            if (_mouseDragHook != null)
            {
                return;
            }

            _mouseDragHook = new LowLevelMouseHook();
            _mouseDragHook.LowLevelMouseEvent += MouseDragHook_LowLevelMouseEvent;
            _mouseDragHook.Initialize();
            _mouseDragStart = new Point(System.Windows.Forms.Cursor.Position.X, System.Windows.Forms.Cursor.Position.Y);
            _mouseDragResize = IsMouseInResizeArea();

            ShellLogger.Debug($"Mouse drag hook started");
        }

        private void StopMouseDragHook()
        {
            _mouseDragHook.LowLevelMouseEvent -= MouseDragHook_LowLevelMouseEvent;
            _mouseDragHook.Dispose();
            _mouseDragHook = null;
            _mouseDragStart = null;
            _mouseDragResize = false;

            ShellLogger.Debug("Mouse drag hook removed");
        }

        private bool IsMouseInResizeArea()
        {
            if (IsLocked) return false;

            int resizeRegionSize = (int)((_unlockedMargin > 0 ? _unlockedMargin : SystemParameters.MinimumVerticalDragDistance * Settings.Instance.TaskbarScale) * DpiScale);
            int mouseX = System.Windows.Forms.Cursor.Position.X;
            int mouseY = System.Windows.Forms.Cursor.Position.Y;

            if (AppBarEdge == AppBarEdge.Bottom && mouseY <= (int)(Top * DpiScale) + resizeRegionSize)
            {
                return true;
            }
            else if (AppBarEdge == AppBarEdge.Top && mouseY >= (int)((Top + Height) * DpiScale) - resizeRegionSize)
            {
                return true;
            }
            else if (AppBarEdge == AppBarEdge.Left && mouseX >= (int)((Left + Width) * DpiScale) - resizeRegionSize)
            {
                return true;
            }
            else if (AppBarEdge == AppBarEdge.Right && mouseX <= (int)(Left * DpiScale) + resizeRegionSize)
            {
                return true;
            }

            return false;
        }

        private void Taskbar_MouseMove(object sender, MouseEventArgs e)
        {
            // Show resize cursor for resizable taskbars
            if (IsMouseInResizeArea() || _mouseDragResize)
            {
                Cursor = Orientation == Orientation.Horizontal ? Cursors.SizeNS : Cursors.SizeWE;
            }
            else
            {
                Cursor = Cursors.Arrow;
            }
        }
        #endregion
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Taskbar.xaml.cs">

// [5200] TASKBAR CONTROLS :: Start button, task list, toolbars, Show Desktop

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Controls/StartButton.xaml.cs">
namespace RetroBar.Controls
{
    #nullable enable
    using System;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using System.Windows.Media;
    using System.Windows.Threading;
    using ManagedShell.Common.Helpers;
    using ManagedShell.Interop;
    using RetroBar.StartMenu;
    using RetroBar.Utilities;

    /// <summary>
    /// Interaction logic for StartButton.xaml
    /// </summary>
    public partial class StartButton : UserControl
    {
        private FloatingStartButton? floatingStartButton;
        private bool allowOpenStart;
        private bool visibilityChanged;
        private DelayedActivationHandler? dragHandler;
        private readonly DispatcherTimer pendingOpenTimer;

        public static DependencyProperty HostProperty = DependencyProperty.Register(nameof(Host), typeof(Taskbar), typeof(StartButton));
        public static DependencyProperty StartMenuMonitorProperty = DependencyProperty.Register(nameof(StartMenuMonitor), typeof(StartMenuMonitor), typeof(StartButton));

        public Taskbar Host
        {
            get { return (Taskbar)GetValue(HostProperty); }
            set { SetValue(HostProperty, value); }
        }

        public StartMenuMonitor StartMenuMonitor
        {
            get { return (StartMenuMonitor)GetValue(StartMenuMonitorProperty); }
            set { SetValue(StartMenuMonitorProperty, value); }
        }

        public StartButton()
        {
            InitializeComponent();

            pendingOpenTimer = new DispatcherTimer(DispatcherPriority.Background);
            pendingOpenTimer.Interval = new TimeSpan(0, 0, 0, 1);
            pendingOpenTimer.Tick += (sender, args) =>
            {
                // if the start menu didn't open, flip the button back to unchecked
                SetStartMenuState(false);
            };
        }

        private void Settings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.Theme))
            {
                bool useFloatingStartButton = Application.Current.FindResource("UseFloatingStartButton") as bool? ?? false;

                if (!useFloatingStartButton && floatingStartButton != null)
                {
                    closeFloatingStart();
                }
            }
        }

        /// <summary>
        /// Window handle of the floating Start orb, or zero when the theme does not use one.
        /// </summary>
        internal IntPtr FloatingStartHandle => floatingStartButton?.Handle ?? IntPtr.Zero;

        public void SetStartMenuState(bool opened)
        {
            Dispatcher.Invoke(() =>
            {
                Start.IsChecked = opened;
                Host?.SetStartMenuOpen(opened);
            });
            pendingOpenTimer.Stop();
        }

        private void Start_OnClick(object sender, RoutedEventArgs e)
        {
            StartMenuManager? vistaStartMenu = StartMenuManager.Instance;

            if (vistaStartMenu != null && vistaStartMenu.IsEnabled && Host != null)
            {
                vistaStartMenu.ToggleFromStartButton(Host);
                Start.IsChecked = vistaStartMenu.IsOpenOn(Host);
                return;
            }

            if (allowOpenStart)
            {
                OpenStartMenu();
                return;
            }

            SetStartMenuState(false);
        }

        private void OpenStartMenu()
        {
            StartMenuManager? vistaStartMenu = StartMenuManager.Instance;

            if (vistaStartMenu != null && vistaStartMenu.IsEnabled && Host != null)
            {
                vistaStartMenu.Open(Host);
                return;
            }

            Host?.SetTrayHost();
            Host?.SetStartMenuOpen(true);
            pendingOpenTimer.Start();
            if (Host != null && StartMenuMonitor != null && Settings.Instance.ShowMultiMon && Settings.Instance.ShowStartButtonMultiMon)
            {
                StartMenuMonitor.ShowStartMenu(Host.Handle);
            }
            else
            {
                ShellHelper.ShowStartMenu();
            }
        }

        private void Start_DragEnter(object sender, DragEventArgs e)
        {
            dragHandler?.OnDragEnter(e);
        }

        private void Start_DragLeave(object sender, DragEventArgs e)
        {
            dragHandler?.OnDragLeave();
        }

        private void Start_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            allowOpenStart = Start.IsChecked == false;
        }

        private void Start_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (EnvironmentHelper.IsWindows10OrBetter)
            {
                ShellHelper.ShowStartContextMenu();
                e.Handled = true;
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            StartMenuMonitor.StartMenuVisibilityChanged += AppVisibilityHelper_StartMenuVisibilityChanged;

            if (StartMenuManager.Instance != null)
            {
                StartMenuManager.Instance.OpenStateChanged += VistaStartMenu_OpenStateChanged;
            }

            Settings.Instance.PropertyChanged += Settings_PropertyChanged;

            dragHandler = new DelayedActivationHandler(() =>
            {
                if (Start.IsChecked == false)
                {
                    OpenStartMenu();
                }
            });

            openFloatingStart();

            IsVisibleChanged += StartButton_IsVisibleChanged;
            LayoutUpdated += StartButton_LayoutUpdated;

            if (Host != null)
            {
                Host.PropertyChanged += Taskbar_PropertyChanged;
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            StartMenuMonitor.StartMenuVisibilityChanged -= AppVisibilityHelper_StartMenuVisibilityChanged;

            if (StartMenuManager.Instance != null)
            {
                StartMenuManager.Instance.OpenStateChanged -= VistaStartMenu_OpenStateChanged;
            }

            if (Host != null)
            {
                Host.PropertyChanged -= Taskbar_PropertyChanged;
            }

            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
            dragHandler?.Dispose();

            hideFloatingStart();
        }

        private void AppVisibilityHelper_StartMenuVisibilityChanged(object? sender, StartMenuMonitor.StartMenuMonitorEventArgs e)
        {
            if (e.Visible && Host != null &&
                ((e.TaskbarHwndActivated != IntPtr.Zero && e.TaskbarHwndActivated != Host.Handle) ||
                (e.StartHmonitor != IntPtr.Zero && e.StartHmonitor != Host.Screen.HMonitor)))
            {
                // Only set as visible when activated from our taskbar
                return;
            }
            SetStartMenuState(e.Visible);
        }

        private void VistaStartMenu_OpenStateChanged(object? sender, StartMenuOpenStateEventArgs e)
        {
            if (e.Host == Host)
            {
                SetStartMenuState(e.IsOpen);
            }
        }

        private void StartButton_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            visibilityChanged = true;
        }

        private void StartButton_LayoutUpdated(object? sender, EventArgs e)
        {
            if (!visibilityChanged)
            {
                UpdateFloatingStartCoordinates();
                return;
            }

            visibilityChanged = false;

            if (IsVisible)
            {
                openFloatingStart();
            }
            else
            {
                hideFloatingStart();
            }
        }

        private void Taskbar_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (Host != null && e.PropertyName == nameof(Opacity))
            {
                if (Host.Opacity == 1)
                {
                    openFloatingStart();
                }
                else
                {
                    hideFloatingStart();
                }
            }
        }

        #region Floating start button

        private void openFloatingStart()
        {
            bool useFloatingStartButton = Application.Current.FindResource("UseFloatingStartButton") as bool? ?? false;

            if (!useFloatingStartButton || Visibility != Visibility.Visible) return;

            if (floatingStartButton == null)
            {
                floatingStartButton = new FloatingStartButton(this, getButtonRect());
                floatingStartButton.Show();
            }
            else
            {
                showFloatingStart();
            }
        }

        private void showFloatingStart()
        {
            if (floatingStartButton == null) return;

            UpdateFloatingStartCoordinates();
            floatingStartButton.Visibility = Visibility.Visible;
        }

        private void hideFloatingStart()
        {
            if (floatingStartButton == null) return;

            floatingStartButton.Visibility = Visibility.Hidden;
        }

        private void closeFloatingStart()
        {
            floatingStartButton?.Close();
            floatingStartButton = null;
        }

        private NativeMethods.Rect getButtonRect()
        {
            // Get the pixel values of the start button's bounds
            Point buttonPosPixels = Start.PointToScreen(new Point(FlowDirection == FlowDirection.LeftToRight ? 0 : Start.ActualWidth, 0));
            Point buttonSizePixels = Start.PointToScreen(new Point(FlowDirection == FlowDirection.LeftToRight ? Start.ActualWidth : 0, Start.ActualHeight));

            // If the start button is currently translated, we get the translated position
            // and need to offset by that much to be positioned correctly.
            if (Host?.AutoHideElement?.RenderTransform is TranslateTransform tt)
            {
                buttonPosPixels.X -= (tt.X * Host.DpiScale);
                buttonPosPixels.Y -= (tt.Y * Host.DpiScale);
                buttonSizePixels.X -= (tt.X * Host.DpiScale);
                buttonSizePixels.Y -= (tt.Y * Host.DpiScale);
            }

            return new NativeMethods.Rect((int)buttonPosPixels.X, (int)buttonPosPixels.Y, (int)buttonSizePixels.X, (int)buttonSizePixels.Y);
        }

        public void UpdateFloatingStartCoordinates()
        {
            // Can't get our coordinates if we aren't visible.
            if (!IsVisible || floatingStartButton == null) return;

            floatingStartButton.SetPosition(getButtonRect());
        }

        public void UpdateFloatingStartTopmost(bool topmost)
        {
            if (floatingStartButton == null) return;

            floatingStartButton.Topmost = topmost;

            if (!topmost)
            {
                // Setting Topmost=false itself does not guarantee that we will go below a full-screen window.
                NativeMethods.SetWindowPos(
                floatingStartButton.Handle,
                Host.Handle,
                0, 0, 0, 0,
                (int)NativeMethods.SetWindowPosFlags.SWP_NOSIZE | (int)NativeMethods.SetWindowPosFlags.SWP_NOMOVE | (int)NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE);
            }
            else
            {
                // Ensure the floating start button is truly topmost when restoring.
                NativeMethods.SetWindowPos(
                floatingStartButton.Handle,
                (IntPtr)NativeMethods.WindowZOrder.HWND_TOPMOST,
                0, 0, 0, 0,
                (int)NativeMethods.SetWindowPosFlags.SWP_NOSIZE | (int)NativeMethods.SetWindowPosFlags.SWP_NOMOVE | (int)NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE);
            }
        }

        public void UpdateFloatingStartVisible(bool visible)
        {
            if (floatingStartButton == null || visible)
            {
                Opacity = 1;
                return;
            }

            Opacity = 0;
        }

        #endregion
    }
}
#nullable restore
// <RETROSTAR_SOURCE_END path="RetroBar/Controls/StartButton.xaml.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Controls/FloatingStartButton.xaml.cs">
namespace RetroBar.Controls
{
    using ManagedShell.AppBar;
    using ManagedShell.Common.Helpers;
    using ManagedShell.Interop;
    using System;
    using System.Windows;
    using System.Windows.Interop;
    using System.Windows.Media;

    /// <summary>
    /// Interaction logic for FloatingStartButton.xaml
    /// </summary>
    public partial class FloatingStartButton : Window
    {
        private WindowInteropHelper helper;
        private NativeMethods.Rect startupRect;

        public IntPtr Handle;

        public FloatingStartButton(StartButton mainButton, NativeMethods.Rect rect)
        {
            Owner = mainButton.Host;
            DataContext = mainButton;

            InitializeComponent();
            startupRect = rect;

            // Render the existing start button control as the ViewRect fill
            VisualBrush visualBrush = new VisualBrush(mainButton.Start);
            ViewRect.Fill = visualBrush;
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            // set up helper and get handle
            helper = new WindowInteropHelper(this);
            Handle = helper.Handle;

            // set up window procedure
            HwndSource source = HwndSource.FromHwnd(Handle);
            source.AddHook(WndProc);

            // Makes click-through by adding transparent style, hide from taskbar
            NativeMethods.SetWindowLong(Handle, NativeMethods.WindowLongFlags.GWL_EXSTYLE, (NativeMethods.GetWindowLong(Handle, NativeMethods.WindowLongFlags.GWL_EXSTYLE) & ~(int)NativeMethods.ExtendedWindowStyles.WS_EX_APPWINDOW) | (int)NativeMethods.ExtendedWindowStyles.WS_EX_TOOLWINDOW | (int)NativeMethods.ExtendedWindowStyles.WS_EX_TRANSPARENT);

            WindowHelper.ExcludeWindowFromPeek(Handle);

            SetPosition(startupRect);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Make transparent to hit tests
            if (msg == (int)NativeMethods.WM.NCHITTEST)
            {
                handled = true;
                return (IntPtr)(-1);
            }

            if (msg == (int)NativeMethods.WM.WINDOWPOSCHANGING)
            {
                // Extract the WINDOWPOS structure corresponding to this message
                NativeMethods.WINDOWPOS wndPos = NativeMethods.WINDOWPOS.FromMessage(lParam);

                // WORKAROUND WPF bug: https://github.com/dotnet/wpf/issues/7561
                // If there is no NOMOVE or NOSIZE or NOACTIVATE flag, and there is a NOZORDER flag, add the NOACTIVATE flag
                if ((wndPos.flags & NativeMethods.SetWindowPosFlags.SWP_NOMOVE) == 0 &&
                    (wndPos.flags & NativeMethods.SetWindowPosFlags.SWP_NOSIZE) == 0 &&
                    (wndPos.flags & NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE) == 0 &&
                    (wndPos.flags & NativeMethods.SetWindowPosFlags.SWP_NOZORDER) != 0)
                {
                    wndPos.flags |= NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE;
                    wndPos.UpdateMessage(lParam);
                }
            }

            handled = false;
            return IntPtr.Zero;
        }

        internal void SetPosition(NativeMethods.Rect rect)
        {
            NativeMethods.Rect currentRect;
            NativeMethods.GetWindowRect(Handle, out currentRect);

            if (rect.Left == currentRect.Left && rect.Top == currentRect.Top && rect.Right == currentRect.Right && rect.Bottom == currentRect.Bottom)
            {
                return;
            }

            int swp = (int)NativeMethods.SetWindowPosFlags.SWP_NOZORDER | (int)NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE;
            NativeMethods.SetWindowPos(Handle, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height, swp);

            if (Owner is Taskbar host && host.Screen != null)
            {
                // Clip the window to the monitor bounds to prevent bleeding into adjacent screens
                var bounds = host.Screen.Bounds;
                int clipLeft = Math.Max(0, bounds.Left - rect.Left);
                int clipTop = Math.Max(0, bounds.Top - rect.Top);
                int clipRight = Math.Min(rect.Width, bounds.Right - rect.Left);
                int clipBottom = Math.Min(rect.Height, bounds.Bottom - rect.Top);

                // Clip the window so as to not cover the in-taskbar button
                switch (host.AppBarEdge)
                {
                    case AppBarEdge.Bottom:
                        clipBottom = Math.Min(clipBottom, host.WindowRect.Top - rect.Top);
                        break;
                    case AppBarEdge.Top:
                        clipTop = Math.Max(clipTop, host.WindowRect.Bottom - rect.Top);
                        break;
                    case AppBarEdge.Left:
                        clipLeft = Math.Max(clipLeft, host.WindowRect.Right - rect.Left);
                        break;
                    case AppBarEdge.Right:
                        clipRight = Math.Min(clipRight, host.WindowRect.Left - rect.Left);
                        break;
                }

                if (clipLeft < clipRight && clipTop < clipBottom)
                {
                    if (host.FlowDirection == FlowDirection.RightToLeft)
                    {
                        // RTL uses a top-right origin instead of top-left
                        int left = rect.Width - clipRight;
                        int right = rect.Width - clipLeft;
                        clipLeft = left;
                        clipRight = right;
                    }

                    IntPtr hRgn = NativeMethods.CreateRectRgn(clipLeft, clipTop, clipRight, clipBottom);
                    if (hRgn != IntPtr.Zero && NativeMethods.SetWindowRgn(Handle, hRgn, true) == 0)
                    {
                        // If SetWindowRgn succeeds, the system owns the region.
                        // If it fails, we still own it and should delete it.
                        NativeMethods.DeleteObject(hRgn);
                    }
                }
            }
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Controls/FloatingStartButton.xaml.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Controls/TaskList.xaml.cs">
namespace RetroBar.Controls
{
    using ManagedShell.AppBar;
    using ManagedShell.WindowsTasks;
    using ManagedShell.Common.Helpers;
    using RetroBar.Utilities;
    using System;
    using System.ComponentModel;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;

    /// <summary>
    /// Interaction logic for TaskList.xaml
    /// </summary>
    public partial class TaskList : UserControl
    {
        private bool isLoaded;
        private bool isActive = true;
        private bool isScrollable;
        private double DefaultButtonWidth;
        private double MinButtonWidth;
        private double TaskButtonLeftMargin;
        private double TaskButtonRightMargin;
        private ICollectionView taskbarItems;

        public static DependencyProperty ButtonWidthProperty = DependencyProperty.Register(nameof(ButtonWidth), typeof(double), typeof(TaskList), new PropertyMetadata(new double()));

        public double ButtonWidth
        {
            get { return (double)GetValue(ButtonWidthProperty); }
            set { SetValue(ButtonWidthProperty, value); }
        }

        public static DependencyProperty ButtonsPerRowProperty = DependencyProperty.Register(nameof(ButtonsPerRow), typeof(int), typeof(TaskList), new PropertyMetadata(0));

        public int ButtonsPerRow
        {
            get { return (int)GetValue(ButtonsPerRowProperty); }
            set { SetValue(ButtonsPerRowProperty, value); }
        }

        // Number of columns in a full row that get one extra pixel, so the row fills the
        // taskbar exactly instead of leaving the floored remainder empty.
        public static DependencyProperty ExtraWidthCountProperty = DependencyProperty.Register(nameof(ExtraWidthCount), typeof(int), typeof(TaskList), new PropertyMetadata(0));

        public int ExtraWidthCount
        {
            get { return (int)GetValue(ExtraWidthCountProperty); }
            set { SetValue(ExtraWidthCountProperty, value); }
        }

        public static DependencyProperty TasksProperty = DependencyProperty.Register(nameof(Tasks), typeof(Tasks), typeof(TaskList), new PropertyMetadata(TasksChangedCallback));

        public Tasks Tasks
        {
            get { return (Tasks)GetValue(TasksProperty); }
            set { SetValue(TasksProperty, value); }
        }

        public static DependencyProperty HostProperty = DependencyProperty.Register(nameof(Host), typeof(Taskbar), typeof(TaskList), new PropertyMetadata(TasksChangedCallback));

        public Taskbar Host
        {
            get { return (Taskbar)GetValue(HostProperty); }
            set { SetValue(HostProperty, value); }
        }

        public TaskList()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Turns the task buttons on or off. The taskbar turns them off while the theme uses the Windows 7 superbar,
        /// so that no hidden task buttons answer for the windows.
        /// </summary>
        internal void SetActive(bool active)
        {
            if (isActive == active)
            {
                return;
            }

            isActive = active;

            if (isLoaded)
            {
                TasksList.ItemsSource = active ? taskbarItems : null;
            }
        }

        private void SetStyles()
        {
            DefaultButtonWidth = Application.Current.FindResource("TaskButtonWidth") as double? ?? 0;
            MinButtonWidth = Application.Current.FindResource("TaskButtonMinWidth") as double? ?? 0;
            Thickness buttonMargin;

            if (Settings.Instance.Edge == AppBarEdge.Left || Settings.Instance.Edge == AppBarEdge.Right)
            {
                buttonMargin = Application.Current.FindResource("TaskButtonVerticalMargin") as Thickness? ?? new Thickness();
            }
            else
            {
                buttonMargin = Application.Current.FindResource("TaskButtonMargin") as Thickness? ?? new Thickness();
            }

            TaskButtonLeftMargin = buttonMargin.Left;
            TaskButtonRightMargin = buttonMargin.Right;
        }

        private void TaskList_OnLoaded(object sender, RoutedEventArgs e)
        {
            SetStyles();
            SetTasksCollection();
        }

        private void SetTasksCollection()
        {
            if (!isLoaded && Tasks != null && Host != null)
            {
                taskbarItems = Tasks.CreateGroupedWindowsCollection();
                if (taskbarItems != null)
                {
                    taskbarItems.CollectionChanged += GroupedWindows_CollectionChanged;
                    taskbarItems.Filter = Tasks_Filter;
                }

                TasksList.ItemsSource = isActive ? taskbarItems : null;

                Settings.Instance.PropertyChanged += Settings_PropertyChanged;
                Host.hotkeyManager.TaskbarHotkeyPressed += TaskList_TaskbarHotkeyPressed;

                isLoaded = true;
            }
        }

        private static void TasksChangedCallback(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TaskList taskList && e.OldValue == null && e.NewValue != null)
            {
                taskList.SetTasksCollection();
            }
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.MultiMonMode))
            {
                taskbarItems?.Refresh();
            }
            else if (e.PropertyName == nameof(Settings.ShowMultiMon))
            {
                if (Settings.Instance.MultiMonMode != MultiMonOption.AllTaskbars)
                {
                    taskbarItems?.Refresh();
                }
            }
        }
        private void TaskList_TaskbarHotkeyPressed(object sender, HotkeyManager.TaskbarHotkeyEventArgs e)
        {
            if (isActive && Settings.Instance.WinNumHotkeysAction == WinNumHotkeysOption.SwitchTasks && Host.Screen.Primary)
            {
                try
                {
                    bool exists = taskbarItems.MoveCurrentToPosition(e.index);

                    if (exists)
                    {
                        ApplicationWindow window = taskbarItems.CurrentItem as ApplicationWindow;

                        if (e.isShiftPressed)
                        {
                            // Open new instance when Shift is pressed
                            ShellHelper.StartProcess(window.IsUWP ? "appx:" + window.AppUserModelID : window.WinFileName);
                        }
                        else
                        {
                            // Normal behavior - switch to existing window
                            if (window.State == ApplicationWindow.WindowState.Active && window.CanMinimize)
                            {
                                window.Minimize();
                            }
                            else
                            {
                                window.BringToFront();
                            }
                        }
                    }

                }
                catch (ArgumentOutOfRangeException) { }
            }
        }

        private bool Tasks_Filter(object obj)
        {
            if (obj is ApplicationWindow window)
            {
                if (!window.ShowInTaskbar)
                {
                    return false;
                }

                if (!Settings.Instance.ShowMultiMon || Settings.Instance.MultiMonMode == MultiMonOption.AllTaskbars)
                {
                    return true;
                }

                if (Settings.Instance.MultiMonMode == MultiMonOption.SameAsWindowAndPrimary && Host.Screen.Primary)
                {
                    return true;
                }

                IntPtr hMonitor = window.HMonitor;
                if (Host.Screen.Primary && !Host.windowManager.IsValidHMonitor(hMonitor))
                {
                    return true;
                }

                if (hMonitor != Host.Screen.HMonitor)
                {
                    return false;
                }
            }

            return true;
        }

        private void TaskList_OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (taskbarItems != null)
            {
                taskbarItems.CollectionChanged -= GroupedWindows_CollectionChanged;
                taskbarItems.Filter = null;
            }

            if (Host != null)
            {
                Host.hotkeyManager.TaskbarHotkeyPressed -= TaskList_TaskbarHotkeyPressed;
            }

            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;

            isLoaded = false;
        }

        private void GroupedWindows_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            SetTaskButtonWidth();
        }

        private void TaskList_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            SetTaskButtonWidth();
        }

        private void SetTaskButtonWidth()
        {
            if (Host is null)
                return; // The state is trashed, but presumably it's just a transition

            if (Settings.Instance.Edge == AppBarEdge.Left || Settings.Instance.Edge == AppBarEdge.Right)
            {
                ExtraWidthCount = 0;
                ButtonWidth = ActualWidth;
                SetScrollable(true); // while technically not always scrollable, we don't run into DPI-specific issues with it enabled while vertical
                return;
            }

            double height = ActualHeight;
            int rows = Host.Rows;

            int taskCount = TasksList.Items.Count;
            TasksList.AlternationCount = taskCount; // keep in sync for correct AlternationIndex

            double margin = TaskButtonLeftMargin + TaskButtonRightMargin;
            ButtonsPerRow = Math.Max(1, (int)Math.Ceiling((double)taskCount / rows));
            double maxWidth = TasksList.ActualWidth / ButtonsPerRow;
            double defaultWidth = DefaultButtonWidth + margin;
            double minWidth = MinButtonWidth + margin;

            if (maxWidth > defaultWidth)
            {
                // Room to spare: keep the default width and leave the trailing gap.
                ExtraWidthCount = 0;
                ButtonWidth = defaultWidth;
                SetScrollable(false);
            }
            else if (maxWidth < minWidth)
            {
                ExtraWidthCount = 0;
                ButtonWidth = Math.Ceiling(defaultWidth / 2);
                SetScrollable(true);
            }
            else
            {
                // Buttons are shrunk to fit a full row: spread the pixels lost to flooring
                // across the columns so the row fills the taskbar.
                double baseWidth = Math.Floor(maxWidth);
                ButtonWidth = baseWidth;
                ExtraWidthCount = (int)Math.Floor(TasksList.ActualWidth) - (int)baseWidth * ButtonsPerRow;
                SetScrollable(false);
            }
        }

        private void SetScrollable(bool canScroll)
        {
            if (canScroll == isScrollable) return;

            if (canScroll)
            {
                TasksScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
            else
            {
                TasksScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }

            isScrollable = canScroll;
        }

        private void TasksScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (!isScrollable && Settings.Instance.TaskWheelAction == TaskWheelActionOption.DoNothing)
            {
                e.Handled = true;
            }
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Controls/TaskList.xaml.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Controls/TaskButton.xaml.cs">
namespace RetroBar.Controls
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Data;
    using System.Windows.Input;
    using System.Windows.Media.Animation;
    using ManagedShell.Common.Helpers;
    using ManagedShell.Interop;
    using ManagedShell.WindowsTasks;
    using RetroBar.Converters;
    using RetroBar.Utilities;

    /// <summary>
    /// Interaction logic for TaskButton.xaml
    /// </summary>
    public partial class TaskButton : UserControl
    {
        public static DependencyProperty HostProperty = DependencyProperty.Register(nameof(Host), typeof(TaskList), typeof(TaskButton));

        public TaskList Host
        {
            get { return (TaskList)GetValue(HostProperty); }
            set { SetValue(HostProperty, value); }
        }

        private ApplicationWindow Window;
        private TaskButtonStyleConverter StyleConverter = new TaskButtonStyleConverter();
        private ApplicationWindow.WindowState PressedWindowState = ApplicationWindow.WindowState.Inactive;

        private DelayedActivationHandler dragHandler;
        private bool _isLoaded;

        public TaskButton()
        {
            InitializeComponent();
            SetStyle();
        }

        private void SetStyle()
        {
            MultiBinding multiBinding = new MultiBinding();
            multiBinding.Converter = StyleConverter;

            multiBinding.Bindings.Add(new Binding { RelativeSource = RelativeSource.Self });
            multiBinding.Bindings.Add(new Binding("State"));

            AppButton.SetBinding(StyleProperty, multiBinding);
        }

        private void ScrollIntoView()
        {
            if (Window == null)
            {
                return;
            }

            if (Window.State == ApplicationWindow.WindowState.Active)
            {
                BringIntoView();
            }
        }

        private void Animate()
        {
            var ease = new SineEase();
            ease.EasingMode = EasingMode.EaseInOut;

            DoubleAnimation animation = new DoubleAnimation();
            animation.From = 0;
            animation.To = Host?.ButtonWidth ?? ActualWidth;
            animation.Duration = new Duration(TimeSpan.FromMilliseconds(250));
            animation.FillBehavior = FillBehavior.Stop;
            animation.EasingFunction = ease;
            Storyboard.SetTarget(animation, this);
            Storyboard.SetTargetProperty(animation, new PropertyPath(WidthProperty));

            Storyboard storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }

        private void TaskButton_OnLoaded(object sender, RoutedEventArgs e)
        {
            Window = DataContext as ApplicationWindow;

            Settings.Instance.PropertyChanged += Settings_PropertyChanged;

            dragHandler = new DelayedActivationHandler(() =>
            {
                Window?.BringToFront();
            });

            if (Window != null)
            {
                Window.GetButtonRect += Window_GetButtonRect;
                Window.PropertyChanged += Window_PropertyChanged;
            }

            if (Settings.Instance.SlideTaskbarButtons && Host?.Host?.Orientation == Orientation.Horizontal)
            {
                Animate();
            }

            _isLoaded = true;
        }

        private void Window_GetButtonRect(ref NativeMethods.ShortRect rect)
        {
            if (Host?.Host?.Screen.Primary != true && Settings.Instance.MultiMonMode != MultiMonOption.SameAsWindow)
            {
                // If there are multiple instances of a button, use the button on the primary display only
                return;
            }

            Point buttonTopLeft = PointToScreen(new Point(0, 0));
            Point buttonBottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
            rect.Top = (short)buttonTopLeft.Y;
            rect.Left = (short)buttonTopLeft.X;
            rect.Bottom = (short)buttonBottomRight.Y;
            rect.Right = (short)buttonBottomRight.X;
        }

        private void Window_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "State")
            {
                ScrollIntoView();
            }
        }

        private void TaskButton_OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded)
            {
                return;
            }

            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
            dragHandler?.Dispose();

            if (Window != null)
            {
                Window.GetButtonRect -= Window_GetButtonRect;
                Window.PropertyChanged -= Window_PropertyChanged;
            }

            _isLoaded = false;
        }

        private void AppButton_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (Window == null)
            {
                return;
            }

            NativeMethods.WindowShowStyle wss = Window.ShowStyle;
            int ws = Window.WindowStyles;

            // disable window operations depending on current window state. originally tried implementing via bindings but found there is no notification we get regarding maximized state
            MaximizeMenuItem.IsEnabled = wss != NativeMethods.WindowShowStyle.ShowMaximized && (ws & (int)NativeMethods.WindowStyles.WS_MAXIMIZEBOX) != 0;
            MinimizeMenuItem.IsEnabled = wss != NativeMethods.WindowShowStyle.ShowMinimized && Window.CanMinimize;
            if (RestoreMenuItem.IsEnabled = wss != NativeMethods.WindowShowStyle.ShowNormal)
            {
                CloseMenuItem.FontWeight = FontWeights.Normal;
                RestoreMenuItem.FontWeight = FontWeights.Bold;
            }
            if (!RestoreMenuItem.IsEnabled || RestoreMenuItem.IsEnabled && !MaximizeMenuItem.IsEnabled)
            {
                CloseMenuItem.FontWeight = FontWeights.Bold;
                RestoreMenuItem.FontWeight = FontWeights.Normal;
            }
            MoveMenuItem.IsEnabled = wss == NativeMethods.WindowShowStyle.ShowNormal;
            SizeMenuItem.IsEnabled = wss == NativeMethods.WindowShowStyle.ShowNormal && (ws & (int)NativeMethods.WindowStyles.WS_MAXIMIZEBOX) != 0;
        }

        private void CloseMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            Window?.Close();
        }

        private void EndTaskMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            if (Window != null)
            {
                ForceEndTask();
            }
        }

        private void ForceEndTask()
        {
            try
            {
                if (Window.ProcId.HasValue && Window.ProcId.Value != 0)
                {
                    // Don't kill RetroBar itself - just close the window gracefully
                    int currentProcId = Process.GetCurrentProcess().Id;
                    if (Window.ProcId.Value == currentProcId)
                    {
                        Window?.Close();
                        return;
                    }

                    Process process = Process.GetProcessById((int)Window.ProcId.Value);
                    process.Kill();
                }
            }
            catch (Exception)
            {
                Window?.Close();
            }
        }

        private void RestoreMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            Window?.Restore();
        }

        private void MoveMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            Window?.Move();
        }

        private void SizeMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            Window?.Size();
        }

        private void MinimizeMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            Window?.Minimize();
        }

        private void MaximizeMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            Window?.Maximize();
        }

        private void AppButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (PressedWindowState == ApplicationWindow.WindowState.Active && Window?.CanMinimize == true)
            {
                Window?.Minimize();
            }
            else
            {
                Window?.BringToFront();
            }
        }

        private void AppButton_OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Settings.Instance.TaskWheelAction == TaskWheelActionOption.DoNothing || (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                return;
            }

            if (e.Delta > 0)
            {
                // SetForegroundWindow can fail if the process doesn't have foreground rights
                // We need to trigger a keyboard event for Windows to grant us this ability
                // The key code of 0xE8 is unassigned, so no apps should handle it
                var input = new NativeMethods.INPUT[]
                {
                    new() { type = NativeMethods.INPUT_KEYBOARD, mkhi = new() { ki = new() { wVk = 0xE8 } } },
                    new() { type = NativeMethods.INPUT_KEYBOARD, mkhi = new() { ki = new() { wVk = 0xE8, dwFlags = NativeMethods.KEYEVENTF_KEYUP } } }
                };
                NativeMethods.SendInput((uint)input.Length, input, Marshal.SizeOf<NativeMethods.INPUT>());
                Window?.BringToFront();
            }
            else if (Window?.CanMinimize == true)
            {
                Window?.Minimize();
            }
            e.Handled = true;
        }

        private void AppButton_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                PressedWindowState = Window.State;
            }
        }

        private void AppButton_OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                if (Window == null || Settings.Instance.TaskMiddleClickAction == TaskMiddleClickOption.DoNothing)
                {
                    return;
                }
                if (Settings.Instance.TaskMiddleClickAction == TaskMiddleClickOption.CloseTask !=
                    (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)))
                {
                    Window?.Close();
                }
                else
                {
                    ShellHelper.StartProcess(Window.IsUWP ? "appx:" + Window.AppUserModelID : Window.WinFileName);
                }
            }
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.Theme))
            {
                SetStyle();
            }
        }

        private void AppButton_OnDragEnter(object sender, DragEventArgs e)
        {
            dragHandler?.OnDragEnter(e);
        }

        private void AppButton_OnDragLeave(object sender, DragEventArgs e)
        {
            dragHandler?.OnDragLeave();
        }

        private void ContextMenu_OpenedOrClosed(object sender, RoutedEventArgs e)
        {
            BindingOperations.GetMultiBindingExpression(AppButton, StyleProperty).UpdateTarget();
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Controls/TaskButton.xaml.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Controls/TaskThumbnail.xaml.cs">
namespace RetroBar.Controls
{
    using System;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Interop;
    using System.Windows.Media;
    using System.Windows.Threading;
    using ManagedShell.Interop;

    /// <summary>
    /// Interaction logic for TaskThumbnail.xaml
    /// </summary>
    public partial class TaskThumbnail : UserControl
    {
        const double MAX_WIDTH = 180;
        const double MAX_HEIGHT = 120;

        public double DpiScale = 1.0;

        private DispatcherTimer _toolTipTimer;
        private EventHandler _renderingHandler;

        public TaskThumbnail()
        {
            InitializeComponent();

            _toolTipTimer = new DispatcherTimer();
            _toolTipTimer.Tick += ToolTipTimer_Tick;
            _toolTipTimer.Interval = new TimeSpan(0, 0, 0, 0, ToolTipService.GetInitialShowDelay(this));
        }

        public IntPtr Handle
        {
            get
            {
                HwndSource source = (HwndSource)PresentationSource.FromVisual(this);

                if (source == null)
                {
                    return IntPtr.Zero;
                }

                IntPtr handle = source.Handle;
                return handle;
            }
        }

        private IntPtr _thumbHandle;

        public static DependencyProperty SourceWindowHandleProperty = DependencyProperty.Register(nameof(SourceWindowHandle), typeof(IntPtr), typeof(TaskThumbnail), new PropertyMetadata(new IntPtr()));

        public IntPtr SourceWindowHandle
        {
            get
            {
                return (IntPtr)GetValue(SourceWindowHandleProperty);
            }
            set
            {
                SetValue(SourceWindowHandleProperty, value);
            }
        }

        public static DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(TaskThumbnail), new PropertyMetadata(""));

        public string Title
        {
            get
            {
                return (string)GetValue(TitleProperty);
            }
            set
            {
                SetValue(TitleProperty, value);
            }
        }

        public NativeMethods.Rect Rect
        {
            get
            {
                try
                {
                    if (this == null)
                        return new NativeMethods.Rect(0, 0, 0, 0);

                    var generalTransform = TransformToAncestor((System.Windows.Media.Visual)Parent);
                    var leftTopPoint = generalTransform.Transform(new Point(0, 0));
                    return new NativeMethods.Rect(
                          (int)(leftTopPoint.X * DpiScale),
                          (int)(leftTopPoint.Y * DpiScale),
                          (int)(leftTopPoint.X * DpiScale) + (int)(MAX_WIDTH * DpiScale),
                          (int)(leftTopPoint.Y * DpiScale) + (int)(MAX_HEIGHT * DpiScale)
                         );
                }
                catch
                {
                    return new NativeMethods.Rect(0, 0, 0, 0);
                }
            }
        }

        public void Refresh()
        {
            if (this == null)
                return;

            if (_thumbHandle == IntPtr.Zero)
                return;

            if (this != null)
            {
                var clientAreaProps = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
                {
                    dwFlags = NativeMethods.DWM_TNP_SOURCECLIENTAREAONLY,
                    fSourceClientAreaOnly = true
                };
                NativeMethods.DwmUpdateThumbnailProperties(_thumbHandle, ref clientAreaProps);

                NativeMethods.DwmQueryThumbnailSourceSize(_thumbHandle, out NativeMethods.PSIZE size);
                double aspectRatio = (double)size.x / size.y;

                var props = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
                {
                    fVisible = true,
                    dwFlags = NativeMethods.DWM_TNP_VISIBLE | NativeMethods.DWM_TNP_RECTDESTINATION,
                    rcDestination = Rect
                };

                if (this != null)
                {
                    if (size.x <= (MAX_WIDTH * DpiScale) && size.y <= (MAX_HEIGHT * DpiScale))
                    {
                        // small, do not scale
                        Width = size.x / DpiScale;
                        Height = size.y / DpiScale;
                        props.rcDestination.Right = props.rcDestination.Left + size.x;
                        props.rcDestination.Bottom = props.rcDestination.Top + size.y;
                    }
                    else
                    {
                        // large, scale preserving aspect ratio
                        double controlAspectRatio = MAX_WIDTH / MAX_HEIGHT;

                        if (aspectRatio > controlAspectRatio)
                        {
                            // wide
                            int height = (int)(MAX_WIDTH / aspectRatio);

                            Width = MAX_WIDTH;
                            Height = height;
                            props.rcDestination.Bottom = props.rcDestination.Top + (int)(height * DpiScale);
                        }
                        else if (aspectRatio < controlAspectRatio)
                        {
                            // tall
                            int width = (int)(MAX_HEIGHT * aspectRatio);

                            Width = width;
                            Height = MAX_HEIGHT;
                            props.rcDestination.Right = props.rcDestination.Left + (int)(width * DpiScale);
                        }
                    }
                }

                if (this != null)
                    NativeMethods.DwmUpdateThumbnailProperties(_thumbHandle, ref props);
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_renderingHandler != null)
            {
                CompositionTarget.Rendering -= _renderingHandler;
                _renderingHandler = null;
            }

            if (_thumbHandle != IntPtr.Zero)
            {
                NativeMethods.DwmUnregisterThumbnail(_thumbHandle);
                _thumbHandle = IntPtr.Zero;
            }

            _toolTipTimer.Stop();
            if (ToolTip is ToolTip tip)
            {
                tip.IsOpen = false;
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            DpiScale = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.M11;

            if (NativeMethods.DwmIsCompositionEnabled() && SourceWindowHandle != IntPtr.Zero && Handle != IntPtr.Zero && NativeMethods.DwmRegisterThumbnail(Handle, SourceWindowHandle, out _thumbHandle) == 0)
            {
                Refresh();
                // once loaded, we need to refresh the thumbnail...
                _renderingHandler = (s, a) => Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(Refresh));
                CompositionTarget.Rendering += _renderingHandler;
            }

            _toolTipTimer.Start();
        }

        private void ToolTipTimer_Tick(object sender, EventArgs e)
        {
            if (ToolTip is ToolTip tip)
            {
                tip.PlacementTarget = this;
                tip.IsOpen = true;
            }
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Controls/TaskThumbnail.xaml.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Controls/SuperbarTaskList.xaml.cs">
namespace RetroBar.Controls
{
    using ManagedShell.Common.Helpers;
    using ManagedShell.Interop;
    using ManagedShell.WindowsTasks;
    using RetroBar.StartMenu;
    using RetroBar.Superbar;
    using RetroBar.Utilities;
    using System;
    using System.ComponentModel;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using System.Windows.Threading;

    /// <summary>
    /// The Windows 7 taskbar buttons (the "superbar"): one icon-only button per program, pinned programs first,
    /// with each program's windows combined. Used instead of TaskList by themes that set UseSuperbar.
    /// </summary>
    public partial class SuperbarTaskList : UserControl
    {
        // Windows 7 shows the window list after the mouse rests on a button, and hides it shortly after the mouse leaves
        private static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(400);
        private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(500);

        public static readonly DependencyProperty TasksProperty = DependencyProperty.Register(nameof(Tasks), typeof(Tasks), typeof(SuperbarTaskList), new PropertyMetadata(null, OnHostOrTasksChanged));

        public Tasks Tasks
        {
            get => (Tasks)GetValue(TasksProperty);
            set => SetValue(TasksProperty, value);
        }

        public static readonly DependencyProperty HostProperty = DependencyProperty.Register(nameof(Host), typeof(Taskbar), typeof(SuperbarTaskList), new PropertyMetadata(null, OnHostOrTasksChanged));

        public Taskbar Host
        {
            get => (Taskbar)GetValue(HostProperty);
            set => SetValue(HostProperty, value);
        }

        private SuperbarModel _model;
        private Taskbar _subscribedHost;
        private bool _isActive;
        private bool _isLoaded;
        private SuperbarWindowList _windowList;
        private bool _windowListHoldsTaskbar;
        private SuperbarButton _hoverButton;
        private SuperbarItem _reorderItem;
        private readonly DispatcherTimer _showTimer;
        private readonly DispatcherTimer _hideTimer;

        public SuperbarTaskList()
        {
            InitializeComponent();

            _showTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = ShowDelay };
            _showTimer.Tick += ShowTimer_Tick;
            _hideTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = HideDelay };
            _hideTimer.Tick += HideTimer_Tick;
        }

        /// <summary>Whether buttons use the glass look; otherwise the opaque Windows 7 Basic look.</summary>
        internal bool UsesGlassLook => Settings.Instance.EnableAeroGlass &&
                                       (Application.Current.TryFindResource("AllowsTransparency") as bool? ?? false);

        /// <summary>Whether the window list shows live thumbnails, as Windows 7 does with Aero; otherwise a list of titles.</summary>
        internal bool UsesThumbnails => UsesGlassLook && NativeMethods.DwmIsCompositionEnabled();

        internal bool IsWindowListOpen => _windowList != null && _windowList.IsOpen;

        /// <summary>Turns the superbar on or off; the taskbar calls this when the theme changes.</summary>
        internal void SetActive(bool active)
        {
            _isActive = active;
            UpdateModel();
        }

        private static void OnHostOrTasksChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            ((SuperbarTaskList)sender).UpdateModel();
        }

        private void SuperbarTaskList_OnLoaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            UpdateModel();
        }

        private void SuperbarTaskList_OnUnloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
            UpdateModel();

            if (_windowList != null)
            {
                _windowList.MouseEnter -= WindowList_MouseEnter;
                _windowList.MouseLeave -= WindowList_MouseLeave;
                _windowList.WindowChosen -= WindowList_WindowChosen;

                if (!_windowList.IsClosed)
                {
                    _windowList.Close();
                }

                _windowList = null;
            }
        }

        /// <summary>Creates the model while the superbar is shown and loaded, and releases it otherwise.</summary>
        private void UpdateModel()
        {
            bool shouldRun = _isActive && _isLoaded && Tasks != null && Host != null;

            if (shouldRun && _model == null)
            {
                Taskbar host = Host;
                _model = new SuperbarModel(Tasks, entry => entry is ApplicationWindow window && host.ShowsTask(window));
                ButtonsList.ItemsSource = _model.Items;

                Settings.Instance.PropertyChanged += Settings_PropertyChanged;
                _subscribedHost = host;
                _subscribedHost.hotkeyManager.TaskbarHotkeyPressed += Host_TaskbarHotkeyPressed;
            }
            else if (!shouldRun && _model != null)
            {
                HideWindowList();
                EndReorder();

                Settings.Instance.PropertyChanged -= Settings_PropertyChanged;

                if (_subscribedHost != null)
                {
                    _subscribedHost.hotkeyManager.TaskbarHotkeyPressed -= Host_TaskbarHotkeyPressed;
                    _subscribedHost = null;
                }

                ButtonsList.ItemsSource = null;
                _model.Dispose();
                _model = null;
            }
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.MultiMonMode) || e.PropertyName == nameof(Settings.ShowMultiMon))
            {
                _model?.RefreshFilter();
            }
            else if (e.PropertyName == nameof(Settings.Theme) || e.PropertyName == nameof(Settings.EnableAeroGlass))
            {
                HideWindowList();
            }
        }

        #region Activation

        /// <summary>
        /// A click on a button, as Windows 7 handles it: a program that is not running (or Shift+click) starts,
        /// a single window is shown or minimized, and a combined button shows its windows (Ctrl+click cycles them).
        /// </summary>
        internal void ActivateItem(SuperbarButton button, ModifierKeys modifiers, bool wasActive)
        {
            SuperbarItem item = button?.Item;

            if (item == null)
            {
                return;
            }

            if (!item.IsRunning || (modifiers & ModifierKeys.Shift) != 0)
            {
                HideWindowList();
                StartNewInstance(item);
                return;
            }

            if (item.Windows.Count == 1)
            {
                HideWindowList();
                ApplicationWindow window = item.FirstWindow;

                if (wasActive && window.CanMinimize)
                {
                    window.Minimize();
                }
                else
                {
                    window.BringToFront();
                }

                return;
            }

            if ((modifiers & ModifierKeys.Control) != 0)
            {
                HideWindowList();
                CycleWindows(item);
                return;
            }

            // With thumbnails the click keeps the list the hover opened; the Basic list opens and closes on click
            if (IsWindowListOpen && _windowList.Item == item && !UsesThumbnails)
            {
                HideWindowList();
            }
            else
            {
                ShowWindowList(button);
            }
        }

        internal void StartNewInstance(SuperbarItem item)
        {
            if (item?.Pin?.Entry != null)
            {
                StartMenuLauncher.Launch(item.Pin.Entry);
                return;
            }

            ApplicationWindow window = item?.FirstWindow;

            if (window != null)
            {
                ShellHelper.StartProcess(window.IsUWP ? "appx:" + window.AppUserModelID : window.WinFileName);
            }
        }

        /// <summary>Brings the next window of a combined button to the front.</summary>
        private static void CycleWindows(SuperbarItem item)
        {
            int count = item.Windows.Count;

            if (count == 0)
            {
                return;
            }

            int active = -1;

            for (int i = 0; i < count; i++)
            {
                if (item.Windows[i].State == ApplicationWindow.WindowState.Active)
                {
                    active = i;
                    break;
                }
            }

            item.Windows[(active + 1) % count].BringToFront();
        }

        private void Host_TaskbarHotkeyPressed(object sender, HotkeyManager.TaskbarHotkeyEventArgs e)
        {
            if (_model == null || Settings.Instance.WinNumHotkeysAction != WinNumHotkeysOption.SwitchTasks || Host?.Screen.Primary != true)
            {
                return;
            }

            if (e.index < 0 || e.index >= _model.Items.Count)
            {
                return;
            }

            SuperbarItem item = _model.Items[e.index];

            if (e.isShiftPressed || !item.IsRunning)
            {
                StartNewInstance(item);
            }
            else if (item.Windows.Count == 1)
            {
                ApplicationWindow window = item.FirstWindow;

                if (window.State == ApplicationWindow.WindowState.Active && window.CanMinimize)
                {
                    window.Minimize();
                }
                else
                {
                    window.BringToFront();
                }
            }
            else
            {
                CycleWindows(item);
            }
        }

        #endregion

        #region Window list

        internal void OnButtonMouseEnter(SuperbarButton button)
        {
            _hideTimer.Stop();
            _hoverButton = button;

            if (_reorderItem != null || button.Item == null || !button.Item.IsRunning || !UsesThumbnails)
            {
                _showTimer.Stop();
                return;
            }

            if (IsWindowListOpen)
            {
                // Moving along the taskbar switches the thumbnails straight away
                ShowWindowList(button);
                return;
            }

            _showTimer.Stop();
            _showTimer.Start();
        }

        internal void OnButtonMouseLeave(SuperbarButton button)
        {
            if (_hoverButton == button)
            {
                _hoverButton = null;
            }

            _showTimer.Stop();

            if (IsWindowListOpen)
            {
                _hideTimer.Stop();
                _hideTimer.Start();
            }
        }

        private void ShowTimer_Tick(object sender, EventArgs e)
        {
            _showTimer.Stop();

            if (_hoverButton != null && _hoverButton.IsMouseOver && _reorderItem == null)
            {
                ShowWindowList(_hoverButton);
            }
        }

        private void HideTimer_Tick(object sender, EventArgs e)
        {
            _hideTimer.Stop();

            if (_windowList?.IsMouseOver == true || _hoverButton?.IsMouseOver == true)
            {
                return;
            }

            HideWindowList();
        }

        internal void ShowWindowList(SuperbarButton button)
        {
            SuperbarItem item = button?.Item;

            if (item == null || !item.IsRunning || Host == null || _model == null)
            {
                return;
            }

            _showTimer.Stop();
            _hideTimer.Stop();

            if (IsWindowListOpen && ReferenceEquals(_windowList.Item, item))
            {
                // Already showing this button's windows
                return;
            }

            if (_windowList == null || _windowList.IsClosed)
            {
                _windowList = new SuperbarWindowList();
                _windowList.MouseEnter += WindowList_MouseEnter;
                _windowList.MouseLeave += WindowList_MouseLeave;
                _windowList.WindowChosen += WindowList_WindowChosen;
            }

            _windowList.ShowFor(Host, button, item, UsesThumbnails, UsesGlassLook);

            if (!_windowListHoldsTaskbar)
            {
                // Keeps an auto-hiding taskbar shown while the list is open
                Host.AddOpenMenu();
                _windowListHoldsTaskbar = true;
            }
        }

        internal void HideWindowList()
        {
            _showTimer.Stop();
            _hideTimer.Stop();
            _windowList?.HideList();

            if (_windowListHoldsTaskbar)
            {
                _windowListHoldsTaskbar = false;
                Host?.RemoveOpenMenu();
            }
        }

        private void WindowList_MouseEnter(object sender, MouseEventArgs e)
        {
            _hideTimer.Stop();
        }

        private void WindowList_MouseLeave(object sender, MouseEventArgs e)
        {
            _hideTimer.Stop();
            _hideTimer.Start();
        }

        private void WindowList_WindowChosen(object sender, EventArgs e)
        {
            HideWindowList();
        }

        #endregion

        #region Reordering

        /// <summary>Starts dragging a button to a new place. The list takes the mouse capture until the button is released.</summary>
        internal void BeginReorder(SuperbarButton button)
        {
            if (_model == null || button?.Item == null || _reorderItem != null || !_model.IsReorderable(button.Item))
            {
                return;
            }

            HideWindowList();
            _reorderItem = button.Item;

            if (!CaptureMouse())
            {
                _reorderItem = null;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_reorderItem == null || !IsMouseCaptured || _model == null)
            {
                return;
            }

            int target = IndexAt(e.GetPosition(ButtonsList));

            if (target >= 0)
            {
                _model.Move(_reorderItem, target);
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);

            if (_reorderItem != null)
            {
                e.Handled = true;
                ReleaseMouseCapture();
                EndReorder();
            }
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            EndReorder();
        }

        private void EndReorder()
        {
            if (_reorderItem == null)
            {
                return;
            }

            _reorderItem = null;
            _model?.SavePinnedOrder();
        }

        private int IndexAt(Point position)
        {
            for (int i = 0; i < ButtonsList.Items.Count; i++)
            {
                if (ButtonsList.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement container && container.IsVisible)
                {
                    Point topLeft = container.TranslatePoint(new Point(0, 0), ButtonsList);

                    if (new Rect(topLeft, new Size(container.ActualWidth, container.ActualHeight)).Contains(position))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        #endregion
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Controls/SuperbarTaskList.xaml.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Controls/SuperbarButton.xaml.cs">
namespace RetroBar.Controls
{
    using ManagedShell.Common.Helpers;
    using ManagedShell.Interop;
    using ManagedShell.WindowsTasks;
    using RetroBar.Superbar;
    using RetroBar.Utilities;
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;

    /// <summary>
    /// One button of the Windows 7 taskbar. Its DataContext is a SuperbarItem.
    /// </summary>
    public partial class SuperbarButton : UserControl
    {
        public static readonly DependencyProperty HostProperty = DependencyProperty.Register(nameof(Host), typeof(SuperbarTaskList), typeof(SuperbarButton), new PropertyMetadata(null, OnHostChanged));

        public SuperbarTaskList Host
        {
            get => (SuperbarTaskList)GetValue(HostProperty);
            set => SetValue(HostProperty, value);
        }

        private SuperbarItem _item;
        private readonly List<ApplicationWindow> _rectWindows = new List<ApplicationWindow>();
        private DelayedActivationHandler _dragHandler;
        private bool _isLoaded;
        private bool _wasActiveOnPress;
        private Point? _pressPoint;
        private bool _menuHoldsTaskbar;

        public SuperbarButton()
        {
            InitializeComponent();
        }

        internal SuperbarItem Item => _item;

        private static void OnHostChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            var button = (SuperbarButton)sender;
            button.UpdateStyle();
            button.UpdateToolTip();
        }

        private void SuperbarButton_OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_isLoaded)
            {
                return;
            }

            _isLoaded = true;
            Settings.Instance.PropertyChanged += Settings_PropertyChanged;

            _dragHandler = new DelayedActivationHandler(() =>
            {
                // Dragging something over a button shows its window, as on any taskbar
                if (_item?.Windows.Count == 1)
                {
                    _item.FirstWindow.BringToFront();
                }
                else if (_item?.IsRunning == true)
                {
                    Host?.ShowWindowList(this);
                }
            });

            Attach(DataContext as SuperbarItem);
            UpdateStyle();
        }

        private void SuperbarButton_OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded)
            {
                return;
            }

            _isLoaded = false;
            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
            _dragHandler?.Dispose();
            _dragHandler = null;
            Attach(null);
        }

        private void SuperbarButton_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_isLoaded)
            {
                Attach(DataContext as SuperbarItem);
            }
        }

        private void Attach(SuperbarItem item)
        {
            if (ReferenceEquals(item, _item))
            {
                return;
            }

            if (_item != null)
            {
                _item.PropertyChanged -= Item_PropertyChanged;
                _item.Windows.CollectionChanged -= Windows_CollectionChanged;
            }

            _item = item;

            if (_item != null)
            {
                _item.PropertyChanged += Item_PropertyChanged;
                _item.Windows.CollectionChanged += Windows_CollectionChanged;
            }

            SyncWindowSubscriptions();
            UpdateToolTip();
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SuperbarItem.ToolTipText) || e.PropertyName == nameof(SuperbarItem.IsRunning))
            {
                UpdateToolTip();
            }
        }

        private void Windows_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            SyncWindowSubscriptions();
        }

        /// <summary>Answers each window's request for its button's position, used for the minimize animation.</summary>
        private void SyncWindowSubscriptions()
        {
            List<ApplicationWindow> current = _item?.Windows.ToList() ?? new List<ApplicationWindow>();

            foreach (ApplicationWindow window in _rectWindows.Where(w => !current.Contains(w)).ToList())
            {
                window.GetButtonRect -= Window_GetButtonRect;
                _rectWindows.Remove(window);
            }

            foreach (ApplicationWindow window in current.Where(w => !_rectWindows.Contains(w)).ToList())
            {
                window.GetButtonRect += Window_GetButtonRect;
                _rectWindows.Add(window);
            }
        }

        private void Window_GetButtonRect(ref NativeMethods.ShortRect rect)
        {
            if (Host?.Host?.Screen.Primary != true && Settings.Instance.MultiMonMode != MultiMonOption.SameAsWindow)
            {
                // If there are multiple instances of a button, use the button on the primary display only
                return;
            }

            if (!IsVisible)
            {
                return;
            }

            Point buttonTopLeft = PointToScreen(new Point(0, 0));
            Point buttonBottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
            rect.Top = (short)buttonTopLeft.Y;
            rect.Left = (short)buttonTopLeft.X;
            rect.Bottom = (short)buttonBottomRight.Y;
            rect.Right = (short)buttonBottomRight.X;
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.Theme) || e.PropertyName == nameof(Settings.EnableAeroGlass))
            {
                UpdateStyle();
                UpdateToolTip();
            }
        }

        private void UpdateStyle()
        {
            AppButton.SetResourceReference(StyleProperty, Host?.UsesGlassLook == false ? "SuperbarButtonBasic" : "SuperbarButton");
        }

        private void UpdateToolTip()
        {
            if (_item == null)
            {
                AppButton.ToolTip = null;
                return;
            }

            // With thumbnails, the window list takes the place of the tooltip for running programs
            bool showsWindowList = _item.IsRunning && Host?.UsesThumbnails == true;
            AppButton.ToolTip = showsWindowList ? null : _item.ToolTipText;
        }

        #region Mouse

        private void AppButton_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _wasActiveOnPress = _item?.Windows.Count == 1 && _item.FirstWindow.State == ApplicationWindow.WindowState.Active;
            _pressPoint = e.GetPosition(this);
        }

        private void AppButton_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _pressPoint = null;
        }

        private void AppButton_OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_item != null && AppButton.ActualWidth > 0)
            {
                // The hover glow follows the mouse across the button
                double x = e.GetPosition(AppButton).X / AppButton.ActualWidth;
                _item.HotTrackCenter = new Point(Math.Max(0, Math.Min(1, x)), 1);
            }

            if (_pressPoint is Point start && e.LeftButton == MouseButtonState.Pressed)
            {
                Point now = e.GetPosition(this);

                if (Math.Abs(now.X - start.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(now.Y - start.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _pressPoint = null;
                    Host?.BeginReorder(this);
                }
            }
        }

        private void AppButton_OnMouseEnter(object sender, MouseEventArgs e)
        {
            Host?.OnButtonMouseEnter(this);
        }

        private void AppButton_OnMouseLeave(object sender, MouseEventArgs e)
        {
            if (_item != null)
            {
                _item.HotTrackCenter = new Point(0.5, 1);
            }

            Host?.OnButtonMouseLeave(this);
        }

        private void AppButton_OnClick(object sender, RoutedEventArgs e)
        {
            Host?.ActivateItem(this, Keyboard.Modifiers, _wasActiveOnPress);
        }

        private void AppButton_OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle || _item == null)
            {
                return;
            }

            // Windows 7 opens a new instance on a middle click; closing applies to a button with a single window
            if (Settings.Instance.TaskMiddleClickAction == TaskMiddleClickOption.CloseTask && _item.Windows.Count == 1 &&
                !(Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)))
            {
                _item.FirstWindow.Close();
            }
            else if (Settings.Instance.TaskMiddleClickAction != TaskMiddleClickOption.DoNothing)
            {
                Host?.HideWindowList();
                Host?.StartNewInstance(_item);
            }
        }

        private void AppButton_OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Settings.Instance.TaskWheelAction == TaskWheelActionOption.DoNothing || (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ||
                _item?.Windows.Count != 1)
            {
                return;
            }

            ApplicationWindow window = _item.FirstWindow;

            if (e.Delta > 0)
            {
                // SetForegroundWindow needs foreground rights; an unassigned key press (0xE8) grants them
                var input = new NativeMethods.INPUT[]
                {
                    new() { type = NativeMethods.INPUT_KEYBOARD, mkhi = new() { ki = new() { wVk = 0xE8 } } },
                    new() { type = NativeMethods.INPUT_KEYBOARD, mkhi = new() { ki = new() { wVk = 0xE8, dwFlags = NativeMethods.KEYEVENTF_KEYUP } } }
                };
                NativeMethods.SendInput((uint)input.Length, input, Marshal.SizeOf<NativeMethods.INPUT>());
                window.BringToFront();
            }
            else if (window.CanMinimize)
            {
                window.Minimize();
            }

            e.Handled = true;
        }

        private void AppButton_OnDragEnter(object sender, DragEventArgs e)
        {
            _dragHandler?.OnDragEnter(e);
        }

        private void AppButton_OnDragLeave(object sender, DragEventArgs e)
        {
            _dragHandler?.OnDragLeave();
        }

        #endregion

        #region Context menu

        /// <summary>
        /// Builds the menu Windows 7 shows below a button's jump list: the program, pin or unpin, and close.
        /// Shift+right-click on a single window shows the window's own menu instead.
        /// </summary>
        private void AppButton_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            ButtonMenu.Items.Clear();

            if (_item == null)
            {
                e.Handled = true;
                return;
            }

            Host?.HideWindowList();

            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            if (shift && _item.Windows.Count == 1)
            {
                AddWindowMenu(_item.FirstWindow);
                return;
            }

            if (shift && _item.Windows.Count > 1)
            {
                AddMenuItem("superbar_restore_all", "Restore all windows", () => ForEachWindow(w => w.Restore()));
                AddMenuItem("superbar_minimize_all", "Minimize all windows", () => ForEachWindow(w => { if (w.CanMinimize) w.Minimize(); }));
                ButtonMenu.Items.Add(new Separator());
                AddMenuItem("superbar_close_all", "Close all windows", () => ForEachWindow(w => w.Close()));
                return;
            }

            MenuItem program = AddMenuItem(null, _item.Name, () => Host?.StartNewInstance(_item));
            program.Icon = new Image { Source = _item.Icon, Width = 16, Height = 16 };

            string pinKey = _item.Pin?.Key ?? TaskbarPins.KeyForWindow(_item.FirstWindow);

            if (pinKey != null)
            {
                if (_item.IsPinned)
                {
                    AddMenuItem("superbar_unpin", "Unpin this program from taskbar", () => TaskbarPins.Unpin(pinKey));
                }
                else
                {
                    AddMenuItem("superbar_pin", "Pin this program to taskbar", () => TaskbarPins.Pin(pinKey));
                }
            }

            if (_item.Windows.Count == 1)
            {
                AddMenuItem("superbar_close_window", "Close window", () => _item?.FirstWindow?.Close());
            }
            else if (_item.Windows.Count > 1)
            {
                AddMenuItem("superbar_close_all", "Close all windows", () => ForEachWindow(w => w.Close()));
            }
        }

        private void AddWindowMenu(ApplicationWindow window)
        {
            NativeMethods.WindowShowStyle showStyle = window.ShowStyle;
            int styles = window.WindowStyles;
            bool canMaximize = (styles & (int)NativeMethods.WindowStyles.WS_MAXIMIZEBOX) != 0;

            MenuItem restore = AddMenuItem("restore", "Restore", () => window.Restore());
            restore.IsEnabled = showStyle != NativeMethods.WindowShowStyle.ShowNormal;

            MenuItem move = AddMenuItem("move", "Move", () => window.Move());
            move.IsEnabled = showStyle == NativeMethods.WindowShowStyle.ShowNormal;

            MenuItem size = AddMenuItem("size", "Size", () => window.Size());
            size.IsEnabled = showStyle == NativeMethods.WindowShowStyle.ShowNormal && canMaximize;

            MenuItem minimize = AddMenuItem("minimize", "Minimize", () => window.Minimize());
            minimize.IsEnabled = showStyle != NativeMethods.WindowShowStyle.ShowMinimized && window.CanMinimize;

            MenuItem maximize = AddMenuItem("maximize", "Maximize", () => window.Maximize());
            maximize.IsEnabled = showStyle != NativeMethods.WindowShowStyle.ShowMaximized && canMaximize;

            ButtonMenu.Items.Add(new Separator());

            MenuItem close = AddMenuItem("close", "Close", () => window.Close());
            close.FontWeight = FontWeights.Bold;
        }

        private MenuItem AddMenuItem(string resourceKey, string fallback, Action action)
        {
            string header = resourceKey == null ? fallback : TryFindResource(resourceKey) as string ?? fallback;
            var item = new MenuItem { Header = header };
            item.Click += (s, args) => action();
            ButtonMenu.Items.Add(item);
            return item;
        }

        private void ForEachWindow(Action<ApplicationWindow> action)
        {
            foreach (ApplicationWindow window in _item?.Windows.ToList() ?? new List<ApplicationWindow>())
            {
                action(window);
            }
        }

        private void ButtonMenu_OnOpened(object sender, RoutedEventArgs e)
        {
            Taskbar taskbar = Host?.Host;

            if (taskbar != null && !_menuHoldsTaskbar)
            {
                taskbar.AddOpenMenu();
                _menuHoldsTaskbar = true;
            }
        }

        private void ButtonMenu_OnClosed(object sender, RoutedEventArgs e)
        {
            if (_menuHoldsTaskbar)
            {
                _menuHoldsTaskbar = false;
                Host?.Host?.RemoveOpenMenu();
            }
        }

        #endregion
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Controls/SuperbarButton.xaml.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Controls/SuperbarWindowList.xaml.cs">
namespace RetroBar.Controls
{
    using ManagedShell.AppBar;
    using ManagedShell.Common.Helpers;
    using ManagedShell.Interop;
    using ManagedShell.WindowsTasks;
    using RetroBar.StartMenu;
    using RetroBar.Superbar;
    using RetroBar.Utilities;
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using System.Windows.Interop;
    using System.Windows.Media;
    using System.Windows.Threading;

    /// <summary>
    /// The windows of a taskbar button, shown above it: live thumbnails with Aero, as Windows 7 shows them,
    /// or a list of titles in the Basic look. It never takes focus from the window in front.
    /// </summary>
    internal partial class SuperbarWindowList : Window
    {
        // Largest thumbnail, in device-independent pixels, as in Windows 7
        private const double MaxThumbnailWidth = 192;
        private const double MaxThumbnailHeight = 120;
        private const double MinThumbnailWidth = 96;
        private const int Gap = 2;

        public static readonly DependencyProperty IsGlassProperty =
            DependencyProperty.Register(nameof(IsGlass), typeof(bool), typeof(SuperbarWindowList), new PropertyMetadata(true));

        public bool IsGlass
        {
            get => (bool)GetValue(IsGlassProperty);
            set => SetValue(IsGlassProperty, value);
        }

        public static readonly DependencyProperty GlassTintOpacityProperty =
            DependencyProperty.Register(nameof(GlassTintOpacity), typeof(double), typeof(SuperbarWindowList), new PropertyMetadata(1.0));

        /// <summary>Opacity of the frame's glass tint: AeroGlassHelper.TintOpacity while the glass is on, otherwise 1.</summary>
        public double GlassTintOpacity
        {
            get => (double)GetValue(GlassTintOpacityProperty);
            set => SetValue(GlassTintOpacityProperty, value);
        }

        public static readonly DependencyProperty ThumbnailWidthProperty =
            DependencyProperty.Register(nameof(ThumbnailWidth), typeof(double), typeof(SuperbarWindowList), new PropertyMetadata(MaxThumbnailWidth));

        public double ThumbnailWidth
        {
            get => (double)GetValue(ThumbnailWidthProperty);
            set => SetValue(ThumbnailWidthProperty, value);
        }

        public static readonly DependencyProperty ThumbnailHeightProperty =
            DependencyProperty.Register(nameof(ThumbnailHeight), typeof(double), typeof(SuperbarWindowList), new PropertyMetadata(MaxThumbnailHeight));

        public double ThumbnailHeight
        {
            get => (double)GetValue(ThumbnailHeightProperty);
            set => SetValue(ThumbnailHeightProperty, value);
        }

        /// <summary>Raised when a window has been chosen or closed from the list.</summary>
        public event EventHandler WindowChosen;

        public SuperbarItem Item { get; private set; }

        public bool IsOpen { get; private set; }

        public bool IsClosed { get; private set; }

        private IntPtr _handle;
        private Taskbar _host;
        private FrameworkElement _anchor;
        private bool _thumbnails;
        private readonly Dictionary<ApplicationWindow, IntPtr> _thumbs = new Dictionary<ApplicationWindow, IntPtr>();

        public SuperbarWindowList()
        {
            InitializeComponent();

            Left = -32000;
            Top = -32000;

            Closed += (sender, e) => _blurBackdrop?.Dispose();
        }

        private GlassBlurBackdrop _blurBackdrop;

        /// <summary>The adjustable blur beneath the list's glass (GlassBlurBackdrop), at the Aero glass blur setting.</summary>
        private void UpdateGlassBlur()
        {
            double blur = IsGlass ? AeroGlassHelper.BlurStrength : 0;

            if (blur > 0 && _blurBackdrop == null && _handle != IntPtr.Zero)
            {
                _blurBackdrop = new GlassBlurBackdrop(_handle);
            }

            _blurBackdrop?.SetStrength(blur);
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            _handle = new WindowInteropHelper(this).Handle;

            // Never take focus from the window in front, and stay out of Alt+Tab
            int exStyle = StartMenuNative.GetWindowLong(_handle, StartMenuNative.GWL_EXSTYLE);
            exStyle = (exStyle | StartMenuNative.WS_EX_TOOLWINDOW | StartMenuNative.WS_EX_NOACTIVATE) & ~StartMenuNative.WS_EX_APPWINDOW;
            StartMenuNative.SetWindowLong(_handle, StartMenuNative.GWL_EXSTYLE, exStyle);

            WindowHelper.ExcludeWindowFromPeek(_handle);
        }

        public void ShowFor(Taskbar host, FrameworkElement anchor, SuperbarItem item, bool thumbnails, bool glass)
        {
            if (Item != null && !ReferenceEquals(Item, item))
            {
                Item.Windows.CollectionChanged -= Windows_CollectionChanged;
            }

            if (!ReferenceEquals(Item, item))
            {
                item.Windows.CollectionChanged += Windows_CollectionChanged;
            }

            _host = host;
            _anchor = anchor;
            Item = item;
            IsGlass = glass;
            GlassTintOpacity = glass ? AeroGlassHelper.TintOpacity : 1.0;

            Refresh(thumbnails);
        }

        public void HideList()
        {
            if (Item != null)
            {
                Item.Windows.CollectionChanged -= Windows_CollectionChanged;
            }

            UnregisterThumbnails();
            Item = null;
            _anchor = null;
            IsOpen = false;
            WindowsList.ItemsSource = null;

            if (IsVisible)
            {
                Hide();
            }
        }

        private void Windows_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Wait until the button has updated, then show the windows as they are now
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (!IsOpen || Item == null)
                {
                    return;
                }

                if (Item.Windows.Count == 0 || _anchor == null || !_anchor.IsVisible)
                {
                    HideList();
                    WindowChosen?.Invoke(this, EventArgs.Empty);
                    return;
                }

                Refresh(_thumbnails);
            }));
        }

        private void Refresh(bool thumbnails)
        {
            UnregisterThumbnails();

            bool vertical = _host?.Orientation == Orientation.Vertical;
            _thumbnails = thumbnails && FitsThumbnails(vertical);

            WindowsList.ItemsPanel = (ItemsPanelTemplate)FindResource(_thumbnails && !vertical ? "HorizontalPanel" : "VerticalPanel");
            WindowsList.ItemTemplate = (DataTemplate)FindResource(_thumbnails ? "ThumbnailTemplate" : "ListTemplate");
            WindowsList.ItemsSource = Item.Windows;

            if (!IsVisible)
            {
                Opacity = 0;
                Show();
            }

            UpdateLayout();
            Position();
            UpdateWindowRegion();

            if (IsGlass)
            {
                AeroGlassHelper.PrepareGlassWindow(_handle);
            }

            UpdateGlassBlur();

            if (_thumbnails)
            {
                RegisterThumbnails();
            }

            Opacity = 1;
            IsOpen = true;
        }

        /// <summary>
        /// Thumbnails shrink to fit the screen; when even the smallest do not fit, Windows 7 shows a list instead.
        /// </summary>
        private bool FitsThumbnails(bool vertical)
        {
            int count = Item?.Windows.Count ?? 0;

            if (count == 0 || _host == null)
            {
                return false;
            }

            double scale = _host.DpiScale > 0 ? _host.DpiScale : 1;
            var screen = _host.Screen.Bounds;
            double available = (vertical ? screen.Bottom - screen.Top : screen.Right - screen.Left) / scale - 40;

            // Each tile adds padding, border and margin around its thumbnail
            double tileExtra = vertical ? 46 : 22;
            double width = MaxThumbnailWidth;
            double height = MaxThumbnailHeight;
            double needed = count * ((vertical ? height : width) + tileExtra);

            if (needed > available)
            {
                double fit = available / count - tileExtra;
                double factor = fit / (vertical ? height : width);
                width *= factor;
                height *= factor;
            }

            if (width < MinThumbnailWidth)
            {
                return false;
            }

            ThumbnailWidth = Math.Floor(width);
            ThumbnailHeight = Math.Floor(height);
            return true;
        }

        private void Position()
        {
            if (_host == null || _anchor == null || _handle == IntPtr.Zero || !_anchor.IsVisible)
            {
                return;
            }

            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int width = (int)Math.Round(ActualWidth * scale);
            int height = (int)Math.Round(ActualHeight * scale);

            Point anchorTopLeft = _anchor.PointToScreen(new Point(0, 0));
            Point anchorBottomRight = _anchor.PointToScreen(new Point(_anchor.ActualWidth, _anchor.ActualHeight));
            double anchorCenterX = (anchorTopLeft.X + anchorBottomRight.X) / 2;
            double anchorCenterY = (anchorTopLeft.Y + anchorBottomRight.Y) / 2;

            var bar = _host.WindowRect;
            var screen = _host.Screen.Bounds;
            int x;
            int y;

            switch (_host.AppBarEdge)
            {
                case AppBarEdge.Top:
                    x = (int)Math.Round(anchorCenterX - width / 2.0);
                    y = bar.Bottom + Gap;
                    break;
                case AppBarEdge.Left:
                    x = bar.Right + Gap;
                    y = (int)Math.Round(anchorCenterY - height / 2.0);
                    break;
                case AppBarEdge.Right:
                    x = bar.Left - width - Gap;
                    y = (int)Math.Round(anchorCenterY - height / 2.0);
                    break;
                default:
                    x = (int)Math.Round(anchorCenterX - width / 2.0);
                    y = bar.Top - height - Gap;
                    break;
            }

            x = Math.Max(screen.Left, Math.Min(x, screen.Right - width));
            y = Math.Max(screen.Top, Math.Min(y, screen.Bottom - height));

            StartMenuNative.SetWindowPos(_handle, StartMenuNative.HWND_TOPMOST, x, y, 0, 0, StartMenuNative.SWP_NOSIZE | StartMenuNative.SWP_NOACTIVATE);
        }

        /// <summary>Clips the window to the frame's rounded corners, so the see-through glass stays inside the frame.</summary>
        private void UpdateWindowRegion()
        {
            if (_handle == IntPtr.Zero || ActualWidth <= 0 || ActualHeight <= 0)
            {
                return;
            }

            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int right = (int)Math.Round(ActualWidth * scale);
            int bottom = (int)Math.Round(ActualHeight * scale);
            int diameter = (int)Math.Round(12 * scale);
            IntPtr region = StartMenuNative.CreateRoundRectRgn(0, 0, right + 1, bottom + 1, diameter, diameter);

            if (region != IntPtr.Zero && StartMenuNative.SetWindowRgn(_handle, region, true) == 0)
            {
                StartMenuNative.DeleteObject(region);
            }
        }

        #region Thumbnails

        private void RegisterThumbnails()
        {
            if (_handle == IntPtr.Zero || Item == null || !NativeMethods.DwmIsCompositionEnabled())
            {
                return;
            }

            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;

            for (int i = 0; i < WindowsList.Items.Count; i++)
            {
                if (WindowsList.Items[i] is not ApplicationWindow window ||
                    WindowsList.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter presenter ||
                    presenter.ContentTemplate?.FindName("ThumbnailHost", presenter) is not FrameworkElement host)
                {
                    continue;
                }

                if (window.Handle == IntPtr.Zero || NativeMethods.DwmRegisterThumbnail(_handle, window.Handle, out IntPtr thumb) != 0)
                {
                    continue;
                }

                _thumbs[window] = thumb;

                var clientAreaProps = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
                {
                    dwFlags = NativeMethods.DWM_TNP_SOURCECLIENTAREAONLY,
                    fSourceClientAreaOnly = true
                };
                NativeMethods.DwmUpdateThumbnailProperties(thumb, ref clientAreaProps);

                NativeMethods.DwmQueryThumbnailSourceSize(thumb, out NativeMethods.PSIZE size);

                // Fit the window into the thumbnail area, keeping its shape and never enlarging it
                double areaWidth = host.ActualWidth * scale;
                double areaHeight = host.ActualHeight * scale;
                double factor = size.x > 0 && size.y > 0 ? Math.Min(1, Math.Min(areaWidth / size.x, areaHeight / size.y)) : 1;
                double thumbWidth = Math.Max(1, size.x * factor);
                double thumbHeight = Math.Max(1, size.y * factor);

                Point origin = host.TransformToAncestor(this).Transform(new Point(0, 0));
                int left = (int)Math.Round(origin.X * scale + (areaWidth - thumbWidth) / 2);
                int top = (int)Math.Round(origin.Y * scale + (areaHeight - thumbHeight) / 2);

                var props = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
                {
                    fVisible = true,
                    dwFlags = NativeMethods.DWM_TNP_VISIBLE | NativeMethods.DWM_TNP_RECTDESTINATION,
                    rcDestination = new NativeMethods.Rect(left, top, left + (int)Math.Round(thumbWidth), top + (int)Math.Round(thumbHeight))
                };
                NativeMethods.DwmUpdateThumbnailProperties(thumb, ref props);
            }
        }

        private void UnregisterThumbnails()
        {
            foreach (IntPtr thumb in _thumbs.Values)
            {
                NativeMethods.DwmUnregisterThumbnail(thumb);
            }

            _thumbs.Clear();
        }

        #endregion

        #region Choosing and closing windows

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ApplicationWindow window)
            {
                e.Handled = true;
                HideList();
                window.BringToFront();
                WindowChosen?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // A middle click closes the window, as in Windows 7
            if (e.ChangedButton == MouseButton.Middle && (sender as FrameworkElement)?.DataContext is ApplicationWindow window)
            {
                e.Handled = true;
                window.Close();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ApplicationWindow window)
            {
                window.Close();
            }
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            IsClosed = true;
            IsOpen = false;
            UnregisterThumbnails();

            if (Item != null)
            {
                Item.Windows.CollectionChanged -= Windows_CollectionChanged;
            }

            base.OnClosed(e);
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Controls/SuperbarWindowList.xaml.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Controls/Toolbar.xaml.cs">
namespace RetroBar.Controls
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Linq;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Data;
    using System.Windows.Input;
    using ManagedShell.Common.Helpers;
    using ManagedShell.Common.Logging;
    using ManagedShell.ShellFolders;
    using ManagedShell.ShellFolders.Enums;
    using RetroBar.Utilities;

    /// <summary>
    /// Interaction logic for Toolbar.xaml
    /// </summary>
    public partial class Toolbar : UserControl
    {
        private bool _ignoreNextUpdate;
        private bool _isLoaded;

        private enum MenuItem : uint
        {
            OpenParentFolder = CommonContextMenuItem.Paste + 1
        }

        public static DependencyProperty PathProperty = DependencyProperty.Register(nameof(Path), typeof(string), typeof(Toolbar), new PropertyMetadata(OnPathChanged));

        public string Path
        {
            get => (string)GetValue(PathProperty);
            set
            {
                SetValue(PathProperty, value);
                SetupFolder(value);
            }
        }

        private static DependencyProperty FolderProperty = DependencyProperty.Register(nameof(Folder), typeof(ShellFolder), typeof(Toolbar));

        public static DependencyProperty HostProperty = DependencyProperty.Register(nameof(Host), typeof(Taskbar), typeof(Toolbar), new PropertyMetadata(HostChangedCallback));

        public Taskbar Host
        {
            get { return (Taskbar)GetValue(HostProperty); }
            set { SetValue(HostProperty, value); }
        }

        public ToolbarDropHandler DropHandler { get; set; }

        private ShellFolder Folder
        {
            get => (ShellFolder)GetValue(FolderProperty);
            set
            {
                SetValue(FolderProperty, value);
                SetItemsSource();
            }
        }

        public Toolbar()
        {
            DropHandler = new ToolbarDropHandler(this);

            InitializeComponent();
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.QuickLaunchOrder))
            {
                if (_ignoreNextUpdate)
                {
                    _ignoreNextUpdate = false;
                    return;
                }

                Refresh();
            }
            else if (e.PropertyName == nameof(Settings.TaskbarScale))
            {
                Refresh();
            }
        }

        private void Refresh()
        {
            if (Folder == null)
            {
                return;
            }

            ListCollectionView cvs = (ListCollectionView)CollectionViewSource.GetDefaultView(Folder.Files);
            cvs.Refresh();
        }

        private void SetupFolder(string path)
        {
            Folder?.Dispose();
            Folder = new ShellFolder(Environment.ExpandEnvironmentVariables(path), IntPtr.Zero, true);
        }

        private void UnloadFolder()
        {
            Folder?.Dispose();
            Folder = null;
        }

        private void SetItemsSource()
        {
            if (Folder != null)
            {
                ToolbarItems.ItemsSource = Folder.Files;
                ListCollectionView cvs = (ListCollectionView)CollectionViewSource.GetDefaultView(Folder.Files);
                cvs.CustomSort = new ToolbarSorter(this);
            }
        }

        public void SaveItemOrder()
        {
            List<string> itemPaths = new List<string>();

            foreach (ShellFile file in ((ListCollectionView)CollectionViewSource.GetDefaultView(Folder.Files)).OfType<ShellFile>())
            {
                itemPaths.Add(file.Path);
            }

            // small optimization, only other toolbars with this folder need to reload when the setting is saved.
            _ignoreNextUpdate = true;

            Settings.Instance.QuickLaunchOrder = itemPaths;
        }

        public void AddToSource(StringCollection filesToAdd)
        {
            string sourcePath = Environment.ExpandEnvironmentVariables(Path);

            foreach (string itemPath in filesToAdd)
            {
                // Create shortcut to each dragged file
                try
                {
                    string destinationFileName = System.IO.Path.GetFileNameWithoutExtension(itemPath);
                    string destinationPath = System.IO.Path.Combine(sourcePath, destinationFileName + ".lnk");
                    int dupCount = 0;

                    while (ShellHelper.Exists(destinationPath))
                    {
                        dupCount++;

                        destinationPath = System.IO.Path.Combine(sourcePath, $"{destinationFileName} ({dupCount}).lnk");
                    }

                    ShellLinkHelper.CreateAndSave(itemPath, destinationPath);
                }
                catch (Exception e)
                {
                    ShellLogger.Error($"Toolbar: Unable to save shortcut to {itemPath}", e);
                }
            }
        }

        #region Events
        private static void OnPathChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is Toolbar toolbar)
            {
                toolbar.SetupFolder((string)e.NewValue);
            }
        }

        private void ToolbarIcon_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ToolbarButton icon = sender as ToolbarButton;
            if (icon == null)
            {
                return;
            }

            Mouse.Capture(null);
            ShellFile file = icon.DataContext as ShellFile;

            if (file == null || string.IsNullOrWhiteSpace(file.Path))
            {
                return;
            }

            if (InvokeContextMenu(file, false))
            {
                e.Handled = true;
            }
        }

        private void ToolbarIcon_OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            ToolbarButton icon = sender as ToolbarButton;
            if (icon == null)
            {
                return;
            }
            
            ShellFile file = icon.DataContext as ShellFile;

            if (InvokeContextMenu(file, true))
            {
                e.Handled = true;
            }
        }

        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is bool visible)
            {
                if (visible)
                {
                    if (Folder != null)
                    {
                        return;
                    }

                    SetupFolder(Path);
                }
                else
                {
                    UnloadFolder();
                }
            }
        }

        private void Toolbar_TaskbarHotkeyPressed(object sender, HotkeyManager.TaskbarHotkeyEventArgs e)
        {
            if (Settings.Instance.WinNumHotkeysAction == WinNumHotkeysOption.InvokeQuickLaunch && Host.Screen.Primary)
            {
                try
                {
                    ListCollectionView items = (ListCollectionView)CollectionViewSource.GetDefaultView(Folder.Files);

                    bool exists = items.MoveCurrentToPosition(e.index);
                    
                    if (exists) InvokeContextMenu((ShellFile)items.CurrentItem, false);

                }
                catch (ArgumentOutOfRangeException) { }
            }
        }
        #endregion

        #region Context menu
        private ShellMenuCommandBuilder GetFileCommandBuilder(ShellFile file)
        {
            if (file == null)
            {
                return new ShellMenuCommandBuilder();
            }

            ShellMenuCommandBuilder builder = new ShellMenuCommandBuilder();

            builder.AddSeparator();
            builder.AddCommand(new ShellMenuCommand
            {
                Flags = MFT.BYCOMMAND,
                Label = (string)FindResource("open_folder"),
                UID = (uint)MenuItem.OpenParentFolder
            });

            return builder;
        }

        private bool InvokeContextMenu(ShellFile file, bool isInteractive)
        {
            if (file == null)
            {
                return false;
            }
            
            var _ = new ShellItemContextMenu(new ShellItem[] { file }, Folder, IntPtr.Zero, HandleFileAction, isInteractive, false, new ShellMenuCommandBuilder(), GetFileCommandBuilder(file));
            return true;
        }

        private bool HandleFileAction(string action, ShellItem[] items, bool allFolders)
        {
            if (action == ((uint)MenuItem.OpenParentFolder).ToString())
            {
                ShellHelper.StartProcess(Folder.Path);
                return true;
            }

            return false;
        }
        #endregion

        private void Initialize()
        {
            if (!_isLoaded && Host != null)
            {
                Settings.Instance.PropertyChanged += Settings_PropertyChanged;
                Host.hotkeyManager.TaskbarHotkeyPressed += Toolbar_TaskbarHotkeyPressed;

                _isLoaded = true;
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            Initialize();
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
            if (Host != null)
            {
                Host.hotkeyManager.TaskbarHotkeyPressed -= Toolbar_TaskbarHotkeyPressed;
            }

            _isLoaded = false;
        }

        private static void HostChangedCallback(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is Toolbar toolbar && e.OldValue == null && e.NewValue != null)
            {
                toolbar.Initialize();
            }
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Controls/Toolbar.xaml.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Controls/ToolbarButton.xaml.cs">
namespace RetroBar.Controls
{
    using RetroBar.StartMenu;
    using RetroBar.Utilities;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Data;
    using System.Windows.Media;

    /// <summary>
    /// Interaction logic for ToolbarButton.xaml
    /// </summary>
    public partial class ToolbarButton : UserControl
    {
        public ToolbarButton()
        {
            InitializeComponent();

            setIconBinding();
        }

        private void setIconBinding()
        {
            string bindingPath = "SmallIcon";
            bool useLargeIcons = Settings.Instance.TaskbarScale > 1 || (Application.Current.FindResource("UseLargeIcons") as bool? ?? false);

            if (useLargeIcons)
            {
                bindingPath = "LargeIcon";
            }

            // Programs and shortcuts take their icon straight from the file, so a blank placeholder left in Windows'
            // icon cache never shows; anything else, or a file without an icon, keeps the shell's icon
            var iconBinding = new MultiBinding
            {
                Mode = BindingMode.OneWay,
                Converter = new ProgramIconConverter(useLargeIcons ? ShellIconSize.Large : ShellIconSize.Small)
            };
            iconBinding.Bindings.Add(new Binding(bindingPath) { Mode = BindingMode.OneWay });
            iconBinding.Bindings.Add(new Binding("Path") { Mode = BindingMode.OneWay });
            ToolbarIcon.SetBinding(Image.SourceProperty, iconBinding);
        }

        private sealed class ProgramIconConverter : IMultiValueConverter
        {
            private static readonly Dictionary<string, ImageSource> _cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
            private readonly ShellIconSize _size;

            public ProgramIconConverter(ShellIconSize size)
            {
                _size = size;
            }

            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                ImageSource shellIcon = values.Length > 0 ? values[0] as ImageSource : null;
                string path = values.Length > 1 ? values[1] as string : null;

                if (string.IsNullOrEmpty(path))
                {
                    return shellIcon;
                }

                // A shortcut changed since is read again
                string key = _size + "|" + path + "|" + System.IO.File.GetLastWriteTimeUtc(path).Ticks;
                if (!_cache.TryGetValue(key, out ImageSource icon))
                {
                    icon = ShellIcons.FromProgramFile(path, _size);
                    _cache[key] = icon;
                }

                return icon ?? shellIcon;
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Controls/ToolbarButton.xaml.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Controls/ShowDesktopButton.xaml.cs">
namespace RetroBar.Controls
{
    using ManagedShell.Common.Helpers;
    using ManagedShell.Interop;
    using ManagedShell.WindowsTasks;
    using RetroBar.Utilities;
    using System;
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Controls;

    /// <summary>
    /// Interaction logic for ShowDesktopButton.xaml
    /// </summary>
    public partial class ShowDesktopButton : UserControl
    {
        private const int TOGGLE_DESKTOP = 407;
        private IntPtr taskbarHandle = Process.GetCurrentProcess().MainWindowHandle;
        private bool isWindows81OrBetter = EnvironmentHelper.IsWindows81OrBetter;
        private bool isLoaded;
        private DelayedActivationHandler dragHandler;

        public static DependencyProperty TasksServiceProperty = DependencyProperty.Register(nameof(TasksService), typeof(TasksService), typeof(ShowDesktopButton), new PropertyMetadata(TasksChangedCallback));

        public TasksService TasksService
        {
            get { return (TasksService)GetValue(TasksServiceProperty); }
            set { SetValue(TasksServiceProperty, value); }
        }

        public ShowDesktopButton()
        {
            InitializeComponent();
        }

        private static void TasksChangedCallback(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is ShowDesktopButton button && e.OldValue == null && e.NewValue != null)
            {
                button.SetupButton();
            }
        }

        private void SetIconSize()
        {
            if (DpiHelper.DpiScale > 1 || Settings.Instance.TaskbarScale > 1)
            {
                ShowDesktopIcon.Source = (System.Windows.Media.ImageSource)FindResource("ShowDesktopIconImageLarge");
            }
            else
            {
                ShowDesktopIcon.Source = (System.Windows.Media.ImageSource)FindResource("ShowDesktopIconImageSmall");
            }
        }

        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            PeekAtDesktopItem.IsEnabled = true;
            if (!NativeMethods.DwmIsCompositionEnabled())
            {
                PeekAtDesktopItem.IsEnabled = false;
            }
        }

        private void ToggleDesktop()
        {
            NativeMethods.SendMessage(WindowHelper.FindWindowsTray(IntPtr.Zero),
                (int)NativeMethods.WM.COMMAND, (IntPtr)TOGGLE_DESKTOP, IntPtr.Zero);
        }

        private void PeekAtDesktop(uint shouldPeek)
        {
            if (Settings.Instance.PeekAtDesktop && NativeMethods.DwmIsCompositionEnabled())
            {
                if (isWindows81OrBetter)
                {
                    NativeMethods.DwmActivateLivePreview(shouldPeek, taskbarHandle,
                        IntPtr.Zero, NativeMethods.AeroPeekType.Desktop, IntPtr.Zero);
                }
                else
                {
                    NativeMethods.DwmActivateLivePreview(shouldPeek, taskbarHandle,
                        IntPtr.Zero, NativeMethods.AeroPeekType.Desktop);
                }
            }
        }

        private void ShowDesktop_OnMouseEnter(object sender, RoutedEventArgs e)
        {
            PeekAtDesktop(1);
        }

        private void ShowDesktop_OnMouseLeave(object sender, RoutedEventArgs e)
        {
            PeekAtDesktop(0);
        }

        private void ShowDesktop_OnClick(object sender, RoutedEventArgs e)
        {
            // If the user activates a window other than the desktop, HandleWindowActivated will deselect the button.
            ToggleDesktop();
        }

        private void OpenDisplayPropertiesCpl()
        {
            ShellHelper.StartProcess("desk.cpl");
        }

        private void PropertiesItem_OnClick(object sender, RoutedEventArgs e)
        {
            OpenDisplayPropertiesCpl();
        }

        private void HandleWindowActivated(object sender, WindowEventArgs e)
        {
            if (ShowDesktop.IsChecked == true)
            {
                ShowDesktop.IsChecked = false;
            }
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.TaskbarScale))
            {
                SetIconSize();
            }
        }

        private void SetupButton()
        {
            if (!isLoaded && TasksService != null)
            {
                SetIconSize();
                TasksService.WindowActivated += HandleWindowActivated;

                Settings.Instance.PropertyChanged += Settings_PropertyChanged;

                dragHandler = new DelayedActivationHandler(() =>
                {
                    if (ShowDesktop.IsChecked == false)
                    {
                        ToggleDesktop();
                        ShowDesktop.IsChecked = true;
                    }
                });

                isLoaded = true;
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            SetupButton();
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (isLoaded && TasksService != null)
            {
                TasksService.WindowActivated -= HandleWindowActivated;
                Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
                dragHandler?.Dispose();
                isLoaded = false;
            }
        }

        private void ShowDesktop_DragEnter(object sender, DragEventArgs e)
        {
            dragHandler?.OnDragEnter(e);
        }

        private void ShowDesktop_DragLeave(object sender, DragEventArgs e)
        {
            dragHandler?.OnDragLeave();
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Controls/ShowDesktopButton.xaml.cs">

// [5300] NOTIFICATION AREA + CLOCK + INPUT LANGUAGE :: tray icons, balloons, clock, keyboard language

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Controls/NotifyIconList.xaml.cs">
namespace RetroBar.Controls
{
    using GongSolutions.Wpf.DragDrop;
    using ManagedShell.WindowsTray;
    using RetroBar.Extensions;
    using RetroBar.Utilities;
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Linq;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Data;
    using System.Windows.Threading;
    using Tray = ManagedShell.WindowsTray;

    /// <summary>
    /// Interaction logic for NotifyIconList.xaml
    /// </summary>
    public partial class NotifyIconList : UserControl
    {
        private bool _isLoaded;
        private ObservableCollection<Tray.NotifyIcon> promotedIcons = new ObservableCollection<Tray.NotifyIcon>();
        private NotifyIconDropHandler dropHandler;
        private ListCollectionView collectionView;
        private ListCollectionView overflowView;

        public static DependencyProperty NotificationAreaProperty = DependencyProperty.Register(nameof(NotificationArea), typeof(NotificationArea), typeof(NotifyIconList), new PropertyMetadata(NotificationAreaChangedCallback));

        public NotificationArea NotificationArea
        {
            get { return (NotificationArea)GetValue(NotificationAreaProperty); }
            set { SetValue(NotificationAreaProperty, value); }
        }

        public NotifyIconList()
        {
            InitializeComponent();
        }

        private bool IsCollapsed()
        {
            // With the overflow flyout the hidden icons are never expanded into the bar
            return Settings.Instance.CollapseNotifyIcons && (UsesOverflow() || NotifyIconToggleButton.IsChecked != true);
        }

        /// <summary>
        /// Whether the theme shows hidden icons in a flyout (UseNotifyIconOverflow, Windows 7) rather than in the bar.
        /// </summary>
        private static bool UsesOverflow()
        {
            return Application.Current.TryFindResource("UseNotifyIconOverflow") as bool? ?? false;
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.CollapseNotifyIcons))
            {
                if (Settings.Instance.CollapseNotifyIcons)
                {
                    SetToggleVisibility();
                }
                else
                {
                    NotifyIconToggleButton.IsChecked = false;
                    NotifyIconToggleButton.Visibility = Visibility.Collapsed;
                }
                collectionView?.Refresh();
            }
            else if (e.PropertyName == nameof(Settings.InvertIconsMode) || e.PropertyName == nameof(Settings.InvertNotifyIcons) || e.PropertyName == nameof(Settings.NotifyIconOrder))
            {
                // Reload icons
                collectionView?.Refresh();
                overflowView?.Refresh();
            }
            else if (e.PropertyName == nameof(Settings.Theme) && UsesOverflow() && NotifyIconToggleButton.IsChecked == true)
            {
                // Icons expanded into the bar by another theme collapse again under a theme with the overflow flyout
                NotifyIconToggleButton.IsChecked = false;
                collectionView?.Refresh();
            }
        }

        private void SetNotificationAreaCollections()
        {
            if (!_isLoaded && NotificationArea != null)
            {
                NotificationArea.NotificationBalloonShown += NotificationArea_NotificationBalloonShown;
                NotificationArea.UnpinnedIcons.CollectionChanged += UnpinnedIcons_CollectionChanged;
                NotificationArea.UnpinnedIcons.Filter = UnpinnedNotifyIcons_Filter;
                Settings.Instance.PropertyChanged += Settings_PropertyChanged;

                collectionView = new ListCollectionView(NotificationArea.TrayIcons);
                collectionView.CustomSort = new NotifyIconComparer(this);
                collectionView.Filter = NotifyIcons_Filter;
                var collectionViewShaping = collectionView as ICollectionViewLiveShaping;
                collectionViewShaping.IsLiveFiltering = true;
                collectionViewShaping.LiveFilteringProperties.Add("IsHidden");
                collectionViewShaping.LiveFilteringProperties.Add("IsPinned");
                NotifyIcons.ItemsSource = collectionView;

                if (Settings.Instance.CollapseNotifyIcons)
                {
                    SetToggleVisibility();
                }

                _isLoaded = true;
            }
        }

        private static void NotificationAreaChangedCallback(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is NotifyIconList notifyIconList && e.OldValue == null && e.NewValue != null)
            {
                notifyIconList.SetNotificationAreaCollections();
            }
        }

        private bool NotifyIcons_Filter(object icon)
        {
            if (icon is Tray.NotifyIcon notifyIcon)
            {
                return (!IsCollapsed() || notifyIcon.IsPinned)
                    && !notifyIcon.IsHidden
                    && notifyIcon.GetBehavior() != NotifyIconBehavior.Remove;
            }
            return false;
        }

        private bool OverflowIcons_Filter(object icon)
        {
            // The overflow flyout shows exactly the icons the collapsed bar leaves out
            if (icon is Tray.NotifyIcon notifyIcon)
            {
                return !notifyIcon.IsPinned
                    && !notifyIcon.IsHidden
                    && notifyIcon.GetBehavior() != NotifyIconBehavior.Remove;
            }
            return false;
        }

        private bool UnpinnedNotifyIcons_Filter(object obj)
        {
            // This filter is used when we check if the toggle should hide
            if (obj is Tray.NotifyIcon notifyIcon)
            {
                return !notifyIcon.IsPinned && !notifyIcon.IsHidden && notifyIcon.GetBehavior() != NotifyIconBehavior.Remove;
            }

            return true;
        }

        private void NotificationArea_NotificationBalloonShown(object sender, NotificationBalloonEventArgs e)
        {
            // This is used to promote unpinned icons to show when the tray is collapsed.

            if (NotificationArea == null)
            {
                return;
            }

            Tray.NotifyIcon notifyIcon = e.Balloon.NotifyIcon;

            if (NotificationArea.PinnedIcons.Contains(notifyIcon))
            {
                // Do not promote pinned icons (they're already there!)
                return;
            }

            if (notifyIcon.GetBehavior() != NotifyIconBehavior.HideWhenInactive)
            {
                // Do not promote icons that are always hidden
                return;
            }

            if (promotedIcons.Contains(notifyIcon))
            {
                // Do not duplicate promoted icons
                return;
            }

            notifyIcon.IsPinned = true;
            promotedIcons.Add(notifyIcon);

            DispatcherTimer unpromoteTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(e.Balloon.Timeout + 500) // Keep it around for a few ms for the animation to complete
            };
            unpromoteTimer.Tick += (object sender, EventArgs e) =>
            {
                if (promotedIcons.Contains(notifyIcon))
                {
                    if (!(Settings.Instance.NotifyIconBehaviors.Find(setting => setting.Identifier == notifyIcon.Identifier) is NotifyIconBehaviorSetting iconSetting && iconSetting.Behavior == NotifyIconBehavior.AlwaysShow))
                    {
                        // Don't unpin if settings were changed to always show
                        notifyIcon.IsPinned = false;
                    }
                    promotedIcons.Remove(notifyIcon);
                }
                unpromoteTimer.Stop();
            };
            unpromoteTimer.Start();
        }

        private void NotifyIconList_Loaded(object sender, RoutedEventArgs e)
        {
            SetNotificationAreaCollections();

            // Set up drag/drop handler
            if (dropHandler == null)
            {
                dropHandler = new NotifyIconDropHandler(this);
                GongSolutions.Wpf.DragDrop.DragDrop.SetDropHandler(NotifyIcons, dropHandler);
            }
        }

        private void NotifyIconList_OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded)
            {
                return;
            }

            if (NotificationArea != null)
            {
                NotificationArea.NotificationBalloonShown -= NotificationArea_NotificationBalloonShown;
                NotificationArea.UnpinnedIcons.CollectionChanged -= UnpinnedIcons_CollectionChanged;
                NotificationArea.UnpinnedIcons.Filter = UnpinnedNotifyIcons_Filter;
                Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
            }

            _isLoaded = false;
        }

        private void UnpinnedIcons_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            SetToggleVisibility();
        }

        private void NotifyIconToggleButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (UsesOverflow())
            {
                if (NotifyIconToggleButton.IsChecked == true)
                {
                    OpenOverflow();
                }
                return;
            }

            collectionView?.Refresh();
        }

        private void NotifyIconToggleButton_OnUnchecked(object sender, RoutedEventArgs e)
        {
            OverflowPopup.IsOpen = false;
        }

        #region Overflow flyout

        private void OpenOverflow()
        {
            if (NotificationArea == null || OverflowPopup.IsOpen)
            {
                return;
            }

            if (overflowView == null)
            {
                overflowView = new ListCollectionView(NotificationArea.TrayIcons);
                overflowView.CustomSort = new NotifyIconComparer(this);
                overflowView.Filter = OverflowIcons_Filter;
                var overflowViewShaping = overflowView as ICollectionViewLiveShaping;
                overflowViewShaping.IsLiveFiltering = true;
                overflowViewShaping.LiveFilteringProperties.Add("IsHidden");
                overflowViewShaping.LiveFilteringProperties.Add("IsPinned");
                OverflowIcons.ItemsSource = overflowView;
            }
            else
            {
                overflowView.Refresh();
            }

            Taskbar host = Window.GetWindow(this) as Taskbar;
            OverflowFrame.DataContext = host;
            OverflowIcons.Tag = host;

            OverflowPopup.IsOpen = true;
        }

        private void OverflowPopup_Opened(object sender, EventArgs e)
        {
            // A click on the button while the flyout is open only closes it, as in Windows 7
            NotifyIconToggleButton.IsHitTestVisible = false;

            PositionOverflow();

            if (AeroGlassHelper.IsSeeThrough && PresentationSource.FromVisual(OverflowFrame) is System.Windows.Interop.HwndSource source)
            {
                AeroGlassHelper.PrepareGlassWindow(source.Handle);
            }
        }

        private void OverflowPopup_Closed(object sender, EventArgs e)
        {
            NotifyIconToggleButton.IsChecked = false;

            // Re-enabled after the click that closed the flyout has been handled
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                NotifyIconToggleButton.IsHitTestVisible = true;
            }));
        }

        private void OverflowFrame_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (OverflowPopup.IsOpen)
            {
                PositionOverflow();
            }
        }

        /// <summary>
        /// Places the flyout centred on the button, NotifyIconOverflowGap away from the taskbar's inner edge, on the side
        /// of the bar facing the desktop. Distances are taken in the button's own coordinates, as the popup's are.
        /// </summary>
        private void PositionOverflow()
        {
            if (!(Window.GetWindow(this) is Taskbar host))
            {
                return;
            }

            FrameworkElement target = NotifyIconToggleButton;
            double gap = Application.Current.TryFindResource("NotifyIconOverflowGap") as double? ?? 0;
            Point barStart = host.TranslatePoint(new Point(0, 0), target);
            Point barEnd = host.TranslatePoint(new Point(host.ActualWidth, host.ActualHeight), target);
            double centreX = (target.ActualWidth - OverflowFrame.ActualWidth) / 2;
            double centreY = (target.ActualHeight - OverflowFrame.ActualHeight) / 2;

            switch (host.AppBarEdge)
            {
                case ManagedShell.AppBar.AppBarEdge.Top:
                    OverflowPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                    OverflowPopup.HorizontalOffset = centreX;
                    OverflowPopup.VerticalOffset = barEnd.Y - target.ActualHeight + gap;
                    break;
                case ManagedShell.AppBar.AppBarEdge.Left:
                    OverflowPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
                    OverflowPopup.HorizontalOffset = barEnd.X - target.ActualWidth + gap;
                    OverflowPopup.VerticalOffset = centreY;
                    break;
                case ManagedShell.AppBar.AppBarEdge.Right:
                    OverflowPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Left;
                    OverflowPopup.HorizontalOffset = barStart.X - gap;
                    OverflowPopup.VerticalOffset = centreY;
                    break;
                default:
                    OverflowPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                    OverflowPopup.HorizontalOffset = centreX;
                    OverflowPopup.VerticalOffset = barStart.Y - gap;
                    break;
            }
        }

        private void OverflowCustomizeButton_OnClick(object sender, RoutedEventArgs e)
        {
            OverflowPopup.IsOpen = false;
            (Window.GetWindow(this) as Taskbar)?.OpenCustomizeNotifications();
        }

        #endregion

        private void SetToggleVisibility()
        {
            if (!Settings.Instance.CollapseNotifyIcons) return;

            if (NotificationArea.UnpinnedIcons.IsEmpty)
            {
                NotifyIconToggleButton.Visibility = Visibility.Collapsed;

                if (NotifyIconToggleButton.IsChecked == true)
                {
                    NotifyIconToggleButton.IsChecked = false;
                }
            }
            else
            {
                NotifyIconToggleButton.Visibility = Visibility.Visible;
            }
        }

        public void UpdateIconOrder(IDropInfo dropInfo)
        {
            if (NotificationArea == null || collectionView == null) return;

            var visibleIcons = collectionView.Cast<Tray.NotifyIcon>().ToList();

            if (IsCollapsed())
            {
                // Do not save temporary promoted icons
                visibleIcons = visibleIcons.Where(i => !promotedIcons.Contains(i)).ToList();
            }

            // Update the dragged icon's position in the list
            if (dropInfo.Data is Tray.NotifyIcon draggedIcon)
            {
                int insertIndex = dropInfo.InsertIndex;
                if (insertIndex > 0 && visibleIcons.IndexOf(draggedIcon) < insertIndex && visibleIcons.Remove(draggedIcon))
                {
                    insertIndex--;
                }
                else
                {
                    visibleIcons.Remove(draggedIcon);
                }
                visibleIcons.Insert(insertIndex, draggedIcon);
            }
            else
            {
                return;
            }
            
            // Never overwrite the list to prevent clearing out settings for non-visible icons
            var oldOrder = Settings.Instance.NotifyIconOrder ?? new List<string>();
            var result = new List<string>();
            int replaceIndex = 0;
            
            foreach (var id in oldOrder)
            {
                if (visibleIcons.Find(i => i.IsEqualByIdentifier(id)) != null)
                {
                    if (replaceIndex < visibleIcons.Count)
                    {
                        result.Add(visibleIcons[replaceIndex++].Identifier);
                    }
                }
                else
                {
                    result.Add(id);
                }
            }

            while (replaceIndex < visibleIcons.Count)
            {
                result.Add(visibleIcons[replaceIndex++].Identifier);
            }

            Settings.Instance.NotifyIconOrder = result;
        }

        public class NotifyIconComparer : System.Collections.IComparer
        {
            private NotifyIconList _host;

            public NotifyIconComparer(NotifyIconList host)
            {
                _host = host;
            }

            public int Compare(object x, object y)
            {
                if (x is Tray.NotifyIcon xIcon && y is Tray.NotifyIcon yIcon && Settings.Instance.NotifyIconOrder is List<string> setting)
                {
                    if (_host.IsCollapsed())
                    {
                        bool xPromoted = _host.promotedIcons.Contains(xIcon);
                        bool yPromoted = _host.promotedIcons.Contains(yIcon);
                        if (xPromoted && !yPromoted)
                        {
                            return -1;
                        }
                        if (!xPromoted && yPromoted)
                        {
                            return 1;
                        }
                    }
                    int xIndex = setting.FindIndex(s => xIcon.IsEqualByIdentifier(s));
                    int yIndex = setting.FindIndex(s => yIcon.IsEqualByIdentifier(s));
                    return xIndex.CompareTo(yIndex);
                }
                return 0;
            }
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Controls/NotifyIconList.xaml.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Controls/NotifyIcon.xaml.cs">
namespace RetroBar.Controls
{
    using System;
    using System.Linq;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using ManagedShell.Common.Helpers;
    using ManagedShell.Interop;
    using ManagedShell.WindowsTray;
    using RetroBar.Extensions;
    using RetroBar.Utilities;

    /// <summary>
    /// Interaction logic for NotifyIcon.xaml
    /// </summary>
    public partial class NotifyIcon : UserControl
    {
        private bool isLoaded;
        private ManagedShell.WindowsTray.NotifyIcon TrayIcon;

        public static DependencyProperty HostProperty = DependencyProperty.Register(nameof(Host), typeof(Taskbar), typeof(NotifyIcon));

        public Taskbar Host
        {
            get { return (Taskbar)GetValue(HostProperty); }
            set { SetValue(HostProperty, value); }
        }

        public NotifyIcon()
        {
            InitializeComponent();
        }

        private void applyEffects()
        {
            if (TrayIcon == null || Settings.Instance.InvertIconsMode == InvertIconsOption.Never)
            {
                return;
            }

            if (!TrayIcon.CanInvert())
            {
                return;
            }

            bool invertByTheme = Application.Current.FindResource("InvertSystemNotifyIcons") as bool? ?? false;
            bool performInvert = invertByTheme || Settings.Instance.InvertIconsMode == InvertIconsOption.Always;

            if (NotifyIconImage.Effect == null != performInvert)
            {
                return;
            }

            if (performInvert)
            {
                NotifyIconImage.Effect = new InvertEffect();
            }
            else
            {
                NotifyIconImage.Effect = null;
            }
        }

        private void NotifyIcon_OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!isLoaded)
            {
                TrayIcon = DataContext as ManagedShell.WindowsTray.NotifyIcon;

                if (TrayIcon == null)
                {
                    return;
                }

                applyEffects();

                TrayIcon.NotificationBalloonShown += TrayIcon_NotificationBalloonShown;

                // If a notification was received before we started listening, it will be here. Show the first one that is not expired.
                NotificationBalloon firstUnexpiredNotification = TrayIcon.MissedNotifications.FirstOrDefault(balloon => balloon.Received.AddMilliseconds(balloon.Timeout) > DateTime.Now);

                if (firstUnexpiredNotification != null && Host != null && Host.Screen.Primary)
                {
                    BalloonControl.Show(firstUnexpiredNotification, NotifyIconBorder);
                    TrayIcon.MissedNotifications.Remove(firstUnexpiredNotification);
                }

                isLoaded = true;
            }
        }

        private void NotifyIcon_OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (TrayIcon != null)
            {
                TrayIcon.NotificationBalloonShown -= TrayIcon_NotificationBalloonShown;
            }
            isLoaded = false;
        }

        private void TrayIcon_NotificationBalloonShown(object sender, NotificationBalloonEventArgs e)
        {
            if (Host == null || !Host.Screen.Primary)
            {
                return;
            }

            if (TrayIcon == null || TrayIcon.GetBehavior() == NotifyIconBehavior.AlwaysHide || TrayIcon.GetBehavior() == NotifyIconBehavior.Remove)
            {
                return;
            }

            if (NotifyIconBorder.DataContext != e.Balloon.NotifyIcon)
            {
                return;
            }

            BalloonControl.Show(e.Balloon, NotifyIconBorder);
            e.Handled = true;
        }

        private void NotifyIcon_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            Host?.SetTrayHost();
            if (e.ChangedButton != MouseButton.Left)
            {
                TrayIcon?.IconMouseDown(e.ChangedButton, MouseHelper.GetCursorPositionParam(), System.Windows.Forms.SystemInformation.DoubleClickTime);
            }
        }

        private void NotifyIcon_OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (e.ChangedButton == MouseButton.Left)
            {
                TrayIcon?.IconMouseDown(e.ChangedButton, MouseHelper.GetCursorPositionParam(), System.Windows.Forms.SystemInformation.DoubleClickTime);
            }
            TrayIcon?.IconMouseUp(e.ChangedButton, MouseHelper.GetCursorPositionParam(), System.Windows.Forms.SystemInformation.DoubleClickTime);
        }

        private void NotifyIcon_OnMouseEnter(object sender, MouseEventArgs e)
        {
            e.Handled = true;

            if (TrayIcon != null)
            {
                // update icon position for Shell_NotifyIconGetRect
                Decorator sendingDecorator = sender as Decorator;
                Point location = sendingDecorator.PointToScreen(new Point(0, 0));
                double dpiScale = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.M11;

                TrayIcon.Placement = new NativeMethods.Rect { Top = (int)location.Y, Left = (int)location.X, Bottom = (int)(sendingDecorator.ActualHeight * dpiScale), Right = (int)(sendingDecorator.ActualWidth * dpiScale) };
                TrayIcon.IconMouseEnter(MouseHelper.GetCursorPositionParam());
            }
        }

        private void NotifyIcon_OnMouseLeave(object sender, MouseEventArgs e)
        {
            e.Handled = true;
            TrayIcon?.IconMouseLeave(MouseHelper.GetCursorPositionParam());
        }

        private void NotifyIcon_OnMouseMove(object sender, MouseEventArgs e)
        {
            e.Handled = true;
            TrayIcon?.IconMouseMove(MouseHelper.GetCursorPositionParam());
        }

        private bool HandleNotificationIconMouseWheel(bool upOrDown)
        {
            switch (TrayIcon?.GUID.ToString())
            {
                case NotificationArea.VOLUME_GUID:
                    VolumeChanger.ChangeVolume(WindowHelper.FindWindowsTray(IntPtr.Zero), upOrDown);
                    return true;
                default:
                    return false;
            }
        }

        private void NotifyIcon_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            HandleNotificationIconMouseWheel(e.Delta > 0);
            e.Handled = true;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Controls/NotifyIcon.xaml.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Controls/NotifyBalloon.xaml.cs">
namespace RetroBar.Controls
{
    using ManagedShell.AppBar;
    using ManagedShell.Common.Helpers;
    using ManagedShell.WindowsTray;
    using RetroBar.Utilities;
    using System;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Controls.Primitives;
    using System.Windows.Interop;
    using System.Windows.Media;
    using System.Windows.Threading;

    /// <summary>
    /// Interaction logic for NotifyBalloon.xaml
    /// </summary>
    public partial class NotifyBalloon : UserControl
    {
        private DispatcherTimer _closeTimer;
        private NotificationBalloon _balloonInfo = new NotificationBalloon();

        public NotifyBalloon()
        {
            DataContext = _balloonInfo;
            InitializeComponent();
        }

        public void Show(NotificationBalloon balloonInfo, UIElement placementTarget)
        {
            _balloonInfo = balloonInfo;
            DataContext = _balloonInfo;

            playSound(_balloonInfo);

            BalloonPopup.PlacementTarget = placementTarget;
            BalloonPopup.Placement = PlacementMode.Custom;
            BalloonPopup.CustomPopupPlacementCallback = new CustomPopupPlacementCallback(PlacePopup);
            BalloonPopup.IsOpen = true;

            _balloonInfo.SetVisibility(BalloonVisibility.Visible);

            startTimer(balloonInfo.Timeout);
        }

        public CustomPopupPlacement[] PlacePopup(Size popupSize, Size targetSize, Point offset)
        {
            CustomPopupPlacement placement;

            DpiScale dpiScale = VisualTreeHelper.GetDpi(this);

            switch (Settings.Instance.Edge)
            {
                case AppBarEdge.Top:
                    if (FlowDirection == FlowDirection.LeftToRight)
                    {
                        placement = new CustomPopupPlacement(new Point((popupSize.Width * -1) + (offset.X * dpiScale.DpiScaleX),
                            targetSize.Height + (offset.Y * dpiScale.DpiScaleY)),
                            PopupPrimaryAxis.Horizontal);
                    }
                    else
                    {
                        placement = new CustomPopupPlacement(new Point(-1 * offset.X * dpiScale.DpiScaleX,
                            targetSize.Height + (offset.Y * dpiScale.DpiScaleY)),
                            PopupPrimaryAxis.Horizontal);
                    }
                    break;

                case AppBarEdge.Left:
                    placement = new CustomPopupPlacement(new Point(offset.X * dpiScale.DpiScaleX - (FlowDirection == FlowDirection.LeftToRight ? 0 : targetSize.Width),
                        (popupSize.Height * -1) + (offset.Y * dpiScale.DpiScaleY)),
                        PopupPrimaryAxis.Horizontal);
                    break;

                case AppBarEdge.Right:
                    placement = new CustomPopupPlacement(new Point((popupSize.Width * -1) + (offset.X * dpiScale.DpiScaleX),
                        (popupSize.Height * -1) + (offset.Y * dpiScale.DpiScaleY)),
                        PopupPrimaryAxis.Horizontal);
                    break;

                default:
                    // bottom taskbar
                    if (FlowDirection == FlowDirection.LeftToRight)
                    {
                        placement = new CustomPopupPlacement(new Point((popupSize.Width * -1) + (offset.X * dpiScale.DpiScaleX),
                            (popupSize.Height * -1) + (offset.Y * dpiScale.DpiScaleY)),
                            PopupPrimaryAxis.Horizontal);
                    }
                    else
                    {
                        placement = new CustomPopupPlacement(new Point(-1 * offset.X * dpiScale.DpiScaleX,
                            (popupSize.Height * -1) + (offset.Y * dpiScale.DpiScaleY)),
                            PopupPrimaryAxis.Horizontal);
                    }
                    break;
            }

            return new CustomPopupPlacement[] { placement };
        }

        private void playSound(NotificationBalloon balloonInfo)
        {
            if (BalloonPopup.IsOpen)
            {
                return;
            }

            if ((balloonInfo.Flags & ManagedShell.Interop.NativeMethods.NIIF.NOSOUND) != 0)
            {
                return;
            }

            SoundHelper.PlayNotificationSound();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            closeBalloon();
            _balloonInfo.SetVisibility(BalloonVisibility.Hidden);
            e.Handled = true;
        }

        private void closeBalloon()
        {
            _closeTimer?.Stop();
            BalloonPopup.IsOpen = false;
        }

        private void startTimer(int timeout)
        {
            _closeTimer?.Stop();

            _closeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(timeout)
            };

            _closeTimer.Tick += CloseTimer_Tick;
            _closeTimer.Start();
        }

        private void CloseTimer_Tick(object sender, EventArgs e)
        {
            closeBalloon();
            _balloonInfo.SetVisibility(BalloonVisibility.TimedOut);
        }

        private void ContentControl_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Prevent taskbar context menu from appearing
            e.Handled = true;
        }

        private void ContentControl_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _balloonInfo.Click();

            closeBalloon();
            e.Handled = true;
        }

        private void BalloonPopup_Opened(object sender, EventArgs e)
        {
            IntPtr balloonHwnd = (PresentationSource.FromVisual(BalloonPopup.Child) as HwndSource).Handle;
            IntPtr taskbarHwnd = (PresentationSource.FromVisual(this) as HwndSource).Handle;

            ManagedShell.Interop.NativeMethods.SetWindowLongPtr(balloonHwnd, ManagedShell.Interop.NativeMethods.WindowLongFlags.GWLP_HWNDPARENT, taskbarHwnd);
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Controls/NotifyBalloon.xaml.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Controls/Clock.xaml.cs">
namespace RetroBar.Controls
{
    using ManagedShell.Common.Helpers;
    using ManagedShell.Common.Logging;
    using ManagedShell.UWPInterop;
    using Microsoft.Win32;
    using RetroBar.Utilities;
    using System;
    using System.Globalization;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Data;
    using System.Windows.Input;
    using System.Windows.Media;
    using System.Windows.Threading;

    /// <summary>
    /// Interaction logic for Clock.xaml
    /// </summary>
    public partial class Clock : UserControl
    {
        public static readonly DependencyProperty NowProperty = DependencyProperty.Register(nameof(Now), typeof(DateTime), typeof(Clock));

        public DateTime Now
        {
            get => (DateTime)GetValue(NowProperty);
            set => SetValue(NowProperty, value);
        }

        private readonly DispatcherTimer _clock = new DispatcherTimer(DispatcherPriority.Background);
        private bool _isLoaded;

        private const int LOCALE_NAME_MAX_LENGTH = 85;

        public Clock()
        {
            InitializeComponent();
            DataContext = this;

            _clock.Interval = TimeSpan.FromMilliseconds(200);
            _clock.Tick += Clock_Tick;
        }

        private void Initialize()
        {
            UpdateUserCulture();
            if (Settings.Instance.ShowClock)
            {
                StartClock();
            }
            else
            {
                Visibility = Visibility.Collapsed;
            }

            Settings.Instance.PropertyChanged += Settings_PropertyChanged;
            SystemEvents.TimeChanged += TimeChanged;
            SystemEvents.UserPreferenceChanged += UserPreferenceChanged;
        }

        private void StartClock()
        {
            SetTime();

            _clock.Start();

            Visibility = Visibility.Visible;
        }

        private void StopClock()
        {
            _clock.Stop();

            Visibility = Visibility.Collapsed;
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(Settings.ShowClock):
                    if (Settings.Instance.ShowClock)
                    {
                        StartClock();
                    }
                    else
                    {
                        StopClock();
                    }
                    break;
                case nameof(Settings.ShowClockSeconds):
                case nameof(Settings.OverrideClockFormat):
                case nameof(Settings.ClockFormat):
                case nameof(Settings.OverrideAMPMDesignators):
                case nameof(Settings.AMDesignator):
                case nameof(Settings.PMDesignator):
                    UpdateUserCulture();
                    break;
            }
        }

        private void Clock_Tick(object sender, EventArgs args)
        {
            SetTime();
        }

        private void TimeChanged(object sender, EventArgs e)
        {
            TimeZoneInfo.ClearCachedData();
        }

        private static void SetConverterCultureRecursively(DependencyObject main, CultureInfo ci)
        {
            if (main != null)
            {
                var binding = BindingOperations.GetBinding(main, TextBlock.TextProperty);

                if (binding != null)
                {
                    BindingOperations.SetBinding(main, TextBlock.TextProperty,
                        new Binding(binding.Path.Path)
                            { StringFormat = binding.StringFormat, ConverterCulture = ci });
                }

                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(main); i++)
                {
                    if (VisualTreeHelper.GetChild(main, i) is UIElement sub)
                    {
                        SetConverterCultureRecursively(sub, ci);
                    }
                }
            }
        }

        private void UpdateUserCulture()
        {
            CultureInfo userCulture;

            try
            {
                string localeName = GetUserDefaultLocaleNameWrapper();

                if (!string.IsNullOrEmpty(localeName))
                {
                    userCulture = new CultureInfo(localeName);
                }
                else
                {
                    // Fallback: use LCID if locale name isn't available.
                    int lcid = GetUserDefaultLCID();
                    userCulture = new CultureInfo(lcid);
                }
            }
            catch (Exception e)
            {
                ShellLogger.Error($"Clock: Unable to get the user culture: {e.Message}, defaulting to current culture");
                userCulture = CultureInfo.CurrentCulture;
            }

            if (userCulture.IsReadOnly)
            {
                userCulture = (CultureInfo)userCulture.Clone();
            }

            if (Settings.Instance.ShowClockSeconds)
            {
                userCulture.DateTimeFormat.ShortTimePattern = userCulture.DateTimeFormat.LongTimePattern;
            }

            // Override culture info if desired, inserting newlines where appropriate
            if (Settings.Instance.OverrideClockFormat && !string.IsNullOrEmpty(Settings.Instance.ClockFormat))
            {
                userCulture.DateTimeFormat.ShortTimePattern = Settings.Instance.ClockFormat.Replace("\\n", "\n");
            }

            if (Settings.Instance.OverrideAMPMDesignators)
            {
                if (Settings.Instance.AMDesignator != "")
                {
                    userCulture.DateTimeFormat.AMDesignator = Settings.Instance.AMDesignator;
                }
                if (Settings.Instance.PMDesignator != "")
                {
                    userCulture.DateTimeFormat.PMDesignator = Settings.Instance.PMDesignator;
                }
            }

            SetConverterCultureRecursively(this, userCulture);
            SetConverterCultureRecursively(ClockTip, userCulture);
        }

        private void UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.Locale)
            {
                CultureInfo.CurrentCulture.ClearCachedData();
                UpdateUserCulture();
            }
        }

        private void SetTime()
        {
            Now = DateTime.Now;
        }

        private static string GetUserDefaultLocaleNameWrapper()
        {
            var sb = new StringBuilder(LOCALE_NAME_MAX_LENGTH);
            int ret = GetUserDefaultLocaleName(sb, sb.Capacity);
            return ret > 0 ? sb.ToString() : null;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetUserDefaultLocaleName(StringBuilder lpLocaleName, int cchLocaleName);

        [DllImport("kernel32.dll")]
        private static extern int GetUserDefaultLCID();

        private void Clock_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Settings.Instance.ClockClickAction == ClockClickOption.DoNothing)
            {
                return;
            }

            e.Handled = true;

            switch (Settings.Instance.ClockClickAction)
            {
                case ClockClickOption.OpenModernCalendar:
                    Point screenPosition = PointToScreen(new(0, 0));
                    ManagedShell.Interop.NativeMethods.Rect rect = new(
                        (int)screenPosition.X, (int)screenPosition.Y,
                        (int)(screenPosition.X + RenderSize.Width),
                        (int)(screenPosition.Y + RenderSize.Height)
                    );
                    ImmersiveShellHelper.ShowClockFlyout(rect);
                    break;
                case ClockClickOption.OpenAeroCalendar:
                    IntPtr hWnd = (PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource).Handle;
                    ClockFlyoutLauncher.ShowAeroClockFlyout(hWnd);
                    break;
                case ClockClickOption.OpenNotificationCenter:
                    if (EnvironmentHelper.IsWindows10RS4OrBetter)
                    {
                        ImmersiveShellHelper.ShowActionCenter();
                    }
                    else
                    {
                        ShellHelper.ShowActionCenter();
                    }
                    break;
            }
        }

        private void Clock_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ShellHelper.StartProcess("timedate.cpl");

            e.Handled = true;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded)
            {
                Initialize();

                _isLoaded = true;
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            StopClock();

            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
            SystemEvents.TimeChanged -= TimeChanged;
            SystemEvents.UserPreferenceChanged -= UserPreferenceChanged;

            _isLoaded = false;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            UpdateUserCulture();
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Controls/Clock.xaml.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Controls/InputLanguage.xaml.cs">
namespace RetroBar.Controls
{
    using System;
    using System.Linq;
    using System.Globalization;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;
    using System.Windows.Threading;
    using ManagedShell.Common.Helpers;
    using ManagedShell.Common.Logging;
    using RetroBar.Converters;
    using RetroBar.Utilities;
    using WinForms = System.Windows.Forms;

    public partial class InputLanguage : UserControl
    {
        public static DependencyProperty LocaleIdentifierProperty = DependencyProperty.Register(nameof(LocaleIdentifier), typeof(CultureInfo), typeof(InputLanguage));

        public CultureInfo LocaleIdentifier
        {
            get { return (CultureInfo)GetValue(LocaleIdentifierProperty); }
            set { SetValue(LocaleIdentifierProperty, value); }
        }

        public static DependencyProperty HostProperty = DependencyProperty.Register(nameof(Host), typeof(Taskbar), typeof(InputLanguage));

        public Taskbar Host
        {
            get { return (Taskbar)GetValue(HostProperty); }
            set { SetValue(HostProperty, value); }
        }

        private readonly DispatcherTimer layoutWatch = new DispatcherTimer(DispatcherPriority.Background);

        private bool _isLoaded;

        public InputLanguage()
        {
            InitializeComponent();
            DataContext = this;

            layoutWatch.Interval = TimeSpan.FromMilliseconds(200);
            layoutWatch.Tick += LayoutWatchTick;
        }

        private void Initialize()
        {
            if (Settings.Instance.ShowInputLanguage)
            {
                StartWatch();
            }
            else
            {
                Visibility = Visibility.Collapsed;
            }

            Settings.Instance.PropertyChanged += Settings_PropertyChanged;
        }

        private void SetLocaleIdentifier()
        {
            try
            {
                var managedShellLayout = KeyboardLayoutHelper.GetKeyboardLayout(false);
                LocaleIdentifier = CultureInfo.GetCultureInfo((short)managedShellLayout.HKL);
            }
            catch (Exception ex)
            {
                ShellLogger.Error($"Error getting locale identifier: {ex.Message}");
            }
        }

        private void StartWatch()
        {
            SetLocaleIdentifier();
            
            layoutWatch.Start();

            Visibility = Visibility.Visible;
        }

        private void JapaneseImeAdd()
        {
            var ChkControl = InputLanguageDockPanel.Children
                                .OfType<JapaneseIme>()
                                .FirstOrDefault();

            if (ChkControl != null)
            {
                return;
            }

            var NewControl = new JapaneseIme();
            InputLanguageDockPanel.Children.Add(NewControl);
        }
        
        private void JapaneseImeRemove()
        {
            var DelControl = InputLanguageDockPanel.Children
                                .OfType<JapaneseIme>()
                                .FirstOrDefault();

            if (DelControl != null)
            {
                InputLanguageDockPanel.Children.Remove(DelControl);
            }
        }

        private int InstalledLanguagesCount()
        {
            int LangCount = 0;

            foreach (System.Windows.Forms.InputLanguage CurLang in System.Windows.Forms.InputLanguage.InstalledInputLanguages)
            {
                LangCount++;
            }

            return LangCount;
        }

        private void LayoutWatchTick(object sender, EventArgs args)
        {
            SetLocaleIdentifier();

            var LocaleConverter = new CultureInfoToLocaleNameConverter();
            string LocaleName = (string)LocaleConverter.Convert(LocaleIdentifier, typeof(string), "TwoLetterIsoLanguageName", CultureInfo.InvariantCulture);

            if (LocaleName == "JA")
            {
                JapaneseImeAdd();

                if (InstalledLanguagesCount() == 1)
                {
                    InputLanguageText.Visibility = Visibility.Collapsed;
                }
                else
                {
                    InputLanguageText.Visibility = Visibility.Visible;
                }
            }
            else
            {
                JapaneseImeRemove();
                InputLanguageText.Visibility = Visibility.Visible;
            }
        }

        private void StopWatch()
        {
            layoutWatch.Stop();

            Visibility = Visibility.Collapsed;
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.ShowInputLanguage))
            {
                if (Settings.Instance.ShowInputLanguage)
                {
                    StartWatch();
                }
                else
                {
                    StopWatch();
                }
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded)
            {
                Initialize();

                _isLoaded = true;
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            StopWatch();
            
            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;

            _isLoaded = false;
        }

        private void ShowLanguageMenu()
        {
            var menu = new ContextMenu();

            foreach (WinForms.InputLanguage lang in WinForms.InputLanguage.InstalledInputLanguages)
            {
                string code = lang.Culture.TwoLetterISOLanguageName.ToUpperInvariant();
                string displayName = lang.Culture.DisplayName;
                bool multiLayout = false;

                foreach (WinForms.InputLanguage nestedLang in WinForms.InputLanguage.InstalledInputLanguages)
                {
                    if (nestedLang.Culture.DisplayName == lang.Culture.DisplayName && nestedLang.LayoutName != lang.LayoutName)
                    {
                        multiLayout = true;
                        break;
                    }
                }

                if (multiLayout)
                {
                    displayName = string.Format((string)FindResource("input_switcher_item_format"), lang.Culture.DisplayName, lang.LayoutName);
                }

                var item = new MenuItem
                {
                    Header = new InputLanguageMenuItem(code, displayName),
                    Tag = lang,
                    IsCheckable = true,
                    IsChecked = lang.Equals(WinForms.InputLanguage.CurrentInputLanguage)
                };

                item.Click += (s, e) =>
                {
                    WinForms.InputLanguage.CurrentInputLanguage = (WinForms.InputLanguage)((MenuItem)s).Tag;
                };

                menu.Items.Add(item);
            }

            menu.Closed += (sender, e) =>
            {
                Host?.RemoveOpenMenu();
            };

            menu.PlacementTarget = InputLanguageButton;
            Host?.AddOpenMenu();
            menu.IsOpen = true;
        }

        private void InputLanguageButton_Click(object sender, RoutedEventArgs e)
        {
            ShowLanguageMenu();
        }
    };
}
// <RETROSTAR_SOURCE_END path="RetroBar/Controls/InputLanguage.xaml.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Controls/InputLanguageMenuItem.xaml.cs">
namespace RetroBar.Controls
{
    using System;
    using System.Windows.Controls;

    /// <summary>
    /// Interaction logic for InputLanguageMenuItem.xaml
    /// </summary>
    public partial class InputLanguageMenuItem : UserControl
    {
        public string Code { get; set; }
        public string DisplayName { get; set; }

        public InputLanguageMenuItem(string code, string displayName)
        {
            Code = code;
            DisplayName = displayName;
            DataContext = this;
            InitializeComponent();
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Controls/InputLanguageMenuItem.xaml.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Controls/JapaneseIme.xaml.cs">
namespace RetroBar.Controls
{
    using System;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Threading;
    using ManagedShell.AppBar;
    using static ManagedShell.Interop.NativeMethods;
    using Microsoft.Win32;
    using RetroBar.Utilities;

    /// <summary>
    /// Interaction logic for JapaneseIme.xaml
    /// </summary>
    public partial class JapaneseIme : UserControl
    {
        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        // IME
        [DllImport("imm32.dll")]
        static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

        const int WM_IME_CONTROL = 0x0283;

        const uint IME_CMODE_NATIVE = 0x0001;
        const uint IME_CMODE_KATAKANA = 0x0002;  // only effect under IME_CMODE_NATIVE
        const uint IME_CMODE_FULLSHAPE = 0x0008;
        const uint IMC_GETCONVERSIONMODE = 0x0001;
        const uint IMC_SETCONVERSIONMODE = 0x0002;
        const uint IMC_GETSENTENCEMODE = 0x0003;
        const uint IMC_SETSENTENCEMODE = 0x0004;
        const uint IMC_GETOPENSTATUS = 0x0005;
        const uint IMC_SETOPENSTATUS = 0x0006;

        // Regstory
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        static extern int RegOpenKey(UIntPtr hKey, string lpSubKey, out UIntPtr phkResult);

        [DllImport("advapi32.dll")]
        static extern int RegCloseKey(UIntPtr hKey);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        static extern int RegQueryValueEx(UIntPtr hKey, string lpValueName, IntPtr lpReserved, out uint lpType, out uint lpData, ref uint lpcbData);

        const int ERROR_SUCCESS = 0;

        const uint REG_DWORD = 4; // 32-bit number
        const uint REG_SIZE_DWORD = 4; // 32-bit number

        // SendInput
        const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        const ushort VK_SHIFT = 0x10;
        const ushort VK_CONTROL = 0x11;
        const ushort VK_F10 = 0x79;
        const ushort VK_OEM_COPY = 0xF2;

        private IntPtr hWndCurFg;
        private bool NewImeEnabled;
        private bool CurInputRoma;
        private uint CurInputMode;
        private uint CurImmConversion;

        private readonly DispatcherTimer ImeCheckTimer = new DispatcherTimer(DispatcherPriority.Background);

        private bool _isLoaded;

        public static readonly DependencyProperty JapaneseImeEnabledProperty = DependencyProperty.Register(nameof(JapaneseImeEnabled), typeof(bool), typeof(JapaneseIme));

        public bool JapaneseImeEnabled
        {
            get => (bool)GetValue(JapaneseImeEnabledProperty);
            set => SetValue(JapaneseImeEnabledProperty, value);
        }

        public static readonly DependencyProperty JapaneseImeTipProperty = DependencyProperty.Register(nameof(JapaneseImeTip), typeof(string), typeof(JapaneseIme));

        public string JapaneseImeTip
        {
            get => (string)GetValue(JapaneseImeTipProperty);
            set => SetValue(JapaneseImeTipProperty, value);
        }

        public static readonly DependencyProperty JapaneseImeStatusProperty = DependencyProperty.Register(nameof(JapaneseImeStatus), typeof(string), typeof(JapaneseIme));

        public string JapaneseImeStatus
        {
            get => (string)GetValue(JapaneseImeStatusProperty);
            set => SetValue(JapaneseImeStatusProperty, value);
        }

        public JapaneseIme()
        {
            InitializeComponent();
            DataContext = this;

            ImeCheckTimer.Interval = TimeSpan.FromMilliseconds(200);
            ImeCheckTimer.Tick += ImeChk_Event;
        }

        private void Initialize()
        {
            hWndCurFg = (IntPtr)0;
            NewImeEnabled = false;
            CurInputRoma = false;
            CurInputMode = 0;
            CurImmConversion = 0;

            JapaneseImeTip = (string)this.FindResource("ime_input_tip_disabled");
            JapaneseImeStatus = @"";

            ImeChk();
            ImeCheckTimer.Start();
        }

        private void JapaneseIme_OnClick(object sender, RoutedEventArgs e)
        {
            ImmSetOpen(ImmOpenStatus.ImmToggle);
        }

        private void JapaneseIme_full_shape_hiragana_OnClick(object sender, RoutedEventArgs e)
        {
            IntPtr hImeWnd;
            uint ImmNewConversion;

            if ((hImeWnd = ImmOpenGetWindow()) == (IntPtr)0)
                return;

            ImmNewConversion = (uint)SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETCONVERSIONMODE, (IntPtr)0);
            ImmNewConversion |= IME_CMODE_FULLSHAPE;
            ImmNewConversion |= IME_CMODE_NATIVE;
            ImmNewConversion &= ~IME_CMODE_KATAKANA;
            SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_SETCONVERSIONMODE, (IntPtr)ImmNewConversion);
        }

        private void JapaneseIme_full_shape_katakana_OnClick(object sender, RoutedEventArgs e)
        {
            IntPtr hImeWnd;
            uint ImmNewConversion;

            if ((hImeWnd = ImmOpenGetWindow()) == (IntPtr)0)
                return;

            ImmNewConversion = (uint)SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETCONVERSIONMODE, (IntPtr)0);
            ImmNewConversion |= IME_CMODE_FULLSHAPE;
            ImmNewConversion |= IME_CMODE_NATIVE;
            ImmNewConversion |= IME_CMODE_KATAKANA;
            SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_SETCONVERSIONMODE, (IntPtr)ImmNewConversion);
        }

        private void JapaneseIme_full_shape_alphanumeric_OnClick(object sender, RoutedEventArgs e)
        {
            IntPtr hImeWnd;
            uint ImmNewConversion;

            if ((hImeWnd = ImmOpenGetWindow()) == (IntPtr)0)
                return;

            ImmNewConversion = (uint)SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETCONVERSIONMODE, (IntPtr)0);
            ImmNewConversion |= IME_CMODE_FULLSHAPE;
            ImmNewConversion &= ~IME_CMODE_NATIVE;
            ImmNewConversion &= ~IME_CMODE_KATAKANA;
            SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_SETCONVERSIONMODE, (IntPtr)ImmNewConversion);
        }

        private void JapaneseIme_kana_OnClick(object sender, RoutedEventArgs e)
        {
            IntPtr hImeWnd;
            uint ImmNewConversion;

            if ((hImeWnd = ImmOpenGetWindow()) == (IntPtr)0)
                return;

            ImmNewConversion = (uint)SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETCONVERSIONMODE, (IntPtr)0);
            ImmNewConversion &= ~IME_CMODE_FULLSHAPE;
            ImmNewConversion |= IME_CMODE_NATIVE;
            ImmNewConversion |= IME_CMODE_KATAKANA;
            SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_SETCONVERSIONMODE, (IntPtr)ImmNewConversion);
        }

        private void JapaneseIme_alphanumeric_OnClick(object sender, RoutedEventArgs e)
        {
            IntPtr hImeWnd;
            uint ImmNewConversion;

            if ((hImeWnd = ImmOpenGetWindow()) == (IntPtr)0)
                return;

            ImmNewConversion = (uint)SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETCONVERSIONMODE, (IntPtr)0);
            ImmNewConversion &= ~IME_CMODE_FULLSHAPE;
            ImmNewConversion &= ~IME_CMODE_NATIVE;
            ImmNewConversion &= ~IME_CMODE_KATAKANA;
            SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_SETCONVERSIONMODE, (IntPtr)ImmNewConversion);
        }

        private void JapaneseIme_direct_OnClick(object sender, RoutedEventArgs e)
        {
            ImmSetOpen(ImmOpenStatus.ImmClose);
        }

        private void JapaneseIme_open_imepad_OnClick(object sender, RoutedEventArgs e)
        {
            if (ImmOpenGetWindow() == (IntPtr)0)
                return;

            ExecImePad("");
        }

        private void JapaneseIme_open_add_word_dictionary_OnClick(object sender, RoutedEventArgs e)
        {
            ExecImeDictionaryTool("-w");
        }

        private void JapaneseIme_open_dictionary_tool_OnClick(object sender, RoutedEventArgs e)
        {
            ExecImeDictionaryTool("-t");
        }

        private void JapaneseIme_open_properties_OnClick(object sender, RoutedEventArgs e)
        {
            ExecImeProperties("");
        }

        private void JapaneseIme_input_key_roma_OnClick(object sender, RoutedEventArgs e)
        {
            if (ImmOpenGetWindow() == (IntPtr)0)
                return;

            if (CurInputRoma)
                return;     // no need to execute

            ToggleKanaMode();	// change "kana" to "roma"
        }

        private void JapaneseIme_input_key_kana_OnClick(object sender, RoutedEventArgs e)
        {
            if (ImmOpenGetWindow() == (IntPtr)0)
                return;

            if (!CurInputRoma)
                return;     // no need to execute

            ToggleKanaMode();	// change "roma" to "kana"
        }

        private void JapaneseIme_conversion_general_OnClick(object sender, RoutedEventArgs e)
        {
            IntPtr hImeWnd;

            if ((hImeWnd = ImmOpenGetWindow()) == (IntPtr)0)
                return;

            SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_SETSENTENCEMODE, (IntPtr)0x08);
        }

        private void JapaneseIme_conversion_none_OnClick(object sender, RoutedEventArgs e)
        {
            IntPtr hImeWnd;

            if ((hImeWnd = ImmOpenGetWindow()) == (IntPtr)0)
                return;

            SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_SETSENTENCEMODE, (IntPtr)0x00);
        }

        private IntPtr ImmOpenGetWindow()
        {
            IntPtr hImeWnd;
            int OpenSts;

            hImeWnd = ImmSetOpen(ImmOpenStatus.ImmOpen);

            OpenSts = (int)SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETOPENSTATUS, (IntPtr)0);

            if (OpenSts == 0)
                return (IntPtr)0;

            return hImeWnd;
        }

        private enum ImmOpenStatus
        {
            ImmClose,
            ImmOpen,
            ImmToggle
        }

        private IntPtr ImmSetOpen(ImmOpenStatus SetStatus)
        {
            IntPtr hImeWnd;
            int OpenCurSts;
            int OpenNewSts;

            SetForeOtherTopWindow();

            if ((hImeWnd = ImmGetDefaultIMEWnd(hWndCurFg)) == (IntPtr)0)
                return (IntPtr)0;

            OpenCurSts = (int)SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETOPENSTATUS, (IntPtr)0);

            switch (SetStatus)
            {
                case ImmOpenStatus.ImmOpen:
                    OpenNewSts = 1;
                    break;

                case ImmOpenStatus.ImmClose:
                    OpenNewSts = 0;
                    break;

                case ImmOpenStatus.ImmToggle:
                    OpenNewSts = OpenCurSts == 0 ? 1 : 0;
                    break;

                default:
                    OpenNewSts = OpenCurSts;
                    break;
            }

            SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_SETOPENSTATUS, (IntPtr)OpenNewSts);

            return hImeWnd;
        }

        // When changing the IME state from the RetroBar,
        // focus should be returned to the window the user was previously using.
        private void SetForeOtherTopWindow()
        {
            UpdateOtherTopWindow();

            if (hWndCurFg == GetForegroundWindow())
                return;     // no need to execute

            if (!IsWindow(hWndCurFg))
                return;     // invalid Window

            if (!IsWindowVisible(hWndCurFg))
                return;     // hidden Window

            SetForegroundWindow(hWndCurFg);
            Thread.Sleep(200);  // 100ms = OK / 50ms = NG
            return;
        }

        // IME state is different for each window.
        // Clicking will give focus to RetroBar.
        // Need to get the previous window.
        private void UpdateOtherTopWindow()
        {
            IntPtr hWkTop;
            uint WkThId, WkProcId;
            int WkLayout;

            if ((hWkTop = GetForegroundWindow()) == (IntPtr)0)
                return; // error, not update.

            if ((WkThId = GetWindowThreadProcessId(hWkTop, out WkProcId)) == 0)
                return; // error, not update.

            if (WkProcId == GetCurrentProcessId())
                return;	// foreground is current process. not update.

            // Reference win32api.
            //     MAKELANGID(LANG_JAPANESE, SUBLANG_JAPANESE_JAPAN) == 0x411
            WkLayout = GetKeyboardLayout(WkThId);
            NewImeEnabled = (WkLayout & 0xFFFF) == 0x411;

            hWndCurFg = hWkTop; // update
            return;
        }

        private bool ExecImeDictionaryTool(string ExecOpt)
        {
            string WinDirPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string Dict32Name = WinDirPath + @"\SysWOW64\IME\IMEJP\imjpdct.exe";
            string Dict64Name = WinDirPath + @"\System32\IME\IMEJP\imjpdct.exe";

            SetForeOtherTopWindow();

            return PrgExec(Dict32Name, Dict64Name, ExecOpt);
        }

        private bool ExecImeProperties(string ExecOpt)
        {
            string WinDirPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string Prop32Name = WinDirPath + @"\SysWOW64\IME\IMEJP\imjpset.exe";
            string Prop64Name = WinDirPath + @"\System32\IME\IMEJP\imjpset.exe";

            SetForeOtherTopWindow();

            return PrgExec(Prop32Name, Prop64Name, ExecOpt);
        }

        // Ideally, call it via an API,
        // but this is a suboptimal solution.
        private bool ExecImePad(string ExecOpt)
        {
            bool RetSts = true;

            INPUT[] InBuf = new INPUT[6];
            uint RetCnt;

            try
            {
                // [Ctrl] + [F10]
                InBuf[0].type = INPUT_KEYBOARD;
                InBuf[0].mkhi.ki.wVk = VK_CONTROL;
                InBuf[0].mkhi.ki.dwFlags = KEYEVENTF_EXTENDEDKEY;

                InBuf[1].type = INPUT_KEYBOARD;
                InBuf[1].mkhi.ki.wVk = VK_F10;
                InBuf[1].mkhi.ki.dwFlags = 0;

                InBuf[2].type = INPUT_KEYBOARD;
                InBuf[2].mkhi.ki.wVk = VK_F10;
                InBuf[2].mkhi.ki.dwFlags = KEYEVENTF_KEYUP;

                InBuf[3].type = INPUT_KEYBOARD;
                InBuf[3].mkhi.ki.wVk = VK_CONTROL;
                InBuf[3].mkhi.ki.dwFlags = KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP;

                // [P]
                InBuf[4].type = INPUT_KEYBOARD;
                InBuf[4].mkhi.ki.wVk = 'P';
                InBuf[4].mkhi.ki.dwFlags = 0;

                InBuf[5].type = INPUT_KEYBOARD;
                InBuf[5].mkhi.ki.wVk = 'P';
                InBuf[5].mkhi.ki.dwFlags = KEYEVENTF_KEYUP;

                RetCnt = SendInput(6, InBuf, Marshal.SizeOf(typeof(INPUT)));

                if (RetCnt != 6)
                    throw new System.Exception();
            }

            catch
            {
                RetSts = false;
            }

            return RetSts;
        }

        private bool PrgExec(string Exec32bit, string Exec64bit, string ExecOpt)
        {
            bool RetSts = true;
            bool Wow64Sts;

            if (!IsWow64Process(GetCurrentProcess(), out Wow64Sts))
                return false;

            if (Wow64Sts)
                RetSts = ManagedShell.Common.Helpers.ShellHelper.StartProcess(Exec32bit);     // 32bit
            else
                RetSts = ManagedShell.Common.Helpers.ShellHelper.StartProcess(Exec64bit);     // 64bit

            return RetSts;
        }

        // Determining whether input is kana or romaji
        // does not work via the API and requires reading the registry.
        private bool GetRegKanaMd()
        {
            bool RetMode;

            string RegKeyStrKanaMd = @"Software\AppDataLow\Software\Microsoft\IME\15.0\IMEJP\MSIME";
            int ChkWk;

            UIntPtr hKey;

            uint GetType, GetData;
            uint GetLen = REG_SIZE_DWORD;

            unchecked
            {
                if ((ChkWk = RegOpenKey((UIntPtr)RegistryHive.CurrentUser, RegKeyStrKanaMd, out hKey)) != ERROR_SUCCESS)
                    return false;   // If unknown, treat as "roma"
            }

            ChkWk = RegQueryValueEx(hKey, "kanaMd", (IntPtr)0, out GetType, out GetData, ref GetLen);

            RegCloseKey(hKey);

            if (ChkWk == ERROR_SUCCESS && GetType == REG_DWORD)
            {
                if (GetData == 1)
                    RetMode = true;		// 1: kana
                else
                    RetMode = false;	// 2: roma
            }
            else
            {
                RetMode = true;		// If unknown, treat as "roma"
            }

            return RetMode;
        }

        // Switching between Kana input and Romaji input
        // affects all windows simultaneously.
        private bool ToggleKanaMode()
        {
            bool RetSts = true;

            INPUT[] InBuf = new INPUT[6];
            uint RetCnt;

            try
            {
                // [Ctrl] + [Shift] + [kana]
                InBuf[0].type = INPUT_KEYBOARD;
                InBuf[0].mkhi.ki.wVk = VK_CONTROL;
                InBuf[0].mkhi.ki.dwFlags = KEYEVENTF_EXTENDEDKEY;

                InBuf[1].type = INPUT_KEYBOARD;
                InBuf[1].mkhi.ki.wVk = VK_SHIFT;
                InBuf[1].mkhi.ki.dwFlags = 0;

                // https://atmarkit.itmedia.co.jp/bbs/phpBB/viewtopic.php?topic=42587&forum=7
                //	VK_OEM_COPY = 0xF2,		// kana key button.

                InBuf[2].type = INPUT_KEYBOARD;
                InBuf[2].mkhi.ki.wVk = VK_OEM_COPY;     // kana key button.
                InBuf[2].mkhi.ki.dwFlags = KEYEVENTF_EXTENDEDKEY;

                InBuf[3].type = INPUT_KEYBOARD;
                InBuf[3].mkhi.ki.wVk = VK_OEM_COPY;     // kana key button.
                InBuf[3].mkhi.ki.dwFlags = KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP;

                InBuf[4].type = INPUT_KEYBOARD;
                InBuf[4].mkhi.ki.wVk = VK_SHIFT;
                InBuf[4].mkhi.ki.dwFlags = KEYEVENTF_KEYUP;

                InBuf[5].type = INPUT_KEYBOARD;
                InBuf[5].mkhi.ki.wVk = VK_CONTROL;
                InBuf[5].mkhi.ki.dwFlags = KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP;

                RetCnt = SendInput(6, InBuf, Marshal.SizeOf(typeof(INPUT)));

                if (RetCnt != 6)
                    throw new System.Exception();
            }

            catch
            {
                RetSts = false;
            }

            return RetSts;
        }

        private void ImeChk_Event(object sender, EventArgs args)
        {
            ((DispatcherTimer)sender).Stop();
            ImeChk();
            ((DispatcherTimer)sender).Start();
        }

        private void ImeChk()
        {
            string NewImmTip;
            IntPtr hImeWnd;
            uint NewInputMode;
            int ImeOpenStatus;
            uint NewInputFlag;
            uint NewImmConversion;
            bool NewInputRoma;
            string NewImeStatus;
            bool NeedDspUpdate;

            const int MODE_FULL_SHAPE_HIRAGANA = 40000;
            const int MODE_FULL_SHAPE_KATAKANA = 40001;
            const int MODE_FULL_SHAPE_ALPHANUMERIC = 40002;
            const int MODE_KANA = 40003;
            const int MODE_ALPHANUMERIC = 40004;
            const int MODE_DIRECT = 40005;

            UpdateOtherTopWindow();

            NeedDspUpdate = false;
            NewImeStatus = @"✖";
            NewImmTip = (string)this.FindResource("ime_input_tip_disabled");

            if ((hImeWnd = ImmGetDefaultIMEWnd(hWndCurFg)) == (IntPtr)0)
                NewImeEnabled = false;  // IME window not found

            if (NewImeEnabled)
            {
                NewImmTip = (string)this.FindResource("ime_input_tip_enabled");

                NewInputMode = 0;

                ImeOpenStatus = (int)SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETOPENSTATUS, (IntPtr)0);

                if (ImeOpenStatus != 0)
                {
                    // conversion enabled
                    NewInputFlag = (uint)SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETCONVERSIONMODE, (IntPtr)0);
                    NewImmConversion = (uint)SendMessage(hImeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETSENTENCEMODE, (IntPtr)0);

                    if (GetRegKanaMd())
                    {
                        // Kana Input
                        NewInputRoma = false;
                    }
                    else
                    {
                        // Romanized Input
                        NewInputRoma = true;
                    }

                    if ((NewInputFlag & IME_CMODE_FULLSHAPE) != 0)
                    {
                        // full shape
                        if ((NewInputFlag & IME_CMODE_NATIVE) != 0)
                        {
                            // full shape / japanese
                            if ((NewInputFlag & IME_CMODE_KATAKANA) != 0)
                            {
                                // full shape / katakana
                                NewInputMode = MODE_FULL_SHAPE_KATAKANA;
                                NewImeStatus = @"カ";
                            }
                            else
                            {
                                // full shape / hiragana
                                NewInputMode = MODE_FULL_SHAPE_HIRAGANA;
                                NewImeStatus = @"あ";
                            }
                        }
                        else
                        {
                            // full shape / alphanumeric
                            NewInputMode = MODE_FULL_SHAPE_ALPHANUMERIC;
                            NewImeStatus = @"Ａ";
                        }
                    }
                    else
                    {
                        // nornal shape
                        if ((NewInputFlag & IME_CMODE_KATAKANA) != 0)
                        {
                            // nornal shape / katakana
                            NewInputMode = MODE_KANA;
                            // If type it directly, the editor will become confused because it contains special characters.
                            NewImeStatus = "\u033A\uFF76\u0000";
                        }
                        else
                        {
                            // nornal shape / alphanumeric
                            NewInputMode = MODE_ALPHANUMERIC;
                            // If type it directly, the editor will become confused because it contains special characters.
                            NewImeStatus = "\u033A\u0041\u0000";
                        }
                    }
                }
                else
                {
                    // conversion disabled
                    NewInputRoma = false;
                    NewImmConversion = 0;
                    NewInputMode = MODE_DIRECT;
                    NewImeStatus = "A";
                }

                if (CurInputRoma != NewInputRoma)
                {
                    CurInputRoma = NewInputRoma;
                    NeedDspUpdate = true;
                }

                if (CurInputMode != NewInputMode)
                {
                    CurInputMode = NewInputMode;
                    NeedDspUpdate = true;
                }

                if (CurImmConversion != NewImmConversion)
                {
                    CurImmConversion = NewImmConversion;
                    NeedDspUpdate = true;
                }
            }

            if (JapaneseImeEnabled != NewImeEnabled)
            {
                JapaneseImeEnabled = NewImeEnabled;
                NeedDspUpdate = true;
            }

            if (JapaneseImeTip != NewImmTip)
            {
                JapaneseImeTip = NewImmTip;
                NeedDspUpdate = true;
            }

            if (JapaneseImeStatus != NewImeStatus)
            {
                JapaneseImeStatus = NewImeStatus;
                NeedDspUpdate = true;
            }

            if (NeedDspUpdate)
            {
                JapaneseIme_full_shape_hiragana.IsChecked = CurInputMode == MODE_FULL_SHAPE_HIRAGANA;
                JapaneseIme_full_shape_katakana.IsChecked = CurInputMode == MODE_FULL_SHAPE_KATAKANA;
                JapaneseIme_full_shape_alphanumeric.IsChecked = CurInputMode == MODE_FULL_SHAPE_ALPHANUMERIC;
                JapaneseIme_kana.IsChecked = CurInputMode == MODE_KANA;
                JapaneseIme_alphanumeric.IsChecked = CurInputMode == MODE_ALPHANUMERIC;
                JapaneseIme_direct.IsChecked = CurInputMode == MODE_DIRECT;

                if (CurInputMode != MODE_DIRECT)
                {
                    JapaneseIme_input_key_roma.IsChecked = CurInputRoma;
                    JapaneseIme_input_key_kana.IsChecked = !CurInputRoma;
                    JapaneseIme_conversion_general.IsChecked = CurImmConversion != 0;
                    JapaneseIme_conversion_none.IsChecked = CurImmConversion == 0;
                }
                else
                {
                    JapaneseIme_input_key_roma.IsChecked = false;
                    JapaneseIme_input_key_kana.IsChecked = false;
                    JapaneseIme_conversion_general.IsChecked = false;
                    JapaneseIme_conversion_none.IsChecked = false;
                }
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded)
            {
                Initialize();

                _isLoaded = true;
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            ImeCheckTimer.Stop();

            _isLoaded = false;
        }
  };
}
// <RETROSTAR_SOURCE_END path="RetroBar/Controls/JapaneseIme.xaml.cs">

// [5400] PROPERTIES WINDOWS :: Properties (Taskbar, Notification area, Advanced, Start Menu tabs) and notification settings

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/PropertiesWindow.xaml.cs">
namespace RetroBar
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using ManagedShell.Common.Helpers;
    using RetroBar.Utilities;
    using System.Windows;
    using ManagedShell.Common.Logging;
    using Microsoft.Win32;
    using ManagedShell.AppBar;
    using System.Windows.Forms;
    using ManagedShell.WindowsTray;
    using System.Runtime.CompilerServices;
    using System.IO;

    /// <summary>
    /// Interaction logic for PropertiesWindow.xaml
    /// </summary>
    public partial class PropertiesWindow : Window, INotifyPropertyChanged
    {
        private static PropertiesWindow _instance;

        private readonly double _barSize;
        private readonly DictionaryManager _dictionaryManager;
        private readonly double _dpiScale;
        private readonly NotificationArea _notificationArea;
        private readonly AppBarScreen _screen;

        private FileSystemWatcher _themesWatcher;

        public event PropertyChangedEventHandler PropertyChanged;

        // Previews should always assume bottom edge
        public AppBarEdge AppBarEdge
        {
            get => AppBarEdge.Bottom;
        }

        // Previews should always assume horizontal orientation
        public Orientation Orientation
        {
            get => Orientation.Horizontal;
        }

        // Previews should always assume normal mode
        public AppBarMode AppBarMode
        {
            get => AppBarMode.Normal;
        }

        // Previews should reflect the locked setting
        public bool IsLocked
        {
            get => Settings.Instance.LockTaskbar;
        }

        // Previews should never be scaled
        public bool IsScaled
        {
            get => false;
        }

        // Previews should always assume 1 row
        public int Rows
        {
            get => 1;
        }

        private PropertiesWindow(NotificationArea notificationArea, DictionaryManager dictionaryManager, AppBarScreen screen, double dpiScale, double barSize)
        {
            _barSize = barSize;
            _dictionaryManager = dictionaryManager;
            _dpiScale = dpiScale;
            _notificationArea = notificationArea;
            _screen = screen;

            InitializeComponent();

            LoadPreviewHeight();
            LoadAutoStart();
            LoadLanguages();
            LoadOSSupport();
            LoadRows();
            LoadThemes();
            LoadWidth();
            LoadAppInfo();
            LoadClockActions();

            Settings.Instance.PropertyChanged += Settings_PropertyChanged;
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.LockTaskbar))
            {
                OnPropertyChanged(nameof(IsLocked));
                LoadPreviewHeight();
            }
            else if (e.PropertyName == nameof(Settings.Theme))
            {
                LoadPreviewHeight();
            }
            else if (e.PropertyName == nameof(Settings.Language))
            {
                LoadAppInfo();
                LoadClockActions();
            }
        }

        public static PropertiesWindow Open(NotificationArea notificationArea, DictionaryManager dictionaryManager, AppBarScreen screen, double dpiScale, double barSize)
        {
            if (_instance == null)
            {
                _instance = new PropertiesWindow(notificationArea, dictionaryManager, screen, dpiScale, barSize);
                _instance.Show();
            }
            else
            {
                _instance.Activate();
            }

            return _instance;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void LoadPreviewHeight()
        {
            double size = System.Windows.Application.Current.FindResource("TaskbarHeight") as double? ?? 0;

            if (!IsLocked)
            {
                size += System.Windows.Application.Current.FindResource("TaskbarUnlockedSize") as double? ?? 0;
            }

            TaskbarAppearancePreviewControl.Height = size;
            NotificationAreaPreviewControl.Height = size;
        }

        private void LoadAutoStart()
        {
            try
            {
                RegistryKey rKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                List<string> rKeyValueNames = rKey?.GetValueNames().ToList();

                if (rKeyValueNames != null)
                {
                    if (rKeyValueNames.Contains("RetroBar"))
                    {
                        cbAutoStart.IsChecked = true;
                    }
                    else
                    {
                        cbAutoStart.IsChecked = false;
                    }
                }
            }
            catch (Exception e)
            {
                ShellLogger.Error($"PropertiesWindow: Unable to load autorun setting from registry: {e.Message}");
            }
        }

        private void LoadLanguages()
        {
            foreach (var language in _dictionaryManager.GetLanguages())
            {
                cboLanguageSelect.Items.Add(language);
            }
        }

        private void LoadOSSupport()
        {
            if (!EnvironmentHelper.IsWindows10OrBetter)
            {
                // See-through glass is Windows' layered transparency, which every supported Windows has
                cbShowStartButtonMultiMon.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadRows()
        {
            for (int i = 1; i <= Settings.Instance.RowLimit; i++)
            {
                cboRowCount.Items.Add(i.ToString());
            }
        }

        private void LoadAppInfo()
        {
            gbRetroBar.Header = System.Windows.Forms.Application.ProductName;
            txtVersion.Text = string.Format((string)FindResource("version"), System.Windows.Forms.Application.ProductVersion);
        }

        private void LoadWidth()
        {
            sldTaskbarWidth.Maximum = Settings.Instance.TaskbarWidthLimit;
        }

        private void LoadThemes()
        {
            foreach (var theme in _dictionaryManager.GetThemes())
            {
                cboThemeSelect.Items.Add(theme);
            }
            try
            {
                string path = _dictionaryManager.GetThemeInstallDir();
                Directory.CreateDirectory(path);

                _themesWatcher = new FileSystemWatcher(_dictionaryManager.GetThemeInstallDir());
                _themesWatcher.Created += ThemesWatcher_Created;
                _themesWatcher.Deleted += ThemesWatcher_Deleted;
                _themesWatcher.Renamed += ThemesWatcher_Renamed;
                _themesWatcher.Filter = "*.xaml";
                _themesWatcher.EnableRaisingEvents = true;
            }
            catch (Exception e)
            {
                ShellLogger.Warning($"Unable to watch custom themes directory: {e}");
            }
        }

        private void ThemesWatcher_Created(object sender, FileSystemEventArgs e)
        {
            string newTheme = Path.GetFileNameWithoutExtension(e.FullPath);
            Dispatcher.BeginInvoke(() => 
            {
                if (!cboThemeSelect.Items.Contains(newTheme))
                {
                    cboThemeSelect.Items.Add(newTheme);
                }
            });
        }

        private void ThemesWatcher_Deleted(object sender, FileSystemEventArgs e)
        {
            string removedTheme = Path.GetFileNameWithoutExtension(e.FullPath);
            Dispatcher.BeginInvoke(() =>
            {
                if (cboThemeSelect.Items.Contains(removedTheme))
                {
                    if (cboThemeSelect.SelectedItem is string selected && selected == removedTheme)
                    {
                        cboThemeSelect.SelectedIndex = 0;
                    }
                    cboThemeSelect.Items.Remove(removedTheme);
                }
            });
        }

        private void ThemesWatcher_Renamed(object sender, RenamedEventArgs e)
        {
            string removedTheme = Path.GetFileNameWithoutExtension(e.OldFullPath);
            string newTheme = Path.GetFileNameWithoutExtension(e.FullPath);
            Dispatcher.BeginInvoke(() =>
            {
                if (cboThemeSelect.Items.Contains(removedTheme))
                {
                    if (cboThemeSelect.SelectedItem is string selected && selected == removedTheme)
                    {
                        cboThemeSelect.SelectedIndex = 0;
                    }
                    cboThemeSelect.Items.Remove(removedTheme);
                }
                if (!cboThemeSelect.Items.Contains(newTheme))
                {
                    cboThemeSelect.Items.Add(newTheme);
                }
            });
        }

        private void UpdateWindowPosition()
        {
            switch (Settings.Instance.Edge)
            {
                case AppBarEdge.Left:
                    Left = (_screen.Bounds.Left / _dpiScale) + _barSize + 10;
                    Top = (_screen.WorkingArea.Top / _dpiScale) + 10;
                    break;
                case AppBarEdge.Top:
                    Left = (_screen.WorkingArea.Left / _dpiScale) + 10;
                    Top = (_screen.Bounds.Top / _dpiScale) + _barSize + 10;
                    break;
                case AppBarEdge.Right:
                    Left = (_screen.Bounds.Right / _dpiScale) - _barSize - Width - 10;
                    Top = (_screen.WorkingArea.Top / _dpiScale) + 10;
                    break;
                case AppBarEdge.Bottom:
                    Left = (_screen.WorkingArea.Left / _dpiScale) + 10;
                    Top = (_screen.Bounds.Bottom / _dpiScale) - _barSize - Height - 10;
                    break;
            }
        }

        private void OK_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SetQuickLaunchLocation_OnClick(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog fbd = new FolderBrowserDialog();
            fbd.Description = (string)FindResource("quick_launch_folder");
#if NETCOREAPP3_0_OR_GREATER
            fbd.UseDescriptionForTitle = true;
#endif
            fbd.ShowNewFolderButton = false;
            fbd.SelectedPath = Settings.Instance.QuickLaunchPath;

            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                Settings.Instance.QuickLaunchPath = fbd.SelectedPath;
            }
        }

        private void PropertiesWindow_OnClosing(object sender, CancelEventArgs e)
        {
            _instance = null;
            if (_themesWatcher != null)
            {
                _themesWatcher.Created -= ThemesWatcher_Created;
                _themesWatcher.Deleted -= ThemesWatcher_Deleted;
                _themesWatcher.Renamed -= ThemesWatcher_Renamed;
            }
            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
        }

        private void PropertiesWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            Left = 10;
            Top = (ScreenHelper.PrimaryMonitorDeviceSize.Height / DpiHelper.DpiScale) - Height - 40;
            UpdateWindowPosition();
        }

        private void PropertiesWindow_OnContentRendered(object sender, EventArgs e)
        {
            UpdateWindowPosition();
        }

        private void CbAutoStart_OnChecked(object sender, RoutedEventArgs e)
        {
            try
            {
                RegistryKey rKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                var chkBox = (System.Windows.Controls.CheckBox)sender;

                if (chkBox.IsChecked.Equals(false))
                {
                    rKey?.DeleteValue("RetroBar");
                }
                else
                {
                    // Registry Run values are command lines; quote the executable path to handle spaces in usernames/paths.
                    rKey?.SetValue("RetroBar", $"\"{ExePath.GetExecutablePath()}\"");
                }
            }
            catch (Exception exception)
            {
                ShellLogger.Error($"PropertiesWindow: Unable to update registry autorun setting: {exception.Message}");
            }
        }

        private void LoadClockActions()
        {
            if (EnvironmentHelper.IsWindows10OrBetter)
            {
                return;
            }

            // Remove options unsupported prior to Windows 10.
            var availableClockActions = (FindResource("clock_click_action_values") as Array)?.Cast<object>().ToList();
            if (availableClockActions == null)
            {
                return;
            }

            availableClockActions.RemoveAt((int)ClockClickOption.OpenNotificationCenter);
            availableClockActions.RemoveAt((int)ClockClickOption.OpenModernCalendar);
            cboClockAction.ItemsSource = availableClockActions;
        }

        private void CboEdgeSelect_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cboEdgeSelect.SelectedItem == null)
            {
                cboEdgeSelect.SelectedValue = cboEdgeSelect.Items[(int)Settings.Instance.Edge];
            }
        }

        private void CboMultiMonMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cboMultiMonMode.SelectedItem == null)
            {
                cboMultiMonMode.SelectedValue = cboMultiMonMode.Items[(int)Settings.Instance.MultiMonMode];
            }
        }

        private void CboInvertIconsMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cboInvertIconsMode.SelectedItem == null)
            {
                cboInvertIconsMode.SelectedValue = cboInvertIconsMode.Items[(int)Settings.Instance.InvertIconsMode];
            }
        }

        private void CboMiddleMouseAction_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cboMiddleMouseAction.SelectedItem == null)
            {
                cboMiddleMouseAction.SelectedValue = cboMiddleMouseAction.Items[(int)Settings.Instance.TaskMiddleClickAction];
            }
        }

        private void CboStartMenuPowerButton_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cboStartMenuPowerButton.SelectedItem == null && cboStartMenuPowerButton.Items.Count > (int)Settings.Instance.StartMenuPowerButtonAction)
            {
                cboStartMenuPowerButton.SelectedValue = cboStartMenuPowerButton.Items[(int)Settings.Instance.StartMenuPowerButtonAction];
            }
        }

        private void CboStartMenu7PowerButton_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cboStartMenu7PowerButton.SelectedItem == null && cboStartMenu7PowerButton.Items.Count > (int)Settings.Instance.Windows7PowerButtonAction)
            {
                cboStartMenu7PowerButton.SelectedValue = cboStartMenu7PowerButton.Items[(int)Settings.Instance.Windows7PowerButtonAction];
            }
        }

        private void CboStartMenuStyle_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cboStartMenuStyle.SelectedItem == null && cboStartMenuStyle.Items.Count > (int)Settings.Instance.StartMenuStyle)
            {
                cboStartMenuStyle.SelectedValue = cboStartMenuStyle.Items[(int)Settings.Instance.StartMenuStyle];
            }
        }

        private void ClearStartMenuRecent_OnClick(object sender, RoutedEventArgs e)
        {
            RetroBar.StartMenu.StartMenuManager.Instance?.ClearRecentPrograms();
        }

        private void CboClockAction_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cboClockAction.SelectedItem == null)
            {
                cboClockAction.SelectedValue = cboClockAction.Items[(int)Settings.Instance.ClockClickAction];
            }
        }

        private void CboTaskWheelAction_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cboTaskWheelAction.SelectedItem == null)
            {
                cboTaskWheelAction.SelectedValue = cboTaskWheelAction.Items[(int)Settings.Instance.TaskWheelAction];
            }
        }

        private void CboRowCount_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cboRowCount.SelectedItem == null)
            {
                cboRowCount.SelectedValue = cboRowCount.Items[Settings.Instance.RowCount - 1];
            }
        }

        private void CboWinNumHotkeysAction_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (this != _instance)
            {
                // Don't pop a message box if we're closed but haven't been GC'd yet
                return;
            }
            if (cboWinNumHotkeysAction.SelectedItem == null)
            {
                cboWinNumHotkeysAction.SelectedValue = cboWinNumHotkeysAction.Items[(int)Settings.Instance.WinNumHotkeysAction];
            }
            else if (e.RemovedItems.Count > 0 && e.RemovedItems[0] == cboWinNumHotkeysAction.Items[0])
            {
                System.Windows.MessageBox.Show((string)System.Windows.Application.Current.FindResource("hotkey_warning_text"), (string)System.Windows.Application.Current.FindResource("hotkey_warning_title"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CustomizeNotifications_OnClick(object sender, RoutedEventArgs e)
        {
            OpenCustomizeNotifications();
        }

        public void OpenCustomizeNotifications()
        {
            NotificationPropertiesWindow.Open(_notificationArea, new Point(Left, Top));
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            ShellHelper.ExecuteProcess(e.Uri.AbsoluteUri);
            e.Handled = true;
        }

        private void OpenCustomThemesFolder_OnClick(object sender, RoutedEventArgs e)
        {
            string path = _dictionaryManager.GetThemeInstallDir();
            Directory.CreateDirectory(path);
            ShellHelper.StartProcess(path);
        }

        private void ClockFormatHelpButton_Click(object sender, RoutedEventArgs e)
        {
            ShellHelper.ExecuteProcess("https://learn.microsoft.com/dotnet/standard/base-types/custom-date-and-time-format-strings#table");
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/PropertiesWindow.xaml.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/NotificationPropertiesWindow.xaml.cs">
namespace RetroBar
{
    using ManagedShell.WindowsTray;
    using RetroBar.Extensions;
    using RetroBar.Utilities;
    using System.Windows;
    using System.Windows.Controls;

    /// <summary>
    /// Interaction logic for NotificationPropertiesWindow.xaml
    /// </summary>
    public partial class NotificationPropertiesWindow : Window
    {
        private static NotificationPropertiesWindow _instance;

        private readonly NotificationArea _notificationArea;

        private NotificationPropertiesWindow(NotificationArea notificationArea)
        {
            _notificationArea = notificationArea;

            InitializeComponent();

            DataContext = _notificationArea;
            Settings.Instance.PropertyChanged += Settings_PropertyChanged;
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.InvertNotifyIcons) || e.PropertyName == nameof(Settings.NotifyIconBehaviors))
            {
                // Reload icons
                DataContext = null;
                DataContext = _notificationArea;
            }
        }

        public static void Open(NotificationArea notificationArea, Point position)
        {
            if (_instance == null)
            {
                _instance = new NotificationPropertiesWindow(notificationArea);
                _instance.Left = position.X + 10;
                _instance.Top = position.Y + 10;
                _instance.Show();
            }
            else
            {
                _instance.Activate();
            }
        }

        private void OK_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
            _instance = null;
        }

        private void BehaviorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && 
                (sender as FrameworkElement).DataContext is NotifyIcon notifyIcon && 
                e.AddedItems[0] is ComboBoxItem selected && 
                selected.Tag is NotifyIconBehavior behavior)
            {
                if (notifyIcon.GetBehavior() == behavior)
                {
                    // Same setting selected; do nothing
                    return;
                }

                notifyIcon.SetBehavior(behavior);
            }
        }

        private void InvertCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;
            var icon = checkBox.DataContext as NotifyIcon;
            icon.SetCanInvert(true);
        }

        private void InvertCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;
            var icon = checkBox.DataContext as NotifyIcon;
            icon.SetCanInvert(false);
        }

        private void Icon_Loaded(object sender, RoutedEventArgs e)
        {
            var image = sender as Image;
            var notifyIcon = image.DataContext as NotifyIcon;
            applyEffects(image, notifyIcon);
        }

        private void applyEffects(Image NotifyIconImage, NotifyIcon TrayIcon)
        {
            if (TrayIcon == null)
            {
                return;
            }

            if (!TrayIcon.CanInvert())
            {
                NotifyIconImage.Effect = null;
                return;
            }

            if (NotifyIconImage.Effect == null)
            {
                NotifyIconImage.Effect = new InvertEffect();
            }
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/NotificationPropertiesWindow.xaml.cs">

// [5500] VALUE CONVERTERS :: IValueConverter classes used from XAML

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/BoolToIntConverter.cs">
namespace RetroBar.Converters
{
    using System;
    using System.Windows.Data;

    [ValueConversion(typeof(bool), typeof(int))]
    public class BoolToIntConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                var multiplier = 1;
                if (parameter is string strValue)
                {
                    multiplier = System.Convert.ToInt32(strValue);
                }
                return boolValue ? 1 * multiplier : 0;
            }

            return Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is int intValue)
            {
                return intValue == 1 ? true : false;
            }

            return Binding.DoNothing;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/BoolToIntConverter.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/BoolToInvertedBoolConverter.cs">
namespace RetroBar.Converters
{
    using System;
    using System.Windows;
    using System.Windows.Data;

    [ValueConversion(typeof(bool), typeof(bool))]
    public class BoolToInvertedBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }

            return Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/BoolToInvertedBoolConverter.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/BoolToInvertedVisibilityConverter.cs">
namespace RetroBar.Converters
{
    using System;
    using System.Windows;
    using System.Windows.Data;

    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class BoolToInvertedVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }

            return Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/BoolToInvertedVisibilityConverter.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/BoolToTextRenderingModeConverter.cs">
namespace RetroBar.Converters
{
    using System;
    using System.Windows.Data;
    using System.Windows.Media;

    [ValueConversion(typeof(bool), typeof(TextRenderingMode))]
    public class BoolToTextRenderingModeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? TextRenderingMode.Auto : TextRenderingMode.Aliased;
            }

            return Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/BoolToTextRenderingModeConverter.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/BoolToVisibilityConverter.cs">
namespace RetroBar.Converters
{
    using System;
    using System.Windows;
    using System.Windows.Data;

    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }

            return Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/BoolToVisibilityConverter.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/CountToIndexConverter.cs">
namespace RetroBar.Converters
{
    using System;
    using System.Globalization;
    using System.Windows.Data;

    public class CountToIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (int)value - 1;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (int)value + 1;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/CountToIndexConverter.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/CultureInfoToLocaleNameConverter.cs">
namespace RetroBar.Converters
{
    using System;
    using System.Globalization;
    using System.Windows;
    using System.Windows.Data;

    [ValueConversion(typeof(CultureInfo), typeof(string))]
    public class CultureInfoToLocaleNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is CultureInfo cultureInfo)
            {
                switch (parameter)
                { 
                    case "TwoLetterIsoLanguageName": return cultureInfo.TwoLetterISOLanguageName.ToUpper();
                    case "EnglishName": return cultureInfo.EnglishName;
                    case null: return Binding.DoNothing;
                }
            }

            return Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/CultureInfoToLocaleNameConverter.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/DockOrientationConverter.cs">
namespace RetroBar.Converters
{
    using System;
    using System.Windows.Controls;
    using System.Windows.Data;
    using ManagedShell.AppBar;
    using RetroBar.Utilities;

    [ValueConversion(typeof(bool), typeof(Orientation))]
    public class DockOrientationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            // parameter is a string "leading" or "trailing"

            if (parameter is string position && value is Orientation orientation)
            {
                if (orientation == Orientation.Vertical)
                {
                    if (position == "leading")
                    {
                        return Dock.Top;
                    }
                    else
                    {
                        return Dock.Bottom;
                    }
                }
                else
                {
                    if (position == "leading")
                    {
                        return Dock.Left;
                    }
                    else
                    {
                        return Dock.Right;
                    }
                }
            }

            return Dock.Left;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/DockOrientationConverter.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/DoubleToPercentConverter.cs">
namespace RetroBar.Converters
{
    using System;
    using System.Windows;
    using System.Windows.Data;

    [ValueConversion(typeof(double), typeof(string))]
    public class DoubleToPercentConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (values[0] is double doubleValue)
            {
                return string.Format((string)Application.Current.FindResource("taskbar_scale_current"), (int)Math.Round(doubleValue * 100));
            }

            return Binding.DoNothing;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/DoubleToPercentConverter.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/EdgeOrientationConverter.cs">
namespace RetroBar.Converters
{
    using System;
    using System.Windows.Controls;
    using System.Windows.Data;
    using ManagedShell.AppBar;
    using RetroBar.Utilities;

    [ValueConversion(typeof(bool), typeof(Orientation))]
    public class EdgeOrientationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (Settings.Instance.Edge == AppBarEdge.Left || Settings.Instance.Edge == AppBarEdge.Right)
            {
                return Orientation.Vertical;
            }
            else
            {
                return Orientation.Horizontal;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/EdgeOrientationConverter.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/EdgeOrientationVisibilityConverter.cs">
namespace RetroBar.Converters
{
    using ManagedShell.AppBar;
    using System;
    using System.Windows;
    using System.Windows.Data;

    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class EdgeOrientationVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is AppBarEdge edge && parameter is string visibleOrientation)
            {
                return ((edge == AppBarEdge.Left || edge == AppBarEdge.Right) && visibleOrientation == "vertical") ||
                    ((edge == AppBarEdge.Top || edge == AppBarEdge.Bottom) && visibleOrientation == "horizontal") ? Visibility.Visible : Visibility.Collapsed;
            }

            return Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/EdgeOrientationVisibilityConverter.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/EnumConverter.cs">
namespace RetroBar.Converters
{
    using System;
    using System.Globalization;
    using System.Windows.Data;

    public class EnumConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (int)value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Enum.ToObject(targetType, value);
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/EnumConverter.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/IconListOrientation.cs">
namespace RetroBar.Converters
{
    using ManagedShell.AppBar;
    using RetroBar.Utilities;
    using System.Windows.Controls;
    using System;
    using System.Windows.Data;
    using System.Globalization;

    public class IconListOrientation : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool horizontal = Settings.Instance.Edge is AppBarEdge.Top or AppBarEdge.Bottom;
            int rows = Settings.Instance.RowCount;

            if (horizontal && rows > 1)
            {
                return Orientation.Vertical;
            }

            return Orientation.Horizontal;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/IconListOrientation.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/NewLineToSpaceConverter.cs">
namespace RetroBar.Converters
{
    using System;
    using System.Windows.Data;

    [ValueConversion(typeof(string), typeof(string))]
    public class NewLineToSpaceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string text)
            {
                return text.Replace('\n', ' ').Replace('\r', ' ');
            }

            return Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/NewLineToSpaceConverter.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/NotifyIconBehaviorConverter.cs">
namespace RetroBar.Converters
{
    using ManagedShell.WindowsTray;
    using RetroBar.Extensions;
    using System;
    using System.Windows.Data;

    [ValueConversion(typeof(NotifyIcon), typeof(int))]
    public class NotifyIconBehaviorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is NotifyIcon icon)
            {
                return (int)icon.GetBehavior();
            }

            return Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/NotifyIconBehaviorConverter.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/NotifyIconCanInvertConverter.cs">
namespace RetroBar.Converters
{
    using ManagedShell.WindowsTray;
    using RetroBar.Extensions;
    using System;
    using System.Windows.Data;

    [ValueConversion(typeof(NotifyIcon), typeof(bool))]
    public class NotifyIconCanInvertConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is NotifyIcon icon)
            {
                return icon.CanInvert();
            }

            return Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/NotifyIconCanInvertConverter.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/SettingsToTextFormattingModeConverter.cs">
namespace RetroBar.Converters
{
    using System;
    using System.Windows.Data;
    using System.Windows.Media;

    [ValueConversion(typeof(bool), typeof(TextFormattingMode))]
    public class SettingsToTextFormattingModeConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (values[0] is double scale && values[1] is bool)
            {
                // Display formatting snaps glyphs to the pixel grid at the unscaled size, so any scale transform then
                // stretches already-snapped glyphs: blurred, clipped text. Scaled, text is laid out at its final size
                // (Ideal), with or without font smoothing; font smoothing itself is TextRenderingMode's
                return Math.Abs(scale - 1.0) > 0.001 ? TextFormattingMode.Ideal : TextFormattingMode.Display;
            }

            return Binding.DoNothing;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/SettingsToTextFormattingModeConverter.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/TaskButtonStyleConverter.cs">
namespace RetroBar.Converters
{
    using System;
    using System.Windows;
    using System.Windows.Data;
    using ManagedShell.WindowsTasks;

    [ValueConversion(typeof(bool), typeof(Style))]
    public class TaskButtonStyleConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (!(values[0] is FrameworkElement fxElement))
            {
                return null;
            }

            if (fxElement.ContextMenu?.IsOpen == true)
            {
                // Always show as active with an open context menu
                return fxElement.FindResource("TaskButtonActive");
            }

            // Default style is Inactive...
            var fxStyle = fxElement.FindResource("TaskButton");

            if (values[1] is ApplicationWindow.WindowState state)
            {
                switch (state)
                {
                    case ApplicationWindow.WindowState.Active:
                        fxStyle = fxElement.FindResource("TaskButtonActive");
                        break;

                    case ApplicationWindow.WindowState.Flashing:
                        fxStyle = fxElement.FindResource("TaskButtonFlashing");
                        break;
                }
            }

            return fxStyle;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/TaskButtonStyleConverter.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/TaskButtonWidthConverter.cs">
namespace RetroBar.Converters
{
    using System;
    using System.Windows.Data;

    // Computes an individual task button width so a full row fills the taskbar exactly.
    // Every button gets the base (floored) width; the pixels lost to flooring are spread
    // evenly across the row's columns, each affected column getting one extra pixel.
    //
    // Distribution is keyed on the column (index % buttonsPerRow) so widths line up in
    // columns across rows, and a partial last row keeps the same per-column widths.
    //
    // values[0] = item index (ItemsControl.AlternationIndex)
    // values[1] = base button width (double)
    // values[2] = number of columns that get an extra pixel (int)
    // values[3] = buttons per row (int)
    //
    // If the index isn't available the base width is returned, so the layout simply falls
    // back to uniform widths rather than breaking.
    public class TaskButtonWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (values.Length < 4 || !(values[1] is double baseWidth))
            {
                return Binding.DoNothing;
            }

            if (values[0] is int index
                && values[2] is int extraCount
                && values[3] is int buttonsPerRow
                && extraCount > 0
                && buttonsPerRow > 0)
            {
                int column = index % buttonsPerRow;

                // Evenly spaced "+1"s: 1 when this column falls on a distribution boundary.
                int extraForColumn = (column + 1) * extraCount / buttonsPerRow - column * extraCount / buttonsPerRow;

                return baseWidth + extraForColumn;
            }

            return baseWidth;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/TaskButtonWidthConverter.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/TaskLabelConverter.cs">
namespace RetroBar.Converters
{
    using ManagedShell.Interop;
    using ManagedShell.WindowsTasks;
    using System;
    using System.Windows.Data;

    [ValueConversion(typeof(ApplicationWindow), typeof(string))]
    public class TaskLabelConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (!(values[0] is string title &&
                values[1] is int progressValue &&
                values[2] is NativeMethods.TBPFLAG progressState))
            {
                return Binding.DoNothing;
            }

            if (progressState == NativeMethods.TBPFLAG.TBPF_NOPROGRESS ||
                progressState == NativeMethods.TBPFLAG.TBPF_INDETERMINATE ||
                progressValue < 0)
            {
                return title;
            }

            if (title.Contains("%"))
            {
                // Window title may already contain progress percentage
                return title;
            }

            return $"[{Math.Floor(progressValue / 65534.0 * 100)}%] {title}";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/TaskLabelConverter.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/ToolTipHorizontalOffsetConverter.cs">
namespace RetroBar.Converters
{
    using ManagedShell.AppBar;
    using RetroBar.Utilities;
    using System;
    using System.Globalization;
    using System.Linq;
    using System.Windows;
    using System.Windows.Data;

    [ValueConversion(typeof(double), typeof(double))]
    public class ToolTipHorizontalOffsetConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.FirstOrDefault(v => v == DependencyProperty.UnsetValue) != null)
            {
                return double.NaN;
            }

            if (Settings.Instance.Edge == AppBarEdge.Left || Settings.Instance.Edge == AppBarEdge.Right)
            {
                return (double)0;
            }
            else
            {
                double placementTargetWidth = (double)values[0];
                double toolTipWidth = (double)values[1];
                return (placementTargetWidth / 2.0) - (toolTipWidth / 2.0 / (double)values[2]);
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/ToolTipHorizontalOffsetConverter.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/ToolTipPlacementConverter.cs">
namespace RetroBar.Converters
{
    using System;
    using System.Windows.Controls.Primitives;
    using System.Windows.Data;
    using ManagedShell.AppBar;
    using RetroBar.Utilities;

    [ValueConversion(typeof(bool), typeof(PlacementMode))]
    public class ToolTipPlacementConverter : IMultiValueConverter
    {
        public object Convert(object[] value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (Settings.Instance.Edge == AppBarEdge.Left)
            {
                return PlacementMode.Right;
            }
            else if (Settings.Instance.Edge == AppBarEdge.Right)
            {
                return PlacementMode.Left;
            }
            else if (Settings.Instance.Edge == AppBarEdge.Top)
            {
                return PlacementMode.Bottom;
            }
            else
            {
                return PlacementMode.Top;
            }
        }

        public object[] ConvertBack(object value, Type[] targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/ToolTipPlacementConverter.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Converters/ToolTipVerticalOffsetConverter.cs">
namespace RetroBar.Converters
{
    using ManagedShell.AppBar;
    using RetroBar.Utilities;
    using System;
    using System.Globalization;
    using System.Linq;
    using System.Windows;
    using System.Windows.Data;

    [ValueConversion(typeof(double), typeof(double))]
    public class ToolTipVerticalOffsetConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.FirstOrDefault(v => v == DependencyProperty.UnsetValue) != null)
            {
                return double.NaN;
            }

            if (Settings.Instance.Edge == AppBarEdge.Left || Settings.Instance.Edge == AppBarEdge.Right)
            {
                double placementTargetHeight = (double)values[0];
                double toolTipHeight = (double)values[1];
                return (placementTargetHeight / 2.0) - (toolTipHeight / 2.0 / (double)values[2]);
            }
            else
            {
                return (double)0;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/Converters/ToolTipVerticalOffsetConverter.cs">

// [6000] START MENU WINDOWS :: the Vista Start menu and its user picture

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/StartMenu/VistaStartMenu.xaml.cs">
namespace RetroBar.StartMenu
{
    using ManagedShell.AppBar;
    using ManagedShell.Common.Helpers;
    using ManagedShell.Common.Logging;
    using Microsoft.Win32;
    using RetroBar.Superbar;
    using RetroBar.Utilities;
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.IO;
    using System.Linq;
    using System.Security.Principal;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Controls.Primitives;
    using System.Windows.Input;
    using System.Windows.Interop;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;
    using System.Windows.Threading;

    /// <summary>
    /// A replica of the Windows Vista Start menu, and of Windows 7's when the Windows 7 style is chosen,
    /// backed by the Windows 10/11 app list and the local Windows Search index.
    /// </summary>
    internal partial class VistaStartMenu : Window
    {
        private enum MenuView
        {
            Home,
            AllPrograms,
            Search
        }

        private const int MaxProgramResults = 8;
        private const int MaxFileResults = 8;
        private const int MaxStoredRecentPrograms = 30;

        #region Dependency properties

        public static readonly DependencyProperty IsGlassProperty =
            DependencyProperty.Register(nameof(IsGlass), typeof(bool), typeof(VistaStartMenu), new PropertyMetadata(true, OnIsGlassChanged));

        /// <summary>Whether the Aero glass look is used; otherwise the opaque Vista Basic look.</summary>
        public bool IsGlass
        {
            get => (bool)GetValue(IsGlassProperty);
            set => SetValue(IsGlassProperty, value);
        }

        public static readonly DependencyProperty GlassTintOpacityProperty =
            DependencyProperty.Register(nameof(GlassTintOpacity), typeof(double), typeof(VistaStartMenu), new PropertyMetadata(1.0));

        /// <summary>
        /// Opacity of the frame's glass tint (FrameTint): AeroGlassHelper.TintOpacity while the glass is on, otherwise 1.
        /// </summary>
        public double GlassTintOpacity
        {
            get => (double)GetValue(GlassTintOpacityProperty);
            set => SetValue(GlassTintOpacityProperty, value);
        }

        public static readonly DependencyProperty IsWindows7Property =
            DependencyProperty.Register(nameof(IsWindows7), typeof(bool), typeof(VistaStartMenu), new PropertyMetadata(false));

        /// <summary>Whether the menu is drawn as Windows 7's; otherwise as Windows Vista's.</summary>
        public bool IsWindows7
        {
            get => (bool)GetValue(IsWindows7Property);
            set => SetValue(IsWindows7Property, value);
        }

        public static readonly DependencyProperty ProgramIconSizeProperty =
            DependencyProperty.Register(nameof(ProgramIconSize), typeof(double), typeof(VistaStartMenu), new PropertyMetadata(32.0));

        public double ProgramIconSize
        {
            get => (double)GetValue(ProgramIconSizeProperty);
            set => SetValue(ProgramIconSizeProperty, value);
        }

        public static readonly DependencyProperty ProgramRowHeightProperty =
            DependencyProperty.Register(nameof(ProgramRowHeight), typeof(double), typeof(VistaStartMenu), new PropertyMetadata(38.0));

        public double ProgramRowHeight
        {
            get => (double)GetValue(ProgramRowHeightProperty);
            set => SetValue(ProgramRowHeightProperty, value);
        }

        private static void OnIsGlassChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((VistaStartMenu)d).UpdateGlass();
        }

        #endregion

        public ObservableCollection<StartMenuEntry> PinnedPrograms { get; } = new ObservableCollection<StartMenuEntry>();
        public ObservableCollection<StartMenuEntry> RecentPrograms { get; } = new ObservableCollection<StartMenuEntry>();
        public ObservableCollection<StartMenuEntry> AllPrograms { get; } = new ObservableCollection<StartMenuEntry>();
        public ObservableCollection<StartMenuEntry> SearchPrograms { get; } = new ObservableCollection<StartMenuEntry>();
        public ObservableCollection<StartMenuEntry> SearchFiles { get; } = new ObservableCollection<StartMenuEntry>();

        /// <summary>Raised after the menu has been hidden.</summary>
        public event EventHandler MenuClosed;

        public bool IsOpen { get; private set; }
        public Taskbar Host { get; private set; }
        public DateTime LastClosed { get; private set; } = DateTime.MinValue;

        private IntPtr _handle;
        private MenuView _view = MenuView.Home;
        private AppCatalogSnapshot _catalog;
        private CancellationTokenSource _searchCancellation;
        private readonly DispatcherTimer _fileSearchTimer;
        private StartMenuEntry _defaultResult;
        private ImageSource _userPicture;
        private readonly UserPictureWindow _pictureWindow;
        private bool _pictureWindowClosed;
        private int _menuX;
        private int _menuY;

        // The user picture's top-left corner relative to the menu's, in device-independent pixels. In Vista it sits
        // inside the frame, at the top of the right column level with the programs pane, as measured in Vista
        private const double VistaPictureOffsetX = 305;
        private const double VistaPictureOffsetY = 6;

        // Windows 7's frame is 6 px wider and its picture overhangs by 24 px, as measured in Windows 7
        private const double Windows7PictureOffsetX = 308;
        private const double Windows7PictureOffsetY = -24;

        /// <summary>The Start menu scale setting (1 to 2): the whole menu, its picture and its rounded clip follow it.</summary>
        private static double MenuScale => Math.Clamp(Settings.Instance.StartMenuScale, 1.0, 2.0);

        private static bool IsMenuScaled => Math.Abs(MenuScale - 1.0) > 0.001;

        private double PictureOffsetX => IsWindows7 ? Windows7PictureOffsetX : VistaPictureOffsetX;
        private double PictureOffsetY => IsWindows7 ? Windows7PictureOffsetY : VistaPictureOffsetY;
        private string _homeListSignature;
        private readonly List<(Button button, ImageSource icon)> _rightItems = new List<(Button, ImageSource)>();
        private string _rightColumnSignature;

        public VistaStartMenu()
        {
            InitializeComponent();

            _fileSearchTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _fileSearchTimer.Tick += FileSearchTimer_Tick;

            Left = -32000;
            Top = -32000;

            SearchBox.IsKeyboardFocusWithinChanged += (sender, e) => UpdateDefaultResult();

            _pictureWindow = new UserPictureWindow();
            _pictureWindow.PictureClicked += PictureWindow_PictureClicked;
            _pictureWindow.Closed += (sender, e) => _pictureWindowClosed = true;
        }

        #region Opening and closing

        public void ShowAt(Taskbar host, AppCatalogSnapshot catalog)
        {
            Host = host;
            ApplySettings();

            if (catalog != null)
            {
                ApplyCatalog(catalog);
            }

            ShowView(MenuView.Home);
            SearchBox.Text = string.Empty;

            Opacity = 0;
            Show();
            UpdateLayout();
            PositionOverTaskbar();
            UpdateWindowRegion();
            UpdateGlass();
            Opacity = 1;

            IsOpen = true;
            BringToForeground();
            ShowUserPicture();
            RaiseStartOrb();

            if (Settings.Instance.StartMenuShowSearch)
            {
                SearchBox.Focus();
                Keyboard.Focus(SearchBox);
            }
            else
            {
                Focus();
            }

            _ = RefreshHomeListsAsync();
        }

        public void CloseMenu()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            LastClosed = DateTime.Now;
            CancelSearch();
            _pictureWindow.Hide();
            Hide();
            MenuClosed?.Invoke(this, EventArgs.Empty);
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            CloseMenu();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _pictureWindow.PictureClicked -= PictureWindow_PictureClicked;
            _blurBackdrop?.Dispose();
            _blurBackdrop = null;

            // WPF closes owned windows along with their owner; close it here only if that has not happened
            if (!_pictureWindowClosed)
            {
                _pictureWindow.Close();
            }
        }

        private void ShowUserPicture()
        {
            if (_pictureWindow.Owner == null)
            {
                // Owned by the menu so it always stays above it
                _pictureWindow.Owner = this;
            }

            // The menu always opens with the framed picture, whatever was hovered when it last closed
            _pictureWindow.ShowPicture(false);

            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX * MenuScale;
            _pictureWindow.ShowAt(_menuX + (int)Math.Round(PictureOffsetX * scale), _menuY + (int)Math.Round(PictureOffsetY * scale));
        }

        private void PictureWindow_PictureClicked(object sender, EventArgs e)
        {
            CloseMenu();
            StartMenuLauncher.Start("ms-settings:yourinfo");
        }

        /// <summary>
        /// In Vista the Start orb is drawn over the bottom-left corner of the open menu.
        /// The orb is a separate window, so it is raised above the menu once the menu is shown.
        /// </summary>
        private void RaiseStartOrb()
        {
            IntPtr orb = Host?.StartButton?.FloatingStartHandle ?? IntPtr.Zero;

            if (orb != IntPtr.Zero)
            {
                StartMenuNative.SetWindowPos(orb, StartMenuNative.HWND_TOPMOST, 0, 0, 0, 0,
                    StartMenuNative.SWP_NOMOVE | StartMenuNative.SWP_NOSIZE | StartMenuNative.SWP_NOACTIVATE);
            }
        }

        /// <summary>
        /// The part of the orb drawn over the menu does not take clicks, so a click there reaches the menu.
        /// As in Vista, clicking the orb closes the menu.
        /// </summary>
        private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsCursorOverStartOrb())
            {
                e.Handled = true;
                CloseMenu();
            }
        }

        private bool IsCursorOverStartOrb()
        {
            IntPtr orb = Host?.StartButton?.FloatingStartHandle ?? IntPtr.Zero;

            if (orb == IntPtr.Zero || !StartMenuNative.IsWindowVisible(orb) ||
                !StartMenuNative.GetWindowRect(orb, out StartMenuNative.RECT rect) ||
                !StartMenuNative.GetCursorPos(out StartMenuNative.POINT cursor))
            {
                return false;
            }

            double radius = Math.Min(rect.Right - rect.Left, rect.Bottom - rect.Top) / 2.0;
            double dx = cursor.X - (rect.Left + rect.Right) / 2.0;
            double dy = cursor.Y - (rect.Top + rect.Bottom) / 2.0;

            return dx * dx + dy * dy <= radius * radius;
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            _handle = new WindowInteropHelper(this).Handle;

            // Hide from Alt+Tab and the taskbar
            int exStyle = StartMenuNative.GetWindowLong(_handle, StartMenuNative.GWL_EXSTYLE);
            exStyle = (exStyle | StartMenuNative.WS_EX_TOOLWINDOW) & ~StartMenuNative.WS_EX_APPWINDOW;
            StartMenuNative.SetWindowLong(_handle, StartMenuNative.GWL_EXSTYLE, exStyle);

            WindowHelper.ExcludeWindowFromPeek(_handle);
        }

        private void BringToForeground()
        {
            Activate();

            IntPtr foreground = StartMenuNative.GetForegroundWindow();

            if (foreground == _handle)
            {
                return;
            }

            // When opened with the Windows key another app is in the foreground; borrow its input queue to take focus
            uint foregroundThread = StartMenuNative.GetWindowThreadProcessId(foreground, IntPtr.Zero);
            uint thisThread = StartMenuNative.GetCurrentThreadId();

            if (foregroundThread != 0 && foregroundThread != thisThread)
            {
                StartMenuNative.AttachThreadInput(foregroundThread, thisThread, true);
                StartMenuNative.SetForegroundWindow(_handle);
                StartMenuNative.BringWindowToTop(_handle);
                StartMenuNative.AttachThreadInput(foregroundThread, thisThread, false);
            }
            else
            {
                StartMenuNative.SetForegroundWindow(_handle);
            }

            Activate();
        }

        private void PositionOverTaskbar()
        {
            if (Host == null || _handle == IntPtr.Zero)
            {
                return;
            }

            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int width = (int)Math.Round(ActualWidth * scale);
            int height = (int)Math.Round(ActualHeight * scale);

            var bar = Host.WindowRect;
            var screen = Host.Screen.Bounds;
            bool rightToLeft = Host.FlowDirection == FlowDirection.RightToLeft;

            int x;
            int y;

            switch (Host.AppBarEdge)
            {
                case AppBarEdge.Top:
                    x = rightToLeft ? bar.Right - width : bar.Left;
                    y = bar.Bottom;
                    break;
                case AppBarEdge.Left:
                    x = bar.Right;
                    y = bar.Top;
                    break;
                case AppBarEdge.Right:
                    x = bar.Left - width;
                    y = bar.Top;
                    break;
                default:
                    x = rightToLeft ? bar.Right - width : bar.Left;
                    y = bar.Top - height;
                    break;
            }

            x = Math.Max(screen.Left, Math.Min(x, screen.Right - width));
            y = Math.Max(screen.Top, Math.Min(y, screen.Bottom - height));

            _menuX = x;
            _menuY = y;

            StartMenuNative.SetWindowPos(_handle, StartMenuNative.HWND_TOPMOST, x, y, 0, 0, StartMenuNative.SWP_NOSIZE | StartMenuNative.SWP_NOACTIVATE);
        }

        /// <summary>
        /// Clips the window to the menu's rounded corners. The window is exactly the size of the frame,
        /// so this only affects the few pixels outside each rounded corner.
        /// </summary>
        private void UpdateWindowRegion()
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            IntPtr frameRegion = CreateRoundRegion(Frame, 6 * MenuScale, scale);

            if (frameRegion == IntPtr.Zero)
            {
                return;
            }

            // On success the system owns the region
            if (StartMenuNative.SetWindowRgn(_handle, frameRegion, true) == 0)
            {
                StartMenuNative.DeleteObject(frameRegion);
            }
        }

        private IntPtr CreateRoundRegion(FrameworkElement element, double radius, double scale)
        {
            if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
            {
                return IntPtr.Zero;
            }

            // Bounds in the window's coordinates, so a scaled frame gives a scaled region
            Rect bounds = element.TransformToAncestor(this).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            int left = (int)Math.Round(bounds.Left * scale);
            int top = (int)Math.Round(bounds.Top * scale);
            int right = (int)Math.Round(bounds.Right * scale);
            int bottom = (int)Math.Round(bounds.Bottom * scale);
            int diameter = (int)Math.Round(radius * 2 * scale);

            return StartMenuNative.CreateRoundRectRgn(left, top, right + 1, bottom + 1, diameter, diameter);
        }

        private void UpdateGlass()
        {
            GlassTintOpacity = IsGlass ? AeroGlassHelper.TintOpacity : 1.0;

            if (IsGlass && _handle != IntPtr.Zero && IsVisible)
            {
                AeroGlassHelper.PrepareGlassWindow(_handle);
            }

            // The adjustable blur beneath the glass (GlassBlurBackdrop), shaped like the menu's rounded frame
            double blur = IsGlass ? AeroGlassHelper.BlurStrength : 0;

            if (blur > 0 && _blurBackdrop == null && _handle != IntPtr.Zero)
            {
                _blurBackdrop = new GlassBlurBackdrop(_handle);
            }

            _blurBackdrop?.SetStrength(blur);
        }

        private GlassBlurBackdrop _blurBackdrop;

        #endregion

        #region Settings and content

        private void ApplySettings()
        {
            IsGlass = Settings.Instance.EnableAeroGlass;
            IsWindows7 = Settings.Instance.StartMenuStyle == StartMenuStyleOption.Windows7;
            ApplyLayout();
            ApplyScale();

            bool largeIcons = Settings.Instance.StartMenuLargeIcons;
            ProgramIconSize = largeIcons ? 32 : 16;
            ProgramRowHeight = largeIcons ? 38 : 24;

            SearchBoxBorder.Visibility = Settings.Instance.StartMenuShowSearch ? Visibility.Visible : Visibility.Hidden;

            if (IsWindows7)
            {
                ShutDownText.Text = Settings.Instance.Windows7PowerButtonAction switch
                {
                    StartMenuPowerAction.Sleep => FindString("start_menu_sleep", "Sleep"),
                    StartMenuPowerAction.Hibernate => FindString("start_menu_hibernate", "Hibernate"),
                    _ => FindString("start_menu7_shut_down", "Shut down")
                };
            }

            // The power button is red whatever it does; the tooltip names the action
            PowerButton.Background = (Brush)FindResource("PowerShutDownBackground");
            PowerButton.ToolTip = Settings.Instance.StartMenuPowerButtonAction switch
            {
                StartMenuPowerAction.ShutDown => FindString("start_menu_shut_down", "Shut Down"),
                StartMenuPowerAction.Hibernate => FindString("start_menu_hibernate", "Hibernate"),
                _ => FindString("start_menu_sleep", "Sleep")
            };

            LockButton.ToolTip = FindString("start_menu_lock", "Lock");

            LoadUserPicture();
            BuildRightColumn();
        }

        /// <summary>
        /// Sizes and arranges the menu as Windows Vista's or Windows 7's. Every value is set for both styles,
        /// so switching back restores Vista's measured layout exactly.
        /// </summary>
        private void ApplyLayout()
        {
            bool win7 = IsWindows7;
            bool glass = IsGlass;

            Frame.Width = win7 ? 411 : 405;
            // Vista's frame is 30 px taller than Windows 7's measure suggests, for the picture inside it above the right column
            Frame.Height = win7 ? 476 : 538;
            FrameHighlight.BorderBrush = new SolidColorBrush(win7 ? (glass ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)) : Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));

            LeftPane.Height = win7 ? 462 : 494;
            LeftPane.Margin = win7 ? new Thickness(6, 6, 0, 6) : new Thickness(6, 6, 0, 0);
            LeftPane.BorderBrush = new SolidColorBrush(win7 ? (glass ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xFF, 0xF4, 0xF8, 0xFC)) : Color.FromArgb(0xFF, 0xA0, 0xA2, 0x9E));
            LeftPaneInner.BorderBrush = new SolidColorBrush(win7 ? (glass ? Color.FromArgb(0xCC, 0x3A, 0x4E, 0x58) : Color.FromArgb(0xFF, 0x7F, 0x93, 0xAB)) : Color.FromArgb(0xFF, 0x3A, 0x3B, 0x38));
            LeftListRow.Height = new GridLength(win7 ? 381 : 459);

            // Vista's first link is centred 90 px below the frame's top, under the picture; Windows 7's picture overhangs
            RightColumn.Margin = win7 ? new Thickness(3, 43, 6, 0) : new Thickness(3, 73, 6, 0);

            AllProgramsButton.Margin = win7 ? new Thickness(0, 3, 0, 3) : new Thickness(0, 3, 0, 0);
            AllProgramsText.FontWeight = win7 ? FontWeights.Normal : FontWeights.Bold;

            // The search box moves into the pane's band in Windows 7, and below the pane in Vista
            Panel searchParent = win7 ? SearchBandHost : LayoutGrid;

            if (SearchBoxBorder.Parent != searchParent)
            {
                (SearchBoxBorder.Parent as Panel)?.Children.Remove(SearchBoxBorder);
                searchParent.Children.Add(SearchBoxBorder);
            }

            Grid.SetColumn(SearchBoxBorder, 0);
            Grid.SetRow(SearchBoxBorder, win7 ? 0 : 1);
            SearchBand.Visibility = win7 ? Visibility.Visible : Visibility.Collapsed;
            SearchBoxBorder.Margin = win7 ? new Thickness(12, 12, 0, 0) : new Thickness(6, 6, 0, 6);
            SearchBoxBorder.Width = win7 ? 222 : 254;
            SearchBoxBorder.Height = win7 ? 22 : 24;
            SearchBoxBorder.HorizontalAlignment = win7 ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            SearchBoxBorder.VerticalAlignment = win7 ? VerticalAlignment.Top : VerticalAlignment.Stretch;
            SearchBoxBorder.BorderBrush = win7 ? (Brush)FindResource("Win7SearchBoxBorder") : new SolidColorBrush(Color.FromArgb(0xFF, 0x6A, 0x6E, 0x68));
            SearchBoxInner.BorderThickness = new Thickness(win7 ? 0 : 1);
            SearchBox.Padding = win7 ? new Thickness(6, 0, 22, 0) : new Thickness(1, 0, 22, 0);
            SearchPlaceholder.Margin = win7 ? new Thickness(8, 0, 22, 0) : new Thickness(4, 0, 22, 0);
            SearchPlaceholder.SetResourceReference(TextBlock.TextProperty, win7 ? "start_menu7_search_placeholder" : "start_menu_search_placeholder");

            PowerStrip.Visibility = win7 ? Visibility.Collapsed : Visibility.Visible;
            ShutDownGroup.Visibility = win7 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Zooms the whole menu by the Start menu scale. Scaled, text is laid out at its final size (Ideal formatting)
        /// so it stays sharp; at 100% it keeps the pixel-grid formatting (Display) the menu was measured with.
        /// </summary>
        private void ApplyScale()
        {
            double menuScale = MenuScale;

            Frame.LayoutTransform = IsMenuScaled ? new ScaleTransform(menuScale, menuScale) : Transform.Identity;
            TextOptions.SetTextFormattingMode(this, IsMenuScaled ? TextFormattingMode.Ideal : TextFormattingMode.Display);
            _pictureWindow.SetScale(menuScale);
        }

        public void ApplyCatalog(AppCatalogSnapshot catalog)
        {
            if (catalog == null || ReferenceEquals(catalog, _catalog))
            {
                return;
            }

            _catalog = catalog;

            AllPrograms.Clear();

            foreach (StartMenuEntry entry in catalog.AllProgramsRoot)
            {
                AllPrograms.Add(entry);
            }

            _homeListSignature = null;

            if (IsOpen)
            {
                _ = RefreshHomeListsAsync();

                if (_view == MenuView.Search)
                {
                    UpdateProgramResults(SearchBox.Text);
                    UpdateResultHeaders(_fileSearchTimer.IsEnabled || _searchCancellation != null);
                }
            }
        }

        /// <summary>
        /// Rebuilds the pinned and recent lists. Icons are loaded off the UI thread.
        /// </summary>
        public async Task RefreshHomeListsAsync()
        {
            AppCatalogSnapshot catalog = _catalog;
            List<string> pinned = new List<string>(Settings.Instance.StartMenuPinnedPrograms ?? new List<string>());
            List<string> recent = new List<string>(Settings.Instance.StartMenuRecentPrograms ?? new List<string>());
            var hidden = new HashSet<string>(Settings.Instance.StartMenuHiddenPrograms ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            int recentCount = Settings.Instance.StartMenuRecentCount;
            bool useWindowsUsage = Settings.Instance.StartMenuUseWindowsUsage;
            string internetLabel = FindString("start_menu_internet", "Internet");
            string emailLabel = FindString("start_menu_email", "E-mail");

            // Windows 7 shows pinned programs by name, without Vista's bold Internet and E-mail headings
            bool programNames = IsWindows7;

            string signature = string.Join("|", pinned) + "#" + string.Join("|", recent) + "#" + string.Join("|", hidden) + "#" + recentCount + useWindowsUsage + programNames + ProgramRowHeight + (catalog?.LoadedAt.Ticks ?? 0);

            if (signature == _homeListSignature)
            {
                return;
            }

            _homeListSignature = signature;

            (List<StartMenuEntry> pinnedEntries, List<StartMenuEntry> recentEntries) = await StaTask.Run(() =>
            {
                var pinnedResult = new List<StartMenuEntry>();
                var pinnedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (string key in pinned)
                {
                    StartMenuEntry entry = key switch
                    {
                        StartMenuLauncher.DefaultBrowserKey => StartMenuLauncher.CreateDefaultProgramEntry(key, internetLabel, programNames),
                        StartMenuLauncher.DefaultEmailKey => StartMenuLauncher.CreateDefaultProgramEntry(key, emailLabel, programNames),
                        _ => ResolveProgram(catalog, key)
                    };

                    if (entry != null && pinnedKeys.Add(key))
                    {
                        pinnedResult.Add(entry);
                    }
                }

                var recentResult = new List<StartMenuEntry>();
                var seen = new HashSet<string>(pinnedKeys, StringComparer.OrdinalIgnoreCase);
                IEnumerable<string> candidates = useWindowsUsage && catalog != null ? recent.Concat(catalog.FrequentKeys) : recent;

                foreach (string key in candidates)
                {
                    if (recentResult.Count >= recentCount)
                    {
                        break;
                    }

                    if (hidden.Contains(key) || !seen.Add(key))
                    {
                        continue;
                    }

                    StartMenuEntry entry = ResolveProgram(catalog, key);

                    if (entry != null)
                    {
                        recentResult.Add(entry);
                    }
                }

                return (pinnedResult, recentResult);
            });

            if (signature != _homeListSignature)
            {
                // A newer refresh has started
                return;
            }

            if (IsWindows7)
            {
                // Windows 7's program list has a fixed height: keep the rows that fit, dropping recent programs first
                double separator = pinnedEntries.Count > 0 && recentEntries.Count > 0 ? 9 : 0;
                int rows = Math.Max(0, (int)Math.Floor((LeftListRow.Height.Value - separator) / Math.Max(1, ProgramRowHeight)));
                int recentRows = Math.Max(0, rows - pinnedEntries.Count);

                if (pinnedEntries.Count > rows)
                {
                    pinnedEntries = pinnedEntries.Take(rows).ToList();
                }

                if (recentEntries.Count > recentRows)
                {
                    recentEntries = recentEntries.Take(recentRows).ToList();
                }
            }

            ReplaceItems(PinnedPrograms, pinnedEntries);
            ReplaceItems(RecentPrograms, recentEntries);

            PinnedSeparator.Visibility = PinnedPrograms.Count > 0 && RecentPrograms.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Resolves a pinned or recent key to an entry with a large icon. Runs on an STA thread.
        /// </summary>
        private static StartMenuEntry ResolveProgram(AppCatalogSnapshot catalog, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            if (key.StartsWith(StartMenuEntry.AppPrefix, StringComparison.OrdinalIgnoreCase))
            {
                StartMenuEntry app = catalog?.FindByKey(key);

                if (app == null)
                {
                    return null;
                }

                ImageSource icon = app.Kind == StartMenuEntryKind.App
                    ? ShellIcons.FromParsingName("shell:AppsFolder\\" + app.Target, ShellIconSize.Large)
                    : ShellIcons.FromPath(app.Target, ShellIconSize.Large);

                return app.CloneWithIcon(icon);
            }

            if (key.StartsWith(StartMenuEntry.FilePrefix, StringComparison.OrdinalIgnoreCase))
            {
                string path = key.Substring(StartMenuEntry.FilePrefix.Length);

                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    return null;
                }

                return new StartMenuEntry
                {
                    Kind = StartMenuEntryKind.File,
                    Target = path,
                    Name = Path.GetFileNameWithoutExtension(path),
                    Icon = ShellIcons.FromPath(path, ShellIconSize.Large)
                };
            }

            return null;
        }

        private static void ReplaceItems(ObservableCollection<StartMenuEntry> collection, List<StartMenuEntry> items)
        {
            collection.Clear();

            foreach (StartMenuEntry item in items)
            {
                collection.Add(item);
            }
        }

        private void LoadUserPicture()
        {
            if (_userPicture == null)
            {
                _userPicture = LoadAccountPicture();
            }

            _pictureWindow.Source = _userPicture;
        }

        private static ImageSource LoadAccountPicture()
        {
            var candidates = new List<string>();

            try
            {
                string sid = WindowsIdentity.GetCurrent().User?.Value;

                if (sid != null)
                {
                    using RegistryKey key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\{sid}", false);

                    foreach (string valueName in new[] { "Image192", "Image240", "Image96", "Image448" })
                    {
                        if (key?.GetValue(valueName) is string path)
                        {
                            candidates.Add(path);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ShellLogger.Debug($"VistaStartMenu: Unable to read the account picture setting: {e.Message}");
            }

            string defaultPictures = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "User Account Pictures");
            candidates.Add(Path.Combine(defaultPictures, "user-192.png"));
            candidates.Add(Path.Combine(defaultPictures, "user.png"));

            foreach (string path in candidates)
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 96;
                    image.UriSource = new Uri(path, UriKind.Absolute);
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
                catch (Exception e)
                {
                    ShellLogger.Debug($"VistaStartMenu: Unable to load account picture {path}: {e.Message}");
                }
            }

            return null;
        }

        #endregion

        #region Right column

        private sealed class RightColumnLink
        {
            public string Label;
            public Action Open;
            public Func<ImageSource> LoadIcon;
        }

        private void BuildRightColumn()
        {
            Settings s = Settings.Instance;
            string signature = string.Join(",", s.StartMenuShowUserFolder, s.StartMenuShowDocuments, s.StartMenuShowPictures, s.StartMenuShowMusic,
                s.StartMenuShowDownloads, s.StartMenuShowSearchLink, s.StartMenuShowRecentItems, s.StartMenuShowComputer, s.StartMenuShowNetwork,
                s.StartMenuShowConnectTo, s.StartMenuShowControlPanel, s.StartMenuShowDefaultPrograms, s.StartMenuShowRun, Settings.Instance.Language,
                IsWindows7, IsGlass, s.StartMenuShowDevicesAndPrinters);

            if (signature == _rightColumnSignature)
            {
                return;
            }

            _rightColumnSignature = signature;

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            string downloads = Path.Combine(userProfile, "Downloads");
            string recentItems = Environment.GetFolderPath(Environment.SpecialFolder.Recent);

            // Grouped as in Vista: personal folders, search and recent items, computer and network, settings
            var groups = new List<List<RightColumnLink>>
            {
                new List<RightColumnLink>(),
                new List<RightColumnLink>(),
                new List<RightColumnLink>(),
                new List<RightColumnLink>()
            };

            if (s.StartMenuShowUserFolder)
                groups[0].Add(FolderLink(Environment.UserName, userProfile));
            if (s.StartMenuShowDocuments)
                groups[0].Add(FolderLink(FindString("start_menu_documents", "Documents"), documents));
            if (s.StartMenuShowPictures)
                groups[0].Add(FolderLink(FindString("start_menu_pictures", "Pictures"), pictures));
            if (s.StartMenuShowMusic)
                groups[0].Add(FolderLink(FindString("start_menu_music", "Music"), music));
            if (s.StartMenuShowDownloads)
                groups[0].Add(new RightColumnLink
                {
                    Label = FindString("start_menu_downloads", "Downloads"),
                    Open = () => StartMenuLauncher.Start("shell:Downloads"),
                    LoadIcon = () => ShellIcons.FromParsingName("shell:Downloads", ShellIconSize.ExtraLarge)
                });

            if (IsWindows7)
            {
                // Grouped as in Windows 7: personal folders, then recent items, computer and network, then settings
                if (s.StartMenuShowRecentItems)
                    groups[1].Add(FolderLink(FindString("start_menu_recent_items", "Recent Items"), recentItems));
                AddComputerAndNetworkLinks(s, groups[1]);
                AddSettingsLinks(s, groups[2], true);
            }
            else
            {
                if (s.StartMenuShowSearchLink)
                    groups[1].Add(new RightColumnLink
                    {
                        Label = FindString("start_menu_search", "Search"),
                        Open = () => StartMenuLauncher.Start("search-ms:"),
                        LoadIcon = () => ShellIcons.FromParsingName("shell:SearchHomeFolder", ShellIconSize.ExtraLarge)
                    });
                if (s.StartMenuShowRecentItems)
                    groups[1].Add(FolderLink(FindString("start_menu_recent_items", "Recent Items"), recentItems));

                AddComputerAndNetworkLinks(s, groups[2]);
                AddSettingsLinks(s, groups[3], false);
            }

            RightColumn.Children.Clear();
            _rightItems.Clear();

            var links = new List<RightColumnLink>();
            bool needsSeparator = false;

            foreach (List<RightColumnLink> group in groups.Where(g => g.Count > 0))
            {
                if (needsSeparator && IsWindows7)
                {
                    // Windows 7: a dark line over a light line, 3 px in all, fading at both ends
                    RightColumn.Children.Add(new Border
                    {
                        Height = 2,
                        Margin = new Thickness(9, 1, 36, 0),
                        Background = (Brush)FindResource(IsGlass ? "Win7RightSeparator" : "Win7RightSeparatorBasic"),
                        OpacityMask = (Brush)FindResource("Win7RightSeparatorMask")
                    });
                }
                else if (needsSeparator)
                {
                    RightColumn.Children.Add(new Border
                    {
                        Height = 1,
                        Margin = new Thickness(6, 5, 6, 5),
                        Background = (Brush)FindResource("RightSeparator")
                    });
                }

                foreach (RightColumnLink link in group)
                {
                    var button = new Button
                    {
                        Content = link.Label,
                        Style = (Style)FindResource(IsWindows7 && !IsGlass ? "RightItemButtonBasic7" : "RightItemButton")
                    };

                    Action open = link.Open;
                    button.Click += (sender, e) =>
                    {
                        CloseMenu();
                        open();
                    };
                    button.MouseEnter += RightItem_MouseEnter;
                    button.MouseLeave += RightItem_MouseLeave;

                    RightColumn.Children.Add(button);
                    _rightItems.Add((button, null));
                    links.Add(link);
                }

                needsSeparator = true;
            }

            _ = LoadRightColumnIconsAsync(links, signature);
        }

        private async Task LoadRightColumnIconsAsync(List<RightColumnLink> links, string signature)
        {
            List<ImageSource> icons = await StaTask.Run(() => links.Select(l => l.LoadIcon?.Invoke()).ToList());

            if (signature != _rightColumnSignature)
            {
                return;
            }

            for (int i = 0; i < icons.Count && i < _rightItems.Count; i++)
            {
                _rightItems[i] = (_rightItems[i].button, icons[i]);
            }
        }

        private void AddComputerAndNetworkLinks(Settings s, List<RightColumnLink> group)
        {
            if (s.StartMenuShowComputer)
                group.Add(ShellLink(FindString("start_menu_computer", "Computer"), "shell:MyComputerFolder"));
            if (s.StartMenuShowNetwork)
                group.Add(ShellLink(FindString("start_menu_network", "Network"), "shell:NetworkPlacesFolder"));
            if (s.StartMenuShowConnectTo)
                group.Add(new RightColumnLink
                {
                    Label = FindString("start_menu_connect_to", "Connect To"),
                    Open = () => StartMenuLauncher.Start("ms-availablenetworks:"),
                    LoadIcon = null
                });
        }

        /// <summary>Control Panel, Default Programs and Run, with Devices and Printers between them in Windows 7.</summary>
        private void AddSettingsLinks(Settings s, List<RightColumnLink> group, bool windows7)
        {
            if (s.StartMenuShowControlPanel)
                group.Add(new RightColumnLink
                {
                    Label = FindString("start_menu_control_panel", "Control Panel"),
                    Open = () => StartMenuLauncher.Start("control.exe"),
                    LoadIcon = () => ShellIcons.FromParsingName("shell:ControlPanelFolder", ShellIconSize.ExtraLarge)
                });
            if (windows7 && s.StartMenuShowDevicesAndPrinters)
                group.Add(ShellLink(FindString("start_menu7_devices_printers", "Devices and Printers"), DevicesAndPrintersFolder));
            if (s.StartMenuShowDefaultPrograms)
                group.Add(new RightColumnLink
                {
                    Label = FindString("start_menu_default_programs", "Default Programs"),
                    Open = () => StartMenuLauncher.Start("ms-settings:defaultapps"),
                    LoadIcon = null
                });
            if (s.StartMenuShowRun)
                group.Add(new RightColumnLink
                {
                    Label = FindString("start_menu_run", "Run..."),
                    Open = StartMenuLauncher.ShowRunDialog,
                    LoadIcon = null
                });
        }

        // The Devices and Printers folder, introduced by Windows 7
        private const string DevicesAndPrintersFolder = "shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}";

        private static RightColumnLink FolderLink(string label, string path)
        {
            return new RightColumnLink
            {
                Label = label,
                Open = () => StartMenuLauncher.Start(path),
                LoadIcon = () => ShellIcons.FromPath(path, ShellIconSize.ExtraLarge)
            };
        }

        private static RightColumnLink ShellLink(string label, string location)
        {
            return new RightColumnLink
            {
                Label = label,
                Open = () => StartMenuLauncher.Start(location),
                LoadIcon = () => ShellIcons.FromParsingName(location, ShellIconSize.ExtraLarge)
            };
        }

        private void RightItem_MouseEnter(object sender, MouseEventArgs e)
        {
            // Vista shows the hovered item's icon in place of the framed user picture: the icon alone, without the frame
            foreach (var item in _rightItems)
            {
                if (item.button == sender)
                {
                    // An item whose icon has not loaded yet shows the framed picture
                    _pictureWindow.ShowItemIcon(item.icon);
                    return;
                }
            }
        }

        private void RightItem_MouseLeave(object sender, MouseEventArgs e)
        {
            // Moving straight onto the next item leaves this one first. The picture comes back only if no item is under
            // the pointer once this mouse movement has been handled, so it never flashes between two items.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                foreach (var item in _rightItems)
                {
                    if (item.button.IsMouseOver)
                    {
                        return;
                    }
                }

                _pictureWindow.ShowPicture(true);
            }));
        }

        #endregion

        #region Views

        private void ShowView(MenuView view)
        {
            _view = view;

            HomeView.Visibility = view == MenuView.Home ? Visibility.Visible : Visibility.Hidden;
            AllProgramsView.Visibility = view == MenuView.AllPrograms ? Visibility.Visible : Visibility.Collapsed;
            SearchView.Visibility = view == MenuView.Search ? Visibility.Visible : Visibility.Collapsed;

            // The bottom row reads "All Programs", "Back" in All Programs, and "See all results" while searching
            bool showBack = view == MenuView.AllPrograms;
            string labelKey = view switch
            {
                MenuView.AllPrograms => "start_menu_back",
                MenuView.Search => IsWindows7 ? "start_menu7_see_more_results" : "start_menu_see_all_results",
                _ => "start_menu_all_programs"
            };
            AllProgramsText.SetResourceReference(TextBlock.TextProperty, labelKey);
            AllProgramsArrow.RenderTransformOrigin = new Point(0.5, 0.5);
            AllProgramsArrow.RenderTransform = showBack ? new ScaleTransform(-1, 1) : Transform.Identity;
            AllProgramsArrow.Visibility = view == MenuView.Search ? Visibility.Collapsed : Visibility.Visible;

            if (view == MenuView.AllPrograms)
            {
                AllProgramsView.ScrollToTop();
            }

            UpdateDefaultResult();
        }

        /// <summary>
        /// Highlights the result Enter opens: the first program, or else the first file, while the search box has the
        /// keyboard. Arrowing into the results moves the highlight to the focused item instead.
        /// </summary>
        private void UpdateDefaultResult()
        {
            StartMenuEntry next = _view == MenuView.Search && SearchBox.IsKeyboardFocusWithin
                ? SearchPrograms.FirstOrDefault() ?? SearchFiles.FirstOrDefault()
                : null;

            if (next == _defaultResult)
            {
                return;
            }

            if (_defaultResult != null)
            {
                _defaultResult.IsDefaultResult = false;
            }

            _defaultResult = next;

            if (_defaultResult != null)
            {
                _defaultResult.IsDefaultResult = true;
            }
        }

        private void AllProgramsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_view == MenuView.Search)
            {
                OpenFullSearch();
            }
            else if (_view == MenuView.AllPrograms)
            {
                CollapseAll(AllPrograms);
                ShowView(MenuView.Home);
            }
            else
            {
                ShowView(MenuView.AllPrograms);
            }
        }

        private static void CollapseAll(IEnumerable<StartMenuEntry> entries)
        {
            foreach (StartMenuEntry entry in entries)
            {
                if (entry.IsFolder)
                {
                    entry.IsExpanded = false;
                    CollapseAll(entry.Children);
                }
            }
        }

        #endregion

        #region Search

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string text = SearchBox.Text;
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;

            CancelSearch();

            if (string.IsNullOrWhiteSpace(text))
            {
                if (_view == MenuView.Search)
                {
                    ShowView(MenuView.Home);
                }

                return;
            }

            ShowView(MenuView.Search);
            UpdateProgramResults(text);
            SearchFiles.Clear();
            UpdateResultHeaders(Settings.Instance.StartMenuSearchFiles);
            UpdateDefaultResult();

            if (Settings.Instance.StartMenuSearchFiles)
            {
                _fileSearchTimer.Start();
            }
        }

        private void UpdateProgramResults(string text)
        {
            SearchPrograms.Clear();

            if (_catalog == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string term = text.Trim();

            IEnumerable<StartMenuEntry> matches = _catalog.Programs
                .Select(p => (entry: p, rank: MatchRank(p.Name, term)))
                .Where(m => m.rank >= 0)
                .OrderBy(m => m.rank)
                .ThenBy(m => m.entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(m => m.entry)
                .Take(MaxProgramResults);

            foreach (StartMenuEntry entry in matches)
            {
                SearchPrograms.Add(entry);
            }
        }

        /// <summary>
        /// 0 for a name that starts with the term, 1 for a word that starts with it, 2 for any other match, -1 for none.
        /// </summary>
        private static int MatchRank(string name, string term)
        {
            if (string.IsNullOrEmpty(name))
            {
                return -1;
            }

            int index = name.IndexOf(term, StringComparison.CurrentCultureIgnoreCase);

            if (index < 0)
            {
                return -1;
            }

            if (index == 0)
            {
                return 0;
            }

            return char.IsWhiteSpace(name[index - 1]) || char.IsPunctuation(name[index - 1]) ? 1 : 2;
        }

        private async void FileSearchTimer_Tick(object sender, EventArgs e)
        {
            _fileSearchTimer.Stop();

            string text = SearchBox.Text;

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var cancellation = new CancellationTokenSource();
            _searchCancellation = cancellation;

            try
            {
                List<StartMenuEntry> files = await FileSearch.SearchAsync(text, MaxFileResults, cancellation.Token);

                if (cancellation.IsCancellationRequested || text != SearchBox.Text)
                {
                    return;
                }

                ReplaceItems(SearchFiles, files);
                UpdateDefaultResult();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                ShellLogger.Debug($"VistaStartMenu: File search failed: {ex.Message}");
            }

            if (!cancellation.IsCancellationRequested)
            {
                UpdateResultHeaders(false);
            }
        }

        /// <param name="filesPending">Whether file results may still arrive, which keeps "no results" hidden.</param>
        private void UpdateResultHeaders(bool filesPending)
        {
            ProgramsHeader.Visibility = SearchPrograms.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            FilesHeader.Visibility = SearchFiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            NoResultsText.Visibility = SearchPrograms.Count == 0 && SearchFiles.Count == 0 && !filesPending ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CancelSearch()
        {
            _fileSearchTimer.Stop();
            _searchCancellation?.Cancel();
            _searchCancellation = null;
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                StartMenuEntry first = SearchPrograms.FirstOrDefault() ?? SearchFiles.FirstOrDefault();

                if (_view == MenuView.Search && first != null)
                {
                    LaunchEntry(first);
                }
                else if (_view == MenuView.Search && !string.IsNullOrWhiteSpace(SearchBox.Text))
                {
                    OpenFullSearch();
                }

                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                ItemsControl list = _view == MenuView.Search
                    ? (SearchPrograms.Count > 0 ? SearchProgramsList : SearchFilesList)
                    : (PinnedPrograms.Count > 0 ? PinnedList : RecentList);

                if (_view == MenuView.AllPrograms)
                {
                    AllProgramsTree.Focus();
                    e.Handled = true;
                }
                else if (FocusFirstButton(list))
                {
                    e.Handled = true;
                }
            }
        }

        private void OpenFullSearch()
        {
            string text = SearchBox.Text.Trim();
            CloseMenu();
            StartMenuLauncher.Start("search-ms:query=" + Uri.EscapeDataString(text));
        }

        private static bool FocusFirstButton(ItemsControl list)
        {
            if (list.Items.Count == 0 || list.ItemContainerGenerator.ContainerFromIndex(0) is not DependencyObject container)
            {
                return false;
            }

            Button button = FindChild<Button>(container);
            return button != null && button.Focus();
        }

        private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);

                if (child is T match)
                {
                    return match;
                }

                T descendant = FindChild<T>(child);

                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
        }

        #endregion

        #region Keyboard

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (!string.IsNullOrEmpty(SearchBox.Text))
                {
                    SearchBox.Text = string.Empty;
                    SearchBox.Focus();
                }
                else if (_view == MenuView.AllPrograms)
                {
                    CollapseAll(AllPrograms);
                    ShowView(MenuView.Home);
                }
                else
                {
                    CloseMenu();
                }

                e.Handled = true;
                return;
            }

            // As in Vista, typing anywhere in the menu goes to the search box
            if (Settings.Instance.StartMenuShowSearch && !SearchBox.IsKeyboardFocusWithin && IsTypingKey(e.Key) &&
                (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0)
            {
                SearchBox.Focus();
                SearchBox.CaretIndex = SearchBox.Text.Length;
            }
        }

        private static bool IsTypingKey(Key key)
        {
            return (key >= Key.A && key <= Key.Z) || (key >= Key.D0 && key <= Key.D9) || (key >= Key.NumPad0 && key <= Key.NumPad9);
        }

        private void AllProgramsTree_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || AllProgramsTree.SelectedItem is not StartMenuEntry entry)
            {
                return;
            }

            if (entry.IsFolder)
            {
                entry.IsExpanded = !entry.IsExpanded;
            }
            else
            {
                LaunchEntry(entry);
            }

            e.Handled = true;
        }

        #endregion

        #region Launching and context menus

        private void ProgramButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is StartMenuEntry entry)
            {
                LaunchEntry(entry);
            }
        }

        private void AllProgramsItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not StartMenuEntry entry)
            {
                return;
            }

            if (entry.IsFolder)
            {
                entry.IsExpanded = !entry.IsExpanded;
            }
            else
            {
                LaunchEntry(entry);
            }

            e.Handled = true;
        }

        private void AllProgramsItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is StartMenuEntry entry && !entry.IsFolder)
            {
                ShowEntryContextMenu((FrameworkElement)sender, entry, false);
                e.Handled = true;
            }
        }

        private void ProgramButton_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is StartMenuEntry entry)
            {
                ShowEntryContextMenu((FrameworkElement)sender, entry, RecentPrograms.Contains(entry));
                e.Handled = true;
            }
        }

        private void LaunchEntry(StartMenuEntry entry, bool asAdministrator = false)
        {
            CloseMenu();

            if (StartMenuLauncher.Launch(entry, asAdministrator))
            {
                RecordLaunch(entry);
            }
        }

        private void RecordLaunch(StartMenuEntry entry)
        {
            if (!entry.IsProgram || !string.IsNullOrEmpty(entry.PinKey) || entry.Key == null)
            {
                return;
            }

            // Only programs are tracked, not documents found by search
            if (entry.Kind == StartMenuEntryKind.File && !IsProgramFile(entry.Target))
            {
                return;
            }

            var recent = new List<string>(Settings.Instance.StartMenuRecentPrograms ?? new List<string>());
            recent.RemoveAll(k => string.Equals(k, entry.Key, StringComparison.OrdinalIgnoreCase));
            recent.Insert(0, entry.Key);

            if (recent.Count > MaxStoredRecentPrograms)
            {
                recent.RemoveRange(MaxStoredRecentPrograms, recent.Count - MaxStoredRecentPrograms);
            }

            Settings.Instance.StartMenuRecentPrograms = recent;

            var hidden = Settings.Instance.StartMenuHiddenPrograms ?? new List<string>();

            if (hidden.Any(k => string.Equals(k, entry.Key, StringComparison.OrdinalIgnoreCase)))
            {
                Settings.Instance.StartMenuHiddenPrograms = hidden.Where(k => !string.Equals(k, entry.Key, StringComparison.OrdinalIgnoreCase)).ToList();
            }
        }

        private static bool IsProgramFile(string path)
        {
            string extension = Path.GetExtension(path ?? string.Empty);
            return extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".msc", StringComparison.OrdinalIgnoreCase);
        }

        private void ShowEntryContextMenu(FrameworkElement target, StartMenuEntry entry, bool isRecent)
        {
            var menu = new ContextMenu
            {
                PlacementTarget = target,
                Placement = PlacementMode.MousePoint
            };

            var open = new MenuItem { Header = FindString("start_menu_open", "Open"), FontWeight = FontWeights.Bold };
            open.Click += (s, e) => LaunchEntry(entry);
            menu.Items.Add(open);

            if (entry.Kind == StartMenuEntryKind.File)
            {
                var runAsAdmin = new MenuItem { Header = FindString("start_menu_run_as_admin", "Run as administrator") };
                runAsAdmin.Click += (s, e) => LaunchEntry(entry, true);
                menu.Items.Add(runAsAdmin);

                var openLocation = new MenuItem { Header = FindString("start_menu_open_location", "Open file location") };
                openLocation.Click += (s, e) =>
                {
                    CloseMenu();
                    StartMenuLauncher.OpenFileLocation(entry);
                };
                menu.Items.Add(openLocation);
            }

            string key = entry.Key;

            if (key != null && (entry.IsProgram || !string.IsNullOrEmpty(entry.PinKey)))
            {
                menu.Items.Add(new Separator());

                List<string> pinned = Settings.Instance.StartMenuPinnedPrograms ?? new List<string>();
                bool isPinned = pinned.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));

                if (IsWindows7 && string.IsNullOrEmpty(entry.PinKey))
                {
                    // Windows 7 can also pin a program to the taskbar
                    bool isOnTaskbar = TaskbarPins.IsPinned(key);
                    var taskbarPin = new MenuItem
                    {
                        Header = isOnTaskbar ? FindString("start_menu7_unpin_taskbar", "Unpin from Taskbar") : FindString("start_menu7_pin_taskbar", "Pin to Taskbar")
                    };
                    taskbarPin.Click += (s, e) =>
                    {
                        if (isOnTaskbar)
                        {
                            TaskbarPins.Unpin(key);
                        }
                        else
                        {
                            TaskbarPins.Pin(key);
                        }
                    };
                    menu.Items.Add(taskbarPin);
                }

                var pin = new MenuItem
                {
                    Header = IsWindows7
                        ? (isPinned ? FindString("start_menu7_unpin", "Unpin from Start Menu") : FindString("start_menu7_pin", "Pin to Start Menu"))
                        : (isPinned ? FindString("start_menu_unpin", "Unpin from Start menu") : FindString("start_menu_pin", "Pin to Start menu"))
                };
                pin.Click += (s, e) =>
                {
                    List<string> current = new List<string>(Settings.Instance.StartMenuPinnedPrograms ?? new List<string>());

                    if (isPinned)
                    {
                        current.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        current.Add(key);
                    }

                    Settings.Instance.StartMenuPinnedPrograms = current;
                    _ = RefreshHomeListsAsync();
                };
                menu.Items.Add(pin);

                if (isRecent)
                {
                    var remove = new MenuItem { Header = FindString("start_menu_remove_from_list", "Remove from this list") };
                    remove.Click += (s, e) =>
                    {
                        var hidden = new List<string>(Settings.Instance.StartMenuHiddenPrograms ?? new List<string>()) { key };
                        Settings.Instance.StartMenuHiddenPrograms = hidden;
                        _ = RefreshHomeListsAsync();
                    };
                    menu.Items.Add(remove);
                }
            }

            menu.IsOpen = true;
        }

        #endregion

        #region Power

        private void PowerButton_Click(object sender, RoutedEventArgs e)
        {
            StartMenuPowerAction action = IsWindows7 ? Settings.Instance.Windows7PowerButtonAction : Settings.Instance.StartMenuPowerButtonAction;
            CloseMenu();
            StartMenuLauncher.PerformPowerAction(action);
        }

        private void LockButton_Click(object sender, RoutedEventArgs e)
        {
            CloseMenu();
            StartMenuLauncher.Lock();
        }

        private void PowerOptionsButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu
            {
                PlacementTarget = IsWindows7 ? ShutDownOptionsButton : PowerOptionsButton,
                Placement = PlacementMode.Right
            };

            void Add(string key, string fallback, Action action)
            {
                var item = new MenuItem { Header = FindString(key, fallback) };
                item.Click += (s, args) =>
                {
                    CloseMenu();
                    action();
                };
                menu.Items.Add(item);
            }

            if (IsWindows7)
            {
                // Windows 7 lists every action except the one on the button, in sentence case
                StartMenuPowerAction buttonAction = Settings.Instance.Windows7PowerButtonAction;
                Add("start_menu7_switch_user", "Switch user", StartMenuLauncher.SwitchUser);
                Add("start_menu7_log_off", "Log off", StartMenuLauncher.LogOff);
                Add("start_menu_lock", "Lock", StartMenuLauncher.Lock);
                menu.Items.Add(new Separator());
                Add("start_menu_restart", "Restart", StartMenuLauncher.Restart);
                if (buttonAction != StartMenuPowerAction.Sleep)
                    Add("start_menu_sleep", "Sleep", StartMenuLauncher.Sleep);
                if (buttonAction != StartMenuPowerAction.Hibernate)
                    Add("start_menu_hibernate", "Hibernate", StartMenuLauncher.Hibernate);
                if (buttonAction != StartMenuPowerAction.ShutDown)
                    Add("start_menu7_shut_down", "Shut down", StartMenuLauncher.ShutDown);

                menu.IsOpen = true;
                return;
            }

            Add("start_menu_switch_user", "Switch User", StartMenuLauncher.SwitchUser);
            Add("start_menu_log_off", "Log Off", StartMenuLauncher.LogOff);
            Add("start_menu_lock", "Lock", StartMenuLauncher.Lock);
            menu.Items.Add(new Separator());
            Add("start_menu_restart", "Restart", StartMenuLauncher.Restart);
            Add("start_menu_sleep", "Sleep", StartMenuLauncher.Sleep);
            Add("start_menu_hibernate", "Hibernate", StartMenuLauncher.Hibernate);
            Add("start_menu_shut_down", "Shut Down", StartMenuLauncher.ShutDown);

            menu.IsOpen = true;
        }

        #endregion

        private string FindString(string key, string fallback)
        {
            return TryFindResource(key) as string ?? fallback;
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/StartMenu/VistaStartMenu.xaml.cs">

// <RETROSTAR_SOURCE_BEGIN path="RetroBar/StartMenu/UserPictureWindow.xaml.cs">
namespace RetroBar.StartMenu
{
    using ManagedShell.Common.Helpers;
    using System;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using System.Windows.Interop;
    using System.Windows.Media;
    using System.Windows.Media.Animation;

    /// <summary>
    /// The framed user picture that overhangs the top of the Start menu. While the pointer is over an item in the
    /// menu's right column it shows that item's icon instead, without the frame, as Vista does (ShowItemIcon).
    /// It is a separate window from the menu so that the menu's glass stays inside the menu frame.
    /// </summary>
    internal partial class UserPictureWindow : Window
    {
        /// <summary>Size of the frame in device-independent pixels.</summary>
        public const double FrameSize = 58;

        /// <summary>
        /// Superellipse exponent of the frame outline, fitted to traced Vista and Windows 7 frames.
        /// 2 would be an ellipse and larger values approach a rectangle; at 7.7 the edges bow slightly
        /// outward and turn through wide, gradual corners.
        /// </summary>
        private const double ShapeExponent = 7.7;

        // Distance of each ring's outer edge from the frame's outer edge, in device-independent pixels
        private const double RimInset = 1;
        private const double BandInset = 2;
        private const double LightLineInset = 4;
        private const double DarkLineInset = 5;
        private const double PictureInset = 6;

        private const int ShapeSegments = 192;

        // How long the frame and the item icons take to fade in or out
        private static readonly Duration FadeDuration = new Duration(TimeSpan.FromMilliseconds(200));

        // The item icon shown now (ItemIconA or ItemIconB), or null while the framed picture is shown
        private Image _shownIcon;

        public event EventHandler PictureClicked;

        public IntPtr Handle { get; private set; }

        public UserPictureWindow()
        {
            InitializeComponent();

            // The outer rings follow the superellipse. The lines around the picture are nearly square,
            // so the glass band between them is thicker at the corners, as in Vista.
            OutlineRing.Data = CreateRing(CreateSuperellipse(0, 0), CreateSuperellipse(RimInset, 0));
            RimRing.Data = CreateRing(CreateSuperellipse(RimInset, 0), CreateSuperellipse(BandInset, 0));
            RimShade.Data = RimRing.Data;
            BandRing.Data = CreateRing(CreateSuperellipse(BandInset, 0), CreateRoundedSquare(LightLineInset, 2.5, 0));
            BandShade.Data = BandRing.Data;
            LightRing.Data = CreateRing(CreateRoundedSquare(LightLineInset, 2.5, 0), CreateRoundedSquare(DarkLineInset, 2, 0));
            DarkRing.Data = CreateRing(CreateRoundedSquare(DarkLineInset, 2, 0), CreateRoundedSquare(PictureInset, 1.5, 0));

            // The picture is clipped to its slightly rounded corners, in its own coordinates
            Geometry pictureClip = CreateRoundedSquare(PictureInset, 1.5, -PictureInset);
            PictureBackground.Clip = pictureClip;
            Picture.Clip = pictureClip;

            Left = -32000;
            Top = -32000;
        }

        /// <summary>Zooms the picture and the item icons with the Start menu; text-free, so only the transform changes.</summary>
        public void SetScale(double scale)
        {
            if (Content is FrameworkElement root)
            {
                root.LayoutTransform = Math.Abs(scale - 1.0) > 0.001 ? new ScaleTransform(scale, scale) : Transform.Identity;
            }
        }

        /// <summary>The user picture inside the frame.</summary>
        public ImageSource Source
        {
            get => Picture.Source;
            set => Picture.Source = value;
        }

        /// <summary>
        /// Shows a right-column item's icon in place of the framed picture: the icon alone, on a transparent
        /// background, with no frame. The frame fades out as the icon fades in; a different item's icon already shown
        /// fades into the new one.
        /// </summary>
        public void ShowItemIcon(ImageSource icon)
        {
            if (icon == null)
            {
                ShowPicture(true);
                return;
            }

            if (_shownIcon != null && _shownIcon.Source == icon)
            {
                return;
            }

            Image next = _shownIcon == ItemIconA ? ItemIconB : ItemIconA;
            next.Source = icon;

            if (_shownIcon != null)
            {
                Fade(_shownIcon, 0);
            }

            Fade(next, 1);
            Fade(FrameLayer, 0);
            _shownIcon = next;
        }

        /// <summary>
        /// Shows the framed user picture again, fading out the item icon, or at once (for a menu that is opening).
        /// </summary>
        public void ShowPicture(bool animate)
        {
            if (!animate)
            {
                SetOpacity(FrameLayer, 1);
                SetOpacity(ItemIconA, 0);
                SetOpacity(ItemIconB, 0);
                _shownIcon = null;
                return;
            }

            if (_shownIcon == null)
            {
                return;
            }

            Fade(_shownIcon, 0);
            Fade(FrameLayer, 1);
            _shownIcon = null;
        }

        /// <summary>Fades an element from its current opacity to the given one.</summary>
        private static void Fade(UIElement element, double opacity)
        {
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, FadeDuration));
        }

        /// <summary>Stops any fade on an element and sets its opacity.</summary>
        private static void SetOpacity(UIElement element, double opacity)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = opacity;
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            Handle = new WindowInteropHelper(this).Handle;

            // Never take focus from the menu, and stay out of Alt+Tab
            int exStyle = StartMenuNative.GetWindowLong(Handle, StartMenuNative.GWL_EXSTYLE);
            exStyle = (exStyle | StartMenuNative.WS_EX_TOOLWINDOW | StartMenuNative.WS_EX_NOACTIVATE) & ~StartMenuNative.WS_EX_APPWINDOW;
            StartMenuNative.SetWindowLong(Handle, StartMenuNative.GWL_EXSTYLE, exStyle);

            WindowHelper.ExcludeWindowFromPeek(Handle);
        }

        /// <summary>
        /// Shows the picture with its top-left corner at the given screen position, in device pixels.
        /// </summary>
        public void ShowAt(int x, int y)
        {
            if (!IsVisible)
            {
                Show();
            }

            StartMenuNative.SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0,
                StartMenuNative.SWP_NOSIZE | StartMenuNative.SWP_NOZORDER | StartMenuNative.SWP_NOACTIVATE);
        }

        /// <summary>
        /// The area inside the outer shape and outside the inner one.
        /// </summary>
        private static Geometry CreateRing(Geometry outer, Geometry inner)
        {
            var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
            ring.Children.Add(outer);
            ring.Children.Add(inner);
            ring.Freeze();
            return ring;
        }

        /// <summary>
        /// A square filling the frame inset by the given distance, with slightly rounded corners,
        /// shifted by offset on both axes.
        /// </summary>
        private static Geometry CreateRoundedSquare(double inset, double cornerRadius, double offset)
        {
            double size = FrameSize - 2 * inset;
            var geometry = new RectangleGeometry(new Rect(inset + offset, inset + offset, size, size), cornerRadius, cornerRadius);
            geometry.Freeze();
            return geometry;
        }

        /// <summary>
        /// A closed superellipse |x/a|^n + |y/b|^n = 1 filling the frame inset by the given distance,
        /// shifted by offset on both axes.
        /// </summary>
        private static Geometry CreateSuperellipse(double inset, double offset)
        {
            double center = FrameSize / 2 + offset;
            double radius = FrameSize / 2 - inset;
            double power = 2 / ShapeExponent;

            var geometry = new StreamGeometry();

            using (StreamGeometryContext context = geometry.Open())
            {
                for (int i = 0; i < ShapeSegments; i++)
                {
                    double angle = 2 * Math.PI * i / ShapeSegments;
                    double cos = Math.Cos(angle);
                    double sin = Math.Sin(angle);
                    var point = new Point(
                        center + radius * Math.Sign(cos) * Math.Pow(Math.Abs(cos), power),
                        center + radius * Math.Sign(sin) * Math.Pow(Math.Abs(sin), power));

                    if (i == 0)
                    {
                        context.BeginFigure(point, true, true);
                    }
                    else
                    {
                        context.LineTo(point, false, true);
                    }
                }
            }

            geometry.Freeze();
            return geometry;
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            PictureClicked?.Invoke(this, EventArgs.Empty);
        }
    }
}
// <RETROSTAR_SOURCE_END path="RetroBar/StartMenu/UserPictureWindow.xaml.cs">
