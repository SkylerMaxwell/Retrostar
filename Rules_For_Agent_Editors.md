# Rules for Agent Editors

**Applies to:** every change to Retrostar's source, layout, settings, documentation or build.

These are the invariants AI coding agents and human editors must keep. A change that breaks one is not finished.
The source decides *what* Retrostar does; these rules decide *how it is changed*.
This file is the only agent document: the rules come first (Part 1), then the navigation index (Part 2, starting at
`# Index for Agents`).

## 1. The dual monolith is the layout

Production C# lives in exactly two files:

- `RetroBar/Retrostar_Core.cs`: everything that has no XAML file (settings, persistence, theme and language loading,
  shell integration, input hooks, Aero glass, the Start menu engine).
- `RetroBar/Retrostar_Interface.cs`: the code-behind of every XAML window and control (`x:Class` partial classes),
  and the value converters used from XAML.

The boundary is mechanical: a class whose other half is a `.xaml` file, or an `IValueConverter`, belongs in Interface;
everything else belongs in Core. Both compile into one assembly, so they may call each other; the split is for
navigation, not access control.

**Do not add a third first-party `.cs` file, do not split either monolith, and do not restore per-file `.cs` files.**
Extend the module that already owns a behaviour, or add a new module when none does (rule 3).

The same principle applies to the files around the C#:

- NuGet references, including Nerdbank.GitVersioning, are in `RetroBar/RetroBar.csproj`. There is no
  `Directory.Build.props`.
- The invert shader's source and its `fxc` command are in `shader_invert.hlsl`. There is no `shader_compile.bat`.
- The rules and the navigation index are this one file. There is no separate index file.

Do not restore those files. What still has its own file has it because a tool finds it by name or location: the
publish profiles in `RetroBar/Properties/PublishProfiles/` (the build picks one with `-p:PublishProfile=<name>`),
`version.json` (Nerdbank.GitVersioning), `app.manifest`, `installer.iss`, `DistLicense.txt` and `LICENSE`, the two
workflows in `.github/workflows/` (the pull request comment workflow is triggered by the build workflow finishing, so
it is a workflow of its own), `RetroBar.sln` (the build runs `dotnet publish` from the repository root), `.gitignore`,
.gitattributes, `.github/dependabot.yml` and `README.md`.

## 2. Module format

Each monolith is a sequence of logical modules:

```
// <RETROSTAR_SOURCE_BEGIN path="RetroBar/Utilities/Settings.cs">
namespace RetroBar.Utilities
{
    using System;
    ...

    internal class Settings ...
}
// <RETROSTAR_SOURCE_END path="RetroBar/Utilities/Settings.cs">
```

- A module is exactly one `namespace ... { }` block. Its `using` directives (including aliases and `using static`)
  go at the start of **its own** namespace block. Never put a `using` at the top of a monolith: it would apply to every
  module in the file and can make names ambiguous.
- The `path` is the module's logical path: where it would live as a separate file. A code-behind module's path is its
  XAML path plus `.cs`. The BEGIN and END paths must match, and each path appears once.
- A module that turns on `#nullable enable` ends with `#nullable restore` before its END marker, so the setting does
  not leak into the next module.
- Modules sit under category markers such as `// [2000] START MENU ENGINE :: ...`. A new module goes into the category
  that owns its behaviour.
- Two modules are special and must stay as they are:
  - `RetroBar/AssemblyInfo.cs` is the first module of Core, because assembly attributes must precede every namespace.
  - `RetroBar/Extensions/ManagementExtensions.cs` is in the global namespace, as it always was, so its callers need no
    `using`. With no namespace block to hold `using` directives, it writes `System.IO.Path` and similar names out in full.

## 3. Adding, moving or removing a module

1. Choose the monolith by rule 1 and the category by the index (Part 2 of this file).
2. Add the module with matching BEGIN and END markers and its own namespace block.
3. A new window or control: its `.xaml` file goes in its folder, and its code-behind module goes in Interface under
   `path="<xaml path>.cs"`. The `x:Class` and the partial class's name, namespace and accessibility must match.
4. Update the index (Part 2 of this file) in the same change. The index carries no line numbers.

## 4. XAML stays in its own files

These cannot be merged into anything, and must not be:

- **Windows and controls** (`App.xaml`, `Taskbar.xaml`, `PropertiesWindow.xaml`, `NotificationPropertiesWindow.xaml`,
  `Controls/*.xaml`, `StartMenu/*.xaml`): WPF compiles each `x:Class` from its own XAML file.
