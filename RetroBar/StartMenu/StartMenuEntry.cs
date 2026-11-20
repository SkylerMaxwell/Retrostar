using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace RetroBar.StartMenu
{
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
