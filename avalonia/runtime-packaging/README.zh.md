# dsh Web 运行时打包（Windows x64）

[English](README.md) | 中文

构建附属运行时目录，使现有 dsh Web UI 可在未安装 Node.js 或 pnpm 的机器上启动。它通过便携式 `node.exe` 运行已构建的 `apps/cli` `lib/` JavaScript 和 `apps/web` 前端 `dist/`，不会运行 `pnpm dsh web` 或 TypeScript source loader。

## 前置条件

- Windows x64、PowerShell 7+（`pwsh`）和不依赖 `tar` 的 `Expand-Archive` 支持（任意原生 Windows 10/11）。
- 已完成 `pnpm install` 的 deepseek-harness checkout：`PATH` 中存在 `node` 和 `pnpm`，workspace store 已就绪，且可访问 `nodejs.org`（或有本地 `node-v<ver>-win-x64.zip`）。
- Node 版本兼容性：固定的 `node.exe` 必须满足仓库 `engines.node` 范围（`^22.19.0 || >=24.0.0`）；脚本会验证。

## 构建运行时

```pwsh
pwsh ./build-runtime.ps1
```

默认 Node ZIP 通过 SHA-256 固定。`-NodeVersion` 与默认值不同时需要同时提供匹配的 `-NodeSha256`。`-NodeZipPath <zip>` 仅会在通过相同校验后使用本地 ZIP。`-SkipBuild` 复用 `apps/cli/lib` 和 `apps/web/dist`；`-SkipDeploy` 复用 `dist/runtime`；`-SmokeTest` 会启动、请求并停止运行时。脚本先运行 `pnpm run build`，以生成 CLI `lib/` 和 Web `dist/` 产物。

## 手动启动运行时

```pwsh
.\dist\runtime\node.exe .\dist\runtime\dsh-web-entry.js web --port 0
```

设置 `DSH_HOME` 到隔离目录，可避免自动初始化的 `profiles/` 和会话数据写入运行时目录。服务就绪后会输出 `dsh web: http://127.0.0.1:<port>`。

## 输出目录

`dist/runtime/` 包含 `node.exe`、`dsh-web-entry.js`、`package.json`、`lib/`、`config/`、已部署的 `node_modules/` 依赖闭包（真实文件，无符号链接或 junction），以及位于 `node_modules/@deepseek-ai/dsh-web-frontend/dist/` 的 Web 静态资源。所有生成产物均由 Git 忽略。

## 桌面应用集成

Avalonia 桌面应用的 Debug 构建仍会在仓库根目录启动 `pnpm.cmd dsh web`。Release 发布会将 `dist/runtime/` 复制到 `DeepSeekHarness.exe` 同级目录，并启动 `runtime/node.exe dsh-web-entry.js web --port 0`。可执行文件默认将 dsh profile 和会话数据存放在 `%LOCALAPPDATA%\DeepSeekHarness\dsh`，会保留已有的 `DSH_HOME`，并且仍需要 Microsoft Edge WebView2 Runtime。
