# Agent Note: Windows 桌面 Web 宿主

Status: implemented

[English](2026-08-17-windows-desktop-web-host.md) | 中文

## 问题

Windows 用户需要现有 Web UI 的桌面入口，同时不能另建一套客户端运行时，也不能把 Node.js 和其包树分发进可执行文件。

## 决策

`avalonia/DeepSeekHarness.Desktop` 提供名为 `DeepSeekHarness.exe` 的 Windows x64 Avalonia 应用。它通过隐藏的 `cmd.exe` 和 `pnpm.cmd` 在仓库根目录启动 `pnpm dsh web --port 0`，从 stdout 解析回环 URL，再将其赋给 `NativeWebView`。WebView 使用 Avalonia 的 Windows WebView2 后端。启动失败会显示捕获到的 stderr，并允许重试。

Avalonia 设计模式只渲染静态启动面板，不附加 `NativeWebView`、启动 pnpm 或创建 WebView2 子窗口。设计器宿主无法稳定创建 WebView2 HWND。

启动器将命令进程加入带有 `KILL_ON_JOB_CLOSE` 的 Windows Job Object。重试和关闭主窗口会释放 Job Object 并等待根进程，因此 pnpm、Corepack、Node 和 Web 服务器后代即使在命令包装器提前退出后也会停止。

项目使用 `net10.0` 和 `win-x64`。Debug 和设计时构建关闭 AOT 和裁剪以供 Avalonia 设计器使用；Release 发布使用自包含 Native AOT 和裁剪。其 Windows 兼容清单启用 `NativeWebView` 使用的原生子窗口。该可执行文件仍依赖本地仓库、Node.js、pnpm、已安装的工作区依赖和 WebView2 Runtime。

## 考虑过的替代方案

- 在 Avalonia 中重写 React 客户端 — 否决；桌面外壳托管现有 Web UI，其行为仍由 Web 应用负责。
- 使用嵌入式 Chromium 引擎 — 否决；官方 `NativeWebView` 使用已安装的 WebView2 Runtime，可保持较小的可执行文件。
- 只依赖 `Process.Kill(entireProcessTree: true)` — 否决；pnpm 的命令包装器可能在关闭前退出，留下脱离树的 Node 后代。Job Object 拥有完整的启动树。
- 为支持 WebView 而关闭 AOT — 否决；Avalonia 官方 WebView 包支持裁剪和 AOT，Native AOT 发布也已成功。

## 后果

桌面可执行文件只支持 Windows x64，且从此仓库的检出目录启动。它加载与 `pnpm dsh web` 相同的回环 Web UI；不打包 Web 客户端、Node.js、pnpm 或安装器。单元测试覆盖 URL 解析、缺少 URL 时的失败文本以及重试清理；原生运行验证 WebView 启动和进程树关闭。
