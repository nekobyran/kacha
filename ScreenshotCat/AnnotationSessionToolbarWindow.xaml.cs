using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using ScreenshotCat.Interop;
using ScreenshotCat.Services;
using Windows.Graphics;
using WinRT;
using WinRT.Interop;

namespace ScreenshotCat;

public sealed partial class AnnotationSessionToolbarWindow : Window
{
    private const int ToolbarHeightDip = 40;

    private readonly nint _targetHwnd;
    private readonly nint _annotationHwnd;
    private readonly Action _onCancel;
    private readonly Action _onClear;
    private readonly Action _onFinish;
    private readonly Action<bool> _onPausedChanged;
    private readonly DispatcherTimer _followTimer = new();
    private readonly TargetWindowTracker _targetWindowTracker;
    private AppWindow? _appWindow;
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfiguration;
    private NativeMethods.POINT _dragStartCursor;
    private NativeMethods.RECT _dragStartTargetRect;
    private bool _isDraggingTarget;
    private NativeMethods.RECT? _lastTargetRect;
    private int _lastToolbarY = int.MinValue;
    private int _lastToolbarHeight;
    private bool _isShown;
    private bool _isPaused;
    private bool _toolbarHidden;
    private int _commentCount;

    public AnnotationSessionToolbarWindow(
        nint targetHwnd,
        nint annotationHwnd,
        Action onCancel,
        Action onClear,
        Action onFinish,
        Action<bool> onPausedChanged)
    {
        _targetHwnd = targetHwnd;
        _annotationHwnd = annotationHwnd;
        _onCancel = onCancel;
        _onClear = onClear;
        _onFinish = onFinish;
        _onPausedChanged = onPausedChanged;

        InitializeComponent();
        ConfigureWindow();
        _targetWindowTracker = new TargetWindowTracker(
            targetHwnd,
            DispatcherQueue,
            () => FollowTarget());
        Activated += AnnotationSessionToolbarWindow_Activated;
        Closed += AnnotationSessionToolbarWindow_Closed;
        _followTimer.Interval = TimeSpan.FromMilliseconds(250);
        _followTimer.Tick += (_, _) => FollowTarget();
    }

    public void StartFollowing(bool startHidden = false)
    {
        _toolbarHidden = startHidden;
        if (!FollowTarget())
        {
            return;
        }

        _followTimer.Start();
    }

    public void SetCommentCount(int count)
    {
        _commentCount = Math.Max(0, count);
        FinishText.Text = "保存";
        FinishButton.IsEnabled = _commentCount > 0 && !_isPaused;
        UpdateStatusAndPauseUi();
    }

    public void SetSavingState(bool isSaving, string? message = null)
    {
        if (isSaving)
        {
            StatusText.Text = message ?? "正在保存批注…";
            FinishButton.IsEnabled = false;
            FinishText.Text = "保存中";
            PauseButton.IsEnabled = false;
            return;
        }

        PauseButton.IsEnabled = true;
        FinishText.Text = "保存";
        FinishButton.IsEnabled = _commentCount > 0 && !_isPaused;
        if (!string.IsNullOrWhiteSpace(message))
        {
            StatusText.Text = message;
            return;
        }

        UpdateStatusAndPauseUi();
    }

    private void UpdateStatusAndPauseUi()
    {
        PauseButton.Content = _isPaused ? "继续批注" : "暂停";
        PauseButton.IsEnabled = true;
        if (_isPaused)
        {
            StatusText.Text = _commentCount > 0
                ? $"已暂停 · 保留 {_commentCount} 条"
                : "已暂停 · 可操作窗口";
            return;
        }

        StatusText.Text = _commentCount == 0 ? "正在批注" : $"正在批注 · {_commentCount} 条";
    }

