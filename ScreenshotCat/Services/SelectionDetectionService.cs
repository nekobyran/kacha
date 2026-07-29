using System.Drawing;
using ScreenshotCat.Interop;
using ScreenshotCat.Models;

namespace ScreenshotCat.Services;

public sealed class SelectionDetectionService
{
    public sealed record DetectedWindow(nint Hwnd, Rectangle Bounds);

    public Rectangle? DetectWindowOrControl(Point screenPoint, Rectangle monitorBounds)
    {
        return DetectControlOrWindow(screenPoint, monitorBounds);
    }

    public Rectangle? DetectWindowAtPoint(Point screenPoint, Rectangle monitorBounds, int? excludedProcessId = null)
    {
        var point = new NativeMethods.POINT { X = screenPoint.X, Y = screenPoint.Y };
        var hwnd = NativeMethods.WindowFromPoint(point);
        return TryDetectWindow(hwnd, monitorBounds, excludedProcessId);
    }

    public Rectangle? DetectTopLevelWindowAtPoint(Point screenPoint, Rectangle monitorBounds, int? excludedProcessId = null)
    {
        return DetectTopLevelWindowHandleAtPoint(screenPoint, monitorBounds, excludedProcessId)?.Bounds;
    }

    public DetectedWindow? DetectTopLevelWindowHandleAtPoint(Point screenPoint, Rectangle monitorBounds, int? excludedProcessId = null)
    {
        DetectedWindow? result = null;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
            if (ProcessIdentityService.IsCurrentApplicationProcess(processId)
                || (excludedProcessId.HasValue && processId == excludedProcessId.Value))
            {
                return true;
            }

            if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            {
                return true;
            }

            var candidate = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (!candidate.Contains(screenPoint))
            {
                return true;
            }

            var clamped = Rectangle.Intersect(candidate, monitorBounds);
            if (!IsUseful(candidate, monitorBounds) || !clamped.Contains(screenPoint))
            {
                return true;
            }

            result = new DetectedWindow(hwnd, clamped);
            return false;
        }, 0);

        return result;
    }

    public Rectangle? GetTopLevelWindowBounds(nint hwnd, Rectangle monitorBounds, int? excludedProcessId = null)
    {
        return TryDetectWindow(hwnd, monitorBounds, excludedProcessId);
    }

    public Rectangle? DetectForegroundWindow(Rectangle monitorBounds, int? excludedProcessId = null)
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        return TryDetectWindow(hwnd, monitorBounds, excludedProcessId);
    }

    public Rectangle? DetectControlOrWindow(Point screenPoint, Rectangle monitorBounds)
    {
        var control = TryDetectControl(screenPoint, monitorBounds);
        if (control.HasValue)
        {
            return control;
        }

        return TryDetectWindow(screenPoint, monitorBounds);
    }

    public Rectangle? DetectGraphicCard(CaptureResult capture, Point screenPoint)
    {
        var localX = Math.Clamp(screenPoint.X - capture.MonitorBounds.Left, 0, capture.Bitmap.Width - 1);
        var localY = Math.Clamp(screenPoint.Y - capture.MonitorBounds.Top, 0, capture.Bitmap.Height - 1);
        var baseColor = capture.Bitmap.GetPixel(localX, localY);
        var left = Scan(capture.Bitmap, localX, localY, -1, 0, baseColor);
        var right = Scan(capture.Bitmap, localX, localY, 1, 0, baseColor);
        var top = Scan(capture.Bitmap, localX, localY, 0, -1, baseColor);
        var bottom = Scan(capture.Bitmap, localX, localY, 0, 1, baseColor);

        var width = right - left;
        var height = bottom - top;
        if (width < 80 || height < 60 || width > capture.Bitmap.Width * 0.95 || height > capture.Bitmap.Height * 0.95)
        {
            return null;
        }

        return new Rectangle(capture.MonitorBounds.Left + left, capture.MonitorBounds.Top + top, width, height);
    }

    private static Rectangle? TryDetectControl(Point screenPoint, Rectangle monitorBounds)
    {
        var point = new NativeMethods.POINT { X = screenPoint.X, Y = screenPoint.Y };
        var hwnd = NativeMethods.WindowFromPoint(point);
        if (hwnd == 0)
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (ProcessIdentityService.IsCurrentApplicationProcess(processId))
        {
            return null;
        }

        // Prefer the deepest visible child under the cursor for control-sized selection.
        var leaf = hwnd;
        for (var depth = 0; depth < 8; depth++)
        {
            if (!NativeMethods.ScreenToClient(leaf, ref point))
            {
                break;
            }

            var child = NativeMethods.RealChildWindowFromPoint(leaf, point);
            if (child == 0 || child == leaf)
            {
                break;
            }

            leaf = child;
            point = new NativeMethods.POINT { X = screenPoint.X, Y = screenPoint.Y };
        }

        if (!NativeMethods.GetWindowRect(leaf, out var rect))
        {
            return null;
        }

        var candidate = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return IsUseful(candidate, monitorBounds) ? Rectangle.Intersect(candidate, monitorBounds) : null;
    }

    private static Rectangle? TryDetectWindow(Point screenPoint, Rectangle monitorBounds)
    {
        var point = new NativeMethods.POINT { X = screenPoint.X, Y = screenPoint.Y };
        var hwnd = NativeMethods.WindowFromPoint(point);
        return TryDetectWindow(hwnd, monitorBounds, excludedProcessId: null);
    }

    private static Rectangle? TryDetectWindow(nint hwnd, Rectangle monitorBounds, int? excludedProcessId)
    {
        if (hwnd == 0)
        {
            return null;
        }

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
        if (root == 0)
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(root, out var processId);
        if (ProcessIdentityService.IsCurrentApplicationProcess(processId)
            || (excludedProcessId.HasValue && processId == excludedProcessId.Value))
        {
            return null;
        }

        if (!NativeMethods.GetWindowRect(root, out var rect))
        {
            return null;
        }

        var candidate = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return IsUseful(candidate, monitorBounds) ? Rectangle.Intersect(candidate, monitorBounds) : null;
    }

    private static bool IsUseful(Rectangle rect, Rectangle monitorBounds)
    {
        var clamped = Rectangle.Intersect(rect, monitorBounds);
        return clamped.Width >= 24
            && clamped.Height >= 24
            && rect.Width <= monitorBounds.Width * 1.25
            && rect.Height <= monitorBounds.Height * 1.25;
    }

    private static int Scan(Bitmap bitmap, int x, int y, int dx, int dy, Color baseColor)
    {
        var currentX = x;
        var currentY = y;
        var steps = 0;
        while (currentX > 1 && currentY > 1 && currentX < bitmap.Width - 2 && currentY < bitmap.Height - 2 && steps < 1600)
        {
            var color = bitmap.GetPixel(currentX, currentY);
            if (ColorDistance(color, baseColor) > 58)
            {
                break;
            }

            currentX += dx;
            currentY += dy;
            steps++;
        }

        return dx != 0 ? currentX : currentY;
    }

    private static int ColorDistance(Color a, Color b)
    {
        return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
    }
}
