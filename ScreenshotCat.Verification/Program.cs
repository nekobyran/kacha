using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using ScreenshotCat.Models;
using ScreenshotCat.Services;
using WPoint = Windows.Foundation.Point;
using WRect = Windows.Foundation.Rect;

var root = Path.Combine(Path.GetTempPath(), $"ScreenshotCat-coordinate-verification-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
try
{
    var results = new List<object>
    {
        VerifyAnnotationSession(root, "dpi-96", 1000, 800, 1000, 800),
        VerifyAnnotationSession(root, "dpi-120", 800, 640, 1000, 800),
        VerifySecondaryOrigin(root),
        VerifyAnnotationHotkeyGesture()
    };
    Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
}
finally
{
    Directory.Delete(root, recursive: true);
}

static object VerifyAnnotationHotkeyGesture()
{
    var gesture = new AnnotationHotkeyGesture();
    var target = new nint(4242);

    AssertGesture(gesture.OnCtrlTabDown(target, 1_000), true, AnnotationHotkeyAction.None, "short down");
    AssertGesture(gesture.OnCtrlTabUp(1_300), true, AnnotationHotkeyAction.ToggleMode, "short up");

    AssertGesture(gesture.OnCtrlTabDown(target, 2_000), true, AnnotationHotkeyAction.None, "long down");
    AssertGesture(
        gesture.OnCtrlTabLongPress(2_000 + AnnotationHotkeyGesture.LongPressMilliseconds),
        true,
        AnnotationHotkeyAction.HideToolbar,
        "long threshold");
    AssertGesture(gesture.OnCtrlTabUp(2_900), true, AnnotationHotkeyAction.None, "long up");

    AssertGesture(gesture.OnPlainTabDown(annotationModeActive: false), false, AnnotationHotkeyAction.None, "tab passthrough");
    AssertGesture(gesture.OnPlainTabDown(annotationModeActive: true), true, AnnotationHotkeyAction.TogglePause, "pause");
    AssertGesture(gesture.OnPlainTabDown(annotationModeActive: true), true, AnnotationHotkeyAction.None, "tab repeat");
    AssertGesture(gesture.OnPlainTabUp(), true, AnnotationHotkeyAction.None, "tab up");
    AssertGesture(gesture.OnPlainTabDown(annotationModeActive: true), true, AnnotationHotkeyAction.TogglePause, "resume");
    AssertGesture(gesture.OnPlainTabUp(), true, AnnotationHotkeyAction.None, "resume up");

    return new
    {
        Case = "annotation-hotkey-gesture",
        ShortPress = "toggle-mode",
        LongPressMilliseconds = AnnotationHotkeyGesture.LongPressMilliseconds,
        LongPress = "hide-toolbar",
        Tab = "pause-resume",
        Passed = true
    };
}

static void AssertGesture(
    AnnotationHotkeyGestureResult result,
    bool handled,
    AnnotationHotkeyAction action,
    string label)
{
    if (result.Handled != handled || result.Action != action)
    {
        throw new InvalidOperationException(
            $"{label}: expected handled={handled}, action={action}; actual handled={result.Handled}, action={result.Action}.");
    }
}

static object VerifyAnnotationSession(
    string root,
    string name,
    double overlayWidth,
    double overlayHeight,
    int snapshotWidth,
    int snapshotHeight)
{
    var caseRoot = Path.Combine(root, name);
    Directory.CreateDirectory(caseRoot);
    var snapshotPath = Path.Combine(caseRoot, "snapshot.png");
    using (var snapshot = new Bitmap(snapshotWidth, snapshotHeight, PixelFormat.Format32bppPArgb))
    using (var graphics = Graphics.FromImage(snapshot))
    {
        graphics.Clear(Color.FromArgb(255, 36, 42, 52));
        snapshot.Save(snapshotPath, ImageFormat.Png);
    }

    var expected = new RectangleF(100, 120, 220, 140);
    var dipSelection = AnnotationCoordinateMapper.ScaleRectangle(
        expected,
        snapshotWidth,
        snapshotHeight,
        overlayWidth,
        overlayHeight);
    var mapped = AnnotationCoordinateMapper.ScaleRectangle(
        dipSelection,
        overlayWidth,
        overlayHeight,
        snapshotWidth,
        snapshotHeight);
    var comment = new AnnotationSessionComment(
        1,
        "coordinate verification",
        new WPoint(mapped.X + mapped.Width / 2, mapped.Y + mapped.Height / 2),
        new WRect(mapped.X, mapped.Y, mapped.Width, mapped.Height));
    var result = new AnnotationSessionSaveService(caseRoot).Save(snapshotPath, [comment]);
    var blueBounds = FindBlueBounds(result.ImagePaths[0]);
    AssertNear(blueBounds.Left, expected.Left, 3, $"{name} left");
    AssertNear(blueBounds.Top, expected.Top, 3, $"{name} top");
    AssertNear(blueBounds.Right, expected.Right, 3, $"{name} right");
    AssertNear(blueBounds.Bottom, expected.Bottom, 3, $"{name} bottom");
    return new
    {
        Case = name,
        Overlay = $"{overlayWidth}x{overlayHeight}",
        Snapshot = $"{snapshotWidth}x{snapshotHeight}",
        Mapped = new { mapped.X, mapped.Y, mapped.Width, mapped.Height },
        BlueBounds = blueBounds,
        Passed = true
    };
}

static object VerifySecondaryOrigin(string root)
{
    var caseRoot = Path.Combine(root, "secondary-origin");
    Directory.CreateDirectory(caseRoot);
    using var bitmap = new Bitmap(1000, 800, PixelFormat.Format32bppPArgb);
    using (var graphics = Graphics.FromImage(bitmap))
    {
        graphics.Clear(Color.FromArgb(255, 36, 42, 52));
    }

    var capture = new CaptureResult(
        bitmap,
        new Rectangle(-1920, 100, 1000, 800),
        Path.Combine(caseRoot, "unused.png"));
    var selection = new Rectangle(-1820, 220, 300, 220);
    var annotation = new Annotation(
        AnnotationKind.Marker,
        new PointF(-1800, 240),
        new PointF(-1640, 360),
        Color.FromArgb(255, 22, 139, 250).ToArgb());
    var result = new ScreenshotSaveService(caseRoot).Save(capture, selection, [annotation], []);
    var blueBounds = FindBlueBounds(result.ImagePath);
    AssertNear(blueBounds.Left, 20, 3, "secondary left");
    AssertNear(blueBounds.Top, 20, 3, "secondary top");
    AssertNear(blueBounds.Right, 180, 3, "secondary right");
    AssertNear(blueBounds.Bottom, 140, 3, "secondary bottom");
    return new
    {
        Case = "secondary-origin",
        MonitorOrigin = new { X = -1920, Y = 100 },
        SelectionOrigin = new { X = selection.X, Y = selection.Y },
        BlueBounds = blueBounds,
        Passed = true
    };
}

static Rectangle FindBlueBounds(string path)
{
    using var image = new Bitmap(path);
    var left = image.Width;
    var top = image.Height;
    var right = -1;
    var bottom = -1;
    for (var y = 0; y < image.Height; y++)
    {
        for (var x = 0; x < image.Width; x++)
        {
            var color = image.GetPixel(x, y);
            if (color.B < 220 || color.G < 100 || color.G > 180 || color.R > 70)
            {
                continue;
            }

            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }
    }

    if (right < 0)
    {
        throw new InvalidOperationException($"No annotation blue pixels found in {path}.");
    }

    return Rectangle.FromLTRB(left, top, right, bottom);
}

static void AssertNear(int actual, float expected, int tolerance, string label)
{
    if (Math.Abs(actual - expected) > tolerance)
    {
        throw new InvalidOperationException(
            $"{label}: expected {expected} +/- {tolerance}, actual {actual}.");
    }
}
