# ScreenshotCat 项目索引

- 应用入口：`ScreenshotCat/App.xaml.cs`
- 主窗口：`ScreenshotCat/MainWindow.xaml.cs`
- 截图与批注 UI：`ScreenshotCat/CaptureOverlayWindow.xaml(.cs)`
- 常驻批注栏：`ScreenshotCat/AttachedToolbarWindow.xaml(.cs)`
- 批注会话栏/暂停：`ScreenshotCat/AnnotationSessionToolbarWindow.xaml(.cs)`
- 全屏批注顶栏：`ScreenshotCat/FullscreenAnnotationToolbarWindow.xaml(.cs)`
- 窗口批注层：`ScreenshotCat/WindowAnnotationOverlayWindow.xaml(.cs)`
- 截图服务：`ScreenshotCat/Services/ScreenCaptureService.cs`
- 保存服务：`ScreenshotCat/Services/ScreenshotSaveService.cs`
- 验证入口：`ScreenshotCat.Verification/Program.cs`
- Release-only 构建与打包：`command/Build-ScreenshotCat.ps1`
- Setup 定义：`installer/ScreenshotCat.iss`
- 静态发布页：`website/`
- 静态页验证与部署：`command/Deploy-KachaSite.ps1`
- Cloudflare Workers Static Assets：`wrangler.jsonc` → `https://kacha.nkbr.cc/`
- 开源说明：`README.md`、`LICENSE`、`SECURITY.md`、`CHANGELOG.md`
- 赞助码：`assets/sponsor.jpg`
- 发布产物：工作区 `release/ScreenshotCat_Windows/release/`（ZIP、Setup、SHA-256）
