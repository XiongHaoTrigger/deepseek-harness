#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Build the self-contained Windows x64 dsh Web runtime under dist/runtime/.

.DESCRIPTION
  Produces a "Node-attached runtime directory" that boots the existing dsh Web
  UI without a system Node, pnpm, the repository root node_modules, or any
  TypeScript source loader. The runtime uses the built apps/cli lib/ output and
  the built apps/web frontend dist, both materialized into the deployed closure.

  Pipeline:
    1. (optional) run `pnpm run build` so apps/cli/lib and apps/web/dist exist.
  2. acquire a SHA-256-verified portable Windows x64 Node.exe (download from
     nodejs.org, or extract a local zip given with -NodeZipPath) and check it
     against the repository Node engines range.
    3. `pnpm --filter @deepseek-ai/dsh deploy` the app closure into dist/runtime
       (prod dependencies only, hoisted, symlink-free after repair).
    4. repair the closure (copy any dependency the deploy dropped from the
       checkout, materialize any remaining link) and verify it.
    5. copy node.exe and dsh-web-entry.js into dist/runtime.

  Release publishing copies this directory beside DeepSeekHarness.exe. Debug
  builds continue to launch the Web UI through `pnpm.cmd dsh web`.

.PARAMETER NodeVersion
  The Node version to download, e.g. v24.19.0. Defaults to the latest pinned
  Windows x64 build that satisfies the repository engines range.

.PARAMETER NodeZipPath
  A local node-v<ver>-win-x64.zip to extract node.exe from, instead of
  downloading. The archive must match the NodeVersion parameter and its pinned
  SHA-256 (or the value passed through -NodeSha256).

.PARAMETER NodeSha256
  The SHA-256 of the Windows x64 Node ZIP. The default NodeVersion has a pinned
  hash. Supply this value when selecting another NodeVersion.

.PARAMETER SkipBuild
  Skip `pnpm run build`; the built apps/cli/lib and apps/web/dist must already
  exist.

.PARAMETER SkipDeploy
  Reuse the existing dist/runtime node_modules and package files instead of
  re-running the pnpm deploy.

.PARAMETER SmokeTest
  After packaging, launch the runtime with `web --port 0`, wait for the
  `dsh web: http://127.0.0.1:<port>` line, request the URL, and assert an HTTP
  200 HTML response, then terminate the process tree.
#>
[CmdletBinding()]
param(
  [string]$NodeVersion = 'v24.19.0',
  [string]$NodeZipPath = '',
  [string]$NodeSha256 = '',
  [switch]$SkipBuild,
  [switch]$SkipDeploy,
  [switch]$SmokeTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$here = $PSScriptRoot
$repoRoot = Split-Path (Split-Path $here -Parent) -Parent
$dist = Join-Path $here 'dist'
$runtimeDir = Join-Path $dist 'runtime'
$cacheDir = Join-Path $here '.cache'
$entryFile = Join-Path $here 'dsh-web-entry.js'
$closureHelper = Join-Path $here 'runtime-closure.mjs'
$appManifest = Join-Path $repoRoot 'apps\cli\package.json'
$rootManifest = Join-Path $repoRoot 'package.json'
$pinnedNodeZipSha256 = @{
  'v24.19.0' = '57f71ab3652e797d84acddc79c81cc9ff1c6ddb2a1974cdb83f00fee9bff4c73'
}

function Write-Step {
  param([string]$Message)
  Write-Host "build-runtime: $Message" -ForegroundColor Cyan
}

function Invoke-Checked {
  param(
    [string]$Label,
    [string]$Command,
    [string[]]$Arguments,
    [string]$WorkingDirectory
  )
  $pretty = (@($Command) + $Arguments) -join ' '
  Write-Step "${Label}: $pretty"
  $previous = Get-Location
  try {
    if ($WorkingDirectory) { Set-Location $WorkingDirectory }
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
      throw "build-runtime: $Label failed (exit $LASTEXITCODE): $pretty"
    }
  } finally {
    Set-Location $previous
  }
}

function Assert-SatisfiesNodeRange {
  param([string]$Version, [string]$Range)
  if ($Version -notmatch '^v?(\d+)\.(\d+)\.(\d+)') {
    throw "build-runtime: cannot parse Node version $Version"
  }
  $vmajor = [int]$Matches[1]
  $vminor = [int]$Matches[2]
  $vpatch = [int]$Matches[3]
  foreach ($part in ($Range -split '\s*\|\|\s*')) {
    $part = $part.Trim()
    if ($part -match '^\^(\d+)\.(\d+)\.(\d+)$') {
      $pmajor = [int]$Matches[1]; $pminor = [int]$Matches[2]; $ppatch = [int]$Matches[3]
      if ($vmajor -eq $pmajor -and ($vminor -gt $pminor -or ($vminor -eq $pminor -and $vpatch -ge $ppatch))) { return $true }
    } elseif ($part -match '^>=(\d+)\.(\d+)\.(\d+)$') {
      $pmajor = [int]$Matches[1]; $pminor = [int]$Matches[2]; $ppatch = [int]$Matches[3]
      if ($vmajor -gt $pmajor -or ($vmajor -eq $pmajor -and ($vminor -gt $pminor -or ($vminor -eq $pminor -and $vpatch -ge $ppatch)))) { return $true }
    } elseif ($part -match '^(\d+)\.(\d+)\.(\d+)$') {
      if ($vmajor -eq [int]$Matches[1] -and $vminor -eq [int]$Matches[2] -and $vpatch -eq [int]$Matches[3]) { return $true }
    } else {
      throw "build-runtime: unsupported engines range fragment $part"
    }
  }
  return $false
}

