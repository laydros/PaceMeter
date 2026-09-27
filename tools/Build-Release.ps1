# Runs tests, publishes the single-file exe, and builds the installer.
# Outputs:
#   src/PaceMeter/bin/Release/net9.0-windows/win-x64/publish/PaceMeter.exe
#   artifacts/PaceMeter-Setup-<version>.exe
# Usage: pwsh -NoProfile -File tools/Build-Release.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

dotnet test -c Release
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

dotnet publish src/PaceMeter -c Release -r win-x64
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$iscc = @(
    (Get-Command iscc -ErrorAction SilentlyContinue).Source,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup" }

& $iscc /Q installer\PaceMeter.iss
if ($LASTEXITCODE -ne 0) { throw "Installer build failed." }

Get-ChildItem artifacts\PaceMeter-Setup-*.exe | Sort-Object LastWriteTime | Select-Object -Last 1 | ForEach-Object {
    Write-Host "Built $($_.FullName)"
}
