namespace ScreenshotCat.Services;

public enum AnnotationHotkeyAction
{
    None,
    ToggleMode,
    ToggleToolbar,
    TogglePause,
    CancelAnnotation,
    ExitAnnotation
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
    private bool _escapeDown;
    private bool _escapeLongPressHandled;
    private bool _plainTabDown;
    private long _ctrlTabPressedAt;
    private long _escapePressedAt;
    private nint _ctrlTabTargetHwnd;
    private nint _escapeTargetHwnd;

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
            AnnotationHotkeyAction.ToggleToolbar,
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
                ? AnnotationHotkeyAction.ToggleToolbar
                : AnnotationHotkeyAction.ToggleMode;
        var targetHwnd = _ctrlTabTargetHwnd;
        _ctrlTabDown = false;
        _ctrlTabLongPressHandled = false;
        _ctrlTabTargetHwnd = 0;
        return new AnnotationHotkeyGestureResult(true, action, targetHwnd);
    }

    public AnnotationHotkeyGestureResult OnEscapeDown(nint targetHwnd, long timestamp)
    {
        if (!_escapeDown)
        {
            _escapeDown = true;
            _escapeLongPressHandled = false;
            _escapePressedAt = timestamp;
            _escapeTargetHwnd = targetHwnd;
        }

        return new AnnotationHotkeyGestureResult(Handled: false);
    }

    public AnnotationHotkeyGestureResult OnEscapeLongPress(long timestamp)
    {
        if (!_escapeDown
            || _escapeLongPressHandled
            || timestamp - _escapePressedAt < LongPressMilliseconds)
        {
            return new AnnotationHotkeyGestureResult(Handled: false);
        }

        _escapeLongPressHandled = true;
        return new AnnotationHotkeyGestureResult(
            Handled: false,
            AnnotationHotkeyAction.CancelAnnotation,
            _escapeTargetHwnd);
    }

    public AnnotationHotkeyGestureResult OnEscapeUp(long timestamp)
    {
        if (!_escapeDown)
        {
            return new AnnotationHotkeyGestureResult(Handled: false);
        }

        // A tap leaves annotation mode through the usual exit, a hold cancels it outright.
        var action = _escapeLongPressHandled || timestamp - _escapePressedAt >= LongPressMilliseconds
            ? AnnotationHotkeyAction.None
            : AnnotationHotkeyAction.ExitAnnotation;
        var targetHwnd = _escapeTargetHwnd;
        _escapeDown = false;
        _escapeLongPressHandled = false;
        _escapeTargetHwnd = 0;
        return new AnnotationHotkeyGestureResult(Handled: false, action, targetHwnd);
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
