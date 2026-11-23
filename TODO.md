# wTile — TODO

The core loop is written and builds/publishes (Native AOT), but the **interactive
behaviour has not been verified by hand yet**.

## Verify first
- [ ] Overlay appears centered on the active window's monitor; arrows/Shift/Ctrl move and
      resize live; `g` cycles presets; Enter keeps; Esc restores.
- [ ] Maximized windows un-maximize and tile correctly.
- [ ] Tab cycles windows on the same monitor and Esc restores all of them.
- [ ] Mixed-DPI multi-monitor: overlay size and window placement on each monitor.
- [ ] Windows with min-size constraints / non-standard frames (Terminal, Chrome, Explorer,
      UWP/ApplicationFrameHost apps).

## Next
- [ ] **Trigger key.** Find what owns `Win+Enter` here; decide between a `WH_KEYBOARD_LL`
      hook to claim it anyway vs. a configurable trigger.
- [ ] **Config file** (`%APPDATA%\wTile\config.json`): grid presets (`Grid.ParseGrids`
      already exists), tile gap, trigger key. Use System.Text.Json source generation (AOT).
- [ ] Target outline (kTile's blue outline on the targeted window, esp. after Tab).
- [ ] `Alt+Esc` / `Alt+Shift+Esc` same-monitor window switcher (note: Alt+Esc is a
      built-in Windows shortcut; may need a hook).
- [ ] Re-add the tray icon on `TaskbarCreated` (Explorer restart).
- [ ] Close the session if the target window is destroyed or focus changes by mouse
      (`SetWinEventHook` `EVENT_SYSTEM_FOREGROUND`).
- [ ] Real app icon; start-with-Windows option (Run key or Startup shortcut).
- [ ] Elevated windows: document, or optional run-as-admin / uiAccess signed build.

## Later
- [ ] Center-in-tile mode, per-monitor presets, anchor-then-extend selection (kTile parity).
- [ ] CI (GitHub Actions: `dotnet test` + AOT publish artifact), releases, winget.
