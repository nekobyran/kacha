using System.Drawing;
using System.IO;
using Microsoft.UI.Xaml;
using ScreenshotCat.Models;
using ScreenshotCat.Interop;
using ScreenshotCat.Services;
using Windows.Graphics;
using WinRT.Interop;

namespace ScreenshotCat;

public sealed partial class MainWindow : Window
{
    private readonly ScreenCaptureService _captureService = new();
    private readonly ClipboardService _clipboardService = new();
    private readonly SelectionDetectionService _selectionDetectionService = new();
    private readonly StartupService _startupService = new();
    private readonly HotkeySettingsService _hotkeySettingsService = new();
    private readonly PersistentWindowService _persistentWindowService = new();
    private readonly DispatcherTimer _persistentWindowRestoreTimer = new();
    private HotkeySettings _hotkeySettings = HotkeySettings.Default;
    private readonly TrayService _trayService;
    private HotkeyService? _hotkeyService;
    private CaptureOverlayWindow? _overlayWindow;
    private readonly Dictionary<nint, AttachedToolbarWindow> _attachedToolbarWindows = [];
    private WindowAnnotationOverlayWindow? _annotationOverlayWindow;
    private AnnotationSessionToolbarWindow? _annotationSessionToolbarWindow;
    private nint _activeAnnotationTargetHwnd;
    private Guid _activeAnnotationSessionId;
    private bool _isExiting;

    public bool HotkeyRegistered { get; private set; }
    public HotkeySettings HotkeySettings => _hotkeySettings;
    public bool AutoGraphicRecognition { get; set; }
    public bool StartupRegistered { get; private set; }
    public SaveResult? LastSave { get; private set; }

    public bool LastSaveCopiedToClipboard { get; private set; }

