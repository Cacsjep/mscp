; MSCPlugins Unified NSIS Installer Script
; Installs Milestone XProtect™ Community Plugins & Drivers
;
; Plugin sections are auto-generated from plugins.json.
; Run: pwsh installer/generate-nsi.ps1

!include "MUI2.nsh"
!include "nsDialogs.nsh"
!include "LogicLib.nsh"
!include "Sections.nsh"

; ── Generated plugin definitions (staging dirs, sections, descriptions) ──
!include "plugin-generated.nsi"

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

; ── Staging directory defines (from plugins.json) ──
!insertmacro PluginStagingDefines

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
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE ComponentsLeave
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

; ── Language ──
!insertmacro MUI_LANGUAGE "English"

; ══════════════════════════════════════════════════════════════
; .onInit -- set safe defaults (critical for silent /S mode)
; ══════════════════════════════════════════════════════════════
Function .onInit
  StrCpy $STOP_SC "1"
  StrCpy $STOP_RS "1"
  StrCpy $STOP_ES "1"
FunctionEnd

; ══════════════════════════════════════════════════════════════
; Pre-install: Stop services/processes based on selected components
; ══════════════════════════════════════════════════════════════
Section "-StopServices"

  !insertmacro _LogOpen

  StrCpy $RS_WAS_RUNNING "0"
  StrCpy $ES_WAS_RUNNING "0"

  !insertmacro _LogMsg "STOP_SC=$STOP_SC  STOP_RS=$STOP_RS  STOP_ES=$STOP_ES"

  ${If} $STOP_SC == "1"
    !insertmacro _LogMsg "Closing Smart Client (${SC_PROCESS})..."
    nsExec::ExecToLog 'taskkill /F /IM "${SC_PROCESS}"'
    Pop $0
    !insertmacro _LogMsg "taskkill Smart Client exit code: $0"
    Sleep 2000
  ${EndIf}

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
; Plugin Sections (auto-generated from plugins.json)
; ══════════════════════════════════════════════════════════════

SectionGroup "Smart Client Plugins" SEC_SC_GROUP
  !insertmacro PluginSmartClientSections
SectionGroupEnd

SectionGroup "Device Drivers" SEC_DD_GROUP
  !insertmacro PluginDeviceDriverSections
SectionGroupEnd

SectionGroup "Admin Plugins" SEC_AP_GROUP
  !insertmacro PluginAdminPluginSections
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

; ── Component descriptions (auto-generated from plugins.json) ──
!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_SC_GROUP}  "Plugins for the XProtect™ Smart Client"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_DD_GROUP}  "Device drivers for the XProtect™ Recording Server"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_AP_GROUP}  "Plugins for the XProtect™ Management Client / Event Server"
  !insertmacro PluginDescriptions
!insertmacro MUI_FUNCTION_DESCRIPTION_END

; ══════════════════════════════════════════════════════════════
; ComponentsLeave callback (auto-generated from plugins.json)
; ══════════════════════════════════════════════════════════════
Function ComponentsLeave
  StrCpy $STOP_SC "0"
  StrCpy $STOP_RS "0"
  StrCpy $STOP_ES "0"
  !insertmacro PluginComponentsLeave
FunctionEnd

; ══════════════════════════════════════════════════════════════
; Uninstall Section
; ══════════════════════════════════════════════════════════════
Section "Uninstall"
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

  ; ── Remove plugin directories + registry (auto-generated) ──
  !insertmacro PluginUninstallCleanup

  ; ── Remove uninstaller ──
  Delete "$INSTDIR\MSCPlugins-Uninstall.exe"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\MSCPlugins"

  ; ── Restart services ──
  DetailPrint "Starting ${RS_SERVICE}..."
  nsExec::ExecToLog 'net start "${RS_SERVICE}"'
  Pop $0
  DetailPrint "Starting ${ES_SERVICE}..."
  nsExec::ExecToLog 'net start "${ES_SERVICE}"'
  Pop $0

  DetailPrint "Uninstallation complete."
SectionEnd
