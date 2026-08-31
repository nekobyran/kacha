using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScreenshotCat.Models;

namespace ScreenshotCat;

public sealed partial class MainPage : Page
{
    private MainWindow? _mainWindow;

    public MainPage()
    {
        InitializeComponent();
        Loaded += MainPage_Loaded;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        _mainWindow = ((App)Application.Current).MainWindow;
        if (_mainWindow is null)
        {
            return;
        }

        RefreshHotkeyStatus();
        _mainWindow.ScreenshotSaved += MainWindow_ScreenshotSaved;
    }

    private void RefreshHotkeyStatus()
    {
        if (_mainWindow is null)
        {
            return;
        }

        var settings = _mainWindow.HotkeySettings;
        var hotkeyStatus = _mainWindow.HotkeyRegistered
            ? $"截图键 {settings.PrimaryCapture} 与 {settings.SecondaryCapture} 已启用。"
            : $"截图键 {settings.PrimaryCapture} / {settings.SecondaryCapture} 注册失败。";
        var startupStatus = _mainWindow.StartupRegistered
            ? "开机自启已启用。"
            : "开机自启注册失败。";
        HotkeyText.Text = $"{hotkeyStatus} {startupStatus}";
    }

    private async void HotkeySettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow is null)
        {
            return;
        }

        var current = _mainWindow.HotkeySettings;
        var primaryBox = new TextBox
        {
            Header = "主截图快捷键",
            Text = current.PrimaryCapture.ToString(),
            PlaceholderText = "例如 Ctrl+Shift+S",
            Width = 320
        };
        var secondaryBox = new TextBox
        {
            Header = "备用截图快捷键",
            Text = current.SecondaryCapture.ToString(),
            PlaceholderText = "例如 Ctrl+Alt+N",
            Width = 320
        };
        var errorInfo = new InfoBar
        {
            Severity = InfoBarSeverity.Error,
            IsOpen = false,
            IsClosable = false
        };
        var resetButton = new Button
        {
            Content = "恢复默认",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        resetButton.Click += (_, _) =>
        {
            primaryBox.Text = HotkeySettings.Default.PrimaryCapture.ToString();
            secondaryBox.Text = HotkeySettings.Default.SecondaryCapture.ToString();
            errorInfo.IsOpen = false;
        };

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = "支持 Ctrl / Alt / Shift / Win + A-Z、0-9、F1-F24 和常用功能键。Tab 相关组合保留给批注操作。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
            MaxWidth = 320
        });
        content.Children.Add(primaryBox);
        content.Children.Add(secondaryBox);
        content.Children.Add(resetButton);
        content.Children.Add(errorInfo);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "快捷键管理",
            Content = content,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!HotkeyBinding.TryParse(primaryBox.Text, out var primary, out var primaryError))
            {
                errorInfo.Message = $"主快捷键：{primaryError}";
                errorInfo.IsOpen = true;
                args.Cancel = true;
                return;
            }

            if (!HotkeyBinding.TryParse(secondaryBox.Text, out var secondary, out var secondaryError))
            {
                errorInfo.Message = $"备用快捷键：{secondaryError}";
                errorInfo.IsOpen = true;
                args.Cancel = true;
                return;
            }

            var settings = new HotkeySettings(primary, secondary);
            if (!_mainWindow.TryUpdateHotkeySettings(settings, out var error))
            {
                errorInfo.Message = error;
                errorInfo.IsOpen = true;
                args.Cancel = true;
                return;
            }

            errorInfo.IsOpen = false;
            RefreshHotkeyStatus();
        };

        await dialog.ShowAsync();
    }

    private void MainWindow_ScreenshotSaved(object? sender, SaveResult result)
    {
        CopyButton.IsEnabled = true;
        var status = _mainWindow?.LastSaveCopiedToClipboard == true
            ? "已保存并复制"
            : "已保存；剪贴板暂时不可用，可点击复制重试";
        StatusText.Text = $"{status}: {result.ImagePath}\n批注: {result.AnnotationText}\n记录: {result.LogPath}";
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow?.StartCapture();
    }

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow?.HideToBackground();
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow?.ExitApplication();
    }

    private async void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow is null)
        {
            return;
        }

        var copied = await _mainWindow.CopyLastAsync();
        if (_mainWindow.LastSave is not null)
        {
            var status = copied ? "已复制" : "复制失败，请稍后重试";
            StatusText.Text = $"{status}: {_mainWindow.LastSave.ImagePath}\n批注: {_mainWindow.LastSave.AnnotationText}\n记录: {_mainWindow.LastSave.LogPath}";
        }
    }

    private void GraphicToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_mainWindow is not null)
        {
            _mainWindow.AutoGraphicRecognition = GraphicToggle.IsOn;
        }
    }
}
