# Builds Serial Debugger and its installer.
# Output: dist\SerialDebugger-Setup.exe
# Usage:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# Building the installer builds the app first (project reference) and embeds it.
dotnet build "$root\src\Installer\Installer.csproj" -c Release -nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item "$root\src\Installer\bin\Release\net48\SerialDebugger-Setup.exe" $dist -Force

$setup = Get-Item (Join-Path $dist 'SerialDebugger-Setup.exe')
"Installer ready: $($setup.FullName) ($([Math]::Round($setup.Length / 1KB)) KB)"
