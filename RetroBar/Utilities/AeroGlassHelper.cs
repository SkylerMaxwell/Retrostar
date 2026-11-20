using ManagedShell.Common.Logging;
using System;
using System.Runtime.InteropServices;

namespace RetroBar.Utilities
{
    /// <summary>
    /// Applies a Windows Vista-style glass blur behind a window.
    ///
    /// Vista's DWM glass is a plain Gaussian blur of whatever is behind the window,
    /// with the tint and gloss painted on top by the theme. On Windows 10 and 11 the
    /// closest match is the ACCENT_ENABLE_BLURBEHIND accent state: it blurs without the
    /// noise texture, luminosity layer and saturation boost that Acrylic adds.
    /// The tint and highlights come from the theme's XAML brushes, as they did on Vista.
    /// </summary>
    internal static class AeroGlassHelper
    {
        private enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_BLURBEHIND = 3
        }

        private enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        /// <summary>
        /// The accent policy API used here exists on Windows 10 and later.
        /// </summary>
        public static bool IsSupported => Environment.OSVersion.Version.Major >= 10;

        public static void SetGlassBlur(IntPtr hwnd, bool enable)
        {
            if (!IsSupported || hwnd == IntPtr.Zero)
            {
                return;
            }

            AccentPolicy accent = new AccentPolicy
            {
                AccentState = enable ? AccentState.ACCENT_ENABLE_BLURBEHIND : AccentState.ACCENT_DISABLED
            };

            int accentSize = Marshal.SizeOf(accent);
            IntPtr accentPtr = Marshal.AllocHGlobal(accentSize);

            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);

                WindowCompositionAttributeData data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    Data = accentPtr,
                    SizeOfData = accentSize
                };

                SetWindowCompositionAttribute(hwnd, ref data);
            }
            catch (Exception e)
            {
                ShellLogger.Warning($"AeroGlassHelper: Unable to set glass blur: {e.Message}");
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }
    }
}
