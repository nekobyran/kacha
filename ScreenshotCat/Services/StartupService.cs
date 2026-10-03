using Microsoft.Win32;
using System.Security;
using System.Reflection;

namespace ScreenshotCat.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "ScreenshotCat";
    public const string StartupArgument = "--startup";

    public bool EnableForCurrentExecutable()
    {
        var executablePath = ResolveStartupExecutablePath();
        if (executablePath is null)
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return false;
            }

            key.SetValue(RunValueName, $"\"{executablePath}\" {StartupArgument}", RegistryValueKind.String);
            return true;
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? ResolveStartupExecutablePath()
    {
        var assemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
        if (!string.IsNullOrWhiteSpace(assemblyName))
        {
            var appHostPath = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.exe");
            if (File.Exists(appHostPath))
            {
                return appHostPath;
            }
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath)
            || !File.Exists(processPath)
            || string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return processPath;
    }
}
