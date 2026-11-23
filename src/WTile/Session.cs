// SPDX-License-Identifier: GPL-3.0-or-later
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using WTile.Core;

namespace WTile;

/// One open-overlay interaction: from Win+Enter until Enter/Esc.
///
/// Like kTile, the bare keys (arrows, Tab, g, Enter, Esc) are registered as
/// global hotkeys only while the overlay is open, then released. Focus never
/// leaves the target window, so you watch it snap live behind the overlay.
internal sealed unsafe class Session(HWND target)
{
    // Grid presets; TODO: load from config (Grid.ParseGrids).
    static readonly IReadOnlyList<GridSize> Grids = Core.Grid.DefaultGrids;
    static GridSize s_lastGrid = Grids[0]; // remembered across sessions

    enum Key
    {
        Left = 100, Right, Up, Down,
        ShiftLeft, ShiftRight, ShiftUp, ShiftDown,
        CtrlLeft, CtrlRight, CtrlUp, CtrlDown,
        Tab, NextGrid, Confirm, Cancel,
    }

    static readonly (Key id, HOT_KEY_MODIFIERS mods, VIRTUAL_KEY vk)[] Bindings =
    [
        (Key.Left, 0, VIRTUAL_KEY.VK_LEFT),
        (Key.Right, 0, VIRTUAL_KEY.VK_RIGHT),
        (Key.Up, 0, VIRTUAL_KEY.VK_UP),
        (Key.Down, 0, VIRTUAL_KEY.VK_DOWN),
        (Key.ShiftLeft, HOT_KEY_MODIFIERS.MOD_SHIFT, VIRTUAL_KEY.VK_LEFT),
        (Key.ShiftRight, HOT_KEY_MODIFIERS.MOD_SHIFT, VIRTUAL_KEY.VK_RIGHT),
        (Key.ShiftUp, HOT_KEY_MODIFIERS.MOD_SHIFT, VIRTUAL_KEY.VK_UP),
        (Key.ShiftDown, HOT_KEY_MODIFIERS.MOD_SHIFT, VIRTUAL_KEY.VK_DOWN),
        (Key.CtrlLeft, HOT_KEY_MODIFIERS.MOD_CONTROL, VIRTUAL_KEY.VK_LEFT),
        (Key.CtrlRight, HOT_KEY_MODIFIERS.MOD_CONTROL, VIRTUAL_KEY.VK_RIGHT),
        (Key.CtrlUp, HOT_KEY_MODIFIERS.MOD_CONTROL, VIRTUAL_KEY.VK_UP),
        (Key.CtrlDown, HOT_KEY_MODIFIERS.MOD_CONTROL, VIRTUAL_KEY.VK_DOWN),
        (Key.Tab, 0, VIRTUAL_KEY.VK_TAB),
        (Key.NextGrid, 0, VIRTUAL_KEY.VK_G),
        (Key.Confirm, 0, VIRTUAL_KEY.VK_RETURN),
        (Key.Cancel, 0, VIRTUAL_KEY.VK_ESCAPE),
    ];

    HWND _target = target;
    HMONITOR _monitor;
    PixelRect _area;
    List<HWND> _ring = [];
    readonly Dictionary<nint, WINDOWPLACEMENT> _original = [];
    bool _closed;

    public GridSize Grid { get; private set; } = s_lastGrid;
    public Selection Selection { get; private set; }
    public IReadOnlyList<GridSize> Presets => Grids;

    public void Open()
    {
        _monitor = WindowOps.MonitorOf(_target);
        _area = WindowOps.WorkArea(_monitor);
        // Frozen for the session: activating a window reorders z-order, so a
        // live list would ping-pong between the top two windows on Tab.
        _ring = WindowOps.ListTileable(_monitor);
        Target(_target);
        Arm();
        Overlay.Show(this, _monitor, _area);
    }

