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
!include "x64.nsh"

!ifndef PACKAGE_DIR
  !error "PACKAGE_DIR must name the verified full package staging directory."
!endif
!ifndef OUTPUT_EXE
  !error "OUTPUT_EXE must name the full installer output."
!endif

!define GAME_EXE "Suikoden I and II HD Remaster.exe"
Name "Stars of Destiny 0.14.6"
OutFile "${OUTPUT_EXE}"
InstallDir "$PROGRAMFILES32\Steam\steamapps\common\Suikoden I and II HD Remaster"
VIProductVersion "0.14.6.0"
VIAddVersionKey /LANG=1033 "ProductName" "Stars of Destiny"
VIAddVersionKey /LANG=1033 "FileDescription" "Stars of Destiny 0.14.6 Full Setup"
VIAddVersionKey /LANG=1033 "ProductVersion" "0.14.6"
VIAddVersionKey /LANG=1033 "FileVersion" "0.14.6.0"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Stars of Destiny project contributors"
VIAddVersionKey /LANG=1033 "CompanyName" "Stars of Destiny"

Var IncludeAtb
Var CheckOnly
Var ResultCode
Var Parameters

!define MUI_CUSTOMFUNCTION_GUIINIT SetComponentDefaults
!define MUI_WELCOMEPAGE_TEXT "Install the combined Suikoden I and II randomizer, desktop launcher, BepInEx and Suikoden Fix.$\r$\n$\r$\nClose the game and launcher first. Existing saves and settings are preserved. The experimental S1 ATB / CTB component is optional.$\r$\n$\r$\nA legal Steam copy of Suikoden I and II HD Remaster is required. First launch needs internet to generate BepInEx interop files."
!insertmacro MUI_PAGE_WELCOME
!define MUI_COMPONENTSPAGE_TEXT_TOP "The randomizer and launcher are required. The S1 battle preview is optional and starts disabled when installed."
!define MUI_COMPONENTSPAGE_SMALLDESC
!insertmacro MUI_PAGE_COMPONENTS
!define MUI_DIRECTORYPAGE_TEXT_TOP "Choose the game folder containing ${GAME_EXE} and GameAssembly.dll. Setup verifies the supported game build before changing files."
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE ValidateDirectory
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_TEXT "Stars of Destiny 0.14.6 is installed.$\r$\n$\r$\nUse the launcher to choose settings. Fresh chest shuffling and ATB are off, and the text-only F6 panel starts hidden.$\r$\n$\r$\nFirst launch may need several minutes and an internet connection. Installation receipts and recovery copies are in the game's SRR-Backups folder."
!define MUI_FINISHPAGE_RUN "$INSTDIR\StarsOfDestiny Launcher\StarsOfDestiny.Launcher.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Open Stars of Destiny Launcher"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
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
  ; Explicit /D and silent mode always keep their target. Registry discovery
  ; must never redirect an automated fixture or a user's requested directory.
  IfSilent init_done
  ClearErrors
  ${GetOptions} "$Parameters" "/D=" $0
  ${IfNot} ${Errors}
    Goto init_done
  ${EndIf}
  SetRegView 64
  ReadRegStr $0 HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1932640" "InstallLocation"
  ${If} $0 == ""
    SetRegView 32
    ReadRegStr $0 HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1932640" "InstallLocation"
  ${EndIf}
  ${If} $0 != ""
    IfFileExists "$0\${GAME_EXE}" 0 init_done
    StrCpy $INSTDIR "$0"
  ${EndIf}
  init_done:
FunctionEnd

Function ValidateDirectory
  IfFileExists "$INSTDIR\${GAME_EXE}" 0 invalid_directory
  IfFileExists "$INSTDIR\GameAssembly.dll" directory_valid invalid_directory
  invalid_directory:
  MessageBox MB_ICONEXCLAMATION|MB_OK "Choose the game folder containing ${GAME_EXE} and GameAssembly.dll."
  Abort
  directory_valid:
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

Section "-Install full package"
  IfSilent flags_ready
  SectionGetFlags ${SEC_ATB} $0
  IntOp $0 $0 & ${SF_SELECTED}
  StrCpy $IncludeAtb $0
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
  DetailPrint "Verifying package and game. Existing files are backed up before replacement..."
  ; The child owns the shared installer mutex and executable lock. Holding the
  ; same mutex here would make the child reject its own NSIS wrapper.
  nsExec::ExecToLog /OEM '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\FullPackage\Install.ps1" -GamePath "$INSTDIR" -NonInteractive $0 $1'
  Pop $ResultCode
  ${If} $ResultCode != "0"
    DetailPrint "Installation stopped with exit code $ResultCode. See the details above."
    MessageBox MB_ICONSTOP|MB_OK "Installation stopped. See the setup details for the reason. If replacement began, setup attempted to restore the previous files; any recovery copies remain in SRR-Backups." /SD IDOK
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