    public event EventHandler<SaveResult>? ScreenshotSaved;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(460, 300));

        RootFrame.Navigate(typeof(MainPage));
        StartupRegistered = _startupService.EnableForCurrentExecutable();
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        _trayService = new TrayService(iconPath);
        _trayService.ShowRequested += (_, _) => DispatcherQueue.TryEnqueue(ShowFromBackground);
        _trayService.CaptureRequested += (_, _) => DispatcherQueue.TryEnqueue(StartCapture);
        _trayService.ExitRequested += (_, _) => DispatcherQueue.TryEnqueue(ExitApplication);
        _hotkeySettings = _hotkeySettingsService.Load();
        RegisterHotkey();
        Closed += MainWindow_Closed;
        UpdateTrayToolTip();

        _persistentWindowRestoreTimer.Interval = TimeSpan.FromSeconds(2);
        _persistentWindowRestoreTimer.Tick += (_, _) => RestorePersistentToolbars();
        _persistentWindowRestoreTimer.Start();

        RootFrame.Loaded += (_, _) =>
        {
            HideToBackground();
            RestorePersistentToolbars();
        };

    }

    public void StartCapture()
    {
        if (_overlayWindow is not null)
        {
            _overlayWindow.Activate();
            return;
        }

        var cursorPoint = _captureService.GetCursorPosition();
        var cursorSystemPoint = new Point(cursorPoint.X, cursorPoint.Y);
        var capture = _captureService.CaptureMonitorAtCursor();
        var initialSelection = _selectionDetectionService.DetectWindowAtPoint(
                cursorSystemPoint,
                capture.MonitorBounds,
                Environment.ProcessId)
            ?? _selectionDetectionService.DetectForegroundWindow(capture.MonitorBounds, Environment.ProcessId);
        _overlayWindow = new CaptureOverlayWindow(
            capture,
            AutoGraphicRecognition,
            OnScreenshotSavedAsync,
            cursorSystemPoint,
            initialSelection,
            hwnd => ShowAttachedToolbar(hwnd));
        _overlayWindow.Closed += (_, _) => _overlayWindow = null;
        _overlayWindow.Activate();
    }

    public void StartDirectAnnotationForWindow(nint targetHwnd)
    {
        if (targetHwnd == 0 || !NativeMethods.GetWindowRect(targetHwnd, out _))
        {
            return;
        }

        var sessionId = Guid.NewGuid();
        _activeAnnotationTargetHwnd = targetHwnd;
        _activeAnnotationSessionId = sessionId;

        if (_attachedToolbarWindows.Remove(targetHwnd, out var attachedToolbar))
        {
            attachedToolbar.Close();
        }

        _overlayWindow?.Close();
        _annotationOverlayWindow?.Close();
        _annotationSessionToolbarWindow?.Close();

        var annotationWindow = new WindowAnnotationOverlayWindow(targetHwnd);
        var returnToPersistentToolbar = false;
        AnnotationSessionToolbarWindow sessionToolbar = null!;
        sessionToolbar = new AnnotationSessionToolbarWindow(
            targetHwnd,
            WindowNative.GetWindowHandle(annotationWindow),
            () =>
            {
                _persistentWindowService.Forget(targetHwnd);
                annotationWindow.RequestExitAnnotationMode(returnToPersistentToolbar: false);
            },
            annotationWindow.ClearAnnotations,
            () =>
            {
                returnToPersistentToolbar = true;
                _ = SaveAnnotationSessionAsync(annotationWindow, sessionToolbar);
            },
            annotationWindow.SetPaused);
        _annotationOverlayWindow = annotationWindow;
        _annotationSessionToolbarWindow = sessionToolbar;
        annotationWindow.SetCompanionToolbarWindow(WindowNative.GetWindowHandle(sessionToolbar));
        annotationWindow.CommentCountChanged += (_, count) => sessionToolbar.SetCommentCount(count);
        annotationWindow.ExitAnnotationModeRequested += (_, _) =>
        {
            returnToPersistentToolbar = annotationWindow.ReturnToPersistentToolbarOnExit;
            _ = ExitAnnotationSessionAsync(annotationWindow, sessionToolbar);
        };
        annotationWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(_annotationOverlayWindow, annotationWindow))
            {
                _annotationOverlayWindow = null;
            }

            if (ReferenceEquals(_annotationSessionToolbarWindow, sessionToolbar))
            {
                _annotationSessionToolbarWindow = null;
                sessionToolbar.Close();
            }

            var ownsActiveSession = _activeAnnotationSessionId == sessionId;
            if (ownsActiveSession)
            {
                _activeAnnotationSessionId = Guid.Empty;
                _activeAnnotationTargetHwnd = 0;
                if (_hotkeyService is not null)
                {
                    _hotkeyService.AnnotationModeActive = false;
                }
            }

            if (ownsActiveSession
                && returnToPersistentToolbar
                && NativeMethods.IsWindowVisible(targetHwnd))
            {
                DispatcherQueue.TryEnqueue(() => ShowAttachedToolbar(targetHwnd));
            }
        };
        annotationWindow.StartFollowing();
        if (_hotkeyService is not null)
        {
            _hotkeyService.AnnotationModeActive = true;
        }
        sessionToolbar.SetCommentCount(annotationWindow.CommentCount);
        sessionToolbar.StartFollowing();
    }

#if DEBUG
    public void ShowPersistentAnnotationForWindow(nint targetHwnd) => ShowAttachedToolbar(targetHwnd);
