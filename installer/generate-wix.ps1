#Requires -Version 5.1
<#
.SYNOPSIS
    Generates WiX component fragments from plugins.json.
    Run this before building the MSI to keep components in sync with the manifest.
.EXAMPLE
    pwsh installer/generate-wix.ps1
    pwsh installer/generate-wix.ps1 -StagingRoot "C:\build\staging"
#>

param(
    [string]$ManifestPath = (Join-Path $PSScriptRoot '..\plugins.json'),
    [string]$OutputPath   = (Join-Path $PSScriptRoot 'wix\Components.wxs'),
    [string]$StagingRoot  = (Join-Path $PSScriptRoot '..\build\staging')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$plugins = Get-Content $ManifestPath -Raw | ConvertFrom-Json

# Group by category
$sc = @($plugins | Where-Object { $_.category -eq 'SmartClient' })
$dd = @($plugins | Where-Object { $_.category -eq 'DeviceDriver' })
$ap = @($plugins | Where-Object { $_.category -eq 'AdminPlugin' })

function Get-InstallDirRef($plugin) {
    if ($plugin.category -eq 'DeviceDriver') { return 'MIPDRIVERS' }
    return 'MIPPLUGINS'
}

# Sanitize strings for use as WiX identifiers (A-Z, a-z, 0-9, _, .)
function Sanitize-WixId($raw) { $raw -replace '[^A-Za-z0-9_.]','_' }

function Get-FeatureId($name) { "Feature_$(Sanitize-WixId $name)" }
function Get-ComponentGroupId($name) { "CG_$(Sanitize-WixId $name)" }

# ── Build the XML ──
$xml = [System.Xml.XmlDocument]::new()
$xml.AppendChild($xml.CreateXmlDeclaration('1.0', 'UTF-8', $null)) | Out-Null

$wix = $xml.CreateElement('Wix', 'http://wixtoolset.org/schemas/v4/wxs')
$xml.AppendChild($wix) | Out-Null

# Add a comment
$wix.AppendChild($xml.CreateComment(' Auto-generated from plugins.json -- do not edit manually ')) | Out-Null
$wix.AppendChild($xml.CreateComment(' Run: pwsh installer/generate-wix.ps1 ')) | Out-Null

# ── Helper: Create a Fragment with a ComponentGroup that harvests files ──
function Add-PluginFragment($plugin) {
    $fragment = $xml.CreateElement('Fragment', 'http://wixtoolset.org/schemas/v4/wxs')
    $wix.AppendChild($fragment) | Out-Null

    $dirRef = Get-InstallDirRef $plugin
    $cgId   = Get-ComponentGroupId $plugin.name

    # Directory reference -> plugin subdirectory
    $dirRefElem = $xml.CreateElement('DirectoryRef', 'http://wixtoolset.org/schemas/v4/wxs')
    $dirRefElem.SetAttribute('Id', $dirRef)
    $fragment.AppendChild($dirRefElem) | Out-Null

    $pluginDir = $xml.CreateElement('Directory', 'http://wixtoolset.org/schemas/v4/wxs')
    $pluginDir.SetAttribute('Id', "Dir_$($plugin.name)")
    $pluginDir.SetAttribute('Name', $plugin.name)
    $dirRefElem.AppendChild($pluginDir) | Out-Null

    # ComponentGroup
    $cg = $xml.CreateElement('ComponentGroup', 'http://wixtoolset.org/schemas/v4/wxs')
    $cg.SetAttribute('Id', $cgId)
    $cg.SetAttribute('Directory', "Dir_$($plugin.name)")
    $fragment.AppendChild($cg) | Out-Null

    # Harvest files from staging directory
    $stagingDir = (Resolve-Path (Join-Path $StagingRoot $plugin.name) -ErrorAction SilentlyContinue).Path
    if ($stagingDir -and (Test-Path $stagingDir)) {
        $files = Get-ChildItem -Path $stagingDir -Recurse -File
        $fileIndex = 0
        $subdirs = @{}

        foreach ($file in $files) {
            $fileIndex++
            $relPath = $file.FullName.Substring($stagingDir.TrimEnd('\','/').Length + 1)
            $relDir  = [System.IO.Path]::GetDirectoryName($relPath)
            $fileId  = "$($plugin.name)_File$fileIndex"
            $compId  = "$($plugin.name)_Comp$fileIndex"

            # Determine which directory element to add to
            $targetDirId = "Dir_$($plugin.name)"
            if ($relDir) {
                # Create nested directory elements for subdirectories
                $parts = $relDir -split '[/\\]'
                $currentParent = "Dir_$($plugin.name)"
                $pathSoFar = ''
                foreach ($part in $parts) {
                    $pathSoFar = if ($pathSoFar) { "$pathSoFar\$part" } else { $part }
                    $subDirId = Sanitize-WixId "Dir_$($plugin.name)_$($pathSoFar -replace '[/\\]','_')"
                    if (-not $subdirs.ContainsKey($subDirId)) {
                        $subDir = $xml.CreateElement('Directory', 'http://wixtoolset.org/schemas/v4/wxs')
                        $subDir.SetAttribute('Id', $subDirId)
                        $subDir.SetAttribute('Name', $part)

                        # Find parent directory element
                        $parentElem = $pluginDir
                        if ($currentParent -ne "Dir_$($plugin.name)") {
                            $parentElem = $xml.SelectSingleNode("//*[@Id='$currentParent']")
                        }
                        if ($parentElem) {
                            $parentElem.AppendChild($subDir) | Out-Null
                        }
                        $subdirs[$subDirId] = $true
                    }
                    $currentParent = $subDirId
                }
                $targetDirId = $currentParent
            }

            # Component with a single file
            $comp = $xml.CreateElement('Component', 'http://wixtoolset.org/schemas/v4/wxs')
            $comp.SetAttribute('Id', $compId)
            $comp.SetAttribute('Directory', $targetDirId)
            $comp.SetAttribute('Bitness', 'always64')
            $cg.AppendChild($comp) | Out-Null

            $fileElem = $xml.CreateElement('File', 'http://wixtoolset.org/schemas/v4/wxs')
            $fileElem.SetAttribute('Id', $fileId)
            $fileElem.SetAttribute('Source', $file.FullName)
            $fileElem.SetAttribute('KeyPath', 'yes')
            $comp.AppendChild($fileElem) | Out-Null
        }
    } else {
        Write-Warning "Staging directory not found: $stagingDir (will be populated during CI build)"
    }
}

# ── Generate fragments for each plugin ──
foreach ($p in $plugins) {
    Add-PluginFragment $p
}

# ── Feature fragments: complete tree with category parents + plugin children ──
# Product.wxs references these top-level features via FeatureRef
function Add-FeatureFragment($featureId, $title, $description, $categoryPlugins) {
    $fragment = $xml.CreateElement('Fragment', 'http://wixtoolset.org/schemas/v4/wxs')
    $wix.AppendChild($fragment) | Out-Null

    # Top-level category feature (e.g. "Smart Client Plugins")
    $feature = $xml.CreateElement('Feature', 'http://wixtoolset.org/schemas/v4/wxs')
    $feature.SetAttribute('Id', $featureId)
    $feature.SetAttribute('Title', $title)
    $feature.SetAttribute('Description', $description)
    $feature.SetAttribute('Level', '1')
    $feature.SetAttribute('Display', 'expand')
    $feature.SetAttribute('AllowAbsent', 'yes')
    $feature.SetAttribute('AllowAdvertise', 'no')
    $fragment.AppendChild($feature) | Out-Null

    # Individual plugin features directly under category
    foreach ($p in $categoryPlugins) {
        $subFeature = $xml.CreateElement('Feature', 'http://wixtoolset.org/schemas/v4/wxs')
        $subFeature.SetAttribute('Id', (Get-FeatureId $p.name))
        $subFeature.SetAttribute('Title', $p.displayName)
        $subFeature.SetAttribute('Description', $p.description)
        $subFeature.SetAttribute('Level', '1')
        $subFeature.SetAttribute('AllowAbsent', 'yes')
        $subFeature.SetAttribute('AllowAdvertise', 'no')
        $feature.AppendChild($subFeature) | Out-Null

        $cgRef = $xml.CreateElement('ComponentGroupRef', 'http://wixtoolset.org/schemas/v4/wxs')
        $cgRef.SetAttribute('Id', (Get-ComponentGroupId $p.name))
        $subFeature.AppendChild($cgRef) | Out-Null
    }
}

Add-FeatureFragment 'SmartClientPlugins' 'Smart Client Plugins' 'Plugins for the XProtect Smart Client. Installs to C:\Program Files\Milestone\MIPPlugins\' $sc
Add-FeatureFragment 'DeviceDrivers' 'Device Drivers' 'Device drivers for the XProtect Recording Server. Installs to C:\Program Files\Milestone\MIPDrivers\' $dd
Add-FeatureFragment 'AdminPlugins' 'Admin Plugins' 'Plugins for the XProtect Management Client / Event Server. Installs to C:\Program Files\Milestone\MIPPlugins\' $ap

# ── Write output ──
$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.IndentChars = '  '
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)

$writer = [System.Xml.XmlWriter]::Create($OutputPath, $settings)
$xml.Save($writer)
$writer.Close()

Write-Host "Generated: $OutputPath"
