# wTile — project context for Claude

Windows port of [kTile](https://github.com/fieldju/kTile) (the user's KDE Plasma 6 KWin
script), which is itself a minimal take on GNOME's gTile. Same interaction model: a hotkey
opens a see-through grid overlay over the active window's monitor; bare arrows move the
selection, Shift+arrows move the right/bottom edge, Ctrl+arrows move the left/top edge,
Tab switches target window, `g` cycles grid presets, Enter confirms, Esc restores.
No auto-tiling. **Open items: [TODO.md](TODO.md).**

## Layout

- `src/WTile.Core/Grid.cs` — pure selection math, a port of kTile's `grid.js`
  (position+span cell model). Rounding mimics JS `Math.round` so results match kTile.
- `src/WTile/` — the Win32 app (WinExe, `net11.0-windows`, x64, Native AOT):
  - `App.cs` — entry point, single-instance mutex, hidden host window that owns every
    hotkey (`WM_HOTKEY`) and the tray icon; `Log` helper.
  - `Session.cs` — one overlay interaction: arms bare-key hotkeys on open, disarms on
    close, applies the selection, remembers original placements for Esc.
  - `Overlay.cs` — layered, topmost, click-through, no-activate panel painted with GDI.
  - `WindowOps.cs` — geometry: visible-frame bounds, work area, move, tileable filter.
  - `Tray.cs` — notification icon + Exit menu.
  - `NativeMethods.txt` / `.json` — CsWin32 API list; `allowMarshaling: false`.
- `tests/WTile.Tests` — xUnit port of kTile's `test/grid.test.js` (references Core only).

## Design decisions

- **Pure Win32 via CsWin32, no WPF/WinForms.** The only UI is the overlay, and this keeps
  Native AOT clean (single ~1.3 MB exe, fast startup, no runtime). With
  `allowMarshaling: false` everything is pointers/function pointers: window procs are
  `[UnmanagedCallersOnly]` statics, and **must catch all exceptions** (an exception
  crossing that boundary kills the process).
- **Bare keys via `RegisterHotKey`, not a low-level keyboard hook.** Mirrors kTile's
  dynamically-armed global shortcuts: registered on open, unregistered on close. Focus
  never leaves the target window, so it snaps live behind the overlay. Avoids LL-hook
  timeout/GC concerns.
- **Visible-frame geometry.** Win10/11 windows have an invisible ~7px resize border.
  `WindowOps.MoveVisibleTo` measures `GetWindowRect` vs `DWMWA_EXTENDED_FRAME_BOUNDS`
  and compensates so the *visible* frame lands exactly on the grid.
- **Per-Monitor-V2 DPI aware** (`app.manifest`): all coordinates are physical pixels;
  the overlay scales by `GetDpiForMonitor`.
- **Tab ring is frozen per session** (same lesson as kTile): activation reorders z-order,
  so a live list ping-pongs between two windows.
- **x64 pinned** (`PlatformTarget`/`RuntimeIdentifier`): CsWin32 can't generate the
  arch-specific `NOTIFYICONDATAW` for AnyCPU.

## Gotchas

- On the dev machine **`Win+Enter` is already registered by another process**
  (`RegisterHotKey` → 1409), so the app falls back to `Win+Alt+Enter`. Taking Win+Enter
  regardless would need a `WH_KEYBOARD_LL` hook (and suppressing the Start menu on Win
  release).
- **UIPI:** a non-elevated wTile cannot move elevated (admin) windows.
- Building from WSL works by calling `dotnet.exe` against the `C:` path; keep the repo on
  the Windows filesystem (WSL-side paths make builds slow and confuse VS).

## Dev loop

```powershell
dotnet test
dotnet publish src/WTile -c Release -o artifacts/publish
Stop-Process -Name wTile -EA SilentlyContinue; Start-Process artifacts\publish\wTile.exe
Get-Content $env:LOCALAPPDATA\wTile\wtile.log -Tail 20 -Wait
```
