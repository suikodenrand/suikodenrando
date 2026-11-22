Unicode true
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 32
ShowInstDetails show
ManifestDPIAware true

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "Sections.nsh"
!include "nsDialogs.nsh"
!include "x64.nsh"

!ifndef PACKAGE_DIR
  !error "PACKAGE_DIR must name the verified full package staging directory."
!endif
!ifndef OUTPUT_EXE
  !error "OUTPUT_EXE must name the full installer output."
!endif
!ifndef DISTRIBUTION_VERSION
  !define DISTRIBUTION_VERSION "0.14.6-r3"
!endif

!define GAME_EXE "Suikoden I and II HD Remaster.exe"
!define DEFAULT_GAME_DIR "$PROGRAMFILES32\Steam\steamapps\common\Suikoden I and II HD Remaster"
Name "Stars of Destiny ${DISTRIBUTION_VERSION}"
OutFile "${OUTPUT_EXE}"
VIProductVersion "0.14.6.0"
VIAddVersionKey /LANG=1033 "ProductName" "Stars of Destiny"
VIAddVersionKey /LANG=1033 "FileDescription" "Stars of Destiny ${DISTRIBUTION_VERSION} Full Setup"
VIAddVersionKey /LANG=1033 "ProductVersion" "0.14.6"
VIAddVersionKey /LANG=1033 "FileVersion" "0.14.6.0"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Stars of Destiny project contributors"
VIAddVersionKey /LANG=1033 "CompanyName" "Stars of Destiny"

Var IncludeAtb
Var CheckOnly
Var ResultCode
Var Parameters
Var Mode
Var KeepConfig
Var RepairChoice
Var UninstallChoice
Var KeepConfigChoice

!define MUI_CUSTOMFUNCTION_GUIINIT SetComponentDefaults
!define MUI_WELCOMEPAGE_TEXT "Install Stars of Destiny for Suikoden I and II HD Remaster, or repair or uninstall an existing copy.$\r$\n$\r$\nClose the game, launcher and CTB editor first.$\r$\n$\r$\nFirst modded launch needs internet to generate BepInEx interop files."
!insertmacro MUI_PAGE_WELCOME
!define MUI_DIRECTORYPAGE_TEXT_TOP "Choose the game folder containing ${GAME_EXE} and GameAssembly.dll."
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE ValidateDirectory
!insertmacro MUI_PAGE_DIRECTORY
Page custom MaintenancePage LeaveMaintenancePage
!define MUI_COMPONENTSPAGE_TEXT_TOP "The randomizer and launcher are required. The S1 battle preview is optional and starts disabled when installed."
!define MUI_COMPONENTSPAGE_SMALLDESC
!define MUI_PAGE_CUSTOMFUNCTION_PRE SkipComponentsForMaintenance
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_TEXT "Stars of Destiny is ready.$\r$\n$\r$\nUse the launcher to choose settings. First modded launch can take several minutes and needs internet access."
!define MUI_FINISHPAGE_RUN "$INSTDIR\StarsOfDestiny Launcher\StarsOfDestiny.Launcher.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Open Stars of Destiny Launcher"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!define MUI_PAGE_CUSTOMFUNCTION_PRE SkipFinishForUninstall
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP|MB_OK "This package requires 64-bit Windows." /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
  StrCpy $IncludeAtb "0"
  StrCpy $CheckOnly "0"
  StrCpy $Mode "Install"
  StrCpy $KeepConfig "1"
  ${GetParameters} $Parameters
  ClearErrors
  ${GetOptions} "$Parameters" "/ATB=" $0
  ${IfNot} ${Errors}
    ${If} $0 == "1"
      StrCpy $IncludeAtb "1"
    ${ElseIf} $0 != "0"
      MessageBox MB_ICONSTOP|MB_OK "Use /ATB=0 or /ATB=1." /SD IDOK
      SetErrorLevel 1
      Abort
    ${EndIf}
  ${EndIf}
  ClearErrors
  ${GetOptions} "$Parameters" "/CHECKONLY=" $0
  ${IfNot} ${Errors}
    ${If} $0 == "1"
      StrCpy $CheckOnly "1"
    ${ElseIf} $0 != "0"
      MessageBox MB_ICONSTOP|MB_OK "Use /CHECKONLY=0 or /CHECKONLY=1." /SD IDOK
      SetErrorLevel 1
      Abort
    ${EndIf}
  ${EndIf}
  IfSilent silent_options
  ; An explicit /D sets $INSTDIR before .onInit and takes precedence.
  StrCmp $INSTDIR "" 0 init_done
  SetRegView 64
  ReadRegStr $0 HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1932640" "InstallLocation"
  ${If} $0 == ""
    SetRegView 32
    ReadRegStr $0 HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1932640" "InstallLocation"
  ${EndIf}
  ${If} $0 != ""
    IfFileExists "$0\${GAME_EXE}" 0 default_folder
    StrCpy $INSTDIR "$0"
    Goto init_done
  ${EndIf}
  Goto default_folder
  silent_options:
  ClearErrors
  ${GetOptions} "$Parameters" "/MODE=" $0
  ${IfNot} ${Errors}
    ${If} $0 == "repair"
      StrCpy $Mode "Repair"
    ${ElseIf} $0 == "uninstall"
      StrCpy $Mode "Uninstall"
    ${ElseIf} $0 != "install"
      MessageBox MB_ICONSTOP|MB_OK "Use /MODE=install, /MODE=repair or /MODE=uninstall." /SD IDOK
      SetErrorLevel 1
      Abort
    ${EndIf}
  ${EndIf}
  ClearErrors
  ${GetOptions} "$Parameters" "/KEEPCONFIG=" $0
  ${IfNot} ${Errors}
    ${If} $0 != "0"
    ${AndIf} $0 != "1"
      MessageBox MB_ICONSTOP|MB_OK "Use /KEEPCONFIG=0 or /KEEPCONFIG=1." /SD IDOK
      SetErrorLevel 1
      Abort
    ${EndIf}
    StrCpy $KeepConfig $0
  ${EndIf}
  ${If} $INSTDIR == ""
  ${AndIf} $Mode != "Install"
    MessageBox MB_ICONSTOP|MB_OK "Silent repair or uninstall requires an explicit /D= game folder." /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
  StrCmp $INSTDIR "" 0 init_done
  default_folder:
  StrCpy $INSTDIR "${DEFAULT_GAME_DIR}"
  init_done:
