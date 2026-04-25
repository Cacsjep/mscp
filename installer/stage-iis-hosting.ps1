#Requires -Version 5.1
<#
.SYNOPSIS
    Stage the public/ payload for the IISHosting feature: index.html (with
    plugin list injected) and web.config. The per-plugin ZIPs themselves are
    NOT staged here — they live in the build directory and are pulled into
    the MSI via wix's -bindpath zips=<dir>, then copied to MSCP_PUBLIC\plugins\
    at install time by CA_CopyPluginZipsToPublic.

    Called by both build.ps1 (local) and the GitHub Actions release/dev
    workflows so the WiX inputs are identical.

.PARAMETER ManifestPath
    Path to plugins.json.

.PARAMETER PublicSrc
    Path to installer/public/ (source of index.html and web.config).

.PARAMETER PublicStaged
    Output directory where index.html and web.config land. Becomes the WiX
    bindpath publicroot=<value>.

.PARAMETER Version
    Version string used in index.html href text and embedded in the page.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ManifestPath,
    [Parameter(Mandatory)] [string] $PublicSrc,
    [Parameter(Mandatory)] [string] $PublicStaged,
    [Parameter(Mandatory)] [string] $Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Web | Out-Null

$plugins = Get-Content $ManifestPath -Raw | ConvertFrom-Json

if (Test-Path $PublicStaged) { Remove-Item $PublicStaged -Recurse -Force }
New-Item -ItemType Directory -Path $PublicStaged -Force | Out-Null

# Per-category section definitions (HTML labels + install paths).
$categoryDefs = @(
    @{ Key = 'SmartClient';  Title = 'Smart Client plugins';      Path = 'C:\Program Files\Milestone\MIPPlugins'; Note = 'Install on each Smart Client / Management Client workstation.' }
    @{ Key = 'DeviceDriver'; Title = 'Device drivers';            Path = 'C:\Program Files\Milestone\MIPDrivers'; Note = 'Install on the Recording Server.' }
    @{ Key = 'AdminPlugin';  Title = 'Management Client plugins'; Path = 'C:\Program Files\Milestone\MIPPlugins'; Note = 'Install on the Management Server (and on each Management Client that needs the configuration UI).' }
)

$sortedPlugins = $plugins | Sort-Object {
    if ($_ | Get-Member -Name displayName -MemberType NoteProperty) { $_.displayName } else { $_.name }
}

# Per-category HTML sections (alphabetical within each category).
$pluginRows = New-Object System.Text.StringBuilder
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

Write-Host "Staged index.html (v$Version) and web.config for $($plugins.Count) plugins"
