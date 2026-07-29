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

public sealed partial class AttachedToolbarWindow : Window
{
    private const int ToolbarHeightDip = 40;

    private readonly nint _targetHwnd;
    private readonly Action<nint> _onAnnotateRequested;
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

    public event EventHandler? CancelRequested;

    public AttachedToolbarWindow(nint targetHwnd, Action<nint> onAnnotateRequested)
    {
        _targetHwnd = targetHwnd;
        _onAnnotateRequested = onAnnotateRequested;

        InitializeComponent();
        ConfigureWindow();
        _targetWindowTracker = new TargetWindowTracker(
            targetHwnd,
            DispatcherQueue,
            () => FollowTarget());
        Activated += AttachedToolbarWindow_Activated;
        Closed += AttachedToolbarWindow_Closed;

        // WinEvent drives movement immediately; this low-frequency timer only
        // handles target close/minimize cases that do not emit a location event.
        _followTimer.Interval = TimeSpan.FromMilliseconds(250);
        _followTimer.Tick += (_, _) => FollowTarget();
    }

    public void StartFollowing()
    {
        if (!FollowTarget())
        {
            return;
        }

        _followTimer.Start();
    }

    private void ConfigureWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.IsShownInSwitchers = false;
        _appWindow.Hide();
        _isShown = false;
        NativeMethods.ConfigureBorderlessToolWindow(hwnd);
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

        if (!NativeMethods.IsWindowVisible(_targetHwnd)
            || NativeMethods.IsIconic(_targetHwnd)
            || !NativeMethods.GetWindowRect(_targetHwnd, out var rect))
        {
            HideToolbar();
            return true;
        }

        var targetWidth = Math.Max(0, rect.Right - rect.Left);
        var targetHeight = Math.Max(0, rect.Bottom - rect.Top);
        if (targetWidth < 80 || targetHeight < 80)
        {
            HideToolbar();
            return true;
        }

        var hwnd = WindowNative.GetWindowHandle(this);
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground != _targetHwnd
            && NativeMethods.GetAncestor(foreground, NativeMethods.GaRoot) != _targetHwnd)
        {
            HideToolbar();
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
            HideToolbar();
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

    private void HideToolbar()
    {
        if (!_isShown)
        {
            return;
        }

        _appWindow?.Hide();
        _isShown = false;
    }

    private void AttachedToolbarWindow_Activated(object sender, WindowActivatedEventArgs args)
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

    private void AnnotateButton_Click(object sender, RoutedEventArgs e) => _onAnnotateRequested(_targetHwnd);

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void AttachedToolbarWindow_Closed(object sender, WindowEventArgs args)
    {
        _followTimer.Stop();
        _targetWindowTracker.Dispose();
        _isDraggingTarget = false;
        _acrylicController?.Dispose();
        _acrylicController = null;
        _backdropConfiguration = null;
    }
}
