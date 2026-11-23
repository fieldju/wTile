// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;
using WTile.Core;

namespace WTile;

/// Win32 window geometry helpers. All coordinates are physical pixels
/// (the process is Per-Monitor-V2 DPI aware).
internal static unsafe class WindowOps
{
    public static PixelRect ToPixelRect(RECT r) => new(r.left, r.top, r.right - r.left, r.bottom - r.top);

    /// The window's *visible* bounds. GetWindowRect includes the invisible ~7px
    /// resize border on Win10/11; DWM's extended frame bounds do not.
    public static RECT GetVisibleRect(HWND hwnd)
    {
        RECT r;
        if (PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
                &r, (uint)sizeof(RECT)).Succeeded)
            return r;
        PInvoke.GetWindowRect(hwnd, &r);
        return r;
    }

    public static HMONITOR MonitorOf(HWND hwnd) =>
        PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);

    /// Monitor work area (excludes the taskbar).
    public static PixelRect WorkArea(HMONITOR monitor)
    {
        var mi = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        PInvoke.GetMonitorInfo(monitor, &mi);
        return ToPixelRect(mi.rcWork);
    }

    /// Place the window so its *visible* frame lands exactly on `target`.
    public static void MoveVisibleTo(HWND hwnd, PixelRect target)
    {
        if (PInvoke.IsZoomed(hwnd) || PInvoke.IsIconic(hwnd))
            PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_RESTORE);

        RECT win;
        PInvoke.GetWindowRect(hwnd, &win);
        RECT vis = GetVisibleRect(hwnd);
        int l = vis.left - win.left, t = vis.top - win.top;
        int r = win.right - vis.right, b = win.bottom - vis.bottom;

        PInvoke.SetWindowPos(hwnd, HWND.Null,
            target.X - l, target.Y - t, target.Width + l + r, target.Height + t + b,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE |
            SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);
    }

    public static WINDOWPLACEMENT GetPlacement(HWND hwnd)
    {
        var wp = new WINDOWPLACEMENT { length = (uint)sizeof(WINDOWPLACEMENT) };
        PInvoke.GetWindowPlacement(hwnd, &wp);
        return wp;
    }

    public static void SetPlacement(HWND hwnd, WINDOWPLACEMENT wp) => PInvoke.SetWindowPlacement(hwnd, &wp);

    static bool IsCloaked(HWND hwnd)
    {
        int cloaked = 0;
        return PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED,
            &cloaked, sizeof(int)).Succeeded && cloaked != 0;
    }

    static string ClassName(HWND hwnd)
    {
        char* buf = stackalloc char[256];
        int n = PInvoke.GetClassName(hwnd, buf, 256);
        return new string(buf, 0, n);
    }

    /// Normal app window we can sensibly tile: visible, resizable, captioned,
    /// not a tool window, not the desktop/taskbar, not one of ours.
    public static bool IsTileable(HWND hwnd)
    {
        if (hwnd.IsNull || !PInvoke.IsWindow(hwnd) || !PInvoke.IsWindowVisible(hwnd)) return false;
        if (hwnd == PInvoke.GetShellWindow() || IsCloaked(hwnd)) return false;

        var style = (WINDOW_STYLE)(uint)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        var ex = (WINDOW_EX_STYLE)(uint)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        if ((ex & WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) != 0) return false;
        if ((style & WINDOW_STYLE.WS_THICKFRAME) == 0 && (style & WINDOW_STYLE.WS_CAPTION) != WINDOW_STYLE.WS_CAPTION)
            return false;

        return ClassName(hwnd) switch
        {
            "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" => false,
            "wTileHost" or "wTileOverlay" => false,
            _ => true,
        };
    }

    static List<HWND>? s_enumBuf;

    /// Tileable, unowned, non-minimized windows on `monitor`, in z-order (topmost first).
    public static List<HWND> ListTileable(HMONITOR monitor)
    {
        s_enumBuf = [];
        PInvoke.EnumWindows(&EnumProc, default);
        var all = s_enumBuf;
        s_enumBuf = null;
        return all.Where(h => !PInvoke.IsIconic(h)
                              && PInvoke.GetWindow(h, GET_WINDOW_CMD.GW_OWNER).IsNull
                              && IsTileable(h)
                              && MonitorOf(h) == monitor).ToList();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static BOOL EnumProc(HWND hwnd, LPARAM _)
    {
        s_enumBuf?.Add(hwnd);
        return true;
    }
}