    private void ConfigureWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.IsShownInSwitchers = false;
        _appWindow.Hide();
        _isShown = false;
        NativeMethods.ConfigureBorderlessToolWindow(hwnd, _annotationHwnd);
        NativeMethods.DisableDwmBorder(hwnd);
        ConfigureAcrylic();
    }

    private void ConfigureAcrylic()
    {
        if (!DesktopAcrylicController.IsSupported())
        {
            return;
        }

        _backdropConfiguration = new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = SystemBackdropTheme.Default
        };
        _acrylicController = new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Thin
        };
        _acrylicController.SetSystemBackdropConfiguration(_backdropConfiguration);
        _ = _acrylicController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
    }

    private bool FollowTarget()
    {
        if (_targetHwnd == 0 || !NativeMethods.IsWindow(_targetHwnd))
        {
            Close();
            return false;
        }

        // Hidden toolbar stays hidden, no matter how the pause state or the target changes.
        if (_toolbarHidden)
        {
            HideToolbarWindow();
            return true;
        }

        if (!NativeMethods.IsWindowVisible(_targetHwnd)
            || NativeMethods.IsIconic(_targetHwnd)
            || !NativeMethods.GetWindowRect(_targetHwnd, out var rect))
        {
            HideToolbarWindow();
            return true;
        }

        var width = Math.Max(0, rect.Right - rect.Left);
        if (width < 240)
        {
            Close();
            return false;
        }

        var hwnd = WindowNative.GetWindowHandle(this);
        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundRoot = NativeMethods.GetAncestor(foreground, NativeMethods.GaRoot);
        // While paused, keep the toolbar visible even if the user operates the target
        // or briefly focuses a child window, so they can resume annotation.
        if (!_isPaused
            && foreground != _targetHwnd
            && foregroundRoot != _targetHwnd
            && foreground != _annotationHwnd
            && foregroundRoot != _annotationHwnd)
        {
            HideToolbarWindow();
            return true;
        }

        var monitorPoint = new NativeMethods.POINT { X = rect.Left, Y = rect.Top };
        var monitor = NativeMethods.MonitorFromPoint(monitorPoint, NativeMethods.MonitorDefaultToNearest);
        var monitorInfo = new NativeMethods.MONITORINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };
        var hasMonitorInfo = NativeMethods.GetMonitorInfo(monitor, ref monitorInfo);
        var monitorLeft = hasMonitorInfo ? monitorInfo.rcMonitor.Left : rect.Left;
        var monitorTop = hasMonitorInfo ? monitorInfo.rcMonitor.Top : rect.Top;
        var monitorRight = hasMonitorInfo ? monitorInfo.rcMonitor.Right : rect.Right;
        var monitorBottom = hasMonitorInfo ? monitorInfo.rcMonitor.Bottom : rect.Bottom;
        var toolbarX = Math.Max(rect.Left, monitorLeft);
        var toolbarWidth = Math.Max(0, Math.Min(rect.Right, monitorRight) - toolbarX);
        if (toolbarWidth < 240)
        {
            HideToolbarWindow();
            return true;
        }

        var toolbarHeight = NativeMethods.DipToPhysicalPixels(hwnd, ToolbarHeightDip);
        var y = Math.Clamp(rect.Top - toolbarHeight, monitorTop, Math.Max(monitorTop, monitorBottom - toolbarHeight));

        var currentBoundsMatch = NativeMethods.GetWindowRect(hwnd, out var currentRect)
            && currentRect.Left == toolbarX
            && currentRect.Top == y
            && currentRect.Right - currentRect.Left == toolbarWidth
            && currentRect.Bottom - currentRect.Top == toolbarHeight;
        var boundsChanged = !currentBoundsMatch
            || !_lastTargetRect.HasValue
            || _lastTargetRect.Value.Left != rect.Left
            || _lastTargetRect.Value.Top != rect.Top
            || _lastTargetRect.Value.Right != rect.Right
            || _lastTargetRect.Value.Bottom != rect.Bottom
            || _lastToolbarY != y
            || _lastToolbarHeight != toolbarHeight;
        if (boundsChanged)
        {
            _appWindow?.MoveAndResize(new RectInt32(toolbarX, y, toolbarWidth, toolbarHeight));
            var settledToolbarHeight = NativeMethods.DipToPhysicalPixels(hwnd, ToolbarHeightDip);
            if (settledToolbarHeight != toolbarHeight)
            {
                toolbarHeight = settledToolbarHeight;
                y = Math.Clamp(rect.Top - toolbarHeight, monitorTop, Math.Max(monitorTop, monitorBottom - toolbarHeight));
                _appWindow?.MoveAndResize(new RectInt32(toolbarX, y, toolbarWidth, toolbarHeight));
            }
            _lastTargetRect = rect;
            _lastToolbarY = y;
            _lastToolbarHeight = toolbarHeight;
        }

        if (!_isShown)
        {
            _appWindow?.Show(activateWindow: false);
            _isShown = true;
        }
        return true;
    }

    /// <summary>Hides the toolbar on user request and keeps it hidden while the session runs.</summary>
    public void HideToolbar()
    {
        _toolbarHidden = true;
        HideToolbarWindow();
    }

    private void HideToolbarWindow()
    {
        if (!_isShown)
        {
            return;
        }

        _appWindow?.Hide();
        _isShown = false;
    }

    private void AnnotationSessionToolbarWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_backdropConfiguration is not null)
        {
            _backdropConfiguration.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
        }

        NativeMethods.DisableDwmBorder(WindowNative.GetWindowHandle(this));
    }

    private void DragRegion_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(DragRegion);
        if (!point.Properties.IsLeftButtonPressed
            || !NativeMethods.GetCursorPos(out _dragStartCursor)
            || !NativeMethods.GetWindowRect(_targetHwnd, out _dragStartTargetRect))
        {
            return;
        }

        _isDraggingTarget = true;
        DragRegion.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void DragRegion_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingTarget
            || !e.GetCurrentPoint(DragRegion).Properties.IsLeftButtonPressed
            || !NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        var x = _dragStartTargetRect.Left + cursor.X - _dragStartCursor.X;
        var y = _dragStartTargetRect.Top + cursor.Y - _dragStartCursor.Y;
        NativeMethods.SetWindowPos(
            _targetHwnd,
            0,
            x,
            y,
            0,
            0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
        FollowTarget();
        e.Handled = true;
    }

    private void DragRegion_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingTarget)
        {
            return;
        }

        _isDraggingTarget = false;
        DragRegion.ReleasePointerCaptures();
        e.Handled = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _onCancel();

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isPaused)
        {
            return;
        }

        _onClear();
    }

    private void FinishButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isPaused)
        {
            return;
        }

        _onFinish();
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        TogglePaused();
    }

    public void TogglePaused()
    {
        _isPaused = !_isPaused;
        UpdateStatusAndPauseUi();
        FinishButton.IsEnabled = _commentCount > 0 && !_isPaused;
        _onPausedChanged(_isPaused);
        if (!_isPaused)
        {
            FollowTarget();
        }
    }

    private void LocateButton_Click(object sender, RoutedEventArgs e)
    {
        NativeMethods.BringWindowToTop(_targetHwnd);
        NativeMethods.SetForegroundWindow(_targetHwnd);
    }

    private void AnnotationSessionToolbarWindow_Closed(object sender, WindowEventArgs args)
    {
        _followTimer.Stop();
        _targetWindowTracker.Dispose();
        _isDraggingTarget = false;
        _acrylicController?.Dispose();
        _acrylicController = null;
        _backdropConfiguration = null;
    }
}
