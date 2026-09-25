; PeekShield Windows installer (NSIS MUI2)
Unicode True
SetCompressor lzma

!include "MUI2.nsh"
!include "WinVer.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "nsDialogs.nsh"

!define APPNAME "PeekShield"
!define APPVERSION "1.2.2.0"
!define PUBLISHER "yty16"
!define EXENAME "PeekShield.exe"
!define APPDIR "$LOCALAPPDATA\Programs\${APPNAME}"
!define DATADIR "$LOCALAPPDATA\PeekShield"
!define PROJECTROOT "${__FILEDIR__}\.."

Name "${APPNAME} ${APPVERSION}"
OutFile "${PROJECTROOT}\installer\PeekShield-1.2.2.0-win-x64-setup.exe"
InstallDir "${APPDIR}"
RequestExecutionLevel user

!define MUI_ABORTWARNING
!define MUI_ICON "${PROJECTROOT}\PeekShield\Resources\icon.ico"
!define MUI_UNICON "${PROJECTROOT}\PeekShield\Resources\icon.ico"

; ---------- Install pages ----------
!define MUI_WELCOMEPAGE_TITLE "欢迎使用 ${APPNAME} ${APPVERSION} 安装向导"
!define MUI_WELCOMEPAGE_TEXT "本向导将引导您完成 ${APPNAME} 的安装。$\r$\n$\r$\n${APPNAME} 是一款本地运行的桌面防偷窥工具，通过本机摄像头进行人脸检测与偷窥判定。点击$\"下一步$\"继续。"
!insertmacro MUI_PAGE_WELCOME

!define MUI_LICENSEPAGE_RADIOBUTTONS
!define MUI_LICENSEPAGE_TEXT_TOP "请阅读以下软件许可协议与隐私告知。您必须接受后才能继续安装。"
!insertmacro MUI_PAGE_LICENSE "${PROJECTROOT}\installer_source\license.txt"

!define MUI_COMPONENTSPAGE_TEXT_TOP "选择要安装的组件。带$\"必需$\"标记的组件不可取消。"
!define MUI_COMPONENTSPAGE_NODESC
!insertmacro MUI_PAGE_COMPONENTS

!define MUI_DIRECTORYPAGE_TEXT_TOP "选择 ${APPNAME} 的安装位置。安装需要约 400 MB 可用磁盘空间，且您对该目录需具备写入权限。"
!define MUI_DIRECTORYPAGE_TEXT_DESTINATION "安装文件夹:"
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE DirectoryLeave
!insertmacro MUI_PAGE_DIRECTORY

!insertmacro MUI_PAGE_INSTFILES

!define MUI_FINISHPAGE_TITLE "完成 ${APPNAME} 安装"
!define MUI_FINISHPAGE_TEXT "PeekShield 已成功安装到您的计算机。$\r$\n$\r$\n点击$\"完成$\"结束安装向导。"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXENAME}"
!define MUI_FINISHPAGE_RUN_TEXT "立即运行 ${APPNAME}"
!define MUI_FINISHPAGE_NOREBOOTSUPPORT
!insertmacro MUI_PAGE_FINISH

; ---------- Uninstall pages ----------
!insertmacro MUI_UNPAGE_CONFIRM
UninstPage custom un.OptionsPage un.OptionsPageLeave
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "SimpChinese"

; ---------- Variables (uninstall options) ----------
Var unOptConfig
Var unOptData
Var hChkConfig
Var hChkData

; ---------- OS / permission check ----------
Function .onInit
  ${Unless} ${AtLeastWin10}
    MessageBox MB_OK|MB_ICONSTOP "本软件需要 Windows 10 或更高版本。当前操作系统不受支持，安装已终止。"
    Abort
  ${EndIf}
FunctionEnd

Function DirectoryLeave
  ${GetRoot} $INSTDIR $0
  ${DriveSpace} $0 "/D=F /S=M" $1
  ${If} $1 < 400
    MessageBox MB_OK|MB_ICONEXCLAMATION "磁盘可用空间不足。安装需要约 400 MB 可用空间，当前驱动器 $0 仅剩约 $1 MB。请清理空间或选择其他位置。"
    Abort
  ${EndIf}
  CreateDirectory "$INSTDIR"
  FileOpen $2 "$INSTDIR\_writetest.tmp" w
  ${If} $2 == ""
    MessageBox MB_OK|MB_ICONEXCLAMATION "无法写入安装目录 $INSTDIR。请选择其他路径，或以具有该目录写入权限的账户运行安装程序。"
    Abort
  ${Else}
    FileWrite $2 "test"
    FileClose $2
    Delete "$INSTDIR\_writetest.tmp"
  ${EndIf}
FunctionEnd

