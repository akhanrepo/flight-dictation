# Feature: System tray and app identity

## What it does
Gives Flight a visible home. Flight has no main window — it lives behind a hotkey and a toast — so today there is no way to tell it's running, reach Settings without dictating first, or quit it short of Task Manager. This feature adds an original Flight logo used everywhere the app appears, and a notification-area (tray) icon that becomes the app's control surface.

## Acceptance criteria

### Logo
- Flight has an original logo, kept as an SVG master in the repo (`assets/brand/`), with PNG exports and a multi-resolution `.ico` containing at least 16, 24, 32, 48, 64 and 256 px frames.
- The small sizes (16–32 px) use a simplified variant of the mark, so the icon stays legible in the tray and title bars rather than shrinking the full mark into a blur.
- The logo is the icon of `Flight.App.exe`, the installer (`FlightApp-win-Setup.exe`), the Start menu shortcut, the tray icon, and the Settings, Account and toast windows.

### Tray icon
- The tray icon is present for as long as Flight runs — from launch, including during the first-run model download — and is removed on exit, leaving no ghost icon behind.
- Hovering shows a tooltip naming the current status: Ready, Listening, Processing, Preview ready, Paused, Error, or Downloading models (with progress).
- The icon itself distinguishes three states at a glance: normal, listening (recording), and paused (dimmed).
- Left-click opens Settings.
- Right-click opens a menu with:
  - a status line (not clickable) matching the tooltip, e.g. "Ready — hold Right Alt to dictate";
  - **Pause dictation** / **Resume dictation**;
  - **Settings…**;
  - **Account & subscription…**;
  - **Start with Windows** (a check item);
  - **Restart to update (vX.Y.Z)**, shown only when an update has been downloaded;
  - **Quit Flight**.

### Pause
- While paused, pressing the push-to-talk key does nothing: no audio is captured and no toast appears.
- Pausing doesn't interrupt a dictation already in progress; it finishes normally.
- Pause is not remembered across restarts. Flight always starts active, so a forgotten pause can't look like a broken app.

### Quit
- Quit stops the hotkey and audio capture and removes the tray icon.
- A pending preview is discarded on quit and the clipboard is left untouched (the clipboard is written only on confirm).
- Queued vocabulary learning gets a bounded chance (a few seconds) to finish before exit, so confirmed dictations aren't silently lost from vocabulary building.
- A downloaded update is applied on quit, as it is on any exit.

### Start with Windows
- Toggling it adds or removes a per-user startup entry (no admin rights), and the check mark reflects whether the entry actually exists, not a remembered preference.
- Off by default. Available only on installed copies; on a dev build it is shown disabled.

### First run and single instance
- The first time Flight runs on a machine, a notification says it is running in the notification area and how to dictate. It is shown once, not on every launch.
- Launching Flight while it's already running doesn't start a second copy (two keyboard hooks would double-trigger dictation); the running copy opens Settings instead.

## Out of scope for this feature
- Hotkey remapping UI (the key stays Right Alt).
- A main window, dashboard, or dictation history view.
- Animated tray icons, or separate light/dark theme icon sets (the colored mark reads on both).
- Meeting capture and document generation (PRD phases 2–3).

## Notes for implementation
- Tray via `Shell_NotifyIcon` P/Invoke in `Flight.App/Win32`, alongside `LowLevelKeyboardHook`, rather than a new dependency. Menu content, status text, pause, quit sequencing, first-run and single-instance decisions live in `Flight.Core` so they're testable without WinUI.
- Start with Windows sits behind an interface, so tests never touch the real registry.
- The logo must be original work, not derived from an existing brand mark.
- **Manual-verification criteria** (can't be meaningfully unit-tested; checked by running the installed app and recorded in the PR/commit notes): legibility at 16 px, the icon actually showing in the taskbar, title bars and Start menu, and the absence of a ghost icon after quit. The `.ico` frame sizes and the project's icon wiring are covered by automated tests.
