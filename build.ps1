# Builds the installer: dist\DeployCraft-Setup-<version>.exe
#   .\build.ps1            (version 0.1.0)
#   .\build.ps1 -Version 0.2.0
param([string]$Version = "0.2.0")

$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
Set-Location $PSScriptRoot

Remove-Item publish -Recurse -Force -ErrorAction SilentlyContinue

# Runner (.NET Framework 4.8) then self-contained server (embedded .NET: nothing to install on the VM).
dotnet build src\DeployCraft.Runner -c Release -v q
if ($LASTEXITCODE) { throw "Runner build failed" }

dotnet publish src\DeployCraft.Server -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:PublishDir="$PSScriptRoot\publish\" -v q
if ($LASTEXITCODE) { throw "Server publish failed" }

# Windows console, published in the same folder (the .NET runtime files are shared).
dotnet publish src\DeployCraft.Console -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:PublishDir="$PSScriptRoot\publish\" -v q
if ($LASTEXITCODE) { throw "Console publish failed" }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found (winget install JRSoftware.InnoSetup)" }

& $iscc /Q "/DAppVersion=$Version" installer\DeployCraft.iss
if ($LASTEXITCODE) { throw "Installer creation failed" }

Get-Item "dist\DeployCraft-Setup-$Version.exe" | Select-Object FullName, @{n = "Mo"; e = { [math]::Round($_.Length / 1MB, 1) } }
