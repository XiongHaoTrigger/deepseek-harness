# Agent Note: Windows desktop web host

Status: implemented

English | [中文](2026-08-17-windows-desktop-web-host.zh.md)

## Problem

Windows users need a desktop entry point for the existing Web UI without creating a second client runtime or distributing Node.js and its package tree inside an executable.

## Decision

`avalonia/DeepSeekHarness.Desktop` ships a Windows x64 Avalonia application named `DeepSeekHarness.exe`. It starts `pnpm dsh web --port 0` from the repository root through hidden `cmd.exe` and `pnpm.cmd`, parses the loopback URL from stdout, and assigns it to `NativeWebView`. The WebView uses the Avalonia Windows WebView2 backend. Startup failure shows captured stderr and permits retry.

Avalonia design mode renders a static startup panel and does not attach `NativeWebView`, start pnpm, or create a WebView2 child window. The design host cannot reliably create a WebView2 HWND.

The launcher assigns its command process to a Windows Job Object with `KILL_ON_JOB_CLOSE`. Retrying and closing the main window dispose the Job Object and await the root process, so the pnpm, Corepack, Node, and Web server descendants stop even after a command wrapper exits early.

The project targets `net10.0` and `win-x64`. Debug and design-time builds disable AOT and trimming for the Avalonia designer; Release publishing is self-contained Native AOT with trimming. Its Windows compatibility manifest enables the native child window used by `NativeWebView`. The executable continues to depend on a local repository, Node.js, pnpm, installed workspace dependencies, and the WebView2 Runtime.

## Alternatives considered

- Reimplement the React client in Avalonia — rejected; the desktop shell hosts the existing Web UI and leaves its behavior owned by the Web application.
- Use an embedded Chromium engine — rejected; the official `NativeWebView` uses the installed WebView2 Runtime and keeps the executable smaller.
- Rely only on `Process.Kill(entireProcessTree: true)` — rejected; pnpm's command wrappers can exit before shutdown, leaving a detached Node descendant. The Job Object owns the whole launched tree.
- Disable AOT for WebView support — rejected; Avalonia's official WebView package supports trimming and AOT, and the Native AOT publish succeeds.

## Consequences

The desktop executable is Windows x64 only and is launched from a checkout of this repository. It loads the same loopback Web UI as `pnpm dsh web`; it does not package the Web client, Node.js, pnpm, or an installer. Unit tests cover URL parsing, missing-URL failure text, and retry cleanup, while the native run verifies WebView startup and process-tree shutdown.