- **Themes** (`Themes/*.xaml`): `DictionaryManager` lists and loads them by file name, and users install their own
  the same way.
- **Languages** (`Languages/*.xaml`, at the repository root): loaded by file name. `English.xaml` is the fallback, so
  every new string key is added to `English.xaml`; other languages fall back to it until translated.

The `Languages` folder is at the repository root, not under `RetroBar/`. `RetroBar.csproj` links it into the build as
`Languages\<name>.xaml`, so the built app is unchanged: `English.xaml` is compiled in at pack path
`Languages/English.xaml`, and every other language is copied to `Languages\` beside the exe. A new language needs only
its file in `Languages/`; the csproj picks it up by wildcard. Do not move the folder back under `RetroBar/` without
removing that link, or every language would be included twice.

## 5. Settings have one declaration

Every setting is declared once in `RetroBar/Utilities/Settings.cs`, with its default as the field initializer, and is
saved by `SettingsManager` to `%LOCALAPPDATA%\RetroBar\settings.json` under the property's name. Renaming a settings
property abandons users' saved values; add a new one instead. Lists are replaced, never changed in place, so the change
is noticed and saved.

## 6. Verification

There is no local build in an agent session and no test suite. A change is verified by:

- the rules in this file, and
- the GitHub Actions build (`.github/workflows/retrobar.yml`), which builds every target: `net6.0-windows` (Windows 7
  and 8) and `net10.0-windows`, x86, x64 and ARM64. A change is not finished until that build succeeds.

Code must compile for both target frameworks: do not use APIs that `net6.0-windows` lacks.

## 7. Privacy

- No telemetry, analytics or crash reporting.
- The update checker is off by default and only fetches a version number when the user turns it on.
- Start menu search is local: programs from the Windows app list and files from the local Windows Search index.
  It shows no web results and sends nothing anywhere.

## 8. Retrostar behaviour that must be kept

- **Aero glass** is one switch, `EnableAeroGlass` (default on). It controls the taskbar and the Start menu together.
  While it is on, glass themes keep their glass when a window is maximized; while it is off they use the opaque
  Basic look (Vista Basic or Windows 7 Basic).
- **The glass is Windows' live transparency, with Aero's look and nothing of Windows' own.** Every glass window is a
  layered window (`AllowsTransparency`), which Windows' compositor draws over whatever is behind it, live: windows,
  video and the desktop show through as they change. This works on every supported Windows, does not depend on
  Windows' "Transparency effects" setting, and appears in screenshots and recordings as it is on screen.
  `AeroGlassHelper.PrepareGlassWindow` switches Windows' own treatments off on every glass window, and is called again
  after DWM, theme and setting changes: the accent policy is set to disabled (no Windows blur, tint or acrylic), and on
  Windows 11 the system backdrop (Mica or Acrylic), rounded corners and accent border are turned off. Never turn on
  any of them, a DWM blur-behind, or Windows' acrylic on a glass window. ManagedShell's `SetBlur` is always called with
  `false`, because its blur is the accent policy or the system backdrop. Never hide a glass window from capture.
- The glass window itself has no blur. Its look is the theme's tint, highlights and edges on the window's tint layer,
  drawn at `AeroGlassHelper.TintOpacity`.
- **Aero glass blur** (`AeroGlassBlur`, 0-100, default 40, Advanced tab slider under the transparency slider) is the
  only blur, and it is never on the glass window: `GlassBlurBackdrop` is a separate click-through window with Windows'
  plain blur-behind (no tint, noise or acrylic), kept directly beneath the glass window with its place, size, region
  and z-order, and drawn at the setting's opacity (`AeroGlassHelper.BlurStrength`), which blends the sharp and the
  blurred desktop. At 0, or with see-through glass off, it is hidden and the glass is exactly as without it. Windows 10
  and later. The taskbar, the Start menu and the Windows 7 window list each own one; a new glass window that should
  blur owns one too, sets its strength whenever its glass is updated, and disposes it when it closes. A window that
  changes its region outside a move calls `Sync` (or sets the strength again) afterwards.
- **Allow see-through glass** (`AllowBlurBehind`, a historical name) applies to every glass surface while Aero glass is
  on. On, the tint is drawn at `AeroGlassHelper.GlassStrength` (0.6), faded further by the transparency slider, so
  what is behind shows clearly. Off, the tint is drawn as measured on Vista, near-opaque.
- **Glass transparency** is one setting, `AeroGlassTransparency` (0-100, default 0, Advanced tab slider), and it
  governs every glass surface: 0% is Aero's look, 100% is clear glass. A glass surface is any window that shows glass,
  now or in the future. Each one must:
  - be a layered window and call `AeroGlassHelper.PrepareGlassWindow` whenever its glass is shown;
  - paint its glass tint (the background and outline of the glass) on a dedicated tint layer that holds no content,
    with the content drawn over it, so only the glass fades and never text, icons or buttons;
  - bind that layer's `Opacity` to its window's `GlassTintOpacity` property;
  - set `GlassTintOpacity` to `AeroGlassHelper.TintOpacity` while its glass is on and to 1 otherwise, so opaque looks
    (Basic, glass off) are never faded.

  Today these are the taskbar (`TaskbarTopBorder` layer in the Vista Aero, Longhorn Aero and Windows 7 Aero themes),
  the Start menu (`FrameTint`) and the Windows 7 window list (`Frame`). A new glass theme follows the
  same pattern in its `Taskbar` template; never fade a whole window or a layer that holds content. The taskbar's tint
  layer lies under the whole bar alike, Quick Launch and the notification area included; never give part of the bar a
  background of its own over the glass.
- The Start menu's glass window is exactly the size of its frame. The user picture is a separate window
  (`UserPictureWindow`) owned by the menu, so the glass never shows outside the frame. In Vista the picture sits inside
  the frame, 6 px below its top edge at the top of the right column, and the right column starts 73 px down, below it
  (Vista's frame is 538 px tall for that); in Windows 7 it overhangs the top edge (`ApplyLayout`, `PictureOffsetX/Y`).
  Do not move the picture back into the menu window. While the pointer is over a right-column item, the picture window shows that item's 48 px icon
  alone, on a transparent background with no frame, as Vista did: the frame and picture fade out as the icon fades in
  (`ShowItemIcon`), and back when no item is under the pointer (`ShowPicture`). The menu always opens with the picture.
- The Start orb is raised above the open menu, and a click on the part of the orb that overlaps the menu closes it.
- **Start menu scale** (`StartMenuScale`, 1-2, Start Menu tab) zooms the whole menu with a layout transform on
  `Frame` (`ApplyScale`); the window region, the picture window (`UserPictureWindow.SetScale`) and the picture offsets
  follow it. Any scaled surface uses Ideal text formatting and keeps Display only at exactly 100%, so text is laid out
  at its final size and stays sharp; the taskbar does the same through `SettingsToTextFormattingModeConverter`.
- While searching, the first result (the first program, or else the first file) is highlighted while the search box
  has the keyboard (`StartMenuEntry.IsDefaultResult`, `UpdateDefaultResult`), and Enter opens exactly that result.
- The Vista power button is red whatever its action (`PowerShutDownBackground`, measured from Vista's power button
  row by row with the hue turned to red); its tooltip names the action.
- The Start menu's scrolling lists use the Vista/Windows 7 Aero scrollbar (`FlatScrollViewer`'s scrollbar style in
  `VistaStartMenu.xaml`); it applies to those ScrollViewers only.
- Measured Vista geometry and colours in `VistaStartMenu.xaml`, `UserPictureWindow.xaml` and the Vista Aero theme
  come from pixel measurements of Vista screenshots; change them only from new measurements.
- **Windows 7.** The Windows 7 taskbar is the `Windows 7 Aero` theme only; it shows the opaque Basic look
  while Aero glass is off, so there is no separate Windows 7 Basic theme (a saved `Windows 7 Basic` setting becomes
  `Windows 7 Aero` in `Settings.Theme`, and the installer deletes the old file). Until the Windows 7 orb
  image is supplied, its Start orb is the Windows Vista orb, inherited from `Windows Vista Aero` (its images, size and
  floating window); do not draw an imitation orb. A theme that sets `UseSuperbar` gets the superbar (`SuperbarTaskList`) in place of `TaskList`; every other theme keeps
  `TaskList`, and only the one shown creates task buttons (`SetActive`). Under a superbar theme Quick Launch is never
  shown (`UpdateQuickLaunch`): Windows 7 has none, pinned programs are full-size superbar buttons and a program opens on
  its pinned button. The `ShowQuickLaunch` setting is kept and applies under every other theme.
- Vista sounds (`VistaSoundScheme`): the sound scheme is the current user's registry (`HKCU\AppEvents\Schemes\Apps`), so
  the sounds are only in place while Retrostar runs. Before changing any entry the original entries are written to
  `%LOCALAPPDATA%\RetroBar\vista-sounds-backup.json` (with the owning process id); `Restore` (on exit, on session end,
  or when `UseVistaSounds` is turned off), the watchdog process and the next `Apply` all restore from it, under the
  named mutex. Never change an entry that is not in the backup, never leave the backup out of step with the registry,
  and keep the watchdog free of windows and settings. A new event goes into the `Map` table with its file in
  `RetroBar/Resources/Sounds` (the csproj and the installer already copy that folder); events this Windows does not
  define are skipped. Start up, log on, log off and shut down are not mapped: Windows 8 and later do not play them
  while a program that started after sign-in is running.
- Vista icons (`VistaIconScheme`): only per-user overrides (`HKCU\Software\Classes\<ProgID or CLSID\{...}>\DefaultIcon`)
  of values that name Windows' own `imageres.dll` by resource ID (`imageres.dll,-ID`) where Vista's file has the same
  ID; Windows merges per-user and machine-wide classes value by value, so only the icon changes. Never write the
  machine-wide registry, never touch extension (`.ext`) keys or associations, never change a Windows file, and never
  run code from the Vista DLL (it is read as data only). Same backup, owner, watchdog and mutex rules as the sounds;
  Restore puts back only values that still hold Retrostar's, removes keys it created if they are left empty, and
  `SHChangeNotify(SHCNE_ASSOCCHANGED)` follows every apply and restore so Explorer redraws without a restart. Icons
  Windows draws from stock icons only (the plain folder on most Windows 11 builds) have no per-user override.
- Windows 7 input language: Windows 7 Aero hides the toolbar grip (`ToolbarThumb`, which Windows 7 only shows beside
  the language button since it has no Quick Launch) and centres the language name (`LanguageBar`, `InputLanguage`).
  Other themes keep theirs.
- Windows 7 notification area overflow: a theme that sets `UseNotifyIconOverflow` (only `Windows 7 Aero`) shows hidden
  icons in a flyout above the "show hidden icons" button (`OverflowPopup` in `NotifyIconList.xaml`) instead of expanding
  them into the bar; the button stays lit while it is open and its arrow never flips. The flyout's structure is the
  control's; its look is the theme's (`NotifyIconOverflowFrame`, `NotifyIconOverflowItems`, `NotifyIconOverflowCell`,
  `NotifyIconOverflowCustomize`, `NotifyIconOverflowGap`), with the taskbar as the frame's DataContext so the theme can
  bind `GlassTintOpacity` and `IsAeroGlassEnabled`. Customize opens Properties at the notification settings
  (`Taskbar.OpenCustomizeNotifications`). Themes without the key keep the old behaviour unchanged. The superbar combines each program's windows into one
  icon-only button, puts pinned programs first and shows the windows as thumbnails (glass) or as a list (Basic).
  Pins are `TaskbarPinnedItems`, using the Start menu's `file:`/`app:` keys; Windows' own taskbar pins are copied in
  once (`TaskbarPinsImported`) and never written back.
- The Windows 7 Start menu is the same window as Vista's, laid out by `ApplyLayout` when `StartMenuStyle` is Windows 7.
  `ApplyLayout` sets every value for both styles, so Vista keeps its measured layout exactly; keep it that way.
  Windows 7 has its own power button action (`Windows7PowerButtonAction`, default Shut down).
- Superbar buttons of programs with more than one window stack as Windows 7 does, inside the same 60 px button: 55 px
  plus one 5 px edge for two windows, 52 px plus two 4 px edges for three or more. The edges are copies of the button
  in its current state, clipped to the strip each exposes (`Stack1`, `Stack2` in the `SuperbarButton` template); never
  give them brushes of their own.
- Measured Windows 7 geometry and colours in the Windows 7 themes, the superbar templates and the Windows 7 parts of
  `VistaStartMenu.xaml` come from Windows 7 screenshots at 100% scale; change them only from new measurements.

## 9. Fork and license

Retrostar is a fork of RetroBar. Keep the original `LICENSE` and copyright notices.

## 10. Documentation

Update Part 1 of this file when an invariant changes, Part 2 (the index) when a category or module is added, removed,
renamed or moved, and `README.md` when something a user would read changes.

# Index for Agents

Navigation map for the two C# monoliths and the files around them. It holds no line numbers: find a category or module
with the searches below. Editing rules are in Part 1 of this file.

## Production C# layout

| Monolith | Holds |
|---|---|
| `RetroBar/Retrostar_Core.cs` | Everything without a XAML file: settings, persistence, theme and language loading, shell integration, input hooks, Aero glass, the Start menu engine. |
| `RetroBar/Retrostar_Interface.cs` | The code-behind of every XAML window and control, and the value converters used from XAML. |

There is no other first-party `.cs` file. Layout and appearance stay in `.xaml` files, which cannot be merged (see rule 4).

## Task routing

| Task | Where |
|---|---|
| A setting or its default | Core `[1100]` `RetroBar/Utilities/Settings.cs`, then its control in `PropertiesWindow.xaml` and its label in `Languages/English.xaml` |
| Taskbar behaviour (placement, auto-hide, glass, maximized look) | Interface `[5100]` `RetroBar/Taskbar.xaml.cs` |
| Taskbar appearance | `RetroBar/Themes/<theme>.xaml` (Vista look: `Windows Vista Aero.xaml`; Windows 7: `Windows 7 Aero.xaml`, which uses the Vista Start orb until the Windows 7 orb image is supplied) |
| Windows 7 taskbar buttons (combining, pinning, window list, reordering) | Interface `[5200]` `SuperbarTaskList.xaml.cs`, `SuperbarButton.xaml.cs`, `SuperbarWindowList.xaml.cs`; pins and grouping in Core `[1700]`; button look in the Windows 7 themes (`SuperbarButton`, `SuperbarButtonBasic`) |
| Start button and orb | Interface `[5200]` `StartButton.xaml.cs`, `FloatingStartButton.xaml.cs` |
| Start menu behaviour (views, search UI, pinning, right column, power strip, positioning, Vista or Windows 7 layout) | Interface `[6000]` `RetroBar/StartMenu/VistaStartMenu.xaml.cs` |
| Start menu look | `RetroBar/StartMenu/VistaStartMenu.xaml`, `RetroBar/StartMenu/UserPictureWindow.xaml` |
| Programs list, file search, icons, launching, power actions, Windows key | Core `[2000]` START MENU ENGINE |
| Aero glass (Windows' live transparency, Windows' own effects off, glass strength) | Core `[1600]` `AeroGlassHelper.cs`; used by `Taskbar.xaml.cs`, `VistaStartMenu.xaml.cs` and `SuperbarWindowList.xaml.cs` (rule 8) |
| Aero glass transparency (the Advanced tab slider) | Setting `AeroGlassTransparency` in Core `[1100]`; `AeroGlassHelper.TintOpacity`; each glass window's `GlassTintOpacity` and the tint layer bound to it (rule 8) |
| Properties window tabs | Interface `[5400]` `PropertiesWindow.xaml.cs` with `PropertiesWindow.xaml` |
| Tray, clock, input language | Interface `[5300]`; helpers in Core `[1500]` |
| Theme or language loading | Core `[1200]` `DictionaryManager.cs` |
| Build, artifacts, installer, package references | `.github/workflows/retrobar.yml`, `installer.iss`, `RetroBar/RetroBar.csproj`, `RetroBar/Properties/PublishProfiles/` |

Most features touch Core (settings, engine) and Interface (code-behind) and a XAML file; change all three together.

## Navigation

1. Pick the monolith from Task routing.
2. Search for a category marker, for example `[2000]` or `[5300]`.
3. Search for a module by its logical path: `RETROSTAR_SOURCE_BEGIN path="RetroBar/StartMenu/AppCatalog.cs"`.
   To list every module: `grep -n RETROSTAR_SOURCE_BEGIN RetroBar/Retrostar_*.cs`.
4. A XAML file's code-behind is the module whose path is the XAML path plus `.cs`
   (`RetroBar/Controls/Clock.xaml` -> module `RetroBar/Controls/Clock.xaml.cs` in the Interface monolith).
5. Each module is one `namespace ... { }` block with its own `using` directives at the start of the block.
6. Update this index when a category or module is added, removed, renamed or moved.

## Core map — `RetroBar/Retrostar_Core.cs`

### [1000] FOUNDATION
Assembly attributes, entry point, path and list helpers, logging.

- `RetroBar/AssemblyInfo.cs` — WPF theme resource location (assembly attribute)
- `RetroBar/Program.cs` — process entry point (StartupObject), single-instance and startup
- `RetroBar/Extensions/ManagementExtensions.cs` — global InLocalAppData / AddFrom string and list helpers
- `RetroBar/Utilities/ExePath.cs` — path of the running executable
- `RetroBar/Utilities/ManagedShellLogger.cs` — ManagedShell log setup and the log file

### [1100] SETTINGS
Every setting, its default, and settings.json persistence.

- `RetroBar/Utilities/Settings.cs` — Settings: every user setting and its declared default (taskbar, Aero glass, Start menu)
- `RetroBar/Utilities/SettingsManager.cs` — JSON load/save of settings.json in %LOCALAPPDATA%\RetroBar

### [1200] THEMES + LANGUAGES
Loading theme and language resource dictionaries by name.

- `RetroBar/Utilities/DictionaryManager.cs` — finds and merges Themes/*.xaml and Languages/*.xaml (built-in, AppData, beside the exe)

### [1300] SHELL INTEGRATION
Taskbar windows per screen, Explorer, the Windows Start menu, updates, clock flyout, volume.

- `RetroBar/Utilities/WindowManager.cs` — opens, reopens and finds the Taskbar window for each screen
- `RetroBar/Utilities/ExplorerMonitor.cs` — reacts to Explorer restarts and display changes
- `RetroBar/Utilities/StartMenuMonitor.cs` — watches and opens the Windows Start menu (used when the Vista menu is off)
- `RetroBar/Utilities/Updater.cs` — optional update check (off by default)
- `RetroBar/Utilities/ClockFlyoutLauncher.cs` — opens the Windows clock and calendar flyout
- `RetroBar/Utilities/VolumeChanger.cs` — system volume changes from the tray
- `RetroBar/Utilities/VistaIconScheme.cs` — Windows Vista's icons while Retrostar runs: per-user DefaultIcon overrides pointing Windows' imageres.dll icons at `Resources\VistaIcons\vista-imageres.dll` (same resource IDs), with backup, restore and the watchdog process (`--restore-vista-icons <pid>`, handled first in `Program.Main`)
- `RetroBar/Utilities/VistaSoundScheme.cs` — Windows Vista's sounds while Retrostar runs: points the user's sound scheme at `Resources\Sounds`, backs up and restores the original entries, and the watchdog process (`--restore-vista-sounds <pid>`, handled first in `Program.Main`)

### [1400] INPUT
Hotkeys, tray hotkeys, low-level mouse hook, delayed activation.

- `RetroBar/Utilities/HotkeyManager.cs` — Win+number taskbar hotkeys
- `RetroBar/Utilities/TrayHotkey.cs` — Explorer tray hotkey table
- `RetroBar/Utilities/LowLevelMouseHook.cs` — low-level mouse hook used to drag the unlocked taskbar
- `RetroBar/Utilities/DelayedActivationHandler.cs` — hover-to-activate timing for drag and drop

### [1500] NOTIFICATION AREA + TOOLBARS
Tray icon helpers, drag and drop handlers, Quick Launch sorting.

- `RetroBar/Extensions/NotifyIconExtensions.cs` — tray icon identity, pinning and invert helpers
- `RetroBar/Utilities/NotifyIconDropHandler.cs` — drag and drop reordering of tray icons
- `RetroBar/Utilities/ToolbarDropHandler.cs` — drag and drop onto toolbars
- `RetroBar/Utilities/ToolbarSorter.cs` — Quick Launch item order

### [1600] VISUAL EFFECTS
Aero glass on Windows' live transparency, and the icon invert shader.

- `RetroBar/Utilities/AeroGlassHelper.cs` — Aero glass: glass strength, tint opacity and blur strength, and Windows' own treatments switched off on glass windows (accent policy, Windows 11 backdrop, corners, border)
- `RetroBar/Utilities/GlassBlurBackdrop.cs` — the adjustable blur: a click-through blur window kept beneath a glass window, at the Aero glass blur setting's opacity
- `RetroBar/Utilities/InvertEffect.cs` — pixel shader that inverts tray icons

### [1700] SUPERBAR
The Windows 7 taskbar's pinned programs and the grouping of windows into buttons; no windows.

- `RetroBar/Superbar/TaskbarPins.cs` — pinned programs: one-time import of Windows' pins, pin/unpin, resolving shortcuts and Apps folder IDs, matching windows
- `RetroBar/Superbar/SuperbarModel.cs` — SuperbarItem (one button: pin, windows, state, progress, hover colour) and SuperbarModel (the ordered buttons of one taskbar)

### [2000] START MENU ENGINE
Programs, search, icons, launching, the Windows key; no windows.

- `RetroBar/StartMenu/StartMenuNative.cs` — Win32 declarations used by the Start menu
- `RetroBar/StartMenu/StartMenuEntry.cs` — one item of the menu: program, folder, search result or link
- `RetroBar/StartMenu/ShellIcons.cs` — system image list icons; StaTask for shell work on STA threads
- `RetroBar/StartMenu/AppCatalog.cs` — All Programs tree, the Apps folder list (Store apps) and Windows usage counts
- `RetroBar/StartMenu/FileSearch.cs` — file search through the local Windows Search index (no web results)
- `RetroBar/StartMenu/StartMenuLauncher.cs` — launching, default browser/mail entries, lock, sleep, restart, shut down
- `RetroBar/StartMenu/WindowsKeyHook.cs` — lone Windows key and Ctrl+Esc open the Vista menu instead of Windows' own
- `RetroBar/StartMenu/StartMenuManager.cs` — owns the menu: opens it from the orb or the key, keeps the catalog loaded

## Interface map — `RetroBar/Retrostar_Interface.cs`

### [5000] APPLICATION
App lifecycle: startup, theme and language loading, shutdown.

- `RetroBar/App.xaml.cs` — App: creates the services, the Start menu manager and the taskbars; exit and crash handling

### [5100] TASKBAR WINDOW
The taskbar app bar window.

- `RetroBar/Taskbar.xaml.cs` — Taskbar: app bar placement, auto-hide, Aero glass, maximized look, dragging

### [5200] TASKBAR CONTROLS
Start button, task list, toolbars, Show Desktop.

- `RetroBar/Controls/StartButton.xaml.cs` — Start button: opens the Vista menu or Windows' own
- `RetroBar/Controls/FloatingStartButton.xaml.cs` — the orb window drawn over the taskbar edge
- `RetroBar/Controls/TaskList.xaml.cs` — task button list and scrolling
- `RetroBar/Controls/TaskButton.xaml.cs` — one task button: click, middle-click, wheel, drag
- `RetroBar/Controls/TaskThumbnail.xaml.cs` — live window thumbnail (DWM)
- `RetroBar/Controls/SuperbarTaskList.xaml.cs` — Windows 7 superbar: clicks, Win+number, hover window list, drag reordering
- `RetroBar/Controls/SuperbarButton.xaml.cs` — one superbar button: style (glass or Basic), hover glow, menu, minimize-animation rect
- `RetroBar/Controls/SuperbarWindowList.xaml.cs` — a button's windows as DWM thumbnails or a list; never takes focus
- `RetroBar/Controls/Toolbar.xaml.cs` — Quick Launch and other folder toolbars
- `RetroBar/Controls/ToolbarButton.xaml.cs` — one toolbar button
- `RetroBar/Controls/ShowDesktopButton.xaml.cs` — Show Desktop button and Peek

### [5300] NOTIFICATION AREA + CLOCK + INPUT LANGUAGE
Tray icons, balloons, clock, keyboard language.

- `RetroBar/Controls/NotifyIconList.xaml.cs` — the notification area icon list
- `RetroBar/Controls/NotifyIcon.xaml.cs` — one tray icon
- `RetroBar/Controls/NotifyBalloon.xaml.cs` — tray balloon notifications
- `RetroBar/Controls/Clock.xaml.cs` — taskbar clock and its format
- `RetroBar/Controls/InputLanguage.xaml.cs` — input language indicator
- `RetroBar/Controls/InputLanguageMenuItem.xaml.cs` — one input language menu item
- `RetroBar/Controls/JapaneseIme.xaml.cs` — Japanese IME mode indicator

### [5400] PROPERTIES WINDOWS
Properties (Taskbar, Notification area, Advanced, Start Menu tabs) and notification settings.

- `RetroBar/PropertiesWindow.xaml.cs` — PropertiesWindow: all tabs' code, including the Start Menu tab
- `RetroBar/NotificationPropertiesWindow.xaml.cs` — per-icon notification behaviour window

### [5500] VALUE CONVERTERS
IValueConverter classes used from XAML.

- `RetroBar/Converters/BoolToIntConverter.cs` — bool to int
- `RetroBar/Converters/BoolToInvertedBoolConverter.cs` — bool to !bool
- `RetroBar/Converters/BoolToInvertedVisibilityConverter.cs` — bool to Visibility, inverted
- `RetroBar/Converters/BoolToTextRenderingModeConverter.cs` — font smoothing setting to TextRenderingMode
- `RetroBar/Converters/BoolToVisibilityConverter.cs` — bool to Visibility
- `RetroBar/Converters/CountToIndexConverter.cs` — row count to combo index
- `RetroBar/Converters/CultureInfoToLocaleNameConverter.cs` — culture to locale name
- `RetroBar/Converters/DockOrientationConverter.cs` — taskbar orientation to Dock
- `RetroBar/Converters/DoubleToPercentConverter.cs` — scale to percent text
- `RetroBar/Converters/EdgeOrientationConverter.cs` — taskbar edge to Orientation
- `RetroBar/Converters/EdgeOrientationVisibilityConverter.cs` — taskbar edge to Visibility
- `RetroBar/Converters/EnumConverter.cs` — enum to int and back (settings combos)
- `RetroBar/Converters/IconListOrientation.cs` — tray icon list orientation
- `RetroBar/Converters/NewLineToSpaceConverter.cs` — newlines to spaces in labels
- `RetroBar/Converters/NotifyIconBehaviorConverter.cs` — tray icon behaviour setting
- `RetroBar/Converters/NotifyIconCanInvertConverter.cs` — whether a tray icon may be inverted
- `RetroBar/Converters/SettingsToTextFormattingModeConverter.cs` — scale and smoothing to TextFormattingMode
- `RetroBar/Converters/TaskButtonStyleConverter.cs` — task state to task button style
- `RetroBar/Converters/TaskButtonWidthConverter.cs` — task button width
- `RetroBar/Converters/TaskLabelConverter.cs` — task label with progress percentage
- `RetroBar/Converters/ToolTipHorizontalOffsetConverter.cs` — tooltip horizontal offset by edge
- `RetroBar/Converters/ToolTipPlacementConverter.cs` — tooltip placement by edge
- `RetroBar/Converters/ToolTipVerticalOffsetConverter.cs` — tooltip vertical offset by edge

### [6000] START MENU WINDOWS
The Vista Start menu and its user picture.

- `RetroBar/StartMenu/VistaStartMenu.xaml.cs` — VistaStartMenu: views, search, pinning, right column, power strip, glass, orb layering; the Windows 7 layout (`ApplyLayout`)
- `RetroBar/StartMenu/UserPictureWindow.xaml.cs` — user picture frame, a separate window owned by the menu; shows the hovered right-column item's icon without the frame


## Files that are not C#

| Path | What it is |
|---|---|
| `RetroBar/App.xaml` | Application resources and startup |
| `RetroBar/Taskbar.xaml` | Taskbar window |
| `RetroBar/PropertiesWindow.xaml` | Properties window, all tabs including Start Menu |
| `RetroBar/NotificationPropertiesWindow.xaml` | Notification icon settings |
| `RetroBar/Controls/*.xaml` | Taskbar controls; each pairs with a module in Interface `[5200]`/`[5300]` |
| `RetroBar/StartMenu/VistaStartMenu.xaml` | Start menu layout, brushes (glass and Basic), styles, for Vista and Windows 7 |
| `RetroBar/StartMenu/UserPictureWindow.xaml` | User picture frame |
| `RetroBar/Themes/*.xaml` | Themes, loaded by file name at runtime; users can install more. `UseSuperbar` turns on the Windows 7 superbar |
| `Languages/*.xaml` | UI strings at the repository root, linked into the build by `RetroBar.csproj`; loaded by file name; `English.xaml` is the fallback for every key |
| `RetroBar/Resources/` | Icons, images, compiled invert shader |
| `RetroBar/app.manifest` | Windows compatibility and privileges (`asInvoker`) |
| `RetroBar/RetroBar.csproj` | Targets `net6.0-windows` (Windows 7/8) and `net10.0-windows`; every package reference, including Nerdbank.GitVersioning |
| `RetroBar/Properties/PublishProfiles/` | x86, x64, ARM64 and portable publish profiles |
| `.github/workflows/retrobar.yml` | Builds every target and uploads the artifacts; runs on push or pull request to `master`, or manually |
| `installer.iss` | Inno Setup installer |
| `version.json` | Versioning (Nerdbank.GitVersioning; its package reference is in `RetroBar.csproj`) |
| `shader_invert.hlsl` | Source of the invert shader, with its `fxc` compile command in the header comment |
| `Rules_For_Agent_Editors.md` | This file: the rules (Part 1) and the index (Part 2) |