FunctionEnd

Function ValidateDirectory
  IfFileExists "$INSTDIR\${GAME_EXE}" 0 invalid_directory
  IfFileExists "$INSTDIR\GameAssembly.dll" directory_valid invalid_directory
  invalid_directory:
  MessageBox MB_ICONEXCLAMATION|MB_OK "Choose the game folder containing ${GAME_EXE} and GameAssembly.dll." /SD IDOK
  SetErrorLevel 1
  Abort
  directory_valid:
FunctionEnd

Function MaintenancePage
  StrCpy $Mode "Install"
  IfFileExists "$INSTDIR\StarsOfDestiny Setup\Install.ps1" installed
  IfFileExists "$INSTDIR\StarsOfDestiny Launcher\StarsOfDestiny.Launcher.exe" installed
  IfFileExists "$INSTDIR\BepInEx\plugins\SuikodenRandomizer\SuikodenRandomizer.dll" installed
  IfFileExists "$INSTDIR\BepInEx\plugins\SuikodenRandomizer\SuikodenRandomizer.Suikoden2.dll" installed
  IfFileExists "$INSTDIR\BepInEx\plugins\SuikodenRandomizer.S1BattlePreview.dll" installed
  IfFileExists "$INSTDIR\BepInEx\SuikodenRandomizer.S1BattlePreview.dll" installed
  Abort
  installed:
  !insertmacro MUI_HEADER_TEXT "Maintain Stars of Destiny" "Choose what to do with this installation."
  nsDialogs::Create 1018
  Pop $0
  StrCmp $0 error 0 +2
  Abort
  ${NSD_CreateRadioButton} 0 0 100% 14u "&Repair"
  Pop $RepairChoice
  ${NSD_AddStyle} $RepairChoice ${WS_GROUP}
  ${NSD_Check} $RepairChoice
  ${NSD_OnClick} $RepairChoice UpdateMaintenanceChoice
  ${NSD_CreateLabel} 12u 18u 96% 26u "Replace missing or damaged installed files. Keep settings and saved campaigns."
  Pop $0
  ${NSD_CreateRadioButton} 0 51u 100% 14u "&Uninstall"
  Pop $UninstallChoice
  ${NSD_OnClick} $UninstallChoice UpdateMaintenanceChoice
  ${NSD_CreateCheckbox} 12u 71u 96% 14u "&Keep configuration and campaign bindings"
  Pop $KeepConfigChoice
  ${NSD_AddStyle} $KeepConfigChoice ${WS_GROUP}
  ${NSD_Check} $KeepConfigChoice
  EnableWindow $KeepConfigChoice 0
  ${NSD_CreateLabel} 12u 91u 96% 38u "Uninstall removes Stars of Destiny and its loader if no other mods use it. Keep configurations to resume existing randomized saves after reinstalling."
  Pop $0
  nsDialogs::Show
FunctionEnd

Function UpdateMaintenanceChoice
  Pop $0
  ${NSD_GetState} $UninstallChoice $0
  EnableWindow $KeepConfigChoice $0
FunctionEnd

