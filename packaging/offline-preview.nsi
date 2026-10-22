; SPDX-License-Identifier: GPL-3.0-or-later
; Compile-only local review candidate. No upgrade/rollback qualification is implied.
Unicode true
!include "MUI2.nsh"
!include "x64.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "${GeneratedConfig}"
Name "Rilmo Recorder (development preview)"
OutFile "${InstallerFile}"
RequestExecutionLevel admin
InstallDir "$PROGRAMFILES64\Rilmo Recorder"
; zlib avoids the LZMA decoder and its additional license in this candidate.
SetCompressor zlib
CRCCheck force
AllowSkipFiles off
ShowInstDetails show
ShowUninstDetails show
BrandingText "Rilmo Recorder - local development preview"
VIProductVersion "${ProductFileVersion}"
VIAddVersionKey /LANG=1033 "ProductName" "Rilmo Recorder"
VIAddVersionKey /LANG=1033 "ProductVersion" "${ProductVersion}"
VIAddVersionKey /LANG=1033 "FileVersion" "${ProductFileVersion}"
VIAddVersionKey /LANG=1033 "FileDescription" "Rilmo Recorder offline development installer"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Rilmo Recorder contributors; GPL-3.0-or-later source; NSIS notices included"
!define MUI_ABORTWARNING
!define MUI_ICON "${ProductIcon}"
!define MUI_UNICON "${ProductIcon}"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${LicenseFile}"
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
!define UninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\Rilmo.Recorder.Desktop"
Var SetupMutex

; Reject links in a path and all ancestors. Missing components are allowed;
; access-denied and other errors are not treated as an absent path.
!macro DefinePathCheck Prefix
Function ${Prefix}PathIsSafe
  Exch $7
  Push $8
  Push $9
  Push $6
  StrCpy $6 1
  pathLoop:
    System::Call 'kernel32::GetFileAttributesW(w r7) i .r8 ?e'
    Pop $9
    ${If} $8 == -1
      ${If} $9 != 2
      ${AndIf} $9 != 3
        StrCpy $6 0
        Goto pathDone
      ${EndIf}
    ${Else}
      IntOp $8 $8 & 0x400
      ${If} $8 != 0
        StrCpy $6 0
        Goto pathDone
      ${EndIf}
    ${EndIf}
    ${GetParent} "$7" $8
    StrCmp $8 "" pathDone
    StrCmp $8 $7 pathDone
    StrCpy $7 $8
    Goto pathLoop
  pathDone:
  StrCpy $7 $6
  Pop $6
  Pop $9
  Pop $8
  Exch $7
FunctionEnd
!macroend
!insertmacro DefinePathCheck ""
!insertmacro DefinePathCheck "un."

!macro CheckOwnedPath Path Failure Prefix
  Push "${Path}"
  Call ${Prefix}PathIsSafe
  Pop $8
  StrCmp $8 1 +2
    Goto ${Failure}
!macroend

!macro CheckIdle
  System::Call 'kernel32::OpenMutexW(i 0x00100000, i 0, w "Local\RilmoRecorder.Desktop.v2") p .r0'
  ${If} $0 P<> 0
    System::Call 'kernel32::CloseHandle(p r0)'
    MessageBox MB_OK|MB_ICONINFORMATION "Close Rilmo Recorder using Exit in its tray menu, then retry setup. Active recordings are left running." /SD IDOK
    SetErrorLevel 1618
    Quit
  ${EndIf}
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Global\RilmoRecorder.Desktop.Setup.v1") p .r0 ?e'
  Pop $1
  ${If} $0 P= 0
  ${OrIf} $1 == 183
    ${If} $0 P<> 0
      System::Call 'kernel32::CloseHandle(p r0)'
    ${EndIf}
    MessageBox MB_OK|MB_ICONINFORMATION "Another Rilmo setup or uninstall is running, or the setup lock could not be acquired. Retry after it closes." /SD IDOK
    SetErrorLevel 1618
    Quit
  ${EndIf}
  StrCpy $SetupMutex $0
!macroend

!macro CheckEmptyDirectory Path Failure Label
  IfFileExists "${Path}\*" 0 ${Label}
  FindFirst $0 $1 "${Path}\*"
  IfErrors ${Failure}
  ${Do}
    ${If} $1 != "."
    ${AndIf} $1 != ".."
    ${AndIf} $1 != ""
      FindClose $0
      Goto ${Failure}
    ${EndIf}
    ClearErrors
    FindNext $0 $1
    ${If} ${Errors}
      ${ExitDo}
    ${EndIf}
  ${Loop}
  FindClose $0
  ${Label}:
!macroend