function Get-ExpectedNodeZipSha256 {
  if ($NodeSha256 -ne '') {
    if ($NodeSha256 -notmatch '^[0-9a-fA-F]{64}$') {
      throw 'build-runtime: -NodeSha256 must be a 64-character SHA-256 value'
    }
    return $NodeSha256.ToLowerInvariant()
  }
  if ($pinnedNodeZipSha256.ContainsKey($NodeVersion)) {
    return $pinnedNodeZipSha256[$NodeVersion]
  }
  throw "build-runtime: NodeVersion $NodeVersion has no pinned SHA-256; supply -NodeSha256 from Node's official SHASUMS256.txt"
}

function Get-NodeExe {
  New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
  if ($NodeZipPath -ne '') {
    $zip = Resolve-Path $NodeZipPath
    Write-Step "using local Node zip $zip"
  } else {
    $zip = Join-Path $cacheDir "node-$NodeVersion-win-x64.zip"
    if (-not (Test-Path $zip)) {
      $url = "https://nodejs.org/dist/$NodeVersion/node-$NodeVersion-win-x64.zip"
      Write-Step "downloading $url"
      Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    }
  }
  $expectedHash = Get-ExpectedNodeZipSha256
  $actualHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualHash -ne $expectedHash) {
    throw "build-runtime: SHA-256 mismatch for $zip; expected $expectedHash, got $actualHash"
  }
  $extract = Join-Path $cacheDir "node-$NodeVersion-win-x64"
  if (Test-Path $extract) { Remove-Item -LiteralPath $extract -Recurse -Force }
  Write-Step "extracting verified $zip"
  Expand-Archive -Path $zip -DestinationPath $cacheDir -Force
  $nodeExe = Join-Path $extract 'node.exe'
  if (-not (Test-Path $nodeExe)) {
    throw "build-runtime: $zip does not contain node.exe at its root"
  }
  return $nodeExe
}

