# Prompt Manager — Project Overview

## Purpose

Windows tray app for storing reusable AI-prompt snippets and inserting them anywhere via a global hotkey. Runs in background (system tray, no taskbar entry); no browser extension or app-specific integration — works with any app that accepts clipboard paste (ChatGPT, IDE, email, terminal, etc).

## Core workflow

1. App start minimized to tray, register a global hotkey (default configurable).
2. User press hotkey anywhere on the OS → floating search popup appear near screen center.
3. Type to fuzzy-filter saved prompts by title/body/tags, arrow keys to move selection.
4. Enter on selected item → prompt body copy to clipboard, popup close.
5. User paste (Ctrl+V) into whatever app has focus.

Popup also reachable via tray icon double-click or right-click menu.

## Windows / screens

- **Search Popup** — borderless, topmost, centered floating window. Single textbox (query) + list of matching results (title + truncated body preview). Keyboard-only: type, Up/Down, Enter, Esc. Hidden (not closed) between uses for instant reopen.
- **Manager window** — full CRUD screen for prompts: list of all prompts, edit pane (Title, Body, comma-separated Tags), New/Save/Delete buttons. Also has Import/Export (JSON file, via native file-picker dialogs) with a status-message line for feedback ("Saved.", "Imported N prompts.", errors).
- **Settings window** — hotkey capture control (press any modifier+key combo, shown live), start-with-Windows toggle.
- **Tray icon** — context menu: Search Prompts, Manage Prompts, Settings, Exit. Double-click = open search popup.

## Data model

`Prompt`: Id (guid), Title, Body (the actual snippet text), Tags (string array, optional). Persisted locally (JSON-backed repository), no cloud/sync.

## Non-UI mechanics worth knowing for a redesign

- Search popup is pre-warmed at startup (shown then immediately hidden) so first real hotkey press has no cold-start render lag — budget is sub-100ms show time. Any redesign should keep result-list virtualization/size cheap.
- Result list capped at 30 visible items for render-cost reasons.
- Manager/Settings windows are also hide-on-close (reused instance), not destroyed — reopening is instant.
- No mouse-required flows exist today — popup is 100% keyboard driven. A redesign could add mouse/click support but shouldn't remove keyboard-first usage since that is the app's whole value prop (fast, hands-on-keyboard access).
- App only recently had its paste mechanism simplified to plain clipboard-copy (no auto-paste/focus-stealing) — so "select item → clipboard has text, user pastes manually" is the accurate current interaction contract, not "instantly types into last app."

## What's explicitly NOT there today (fair game for redesign to add or ignore)

- No prompt categories/folders beyond free-text tags.
- No prompt preview/formatting (plain text only).
- No usage stats, favorites/pinning, or recency sorting.
- No light/dark theme toggle (fixed dark popup styling currently).
- No multi-select / bulk operations in Manager.
