Unicode True
RequestExecutionLevel user
ManifestDPIAware true
Name "Twin Shift · Vedal AI"
OutFile "${OUTPUT}\TwinShift-1.1.0-beta.1-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\TwinShift"
Icon "${STAGE}\assets\twinshift.ico"
SetCompressor /SOLID lzma
!include MUI2.nsh
!include FileFunc.nsh
!include LogicLib.nsh
!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"
!define UNKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\TwinShift"
Var Portable
Function .onInit
  SetShellVarContext current
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} "$0" "/PORTABLE" $1
  ${IfNot} ${Errors}
    StrCpy $Portable 1
  ${EndIf}
  IfFileExists "$INSTDIR\*.*" 0 fresh
  IfFileExists "$INSTDIR\twinshift.install" owned 0
  MessageBox MB_OK|MB_ICONSTOP "安装目录已有其他文件，请使用空目录。" /SD IDOK
  SetErrorLevel 4
  Abort
owned:
  IfFileExists "$INSTDIR\portable.install" 0 +2
  StrCpy $Portable 1
  ExecWait '"$INSTDIR\TwinShift.exe" --setup-check' $0
  ${If} $0 != 0
    MessageBox MB_OK "请从系统托盘退出Vedal AI，再继续安装。" /SD IDOK
    SetErrorLevel 3
    Abort
  ${EndIf}
fresh:
FunctionEnd
Section
  SetOutPath "$INSTDIR"
  File /r "${STAGE}\*.*"
  FileOpen $0 "$INSTDIR\twinshift.install" w
  FileWrite $0 "TwinShift 1.1.0-beta.1"
  FileClose $0
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  ${If} $Portable == 1
    FileOpen $0 "$INSTDIR\portable.install" w
    FileClose $0
  ${Else}
    CreateShortcut "$SMPROGRAMS\Twin Shift.lnk" "$INSTDIR\TwinShift.exe"
    WriteRegStr HKCU "${UNKEY}" "DisplayName" "Twin Shift · Vedal AI"
    WriteRegStr HKCU "${UNKEY}" "Publisher" "Twin Shift contributors"
    WriteRegStr HKCU "${UNKEY}" "DisplayVersion" "1.1.0-beta.1"
    WriteRegStr HKCU "${UNKEY}" "DisplayIcon" "$INSTDIR\TwinShift.exe"
    WriteRegStr HKCU "${UNKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegStr HKCU "${UNKEY}" "InstallLocation" "$INSTDIR"
  ${EndIf}
SectionEnd
Section "Uninstall"
  SetShellVarContext current
  IfFileExists "$INSTDIR\twinshift.install" +3 0
  SetErrorLevel 4
  Abort
  ExecWait '"$INSTDIR\TwinShift.exe" --uninstall-check' $0
  ${If} $0 != 0
    MessageBox MB_OK "请退出托盘程序，并先在接入页移除已启用的插件，再重试卸载。" /SD IDOK
    SetErrorLevel 3
    Abort
  ${EndIf}
  IfFileExists "$INSTDIR\portable.install" +3 0
  Delete "$SMPROGRAMS\Twin Shift.lnk"
  DeleteRegKey HKCU "${UNKEY}"
  !include "${OUTPUT}\uninstall-files.nsh"
  Delete "$INSTDIR\portable.install"
  Delete "$INSTDIR\twinshift.install"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
SectionEnd