Function LeaveMaintenancePage
  ${NSD_GetState} $UninstallChoice $0
  ${If} $0 != ${BST_CHECKED}
    StrCpy $Mode "Repair"
    Return
  ${EndIf}
  ${NSD_GetState} $KeepConfigChoice $KeepConfig
  StrCpy $0 "Your configuration and campaign bindings will be kept."
  ${If} $KeepConfig == "0"
    StrCpy $0 "Your configuration and campaign bindings will be deleted. Existing randomized saves may need their original bindings."
  ${EndIf}
  MessageBox MB_ICONEXCLAMATION|MB_YESNO|MB_DEFBUTTON2 "Uninstall Stars of Destiny from this folder?$\r$\n$INSTDIR$\r$\n$\r$\n$0" IDYES confirmed
  Abort
  confirmed:
  StrCpy $Mode "Uninstall"
FunctionEnd

Function SkipComponentsForMaintenance
  ${If} $Mode != "Install"
    Abort
  ${EndIf}
FunctionEnd

Function SkipFinishForUninstall
  ${If} $Mode == "Uninstall"
    Abort
  ${EndIf}
FunctionEnd

Section "Randomizer, launcher, BepInEx and Suikoden Fix" SEC_REQUIRED
  SectionIn RO
SectionEnd

Section /o "Experimental S1 ATB / CTB (installed disabled)" SEC_ATB
SectionEnd

Function SetComponentDefaults
  ${If} $IncludeAtb == "1"
    !insertmacro SelectSection ${SEC_ATB}
  ${EndIf}
FunctionEnd

Section "-Maintain Stars of Destiny"
  Call ValidateDirectory
  IfSilent flags_ready
  ${If} $Mode == "Install"
    SectionGetFlags ${SEC_ATB} $0
    IntOp $0 $0 & ${SF_SELECTED}
    StrCpy $IncludeAtb $0
  ${EndIf}
  flags_ready:
  InitPluginsDir
  SetOutPath "$PLUGINSDIR\FullPackage"
  ClearErrors
  File /r "${PACKAGE_DIR}\*"
  ${If} ${Errors}
    SetErrorLevel 1
    MessageBox MB_ICONSTOP|MB_OK "Setup could not extract the package. Check free space in your temporary folder." /SD IDOK
    Abort
  ${EndIf}
  StrCpy $0 ""
  ${If} $IncludeAtb == "1"
    StrCpy $0 "-IncludeAtb"
  ${EndIf}
  StrCpy $1 ""
  ${If} $CheckOnly == "1"
    StrCpy $1 "-CheckOnly"
  ${EndIf}
  StrCpy $2 ""
  ${If} $Mode == "Uninstall"
    ${If} $KeepConfig == "0"
      StrCpy $2 "-RemoveConfig"
    ${EndIf}
    IfSilent +2
    StrCpy $2 "$2 -RemoveShortcuts"
  ${EndIf}
  DetailPrint "$Mode in $INSTDIR"
  ; The child owns the shared installer mutex and executable lock. Holding the
  ; same mutex here would make the child reject its own NSIS wrapper.
  nsExec::ExecToLog /OEM '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\FullPackage\Install.ps1" -GamePath "$INSTDIR" -Mode $Mode -NonInteractive $0 $1 $2'
  Pop $ResultCode
  ${If} $ResultCode != "0"
    DetailPrint "$Mode stopped with exit code $ResultCode. See the details above."
    ${If} $Mode == "Uninstall"
      MessageBox MB_ICONSTOP|MB_OK "Uninstall stopped. See the setup details for the reason. You can run setup again to finish removal." /SD IDOK
    ${Else}
      MessageBox MB_ICONSTOP|MB_OK "Setup stopped. See the details for the reason. Recovery copies of replaced files are in SRR-Backups." /SD IDOK
    ${EndIf}
    SetErrorLevel 1
    Abort
  ${EndIf}
  ${If} $CheckOnly == "1"
    DetailPrint "Preflight passed. No game files were changed."
    SetErrorLevel 0
    Quit
  ${EndIf}
  ; Shortcuts are conveniences, outside the file transaction. Silent installs
  ; avoid writing current-user shell folders during automation.
  IfSilent no_shortcuts
  StrCmp $Mode "Uninstall" no_shortcuts
  SetShellVarContext current
  CreateDirectory "$SMPROGRAMS\Stars of Destiny"
  CreateShortcut "$SMPROGRAMS\Stars of Destiny\Stars of Destiny Launcher.lnk" "$INSTDIR\StarsOfDestiny Launcher\StarsOfDestiny.Launcher.exe"
  CreateShortcut "$SMPROGRAMS\Stars of Destiny\Setup and recovery.lnk" "$INSTDIR\StarsOfDestiny Setup"
  no_shortcuts:
  SetErrorLevel 0
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_REQUIRED} "Combined 0.14.6 randomizer, self-contained Windows launcher, BepInEx 6 loader and Suikoden Fix. Saves and existing settings are preserved."
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_ATB} "Adds ATB and the CTB 0.5.2 preview for S1. Both start disabled. Choose your battle mode in the launcher after disabling shuffled S1 Unites."
!insertmacro MUI_FUNCTION_DESCRIPTION_END
