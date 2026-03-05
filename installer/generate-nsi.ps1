#Requires -Version 5.1
<#
.SYNOPSIS
    Generates plugin-generated.nsi from plugins.json.
    Run this before makensis to keep NSIS sections in sync with the manifest.
.EXAMPLE
    pwsh installer/generate-nsi.ps1
#>

param(
    [string]$ManifestPath = (Join-Path $PSScriptRoot '..\plugins.json'),
    [string]$OutputPath   = (Join-Path $PSScriptRoot 'plugin-generated.nsi')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$plugins = Get-Content $ManifestPath -Raw | ConvertFrom-Json

# Group by category
$sc = @($plugins | Where-Object { $_.category -eq 'SmartClient' })
$dd = @($plugins | Where-Object { $_.category -eq 'DeviceDriver' })
$ap = @($plugins | Where-Object { $_.category -eq 'AdminPlugin' })

function Get-SectionId($name) { "SEC_$($name.ToUpper())" }
function Get-DirDefine($name) { "$($name.ToUpper())_DIR" }
function Get-InstallDir($plugin) {
    if ($plugin.category -eq 'DeviceDriver') { return 'MIPDrivers' }
    return 'MIPPlugins'
}

$out = [System.Text.StringBuilder]::new()
[void]$out.AppendLine('; Auto-generated from plugins.json -- do not edit manually')
[void]$out.AppendLine('; Run: pwsh installer/generate-nsi.ps1')
[void]$out.AppendLine('')

# ── Macro: PluginStagingDefines ──
[void]$out.AppendLine('!macro PluginStagingDefines')
foreach ($p in $plugins) {
    $def = Get-DirDefine $p.name
    [void]$out.AppendLine("  !ifndef $def")
    [void]$out.AppendLine("    !define $def `"..\build\staging\$($p.name)`"")
    [void]$out.AppendLine("  !endif")
}
[void]$out.AppendLine('!macroend')
[void]$out.AppendLine('')

# ── Helper: generate a standard install section ──
function Write-Section($p) {
    $secId = Get-SectionId $p.name
    $dirDef = Get-DirDefine $p.name
    $installDir = Get-InstallDir $p

    [void]$out.AppendLine("  Section `"$($p.displayName)`" $secId")

    # Device drivers get DFP gate check
    if ($p.category -eq 'DeviceDriver') {
        [void]$out.AppendLine('    _dfp_check:')
        [void]$out.AppendLine('    !insertmacro _CheckProcessRunning "${DFP_PROCESS}" $R1')
        [void]$out.AppendLine('    !insertmacro _LogMsg "DriverFrameworkProcess running: $R1"')
        [void]$out.AppendLine('    ${If} $R1 == "1"')
        [void]$out.AppendLine('      MessageBox MB_YESNO|MB_ICONEXCLAMATION \')
        [void]$out.AppendLine('        "${DFP_PROCESS} is still running and will lock driver files.$\r$\n$\r$\nKill the process now?" \')
        [void]$out.AppendLine('        IDNO _dfp_skip')
        [void]$out.AppendLine('      !insertmacro _LogMsg "Killing ${DFP_PROCESS}..."')
        [void]$out.AppendLine('      nsExec::ExecToLog ''taskkill /F /IM "${DFP_PROCESS}"''')
        [void]$out.AppendLine('      Pop $0')
        [void]$out.AppendLine('      !insertmacro _LogMsg "taskkill ${DFP_PROCESS} exit code: $0"')
        [void]$out.AppendLine('      Sleep 3000')
        [void]$out.AppendLine('      Goto _dfp_check')
        [void]$out.AppendLine('      _dfp_skip:')
        [void]$out.AppendLine('        !insertmacro _LogMsg "User skipped killing ${DFP_PROCESS}."')
        [void]$out.AppendLine('    ${EndIf}')
        [void]$out.AppendLine('')
    }

    [void]$out.AppendLine("    SetOutPath `"`$INSTDIR\$installDir\$($p.name)`"")
    [void]$out.AppendLine("    !insertmacro _LogMsg `"Installing $($p.displayName) to `$INSTDIR\$installDir\$($p.name)...`"")
    [void]$out.AppendLine("    File /r `"`${$dirDef}\*.*`"")
    [void]$out.AppendLine("    !insertmacro _LogMsg `"$($p.displayName) installed.`"")
    [void]$out.AppendLine('')
    [void]$out.AppendLine("    WriteRegStr HKLM `"Software\Microsoft\Windows\CurrentVersion\Uninstall\$($p.name)`" \")
    [void]$out.AppendLine("      `"DisplayName`" `"$($p.displayName) v`${VERSION}`"")
    [void]$out.AppendLine("    WriteRegStr HKLM `"Software\Microsoft\Windows\CurrentVersion\Uninstall\$($p.name)`" \")
    [void]$out.AppendLine("      `"UninstallString`" `"`$\`"`$INSTDIR\$installDir\$($p.name)\Uninstall.exe`$\`"`"")
    [void]$out.AppendLine("    WriteRegStr HKLM `"Software\Microsoft\Windows\CurrentVersion\Uninstall\$($p.name)`" \")
    [void]$out.AppendLine("      `"DisplayVersion`" `"`${VERSION}`"")
    [void]$out.AppendLine("    WriteRegStr HKLM `"Software\Microsoft\Windows\CurrentVersion\Uninstall\$($p.name)`" \")
    [void]$out.AppendLine("      `"Publisher`" `"MSC Community Plugins`"")
    [void]$out.AppendLine("    WriteRegDWORD HKLM `"Software\Microsoft\Windows\CurrentVersion\Uninstall\$($p.name)`" \")
    [void]$out.AppendLine("      `"NoModify`" 1")
    [void]$out.AppendLine("    WriteRegDWORD HKLM `"Software\Microsoft\Windows\CurrentVersion\Uninstall\$($p.name)`" \")
    [void]$out.AppendLine("      `"NoRepair`" 1")
    [void]$out.AppendLine('  SectionEnd')
    [void]$out.AppendLine('')
}

# ── Macro: PluginSmartClientSections ──
[void]$out.AppendLine('!macro PluginSmartClientSections')
foreach ($p in $sc) { Write-Section $p }
[void]$out.AppendLine('!macroend')
[void]$out.AppendLine('')

# ── Macro: PluginDeviceDriverSections ──
[void]$out.AppendLine('!macro PluginDeviceDriverSections')
foreach ($p in $dd) { Write-Section $p }
[void]$out.AppendLine('!macroend')
[void]$out.AppendLine('')

# ── Macro: PluginAdminPluginSections ──
[void]$out.AppendLine('!macro PluginAdminPluginSections')
foreach ($p in $ap) { Write-Section $p }
[void]$out.AppendLine('!macroend')
[void]$out.AppendLine('')

# ── Macro: PluginDescriptions ──
[void]$out.AppendLine('!macro PluginDescriptions')
foreach ($p in $plugins) {
    $secId = Get-SectionId $p.name
    [void]$out.AppendLine("  !insertmacro MUI_DESCRIPTION_TEXT `${$secId} `"$($p.description)`"")
}
[void]$out.AppendLine('!macroend')
[void]$out.AppendLine('')

# ── Macro: PluginComponentsLeave ──
[void]$out.AppendLine('!macro PluginComponentsLeave')

if ($sc.Count -gt 0) {
    [void]$out.AppendLine("  ; Smart Client plugins -> stop Smart Client")
    $first = $true
    foreach ($p in $sc) {
        $secId = Get-SectionId $p.name
        if ($first) {
            [void]$out.AppendLine("  `${If} `${SectionIsSelected} `${$secId}")
            $first = $false
        } else {
            [void]$out.AppendLine("  `${OrIf} `${SectionIsSelected} `${$secId}")
        }
    }
    [void]$out.AppendLine('    StrCpy $STOP_SC "1"')
    [void]$out.AppendLine('  ${EndIf}')
    [void]$out.AppendLine('')
}

if ($dd.Count -gt 0) {
    [void]$out.AppendLine("  ; Device Drivers -> stop Recording Server")
    $first = $true
    foreach ($p in $dd) {
        $secId = Get-SectionId $p.name
        if ($first) {
            [void]$out.AppendLine("  `${If} `${SectionIsSelected} `${$secId}")
            $first = $false
        } else {
            [void]$out.AppendLine("  `${OrIf} `${SectionIsSelected} `${$secId}")
        }
    }
    [void]$out.AppendLine('    StrCpy $STOP_RS "1"')
    [void]$out.AppendLine('  ${EndIf}')
    [void]$out.AppendLine('')
}

if ($ap.Count -gt 0) {
    [void]$out.AppendLine("  ; Admin Plugins -> stop Event Server + Smart Client")
    $first = $true
    foreach ($p in $ap) {
        $secId = Get-SectionId $p.name
        if ($first) {
            [void]$out.AppendLine("  `${If} `${SectionIsSelected} `${$secId}")
            $first = $false
        } else {
            [void]$out.AppendLine("  `${OrIf} `${SectionIsSelected} `${$secId}")
        }
    }
    [void]$out.AppendLine('    StrCpy $STOP_ES "1"')
    [void]$out.AppendLine('    StrCpy $STOP_SC "1"')
    [void]$out.AppendLine('  ${EndIf}')
}

[void]$out.AppendLine('!macroend')
[void]$out.AppendLine('')

# ── Macro: PluginUninstallCleanup ──
[void]$out.AppendLine('!macro PluginUninstallCleanup')
foreach ($p in $plugins) {
    $installDir = Get-InstallDir $p
    [void]$out.AppendLine("  RMDir /r `"`$INSTDIR\$installDir\$($p.name)`"")
}
[void]$out.AppendLine('')
foreach ($p in $plugins) {
    [void]$out.AppendLine("  DeleteRegKey HKLM `"Software\Microsoft\Windows\CurrentVersion\Uninstall\$($p.name)`"")
}
[void]$out.AppendLine('!macroend')

$out.ToString() | Set-Content $OutputPath -Encoding UTF8
Write-Host "Generated: $OutputPath"
