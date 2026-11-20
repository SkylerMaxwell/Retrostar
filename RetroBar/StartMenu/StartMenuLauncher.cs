using ManagedShell.Common.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace RetroBar.StartMenu
{
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
        /// </summary>
        public static StartMenuEntry CreateDefaultProgramEntry(string key, string category)
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
                Name = category,
                Subtitle = appName,
                IsBold = true,
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
