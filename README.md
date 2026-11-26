![Retrostar screenshot](retrobar-preview.png)

# Retrostar New

Retrostar is a fork of [RetroBar](https://github.com/dremin/RetroBar). It replaces the modern Windows taskbar with the classic Windows 95, 98, Me, 2000, XP, Vista, or 7 look, and adds a Vista- or Windows 7-style Start menu with Aero glass.

It is built on the [ManagedShell](https://github.com/cairoshell/ManagedShell) library.

## What Retrostar adds
- Vista-style Start menu with pinned programs, All Programs, a right-hand column, and a power strip
- Local Start menu search of programs and files, with no web results
- Aero glass for the taskbar and Start menu: live see-through glass from Windows' own compositor, with Windows' own tint and Windows 11 materials switched off, a transparency slider and an adjustable blur behind the glass (Windows 10 and later)
- Start menu scale, from 100% to 200%, with sharp text at every size
- Start orb that sits above the open menu
- Windows key and Ctrl+Esc open the Vista menu
- Windows 7 taskbar (Windows 7 Aero theme): icon-only buttons that combine each program's windows, pinned programs, live thumbnails, a hover glow in the icon's colour, and a Show Desktop strip
- Windows Vista icons (Properties > Advanced): Windows' default icons, desktop icons, special folders and stock folder and drive icons come from Vista's imageres.dll, and every folder is drawn as Vista's folder instead of a Windows 11 folder thumbnail; they stay until the setting is turned off or Retrostar is uninstalled, which puts Windows' own back exactly
- Windows Vista sounds while Retrostar runs (Properties > Advanced): Windows' own sounds return when it closes
- Windows 7 Start menu, chosen in Properties > Start Menu: search box in the program list, Devices and Printers, and a Shut down button

## Features
- Classic taskbar with a native notification area, task list, and Quick Launch toolbar
- Taskbar on any side of the screen, resizable, with multiple rows and multiple monitors
- Auto-hide, locked and unlocked appearances, and Vista-style window thumbnails
- Input language switcher
- Themes from Windows 95 through Vista, plus custom theme and language support

## Requirements
- Windows 7 SP1, Windows 8.1, Windows 10, or Windows 11
- Microsoft .NET Desktop Runtime. The installer downloads it if needed; the portable version prompts you on first launch.

Installer and portable builds are produced by the GitHub Actions workflow.

## Custom themes and languages
Install community theme files in Properties > Advanced, or place `.xaml` files in a `Themes` or `Languages` folder in `%localappdata%\RetroBar`. Themes use the XAML `ResourceDictionary` format; the files in `RetroBar/Themes` are good examples.

## Privacy
No telemetry, analytics, or crash reporting. The update checker is off by default.

## Contributing
Read `Rules_For_Agent_Editors.md` first. It holds the layout rules and a navigation index.

## License
Retrostar is a fork of RetroBar by Sam Johnson. See `LICENSE` for the original license and copyright notice.
