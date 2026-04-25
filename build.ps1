#Requires -Version 5.1
<#
.SYNOPSIS
    Unified build script for MSC Community Plugins.
    Reads plugins.json for the plugin list. Builds all plugins in Release,
    creates per-plugin ZIPs and a WiX MSI installer.
.EXAMPLE
    .\build.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

# ── Create ZIPs ──
Write-Host "`n[5/6] Creating release ZIPs..." -ForegroundColor Yellow
foreach ($p in $plugins) {
    $zipPath = Join-Path $buildDir "$($p.name)-v$version.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $staging $p.name) -DestinationPath $zipPath
    Write-Host "  -> $zipPath"
}

# ── Build WiX MSI installer ──
Write-Host "`n[6/6] Building WiX MSI installer..." -ForegroundColor Yellow

$wixCmd = Get-Command wix -ErrorAction SilentlyContinue
if ($wixCmd) {
    # Generate WiX components from staging
    $genWixScript = Join-Path $root 'installer\generate-wix.ps1'
    & $genWixScript -ManifestPath $manifestPath -StagingRoot $staging

    $wixDir = Join-Path $root 'installer\wix'
    $productWxs = Join-Path $wixDir 'Product.wxs'
    $componentsWxs = Join-Path $wixDir 'Components.wxs'
    $iisHostingWxs = Join-Path $wixDir 'IisHosting.wxs'
    $iisHostingPluginsWxs = Join-Path $wixDir 'IisHostingPlugins.wxs'
    $msiPath = Join-Path $buildDir "MSCPlugins-v$version.msi"
    $customActionProj = Join-Path $root 'installer\customactions\InstallerCustomActions.csproj'

    Write-Host "Building installer custom actions..." -ForegroundColor DarkYellow
    & $msbuildPath $customActionProj `
        /restore `
        /p:Configuration=Release `
        /t:Rebuild `
        /v:minimal
    if ($LASTEXITCODE -ne 0) { Write-Error "Installer custom actions build failed"; exit 1 }

    # Stage public/index.html and web.config into build/public/, copy each
    # per-plugin ZIP into build/public/plugins/, generate the WiX fragment
    # that turns those ZIPs into installable components, and inject the
    # plugin-list rows into index.html.
    $publicSrc = Join-Path $root 'installer\public'
    $publicStaged = Join-Path $buildDir 'public'
    $publicPluginsStaged = Join-Path $publicStaged 'plugins'
    if (Test-Path $publicStaged) { Remove-Item $publicStaged -Recurse -Force }
    New-Item -ItemType Directory -Path $publicStaged -Force | Out-Null
    New-Item -ItemType Directory -Path $publicPluginsStaged -Force | Out-Null

    Add-Type -AssemblyName System.Web | Out-Null

    # Deterministic GUID derived from a fixed namespace + plugin name. Stable
    # across builds and machines, so MSI upgrade swaps the prior version's ZIP
    # cleanly. WiX does not allow Guid="*" for components rooted outside a
    # standard directory like ProgramFilesFolder, which our MSCP_PUBLIC is.
    $md5 = [System.Security.Cryptography.MD5]::Create()
    function Get-StableGuid([string]$seed) {
        $bytes = $md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes("MscpPluginZip:$seed"))
        # Stamp version=4 / variant=10 bits per RFC 4122 so the value is a
        # well-formed UUID (purely cosmetic, MSI doesn't care).
        $bytes[6] = ($bytes[6] -band 0x0F) -bor 0x40
        $bytes[8] = ($bytes[8] -band 0x3F) -bor 0x80
        ([System.Guid]::new($bytes)).ToString().ToUpper()
    }

    # Copy per-plugin ZIPs into the staged plugins folder, build the WiX
    # component fragment, and produce per-category HTML sections sorted by
    # display name. Categories that aren't present in plugins.json are simply
    # absent from the page.
    $categoryDefs = @(
        @{ Key = 'SmartClient';  Title = 'Smart Client plugins';      Path = 'C:\Program Files\Milestone\MIPPlugins'; Note = 'Install on each Smart Client / Management Client workstation.' }
        @{ Key = 'DeviceDriver'; Title = 'Device drivers';            Path = 'C:\Program Files\Milestone\MIPDrivers'; Note = 'Install on the Recording Server.' }
        @{ Key = 'AdminPlugin';  Title = 'Management Client plugins'; Path = 'C:\Program Files\Milestone\MIPPlugins'; Note = 'Install on the Management Server (and on each Management Client that needs the configuration UI).' }
    )

    $pluginRows = New-Object System.Text.StringBuilder
    $pluginCmpEntries = New-Object System.Text.StringBuilder
    $sortedPlugins = $plugins | Sort-Object {
        if ($_ | Get-Member -Name displayName -MemberType NoteProperty) { $_.displayName } else { $_.name }
    }

    # Always emit the WiX components for every plugin (regardless of category).
    foreach ($p in $sortedPlugins) {
        $zipName = "$($p.name)-v$version.zip"
        $zipSrc = Join-Path $buildDir $zipName
        Copy-Item -Path $zipSrc -Destination $publicPluginsStaged -Force

        $cmpId = "cmpZip_$($p.name)"
        $cmpGuid = Get-StableGuid $p.name
        [void]$pluginCmpEntries.AppendLine("      <Component Id=`"$cmpId`" Directory=`"MSCP_PLUGINS_DIR`" Guid=`"$cmpGuid`" Condition=`"IIS_PRESENT`">")
        [void]$pluginCmpEntries.AppendLine("        <File Source=`"!(bindpath.publicroot)\plugins\$zipName`" KeyPath=`"yes`" />")
        [void]$pluginCmpEntries.AppendLine("      </Component>")
    }

    foreach ($cat in $categoryDefs) {
        $catPlugins = $sortedPlugins | Where-Object {
            ($_ | Get-Member -Name category -MemberType NoteProperty) -and $_.category -eq $cat.Key
        }
        if (-not $catPlugins -or $catPlugins.Count -eq 0) { continue }

        $titleEnc = [System.Web.HttpUtility]::HtmlEncode($cat.Title)
        $pathEnc = [System.Web.HttpUtility]::HtmlEncode($cat.Path)
        $noteEnc = [System.Web.HttpUtility]::HtmlEncode($cat.Note)
        [void]$pluginRows.AppendLine("      <h3>$titleEnc</h3>")
        [void]$pluginRows.AppendLine("      <p class=`"path`">$noteEnc Installs to <code>$pathEnc</code>.</p>")
        [void]$pluginRows.AppendLine("      <ul class=`"plugin-list`">")
        foreach ($p in $catPlugins) {
            $zipName = "$($p.name)-v$version.zip"
            $display = if ($p | Get-Member -Name displayName -MemberType NoteProperty) { $p.displayName } else { $p.name }
            $displayEnc = [System.Web.HttpUtility]::HtmlEncode($display)
            [void]$pluginRows.AppendLine("        <li><a href=`"plugins/$zipName`" download>$displayEnc</a></li>")
        }
        [void]$pluginRows.AppendLine("      </ul>")
    }

    # Materialize index.html with __VERSION__ and __PLUGINS_HTML__ replaced.
    $indexHtml = Get-Content (Join-Path $publicSrc 'index.html') -Raw
    $indexHtml = $indexHtml.Replace('__VERSION__', $version).Replace('__PLUGINS_HTML__', $pluginRows.ToString().TrimEnd())
    Set-Content -Path (Join-Path $publicStaged 'index.html') -Value $indexHtml -NoNewline -Encoding UTF8

    # Materialize web.config (no token replacement, just copy).
    Copy-Item -Path (Join-Path $publicSrc 'web.config') -Destination $publicStaged -Force

    # Materialize the auto-generated WiX fragment for the plugin ZIPs.
    $iisHostingPluginsContent = @"
<?xml version="1.0" encoding="UTF-8"?>
<!--
  AUTO-GENERATED by build.ps1 from plugins.json. Do not edit by hand;
  changes will be overwritten on the next build.
-->
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>
    <ComponentGroup Id="MscpPluginZips">
$($pluginCmpEntries.ToString().TrimEnd())
    </ComponentGroup>
  </Fragment>
</Wix>
"@
    Set-Content -Path $iisHostingPluginsWxs -Value $iisHostingPluginsContent -Encoding UTF8

    Write-Host "Staged index.html (v$version), web.config, $($plugins.Count) plugin ZIPs, IisHostingPlugins.wxs" -ForegroundColor DarkYellow

    # Ensure the IIS extension is registered with the wix CLI. The latest IIS
    # extension on NuGet (v7) is not compatible with WiX 5; pin to the v5 line.
    # Idempotent: re-running with the same version is a no-op.
    Write-Host "Ensuring WixToolset.Iis.wixext extension is installed..." -ForegroundColor DarkYellow
    & wix extension add -g WixToolset.Iis.wixext/5.0.2 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { Write-Error "Failed to install WixToolset.Iis.wixext"; exit 1 }

    & wix build `
        -src $productWxs $componentsWxs $iisHostingWxs $iisHostingPluginsWxs `
        -d "Version=$version" `
        -ext WixToolset.UI.wixext `
        -ext WixToolset.Util.wixext `
        -ext WixToolset.Iis.wixext `
        -b $wixDir `
        -bindpath "publicroot=$publicStaged" `
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
