#Requires -Version 5.1
<#
.SYNOPSIS
    Unified build script for MSC Community Plugins.
    Reads plugins.json for the plugin list. Builds all plugins in Release,
    creates per-plugin ZIPs and a WiX MSI installer.

.PARAMETER Fast
    Skip the per-plugin ZIPs and the WiX MSI. Useful when iterating on the
    plugin or agent code; just produces the binaries under each project's
    bin/Release/ folder. Stages files into build/staging/ so generated
    WiX fragments still pick them up next full build.

.PARAMETER OnlyPki
    Build only PKI-related projects (Pki.Messages, Pki.Agent, Pki.Tray,
    PKI plugin) via `dotnet build`. Skips the full solution, ZIPs, and
    MSI. Fastest path when you're only changing agent or plugin code.
    Implies -Fast.

.PARAMETER MaxCpuCount
    msbuild /m value. Default 0 = use all logical cores. Set to 1 for
    serial builds (e.g. when diagnosing parallel-build flakiness).

.EXAMPLE
    .\build.ps1                # full build, MSI included
    .\build.ps1 -Fast          # skip ZIPs + MSI
    .\build.ps1 -OnlyPki       # PKI-only, fastest dev loop
#>

param(
    [switch]$Fast,
    [switch]$OnlyPki,
    [int]$MaxCpuCount = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# OnlyPki implies Fast.
if ($OnlyPki) { $Fast = $true }

$root = $PSScriptRoot
# Auto-increment build number so MSI always upgrades without uninstall.
# MSI limits: major<256, minor<256, build<65536. We use 1.<minor>.<build>
# where minor = months since 2025-01 and build = day*1000 + time-of-day (0-1439).
$epoch = [DateTime]::new(2025,1,1)
$now = [DateTime]::UtcNow
$minor = (($now.Year - $epoch.Year) * 12) + ($now.Month - $epoch.Month)
$build = ($now.Day * 1000) + ($now.Hour * 60 + $now.Minute)
$version = "1.$minor.$build"
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
    if ($_ | Get-Member -Name platform -MemberType NoteProperty) { $_.platform } else { 'AnyCPU' }
} | Sort-Object -Unique

$mFlag = if ($MaxCpuCount -gt 0) { "/m:$MaxCpuCount" } else { '/m' }

if ($OnlyPki) {
    # PKI-only fast path: dotnet build the four PKI projects in dependency
    # order. Skips MSBuild on the rest of the sln for big wins on dev loops.
    $stepNum = 2
    Write-Host "`n[$stepNum/3] Building PKI suite (Pki.Messages -> Pki.Agent / Pki.Tray / PKI)..." -ForegroundColor Yellow
    $pkiProjects = @(
        'Agent\Pki.Messages\Pki.Messages.csproj',
        'Agent\Pki.Agent\Pki.Agent.csproj',
        'Agent\Pki.Tray\Pki.Tray.csproj',
        'Admin Plugins\PKI\PKI.csproj'
    )
    foreach ($p in $pkiProjects) {
        $proj = Join-Path $root $p
        & dotnet build $proj -c Release -v minimal $mFlag
        if ($LASTEXITCODE -ne 0) { Write-Error "Build of $p failed"; exit 1 }
    }
    $stepNum++
} else {
    $stepNum = 2
    foreach ($plat in $platforms) {
        $platDisplay = if ($plat -eq 'AnyCPU') { 'Any CPU' } else { $plat }
        Write-Host "`n[$stepNum/6] Building Release|$platDisplay (parallel: $mFlag)..." -ForegroundColor Yellow
        & $msbuildPath $slnPath `
            /p:Configuration=Release `
            "/p:Platform=$platDisplay" `
            /p:CIBuild=true `
            /t:Build `
            /v:minimal `
            $mFlag
        if ($LASTEXITCODE -ne 0) { Write-Error "Build ($platDisplay) failed"; exit 1 }
        $stepNum++
    }
}

# ── Stage files ──
$staging = Join-Path $buildDir 'staging'

if ($OnlyPki) {
    Write-Host "`n[4/6] Skipping staging (-OnlyPki)." -ForegroundColor DarkGray
} else {
    Write-Host "`n[4/6] Staging release files..." -ForegroundColor Yellow

foreach ($p in $plugins) {
    $hasPlatform = $p | Get-Member -Name platform -MemberType NoteProperty
    $hasOutputPath = $p | Get-Member -Name outputPath -MemberType NoteProperty
    $platform = if ($hasPlatform) { $p.platform } else { 'AnyCPU' }
    $outputPath = if ($hasOutputPath) { $p.outputPath }
                  elseif ($platform -eq 'x64') { 'bin/x64/Release/net48' }
                  else { 'bin/Release/net48' }

    $stageDir = Join-Path $staging $p.name
    New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
    $srcPath = Join-Path $root "$($p.path)\$outputPath"
    Copy-Item -Path "$srcPath\*" -Destination $stageDir -Recurse
    Write-Host "  Staged: $($p.name) <- $($p.path)\$outputPath"

    # Extra staging directories
    if (($p | Get-Member -Name extraStagingDirs -MemberType NoteProperty) -and $p.extraStagingDirs) {
        foreach ($dir in $p.extraStagingDirs) {
            $extraSrc = Join-Path $root $dir
            Copy-Item -Path "$extraSrc\*" -Destination $stageDir -Recurse -Force
            Write-Host "    + extra dir: $dir"
        }
    }

    # Extra staging files
    if (($p | Get-Member -Name extraStagingFiles -MemberType NoteProperty) -and $p.extraStagingFiles) {
        foreach ($file in $p.extraStagingFiles) {
            $extraFile = Join-Path $root $file
            Copy-Item -Path $extraFile -Destination $stageDir -Force
            Write-Host "    + extra file: $file"
        }
    }
}
}  # end if (-not $OnlyPki)

