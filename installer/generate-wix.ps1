#Requires -Version 5.1
<#
.SYNOPSIS
    Generates WiX component fragments from plugins.json.

    Each plugin is represented in the MSI by ONE component: a single ZIP file
    installed into [CommonAppDataFolder]Milestone\MSCPPluginPayload\<Name>.zip.
    Per-plugin extract / cleanup CAs unpack the ZIP into MIPPLUGINS\<Name>\ or
    MIPDRIVERS\<Name>\ at install time, and remove that folder on uninstall /
    feature deselect. The IISHosting feature pulls in every plugin's ZIP via
    the MscpAllPluginZips component group, so a "page only" install ends up
    with all 18 ZIPs on disk under ProgramData (which a sweep CA in
    Product.wxs then copies into MSCP_PUBLIC\plugins\).

    Run this before building the MSI. The output is consumed by wix build via
    -bindpath zips=<dir-with-per-plugin-ZIPs> so the Source attribute resolves
    to e.g. <buildDir>\Weather-v1.15.25613.zip at compile time.

.EXAMPLE
    pwsh installer/generate-wix.ps1
#>

param(
    [string]$ManifestPath = (Join-Path $PSScriptRoot '..\plugins.json'),
    [string]$OutputPath   = (Join-Path $PSScriptRoot 'wix\Components.wxs')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$plugins = Get-Content $ManifestPath -Raw | ConvertFrom-Json

# Group by category for the rollup features (Smart Client / Drivers / Admin).
$sc = @($plugins | Where-Object { $_.category -eq 'SmartClient' })
$dd = @($plugins | Where-Object { $_.category -eq 'DeviceDriver' })
$ap = @($plugins | Where-Object { $_.category -eq 'AdminPlugin' })

# Sanitize plugin name -> WiX identifier (A-Z, a-z, 0-9, _, .)
function Sanitize-WixId($raw) { $raw -replace '[^A-Za-z0-9_.]','_' }

function Get-FeatureId($name)   { "Feature_$(Sanitize-WixId $name)" }
function Get-ComponentId($name) { "cmpZip_$(Sanitize-WixId $name)" }
function Get-ExtractCAId($name) { "CA_Extract_$(Sanitize-WixId $name)" }
function Get-CleanupCAId($name) { "CA_Cleanup_$(Sanitize-WixId $name)" }
function Get-DirRef($plugin) {
    if ($plugin.category -eq 'DeviceDriver') { return 'MIPDRIVERS' }
    return 'MIPPLUGINS'
}

# Stable component GUID (MD5 of "MscpPluginZip:<name>", stamped to UUID v4).
# WiX requires explicit GUIDs for components rooted under non-standard
# directories, and stability lets Major Upgrade swap the file cleanly.
$md5 = [System.Security.Cryptography.MD5]::Create()
function Get-StableGuid([string]$seed) {
    $bytes = $md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes("MscpPluginZip:$seed"))
    $bytes[6] = ($bytes[6] -band 0x0F) -bor 0x40
    $bytes[8] = ($bytes[8] -band 0x3F) -bor 0x80
    ([System.Guid]::new($bytes)).ToString().ToUpper()
}

# ── Build the XML ──
$xml = [System.Xml.XmlDocument]::new()
$xml.AppendChild($xml.CreateXmlDeclaration('1.0', 'UTF-8', $null)) | Out-Null

$wix = $xml.CreateElement('Wix', 'http://wixtoolset.org/schemas/v4/wxs')
$xml.AppendChild($wix) | Out-Null

$wix.AppendChild($xml.CreateComment(' Auto-generated from plugins.json -- do not edit manually ')) | Out-Null
$wix.AppendChild($xml.CreateComment(' Run: pwsh installer/generate-wix.ps1 ')) | Out-Null

function Add-Element($parent, $name, $attrs = @{}) {
    $elem = $xml.CreateElement($name, 'http://wixtoolset.org/schemas/v4/wxs')
    foreach ($k in $attrs.Keys) { $elem.SetAttribute($k, $attrs[$k]) }
    $parent.AppendChild($elem) | Out-Null
    return $elem
}

# ── Fragment 1: Payload directory under CommonAppDataFolder ──
$frag = Add-Element $wix 'Fragment'
$std = Add-Element $frag 'StandardDirectory' @{ Id = 'CommonAppDataFolder' }
$dirMs = Add-Element $std 'Directory' @{ Id = 'MSCP_PAYLOAD_MILESTONE'; Name = 'Milestone' }
$dirPay = Add-Element $dirMs 'Directory' @{ Id = 'MSCP_PAYLOAD_DIR'; Name = 'MSCPPluginPayload' }

