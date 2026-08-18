# dsh Web runtime packaging (Windows x64)

English | [中文](README.zh.md)

Builds an attached runtime directory that boots the existing dsh Web UI on a
machine with no Node.js or pnpm installed. It runs built `apps/cli` `lib/`
JavaScript and the built `apps/web` frontend dist through a portable `node.exe`;
it never runs `pnpm dsh web` or a TypeScript source loader.

## Prerequisites

- Windows x64 with PowerShell 7+ (`pwsh`) and `tar`-free `Expand-Archive` support (any stock Windows 10/11).
- A deepseek-harness checkout with `pnpm install` completed: `node` and `pnpm` on `PATH`, the workspace store populated, and a reachable `nodejs.org` (or a local `node-v<ver>-win-x64.zip`).
- Node version compatibility: the pinned `node.exe` must satisfy the repository's `engines.node` range (`^22.19.0 || >=24.0.0`); the script verifies it.

## Build the runtime

```pwsh
pwsh ./build-runtime.ps1
```

The default Node ZIP is pinned by SHA-256. `-NodeVersion` needs a matching `-NodeSha256` when it differs from the default. `-NodeZipPath <zip>` uses a local ZIP only after the same verification. `-SkipBuild` reuses `apps/cli/lib` and `apps/web/dist`; `-SkipDeploy` reuses `dist/runtime`; `-SmokeTest` launches, fetches, and stops the runtime. The script runs `pnpm run build` first so the CLI `lib/` and Web `dist/` artifacts exist.

## Start the runtime manually

```pwsh
.\dist\runtime\node.exe .\dist\runtime\dsh-web-entry.js web --port 0
```

Set `DSH_HOME` to an isolated directory to keep the auto-initialized `profiles/` and session data out of the runtime directory. The server prints `dsh web: http://127.0.0.1:<port>` once ready.

## Output directory

`dist/runtime/` contains `node.exe`, `dsh-web-entry.js`, `package.json`, `lib/`, `config/`, the deployed `node_modules/` dependency closure (real files, no symlinks or junctions), and the Web static assets under `node_modules/@deepseek-ai/dsh-web-frontend/dist/`. All generated output is gitignored.

## Desktop integration

Debug builds of the Avalonia desktop application still launch `pnpm.cmd dsh web` from the repository root. Release publishing copies `dist/runtime/` beside `DeepSeekHarness.exe`, then starts `runtime/node.exe dsh-web-entry.js web --port 0`. The executable defaults dsh profiles and session data to `%LOCALAPPDATA%\DeepSeekHarness\dsh`, honors an existing `DSH_HOME`, and still requires the Microsoft Edge WebView2 Runtime.
