![Retrostar screenshot](retrobar-preview.png)

# Retrostar

Retrostar is a fork of [RetroBar](https://github.com/dremin/RetroBar). It replaces the modern Windows taskbar with the classic Windows 95, 98, Me, 2000, XP, Vista, or 7 look, and adds a Vista- or Windows 7-style Start menu with Aero glass.

It is built on the [ManagedShell](https://github.com/cairoshell/ManagedShell) library.

## What Retrostar adds
- Vista-style Start menu with pinned programs, All Programs, a right-hand column, and a power strip
- Local Start menu search of programs and files, with no web results
- Aero glass blur for the taskbar and Start menu, controlled by one setting
- Start orb that sits above the open menu
- Windows key and Ctrl+Esc open the Vista menu
- Windows 7 taskbar (Windows 7 Aero and Windows 7 Basic themes): icon-only buttons that combine each program's windows, pinned programs, live thumbnails, a hover glow in the icon's colour, and a Show Desktop strip
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