function Invoke-SmokeTest {
  param([string]$NodeExe)
  $temp = Join-Path $env:TEMP ("dsh-runtime-smoke-" + [guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Path $temp -Force | Out-Null
  $stdout = Join-Path $temp 'stdout.log'
  $stderr = Join-Path $temp 'stderr.log'
  $dshHome = Join-Path $temp 'home'
  $previousHome = $env:DSH_HOME
  $previousTelemetry = $env:DSH_TELEMETRY_DISABLED
  $env:DSH_HOME = $dshHome
  $env:DSH_TELEMETRY_DISABLED = '1'
  $process = $null
  try {
    Write-Step 'smoke: launching runtime web --port 0'
    $process = Start-Process -FilePath $NodeExe -ArgumentList 'dsh-web-entry.js', 'web', '--port', '0' `
      -WorkingDirectory $runtimeDir -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -WindowStyle Hidden
    $url = $null
    $deadline = (Get-Date).AddSeconds(120)
    while ((Get-Date) -lt $deadline) {
      Start-Sleep -Milliseconds 500
      if ($process.HasExited) { break }
      $content = Get-Content $stdout -Raw -ErrorAction SilentlyContinue
      if ($content -match 'dsh web: (http://127\.0\.0\.1:\d+)') {
        $url = $Matches[1]
        break
      }
    }
    if ($url -eq $null) {
      Write-Host 'build-runtime: smoke stdout:'
      Get-Content $stdout -ErrorAction SilentlyContinue
      Write-Host 'build-runtime: smoke stderr:'
      Get-Content $stderr -ErrorAction SilentlyContinue
      throw 'build-runtime: smoke test timed out waiting for the dsh web URL line'
    }
    Write-Step "smoke: $url"
    $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 30
    if ($response.StatusCode -ne 200) {
      throw "build-runtime: smoke test got HTTP $($response.StatusCode)"
    }
    if ($response.Content -notmatch '<!doctype html|<html') {
      throw 'build-runtime: smoke test response is not the Web UI HTML'
    }
    Write-Step "smoke: HTTP $($response.StatusCode), HTML bytes $($response.Content.Length)"
  } finally {
    if ($process -ne $null -and -not $process.HasExited) {
      & taskkill.exe /PID $process.Id /T /F | Out-Null
    }
    $env:DSH_HOME = $previousHome
    $env:DSH_TELEMETRY_DISABLED = $previousTelemetry
    Start-Sleep -Milliseconds 500
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
  }
}

Write-Step "repo root: $repoRoot"

# ── preflight ───────────────────────────────────────────────────────────────
foreach ($tool in @('node', 'pnpm')) {
  if ($null -eq (Get-Command $tool -ErrorAction SilentlyContinue)) {
    throw "build-runtime: $tool is required on PATH to build the runtime"
  }
}
if (-not (Test-Path $appManifest)) {
  throw "build-runtime: app manifest $appManifest not found; is the repo root layout intact?"
}
$engines = (Get-Content $rootManifest -Raw | ConvertFrom-Json).engines.node
if (-not $engines) { throw "build-runtime: no engines.node in $rootManifest" }

# ── build (optional) ────────────────────────────────────────────────────────
if ($SkipBuild) {
  foreach ($artifact in @(Join-Path $repoRoot 'apps\cli\lib\bin.js'), (Join-Path $repoRoot 'apps\web\dist\index.html')) {
    if (-not (Test-Path $artifact)) {
      throw "build-runtime: $artifact missing; run without -SkipBuild or run pnpm run build first"
    }
  }
  Write-Step 'skipping pnpm run build (-SkipBuild)'
} else {
  Invoke-Checked -Label 'build' -Command 'pnpm' -Arguments @('run', 'build') -WorkingDirectory $repoRoot
}

# ── node.exe ────────────────────────────────────────────────────────────────
$nodeExe = Get-NodeExe
$nodeVersionOutput = (& $nodeExe --version).Trim()
if ($nodeVersionOutput -ne $NodeVersion) {
  throw "build-runtime: extracted $nodeVersionOutput, expected $NodeVersion"
}
if (-not (Assert-SatisfiesNodeRange -Version $nodeVersionOutput -Range $engines)) {
  throw "build-runtime: $nodeVersionOutput does not satisfy the repository engines range $engines"
}
Write-Step "node $nodeVersionOutput satisfies engines $engines"

# ── deploy ──────────────────────────────────────────────────────────────────
if ($SkipDeploy) {
  if (-not (Test-Path (Join-Path $runtimeDir 'package.json'))) {
    throw 'build-runtime: -SkipDeploy requires an existing dist/runtime/package.json'
  }
  Write-Step 'skipping pnpm deploy (-SkipDeploy)'
} else {
  if (Test-Path $dist) { Remove-Item -LiteralPath $dist -Recurse -Force }
  New-Item -ItemType Directory -Path $dist -Force | Out-Null
  Invoke-Checked -Label 'deploy' -Command 'pnpm' -Arguments @(
    '--filter', '@deepseek-ai/dsh',
    'deploy', '--legacy', '--prod',
    '--config.node-linker=hoisted',
    '--config.auto-install-peers=false',
    '--config.link-workspace-packages=true',
    $runtimeDir
  ) -WorkingDirectory $repoRoot
}

# ── post-process: strip the deploy's documentation, then repair + verify ────
foreach ($name in @('README.md', 'README.zh.md', 'README.i18n.yaml')) {
  Remove-Item -LiteralPath (Join-Path $runtimeDir $name) -Force -ErrorAction SilentlyContinue
}
$pnpmDir = Join-Path $runtimeDir 'node_modules\.pnpm'
if ((Test-Path $pnpmDir) -and -not (Get-ChildItem $pnpmDir -Force -ErrorAction SilentlyContinue)) {
  Remove-Item -LiteralPath $pnpmDir -Recurse -Force
}

Invoke-Checked -Label 'closure repair' -Command 'node' -Arguments @($closureHelper, 'repair', $runtimeDir, $appManifest)
Invoke-Checked -Label 'closure verify' -Command 'node' -Arguments @($closureHelper, 'verify', $runtimeDir)

# ── stage node.exe and the entry ────────────────────────────────────────────
Copy-Item -LiteralPath $nodeExe -Destination (Join-Path $runtimeDir 'node.exe') -Force
Copy-Item -LiteralPath $entryFile -Destination (Join-Path $runtimeDir 'dsh-web-entry.js') -Force

# ── report ──────────────────────────────────────────────────────────────────
$size = (Get-ChildItem -LiteralPath $runtimeDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
$sizeMb = [math]::Round($size / 1MB, 1)
Write-Step "runtime ready: $runtimeDir ($sizeMb MB)"
Write-Step "start: .\dist\runtime\node.exe .\dist\runtime\dsh-web-entry.js web --port 0"
Write-Step 'Release publishing copies this runtime beside DeepSeekHarness.exe'

if ($SmokeTest) {
  Invoke-SmokeTest -NodeExe (Join-Path $runtimeDir 'node.exe')
}