# ── Create ZIPs ──
if (-not $Fast) {
    Write-Host "`n[5/6] Creating release ZIPs..." -ForegroundColor Yellow
    foreach ($p in $plugins) {
        $zipPath = Join-Path $buildDir "$($p.name)-v$version.zip"
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
        Compress-Archive -Path (Join-Path $staging $p.name) -DestinationPath $zipPath
        Write-Host "  -> $zipPath"
    }
} else {
    Write-Host "`n[5/6] Skipping per-plugin ZIPs (-Fast)." -ForegroundColor DarkGray
}

# ── Build WiX MSI installer ──
if ($Fast) {
    Write-Host "`n[6/6] Skipping MSI build (-Fast)." -ForegroundColor DarkGray
    Write-Host "`n== Build complete (fast) ==" -ForegroundColor Green
    Write-Host "Output: $buildDir\staging\<plugin>"
    return
}

Write-Host "`n[6/6] Building WiX MSI installer..." -ForegroundColor Yellow

$wixCmd = Get-Command wix -ErrorAction SilentlyContinue
if ($wixCmd) {
    # Generate WiX components from plugins.json. The new generator emits one
    # <Component> per plugin (a single ZIP File) plus the per-plugin extract /
    # cleanup CAs and Features — it doesn't need the staging tree any more.
    $genWixScript = Join-Path $root 'installer\generate-wix.ps1'
    & $genWixScript -ManifestPath $manifestPath

    $wixDir = Join-Path $root 'installer\wix'
    $productWxs = Join-Path $wixDir 'Product.wxs'
    $componentsWxs = Join-Path $wixDir 'Components.wxs'
    $iisHostingWxs = Join-Path $wixDir 'IisHosting.wxs'
    $msiPath = Join-Path $buildDir "MSCPlugins-v$version.msi"
    $customActionProj = Join-Path $root 'installer\customactions\InstallerCustomActions.csproj'

    Write-Host "Building installer custom actions..." -ForegroundColor DarkYellow
    & $msbuildPath $customActionProj `
        /restore `
        /p:Configuration=Release `
        /t:Rebuild `
        /v:minimal
    if ($LASTEXITCODE -ne 0) { Write-Error "Installer custom actions build failed"; exit 1 }

    # Stage public/index.html and web.config into build/public/. The
    # per-plugin ZIPs are NOT staged here — they live next to the MSI in
    # $buildDir and are referenced directly from Components.wxs via the
    # wix bindpath zips=<buildDir>. CA_CopyPluginZipsToPublic replicates
    # them into MSCP_PUBLIC\plugins\ at install time when IISHosting is on.
    $publicSrc = Join-Path $root 'installer\public'
    $publicStaged = Join-Path $buildDir 'public'
    $stageScript = Join-Path $root 'installer\stage-iis-hosting.ps1'
    & $stageScript `
        -ManifestPath $manifestPath `
        -PublicSrc $publicSrc `
        -PublicStaged $publicStaged `
        -Version $version
    if ($LASTEXITCODE -ne 0) { Write-Error "stage-iis-hosting failed"; exit 1 }

    # Ensure the IIS extension is registered with the wix CLI. The latest IIS
    # extension on NuGet (v7) is not compatible with WiX 5; pin to the v5 line.
    # Idempotent: re-running with the same version is a no-op.
    Write-Host "Ensuring WixToolset.Iis.wixext extension is installed..." -ForegroundColor DarkYellow
    & wix extension add -g WixToolset.Iis.wixext/5.0.2 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { Write-Error "Failed to install WixToolset.Iis.wixext"; exit 1 }

    & wix build `
        -src $productWxs $componentsWxs $iisHostingWxs `
        -d "Version=$version" `
        -ext WixToolset.UI.wixext `
        -ext WixToolset.Util.wixext `
        -ext WixToolset.Iis.wixext `
        -b $wixDir `
        -bindpath "publicroot=$publicStaged" `
        -bindpath "zips=$buildDir" `
        -o $msiPath `
        -pdbtype none `
        -arch x64

    if ($LASTEXITCODE -ne 0) { Write-Error "WiX MSI build failed"; exit 1 }
} else {
    Write-Warning "WiX Toolset not found -- skipping MSI build."
    Write-Warning "Install with: dotnet tool install --global wix"
}

# ── Done ──
Write-Host "`n== Build complete ==" -ForegroundColor Green
Write-Host "Output: $buildDir"
foreach ($p in $plugins) {
    Write-Host "  ZIP: $($p.name)-v$version.zip"
}
if ($wixCmd) {
    Write-Host "  MSI: MSCPlugins-v$version.msi"
}
