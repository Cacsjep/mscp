#Requires -Version 5.1
<#
.SYNOPSIS
    Unified build script for MSC Community Plugins.
    Builds all plugins/drivers in Release, creates per-plugin ZIPs, and a combined NSIS installer.
.EXAMPLE
    .\build.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$version = '1.0.0'
$buildDir = Join-Path $root 'build'

Write-Host "== MSC Community Plugins v$version ==" -ForegroundColor Cyan

# ── Locate MSBuild ──
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuildPath = $null
if (Test-Path $vswhere) {
    $msbuildPath = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
}
if (-not $msbuildPath -or -not (Test-Path $msbuildPath)) {
    $msbuildPath = (Get-Command msbuild -ErrorAction SilentlyContinue).Source
}
if (-not $msbuildPath) {
    Write-Error "MSBuild not found. Install Visual Studio or add MSBuild to PATH."
    exit 1
}
Write-Host "MSBuild: $msbuildPath"

# ── Locate NuGet ──
$nugetPath = $null
$nugetCmd = Get-Command nuget -ErrorAction SilentlyContinue
if ($nugetCmd) {
    $nugetPath = $nugetCmd.Source
} else {
    $vsNuget = Join-Path (Split-Path $msbuildPath -Parent) 'nuget.exe'
    if (Test-Path $vsNuget) { $nugetPath = $vsNuget }
}
if (-not $nugetPath) {
    Write-Error "nuget.exe not found in PATH. Download from https://www.nuget.org/downloads"
    exit 1
}
Write-Host "NuGet:   $nugetPath"

$slnPath = Join-Path $root 'MSCPlugins.sln'

# ── Clean build directory ──
if (Test-Path $buildDir) { Remove-Item $buildDir -Recurse -Force }
New-Item -ItemType Directory -Path $buildDir -Force | Out-Null

# ── Restore NuGet packages ──
Write-Host "`n[1/6] Restoring NuGet packages..." -ForegroundColor Yellow
& $nugetPath restore $slnPath
if ($LASTEXITCODE -ne 0) { Write-Error "NuGet restore failed"; exit 1 }

# ── Build Release|Any CPU (Smart Client plugins + RTMPDriver) ──
Write-Host "`n[2/6] Building Release|Any CPU..." -ForegroundColor Yellow
& $msbuildPath $slnPath `
    /p:Configuration=Release `
    '/p:Platform=Any CPU' `
    /p:CIBuild=true `
    /p:PreBuildEvent= `
    /p:PostBuildEvent= `
    /t:Rebuild `
    /v:minimal
if ($LASTEXITCODE -ne 0) { Write-Error "Build (Any CPU) failed"; exit 1 }

# ── Build Release|x64 (RTMPStreamer + RTMPStreamerHelper) ──
Write-Host "`n[3/6] Building Release|x64..." -ForegroundColor Yellow
& $msbuildPath $slnPath `
    /p:Configuration=Release `
    /p:Platform=x64 `
    /p:CIBuild=true `
    /p:PreBuildEvent= `
    /p:PostBuildEvent= `
    /t:Rebuild `
    /v:minimal
if ($LASTEXITCODE -ne 0) { Write-Error "Build (x64) failed"; exit 1 }

# ── Stage files ──
Write-Host "`n[4/6] Staging release files..." -ForegroundColor Yellow
$staging = Join-Path $buildDir 'staging'

# Weather
$stageWeather = Join-Path $staging 'Weather'
New-Item -ItemType Directory -Path $stageWeather -Force | Out-Null
Copy-Item -Path (Join-Path $root 'Smart Client Plugins\Weather\bin\Release\net48\*') -Destination $stageWeather -Recurse

# RDP
$stageRdp = Join-Path $staging 'RDP'
New-Item -ItemType Directory -Path $stageRdp -Force | Out-Null
Copy-Item -Path (Join-Path $root 'Smart Client Plugins\RDP\bin\Release\net48\*') -Destination $stageRdp -Recurse

# RTMPDriver
$stageDriver = Join-Path $staging 'RTMPDriver'
New-Item -ItemType Directory -Path $stageDriver -Force | Out-Null
Copy-Item -Path (Join-Path $root 'Device Drivers\Rtmp\RTMPDriver\bin\Release\*') -Destination $stageDriver -Recurse

# RTMPStreamer
$stageStreamer = Join-Path $staging 'RTMPStreamer'
New-Item -ItemType Directory -Path $stageStreamer -Force | Out-Null
Copy-Item -Path (Join-Path $root 'Admin Plugins\RTMPStreamer\bin\x64\Release\*') -Destination $stageStreamer -Recurse
Copy-Item -Path (Join-Path $root 'Admin Plugins\RTMPStreamer\RTMPStreamerHelper\bin\x64\Release\*') -Destination $stageStreamer -Recurse -Force
Copy-Item -Path (Join-Path $root 'Admin Plugins\RTMPStreamer\plugin.def') -Destination $stageStreamer -Force

# ── Create ZIPs ──
Write-Host "`n[5/6] Creating release ZIPs..." -ForegroundColor Yellow
$artifacts = @('Weather', 'RDP', 'RTMPDriver', 'RTMPStreamer')
foreach ($name in $artifacts) {
    $zipPath = Join-Path $buildDir "$name-v$version.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $staging $name) -DestinationPath $zipPath
    Write-Host "  -> $zipPath"
}

# ── Build NSIS installer ──
Write-Host "`n[6/6] Building NSIS installer..." -ForegroundColor Yellow
$makensis = $null
$nsisLocations = @(
    "${env:ProgramFiles(x86)}\NSIS\makensis.exe",
    "$env:ProgramFiles\NSIS\makensis.exe"
)
foreach ($loc in $nsisLocations) {
    if (Test-Path $loc) { $makensis = $loc; break }
}
if (-not $makensis) {
    $nsisCmd = Get-Command makensis -ErrorAction SilentlyContinue
    if ($nsisCmd) { $makensis = $nsisCmd.Source }
}

if ($makensis) {
    $nsiScript = Join-Path $root 'installer\MSCPlugins.nsi'
    $weatherDir = (Resolve-Path (Join-Path $staging 'Weather')).Path
    $rdpDir     = (Resolve-Path (Join-Path $staging 'RDP')).Path
    $driverDir  = (Resolve-Path (Join-Path $staging 'RTMPDriver')).Path
    $streamerDir = (Resolve-Path (Join-Path $staging 'RTMPStreamer')).Path

    & $makensis /DVERSION=$version `
        "/DWEATHER_DIR=$weatherDir" `
        "/DRDP_DIR=$rdpDir" `
        "/DRTMPDRIVER_DIR=$driverDir" `
        "/DRTMPSTREAMER_DIR=$streamerDir" `
        "/DOUTDIR=$buildDir" `
        $nsiScript
    if ($LASTEXITCODE -ne 0) { Write-Error "NSIS build failed"; exit 1 }
} else {
    Write-Warning "NSIS not found -- skipping installer build."
    Write-Warning "Install NSIS from https://nsis.sourceforge.io/ and re-run."
}

# ── Done ──
Write-Host "`n== Build complete ==" -ForegroundColor Green
Write-Host "Output: $buildDir"
foreach ($name in $artifacts) {
    Write-Host "  ZIP: $name-v$version.zip"
}
if ($makensis) {
    Write-Host "  Installer: MSCPlugins-v$version-Setup.exe"
}
