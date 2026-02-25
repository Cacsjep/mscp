; MSCPlugins Unified NSIS Installer Script
; Installs Milestone XProtect™ Community Plugins & Drivers

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

; ── Log file location ──
!define LOG_FILE "$TEMP\MSCPlugins-install.log"

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
  !define RTMPSTREAMER_DIR "..\build\staging\RTMPStreamer"
!endif

; ── Process / service names ──
!define SC_PROCESS  "Client.exe"
!define MC_PROCESS  "VideoOS.Platform.Administration.exe"
!define DFP_PROCESS "VideoOS.IO.Drivers.DriverFrameworkProcess.exe"
!define RS_SERVICE  "Milestone XProtect Recording Server"
!define ES_SERVICE  "MilestoneEventServerService"

; ── Variables ──
Var RS_WAS_RUNNING       ; "1" if Recording Server was running before install
Var ES_WAS_RUNNING       ; "1" if Event Server was running before install
Var STOP_SC              ; "1" if we need to close Smart Client
Var STOP_RS              ; "1" if we need to stop Recording Server
Var STOP_ES              ; "1" if we need to stop Event Server + Management Client
Var LOG_HANDLE           ; file handle for install log

; ══════════════════════════════════════════════════════════════
; Logging macros -- write to %TEMP%\MSCPlugins-install.log
; ══════════════════════════════════════════════════════════════
!macro _LogOpen
  FileOpen $LOG_HANDLE "${LOG_FILE}" w
  !insertmacro _LogMsg "MSCPlugins installer v${VERSION} started"
!macroend

!macro _LogMsg MSG
  ${If} $LOG_HANDLE != ""
    FileWrite $LOG_HANDLE "${MSG}$\r$\n"
  ${EndIf}
  DetailPrint "${MSG}"
!macroend

!macro _LogClose
  !insertmacro _LogMsg "Installer finished."
  FileClose $LOG_HANDLE
  StrCpy $LOG_HANDLE ""
!macroend

; ══════════════════════════════════════════════════════════════
; Process / service check macros
; ══════════════════════════════════════════════════════════════

; Check if a process is running
;   ${RESULT_VAR} = "1" if running, "0" if not
!macro _CheckProcessRunning PROC_NAME RESULT_VAR
  nsExec::ExecToStack 'cmd /c tasklist /FI "IMAGENAME eq ${PROC_NAME}" /FO CSV /NH 2>nul | findstr /I /C:"${PROC_NAME}"'
  Pop ${RESULT_VAR}
  Pop $9
  ${If} ${RESULT_VAR} == 0
    StrCpy ${RESULT_VAR} "1"
  ${Else}
    StrCpy ${RESULT_VAR} "0"
  ${EndIf}
!macroend

; Check if a Windows service is running
;   ${RESULT_VAR} = "1" if running, "0" if not / not installed
!macro _CheckServiceRunning SVC_NAME RESULT_VAR
  nsExec::ExecToStack 'cmd /c sc query "${SVC_NAME}" 2>nul | findstr /C:"RUNNING"'
  Pop ${RESULT_VAR}
  Pop $9
  ${If} ${RESULT_VAR} == 0
    StrCpy ${RESULT_VAR} "1"
  ${Else}
    StrCpy ${RESULT_VAR} "0"
  ${EndIf}
!macroend

; ── MUI Settings ──
!define MUI_ICON "${NSISDIR}\Contrib\Graphics\Icons\modern-install.ico"
!define MUI_UNICON "${NSISDIR}\Contrib\Graphics\Icons\modern-uninstall.ico"
!define MUI_ABORTWARNING

!define MUI_WELCOMEPAGE_TEXT "This installer will install the selected Milestone XProtect™ community plugins and drivers.$\r$\n$\r$\nDepending on the components you select, the installer may need to stop running Milestone services and applications. They will be restarted automatically after installation.$\r$\n$\r$\nClick Next to continue."

; ── Pages ──
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "license.txt"
; ComponentsLeave callback captures which services need to be stopped
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE ComponentsLeave
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

; ── Language ──
!insertmacro MUI_LANGUAGE "English"

; ══════════════════════════════════════════════════════════════
; .onInit -- set safe defaults (critical for silent /S mode
;   where page callbacks never run)
; ══════════════════════════════════════════════════════════════
Function .onInit
  ; Default: stop everything. ComponentsLeave will narrow this
  ; down in interactive mode based on the actual selection.
  StrCpy $STOP_SC "1"
  StrCpy $STOP_RS "1"
  StrCpy $STOP_ES "1"
FunctionEnd