!macro WriteRemovalEntry Failure
  ClearErrors
  WriteRegStr HKLM "${UninstallKey}" "DisplayName" "Rilmo Recorder (development preview)"
  WriteRegStr HKLM "${UninstallKey}" "DisplayVersion" "${ProductVersion}"
  WriteRegStr HKLM "${UninstallKey}" "Publisher" "Rilmo Recorder project"
  WriteRegStr HKLM "${UninstallKey}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UninstallKey}" "DisplayIcon" "$INSTDIR\Rilmo Recorder.exe"
  WriteRegStr HKLM "${UninstallKey}" "UninstallString" '$\"$INSTDIR\Uninstall Rilmo.exe$\"'
  WriteRegStr HKLM "${UninstallKey}" "QuietUninstallString" '$\"$INSTDIR\Uninstall Rilmo.exe$\" /S'
  WriteRegDWORD HKLM "${UninstallKey}" "NoModify" 1
  WriteRegDWORD HKLM "${UninstallKey}" "NoRepair" 1
  WriteRegDWORD HKLM "${UninstallKey}" "EstimatedSize" ${PayloadKiB}
  IfErrors ${Failure}
!macroend

!macro DeleteOptionalFile Path Failure Label
  IfFileExists "${Path}" 0 ${Label}
  ClearErrors
  Delete "${Path}"
  IfErrors ${Failure}
  ${Label}:
!macroend

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "Rilmo Recorder requires 64-bit Windows 10 or Windows 11." /SD IDOK
    SetErrorLevel 1633
    Quit
  ${EndIf}
  SetRegView 64
  ClearErrors
  ReadRegStr $0 HKLM "SOFTWARE\Microsoft\Windows NT\CurrentVersion" "CurrentBuildNumber"
  IfErrors unsupported
  ${If} $0 < 19041
    Goto unsupported
  ${EndIf}
  StrCpy $INSTDIR "$PROGRAMFILES64\Rilmo Recorder"
  SetShellVarContext all
  !insertmacro CheckIdle
  !insertmacro CheckOwnedPath "$INSTDIR" unsafePath ""
  !insertmacro CheckOwnedPath "$SMPROGRAMS\Rilmo Recorder" unsafePath ""
  ReadRegStr $0 HKLM "${UninstallKey}" "InstallLocation"
  StrCmp $0 "" +2
    Goto existingInstall
  !insertmacro CheckEmptyDirectory "$INSTDIR" existingInstall appEmpty
  !insertmacro CheckEmptyDirectory "$SMPROGRAMS\Rilmo Recorder" existingInstall menuEmpty
  Return
  unsupported:
    MessageBox MB_OK|MB_ICONSTOP "Rilmo Recorder requires Windows build 19041 or later." /SD IDOK
    SetErrorLevel 1633
    Quit
  unsafePath:
    MessageBox MB_OK|MB_ICONSTOP "An installation path is a link or cannot be safely inspected. Setup has not copied files." /SD IDOK
    SetErrorLevel 1603
    Quit
  existingInstall:
    MessageBox MB_OK|MB_ICONINFORMATION "An existing installation, partial installation, or Start menu folder is present. Uninstall the existing Rilmo preview first. This candidate does not perform an in-place upgrade." /SD IDOK
    SetErrorLevel 1638
    Quit
FunctionEnd

Section "Recorder"
  SectionIn RO
  ClearErrors
  SetOutPath "$INSTDIR"
  IfErrors installFailed
  WriteUninstaller "$INSTDIR\Uninstall Rilmo.exe"
  IfErrors installFailed
  !insertmacro WriteRemovalEntry installFailed
  SetOutPath "$INSTDIR\_installer"
  IfErrors installFailed
  File /oname=payload-manifest.json "${PayloadManifest}"
  File /oname=NSIS-COPYING.txt "${NsisLicenseFile}"
  IfErrors installFailed
  !define PathCheckPrefix ""
  !define PathCheckFailure installFailed
  !include "${PathChecks}"
  !undef PathCheckPrefix
  !undef PathCheckFailure
  !include "${InstallFiles}"
  ClearErrors
  CreateDirectory "$SMPROGRAMS\Rilmo Recorder"
  CreateShortcut "$SMPROGRAMS\Rilmo Recorder\Rilmo Recorder.lnk" "$INSTDIR\Rilmo Recorder.exe"
  CreateShortcut "$SMPROGRAMS\Rilmo Recorder\Uninstall.lnk" "$INSTDIR\Uninstall Rilmo.exe"
  IfErrors installFailed
  ; Camera registration is an explicit app action, never an install side effect.
  SetErrorLevel 0
  Goto installed
  installFailed:
    MessageBox MB_OK|MB_ICONSTOP "Setup could not finish. If files were copied, use Uninstall Rilmo.exe in the installation folder to remove the partial installation, then retry. Setup has not registered a camera." /SD IDOK
    SetErrorLevel 1603
    Abort
  installed:
SectionEnd

