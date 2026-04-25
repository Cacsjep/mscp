#Requires -Version 5.1
<#
.SYNOPSIS
    Stage the public/ payload for the IISHosting feature and generate
    the matching WiX fragment with one component per plugin ZIP.

    Called by both build.ps1 (local) and the GitHub Actions release/dev
    workflows so the WiX inputs are identical in both code paths.

.PARAMETER ManifestPath
    Path to plugins.json.

.PARAMETER PublicSrc
    Path to installer/public/ (source of index.html and web.config).

.PARAMETER PublicStaged
    Output directory where index.html, web.config, and plugins/*.zip land.
    Becomes the WiX bindpath publicroot=<value>.

.PARAMETER ZipsRoot
    Directory containing the per-plugin ZIPs named "<PluginName>-v<Version>.zip".

.PARAMETER WixOutPath
    Where to write the generated IisHostingPlugins.wxs fragment.

.PARAMETER Version
    Version string used in index.html and ZIP file names.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ManifestPath,
    [Parameter(Mandatory)] [string] $PublicSrc,
    [Parameter(Mandatory)] [string] $PublicStaged,
    [Parameter(Mandatory)] [string] $ZipsRoot,
    [Parameter(Mandatory)] [string] $WixOutPath,
    [Parameter(Mandatory)] [string] $Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Web | Out-Null

$plugins = Get-Content $ManifestPath -Raw | ConvertFrom-Json

if (Test-Path $PublicStaged) { Remove-Item $PublicStaged -Recurse -Force }
$publicPluginsStaged = Join-Path $PublicStaged 'plugins'
New-Item -ItemType Directory -Path $publicPluginsStaged -Force | Out-Null

# Deterministic GUID derived from a fixed namespace + plugin name. Stable
# across builds and machines so MSI upgrade swaps each ZIP cleanly. WiX
# does not allow Guid="*" for components rooted outside a standard
# directory, which our MSCP_PUBLIC is.
$md5 = [System.Security.Cryptography.MD5]::Create()
function Get-StableGuid([string]$seed) {
    $bytes = $md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes("MscpPluginZip:$seed"))
    $bytes[6] = ($bytes[6] -band 0x0F) -bor 0x40
    $bytes[8] = ($bytes[8] -band 0x3F) -bor 0x80
    ([System.Guid]::new($bytes)).ToString().ToUpper()
}

# Per-category section definitions (HTML labels + install paths).
$categoryDefs = @(
    @{ Key = 'SmartClient';  Title = 'Smart Client plugins';      Path = 'C:\Program Files\Milestone\MIPPlugins'; Note = 'Install on each Smart Client / Management Client workstation.' }
    @{ Key = 'DeviceDriver'; Title = 'Device drivers';            Path = 'C:\Program Files\Milestone\MIPDrivers'; Note = 'Install on the Recording Server.' }
    @{ Key = 'AdminPlugin';  Title = 'Management Client plugins'; Path = 'C:\Program Files\Milestone\MIPPlugins'; Note = 'Install on the Management Server (and on each Management Client that needs the configuration UI).' }
)

$sortedPlugins = $plugins | Sort-Object {
    if ($_ | Get-Member -Name displayName -MemberType NoteProperty) { $_.displayName } else { $_.name }
}

$pluginRows = New-Object System.Text.StringBuilder
$pluginCmpEntries = New-Object System.Text.StringBuilder

# WiX components: one per plugin ZIP regardless of category.
foreach ($p in $sortedPlugins) {
    $zipName = "$($p.name)-v$Version.zip"
    $zipSrc = Join-Path $ZipsRoot $zipName
    if (-not (Test-Path $zipSrc)) {
        throw "Expected ZIP not found: $zipSrc"
    }
    Copy-Item -Path $zipSrc -Destination $publicPluginsStaged -Force

    $cmpId = "cmpZip_$($p.name)"
    $cmpGuid = Get-StableGuid $p.name
    [void]$pluginCmpEntries.AppendLine("      <Component Id=`"$cmpId`" Directory=`"MSCP_PLUGINS_DIR`" Guid=`"$cmpGuid`" Condition=`"IIS_PRESENT`">")
    [void]$pluginCmpEntries.AppendLine("        <File Source=`"!(bindpath.publicroot)\plugins\$zipName`" KeyPath=`"yes`" />")
    [void]$pluginCmpEntries.AppendLine("      </Component>")
}

# Per-category HTML sections (alphabetical within each category).
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
        $zipName = "$($p.name)-v$Version.zip"
        $display = if ($p | Get-Member -Name displayName -MemberType NoteProperty) { $p.displayName } else { $p.name }
        $displayEnc = [System.Web.HttpUtility]::HtmlEncode($display)
        [void]$pluginRows.AppendLine("        <li><a href=`"plugins/$zipName`" download>$displayEnc</a></li>")
    }
    [void]$pluginRows.AppendLine("      </ul>")
}

# Materialize index.html with __VERSION__ and __PLUGINS_HTML__ replaced.
$indexHtml = Get-Content (Join-Path $PublicSrc 'index.html') -Raw
$indexHtml = $indexHtml.Replace('__VERSION__', $Version).Replace('__PLUGINS_HTML__', $pluginRows.ToString().TrimEnd())
Set-Content -Path (Join-Path $PublicStaged 'index.html') -Value $indexHtml -NoNewline -Encoding UTF8

# Copy web.config (no token replacement).
Copy-Item -Path (Join-Path $PublicSrc 'web.config') -Destination $PublicStaged -Force

# Materialize IisHostingPlugins.wxs.
$wxs = @"
<?xml version="1.0" encoding="UTF-8"?>
<!--
  AUTO-GENERATED by installer/stage-iis-hosting.ps1 from plugins.json.
  Do not edit by hand; changes will be overwritten on the next build.
-->
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>
    <ComponentGroup Id="MscpPluginZips">
$($pluginCmpEntries.ToString().TrimEnd())
    </ComponentGroup>
  </Fragment>
</Wix>
"@
Set-Content -Path $WixOutPath -Value $wxs -Encoding UTF8

Write-Host "Staged $($plugins.Count) plugin ZIPs and generated IisHostingPlugins.wxs"
