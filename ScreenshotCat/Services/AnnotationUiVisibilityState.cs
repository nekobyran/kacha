namespace ScreenshotCat.Services;

/// <summary>
/// Remembers the target windows whose annotation toolbar the user hid. Hiding only affects
/// toolbar visibility: annotation stays available and the toolbar must not reappear until the
/// user explicitly shows it again.
/// </summary>
public sealed class AnnotationUiVisibilityState
{
    private readonly HashSet<nint> _hiddenTargets = [];

    public bool IsToolbarHidden(nint targetHwnd) => _hiddenTargets.Contains(targetHwnd);

    public void HideToolbar(nint targetHwnd) => _hiddenTargets.Add(targetHwnd);

    public void ShowToolbar(nint targetHwnd) => _hiddenTargets.Remove(targetHwnd);

    /// <summary>
    /// A hidden target still starts an annotation session; the session toolbar just starts hidden.
    /// </summary>
    public bool SessionToolbarVisibleOnStart(nint targetHwnd) => !IsToolbarHidden(targetHwnd);
}
