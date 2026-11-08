Unicode true
RequestExecutionLevel user
SetCompressor /SOLID zlib
ShowInstDetails show

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"

!ifndef RUNTIME_DIR
  !error "Specify RUNTIME_DIR with the two production Release DLLs."
!endif
!ifndef OUTPUT_EXE
  !define OUTPUT_EXE "StarsOfDestiny-Setup.exe"
!endif
!ifndef PRODUCT_VERSION
  !define PRODUCT_VERSION "0.8.11"
!endif
!ifndef GAME_SHA256
  !define GAME_SHA256 "e093f175eaf3734d293ca0a236c652dd7fcc72ea5fa10d7b21103edf12f11c3a"
!endif

!define PRODUCT "Stars of Destiny - Character Randomizer"
!define GAME "Suikoden I and II HD Remaster"
!define GAME_EXE "${GAME}.exe"
!define CFG "agilen.suikoden.recruitment_randomizer.cfg"
!define PLUGIN "SuikodenRandomizer.dll"
!define CORE "SuikodenRandomizer.Core.dll"
!define PLUGIN_DIR "$INSTDIR\BepInEx\plugins\SuikodenRandomizer"
!define CONFIG_DIR "$INSTDIR\BepInEx\config"
!define BACKUP_ROOT "$INSTDIR\SRR-Backups"

Name "${PRODUCT}"
OutFile "${OUTPUT_EXE}"
InstallDir "$PROGRAMFILES32\Steam\steamapps\common\${GAME}"
VIProductVersion "${PRODUCT_VERSION}.0"
VIAddVersionKey /LANG=1033 "ProductName" "${PRODUCT}"
VIAddVersionKey /LANG=1033 "FileDescription" "${PRODUCT} Setup"
VIAddVersionKey /LANG=1033 "ProductVersion" "${PRODUCT_VERSION}"
VIAddVersionKey /LANG=1033 "FileVersion" "${PRODUCT_VERSION}.0"
VIAddVersionKey /LANG=1033 "CompanyName" "Stars of Destiny - Character Randomizer"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Randomizer by the Stars of Destiny project contributors."

!define MUI_DIRECTORYPAGE_TEXT_TOP "Close the game. Select the folder containing ${GAME_EXE} and BepInEx."
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE CheckDirectoryPage
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Var BackupDir
Var PluginInstalled
Var CoreInstalled
Var ConfigInstalled
Var RollbackFailed
Var Failure

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP|MB_OK "This randomizer requires 64-bit Windows." /SD IDOK
    Abort
  ${EndIf}
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Local\StarsOfDestinyInstaller") p.r0 ?e'
  Pop $1
  ${If} $0 == 0
  ${OrIf} $1 == 183 ; ERROR_ALREADY_EXISTS
    MessageBox MB_ICONSTOP|MB_OK "Close the other Stars of Destiny installer first." /SD IDOK
    Abort
  ${EndIf}

  IfFileExists "$EXEDIR\${CFG}" config_present
    MessageBox MB_ICONSTOP|MB_OK "Extract the entire download into one folder, then open StarsOfDestiny-Setup.exe there." /SD IDOK
    Abort
  config_present:
  SetRegView 64
  ReadRegStr $0 HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1932640" "InstallLocation"
  SetRegView 32
  ${If} $0 == ""
    ReadRegStr $0 HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1932640" "InstallLocation"
  ${EndIf}
  ${If} $0 != ""
    IfFileExists "$0\${GAME_EXE}" 0 +2
      StrCpy $INSTDIR "$0"
  ${EndIf}
FunctionEnd

Function CheckDirectoryPage
  Call CheckDirectory
  ${If} $Failure != ""
    MessageBox MB_ICONEXCLAMATION|MB_OK "$Failure"
    Abort
  ${EndIf}
FunctionEnd

Function CheckDirectory
  StrCpy $Failure "Choose the Suikoden I and II HD Remaster game folder."
  IfFileExists "$INSTDIR\${GAME_EXE}" 0 directory_done
  IfFileExists "$INSTDIR\GameAssembly.dll" 0 directory_done

  StrCpy $Failure "Install BepInEx 6 IL2CPP, start the game once, then close it before running this setup."
  IfFileExists "$INSTDIR\BepInEx\core\BepInEx.Unity.IL2CPP.dll" 0 directory_done
  IfFileExists "$INSTDIR\BepInEx\core\Il2CppInterop.Runtime.dll" 0 directory_done
  IfFileExists "$INSTDIR\BepInEx\interop\GSD1.dll" 0 directory_done

  StrCpy $Failure "An older randomizer DLL is directly under BepInEx\plugins. Move that copy out of the plugins folder before installing."
  IfFileExists "$INSTDIR\BepInEx\plugins\${PLUGIN}" directory_done
  IfFileExists "$INSTDIR\BepInEx\plugins\${CORE}" directory_done
  StrCpy $Failure ""
  directory_done:
FunctionEnd

; Use Windows CryptoAPI to check the game version before touching installed files.
; Only the supported game binary is read. No process or network access is required.
Function VerifyGameHash
  StrCpy $Failure "This game version could not be verified. No mod files were changed."
  StrCpy $1 0 ; provider
  StrCpy $2 0 ; SHA-256 handle
  StrCpy $3 -1 ; open file
  StrCpy $4 0 ; reusable buffer

  ; PROV_RSA_AES, CRYPT_VERIFYCONTEXT, CALG_SHA_256
  System::Call 'advapi32::CryptAcquireContextW(*p .r1, p 0, p 0, i 24, i 0xF0000000)i.r0'
  ${If} $0 = 0
    Goto hash_cleanup
  ${EndIf}
  System::Call 'advapi32::CryptCreateHash(p r1, i 0x800c, p 0, i 0, *p .r2)i.r0'
  ${If} $0 = 0
    Goto hash_cleanup
  ${EndIf}
  System::Call 'kernel32::CreateFileW(w "$INSTDIR\GameAssembly.dll", i 0x80000000, i 1, p 0, i 3, i 0x08000000, p 0)p.r3'
  ${If} $3 P= -1
    Goto hash_cleanup
  ${EndIf}
  System::Alloc 65536
  Pop $4
  ${If} $4 P= 0
    Goto hash_cleanup
  ${EndIf}
  hash_read:
  System::Call 'kernel32::ReadFile(p r3, p r4, i 65536, *i .r5, p 0)i.r0'
  ${If} $0 = 0
    Goto hash_cleanup
  ${EndIf}
  ${If} $5 > 0
    System::Call 'advapi32::CryptHashData(p r2, p r4, i r5, i 0)i.r0'
    ${If} $0 = 0
      Goto hash_cleanup
    ${EndIf}
    Goto hash_read
  ${EndIf}

  ; HP_HASHVAL, CRYPT_STRING_HEXRAW | CRYPT_STRING_NOCRLF
  System::Call 'advapi32::CryptGetHashParam(p r2, i 2, p r4, *i 32, i 0)i.r0'
  ${If} $0 = 0
    Goto hash_cleanup
  ${EndIf}
  System::Call 'crypt32::CryptBinaryToStringW(p r4, i 32, i 0x4000000c, w .r6, *i 65)i.r0'
  ${If} $0 = 0
    Goto hash_cleanup
  ${EndIf}
  ${If} $6 == "${GAME_SHA256}"
    StrCpy $Failure ""
  ${Else}
    StrCpy $Failure "This game version is not supported. No mod files were changed."
  ${EndIf}

  hash_cleanup:
  ${If} $4 P<> 0
    System::Free $4
  ${EndIf}
  ${If} $3 P<> -1
    System::Call 'kernel32::CloseHandle(p r3)'
  ${EndIf}
  ${If} $2 P<> 0
    System::Call 'advapi32::CryptDestroyHash(p r2)'
  ${EndIf}
  ${If} $1 P<> 0
    System::Call 'advapi32::CryptReleaseContext(p r1, i 0)'
  ${EndIf}
FunctionEnd

!macro SaveOriginal DIR NAME
  IfFileExists "${DIR}\${NAME}" 0 +4
    ClearErrors
    Rename "${DIR}\${NAME}" "$BackupDir\${NAME}"
    IfErrors rollback
!macroend

!macro InstallFile DIR NAME FLAG
  ClearErrors
  Rename "$BackupDir\new\${NAME}" "${DIR}\${NAME}"
  IfErrors rollback
  StrCpy ${FLAG} "1"
!macroend

