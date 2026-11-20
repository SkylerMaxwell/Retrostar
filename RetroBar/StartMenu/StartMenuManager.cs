using ManagedShell.Common.Logging;
using RetroBar.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace RetroBar.StartMenu
{
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