; ══════════════════════════════════════════════════════════════
; Pre-install: Stop services/processes based on selected components
; (hidden section -- always runs first)
; ══════════════════════════════════════════════════════════════
Section "-StopServices"

  !insertmacro _LogOpen

  StrCpy $RS_WAS_RUNNING "0"
  StrCpy $ES_WAS_RUNNING "0"

  !insertmacro _LogMsg "STOP_SC=$STOP_SC  STOP_RS=$STOP_RS  STOP_ES=$STOP_ES"

  ; ── Smart Client plugins selected → close Smart Client ──
  ${If} $STOP_SC == "1"
    !insertmacro _LogMsg "Closing Smart Client (${SC_PROCESS})..."
    nsExec::ExecToLog 'taskkill /F /IM "${SC_PROCESS}"'
    Pop $0
    !insertmacro _LogMsg "taskkill Smart Client exit code: $0"
    Sleep 2000
  ${EndIf}

  ; ── Device Drivers selected → stop Recording Server ──
  ${If} $STOP_RS == "1"
    !insertmacro _CheckServiceRunning "${RS_SERVICE}" $RS_WAS_RUNNING
    !insertmacro _LogMsg "Recording Server was running: $RS_WAS_RUNNING"
    ${If} $RS_WAS_RUNNING == "1"
      !insertmacro _LogMsg "Stopping ${RS_SERVICE}..."
      nsExec::ExecToLog 'net stop "${RS_SERVICE}" /y'
      Pop $0
      !insertmacro _LogMsg "net stop Recording Server exit code: $0"
    ${EndIf}
  ${EndIf}

  ; ── Admin Plugins selected → close Management Client + stop Event Server ──
  ${If} $STOP_ES == "1"
    !insertmacro _LogMsg "Closing Management Client (${MC_PROCESS})..."
    nsExec::ExecToLog 'taskkill /F /IM "${MC_PROCESS}"'
    Pop $0
    !insertmacro _LogMsg "taskkill Management Client exit code: $0"

    !insertmacro _CheckServiceRunning "${ES_SERVICE}" $ES_WAS_RUNNING
    !insertmacro _LogMsg "Event Server was running: $ES_WAS_RUNNING"
    ${If} $ES_WAS_RUNNING == "1"
      !insertmacro _LogMsg "Stopping ${ES_SERVICE}..."
      nsExec::ExecToLog 'net stop "${ES_SERVICE}" /y'
      Pop $0
      !insertmacro _LogMsg "net stop Event Server exit code: $0"
      Sleep 2000
    ${EndIf}
  ${EndIf}

SectionEnd

; ══════════════════════════════════════════════════════════════
; Smart Client Plugins
; ══════════════════════════════════════════════════════════════

SectionGroup "Smart Client Plugins" SEC_SC_GROUP

  Section "Weather Plugin" SEC_WEATHER
    SetOutPath "$INSTDIR\MIPPlugins\Weather"
    !insertmacro _LogMsg "Installing Weather Plugin to $INSTDIR\MIPPlugins\Weather..."
    File /r "${WEATHER_DIR}\*.*"
    !insertmacro _LogMsg "Weather Plugin installed."

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
    SetOutPath "$INSTDIR\MIPPlugins\RDP"
    !insertmacro _LogMsg "Installing RDP Plugin to $INSTDIR\MIPPlugins\RDP..."
    File /r "${RDP_DIR}\*.*"
    !insertmacro _LogMsg "RDP Plugin installed."

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
    ; ── Gate: ensure DriverFrameworkProcess is not locking driver DLLs ──
    _dfp_check:
    !insertmacro _CheckProcessRunning "${DFP_PROCESS}" $R1
    !insertmacro _LogMsg "DriverFrameworkProcess running: $R1"

    ${If} $R1 == "1"
      MessageBox MB_YESNO|MB_ICONEXCLAMATION \
        "${DFP_PROCESS} is still running and will lock driver files.$\r$\n$\r$\nKill the process now?" \
        IDNO _dfp_skip

      ; — Yes: kill the process —
      !insertmacro _LogMsg "Killing ${DFP_PROCESS}..."
      nsExec::ExecToLog 'taskkill /F /IM "${DFP_PROCESS}"'
      Pop $0
      !insertmacro _LogMsg "taskkill ${DFP_PROCESS} exit code: $0"
      Sleep 3000
      Goto _dfp_check

      _dfp_skip:
        !insertmacro _LogMsg "User skipped killing ${DFP_PROCESS}."
    ${EndIf}

    SetOutPath "$INSTDIR\MIPDrivers\RTMPDriver"
    !insertmacro _LogMsg "Installing RTMP Push Driver to $INSTDIR\MIPDrivers\RTMPDriver..."
    File /r "${RTMPDRIVER_DIR}\*.*"
    !insertmacro _LogMsg "RTMP Push Driver installed."

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
  SectionEnd

SectionGroupEnd

; ══════════════════════════════════════════════════════════════
; Admin Plugins
; ══════════════════════════════════════════════════════════════

SectionGroup "Admin Plugins" SEC_AP_GROUP

  Section "RTMP Streamer Plugin" SEC_RTMPSTREAMER
    SetOutPath "$INSTDIR\MIPPlugins\RTMPStreamer"
    !insertmacro _LogMsg "Installing RTMP Streamer Plugin to $INSTDIR\MIPPlugins\RTMPStreamer..."
    File /r "${RTMPSTREAMER_DIR}\*.*"
    !insertmacro _LogMsg "RTMP Streamer Plugin installed."

    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RTMPStreamer" \
      "DisplayName" "RTMPStreamer v${VERSION}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RTMPStreamer" \
      "UninstallString" "$\"$INSTDIR\MIPPlugins\RTMPStreamer\Uninstall.exe$\""
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RTMPStreamer" \
      "DisplayVersion" "${VERSION}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RTMPStreamer" \
      "Publisher" "MSC Community Plugins"
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RTMPStreamer" \
      "NoModify" 1
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RTMPStreamer" \
      "NoRepair" 1
  SectionEnd

