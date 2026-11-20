using ManagedShell.Common.Logging;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RetroBar.StartMenu
{
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
