#Requires -Version 5.1
<#
.SYNOPSIS
    Unified build script for MSC Community Plugins.
    Reads plugins.json for the plugin list. Builds all plugins in Release,
    creates per-plugin ZIPs, and a combined NSIS installer.
.EXAMPLE
    .\build.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$version = '1.0.0'
$buildDir = Join-Path $root 'build'
$manifestPath = Join-Path $root 'plugins.json'

Write-Host "== MSC Community Plugins v$version ==" -ForegroundColor Cyan

# ── Load plugin manifest ──
$plugins = Get-Content $manifestPath -Raw | ConvertFrom-Json
Write-Host "Loaded $($plugins.Count) plugins from plugins.json"

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

# ── Determine which platforms are needed ──
$platforms = $plugins | ForEach-Object {
    if ($_.platform) { $_.platform } else { 'AnyCPU' }
} | Sort-Object -Unique

$stepNum = 2
foreach ($plat in $platforms) {
    $platDisplay = if ($plat -eq 'AnyCPU') { 'Any CPU' } else { $plat }
    Write-Host "`n[$stepNum/6] Building Release|$platDisplay..." -ForegroundColor Yellow
    & $msbuildPath $slnPath `
        /p:Configuration=Release `
        "/p:Platform=$platDisplay" `
        /p:CIBuild=true `
        /t:Rebuild `
        /v:minimal
    if ($LASTEXITCODE -ne 0) { Write-Error "Build ($platDisplay) failed"; exit 1 }
    $stepNum++
}

# ── Stage files ──
Write-Host "`n[4/6] Staging release files..." -ForegroundColor Yellow
$staging = Join-Path $buildDir 'staging'

foreach ($p in $plugins) {
    $platform = if ($p.platform) { $p.platform } else { 'AnyCPU' }
    $outputPath = if ($p.outputPath) { $p.outputPath }
                  elseif ($platform -eq 'x64') { 'bin/x64/Release/net48' }
                  else { 'bin/Release/net48' }

    $stageDir = Join-Path $staging $p.name
    New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
    $srcPath = Join-Path $root "$($p.path)\$outputPath"
    Copy-Item -Path "$srcPath\*" -Destination $stageDir -Recurse
    Write-Host "  Staged: $($p.name) <- $($p.path)\$outputPath"

    # Extra staging directories
    if ($p.extraStagingDirs) {
        foreach ($dir in $p.extraStagingDirs) {
            $extraSrc = Join-Path $root $dir
            Copy-Item -Path "$extraSrc\*" -Destination $stageDir -Recurse -Force
            Write-Host "    + extra dir: $dir"
        }
    }

    # Extra staging files
    if ($p.extraStagingFiles) {
        foreach ($file in $p.extraStagingFiles) {
            $extraFile = Join-Path $root $file
            Copy-Item -Path $extraFile -Destination $stageDir -Force
            Write-Host "    + extra file: $file"
        }
    }
}

# ── Create ZIPs ──
Write-Host "`n[5/6] Creating release ZIPs..." -ForegroundColor Yellow
foreach ($p in $plugins) {
    $zipPath = Join-Path $buildDir "$($p.name)-v$version.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $staging $p.name) -DestinationPath $zipPath
    Write-Host "  -> $zipPath"
}

# ── Generate NSIS include + Build installer ──
Write-Host "`n[6/6] Building NSIS installer..." -ForegroundColor Yellow

# Generate plugin-generated.nsi from plugins.json
$genScript = Join-Path $root 'installer\generate-nsi.ps1'
& $genScript -ManifestPath $manifestPath

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

    # Build /D args for staging dirs dynamically
    $nsisArgs = @("/DVERSION=$version")
    foreach ($p in $plugins) {
        $dirName = "$($p.name.ToUpper())_DIR"
        $dirPath = (Resolve-Path (Join-Path $staging $p.name)).Path
        $nsisArgs += "/D${dirName}=$dirPath"
    }
    $nsisArgs += "/DOUTDIR=$buildDir"
    $nsisArgs += $nsiScript

    & $makensis @nsisArgs
    if ($LASTEXITCODE -ne 0) { Write-Error "NSIS build failed"; exit 1 }
} else {
    Write-Warning "NSIS not found -- skipping installer build."
    Write-Warning "Install NSIS from https://nsis.sourceforge.io/ and re-run."
}

# ── Done ──
Write-Host "`n== Build complete ==" -ForegroundColor Green
Write-Host "Output: $buildDir"
foreach ($p in $plugins) {
    Write-Host "  ZIP: $($p.name)-v$version.zip"
}
if ($makensis) {
    Write-Host "  Installer: MSCPlugins-v$version-Setup.exe"
}
