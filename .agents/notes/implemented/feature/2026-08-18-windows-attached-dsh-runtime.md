# Agent Note: Windows attached dsh runtime

Status: implemented

English | [中文](2026-08-18-windows-attached-dsh-runtime.zh.md)

## Problem

A published Windows desktop application cannot require a source checkout, pnpm, or a locally installed Node.js runtime to serve the existing Web UI.

## Decision

`avalonia/runtime-packaging/build-runtime.ps1` builds a Windows x64 runtime directory from the built CLI, frontend assets, deployed production dependency closure, `dsh-web-entry.js`, and a pinned, SHA-256-verified Node ZIP. The script materializes links, verifies dependency resolution, and can launch the resulting server as a smoke test.

Release publishing validates the runtime directory and copies it to `runtime/` beside `DeepSeekHarness.exe`. The desktop host starts `runtime/node.exe` with the packaged entry and defaults dsh profiles and session data to `%LOCALAPPDATA%\DeepSeekHarness\dsh` unless `DSH_HOME` already exists. Debug builds retain the repository pnpm launch path.

## Alternatives considered

- Start `pnpm dsh web` from the published application — rejected; a published application would still depend on the development toolchain and checkout.
- Bundle Node.js inside the Native AOT executable — rejected; Node and the deployed dependency closure remain regular files beside the executable, avoiding a custom executable packer.
- Replace the Web UI with the Python SDK runtime — rejected; that runtime exposes JSON-RPC and would require a second desktop client implementation.

## Consequences

The Release payload includes Node.js and the deployed JavaScript dependency closure, increasing its size. Runtime Node upgrades require changing the pinned version and SHA-256 together. The package remains Windows x64 only, requires WebView2 Runtime, and does not include pnpm, an installer, or automatic updates.
