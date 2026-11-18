# Publishes a GitHub release: builds the installer and uploads it with its .sha256.
# Installed copies of the program find the release and update themselves.
# Usage: powershell -ExecutionPolicy Bypass -File tools\release.ps1 [-Notes "что нового"]
param([string]$Notes = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1')
$csproj = Join-Path $root 'src\CleanFolder\CleanFolder.csproj'
$version = ([xml](Get-Content $csproj -Raw -Encoding UTF8)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$setup = Join-Path $root "dist\CleanFolder-Setup-$version.exe"
if (-not $Notes) { $Notes = "Чистая папка $version" }
gh release create "v$version" $setup "$setup.sha256" --repo worldruschel/clean-folder --title "Чистая папка $version" --notes $Notes
if ($LASTEXITCODE -ne 0) { throw 'gh release create failed' }
"Released v$version"
