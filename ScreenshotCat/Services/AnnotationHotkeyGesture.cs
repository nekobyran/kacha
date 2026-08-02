namespace ScreenshotCat.Services;

public enum AnnotationHotkeyAction
{
    None,
    ToggleMode,
    HideToolbar,
    TogglePause
}

public readonly record struct AnnotationHotkeyGestureResult(
    bool Handled,
    AnnotationHotkeyAction Action = AnnotationHotkeyAction.None,
    nint TargetHwnd = 0);

public sealed class AnnotationHotkeyGesture
{
    public const long LongPressMilliseconds = 650;

    private bool _ctrlTabDown;
    private bool _ctrlTabLongPressHandled;
    private bool _plainTabDown;
    private long _ctrlTabPressedAt;
    private nint _ctrlTabTargetHwnd;

    public AnnotationHotkeyGestureResult OnCtrlTabDown(nint targetHwnd, long timestamp)
    {
        if (!_ctrlTabDown)
        {
            _ctrlTabDown = true;
            _ctrlTabLongPressHandled = false;
            _ctrlTabPressedAt = timestamp;
            _ctrlTabTargetHwnd = targetHwnd;
        }

        return new AnnotationHotkeyGestureResult(Handled: true);
    }

    public AnnotationHotkeyGestureResult OnCtrlTabLongPress(long timestamp)
    {
        if (!_ctrlTabDown
            || _ctrlTabLongPressHandled
            || timestamp - _ctrlTabPressedAt < LongPressMilliseconds)
        {
            return new AnnotationHotkeyGestureResult(Handled: _ctrlTabDown);
        }

        _ctrlTabLongPressHandled = true;
        return new AnnotationHotkeyGestureResult(
            Handled: true,
            AnnotationHotkeyAction.HideToolbar,
            _ctrlTabTargetHwnd);
    }

    public AnnotationHotkeyGestureResult OnCtrlTabUp(long timestamp)
    {
        if (!_ctrlTabDown)
        {
            return new AnnotationHotkeyGestureResult(Handled: false);
        }

        var action = _ctrlTabLongPressHandled
            ? AnnotationHotkeyAction.None
            : timestamp - _ctrlTabPressedAt >= LongPressMilliseconds
                ? AnnotationHotkeyAction.HideToolbar
                : AnnotationHotkeyAction.ToggleMode;
        var targetHwnd = _ctrlTabTargetHwnd;
        _ctrlTabDown = false;
        _ctrlTabLongPressHandled = false;
        _ctrlTabTargetHwnd = 0;
        return new AnnotationHotkeyGestureResult(true, action, targetHwnd);
    }

    public AnnotationHotkeyGestureResult OnPlainTabDown(bool annotationModeActive)
    {
        if (!annotationModeActive)
        {
            return new AnnotationHotkeyGestureResult(Handled: false);
        }

        var action = _plainTabDown
            ? AnnotationHotkeyAction.None
            : AnnotationHotkeyAction.TogglePause;
        _plainTabDown = true;
        return new AnnotationHotkeyGestureResult(true, action);
    }

    public AnnotationHotkeyGestureResult OnPlainTabUp()
    {
        if (!_plainTabDown)
        {
            return new AnnotationHotkeyGestureResult(Handled: false);
        }

        _plainTabDown = false;
        return new AnnotationHotkeyGestureResult(Handled: true);
    }
}
