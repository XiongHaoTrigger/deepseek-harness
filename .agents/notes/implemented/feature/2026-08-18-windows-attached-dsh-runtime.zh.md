# Agent Note: Windows 附属 dsh 运行时

Status: implemented

[English](2026-08-18-windows-attached-dsh-runtime.md) | 中文

## 问题

已发布的 Windows 桌面应用不能要求使用源代码 checkout、pnpm 或本机安装的 Node.js 运行时来提供现有 Web UI。

## 决策

`avalonia/runtime-packaging/build-runtime.ps1` 从已构建的 CLI、前端资源、已部署的生产依赖闭包、`dsh-web-entry.js` 以及固定且经 SHA-256 验证的 Node ZIP 构建 Windows x64 运行时目录。该脚本会实体化链接、验证依赖解析，并可将生成的服务作为冒烟测试启动。

Release 发布会验证运行时目录，并将其复制到 `DeepSeekHarness.exe` 同级的 `runtime/`。桌面宿主通过打包入口启动 `runtime/node.exe`，默认将 dsh profile 和会话数据存放在 `%LOCALAPPDATA%\DeepSeekHarness\dsh`，除非已有 `DSH_HOME`。Debug 构建保留仓库 pnpm 启动路径。

## 考虑过的替代方案

- 从发布后的应用启动 `pnpm dsh web` — 否决；已发布的应用仍会依赖开发工具链和 checkout。
- 将 Node.js 打包进 Native AOT 可执行文件 — 否决；Node 和已部署的依赖闭包作为可执行文件旁的普通文件保留，不引入自定义可执行文件打包器。
- 使用 Python SDK 运行时替换 Web UI — 否决；该运行时公开 JSON-RPC，需要实现第二套桌面客户端。

## 后果

Release 负载包含 Node.js 和已部署的 JavaScript 依赖闭包，增大了其体积。运行时 Node 升级必须同时更新固定版本和 SHA-256。该包仅支持 Windows x64，需要 WebView2 Runtime，且不包含 pnpm、安装器或自动更新。
