# DeepSeek Harness 桌面版

[English](README.md) | 中文

需要 Windows x64、.NET SDK 10、Node.js、pnpm、已安装的仓库依赖以及 Microsoft Edge WebView2 Runtime。

在仓库根目录执行 `dotnet run --project avalonia/DeepSeekHarness.Desktop`。

使用 `dotnet publish avalonia/DeepSeekHarness.Desktop/DeepSeekHarness.Desktop.csproj -c Release -r win-x64` 发布 Native AOT 可执行文件。

发布后的可执行文件仍会启动仓库中的 `pnpm dsh web`；它不打包 Node.js、pnpm 或 `node_modules`。
