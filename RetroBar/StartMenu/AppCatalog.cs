using ManagedShell.Common.Logging;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace RetroBar.StartMenu
{
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
