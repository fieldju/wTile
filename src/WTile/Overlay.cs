// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;
using WTile.Core;

namespace WTile;

/// The see-through mini-grid panel centered on the target's monitor.
/// Topmost, click-through, never activated — focus stays on the target window.
internal static unsafe class Overlay
{
    const string ClassName = "wTileOverlay";
    const byte Opacity = 225;

    static HWND s_hwnd;
    static Session? s_session;
    static double s_scale = 1;
    static HFONT s_font;
    static int s_fontDpi;

    // Colors (COLORREF is 0x00BBGGRR).
    static COLORREF Rgb(byte r, byte g, byte b) => new((uint)(r | (g << 8) | (b << 16)));
    static readonly COLORREF Panel = Rgb(32, 32, 36);
    static readonly COLORREF Cell = Rgb(62, 62, 70);
    static readonly COLORREF Selected = Rgb(53, 132, 228);
    static readonly COLORREF Text = Rgb(220, 220, 225);
    static readonly COLORREF Dim = Rgb(140, 140, 150);

    public static void RegisterClass()
    {
        fixed (char* cls = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = App.Instance,
                lpszClassName = cls,
            };
            PInvoke.RegisterClassEx(&wc);
        }
    }

    public static void Show(Session session, HMONITOR monitor, PixelRect area)
    {
        s_session = session;
        uint dpiX = 96, dpiY = 96;
        PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, &dpiX, &dpiY);
        s_scale = dpiX / 96.0;

        // Panel: grid preview with the monitor's aspect ratio, plus a text footer.
        int pad = S(14);
        int gridW = S(320);
        int gridH = (int)(gridW * (double)area.Height / Math.Max(1, area.Width));
        int footer = S(52);
        int w = gridW + 2 * pad, h = gridH + 2 * pad + footer;
        int x = area.X + (area.Width - w) / 2, y = area.Y + (area.Height - h) / 2;

        if (s_hwnd.IsNull)
        {
            fixed (char* cls = ClassName)
            {
                s_hwnd = PInvoke.CreateWindowEx(
                    WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW |
                    WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_LAYERED |
                    WINDOW_EX_STYLE.WS_EX_TRANSPARENT,
                    cls, cls, WINDOW_STYLE.WS_POPUP, x, y, w, h,
                    HWND.Null, HMENU.Null, App.Instance, null);
            }
            PInvoke.SetLayeredWindowAttributes(s_hwnd, default, Opacity,
                LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);
        }

        PInvoke.SetWindowPos(s_hwnd, HWND.HWND_TOPMOST, x, y, w, h,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
        Invalidate();
    }

    public static void Hide()
    {
        s_session = null;
        if (!s_hwnd.IsNull) PInvoke.ShowWindow(s_hwnd, SHOW_WINDOW_CMD.SW_HIDE);
    }

    public static void Invalidate()
    {
        if (!s_hwnd.IsNull) PInvoke.InvalidateRect(s_hwnd, (RECT*)null, false);
    }

    static int S(int px) => (int)Math.Round(px * s_scale);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            switch (msg)
            {
                case PInvoke.WM_PAINT:
                    Paint(hwnd);
                    return default;
                case PInvoke.WM_ERASEBKGND:
                    return new LRESULT(1); // we paint everything; avoids flicker
                case PInvoke.WM_MOUSEACTIVATE:
                    return new LRESULT((nint)PInvoke.MA_NOACTIVATE);
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Overlay msg=0x{msg:X}: {ex}");
        }
        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    static void Paint(HWND hwnd)
    {
        PAINTSTRUCT ps;
        HDC screen = PInvoke.BeginPaint(hwnd, &ps);
        try
        {
            RECT client;
            PInvoke.GetClientRect(hwnd, &client);
            int w = client.right, h = client.bottom;

            // Double-buffer into a memory DC.
            HDC dc = PInvoke.CreateCompatibleDC(screen);
            HBITMAP bmp = PInvoke.CreateCompatibleBitmap(screen, w, h);
            HGDIOBJ oldBmp = PInvoke.SelectObject(dc, bmp);

            Fill(dc, client, Panel);
            if (s_session is { } s) DrawSession(dc, s, w, h);

            PInvoke.BitBlt(screen, 0, 0, w, h, dc, 0, 0, ROP_CODE.SRCCOPY);
            PInvoke.SelectObject(dc, oldBmp);
            PInvoke.DeleteObject(bmp);
            PInvoke.DeleteDC(dc);
        }
        finally
        {
            PInvoke.EndPaint(hwnd, &ps);
        }
    }

    static void DrawSession(HDC dc, Session s, int w, int h)
    {
        int pad = S(14), footer = S(52), gap = Math.Max(1, S(2));
        var area = new PixelRect(pad, pad, w - 2 * pad, h - 2 * pad - footer);
        var grid = s.Grid;
        var sel = s.Selection;

        for (int r = 0; r < grid.Rows; r++)
        for (int c = 0; c < grid.Cols; c++)
        {
            var cell = Grid.ToRect(new Selection(c, r, 1, 1), grid, area, 0);
            bool on = c >= sel.Col && c < sel.Col + sel.Cols && r >= sel.Row && r < sel.Row + sel.Rows;
            Fill(dc, new RECT(cell.X + gap, cell.Y + gap, cell.Right - gap, cell.Bottom - gap),
                on ? Selected : Cell);
        }

        EnsureFont();
        HGDIOBJ oldFont = PInvoke.SelectObject(dc, s_font);
        PInvoke.SetBkMode(dc, BACKGROUND_MODE.TRANSPARENT);

        // Line 1: presets, current one highlighted.
        int y1 = h - footer - pad / 2 + S(10);
        int x = pad;
        foreach (var g in s.Presets)
        {
            string label = g.ToString();
            PInvoke.SetTextColor(dc, g == grid ? Selected : Dim);
            x += DrawText(dc, label, x, y1) + S(14);
        }

        // Line 2: key help.
        PInvoke.SetTextColor(dc, Text);
        DrawText(dc, "arrows move · shift/ctrl+arrows resize · tab window · g grid · enter/esc",
            pad, y1 + S(20));

        PInvoke.SelectObject(dc, oldFont);
    }

    /// Draws single-line text at (x, y); returns its width.
    static int DrawText(HDC dc, string text, int x, int y)
    {
        var rc = new RECT(x, y, x + S(2000), y + S(40));
        fixed (char* p = text)
        {
            PInvoke.DrawText(dc, p, text.Length, &rc,
                DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_NOPREFIX | DRAW_TEXT_FORMAT.DT_CALCRECT);
            PInvoke.DrawText(dc, p, text.Length, &rc, DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_NOPREFIX);
        }
        return rc.right - rc.left;
    }

    static void EnsureFont()
    {
        int dpi = (int)Math.Round(96 * s_scale);
        if (!s_font.IsNull && s_fontDpi == dpi) return;
        if (!s_font.IsNull) PInvoke.DeleteObject(s_font);
        fixed (char* face = "Segoe UI")
        {
            s_font = PInvoke.CreateFont(-S(13), 0, 0, 0, 400 /* FW_NORMAL */, 0, 0, 0,
                FONT_CHARSET.DEFAULT_CHARSET, FONT_OUTPUT_PRECISION.OUT_DEFAULT_PRECIS,
                FONT_CLIP_PRECISION.CLIP_DEFAULT_PRECIS, FONT_QUALITY.CLEARTYPE_QUALITY,
                0, face);
        }
        s_fontDpi = dpi;
    }

    static void Fill(HDC dc, RECT rc, COLORREF color)
    {
        HBRUSH brush = PInvoke.CreateSolidBrush(color);
        PInvoke.FillRect(dc, &rc, brush);
        PInvoke.DeleteObject(brush);
    }
}
