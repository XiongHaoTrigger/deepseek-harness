# DeepSeek Harness Desktop

English | [中文](README.zh.md)

Requires Windows x64, .NET SDK 10, Node.js, pnpm, installed repository dependencies, and the Microsoft Edge WebView2 Runtime.

Run from the repository root with `dotnet run --project avalonia/DeepSeekHarness.Desktop`.

Publish a Native AOT executable with `dotnet publish avalonia/DeepSeekHarness.Desktop/DeepSeekHarness.Desktop.csproj -c Release -r win-x64`.

The published executable still starts the repository's `pnpm dsh web`; it does not bundle Node.js, pnpm, or `node_modules`.
