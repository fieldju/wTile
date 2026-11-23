// SPDX-License-Identifier: GPL-3.0-or-later
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WTile;

/// Notification-area icon with a right-click "Exit" menu.
internal static unsafe class Tray
{
    public const uint IDM_EXIT = 1001;
    const uint IconId = 1;

    public static void Add(HWND host, string tip)
    {
        var nid = Base(host);
        nid.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON |
                     NOTIFY_ICON_DATA_FLAGS.NIF_TIP;
        nid.uCallbackMessage = App.WM_TRAY;
        nid.hIcon = PInvoke.LoadIcon(HINSTANCE.Null, PInvoke.IDI_APPLICATION);
        nid.szTip = tip;
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, &nid);
    }

    public static void Remove(HWND host)
    {
        var nid = Base(host);
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, &nid);
    }

    static NOTIFYICONDATAW Base(HWND host) => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW),
        hWnd = host,
        uID = IconId,
    };

    public static void OnMessage(HWND host, uint mouseMsg)
    {
        if (mouseMsg != PInvoke.WM_RBUTTONUP && mouseMsg != PInvoke.WM_CONTEXTMENU) return;

        HMENU menu = PInvoke.CreatePopupMenu();
        fixed (char* label = "Exit wTile")
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, IDM_EXIT, label);

        System.Drawing.Point pt;
        PInvoke.GetCursorPos(&pt);
        // Required so the menu dismisses when clicking elsewhere (KB135788).
        PInvoke.SetForegroundWindow(host);
        PInvoke.TrackPopupMenu(menu, TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON, pt.X, pt.Y, 0, host, (RECT*)null);
        PInvoke.PostMessage(host, PInvoke.WM_NULL, default, default);
        PInvoke.DestroyMenu(menu);
    }
}
