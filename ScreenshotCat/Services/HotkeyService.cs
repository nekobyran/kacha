using ScreenshotCat.Interop;
using ScreenshotCat.Models;
using WinRT.Interop;

namespace ScreenshotCat.Services;

public sealed class HotkeyService : IDisposable
{
    private const int PrimaryCaptureHotkeyId = 2101;
    private const int SecondaryCaptureHotkeyId = 2102;
    private readonly nint _hwnd;
    private readonly HotkeySettings _settings;
    private readonly NativeMethods.SubclassProc _subclassProc;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardProc;
    private readonly AnnotationHotkeyGesture _annotationGesture = new();
    private readonly object _annotationGestureLock = new();
    private nint _keyboardHook;
    private Timer? _ctrlTabLongPressTimer;
    private long _lastHookTriggerTicks;
    private bool _primaryRegistered;
    private bool _secondaryRegistered;
    private bool _subclassed;
    private volatile bool _annotationModeActive;

    public event EventHandler? CaptureRequested;
    public event EventHandler<AnnotationHotkeyGestureResult>? AnnotationHotkeyRequested;

    public bool AnnotationModeActive
    {
        get => _annotationModeActive;
        set => _annotationModeActive = value;
    }

    public HotkeyService(Microsoft.UI.Xaml.Window window, HotkeySettings settings)
    {
        _hwnd = WindowNative.GetWindowHandle(window);
        _settings = settings;
        _subclassProc = WndProc;
        _keyboardProc = KeyboardProc;
    }

    public bool Register()
    {
        _subclassed = NativeMethods.SetWindowSubclass(_hwnd, _subclassProc, PrimaryCaptureHotkeyId, 0);
        _primaryRegistered = NativeMethods.RegisterHotKey(
            _hwnd,
            PrimaryCaptureHotkeyId,
            (uint)_settings.PrimaryCapture.Modifiers | NativeMethods.ModNoRepeat,
            _settings.PrimaryCapture.VirtualKey);
        _secondaryRegistered = NativeMethods.RegisterHotKey(
            _hwnd,
            SecondaryCaptureHotkeyId,
            (uint)_settings.SecondaryCapture.Modifiers | NativeMethods.ModNoRepeat,
            _settings.SecondaryCapture.VirtualKey);
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _keyboardProc, 0, 0);
        var hookAvailable = _keyboardHook != 0;
        var primaryAvailable = (_subclassed && _primaryRegistered) || hookAvailable;
        var secondaryAvailable = (_subclassed && _secondaryRegistered) || hookAvailable;
        return primaryAvailable && secondaryAvailable && hookAvailable;
    }

    private nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam, nuint idSubclass, nuint refData)
    {
        if (msg == NativeMethods.WhHotkey
            && ((int)wParam == PrimaryCaptureHotkeyId || (int)wParam == SecondaryCaptureHotkeyId))
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

            var primaryPressed = !_primaryRegistered && MatchesBinding(data.vkCode, _settings.PrimaryCapture);
            var secondaryPressed = !_secondaryRegistered && MatchesBinding(data.vkCode, _settings.SecondaryCapture);
            if (primaryPressed || secondaryPressed)
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

    private static bool MatchesBinding(uint virtualKey, HotkeyBinding binding)
    {
        if (virtualKey != binding.VirtualKey)
        {
            return false;
        }

        var controlPressed = IsPressed(NativeMethods.VkControl);
        var altPressed = IsPressed(NativeMethods.VkMenu);
        var shiftPressed = IsPressed(NativeMethods.VkShift);
        var winPressed = IsPressed(NativeMethods.VkLWin) || IsPressed(NativeMethods.VkRWin);
        return controlPressed == binding.Modifiers.HasFlag(HotkeyModifiers.Control)
            && altPressed == binding.Modifiers.HasFlag(HotkeyModifiers.Alt)
            && shiftPressed == binding.Modifiers.HasFlag(HotkeyModifiers.Shift)
            && winPressed == binding.Modifiers.HasFlag(HotkeyModifiers.Win);
    }

    private static bool IsPressed(int virtualKey) =>
        (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

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

        if (_primaryRegistered)
        {
            NativeMethods.UnregisterHotKey(_hwnd, PrimaryCaptureHotkeyId);
            _primaryRegistered = false;
        }

        if (_secondaryRegistered)
        {
            NativeMethods.UnregisterHotKey(_hwnd, SecondaryCaptureHotkeyId);
            _secondaryRegistered = false;
        }

        if (_subclassed)
        {
            NativeMethods.RemoveWindowSubclass(_hwnd, _subclassProc, PrimaryCaptureHotkeyId);
            _subclassed = false;
        }

        if (_keyboardHook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }
    }
}
