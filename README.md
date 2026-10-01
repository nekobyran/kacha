# ScreenshotCat

一只轻量、原生的 Windows 截图与批注工具。ScreenshotCat 基于 WinUI 3 与 .NET 构建，所有截图和批注都在本机处理。

## 功能

- 使用 `Scroll Lock` 或 `Ctrl + Alt + N` 随时开始截图
- 自动识别窗口、控件与自由框选区域
- 支持箭头、标记、编号批注和文字说明
- 支持多显示器与不同 DPI 缩放
- 保存后自动复制到剪贴板，便于直接粘贴分享
- 托盘常驻、安装后默认开机自启，并可对目标窗口持续批注
- `Ctrl + Tab` 短按切换聚焦窗口的批注模式，长按隐藏对应批注栏
- 批注模式中按 `Tab` 暂停或继续；退出时自动保存已有批注
- 截图默认保存到 `图片\ScreenshotCat`

## 下载与使用

1. 在 [Releases](https://github.com/nekobyran/kacha/releases) 下载最新的 `ScreenshotCat-*-win-x64-setup.exe`。
2. 运行 Setup，按向导完成当前用户安装。
3. 也可下载同版本 ZIP，解压后直接运行 `ScreenshotCat.exe`。

当前 Release 面向 Windows 10 1809（版本 17763）及以上的 64 位系统，并自带裁剪后的 .NET 运行时。为控制体积，包内不重复携带完整 Windows App Runtime；运行前需安装匹配的 [Windows App Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)（Windows 11 多数环境已预装）。程序暂未进行商业代码签名，首次运行时 Windows 可能显示安全提示；请只从本仓库 Releases 下载。

## 从源码构建

需要 Windows、.NET 10 SDK 和 Windows App SDK 构建环境。

```powershell
pwsh -File .\command\Build-ScreenshotCat.ps1 -Action Validate
pwsh -File .\command\Build-ScreenshotCat.ps1 -Action PackageRelease -Version 1.0.5
```

本项目只执行 Release 验证和构建，不生成 Debug 版本。`PackageRelease` 会执行 Release 验证、partial IL 裁剪、剥离调试/诊断文件及非运行必需组件，并同时输出 ZIP 与 Setup。SDK 缓存、临时文件和 NuGet 包位于 `D:\vibecoding\sdk`，发布包输出到工作区 `release/ScreenshotCat_Windows/release/`。

## 隐私

ScreenshotCat 不上传截图、批注或剪贴板内容，也不包含遥测服务。截图文件仅保存在本机。安装后默认通过当前用户的 Windows `Run` 注册项开机启动，可通过退出程序并在系统“启动应用”设置中关闭。

## 赞助

如果 ScreenshotCat 对你有帮助，可以自愿扫码赞助。赞助与软件功能、更新和支持无绑定。

<p align="center">
  <img src="./assets/sponsor.jpg" alt="ScreenshotCat 赞助码" width="320">
</p>

## 参与贡献

欢迎提交 Issue 与 Pull Request。提交前请先运行验证脚本，并避免在测试截图中包含隐私内容。

## 许可证

[MIT License](./LICENSE)