SectionGroupEnd

; ══════════════════════════════════════════════════════════════
; Uninstaller & service restart
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

; ── Post-install: restart only the services we stopped ──
Section "-RestartServices"
  ${If} $STOP_RS == "1"
  ${AndIf} $RS_WAS_RUNNING == "1"
    !insertmacro _LogMsg "Starting ${RS_SERVICE}..."
    nsExec::ExecToLog 'net start "${RS_SERVICE}"'
    Pop $0
    !insertmacro _LogMsg "net start Recording Server exit code: $0"
  ${EndIf}

  ${If} $STOP_ES == "1"
  ${AndIf} $ES_WAS_RUNNING == "1"
    !insertmacro _LogMsg "Starting ${ES_SERVICE}..."
    nsExec::ExecToLog 'net start "${ES_SERVICE}"'
    Pop $0
    !insertmacro _LogMsg "net start Event Server exit code: $0"
  ${EndIf}

  !insertmacro _LogClose
SectionEnd

; ── Component descriptions ──
!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_SC_GROUP}    "Plugins for the XProtect™ Smart Client"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_WEATHER}     "Display live weather in Smart Client view items (Open-Meteo)"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_RDP}         "Embed interactive RDP sessions in Smart Client view items"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_DD_GROUP}    "Device drivers for the XProtect™ Recording Server"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_RTMPDRIVER}  "Receive RTMP push streams (H.264) directly into XProtect™"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_AP_GROUP}    "Plugins for the XProtect™ Management Client / Event Server"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_RTMPSTREAMER} "Stream XProtect™ cameras to RTMP destinations (YouTube, Twitch, etc.)"
!insertmacro MUI_FUNCTION_DESCRIPTION_END

; ══════════════════════════════════════════════════════════════
; ComponentsLeave callback
;   Runs when the user clicks Next on the Components page (interactive only).
;   Narrows down $STOP_* from the "stop all" default set in .onInit.
; ══════════════════════════════════════════════════════════════
Function ComponentsLeave
  StrCpy $STOP_SC "0"
  StrCpy $STOP_RS "0"
  StrCpy $STOP_ES "0"

  ; Smart Client plugins → need to close Smart Client
  ${If} ${SectionIsSelected} ${SEC_WEATHER}
  ${OrIf} ${SectionIsSelected} ${SEC_RDP}
    StrCpy $STOP_SC "1"
  ${EndIf}

  ; Device Drivers → need to stop Recording Server + wait for DFP
  ${If} ${SectionIsSelected} ${SEC_RTMPDRIVER}
    StrCpy $STOP_RS "1"
  ${EndIf}

  ; Admin Plugins → need to stop Event Server + close Management Client
  ${If} ${SectionIsSelected} ${SEC_RTMPSTREAMER}
    StrCpy $STOP_ES "1"
  ${EndIf}
FunctionEnd

; ══════════════════════════════════════════════════════════════
; Uninstall Section
; ══════════════════════════════════════════════════════════════
Section "Uninstall"
  ; ── Stop everything ──
  DetailPrint "Closing XProtect™ Smart Client..."
  nsExec::ExecToLog 'taskkill /F /IM "${SC_PROCESS}"'
  Pop $0
  DetailPrint "Closing XProtect™ Management Client..."
  nsExec::ExecToLog 'taskkill /F /IM "${MC_PROCESS}"'
  Pop $0
  DetailPrint "Stopping ${RS_SERVICE}..."
  nsExec::ExecToLog 'net stop "${RS_SERVICE}" /y'
  Pop $0
  DetailPrint "Stopping ${ES_SERVICE}..."
  nsExec::ExecToLog 'net stop "${ES_SERVICE}" /y'
  Pop $0
  Sleep 5000

  ; ── Remove plugin directories ──
  RMDir /r "$INSTDIR\MIPPlugins\Weather"
  RMDir /r "$INSTDIR\MIPPlugins\RDP"
  RMDir /r "$INSTDIR\MIPDrivers\RTMPDriver"
  RMDir /r "$INSTDIR\MIPPlugins\RTMPStreamer"

  ; ── Remove uninstaller ──
  Delete "$INSTDIR\MSCPlugins-Uninstall.exe"

  ; ── Remove registry entries ──
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\MSCPlugins"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Weather"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RDP"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RTMPDriver"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RTMPStreamer"

  ; ── Restart services ──
  DetailPrint "Starting ${RS_SERVICE}..."
  nsExec::ExecToLog 'net start "${RS_SERVICE}"'
  Pop $0
  DetailPrint "Starting ${ES_SERVICE}..."
  nsExec::ExecToLog 'net start "${ES_SERVICE}"'
  Pop $0

  DetailPrint "Uninstallation complete."
SectionEnd
