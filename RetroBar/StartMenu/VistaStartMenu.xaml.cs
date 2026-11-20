using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using Microsoft.Win32;
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

namespace RetroBar.StartMenu
{
    /// <summary>
    /// A replica of the Windows Vista Start menu, backed by the Windows 10/11 app list and the local Windows Search index.
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

        public static readonly DependencyProperty ProgramIconSizeProperty =
            DependencyProperty.Register(nameof(ProgramIconSize), typeof(double), typeof(VistaStartMenu), new PropertyMetadata(32.0));

        public double ProgramIconSize
        {
            get => (double)GetValue(ProgramIconSizeProperty);
            set => SetValue(ProgramIconSizeProperty, value);
        }

        public static readonly DependencyProperty ProgramRowHeightProperty =
            DependencyProperty.Register(nameof(ProgramRowHeight), typeof(double), typeof(VistaStartMenu), new PropertyMetadata(36.0));

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
        private ImageSource _userPicture;
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
            Hide();
            MenuClosed?.Invoke(this, EventArgs.Empty);
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            CloseMenu();
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

            StartMenuNative.SetWindowPos(_handle, StartMenuNative.HWND_TOPMOST, x, y, 0, 0, StartMenuNative.SWP_NOSIZE | StartMenuNative.SWP_NOACTIVATE);
        }

        /// <summary>
        /// Clips the window to the rounded menu frame and the user picture, so the glass blur
        /// does not show in the transparent areas around them.
        /// </summary>
        private void UpdateWindowRegion()
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;

            IntPtr frameRegion = CreateRoundRegion(Frame, 6, scale);
            IntPtr pictureRegion = CreateRoundRegion(PictureFrame, 5, scale);

            if (frameRegion == IntPtr.Zero)
            {
                if (pictureRegion != IntPtr.Zero)
                {
                    StartMenuNative.DeleteObject(pictureRegion);
                }

                return;
            }

            if (pictureRegion != IntPtr.Zero)
            {
                StartMenuNative.CombineRgn(frameRegion, frameRegion, pictureRegion, StartMenuNative.RGN_OR);
                StartMenuNative.DeleteObject(pictureRegion);
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

            Point topLeft = element.TranslatePoint(new Point(0, 0), this);
            int left = (int)Math.Round(topLeft.X * scale);
            int top = (int)Math.Round(topLeft.Y * scale);
            int right = (int)Math.Round((topLeft.X + element.ActualWidth) * scale);
            int bottom = (int)Math.Round((topLeft.Y + element.ActualHeight) * scale);
            int diameter = (int)Math.Round(radius * 2 * scale);

            return StartMenuNative.CreateRoundRectRgn(left, top, right + 1, bottom + 1, diameter, diameter);
        }

        private void UpdateGlass()
        {
            if (_handle != IntPtr.Zero && IsVisible)
            {
                AeroGlassHelper.SetGlassBlur(_handle, IsGlass);
            }
        }

        #endregion

        #region Settings and content

        private void ApplySettings()
        {
            IsGlass = Settings.Instance.EnableAeroGlass;

            bool largeIcons = Settings.Instance.StartMenuLargeIcons;
            ProgramIconSize = largeIcons ? 32 : 16;
            ProgramRowHeight = largeIcons ? 36 : 22;

            SearchBoxBorder.Visibility = Settings.Instance.StartMenuShowSearch ? Visibility.Visible : Visibility.Hidden;

            switch (Settings.Instance.StartMenuPowerButtonAction)
            {
                case StartMenuPowerAction.ShutDown:
                    PowerButton.Background = (Brush)FindResource("PowerShutDownBackground");
                    PowerButton.ToolTip = FindString("start_menu_shut_down", "Shut Down");
                    break;
                case StartMenuPowerAction.Hibernate:
                    PowerButton.Background = (Brush)FindResource("PowerSleepBackground");
                    PowerButton.ToolTip = FindString("start_menu_hibernate", "Hibernate");
                    break;
                default:
                    PowerButton.Background = (Brush)FindResource("PowerSleepBackground");
                    PowerButton.ToolTip = FindString("start_menu_sleep", "Sleep");
                    break;
            }

            LockButton.ToolTip = FindString("start_menu_lock", "Lock");

            LoadUserPicture();
            BuildRightColumn();
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

            string signature = string.Join("|", pinned) + "#" + string.Join("|", recent) + "#" + string.Join("|", hidden) + "#" + recentCount + useWindowsUsage + (catalog?.LoadedAt.Ticks ?? 0);

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
                        StartMenuLauncher.DefaultBrowserKey => StartMenuLauncher.CreateDefaultProgramEntry(key, internetLabel),
                        StartMenuLauncher.DefaultEmailKey => StartMenuLauncher.CreateDefaultProgramEntry(key, emailLabel),
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

            UserPicture.Source = _userPicture;
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
                s.StartMenuShowConnectTo, s.StartMenuShowControlPanel, s.StartMenuShowDefaultPrograms, s.StartMenuShowRun, Settings.Instance.Language);

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

            if (s.StartMenuShowSearchLink)
                groups[1].Add(new RightColumnLink
                {
                    Label = FindString("start_menu_search", "Search"),
                    Open = () => StartMenuLauncher.Start("search-ms:"),
                    LoadIcon = () => ShellIcons.FromParsingName("shell:SearchHomeFolder", ShellIconSize.ExtraLarge)
                });
            if (s.StartMenuShowRecentItems)
                groups[1].Add(FolderLink(FindString("start_menu_recent_items", "Recent Items"), recentItems));

            if (s.StartMenuShowComputer)
                groups[2].Add(ShellLink(FindString("start_menu_computer", "Computer"), "shell:MyComputerFolder"));
            if (s.StartMenuShowNetwork)
                groups[2].Add(ShellLink(FindString("start_menu_network", "Network"), "shell:NetworkPlacesFolder"));
            if (s.StartMenuShowConnectTo)
                groups[2].Add(new RightColumnLink
                {
                    Label = FindString("start_menu_connect_to", "Connect To"),
                    Open = () => StartMenuLauncher.Start("ms-availablenetworks:"),
                    LoadIcon = null
                });

            if (s.StartMenuShowControlPanel)
                groups[3].Add(new RightColumnLink
                {
                    Label = FindString("start_menu_control_panel", "Control Panel"),
                    Open = () => StartMenuLauncher.Start("control.exe"),
                    LoadIcon = () => ShellIcons.FromParsingName("shell:ControlPanelFolder", ShellIconSize.ExtraLarge)
                });
            if (s.StartMenuShowDefaultPrograms)
                groups[3].Add(new RightColumnLink
                {
                    Label = FindString("start_menu_default_programs", "Default Programs"),
                    Open = () => StartMenuLauncher.Start("ms-settings:defaultapps"),
                    LoadIcon = null
                });
            if (s.StartMenuShowRun)
                groups[3].Add(new RightColumnLink
                {
                    Label = FindString("start_menu_run", "Run..."),
                    Open = StartMenuLauncher.ShowRunDialog,
                    LoadIcon = null
                });

            RightColumn.Children.Clear();
            _rightItems.Clear();

            var links = new List<RightColumnLink>();
            bool needsSeparator = false;

            foreach (List<RightColumnLink> group in groups.Where(g => g.Count > 0))
            {
                if (needsSeparator)
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
                        Style = (Style)FindResource("RightItemButton")
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
            // Vista shows the hovered item's icon in place of the user picture
            foreach (var item in _rightItems)
            {
                if (item.button == sender && item.icon != null)
                {
                    UserPicture.Source = item.icon;
                    return;
                }
            }
        }

        private void RightItem_MouseLeave(object sender, MouseEventArgs e)
        {
            UserPicture.Source = _userPicture;
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
                MenuView.Search => "start_menu_see_all_results",
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

                var pin = new MenuItem
                {
                    Header = isPinned ? FindString("start_menu_unpin", "Unpin from Start menu") : FindString("start_menu_pin", "Pin to Start menu")
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
            StartMenuPowerAction action = Settings.Instance.StartMenuPowerButtonAction;
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
                PlacementTarget = PowerOptionsButton,
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
