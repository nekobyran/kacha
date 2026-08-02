using System.Globalization;
using System.Drawing.Imaging;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using ScreenshotCat.Interop;
using ScreenshotCat.Models;
using ScreenshotCat.Services;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;
using Windows.UI;
using WinRT.Interop;
using WPoint = Windows.Foundation.Point;
using WRect = Windows.Foundation.Rect;

namespace ScreenshotCat;

public sealed partial class WindowAnnotationOverlayWindow : Window
{
    private const double TopBarHeight = 0;
    private const double MinimumSelectionSize = 12;

    private readonly nint _targetHwnd;
    private readonly ScreenCaptureService _captureService = new();
    private readonly AnnotationSessionSaveService _saveService = new();
    private readonly DispatcherTimer _followTimer = new();
    private readonly TargetWindowTracker _targetWindowTracker;
    private readonly List<AnnotationSessionComment> _comments = [];
    private string? _snapshotPath;
    private System.Drawing.Size _snapshotPixelSize;
    private AppWindow? _appWindow;
    private WPoint _dragStart;
    private WPoint _currentPoint;
    private WRect? _draftSelection;
    private bool _isDragging;
    private int? _editingCommentIndex;
    private int _targetUnavailableTicks;
    private NativeMethods.RECT? _lastTargetRect;
    private bool _isShown;
    private bool _isClosed;
    private int _snapshotRefreshVersion;
    private nint _companionToolbarHwnd;

    public int CommentCount => _comments.Count;
    public bool ReturnToPersistentToolbarOnExit { get; private set; } = true;

    public event EventHandler<int>? CommentCountChanged;
    public event EventHandler? ExitAnnotationModeRequested;

    private bool _isPaused;

    public void SetPaused(bool paused)
    {
        if (_isPaused == paused)
        {
            return;
        }

        CancelDraft();
        _isPaused = paused;
        var hwnd = WindowNative.GetWindowHandle(this);
        if (paused)
        {
            // Keep comments in memory, but let the real window receive input so the
            // user can navigate to another UI state before resuming annotation.
            _snapshotRefreshVersion++;
            NativeMethods.SetMouseClickThrough(hwnd, true);
            CommentPreviewPanel.Visibility = Visibility.Collapsed;
            SelectionPreview.Visibility = Visibility.Collapsed;
            HintPill.Visibility = Visibility.Collapsed;
            NotePanel.IsOpen = false;
            HideOverlay();
            return;
        }

        NativeMethods.SetMouseClickThrough(hwnd, false);
        AnnotationCanvas.Visibility = Visibility.Visible;
        RebuildCommentUi();
        HintPill.Visibility = _comments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSessionUi();
        _ = RefreshTargetSnapshotAsync(activateWhenReady: true);
    }

    // Compatibility wrapper used by older call sites.
    public void SetTargetInteractionEnabled(bool enabled) => SetPaused(enabled);

    public void SetCompanionToolbarWindow(nint hwnd) => _companionToolbarHwnd = hwnd;

    public WindowAnnotationOverlayWindow(nint targetHwnd)
    {
        _targetHwnd = targetHwnd;

        InitializeComponent();
        ConfigureWindow();
        _targetWindowTracker = new TargetWindowTracker(targetHwnd, DispatcherQueue, FollowTarget);
        Activated += (_, _) => NativeMethods.DisableDwmBorder(WindowNative.GetWindowHandle(this));
        Closed += WindowAnnotationOverlayWindow_Closed;
        Root.Loaded += Root_Loaded;

        _followTimer.Interval = TimeSpan.FromMilliseconds(250);
        _followTimer.Tick += (_, _) => FollowTarget();
    }

    public void StartFollowing()
    {
        _followTimer.Start();
        _ = RefreshTargetSnapshotAsync(activateWhenReady: true);
    }

