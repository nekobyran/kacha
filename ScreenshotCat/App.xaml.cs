using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using ScreenshotCat.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ScreenshotCat;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private readonly Mutex _singleInstanceMutex;
    private readonly bool _ownsSingleInstanceMutex;

    public MainWindow? MainWindow { get; private set; }

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\ScreenshotCat.SingleInstance",
            createdNew: out _ownsSingleInstanceMutex);
        InitializeComponent();
#if DEBUG
        UnhandledException += (_, eventArgs) =>
            File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ScreenshotCat-debug-crash.log"),
                eventArgs.Exception.ToString());
#endif
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        if (!_ownsSingleInstanceMutex)
        {
            Environment.Exit(0);
            return;
        }

        MainWindow = new MainWindow();
#if DEBUG
        var debugTarget = Environment.GetCommandLineArgs()
            .FirstOrDefault(value => value.StartsWith("--debug-annotation=", StringComparison.OrdinalIgnoreCase))?
            .Split('=', 2)[1];
        if (nint.TryParse(debugTarget, out var targetHwnd) && targetHwnd != 0)
        {
            MainWindow.StartDirectAnnotationForWindow(targetHwnd);
            return;
        }

        var debugToolbarTarget = Environment.GetCommandLineArgs()
            .FirstOrDefault(value => value.StartsWith("--debug-toolbar=", StringComparison.OrdinalIgnoreCase))?
            .Split('=', 2)[1];
        if (nint.TryParse(debugToolbarTarget, out targetHwnd) && targetHwnd != 0)
        {
            MainWindow.ShowPersistentAnnotationForWindow(targetHwnd);
            return;
        }
#endif
        MainWindow.Activate();
    }
}
