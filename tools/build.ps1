# Builds CleanFolder.exe (Release), the installer dist\CleanFolder-Setup-<version>.exe and its .sha256 file.
# The version is taken from src\CleanFolder\CleanFolder.csproj (<Version>), installer\CleanFolder.iss must match.
# Usage: powershell -ExecutionPolicy Bypass -File tools\build.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $root 'src\CleanFolder\CleanFolder.csproj'
$iss = Join-Path $root 'installer\CleanFolder.iss'

$version = ([xml](Get-Content $csproj -Raw -Encoding UTF8)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$issVersion = [regex]::Match((Get-Content $iss -Raw -Encoding UTF8), '#define AppVersion "([^"]+)"').Groups[1].Value
if ($version -ne $issVersion) { throw "Version mismatch: csproj $version, iss $issVersion" }

if (-not (Test-Path (Join-Path $root 'assets\cleanfolder.ico'))) { & (Join-Path $PSScriptRoot 'make-icon.ps1') }

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) not found' }

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
& $dotnet build $csproj -c Release | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }
& $iscc /Q $iss | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' }

$setup = Join-Path $root "dist\CleanFolder-Setup-$version.exe"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$setup.sha256", "$hash  CleanFolder-Setup-$version.exe`n", [Text.UTF8Encoding]::new($false))
"Built $setup"
"SHA256 $hash"
