; PeekShield Windows installer (NSIS 3.x)
Unicode True
SetCompressor lzma

!define APPNAME "PeekShield"
!define APPVERSION "1.2.1.3"
!define PUBLISHER "yty16"
!define EXENAME "PeekShield.exe"
!define INSTALLDIR "$LOCALAPPDATA\Programs\${APPNAME}"
!define PROJECTROOT "${__FILEDIR__}\.."

Name "${APPNAME} ${APPVERSION}"
OutFile "${PROJECTROOT}\installer\PeekShield-1.2.1.3-win-x64-setup.exe"
InstallDir "${INSTALLDIR}"
RequestExecutionLevel user
ShowInstDetails show
ShowUninstDetails show

Section "Install"
    ; 进程保护兼容：先写优雅退出标志，覆盖更新时守护进程不会反复重启旧主程序
    CreateDirectory "$LOCALAPPDATA\PeekShield"
    FileOpen $1 "$LOCALAPPDATA\PeekShield\guardian_shutdown" w
    FileWrite $1 "1"
    FileClose $1
    ; 先移除旧版守护进程（计划任务/服务），再结束守护残留进程与主程序进程
    DetailPrint "正在关闭已运行的 PeekShield 进程..."
    ExecWait 'schtasks /Delete /TN "PeekShieldGuard" /F' $2
    ExecWait 'taskkill /F /IM "PeekShieldGuard.exe"' $3
    ExecWait 'taskkill /F /IM "${EXENAME}"' $0
    ; 清理旧版改名看门狗残留（早期版本遗留的可执行文件）
    Delete "$INSTDIR\PeekShieldGuard.exe"
    ; 等待进程完全退出并释放 DLL 句柄，避免覆盖写入（如 Avalonia.Base.dll）被占用
    Sleep 2000

    SetOutPath "$INSTDIR"
    File /r /x "*.nsi" /x "setup.cmd" /x "*.p7s" /x "*.h" /x "*.lib" /x "*.pdb" "${PROJECTROOT}\dist\win-x64\*.*"
    WriteUninstaller "$INSTDIR\Uninstall.exe"

    CreateDirectory "$SMPROGRAMS\${APPNAME}"
    CreateShortcut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\${EXENAME}"
    CreateShortcut "$SMPROGRAMS\${APPNAME}\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
    CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${EXENAME}"

    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayName" "${APPNAME}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayVersion" "${APPVERSION}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "Publisher" "${PUBLISHER}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "UninstallString" "$INSTDIR\Uninstall.exe"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayIcon" "$INSTDIR\${EXENAME}"
    ; 安装结束清除优雅退出标志，使下次启动可正常启用进程保护
    Delete "$LOCALAPPDATA\PeekShield\guardian_shutdown"
SectionEnd

Section "Uninstall"
    ; 进程保护兼容：先写优雅退出标志，使看门狗在进程被杀后不再重启
    CreateDirectory "$LOCALAPPDATA\PeekShield"
    FileOpen $1 "$LOCALAPPDATA\PeekShield\guardian_shutdown" w
    FileWrite $1 "1"
    FileClose $1
    IfFileExists "$INSTDIR\${EXENAME}" 0 skip_guard
    ExecWait '"$INSTDIR\${EXENAME}" --uninstall-verify' $0
    IntCmp $0 0 proceed_guard
    ; 验证失败/取消：移除标志，恢复进程保护
    Delete "$LOCALAPPDATA\PeekShield\guardian_shutdown"
    MessageBox MB_OK|MB_ICONEXCLAMATION "密码验证失败或已取消，卸载已终止。"
    Abort
    proceed_guard:
    ; 验证通过：先移除守护进程（计划任务），再结束主程序（标志已写入，守护进程不会重启）
    ExecWait 'schtasks /Delete /TN "PeekShieldGuard" /F' $R2
    ExecWait 'taskkill /F /IM "${EXENAME}" /FI "STATUS eq RUNNING"' $R1
    Sleep 500
    Delete "$LOCALAPPDATA\PeekShield\guardian_shutdown"
    skip_guard:
    Delete "$DESKTOP\${APPNAME}.lnk"
    RMDir /r "$INSTDIR"
    RMDir /r "$SMPROGRAMS\${APPNAME}"
    DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}"
SectionEnd