; ---------- Install sections ----------
Section "主程序（必需）" SEC_MAIN
  SectionIn RO

  ; 进程保护兼容：先写优雅退出标志，覆盖更新时守护进程不会反复重启旧主程序
  CreateDirectory "${DATADIR}"
  FileOpen $1 "${DATADIR}\guardian_shutdown" w
  FileWrite $1 "1"
  FileClose $1
  ; 先移除旧版守护进程（计划任务/服务），再结束守护残留进程与主程序进程
  DetailPrint "正在关闭已运行的 PeekShield 进程..."
  Exec 'schtasks /Delete /TN "PeekShieldGuard" /F'
  ExecWait 'taskkill /F /IM "PeekShieldGuard.exe"' $3
  ExecWait 'taskkill /F /IM "${EXENAME}"' $0
  ; 清理旧版改名看门狗残留（早期版本遗留的可执行文件）
  Delete "$INSTDIR\PeekShieldGuard.exe"
  ; 等待进程完全退出并释放 DLL 句柄，避免覆盖写入被占用
  Sleep 2000

  SetOutPath "$INSTDIR"
  File /r /x "*.nsi" /x "setup.cmd" /x "*.p7s" /x "*.h" /x "*.lib" /x "*.pdb" "${PROJECTROOT}\dist\win-x64\*.*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; 注册卸载信息
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayName" "${APPNAME}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayVersion" "${APPVERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "UninstallString" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayIcon" "$INSTDIR\${EXENAME}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "NoModify" "1"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "NoRepair" "1"

  ; 安装结束清除优雅退出标志，使下次启动可正常启用进程保护
  Delete "${DATADIR}\guardian_shutdown"
SectionEnd

Section "创建桌面快捷方式" SEC_DESKTOP
  CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${EXENAME}"
SectionEnd

Section "创建开始菜单快捷方式" SEC_STARTMENU
  CreateDirectory "$SMPROGRAMS\${APPNAME}"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\${EXENAME}"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
SectionEnd

; ---------- Uninstall custom options page ----------
Function un.OptionsPage
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}

  ${NSD_CreateLabel} 0 0 100% 24u "请选择卸载时要执行的操作："
  Pop $0

  ${NSD_CreateCheckBox} 0 32u 100% 16u "删除配置信息（软件设置、授权等）"
  Pop $hChkConfig
  ${NSD_Check} $hChkConfig

  ${NSD_CreateCheckBox} 0 56u 100% 16u "清空保存的数据（人脸录入、日志等，不可恢复）"
  Pop $hChkData

  nsDialogs::Show
FunctionEnd

Function un.OptionsPageLeave
  ${NSD_GetState} $hChkConfig $unOptConfig
  ${NSD_GetState} $hChkData $unOptData
FunctionEnd

; ---------- Uninstall section ----------
Section "Uninstall"
  ; 进程保护兼容：先写优雅退出标志，使看门狗在进程被杀后不再重启
  CreateDirectory "${DATADIR}"
  FileOpen $1 "${DATADIR}\guardian_shutdown" w
  FileWrite $1 "1"
  FileClose $1
  IfFileExists "$INSTDIR\${EXENAME}" 0 skip_guard
  ExecWait '"$INSTDIR\${EXENAME}" --uninstall-verify' $0
  IntCmp $0 0 proceed_guard
  ; 验证失败/取消：移除标志，恢复进程保护
  Delete "${DATADIR}\guardian_shutdown"
  MessageBox MB_OK|MB_ICONEXCLAMATION "密码验证失败或已取消，卸载已终止。"
  Abort
  proceed_guard:
  ; 验证通过：先移除守护进程（计划任务），再结束主程序（标志已写入，守护进程不会重启）
  Exec 'schtasks /Delete /TN "PeekShieldGuard" /F'
  ExecWait 'taskkill /F /IM "${EXENAME}" /FI "STATUS eq RUNNING"' $R1
  Sleep 500
  Delete "${DATADIR}\guardian_shutdown"
  skip_guard:

  Delete "$DESKTOP\${APPNAME}.lnk"
  RMDir /r "$SMPROGRAMS\${APPNAME}"
  RMDir /r "$INSTDIR"

  ; 可选：清空保存的数据（整个数据目录）
  ${If} $unOptData == 1
    RMDir /r "${DATADIR}"
  ${Else}
    ; 可选：仅删除配置信息（设置文件），保留人脸录入与日志
    ${If} $unOptConfig == 1
      Delete "${DATADIR}\settings.json"
      Delete "${DATADIR}\settings.json.bak"
      Delete "${DATADIR}\consent.json"
    ${EndIf}
  ${EndIf}

  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}"
SectionEnd
