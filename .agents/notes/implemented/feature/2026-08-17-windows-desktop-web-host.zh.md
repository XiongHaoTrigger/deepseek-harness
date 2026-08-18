# Agent Note: Windows 桌面 Web 宿主

Status: implemented

[English](2026-08-17-windows-desktop-web-host.md) | 中文

## 问题

Windows 用户需要现有 Web UI 的桌面入口，同时不能另建一套客户端运行时。

## 决策

`avalonia/DeepSeekHarness.Desktop` 提供名为 `DeepSeekHarness.exe` 的 Windows x64 Avalonia 应用。Debug 构建通过隐藏的 `cmd.exe` 和 `pnpm.cmd` 在仓库根目录启动 `pnpm dsh web --port 0`。Release 构建使用[附属 dsh 运行时](2026-08-18-windows-attached-dsh-runtime.md)，直接启动其捆绑的 `node.exe`。两种启动路径都会从 stdout 解析回环 URL，再将其赋给 `NativeWebView`。WebView 使用 Avalonia 的 Windows WebView2 后端。启动失败会显示捕获到的 stderr，并允许重试。

Avalonia 设计模式只渲染静态启动面板，不附加 `NativeWebView`、启动 Web 服务或创建 WebView2 子窗口。设计器宿主无法稳定创建 WebView2 HWND。

启动器将根进程加入带有 `KILL_ON_JOB_CLOSE` 的 Windows Job Object。重试和关闭主窗口会释放 Job Object 并等待根进程，因此命令包装器（如有）、Node 和 Web 服务器后代即使在根进程提前退出后也会停止。

项目使用 `net10.0` 和 `win-x64`。Debug 和设计时构建关闭 AOT 和裁剪以供 Avalonia 设计器使用；Release 发布使用自包含 Native AOT 和裁剪。其 Windows 兼容清单启用 `NativeWebView` 使用的原生子窗口。发布后的可执行文件需要 WebView2 Runtime，但不需要本地仓库、Node.js、pnpm 或已安装的工作区依赖。

## 考虑过的替代方案

- 在 Avalonia 中重写 React 客户端 — 否决；桌面外壳托管现有 Web UI，其行为仍由 Web 应用负责。
- 使用嵌入式 Chromium 引擎 — 否决；官方 `NativeWebView` 使用已安装的 WebView2 Runtime，可保持较小的可执行文件。
- 只依赖 `Process.Kill(entireProcessTree: true)` — 否决；pnpm 的命令包装器可能在关闭前退出，留下脱离树的 Node 后代。Job Object 拥有完整的启动树。
- 从发布后的可执行文件启动 pnpm — 否决；附属运行时使发布后的应用不依赖开发工具链。
- 为支持 WebView 而关闭 AOT — 否决；Avalonia 官方 WebView 包支持裁剪和 AOT，Native AOT 发布也已成功。

## 后果

桌面可执行文件只支持 Windows x64。它加载与 `pnpm dsh web` 相同的回环 Web UI；Release 负载包含 Web 客户端、Node.js 和已部署的依赖闭包，但不包含 pnpm 或安装器。单元测试覆盖 URL 解析、缺少 URL 时的失败文本、重试清理和附属运行时启动解析；原生运行验证 WebView 启动和进程树关闭。
