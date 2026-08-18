# DeepSeek Harness 桌面版

[English](README.md) | 中文

## 开发环境

- Windows 10 或 11 x64。
- .NET SDK 10。
- 满足仓库 `engines.node` 设置（`^22.19.0 || >=24.0.0`）的 Node.js 以及 pnpm。
- 已完成 `pnpm install` 的 deepseek-harness checkout。
- Microsoft Edge WebView2 Runtime。

## 从源代码启动

在仓库根目录执行：

```pwsh
pnpm install
dotnet run --project avalonia/DeepSeekHarness.Desktop
```

Debug 应用会在仓库根目录启动 `pnpm.cmd dsh web --port 0`，然后在 WebView2 中打开输出的回环 URL。

## 发布并启动

发布前先构建附属 dsh 运行时：

```pwsh
pwsh ./avalonia/runtime-packaging/build-runtime.ps1 -SmokeTest
dotnet publish avalonia/DeepSeekHarness.Desktop/DeepSeekHarness.Desktop.csproj -c Release -r win-x64
```

发布后启动 `avalonia/DeepSeekHarness.Desktop/bin/Release/net10.0/win-x64/publish/DeepSeekHarness.exe`。Release 发布会将 `runtime/` 放在可执行文件旁，并直接启动 `runtime/node.exe`；不需要本地仓库 checkout、Node.js、pnpm 或仓库 `node_modules`。

发布后的应用需要 Windows x64 和 Microsoft Edge WebView2 Runtime。不包含安装器、pnpm 或自动更新。
