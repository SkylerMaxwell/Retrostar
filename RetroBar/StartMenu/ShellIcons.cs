using ManagedShell.Common.Logging;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RetroBar.StartMenu
{
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
