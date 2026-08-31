using System.Text.Json;
using ScreenshotCat.Models;

namespace ScreenshotCat.Services;

public sealed class HotkeySettingsService
{
    private readonly string _settingsPath;

    public HotkeySettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreenshotCat",
            "hotkeys.json");
    }

    public string SettingsPath => _settingsPath;

    public HotkeySettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return HotkeySettings.Default;
            }

            var document = JsonSerializer.Deserialize<HotkeySettingsDocument>(File.ReadAllText(_settingsPath));
            if (document is null
                || !HotkeyBinding.TryParse(document.PrimaryCapture, out var primary, out _)
                || !HotkeyBinding.TryParse(document.SecondaryCapture, out var secondary, out _))
            {
                return HotkeySettings.Default;
            }

            var settings = new HotkeySettings(primary, secondary);
            return settings.TryValidate(out _) ? settings : HotkeySettings.Default;
        }
        catch
        {
            return HotkeySettings.Default;
        }
    }

    public bool TrySave(HotkeySettings settings, out string error)
    {
        error = string.Empty;
        if (!settings.TryValidate(out error))
        {
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var document = new HotkeySettingsDocument
            {
                PrimaryCapture = settings.PrimaryCapture.ToString(),
                SecondaryCapture = settings.SecondaryCapture.ToString()
            };
            var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
            var tempPath = _settingsPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _settingsPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            error = $"保存快捷键设置失败：{ex.Message}";
            return false;
        }
    }

    private sealed class HotkeySettingsDocument
    {
        public string PrimaryCapture { get; set; } = HotkeySettings.Default.PrimaryCapture.ToString();

        public string SecondaryCapture { get; set; } = HotkeySettings.Default.SecondaryCapture.ToString();
    }
}
