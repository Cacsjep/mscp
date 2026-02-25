; MSCPlugins Unified NSIS Installer Script
; Installs Milestone XProtect Community Plugins & Drivers

!include "MUI2.nsh"
!include "nsDialogs.nsh"
!include "LogicLib.nsh"
!include "Sections.nsh"

; ── Version (passed via /DVERSION=x.x on the command line) ──
!ifndef VERSION
  !define VERSION "0.0"
!endif

; ── Output directory (passed via /DOUTDIR= on the command line) ──
!ifndef OUTDIR
  !define OUTDIR "."
!endif

; ── General ──
Name "MSC Plugins v${VERSION}"
OutFile "${OUTDIR}\MSCPlugins-v${VERSION}-Setup.exe"
InstallDir "$PROGRAMFILES64\Milestone"
RequestExecutionLevel admin
BrandingText "MSC Community Plugins v${VERSION}"

; ── Staging directories (passed via /D on the command line) ──
!ifndef WEATHER_DIR
  !define WEATHER_DIR "..\build\staging\Weather"
!endif
!ifndef RDP_DIR
  !define RDP_DIR "..\build\staging\RDP"
!endif
!ifndef RTMPDRIVER_DIR
  !define RTMPDRIVER_DIR "..\build\staging\RTMPDriver"
!endif
!ifndef RTMPSTREAMER_DIR
  !define RTMPSTREAMER_DIR "..\build\staging\RtmpStreamer"
!endif

; ── Process / service names ──
!define SC_PROCESS "Client.exe"
!define RS_SERVICE "Milestone XProtect Recording Server"
!define ES_SERVICE "MilestoneEventServerService"

; ── MUI Settings ──
!define MUI_ICON "${NSISDIR}\Contrib\Graphics\Icons\modern-install.ico"
!define MUI_UNICON "${NSISDIR}\Contrib\Graphics\Icons\modern-uninstall.ico"
!define MUI_ABORTWARNING

!define MUI_WELCOMEPAGE_TEXT "This installer will install the selected Milestone XProtect$\u2122 community plugins and drivers.$\r$\n$\r$\nRunning services will be stopped as needed and restarted after installation.$\r$\n$\r$\nClick Next to continue."

; ── Pages ──
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "license.txt"
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

; ── Language ──
!insertmacro MUI_LANGUAGE "English"

; ══════════════════════════════════════════════════════════════
; Smart Client Plugins
; ══════════════════════════════════════════════════════════════

SectionGroup "Smart Client Plugins" SEC_SC_GROUP

  Section "Weather Plugin" SEC_WEATHER
    DetailPrint "Closing Smart Client..."
    nsExec::ExecToLog 'taskkill /F /IM "${SC_PROCESS}"'
    Pop $0
    Sleep 2000

    SetOutPath "$INSTDIR\MIPPlugins\Weather"
    DetailPrint "Installing Weather Plugin..."
    File /r "${WEATHER_DIR}\*.*"

    ; Registry
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Weather" \
      "DisplayName" "Weather Plugin v${VERSION}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Weather" \
      "UninstallString" "$\"$INSTDIR\MIPPlugins\Weather\Uninstall.exe$\""
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Weather" \
      "DisplayVersion" "${VERSION}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Weather" \
      "Publisher" "MSC Community Plugins"
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Weather" \
      "NoModify" 1
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Weather" \
      "NoRepair" 1
  SectionEnd

  Section "RDP Plugin" SEC_RDP
    DetailPrint "Closing Smart Client..."
    nsExec::ExecToLog 'taskkill /F /IM "${SC_PROCESS}"'
    Pop $0
    Sleep 2000

    SetOutPath "$INSTDIR\MIPPlugins\RDP"
    DetailPrint "Installing RDP Plugin..."
    File /r "${RDP_DIR}\*.*"

    ; Registry
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RDP" \
      "DisplayName" "RDP Plugin v${VERSION}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RDP" \
      "UninstallString" "$\"$INSTDIR\MIPPlugins\RDP\Uninstall.exe$\""
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RDP" \
      "DisplayVersion" "${VERSION}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RDP" \
      "Publisher" "MSC Community Plugins"
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RDP" \
      "NoModify" 1
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RDP" \
      "NoRepair" 1
  SectionEnd

SectionGroupEnd

; ══════════════════════════════════════════════════════════════
; Device Drivers
; ══════════════════════════════════════════════════════════════