!macro RestoreOriginal DIR NAME FLAG
  ${If} ${FLAG} == "1"
    ClearErrors
    Delete "${DIR}\${NAME}"
    ${If} ${Errors}
      StrCpy $RollbackFailed "1"
    ${EndIf}
  ${EndIf}
  IfFileExists "$BackupDir\${NAME}" 0 +5
    ClearErrors
    Rename "$BackupDir\${NAME}" "${DIR}\${NAME}"
    IfErrors 0 +2
      StrCpy $RollbackFailed "1"
!macroend

Section "Install"
  Call CheckDirectory
  ${If} $Failure != ""
    Goto failure
  ${EndIf}
  DetailPrint "Verifying the game version..."
  Call VerifyGameHash
  ${If} $Failure != ""
    Goto failure
  ${EndIf}

  StrCpy $Failure "Setup needs write access to the selected game folder."
  StrCpy $PluginInstalled "0"
  StrCpy $CoreInstalled "0"
  StrCpy $ConfigInstalled "0"
  StrCpy $RollbackFailed "0"
  ClearErrors
  CreateDirectory "${BACKUP_ROOT}"
  IfErrors failure
  GetTempFileName $0 "${BACKUP_ROOT}"
  IfErrors failure
  Delete "$0"
  IfErrors failure
  CreateDirectory "$0\new"
  IfErrors failure
  StrCpy $BackupDir "$0"
  SetOutPath "$BackupDir\new"
  IfErrors discard_prepared
  File "/oname=${PLUGIN}" "${RUNTIME_DIR}/${PLUGIN}"
  IfErrors discard_prepared
  File "/oname=${CORE}" "${RUNTIME_DIR}/${CORE}"
  IfErrors discard_prepared
  System::Call 'kernel32::CopyFileW(w "$EXEDIR\${CFG}", w "$BackupDir\new\${CFG}", i 1)i.r0'
  ${If} $0 = 0
    StrCpy $Failure "Setup could not copy the selected configuration. Extract the complete download and try again."
    Goto discard_prepared
  ${EndIf}
  SetOutPath "$INSTDIR"
  ClearErrors
  CreateDirectory "${PLUGIN_DIR}"
  IfErrors discard_prepared
  CreateDirectory "${CONFIG_DIR}"
  IfErrors discard_prepared

  DetailPrint "Backing up existing mod files..."
  !insertmacro SaveOriginal "${PLUGIN_DIR}" "${PLUGIN}"
  !insertmacro SaveOriginal "${PLUGIN_DIR}" "${CORE}"
  !insertmacro SaveOriginal "${CONFIG_DIR}" "${CFG}"

  DetailPrint "Installing your selected configuration and precompiled mod..."
  !insertmacro InstallFile "${PLUGIN_DIR}" "${PLUGIN}" $PluginInstalled
  !insertmacro InstallFile "${PLUGIN_DIR}" "${CORE}" $CoreInstalled
  !insertmacro InstallFile "${CONFIG_DIR}" "${CFG}" $ConfigInstalled
  RMDir "$BackupDir\new"
  IfFileExists "$BackupDir\${PLUGIN}" show_backup
  IfFileExists "$BackupDir\${CORE}" show_backup
  IfFileExists "$BackupDir\${CFG}" show_backup
  RMDir "$BackupDir"
  Goto success

  show_backup:
  DetailPrint "Previous files backed up in $BackupDir"
  success:
  DetailPrint "Installed. Start a New Game to use your selected settings."
  Goto done

  rollback:
  DetailPrint "Restoring previous mod files..."
  !insertmacro RestoreOriginal "${CONFIG_DIR}" "${CFG}" $ConfigInstalled
  !insertmacro RestoreOriginal "${PLUGIN_DIR}" "${CORE}" $CoreInstalled
  !insertmacro RestoreOriginal "${PLUGIN_DIR}" "${PLUGIN}" $PluginInstalled
  ${If} $RollbackFailed == "1"
    StrCpy $Failure "Setup could not complete. Previous files are in $BackupDir."
    Goto failure
  ${EndIf}
  StrCpy $Failure "Setup could not replace the files. Close the game and check that the game folder is writable."

  discard_prepared:
  SetOutPath "$INSTDIR"
  Delete "$BackupDir\new\${PLUGIN}"
  Delete "$BackupDir\new\${CORE}"
  Delete "$BackupDir\new\${CFG}"
  RMDir "$BackupDir\new"
  RMDir "$BackupDir"
  failure:
  SetErrorLevel 1
  MessageBox MB_ICONSTOP|MB_OK "$Failure" /SD IDOK
  Abort
  done:
SectionEnd