#endif

    private async Task SaveAnnotationSessionAsync(
        WindowAnnotationOverlayWindow annotationWindow,
        AnnotationSessionToolbarWindow sessionToolbar)
    {
        sessionToolbar.SetSavingState(true);
        try
        {
            var result = annotationWindow.SaveAnnotations();
            if (result is null)
            {
                sessionToolbar.SetSavingState(false, "请先添加批注");
                return;
            }

            var clipboardFiles = result.ImagePaths
                .Append(result.TextPath)
                .ToArray();
            LastSave = new SaveResult(result.ImagePaths[0], result.SummaryText, result.TextPath);
            LastSaveCopiedToClipboard = await TryCopyFilesAndTextAsync(clipboardFiles, result.SummaryText);
            ScreenshotSaved?.Invoke(this, LastSave);
            annotationWindow.Close();
        }
        catch
        {
            sessionToolbar.SetSavingState(false, "保存失败，请重试");
        }
    }

    private async Task ExitAnnotationSessionAsync(
        WindowAnnotationOverlayWindow annotationWindow,
        AnnotationSessionToolbarWindow sessionToolbar)
    {
        if (annotationWindow.CommentCount == 0)
        {
            annotationWindow.Close();
            return;
        }

        await SaveAnnotationSessionAsync(annotationWindow, sessionToolbar);
    }

    public async Task<bool> CopyLastAsync()
    {
        if (LastSave is null)
        {
            return false;
        }

        LastSaveCopiedToClipboard = await TryCopyImageAsync(LastSave.ImagePath);
        return LastSaveCopiedToClipboard;
    }

    public void HideToBackground()
    {
        NativeMethods.ShowWindow(WindowNative.GetWindowHandle(this), NativeMethods.SwHide);
    }

    public void ShowFromBackground()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        NativeMethods.ShowWindow(hwnd, NativeMethods.SwShow);
        NativeMethods.BringWindowToTop(hwnd);
        NativeMethods.SetForegroundWindow(hwnd);
    }

    public void ExitApplication()
    {
        _isExiting = true;
        _persistentWindowRestoreTimer.Stop();
        foreach (var toolbar in _attachedToolbarWindows.Values.ToArray())
        {
            toolbar.Close();
        }
        _attachedToolbarWindows.Clear();
        _annotationOverlayWindow?.Close();
        _annotationSessionToolbarWindow?.Close();
        _trayService.Dispose();
        Close();
    }

    private void ShowAttachedToolbar(nint targetHwnd, bool remember = true)
    {
        if (targetHwnd == 0 || !NativeMethods.IsWindowVisible(targetHwnd))
        {
            return;
        }

        if (targetHwnd == _activeAnnotationTargetHwnd)
        {
            return;
        }

        if (_attachedToolbarWindows.TryGetValue(targetHwnd, out var existing))
        {
            return;
        }

        if (remember)
        {
            _persistentWindowService.Remember(targetHwnd);
        }

        NativeMethods.BringWindowToTop(targetHwnd);
        NativeMethods.SetForegroundWindow(targetHwnd);

        var toolbar = new AttachedToolbarWindow(targetHwnd, hwnd => DispatcherQueue.TryEnqueue(() => StartDirectAnnotationForWindow(hwnd)));
        _attachedToolbarWindows[targetHwnd] = toolbar;
        toolbar.CancelRequested += (_, _) => _persistentWindowService.Forget(targetHwnd);
        toolbar.Closed += (_, _) =>
        {
            if (_attachedToolbarWindows.TryGetValue(targetHwnd, out var current)
                && ReferenceEquals(current, toolbar))
            {
                _attachedToolbarWindows.Remove(targetHwnd);
            }
        };
        toolbar.StartFollowing();
    }

    private void RestorePersistentToolbars()
    {
        foreach (var hwnd in _persistentWindowService.ResolveAvailableWindows())
        {
            if (hwnd == _activeAnnotationTargetHwnd)
            {
                continue;
            }

            ShowAttachedToolbar(hwnd, remember: false);
        }
    }

    public bool TryUpdateHotkeySettings(HotkeySettings settings, out string error)
    {
        if (!settings.TryValidate(out error))
        {
            return false;
        }

        var previousSettings = _hotkeySettings;
        if (!ApplyHotkeySettings(settings))
        {
            _ = ApplyHotkeySettings(previousSettings);
            error = "新的快捷键无法注册，已恢复之前的设置。";
            return false;
        }

        if (_hotkeySettingsService.TrySave(settings, out error))
        {
            UpdateTrayToolTip();
            return true;
        }

        _ = ApplyHotkeySettings(previousSettings);
        return false;
    }

    private void RegisterHotkey()
    {
        _ = ApplyHotkeySettings(_hotkeySettings);
    }

    private bool ApplyHotkeySettings(HotkeySettings settings)
    {
        _hotkeyService?.Dispose();
        _hotkeySettings = settings;
        _hotkeyService = new HotkeyService(this, settings);
        HotkeyRegistered = _hotkeyService.Register();
        _hotkeyService.CaptureRequested += (_, _) => DispatcherQueue.TryEnqueue(StartCapture);
        _hotkeyService.AnnotationHotkeyRequested += (_, gesture) =>
            DispatcherQueue.TryEnqueue(() => HandleAnnotationHotkey(gesture));
        return HotkeyRegistered;
    }

    private void HandleAnnotationHotkey(AnnotationHotkeyGestureResult gesture)
    {
        switch (gesture.Action)
        {
            case AnnotationHotkeyAction.TogglePause:
                _annotationSessionToolbarWindow?.TogglePaused();
                break;
            case AnnotationHotkeyAction.ToggleMode:
                if (_annotationOverlayWindow is not null)
                {
                    _annotationOverlayWindow.RequestExitAnnotationMode();
                    break;
                }

                var toggleTarget = ResolveAnnotationTarget(gesture.TargetHwnd);
                if (toggleTarget != 0)
                {
                    StartDirectAnnotationForWindow(toggleTarget);
                }
                break;
            case AnnotationHotkeyAction.HideToolbar:
                HideAnnotationUiForWindow(ResolveAnnotationTarget(gesture.TargetHwnd));
                break;
        }
    }

    private nint ResolveAnnotationTarget(nint foregroundHwnd)
    {
        if (_activeAnnotationTargetHwnd != 0)
        {
            return _activeAnnotationTargetHwnd;
        }

        if (foregroundHwnd == 0 || !NativeMethods.IsWindowVisible(foregroundHwnd))
        {
            return 0;
        }

        _ = NativeMethods.GetWindowThreadProcessId(foregroundHwnd, out var processId);
        return processId != 0 && !ProcessIdentityService.IsCurrentApplicationProcess(processId)
            ? foregroundHwnd
            : 0;
    }

    private void HideAnnotationUiForWindow(nint targetHwnd)
    {
        if (targetHwnd == 0)
        {
            return;
        }

        _persistentWindowService.Forget(targetHwnd);
        if (_attachedToolbarWindows.Remove(targetHwnd, out var toolbar))
        {
            toolbar.Close();
        }

        if (targetHwnd == _activeAnnotationTargetHwnd)
        {
            _annotationOverlayWindow?.RequestExitAnnotationMode(returnToPersistentToolbar: false);
        }
    }

    private async Task OnScreenshotSavedAsync(SaveResult result)
    {
        LastSave = result;
        LastSaveCopiedToClipboard = await TryCopyImageAsync(result.ImagePath);
        ScreenshotSaved?.Invoke(this, result);
    }

    private async Task<bool> TryCopyImageAsync(string imagePath)
    {
        try
        {
            await _clipboardService.CopyImageAsync(imagePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryCopyFilesAndTextAsync(IReadOnlyList<string> filePaths, string text)
    {
        try
        {
            await _clipboardService.CopyFilesAndTextAsync(filePaths, text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (!_isExiting)
        {
            args.Handled = true;
            HideToBackground();
            UpdateTrayToolTip();
            return;
        }

        _hotkeyService?.Dispose();
        _persistentWindowRestoreTimer.Stop();
        foreach (var toolbar in _attachedToolbarWindows.Values.ToArray())
        {
            toolbar.Close();
        }
        _attachedToolbarWindows.Clear();
        _annotationOverlayWindow?.Close();
        _annotationSessionToolbarWindow?.Close();
    }

    private void UpdateTrayToolTip()
    {
        var status = HotkeyRegistered ? "截图热键已就绪" : "截图热键不可用";
        var startup = StartupRegistered ? "已设置开机自启" : "开机自启未启用";
        _trayService.SetToolTip($"ScreenshotCat - {status}，{startup}");
    }
}
