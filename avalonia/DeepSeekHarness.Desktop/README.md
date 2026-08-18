# DeepSeek Harness Desktop

English | [中文](README.zh.md)

## Development environment

- Windows 10 or 11 on x64.
- .NET SDK 10.
- Node.js that satisfies the repository `engines.node` setting (`^22.19.0 || >=24.0.0`) and pnpm.
- A deepseek-harness checkout with `pnpm install` completed.
- Microsoft Edge WebView2 Runtime.

## Run from source

From the repository root:

```pwsh
pnpm install
dotnet run --project avalonia/DeepSeekHarness.Desktop
```

The Debug application starts `pnpm.cmd dsh web --port 0` from the repository root, then opens the emitted loopback URL in WebView2.

## Publish and run

Build the attached dsh runtime before publishing:

```pwsh
pwsh ./avalonia/runtime-packaging/build-runtime.ps1 -SmokeTest
dotnet publish avalonia/DeepSeekHarness.Desktop/DeepSeekHarness.Desktop.csproj -c Release -r win-x64
```

Start `avalonia/DeepSeekHarness.Desktop/bin/Release/net10.0/win-x64/publish/DeepSeekHarness.exe` after publishing. Release publishing places `runtime/` beside the executable; it starts `runtime/node.exe` directly and does not need a local repository checkout, Node.js, pnpm, or repository `node_modules`.

The published application requires Windows x64 and Microsoft Edge WebView2 Runtime. It does not bundle an installer, pnpm, or automatic updates.
