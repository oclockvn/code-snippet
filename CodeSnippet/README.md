# Prompt Manager

Windows tray utility for managing text prompts (add/update/delete), invoked by a global hotkey
into a Spotlight-style search popup. <kbd>Enter</kbd> pastes the selected prompt into whatever
window had focus before the hotkey. Runs at startup, lives in the tray. Import/export via JSON.

Default hotkey: **Ctrl+Alt+P** (rebindable from Settings).

## Requirements

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Build & run

```powershell
dotnet build CodeSnippet.sln
dotnet run --project CodeSnippet\CodeSnippet.csproj
```

The app has no visible main window on launch — it registers the global hotkey and puts an icon in
the system tray. Right-click the tray icon for **Search Prompts**, **Manage Prompts...**,
**Settings...**, and **Exit**.

## Usage

- Press the global hotkey to open the search popup, type to filter (title matches rank above body
  matches), use <kbd>&uarr;</kbd>/<kbd>&darr;</kbd> to move the selection, <kbd>Enter</kbd> to
  paste the selected prompt into the previously focused window, <kbd>Esc</kbd> to dismiss without
  side effects.
- **Manage Prompts...** opens a full add/update/delete screen, plus **Import...**/**Export...**
  for JSON round-tripping.
- **Settings...** lets you toggle "start with Windows" and rebind the global hotkey (click the
  hotkey box, then press the new combination).

## Data

Prompts are stored as JSON at `%AppData%\CodeSnippet\prompts.json`; app settings (hotkey binding)
at `%AppData%\CodeSnippet\settings.json`. Both load fully into memory at startup; writes happen
on a background task so no UI action blocks on disk I/O.

## Notes

- `SendInput`-based paste can be blocked by Windows UIPI when the target window is running
  elevated (as administrator) while Prompt Manager is not. In that case the prompt still lands on
  the clipboard even though the automatic paste doesn't fire — paste manually with Ctrl+V.
- Built with WPF + `CommunityToolkit.Mvvm` (source-generated bindings/commands, no reflection) and
  `H.NotifyIcon.Wpf` for the tray icon.