Function un.onInit
  SetRegView 64
  SetShellVarContext all
  GetFullPathName $0 "$INSTDIR"
  StrCmp $0 "$PROGRAMFILES64\Rilmo Recorder" +4
    MessageBox MB_OK|MB_ICONSTOP "The installation path is unexpected. No files were removed." /SD IDOK
    SetErrorLevel 1603
    Quit
  !insertmacro CheckIdle
FunctionEnd

Section "Uninstall"
  !define PathCheckPrefix "un."
  !define PathCheckFailure uninstallFailed
  !include "${PathChecks}"
  !undef PathCheckPrefix
  !undef PathCheckFailure
  !insertmacro CheckOwnedPath "$INSTDIR\_installer\payload-manifest.json" uninstallFailed "un."
  !insertmacro CheckOwnedPath "$INSTDIR\_installer\NSIS-COPYING.txt" uninstallFailed "un."
  !insertmacro CheckOwnedPath "$INSTDIR\Uninstall Rilmo.exe" uninstallFailed "un."
  !insertmacro CheckOwnedPath "$SMPROGRAMS\Rilmo Recorder\Rilmo Recorder.lnk" uninstallFailed "un."
  !insertmacro CheckOwnedPath "$SMPROGRAMS\Rilmo Recorder\Uninstall.lnk" uninstallFailed "un."
  ReadRegStr $0 HKLM "SOFTWARE\Classes\CLSID\{9E7FF7BC-AA7A-4B1D-8A9E-DDDFB0692DFD}\InprocServer32" ""
  SetRegView 32
  ReadRegStr $1 HKLM "SOFTWARE\Classes\CLSID\{9E7FF7BC-AA7A-4B1D-8A9E-DDDFB0692DFD}\InprocServer32" ""
  SetRegView 64
  ${If} $0 != ""
  ${OrIf} $1 != ""
    Goto removeCamera
  ${EndIf}
  IfFileExists "$PROGRAMFILES64\Rilmo\Virtual Camera\installation.json" removeCamera afterCamera
  removeCamera:
    ; NSIS is x86; Sysnative deliberately invokes x64 system PowerShell.
    IfFileExists "$INSTDIR\tools\setup-virtual-camera.ps1" +2
      Goto cameraFailed
    ClearErrors
    StrCpy $0 -1
    ExecWait '$\"$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe$\" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $\"$INSTDIR\tools\setup-virtual-camera.ps1$\" -Action Uninstall -AcknowledgeSystemDeviceChange' $0
    IfErrors cameraFailed
    ${If} $0 != 0
      Goto cameraFailed
    ${EndIf}
    IfFileExists "$PROGRAMFILES64\Rilmo\Virtual Camera\installation.json" cameraFailed afterCamera
  cameraFailed:
    MessageBox MB_OK|MB_ICONSTOP "Camera cleanup could not finish. Close apps using Rilmo Virtual Camera, then retry uninstall. The recorder and cleanup tool have been kept." /SD IDOK
    SetErrorLevel 1603
    Abort
  afterCamera:
  SetOutPath "$TEMP"
  !include "${UninstallFiles}"
  ; Keep the cleanup tool until every ordinary payload deletion has succeeded.
  !include "${UninstallSupportFiles}"
  !insertmacro DeleteOptionalFile "$SMPROGRAMS\Rilmo Recorder\Rilmo Recorder.lnk" uninstallFailed appShortcutRemoved
  !insertmacro DeleteOptionalFile "$SMPROGRAMS\Rilmo Recorder\Uninstall.lnk" uninstallFailed removeShortcutRemoved
  RMDir "$SMPROGRAMS\Rilmo Recorder"
  !insertmacro DeleteOptionalFile "$INSTDIR\_installer\NSIS-COPYING.txt" uninstallFailed licenseRemoved
  !insertmacro DeleteOptionalFile "$INSTDIR\_installer\payload-manifest.json" uninstallFailed manifestRemoved
  RMDir "$INSTDIR\_installer"
  ClearErrors
  DeleteRegKey HKLM "${UninstallKey}"
  IfErrors uninstallFailed
  ClearErrors
  Delete "$INSTDIR\Uninstall Rilmo.exe"
  IfErrors restoreRemovalEntry
  RMDir "$INSTDIR"
  ; Nonempty directories, custom files, recordings and preferences are preserved.
  SetErrorLevel 0
  Goto removed
  restoreRemovalEntry:
    !insertmacro WriteRemovalEntry uninstallFailed
  uninstallFailed:
    MessageBox MB_OK|MB_ICONSTOP "Removal could not finish. A path may be linked, inaccessible, or in use. Close Rilmo and camera apps, then retry the retained uninstaller. Custom files are not removed." /SD IDOK
    SetErrorLevel 1603
    Abort
  removed:
SectionEnd
