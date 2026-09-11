; PeekShield Windows installer (NSIS 3.x)
Unicode True
SetCompressor /SOLID lzma

!define APPNAME "PeekShield"
!define APPVERSION "1.2.0.0"
!define PUBLISHER "yty16"
!define EXENAME "PeekShield.exe"
!define INSTALLDIR "$LOCALAPPDATA\Programs\${APPNAME}"
!define PROJECTROOT "${__FILEDIR__}\.."

Name "${APPNAME} ${APPVERSION}"
OutFile "${PROJECTROOT}\installer\PeekShield-1.2.0.0-win-x64-setup.exe"
InstallDir "${INSTALLDIR}"
RequestExecutionLevel user
ShowInstDetails show
ShowUninstDetails show

Section "Install"
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
SectionEnd

Section "Uninstall"
    IfFileExists "$INSTDIR\${EXENAME}" 0 skip_guard
    ExecWait '"$INSTDIR\${EXENAME}" --uninstall-verify' $0
    IntCmp $0 0 proceed_guard
    MessageBox MB_OK|MB_ICONEXCLAMATION "密码验证失败或已取消，卸载已终止。"
    Abort
    proceed_guard:
    skip_guard:
    Delete "$DESKTOP\${APPNAME}.lnk"
    RMDir /r "$INSTDIR"
    RMDir /r "$SMPROGRAMS\${APPNAME}"
    DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}"
SectionEnd
