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
    private const nuint RetryTimerId = 1;
    private const uint RetryIntervalMilliseconds = 2_000;
    private const int MaxRetryAttempts = 30;

    private readonly NativeMethods.WndProc _wndProc;
    private readonly nint _hwnd;
    private readonly nint _iconHandle;
    private readonly ushort _classAtom;
    private readonly uint _taskbarCreatedMessage;
    private bool _visible;
    private bool _retryTimerActive;
    private int _retryAttempts;
    private bool _disposed;

    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? CaptureRequested;

    public TrayService(string iconPath)
    {
        _wndProc = WndProc;
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _classAtom = NativeMethods.RegisterWindowClass("ScreenshotCatTrayWindow", _wndProc);
        _hwnd = NativeMethods.CreateHiddenWindow(_classAtom, "ScreenshotCatTray");
        if (_hwnd == 0)
        {
            throw new InvalidOperationException("Failed to create tray message window.");
        }

        _iconHandle = NativeMethods.LoadIconFromFile(iconPath);
        EnsureIcon();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopRetryTimer();
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
        if (_disposed || !_visible || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var data = CreateNotifyIconData(includeMessage: false, tip: text);
        _ = NativeMethods.Shell_NotifyIcon(NativeMethods.NimModify, ref data);
    }

    private bool EnsureIcon()
    {
        if (_visible)
        {
            var modifyData = CreateNotifyIconData(includeMessage: true, tip: "ScreenshotCat");
            if (NativeMethods.Shell_NotifyIcon(NativeMethods.NimModify, ref modifyData))
            {
                return true;
            }

            _visible = false;
        }

        var data = CreateNotifyIconData(includeMessage: true, tip: "ScreenshotCat");
        if (NativeMethods.Shell_NotifyIcon(NativeMethods.NimAdd, ref data))
        {
            _visible = true;
            _retryAttempts = 0;
            StopRetryTimer();
            return true;
        }

        ScheduleRetry();
        return false;
    }

    private void ScheduleRetry()
    {
        if (_disposed || _retryTimerActive || _retryAttempts >= MaxRetryAttempts)
        {
            return;
        }

        _retryAttempts++;
        _retryTimerActive = NativeMethods.SetTimer(
            _hwnd,
            RetryTimerId,
            RetryIntervalMilliseconds,
            0) != 0;
    }

    private void StopRetryTimer()
    {
        if (!_retryTimerActive)
        {
            return;
        }

        _ = NativeMethods.KillTimer(_hwnd, RetryTimerId);
        _retryTimerActive = false;
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
        if (msg == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            _visible = false;
            _retryAttempts = 0;
            StopRetryTimer();
            EnsureIcon();
            return 0;
        }

        if (msg == NativeMethods.WmTimer && wParam == RetryTimerId)
        {
            _retryTimerActive = false;
            _ = NativeMethods.KillTimer(_hwnd, RetryTimerId);
            EnsureIcon();
            return 0;
        }

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
