# wTile

A minimal, keyboard-driven window tiler for **Windows 10/11**, the Windows sibling of
[kTile](https://github.com/fieldju/kTile) (KDE) and inspired by the GNOME extension
[gTile](https://github.com/gTile/gTile).

1. Press `Win+Enter` (falls back to `Win+Alt+Enter` if something else owns it) to pop a
   **see-through grid overlay** on the active window's monitor.
2. **Arrow keys move** the window's tile selection around the grid.
3. **Shift / Ctrl + arrows resize** it.
4. Cycle through grid presets (8×6, 6×4, 4×4) on the fly.

The window snaps **live** on every keypress; you watch it move behind the overlay.
No auto-tiling.

## Shortcuts

| Shortcut | Action |
| --- | --- |
| `Win+Enter` / `Win+Alt+Enter` | Toggle the grid overlay on the active window |
| `←` `→` `↑` `↓` | Move the tile selection |
| `Shift` + arrow | Resize: the **right/bottom** edge moves in the arrow's direction |
| `Ctrl` + arrow | Resize: the **left/top** edge moves in the arrow's direction |
| `Tab` | Switch which window the grid targets (windows on the same monitor) |
| `g` | Cycle to the next grid preset |
| `Enter` | Confirm (keep the placement) |
| `Esc` | Cancel (restore every window touched) |

Arrows, `Tab`, `g`, `Enter` and `Esc` are grabbed **only while the overlay is open**.

## Build & run

Requires the .NET 11 SDK; publishing needs the Visual Studio C++ build tools (Native AOT linker).

```powershell
dotnet test                                                    # grid math unit tests
dotnet run --project src/WTile                                 # debug run
dotnet publish src/WTile -c Release -o artifacts/publish       # single native wTile.exe (~1.3 MB)
```

wTile lives in the notification area; right-click the icon → **Exit wTile**.
Logs: `%LOCALAPPDATA%\wTile\wtile.log`.

## License

GPL-3.0-or-later. See `LICENSE`.