    private void ConfigureWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        _appWindow.IsShownInSwitchers = false;
        _appWindow.Hide();
        _isShown = false;
        NativeMethods.ConfigureBorderlessOverlayWindow(hwnd);
        NativeMethods.DisableDwmBorder(hwnd);
    }


    private async Task RefreshTargetSnapshotAsync(bool activateWhenReady)
    {
        var refreshVersion = ++_snapshotRefreshVersion;
        var previousPath = _snapshotPath;
        var capture = await Task.Run(TryCaptureTargetSnapshot);
        if (capture is null)
        {
            return;
        }

        if (_isClosed || _isPaused || refreshVersion != _snapshotRefreshVersion)
        {
            DeleteSnapshot(capture.Path);
            return;
        }

        BitmapImage image;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(capture.Path);
            using var stream = await file.OpenReadAsync();
            image = new BitmapImage();
            await image.SetSourceAsync(stream);
        }
        catch
        {
            DeleteSnapshot(capture.Path);
            return;
        }

        if (_isClosed || _isPaused || refreshVersion != _snapshotRefreshVersion)
        {
            DeleteSnapshot(capture.Path);
            return;
        }

        _snapshotPath = capture.Path;
        _snapshotPixelSize = capture.PixelSize;
        TargetSnapshot.Source = image;
        if (!string.IsNullOrWhiteSpace(previousPath)
            && !string.Equals(previousPath, capture.Path, StringComparison.OrdinalIgnoreCase))
        {
            DeleteSnapshot(previousPath);
        }

        FollowTarget();
        if (activateWhenReady && _isShown)
        {
            Activate();
            NativeMethods.DisableDwmBorder(WindowNative.GetWindowHandle(this));
            Root.Focus(FocusState.Programmatic);
        }
    }

    private CapturedSnapshot? TryCaptureTargetSnapshot()
    {
        if (!NativeMethods.GetWindowRect(_targetHwnd, out var rect))
        {
            return null;
        }

        var targetBounds = System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        if (targetBounds.Width < 1 || targetBounds.Height < 1)
        {
            return null;
        }

        try
        {
            var center = new System.Drawing.Point(
                targetBounds.Left + targetBounds.Width / 2,
                targetBounds.Top + targetBounds.Height / 2);
            var monitorBounds = _captureService.GetMonitorBoundsAtPoint(center);
            var visibleBounds = System.Drawing.Rectangle.Intersect(targetBounds, monitorBounds);
            if (visibleBounds.Width < 1 || visibleBounds.Height < 1)
            {
                return null;
            }

            using var snapshot = new System.Drawing.Bitmap(
                targetBounds.Width,
                targetBounds.Height,
                PixelFormat.Format32bppPArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(snapshot))
            {
                graphics.Clear(System.Drawing.Color.FromArgb(255, 16, 20, 24));
                var source = new System.Drawing.Rectangle(
                    visibleBounds.Left,
                    visibleBounds.Top,
                    visibleBounds.Width,
                    visibleBounds.Height);
                var destination = new System.Drawing.Rectangle(
                    visibleBounds.Left - targetBounds.Left,
                    visibleBounds.Top - targetBounds.Top,
                    visibleBounds.Width,
                    visibleBounds.Height);
                graphics.CopyFromScreen(
                    source.Left,
                    source.Top,
                    destination.Left,
                    destination.Top,
                    source.Size,
                    System.Drawing.CopyPixelOperation.SourceCopy);
            }

            if (IsLikelyBlankSnapshot(snapshot))
            {
                return TryCaptureTargetWithPrintWindow(targetBounds);
            }

            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"ScreenshotCat-annotation-{Guid.NewGuid():N}.png");
            snapshot.Save(path, ImageFormat.Png);
            return new CapturedSnapshot(path, snapshot.Size);
        }
        catch
        {
            try
            {
                return TryCaptureTargetWithPrintWindow(targetBounds);
            }
            catch
            {
                return null;
            }
        }
    }

    private CapturedSnapshot? TryCaptureTargetWithPrintWindow(System.Drawing.Rectangle targetBounds)
    {
        using var rawSnapshot = new System.Drawing.Bitmap(
            targetBounds.Width,
            targetBounds.Height,
            PixelFormat.Format32bppPArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(rawSnapshot))
        {
            graphics.Clear(System.Drawing.Color.FromArgb(255, 16, 20, 24));
            var hdc = graphics.GetHdc();
            try
            {
                if (!NativeMethods.PrintWindow(_targetHwnd, hdc, NativeMethods.PwRenderFullContent))
                {
                    return null;
                }
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        if (IsLikelyBlankSnapshot(rawSnapshot))
        {
            return null;
        }

        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ScreenshotCat-annotation-{Guid.NewGuid():N}.png");
        rawSnapshot.Save(path, ImageFormat.Png);
        return new CapturedSnapshot(path, rawSnapshot.Size);
    }

    private static bool IsLikelyBlankSnapshot(System.Drawing.Bitmap bitmap)
    {
        var sampleLeft = bitmap.Width / 10;
        var sampleTop = bitmap.Height / 10;
        var sampleRight = bitmap.Width - sampleLeft;
        var sampleBottom = bitmap.Height - sampleTop;
        var stepX = Math.Max(1, (sampleRight - sampleLeft) / 32);
        var stepY = Math.Max(1, (sampleBottom - sampleTop) / 24);
        var minimumLuminance = 255;
        var maximumLuminance = 0;
        long luminanceSum = 0;
        var samples = 0;
        var darkSamples = 0;

        for (var y = sampleTop + stepY / 2; y < sampleBottom; y += stepY)
        {
            for (var x = sampleLeft + stepX / 2; x < sampleRight; x += stepX)
            {
                var color = bitmap.GetPixel(x, y);
                var luminance = (color.R * 54 + color.G * 183 + color.B * 19) >> 8;
                minimumLuminance = Math.Min(minimumLuminance, luminance);
                maximumLuminance = Math.Max(maximumLuminance, luminance);
                luminanceSum += luminance;
                samples++;
                if (luminance < 32)
                {
                    darkSamples++;
                }
            }
        }

        return samples > 0
            && darkSamples >= samples * 98 / 100
            && (luminanceSum / samples < 22 || maximumLuminance - minimumLuminance < 18);
    }

    private void Root_Loaded(object sender, RoutedEventArgs e)
    {
        Root.Focus(FocusState.Programmatic);
        UpdateSessionUi();
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_comments.Count > 0)
        {
            RebuildCommentUi();
        }
    }

    private void FollowTarget()
    {
        if (_targetHwnd == 0 || !NativeMethods.IsWindow(_targetHwnd))
        {
            CloseAfterSustainedTargetLoss();
            return;
        }

        if (!NativeMethods.IsWindowVisible(_targetHwnd)
            || NativeMethods.IsIconic(_targetHwnd)
            || !NativeMethods.GetWindowRect(_targetHwnd, out var rect))
        {
            HideOverlay();
            return;
        }

        var width = Math.Max(0, rect.Right - rect.Left);
        var height = Math.Max(0, rect.Bottom - rect.Top);
        if (width < 240 || height < 160)
        {
            HideOverlay();
            return;
        }

        _targetUnavailableTicks = 0;
        if (_isPaused || TargetSnapshot.Source is null)
        {
            HideOverlay();
            return;
        }

        var hwnd = WindowNative.GetWindowHandle(this);
        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundRoot = NativeMethods.GetAncestor(foreground, NativeMethods.GaRoot);
        if (!_isPaused
            && foreground != _targetHwnd
            && foregroundRoot != _targetHwnd
            && foreground != hwnd
            && foregroundRoot != hwnd
            && foreground != _companionToolbarHwnd
            && foregroundRoot != _companionToolbarHwnd)
        {
            HideOverlay();
            return;
        }

        var currentBoundsMatch = NativeMethods.GetWindowRect(hwnd, out var currentRect)
            && currentRect.Left == rect.Left
            && currentRect.Top == rect.Top
            && currentRect.Right - currentRect.Left == width
            && currentRect.Bottom - currentRect.Top == height;
        var boundsChanged = !currentBoundsMatch
            || !_lastTargetRect.HasValue
            || _lastTargetRect.Value.Left != rect.Left
            || _lastTargetRect.Value.Top != rect.Top
            || _lastTargetRect.Value.Right != rect.Right
            || _lastTargetRect.Value.Bottom != rect.Bottom;
        if (boundsChanged)
        {
            _appWindow?.MoveAndResize(new RectInt32(rect.Left, rect.Top, width, height));
            _lastTargetRect = rect;
        }

        if (!_isShown)
        {
            _appWindow?.Show(activateWindow: false);
            _isShown = true;
        }
    }

    private void HideOverlay()
    {
        if (!_isShown)
        {
            return;
        }

        _appWindow?.Hide();
        _isShown = false;
    }

    private void CloseAfterSustainedTargetLoss()
    {
        _targetUnavailableTicks++;
        if (_targetUnavailableTicks >= 25)
        {
            Close();
        }
    }

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_isPaused)
        {
            return;
        }

        var point = e.GetCurrentPoint(Root);
        if (!point.Properties.IsLeftButtonPressed
            || point.Position.Y <= TopBarHeight)
        {
            return;
        }

        CancelDraft();
        HintPill.Visibility = Visibility.Collapsed;
        _dragStart = ClampToContent(point.Position);
        _currentPoint = _dragStart;
        _isDragging = true;
        Root.CapturePointer(e.Pointer);
        ShowSelectionPreview(NormalizeRect(_dragStart, _currentPoint));
        e.Handled = true;
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging || !e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _currentPoint = ClampToContent(e.GetCurrentPoint(Root).Position);
        ShowSelectionPreview(NormalizeRect(_dragStart, _currentPoint));
        e.Handled = true;
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _currentPoint = ClampToContent(e.GetCurrentPoint(Root).Position);
        _isDragging = false;
        Root.ReleasePointerCaptures();

        var selection = NormalizeRect(_dragStart, _currentPoint);
        if (selection.Width < MinimumSelectionSize || selection.Height < MinimumSelectionSize)
        {
            selection = CreateClickSelection(_currentPoint);
        }

        BeginDraft(selection);
        e.Handled = true;
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        RequestExitAnnotationMode();
        e.Handled = true;
    }

    private void NoteBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            SaveNote();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            RequestExitAnnotationMode();
            e.Handled = true;
        }
    }

    public void RequestExitAnnotationMode(bool returnToPersistentToolbar = true)
    {
        CancelDraft();
        ReturnToPersistentToolbarOnExit = returnToPersistentToolbar;
        if (ExitAnnotationModeRequested is null)
        {
            Close();
            return;
        }

        ExitAnnotationModeRequested.Invoke(this, EventArgs.Empty);
    }

    private void BeginDraft(WRect selection)
    {
        _draftSelection = selection;
        ShowSelectionPreview(selection);
        NotePanel.Title = "添加批注";
        NotePanel.IsOpen = true;
        NoteBox.Text = string.Empty;
        NoteBox.Focus(FocusState.Programmatic);
    }

    private void SaveNote()
    {
        if (!_draftSelection.HasValue)
        {
            return;
        }

        var text = NoteBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            NoteBox.Focus(FocusState.Programmatic);
            return;
        }

        var selectionDip = _draftSelection.Value;
        var selection = DipRectToSnapshot(selectionDip);
        var index = _comments.Count + 1;
        var anchor = new WPoint(
            selection.X + selection.Width / 2,
            selection.Y + selection.Height / 2);
        if (_editingCommentIndex.HasValue)
        {
            var commentIndex = _editingCommentIndex.Value;
            var existing = _comments[commentIndex];
            _comments[commentIndex] = existing with { Text = text };
        }
        else
        {
            _comments.Add(new AnnotationSessionComment(index, text, anchor, selection));
        }

        CancelDraft();
        RebuildCommentUi();
        UpdateSessionUi();
        Root.Focus(FocusState.Programmatic);
    }

    private void CancelDraft()
    {
        _isDragging = false;
        Root.ReleasePointerCaptures();
        _draftSelection = null;
        _editingCommentIndex = null;
        SelectionPreview.Visibility = Visibility.Collapsed;
        NotePanel.IsOpen = false;
        NoteBox.Text = string.Empty;
    }

    public void ClearAnnotations()
    {
        CancelDraft();
        _comments.Clear();
        AnnotationCanvas.Children.Clear();
        CommentPreviewList.Children.Clear();
        CommentPreviewPanel.Visibility = Visibility.Collapsed;
        HintPill.Visibility = Visibility.Visible;
        UpdateSessionUi();
        Root.Focus(FocusState.Programmatic);
    }

    public AnnotationSessionSaveResult? SaveAnnotations()
    {
        CancelDraft();
        if (_comments.Count == 0 || _snapshotPath is null)
        {
            return null;
        }

        return _saveService.Save(_snapshotPath, _comments);
    }

    private void DrawCommentBadge(AnnotationSessionComment comment)
    {
        var marker = new Button
        {
            Width = 32,
            Height = 34,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Tag = comment.Index - 1
        };
        ToolTipService.SetToolTip(marker, $"点击修改：{comment.Text}");
        marker.Click += CommentBadge_Click;

        var markerContent = new Grid { Width = 32, Height = 34 };

        var tail = new Polygon
        {
            Points =
            [
                new WPoint(8, 0),
                new WPoint(16, 0),
                new WPoint(8, 9)
            ],
            Fill = new SolidColorBrush(Color.FromArgb(255, 22, 139, 250)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 1)
        };

        var badge = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(255, 22, 139, 250)),
            BorderBrush = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(1.5),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = comment.Index.ToString(CultureInfo.InvariantCulture),
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        markerContent.Children.Add(tail);
        markerContent.Children.Add(badge);
        marker.Content = markerContent;
        var anchorDip = SnapshotPointToDip(comment.Anchor);
        Canvas.SetLeft(marker, Math.Clamp(anchorDip.X - 16, 0, Math.Max(0, Root.ActualWidth - 32)));
        Canvas.SetTop(marker, Math.Clamp(anchorDip.Y - 17, TopBarHeight, Math.Max(TopBarHeight, Root.ActualHeight - 34)));
        AnnotationCanvas.Children.Add(marker);
    }

    private void AddCommentPreview(AnnotationSessionComment comment)
    {
        var row = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(7)
        };
        var text = new TextBlock
        {
            Text = $"{comment.Index}. {comment.Text}",
            Foreground = new SolidColorBrush(Colors.White),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 4,
            Opacity = 0.9,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Child = text;
        CommentPreviewList.Children.Add(row);
        CommentPreviewPanel.Visibility = Visibility.Visible;
    }

    private void CommentBadge_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int commentIndex }
            || commentIndex < 0
            || commentIndex >= _comments.Count)
        {
            return;
        }

        var comment = _comments[commentIndex];
        CancelDraft();
        _editingCommentIndex = commentIndex;
        var selectionDip = SnapshotRectToDip(comment.Selection);
        _draftSelection = selectionDip;
        ShowSelectionPreview(selectionDip);
        NoteBox.Text = comment.Text;
        NotePanel.Title = $"修改批注 {comment.Index}";
        NotePanel.IsOpen = true;
        NoteBox.Focus(FocusState.Programmatic);
        NoteBox.SelectAll();
    }

    private void InteractiveElement_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void RebuildCommentUi()
    {
        AnnotationCanvas.Children.Clear();
        CommentPreviewList.Children.Clear();
        foreach (var comment in _comments)
        {
            DrawCommentBadge(comment);
            AddCommentPreview(comment);
        }

        if (_comments.Count == 0)
        {
            CommentPreviewPanel.Visibility = Visibility.Collapsed;
        }
    }

    private WRect DipRectToSnapshot(WRect rect)
    {
        var mapped = AnnotationCoordinateMapper.ScaleRectangle(
            new System.Drawing.RectangleF(
                (float)rect.X,
                (float)rect.Y,
                (float)rect.Width,
                (float)rect.Height),
            Root.ActualWidth,
            Root.ActualHeight,
            _snapshotPixelSize.Width,
            _snapshotPixelSize.Height);
        return new WRect(mapped.X, mapped.Y, mapped.Width, mapped.Height);
    }

    private WRect SnapshotRectToDip(WRect rect)
    {
        var mapped = AnnotationCoordinateMapper.ScaleRectangle(
            new System.Drawing.RectangleF(
                (float)rect.X,
                (float)rect.Y,
                (float)rect.Width,
                (float)rect.Height),
            _snapshotPixelSize.Width,
            _snapshotPixelSize.Height,
            Root.ActualWidth,
            Root.ActualHeight);
        return new WRect(mapped.X, mapped.Y, mapped.Width, mapped.Height);
    }

    private WPoint SnapshotPointToDip(WPoint point)
    {
        var mapped = AnnotationCoordinateMapper.ScalePoint(
            new System.Drawing.PointF((float)point.X, (float)point.Y),
            _snapshotPixelSize.Width,
            _snapshotPixelSize.Height,
            Root.ActualWidth,
            Root.ActualHeight);
        return new WPoint(mapped.X, mapped.Y);
    }

    private void CommentPreviewPanel_PointerPressed(object sender, PointerRoutedEventArgs e) =>
        InteractiveElement_PointerPressed(sender, e);

    private void ShowSelectionPreview(WRect selection)
    {
        SelectionPreview.Margin = new Thickness(selection.X, selection.Y, 0, 0);
        SelectionPreview.Width = Math.Max(1, selection.Width);
        SelectionPreview.Height = Math.Max(1, selection.Height);
        SelectionPreview.Visibility = Visibility.Visible;
    }

    private WPoint ClampToContent(WPoint point)
    {
        return new WPoint(
            Math.Clamp(point.X, 0, Math.Max(0, Root.ActualWidth)),
            Math.Clamp(point.Y, TopBarHeight, Math.Max(TopBarHeight, Root.ActualHeight)));
    }

    private WRect CreateClickSelection(WPoint point)
    {
        const double width = 72;
        const double height = 48;
        var x = Math.Clamp(point.X - width / 2, 0, Math.Max(0, Root.ActualWidth - width));
        var y = Math.Clamp(point.Y - height / 2, TopBarHeight, Math.Max(TopBarHeight, Root.ActualHeight - height));
        return new WRect(x, y, width, height);
    }

    private void UpdateSessionUi()
    {
        CommentCountChanged?.Invoke(this, _comments.Count);
    }

    private static WRect NormalizeRect(WPoint start, WPoint end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var right = Math.Max(start.X, end.X);
        var bottom = Math.Max(start.Y, end.Y);
        return new WRect(left, top, right - left, bottom - top);
    }

    private void SubmitNoteButton_Click(TeachingTip sender, object args)
    {
        SaveNote();
    }

    private void CancelNoteButton_Click(TeachingTip sender, object args)
    {
        CancelDraft();
        Root.Focus(FocusState.Programmatic);
    }

    private void WindowAnnotationOverlayWindow_Closed(object sender, WindowEventArgs args)
    {
        _isClosed = true;
        _snapshotRefreshVersion++;
        _followTimer.Stop();
        _targetWindowTracker.Dispose();
        TargetSnapshot.Source = null;
        if (_snapshotPath is not null)
        {
            DeleteSnapshot(_snapshotPath);
        }
    }

    private static void DeleteSnapshot(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Temporary snapshots are best-effort cleanup only.
        }
    }

    private sealed record CapturedSnapshot(string Path, System.Drawing.Size PixelSize);
}
