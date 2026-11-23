// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WTile;

/// Process entry + the hidden host window that owns every hotkey and the tray icon.
internal static unsafe class App
{
    public const uint WM_TRAY = PInvoke.WM_APP + 1;
    const int HK_OPEN = 1;

    public static HWND Host { get; private set; }
    public static HINSTANCE Instance { get; private set; }
    static Session? s_session;

    [STAThread]
    static int Main()
    {
        using var single = new Mutex(true, @"Local\wTile-singleton", out bool first);
        if (!first) return 0;

        Instance = new HINSTANCE(PInvoke.GetModuleHandle((PCWSTR)null).Value);
        Host = CreateHostWindow();
        Overlay.RegisterClass();

        // Win+Enter matches kTile's Meta+Return; fall back if something else owns it.
        // TODO: make the trigger configurable.
        const HOT_KEY_MODIFIERS Win = HOT_KEY_MODIFIERS.MOD_WIN | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        string trigger;
        if (PInvoke.RegisterHotKey(Host, HK_OPEN, Win, (uint)VIRTUAL_KEY.VK_RETURN))
            trigger = "Win+Enter";
        else if (PInvoke.RegisterHotKey(Host, HK_OPEN, Win | HOT_KEY_MODIFIERS.MOD_ALT, (uint)VIRTUAL_KEY.VK_RETURN))
            trigger = "Win+Alt+Enter";
        else
        {
            Error("Neither Win+Enter nor Win+Alt+Enter could be registered (in use by another app).\nwTile will exit.");
            return 1;
        }

        Tray.Add(Host, $"wTile — {trigger} to tile");
        Log.Write($"wTile started; trigger {trigger}");

        MSG msg;
        while (PInvoke.GetMessage(&msg, HWND.Null, 0, 0).Value > 0)
        {
            PInvoke.TranslateMessage(&msg);
            PInvoke.DispatchMessage(&msg);
        }

        s_session?.Cancel();
        Tray.Remove(Host);
        PInvoke.UnregisterHotKey(Host, HK_OPEN);
        return 0;
    }

    public static void Quit() => PInvoke.PostQuitMessage(0);

    /// Called by a Session once it has closed (confirm or cancel).
    public static void SessionEnded(Session s)
    {
        if (ReferenceEquals(s_session, s)) s_session = null;
    }

    static HWND CreateHostWindow()
    {
        fixed (char* cls = "wTileHost")
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &HostProc,
                hInstance = Instance,
                lpszClassName = cls,
            };
            PInvoke.RegisterClassEx(&wc);
            // A real (never shown) top-level window rather than HWND_MESSAGE: the
            // tray menu needs a window that can become foreground.
            return PInvoke.CreateWindowEx(0, cls, cls, 0, 0, 0, 0, 0,
                HWND.Null, HMENU.Null, Instance, null);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static LRESULT HostProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            switch (msg)
            {
                case PInvoke.WM_HOTKEY:
                    OnHotkey((int)wParam.Value);
                    return default;
                case WM_TRAY:
                    Tray.OnMessage(hwnd, (uint)(lParam.Value & 0xFFFF));
                    return default;
                case PInvoke.WM_COMMAND:
                    if ((wParam.Value & 0xFFFF) == Tray.IDM_EXIT) Quit();
                    return default;
            }
        }
        catch (Exception ex)
        {
            // Exceptions must never cross an UnmanagedCallersOnly boundary.
            Log.Write($"HostProc msg=0x{msg:X}: {ex}");
        }
        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    static void OnHotkey(int id)
    {
        if (id == HK_OPEN)
        {
            if (s_session is not null)
            {
                s_session.Confirm(); // Win+Enter again toggles the overlay off
                return;
            }
            var target = PInvoke.GetForegroundWindow();
            if (!WindowOps.IsTileable(target))
            {
                Log.Write($"foreground 0x{(nint)target.Value:X} is not tileable");
                return;
            }
            s_session = new Session(target);
            s_session.Open();
            return;
        }
        s_session?.OnKey(id);
    }

    public static void Error(string text)
    {
        fixed (char* t = text)
        fixed (char* c = "wTile")
            PInvoke.MessageBox(HWND.Null, t, c, MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONERROR);
    }
}

internal static class Log
{
    static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wTile", "wtile.log");

    public static void Write(string line)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch { /* logging must never take the app down */ }
    }
}