# ── Fragment 2..N: one per plugin (component + extract/cleanup CAs + sequence) ──
foreach ($p in $plugins) {
    $cmpId   = Get-ComponentId $p.name
    $cmpGuid = Get-StableGuid   $p.name
    $exId    = Get-ExtractCAId  $p.name
    $clId    = Get-CleanupCAId  $p.name
    $featId  = Get-FeatureId    $p.name
    $dirRef  = Get-DirRef       $p

    $frag = Add-Element $wix 'Fragment'

    # Component holding the single ZIP file.
    $dr = Add-Element $frag 'DirectoryRef' @{ Id = 'MSCP_PAYLOAD_DIR' }
    $cmp = Add-Element $dr 'Component' @{ Id = $cmpId; Guid = $cmpGuid; Bitness = 'always64' }
    Add-Element $cmp 'File' @{
        Id      = "zip_$(Sanitize-WixId $p.name)"
        Name    = "$($p.name).zip"
        Source  = "!(bindpath.zips)\$($p.name)-v`$(var.Version).zip"
        KeyPath = 'yes'
    } | Out-Null

    # Extract CA: unpack the ZIP into [MIPPLUGINS|MIPDRIVERS]\<Name>\ on install.
    Add-Element $frag 'SetProperty' @{
        Id       = $exId
        Before   = $exId
        Sequence = 'execute'
        Value    = "Source=[CommonAppDataFolder]Milestone\MSCPPluginPayload\$($p.name).zip;Dest=[$dirRef]$($p.name)"
    } | Out-Null
    Add-Element $frag 'CustomAction' @{
        Id          = $exId
        BinaryRef   = 'InstallerCustomActions'
        DllEntry    = 'ExtractZipToFolder'
        Execute     = 'deferred'
        Impersonate = 'no'
        Return      = 'check'
    } | Out-Null

    # Cleanup CA: remove the extracted folder on uninstall / feature removal.
    Add-Element $frag 'SetProperty' @{
        Id       = $clId
        Before   = $clId
        Sequence = 'execute'
        Value    = "Dest=[$dirRef]$($p.name)"
    } | Out-Null
    Add-Element $frag 'CustomAction' @{
        Id          = $clId
        BinaryRef   = 'InstallerCustomActions'
        DllEntry    = 'RemoveExtractedFolder'
        Execute     = 'deferred'
        Impersonate = 'no'
        Return      = 'ignore'
    } | Out-Null

    # InstallExecuteSequence: run extract on Feature_<Name>=3 (going to local),
    # cleanup on =2 (being removed) or full uninstall.
    $seq = Add-Element $frag 'InstallExecuteSequence'
    Add-Element $seq 'Custom' @{
        Action    = $exId
        After     = 'InstallFiles'
        Condition = "&$featId=3"
    } | Out-Null
    Add-Element $seq 'Custom' @{
        Action    = $clId
        Before    = 'RemoveFiles'
        Condition = "(&$featId=2) OR REMOVE=`"ALL`""
    } | Out-Null
}

# ── Fragment: rollup features (SmartClient / Drivers / Admin) ──
function Add-RollupFeature($featureId, $title, $description, $categoryPlugins) {
    $frag = Add-Element $wix 'Fragment'
    $rollup = Add-Element $frag 'Feature' @{
        Id             = $featureId
        Title          = $title
        Description    = $description
        Level          = '1'
        Display        = 'expand'
        AllowAbsent    = 'yes'
        AllowAdvertise = 'no'
    }
    foreach ($p in $categoryPlugins) {
        $sub = Add-Element $rollup 'Feature' @{
            Id             = (Get-FeatureId $p.name)
            Title          = $p.displayName
            Description    = $p.description
            Level          = '1'
            AllowAbsent    = 'yes'
            AllowAdvertise = 'no'
        }
        Add-Element $sub 'ComponentRef' @{ Id = (Get-ComponentId $p.name) } | Out-Null
    }
}

Add-RollupFeature 'SmartClientPlugins' 'Smart Client Plugins' 'Plugins for the XProtect Smart Client. Installs to C:\Program Files\Milestone\MIPPlugins\' $sc
Add-RollupFeature 'DeviceDrivers'      'Device Drivers'        'Device drivers for the XProtect Recording Server. Installs to C:\Program Files\Milestone\MIPDrivers\' $dd
Add-RollupFeature 'AdminPlugins'       'Admin Plugins'         'Plugins for the XProtect Management Client / Event Server. Installs to C:\Program Files\Milestone\MIPPlugins\' $ap

# ── Fragment: MscpAllPluginZips component group, referenced by IISHosting ──
# Pulling this group into the IISHosting feature is what guarantees that
# selecting "Local download page" installs every plugin's ZIP under
# ProgramData regardless of which plugin features the user picked.
$frag = Add-Element $wix 'Fragment'
$cg = Add-Element $frag 'ComponentGroup' @{ Id = 'MscpAllPluginZips' }
foreach ($p in $plugins) {
    Add-Element $cg 'ComponentRef' @{ Id = (Get-ComponentId $p.name) } | Out-Null
}

# ── Write output ──
$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.IndentChars = '  '
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)

$writer = [System.Xml.XmlWriter]::Create($OutputPath, $settings)
$xml.Save($writer)
$writer.Close()

Write-Host "Generated: $OutputPath ($($plugins.Count) plugins, ZIP-only payload)"
