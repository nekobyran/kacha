using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using ScreenshotCat.Interop;
using Windows.Graphics;
using WinRT;
using WinRT.Interop;
using DRectangle = System.Drawing.Rectangle;

namespace ScreenshotCat;

public sealed partial class FullscreenAnnotationToolbarWindow : Window
{
    private const int ToolbarHeightDip = 40;

    private readonly nint _ownerHwnd;
    private readonly DRectangle _monitorBounds;
    private readonly Action _onClose;
    private readonly Action _onAnnotate;
    private readonly Action _onExitAnnotation;
    private readonly Func<Task> _onSaveAsync;
    private AppWindow? _appWindow;
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfiguration;
    private bool _annotationMode;
    private bool _isSaving;

    public FullscreenAnnotationToolbarWindow(
        nint ownerHwnd,
        DRectangle monitorBounds,
        Action onClose,
        Action onAnnotate,
        Action onExitAnnotation,
        Func<Task> onSaveAsync)
    {
        _ownerHwnd = ownerHwnd;
        _monitorBounds = monitorBounds;
        _onClose = onClose;
        _onAnnotate = onAnnotate;
        _onExitAnnotation = onExitAnnotation;
        _onSaveAsync = onSaveAsync;

        InitializeComponent();
        ConfigureWindow();
        Activated += ToolbarWindow_Activated;
        Closed += ToolbarWindow_Closed;
    }

    public void ShowAtMonitorTop()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var height = NativeMethods.DipToPhysicalPixels(hwnd, ToolbarHeightDip);
        _appWindow?.MoveAndResize(new RectInt32(
            _monitorBounds.Left,
            _monitorBounds.Top,
            _monitorBounds.Width,
            height));
        NativeMethods.ApplyBorderlessRegion(hwnd, _monitorBounds.Width, height);
        _appWindow?.Show(activateWindow: false);
        NativeMethods.BringWindowToTop(hwnd);
    }

    public void SetAnnotationMode(bool annotationMode)
    {
        _annotationMode = annotationMode;
        ModeText.Text = annotationMode ? "全屏批注" : "全屏常驻批注";
        ActionText.Text = annotationMode ? "结束批注" : "批注";
        SaveButton.Visibility = annotationMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ConfigureWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.IsShownInSwitchers = false;
        _appWindow.Hide();
        NativeMethods.ConfigureBorderlessToolWindow(hwnd, _ownerHwnd);
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

    private void CloseButton_Click(object sender, RoutedEventArgs e) => _onClose();

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_annotationMode)
        {
            _onExitAnnotation();
            return;
        }

        _onAnnotate();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isSaving)
        {
            return;
        }

        _isSaving = true;
        SaveButton.IsEnabled = false;
        try
        {
            await _onSaveAsync();
        }
        finally
        {
            _isSaving = false;
            SaveButton.IsEnabled = true;
        }
    }

    private void ToolbarWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_backdropConfiguration is not null)
        {
            _backdropConfiguration.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
        }

        NativeMethods.DisableDwmBorder(WindowNative.GetWindowHandle(this));
    }

    private void ToolbarWindow_Closed(object sender, WindowEventArgs args)
    {
        _acrylicController?.Dispose();
        _acrylicController = null;
        _backdropConfiguration = null;
    }
}
