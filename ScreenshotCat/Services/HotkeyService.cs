using ScreenshotCat.Interop;
using WinRT.Interop;

namespace ScreenshotCat.Services;

public sealed class HotkeyService : IDisposable
{
    private const int ScrollCaptureHotkeyId = 2101;
    private const int CtrlAltNCaptureHotkeyId = 2102;
    private readonly nint _hwnd;
    private readonly NativeMethods.SubclassProc _subclassProc;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardProc;
    private readonly AnnotationHotkeyGesture _annotationGesture = new();
    private readonly object _annotationGestureLock = new();
    private nint _keyboardHook;
    private Timer? _ctrlTabLongPressTimer;
    private long _lastHookTriggerTicks;
    private bool _scrollRegistered;
    private bool _ctrlAltNRegistered;
    private bool _subclassed;
    private volatile bool _annotationModeActive;

    public event EventHandler? CaptureRequested;
    public event EventHandler<AnnotationHotkeyGestureResult>? AnnotationHotkeyRequested;

    public bool AnnotationModeActive
    {
        get => _annotationModeActive;
        set => _annotationModeActive = value;
    }

    public HotkeyService(Microsoft.UI.Xaml.Window window)
    {
        _hwnd = WindowNative.GetWindowHandle(window);
        _subclassProc = WndProc;
        _keyboardProc = KeyboardProc;
    }

    public bool Register()
    {
        _subclassed = NativeMethods.SetWindowSubclass(_hwnd, _subclassProc, ScrollCaptureHotkeyId, 0);
        _scrollRegistered = NativeMethods.RegisterHotKey(
            _hwnd,
            ScrollCaptureHotkeyId,
            NativeMethods.ModNoRepeat,
            NativeMethods.VkScroll);
        _ctrlAltNRegistered = NativeMethods.RegisterHotKey(
            _hwnd,
            CtrlAltNCaptureHotkeyId,
            NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat,
            NativeMethods.VkN);
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _keyboardProc, 0, 0);
        var hookAvailable = _keyboardHook != 0;
        var scrollAvailable = (_subclassed && _scrollRegistered) || hookAvailable;
        var ctrlAltNAvailable = (_subclassed && _ctrlAltNRegistered) || hookAvailable;
        return scrollAvailable && ctrlAltNAvailable && hookAvailable;
    }

    private nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam, nuint idSubclass, nuint refData)
    {
        if (msg == NativeMethods.WhHotkey
            && ((int)wParam == ScrollCaptureHotkeyId || (int)wParam == CtrlAltNCaptureHotkeyId))
        {
            CaptureRequested?.Invoke(this, EventArgs.Empty);
            return 0;
        }

        return NativeMethods.DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private nint KeyboardProc(int nCode, nuint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var data = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            var isKeyDown = wParam == NativeMethods.WmKeyDown || wParam == NativeMethods.WmSysKeyDown;
            var isKeyUp = wParam == NativeMethods.WmKeyUp || wParam == NativeMethods.WmSysKeyUp;
            if (data.vkCode == NativeMethods.VkTab && (isKeyDown || isKeyUp))
            {
                var result = HandleTabKey(isKeyDown);
                if (result.Handled)
                {
                    DispatchAnnotationGesture(result);
                    return 1;
                }
            }

            if (!isKeyDown)
            {
                return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            var scrollPressed = data.vkCode == NativeMethods.VkScroll
                && !(_subclassed && _scrollRegistered);
            var ctrlAltNPressed = data.vkCode == NativeMethods.VkN
                && !(_subclassed && _ctrlAltNRegistered)
                && (NativeMethods.GetAsyncKeyState(NativeMethods.VkControl) & 0x8000) != 0
                && (NativeMethods.GetAsyncKeyState(NativeMethods.VkMenu) & 0x8000) != 0;
            if (scrollPressed || ctrlAltNPressed)
            {
                var now = Environment.TickCount64;
                if (now - _lastHookTriggerTicks > 400)
                {
                    _lastHookTriggerTicks = now;
                    CaptureRequested?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private AnnotationHotkeyGestureResult HandleTabKey(bool isKeyDown)
    {
        lock (_annotationGestureLock)
        {
            if (isKeyDown)
            {
                var controlPressed = (NativeMethods.GetAsyncKeyState(NativeMethods.VkControl) & 0x8000) != 0;
                if (!controlPressed)
                {
                    return _annotationGesture.OnPlainTabDown(AnnotationModeActive);
                }

                var foreground = NativeMethods.GetForegroundWindow();
                var target = NativeMethods.GetAncestor(foreground, NativeMethods.GaRoot);
                var result = _annotationGesture.OnCtrlTabDown(target, Environment.TickCount64);
                _ctrlTabLongPressTimer ??= new Timer(
                    _ => HandleCtrlTabLongPress(),
                    null,
                    AnnotationHotkeyGesture.LongPressMilliseconds,
                    Timeout.Infinite);
                return result;
            }

            var ctrlTabResult = _annotationGesture.OnCtrlTabUp(Environment.TickCount64);
            if (ctrlTabResult.Handled)
            {
                StopCtrlTabTimer();
                return ctrlTabResult;
            }

            return _annotationGesture.OnPlainTabUp();
        }
    }

    private void HandleCtrlTabLongPress()
    {
        AnnotationHotkeyGestureResult result;
        lock (_annotationGestureLock)
        {
            result = _annotationGesture.OnCtrlTabLongPress(Environment.TickCount64);
        }

        DispatchAnnotationGesture(result);
    }

    private void DispatchAnnotationGesture(AnnotationHotkeyGestureResult result)
    {
        if (result.Action != AnnotationHotkeyAction.None)
        {
            AnnotationHotkeyRequested?.Invoke(this, result);
        }
    }

    private void StopCtrlTabTimer()
    {
        _ctrlTabLongPressTimer?.Dispose();
        _ctrlTabLongPressTimer = null;
    }

    public void Dispose()
    {
        lock (_annotationGestureLock)
        {
            StopCtrlTabTimer();
        }

        if (_scrollRegistered)
        {
            NativeMethods.UnregisterHotKey(_hwnd, ScrollCaptureHotkeyId);
            _scrollRegistered = false;
        }

        if (_ctrlAltNRegistered)
        {
            NativeMethods.UnregisterHotKey(_hwnd, CtrlAltNCaptureHotkeyId);
            _ctrlAltNRegistered = false;
        }

        if (_subclassed)
        {
            NativeMethods.RemoveWindowSubclass(_hwnd, _subclassProc, ScrollCaptureHotkeyId);
            _subclassed = false;
        }

        if (_keyboardHook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }
    }
}