    void Target(HWND hwnd)
    {
        _target = hwnd;
        _original.TryAdd((nint)hwnd.Value, WindowOps.GetPlacement(hwnd));
        Reseed();
    }

    /// Seed the selection from the window's current geometry and snap to it.
    void Reseed()
    {
        var visible = WindowOps.ToPixelRect(WindowOps.GetVisibleRect(_target));
        Selection = Core.Grid.FromRect(visible, Grid, _area);
        Apply();
    }

    void Apply()
    {
        if (!PInvoke.IsWindow(_target)) { Cancel(); return; }
        WindowOps.MoveVisibleTo(_target, Core.Grid.ToRect(Selection, Grid, _area));
        Overlay.Invalidate();
    }

    public void OnKey(int id)
    {
        if (_closed) return;
        var s = Selection;
        var g = Grid;
        switch ((Key)id)
        {
            case Key.Left: Selection = Core.Grid.Move(s, g, Direction.West); break;
            case Key.Right: Selection = Core.Grid.Move(s, g, Direction.East); break;
            case Key.Up: Selection = Core.Grid.Move(s, g, Direction.North); break;
            case Key.Down: Selection = Core.Grid.Move(s, g, Direction.South); break;

            // Shift: the right/bottom edge moves in the arrow's direction.
            case Key.ShiftLeft: Selection = Core.Grid.ResizeEdge(s, g, Direction.East, -1); break;
            case Key.ShiftRight: Selection = Core.Grid.ResizeEdge(s, g, Direction.East, +1); break;
            case Key.ShiftUp: Selection = Core.Grid.ResizeEdge(s, g, Direction.South, -1); break;
            case Key.ShiftDown: Selection = Core.Grid.ResizeEdge(s, g, Direction.South, +1); break;

            // Ctrl: the left/top edge moves in the arrow's direction.
            case Key.CtrlLeft: Selection = Core.Grid.ResizeEdge(s, g, Direction.West, +1); break;
            case Key.CtrlRight: Selection = Core.Grid.ResizeEdge(s, g, Direction.West, -1); break;
            case Key.CtrlUp: Selection = Core.Grid.ResizeEdge(s, g, Direction.North, +1); break;
            case Key.CtrlDown: Selection = Core.Grid.ResizeEdge(s, g, Direction.North, -1); break;

            case Key.Tab: NextWindow(); return;
            case Key.NextGrid:
                Grid = s_lastGrid = Core.Grid.NextGrid(Grids, g);
                Reseed();
                return;
            case Key.Confirm: Confirm(); return;
            case Key.Cancel: Cancel(); return;
            default: return;
        }
        if (Selection != s) Apply();
    }

    void NextWindow()
    {
        _ring.RemoveAll(h => !PInvoke.IsWindow(h));
        if (_ring.Count < 2) return;
        int i = _ring.IndexOf(_target);
        var next = _ring[(i + 1) % _ring.Count];
        PInvoke.SetForegroundWindow(next);
        Target(next);
    }

    public void Confirm() => Close();

    /// Esc: restore every window this session touched.
    public void Cancel()
    {
        if (_closed) return;
        foreach (var (h, wp) in _original)
        {
            var hwnd = new HWND((void*)h);
            if (PInvoke.IsWindow(hwnd)) WindowOps.SetPlacement(hwnd, wp);
        }
        Close();
    }

    void Close()
    {
        if (_closed) return;
        _closed = true;
        Disarm();
        Overlay.Hide();
        App.SessionEnded(this);
    }

    void Arm()
    {
        foreach (var (id, mods, vk) in Bindings)
            if (!PInvoke.RegisterHotKey(App.Host, (int)id, mods, (uint)vk))
                Log.Write($"could not register {mods}+{vk} (in use by another app?)");
    }

    void Disarm()
    {
        foreach (var (id, _, _) in Bindings)
            PInvoke.UnregisterHotKey(App.Host, (int)id);
    }
}
