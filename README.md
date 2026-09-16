# Prompt Manager

Windows tray utility for fast, filename-only search over a local folder — built for an Obsidian
vault, but works over any folder of markdown files. A global hotkey opens a Spotlight-style search
popup over whatever app has focus — search, and <kbd>Enter</kbd> to copy the matched note's
content to the clipboard, then paste it (<kbd>Ctrl+V</kbd>) wherever you need it. Runs at startup,
lives in the tray.

Default hotkey: **Alt+Shift+J** (rebindable from Settings).

## Features

- **Global hotkey** opens a borderless popup on top of whatever you're doing; <kbd>Esc</kbd>
  dismisses it with no side effects.
- **Fuzzy search by filename** (VS Code Quick Open-style) — matches and ranks on the file name
  only, not file content. Each result shows its containing folder to disambiguate same-named
  files in different places.
- **Copy on Enter** — <kbd>Enter</kbd> (or click) reads the selected file's content fresh from
  disk and copies it to the clipboard; paste manually with <kbd>Ctrl+V</kbd>.
- **Read-only** — this app never writes to your vault. Creating, editing, and deleting notes stays
  in Obsidian (or your editor of choice); the popup re-scans the vault folder every time it opens,
  so it always reflects whatever's on disk.
- **Settings** — point the app at a vault folder, rebind the global hotkey, toggle "Start with
  Windows".

## Requirements

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Build & run

```powershell
dotnet build CodeSnippet\CodeSnippet.slnx
dotnet run --project CodeSnippet\CodeSnippet\CodeSnippet.csproj
```

The app has no visible main window on launch — it registers the global hotkey and puts an icon in
the system tray. Right-click the tray icon for **Search vault**, **Settings...**, and **Exit**.

## Release

Push a `v*` tag to trigger [Build](.github/workflows/build.yml), which builds, publishes, zips,
and creates a GitHub Release with the zip attached:

```bash
git tag v1.0.0
git push origin v1.0.0
```

## Usage

- First run: open **Settings** (tray menu, or <kbd>Enter</kbd> on the popup's first-run screen)
  and choose your vault folder.
- Press the global hotkey to open the popup. Type to filter by filename,
  <kbd>&uarr;</kbd>/<kbd>&darr;</kbd> to move the selection, <kbd>Enter</kbd> to copy the selected
  note's content, <kbd>Esc</kbd> to close.
- **Settings...** (tray menu) changes the vault folder, rebinds the hotkey, and toggles "start
  with Windows".

## Data

Only `.md` files under the configured vault folder are indexed, by name; folders starting with a
dot (`.obsidian`, `.trash`, `.git`, ...) are skipped. Nothing about the vault is persisted by this
app — it's re-scanned from disk every time the popup opens, so external edits (Obsidian, sync,
git) are always picked up. App settings (vault path, hotkey binding) are stored as JSON at
`%AppData%\CodeSnippet\settings.json`; writes happen on a background task so no UI action blocks
on disk I/O.

## Notes

- Built with WPF + `CommunityToolkit.Mvvm` (source-generated bindings/commands, no reflection) and
  `H.NotifyIcon.Wpf` for the tray icon.
- The `screenshots/` folder and this doc's old screenshot embeds show the previous prompt-library
  UI (inline edit form, tag pills, preview pane); they're pending a retake for the vault-search
  popup.