SectionGroup "Device Drivers" SEC_DD_GROUP

  Section "RTMP Push Driver" SEC_RTMPDRIVER
    DetailPrint "Stopping ${RS_SERVICE}..."
    nsExec::ExecToLog 'net stop "${RS_SERVICE}"'
    Pop $0
    Sleep 2000

    SetOutPath "$INSTDIR\MIPDrivers\RTMPDriver"
    DetailPrint "Installing RTMP Push Driver..."
    File /r "${RTMPDRIVER_DIR}\*.*"

    ; Registry
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RTMPDriver" \
      "DisplayName" "RTMPDriver v${VERSION}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RTMPDriver" \
      "UninstallString" "$\"$INSTDIR\MIPDrivers\RTMPDriver\Uninstall.exe$\""
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RTMPDriver" \
      "DisplayVersion" "${VERSION}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RTMPDriver" \
      "Publisher" "MSC Community Plugins"
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RTMPDriver" \
      "NoModify" 1
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RTMPDriver" \
      "NoRepair" 1

    DetailPrint "Starting ${RS_SERVICE}..."
    nsExec::ExecToLog 'net start "${RS_SERVICE}"'
    Pop $0
  SectionEnd

SectionGroupEnd

; ══════════════════════════════════════════════════════════════
; Admin Plugins
; ══════════════════════════════════════════════════════════════

SectionGroup "Admin Plugins" SEC_AP_GROUP

  Section "RTMP Streamer Plugin" SEC_RTMPSTREAMER
    DetailPrint "Stopping ${ES_SERVICE}..."
    nsExec::ExecToLog 'net stop "${ES_SERVICE}"'
    Pop $0
    Sleep 2000

    SetOutPath "$INSTDIR\MIPPlugins\RtmpStreamer"
    DetailPrint "Installing RTMP Streamer Plugin..."
    File /r "${RTMPSTREAMER_DIR}\*.*"

    ; Registry
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RtmpStreamer" \
      "DisplayName" "RtmpStreamer v${VERSION}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RtmpStreamer" \
      "UninstallString" "$\"$INSTDIR\MIPPlugins\RtmpStreamer\Uninstall.exe$\""
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RtmpStreamer" \
      "DisplayVersion" "${VERSION}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RtmpStreamer" \
      "Publisher" "MSC Community Plugins"
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RtmpStreamer" \
      "NoModify" 1
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RtmpStreamer" \
      "NoRepair" 1

    DetailPrint "Starting ${ES_SERVICE}..."
    nsExec::ExecToLog 'net start "${ES_SERVICE}"'
    Pop $0
  SectionEnd

SectionGroupEnd

; ══════════════════════════════════════════════════════════════
; Uninstaller
; ══════════════════════════════════════════════════════════════

Section "-WriteUninstaller"
  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\MSCPlugins-Uninstall.exe"

  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\MSCPlugins" \
    "DisplayName" "MSC Community Plugins v${VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\MSCPlugins" \
    "UninstallString" "$\"$INSTDIR\MSCPlugins-Uninstall.exe$\""
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\MSCPlugins" \
    "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\MSCPlugins" \
    "Publisher" "MSC Community Plugins"
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\MSCPlugins" \
    "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\MSCPlugins" \
    "NoRepair" 1
SectionEnd

; ── Component descriptions ──
!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_SC_GROUP}    "Plugins for the XProtect$\u2122 Smart Client"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_WEATHER}     "Display live weather in Smart Client view items (Open-Meteo)"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_RDP}         "Embed interactive RDP sessions in Smart Client view items"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_DD_GROUP}    "Device drivers for the XProtect$\u2122 Recording Server"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_RTMPDRIVER}  "Receive RTMP push streams (H.264) directly into XProtect$\u2122"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_AP_GROUP}    "Plugins for the XProtect$\u2122 Management Client / Event Server"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_RTMPSTREAMER} "Stream XProtect$\u2122 cameras to RTMP destinations (YouTube, Twitch, etc.)"
!insertmacro MUI_FUNCTION_DESCRIPTION_END

; ── Uninstall Section ──
Section "Uninstall"
  ; Stop services
  DetailPrint "Stopping services..."
  nsExec::ExecToLog 'taskkill /F /IM "${SC_PROCESS}"'
  Pop $0
  nsExec::ExecToLog 'net stop "${RS_SERVICE}"'
  Pop $0
  nsExec::ExecToLog 'net stop "${ES_SERVICE}"'
  Pop $0
  Sleep 2000

  ; Remove plugin directories
  RMDir /r "$INSTDIR\MIPPlugins\Weather"
  RMDir /r "$INSTDIR\MIPPlugins\RDP"
  RMDir /r "$INSTDIR\MIPDrivers\RTMPDriver"
  RMDir /r "$INSTDIR\MIPPlugins\RtmpStreamer"

  ; Remove uninstaller
  Delete "$INSTDIR\MSCPlugins-Uninstall.exe"

  ; Remove registry entries
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\MSCPlugins"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Weather"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RDP"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RTMPDriver"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RtmpStreamer"

  ; Restart services
  DetailPrint "Starting services..."
  nsExec::ExecToLog 'net start "${RS_SERVICE}"'
  Pop $0
  nsExec::ExecToLog 'net start "${ES_SERVICE}"'
  Pop $0

  DetailPrint "Uninstallation complete."
SectionEnd
