using System.Runtime.InteropServices;
using ScreenshotCat.Interop;

namespace ScreenshotCat.Services;

public sealed class TrayService : IDisposable
{
    private const uint WmTrayIcon = NativeMethods.WmApp + 1;
    private const uint TrayIconId = 1;
    private const uint CmdShow = 1001;
    private const uint CmdCapture = 1002;
    private const uint CmdExit = 1003;

    private readonly NativeMethods.WndProc _wndProc;
    private readonly nint _hwnd;
    private readonly nint _iconHandle;
    private readonly ushort _classAtom;
    private bool _visible;
    private bool _disposed;

    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? CaptureRequested;

    public TrayService(string iconPath)
    {
        _wndProc = WndProc;
        _classAtom = NativeMethods.RegisterWindowClass("ScreenshotCatTrayWindow", _wndProc);
        _hwnd = NativeMethods.CreateMessageWindow(_classAtom, "ScreenshotCatTray");
        if (_hwnd == 0)
        {
            throw new InvalidOperationException("Failed to create tray message window.");
        }

        _iconHandle = NativeMethods.LoadIconFromFile(iconPath);
        AddOrModifyIcon(add: true);
        _visible = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_visible)
        {
            var data = CreateNotifyIconData(includeMessage: false);
            _ = NativeMethods.Shell_NotifyIcon(NativeMethods.NimDelete, ref data);
            _visible = false;
        }

        if (_iconHandle != 0)
        {
            _ = NativeMethods.DestroyIcon(_iconHandle);
        }

        if (_hwnd != 0)
        {
            _ = NativeMethods.DestroyWindow(_hwnd);
        }

        if (_classAtom != 0)
        {
            _ = NativeMethods.UnregisterClass(new nint(_classAtom), NativeMethods.GetModuleHandle(null));
        }
    }

    public void SetToolTip(string text)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var data = CreateNotifyIconData(includeMessage: false, tip: text);
        _ = NativeMethods.Shell_NotifyIcon(NativeMethods.NimModify, ref data);
    }

    private void AddOrModifyIcon(bool add)
    {
        var data = CreateNotifyIconData(includeMessage: true, tip: "ScreenshotCat");
        var message = add ? NativeMethods.NimAdd : NativeMethods.NimModify;
        if (!NativeMethods.Shell_NotifyIcon(message, ref data) && add)
        {
            throw new InvalidOperationException("Failed to create tray icon.");
        }
    }

    private NativeMethods.NOTIFYICONDATA CreateNotifyIconData(bool includeMessage, string? tip = null)
    {
        return new NativeMethods.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = TrayIconId,
            uFlags = NativeMethods.NifIcon | NativeMethods.NifTip | (includeMessage ? NativeMethods.NifMessage : 0),
            uCallbackMessage = WmTrayIcon,
            hIcon = _iconHandle,
            szTip = tip ?? "ScreenshotCat"
        };
    }

    private nint WndProc(nint hWnd, uint msg, nuint wParam, nint lParam)
    {
        if (msg == WmTrayIcon && wParam == TrayIconId)
        {
            var mouseMsg = (uint)lParam & 0xFFFF;
            switch (mouseMsg)
            {
                case NativeMethods.WmLButtonDblClk:
                    ShowRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case NativeMethods.WmRButtonUp:
                case NativeMethods.WmContextMenu:
                    ShowContextMenu();
                    break;
            }

            return 0;
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            _ = NativeMethods.AppendMenu(menu, NativeMethods.MfString, new nuint(CmdShow), "打开界面");
            _ = NativeMethods.AppendMenu(menu, NativeMethods.MfString, new nuint(CmdCapture), "立即截图");
            _ = NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, nuint.Zero, string.Empty);
            _ = NativeMethods.AppendMenu(menu, NativeMethods.MfString, new nuint(CmdExit), "退出");

            _ = NativeMethods.GetCursorPos(out var point);
            _ = NativeMethods.SetForegroundWindow(_hwnd);
            var command = NativeMethods.TrackPopupMenu(
                menu,
                NativeMethods.TpmRightButton | NativeMethods.TpmReturnCmd,
                point.X,
                point.Y,
                0,
                _hwnd,
                0);
            // Required so the menu dismisses correctly.
            _ = NativeMethods.PostMessage(_hwnd, 0, 0, 0);

            switch (command)
            {
                case CmdShow:
                    ShowRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CmdCapture:
                    CaptureRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CmdExit:
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        finally
        {
            _ = NativeMethods.DestroyMenu(menu);
        }
    }
}
