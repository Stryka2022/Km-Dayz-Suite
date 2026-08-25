#requires -Version 5
# Build the APH Havoc installer locally. Usage: scripts\pack.ps1 -Version 0.1.0
param(
  [Parameter(Mandatory = $true)][string]$Version
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'artifacts\publish'
$release = Join-Path $root 'artifacts\release'

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publish | Out-Null
# Clean the release dir too: vpk refuses to pack a version equal/older than one already present
# there, so a stale prior run (or an old packId) would abort this pack.
if (Test-Path $release) { Remove-Item $release -Recurse -Force }

# Tray + CLI publish into the root (both are clean net8; identical runtime files collapse to one
# copy). Mcp publishes into a SEPARATE 'mcp' subfolder: ModelContextProtocol / Microsoft.Extensions
# pull .NET 10 BCL assemblies (System.IO.Pipelines / System.Text.Json 10.x) which, if merged into the
# root, poison the net8 Tray at runtime (an assembly-load crash). Isolating Mcp keeps the Tray pure.
$common = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
            '-p:PublishSingleFile=false', "-p:Version=$Version")
$mcpDir = Join-Path $publish 'mcp'
dotnet publish (Join-Path $root 'src\Dzl.Cli')  @common -o $publish
dotnet publish (Join-Path $root 'src\Dzl.Tray') @common -o $publish
dotnet publish (Join-Path $root 'src\Dzl.Mcp')  @common -o $mcpDir

# Friendly apphost names (renaming a published apphost .exe is safe - it locates its managed
# .dll by an embedded name, independent of the .exe filename).
Rename-Item (Join-Path $publish 'Dzl.Cli.exe')  'aph-havoc.exe'         -Force
Rename-Item (Join-Path $publish 'Dzl.Tray.exe') 'aph-havoc-manager.exe' -Force
Rename-Item (Join-Path $mcpDir  'Dzl.Mcp.exe')  'aph-havoc-mcp.exe'     -Force

# Smoke: the CLI runs from the Tray+CLI root (proves that bundle is sound).
& (Join-Path $publish 'aph-havoc.exe') '--help' | Out-Null
if ($LASTEXITCODE -ne 0) { throw "aph-havoc.exe --help failed (exit $LASTEXITCODE)" }
foreach ($exe in @((Join-Path $publish 'aph-havoc.exe'), (Join-Path $publish 'aph-havoc-manager.exe'), (Join-Path $mcpDir 'aph-havoc-mcp.exe'))) {
  if (-not (Test-Path $exe)) { throw "missing $exe in publish output" }
}
# Regression guard: the Tray root must NOT contain a .NET 10 System.IO.Pipelines (the bug this fixes).
$stray = Join-Path $publish 'System.IO.Pipelines.dll'
if ((Test-Path $stray) -and ([version](Get-Item $stray).VersionInfo.FileVersion).Major -ge 9) {
  throw "Tray root contains a .NET 10 System.IO.Pipelines - Mcp isolation failed"
}

# Pack into a single per-user Setup.exe + the update feed.
# The APH packId keeps the installed program separate from the legacy compatibility config folder.
# Velopack's uninstall removes the install directory, so configuration must remain elsewhere.

# Bundle the DzlDevTools mod (editable source + prebuilt, unsigned/unbinarized PBO). The app deploys
# it into the user's workspace on demand (My Mods -> Dev tools); DevToolsAssets.BundleDir looks for
# it under <install>\assets\DzlDevTools.
$devTools = Join-Path $root 'assets\DzlDevTools'
if (-not (Test-Path (Join-Path $devTools 'build\DzlDevTools.pbo'))) {
  throw "assets\DzlDevTools\build\DzlDevTools.pbo missing - build it (aph-havoc build DzlDevTools --no-binarize) and copy it into assets first"
}
$dst = Join-Path $publish 'assets\DzlDevTools'
New-Item -ItemType Directory -Force -Path $dst | Out-Null
Copy-Item (Join-Path $devTools '*') $dst -Recurse -Force

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) { dotnet tool install -g vpk --version 1.2.0 }
vpk pack --packId AphHavoc --packVersion $Version --packDir $publish --mainExe aph-havoc-manager.exe `
         --packTitle 'APH Havoc Server Manager' --packAuthors 'APH Havoc Survival' -o $release
# $ErrorActionPreference='Stop' does NOT catch native-exe failures, so check vpk's exit explicitly.
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed (exit $LASTEXITCODE)" }
Write-Host "Built APH Havoc release in: $release"
