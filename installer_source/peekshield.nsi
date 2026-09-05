; PeekShield Windows installer (NSIS 3.x)
Unicode True
SetCompressor /SOLID lzma

!define APPNAME "PeekShield"
!define APPVERSION "1.0.0.0"
!define PUBLISHER "yty16"
!define EXENAME "PeekShield.exe"
!define INSTALLDIR "$LOCALAPPDATA\Programs\${APPNAME}"

Name "${APPNAME} ${APPVERSION}"
OutFile "C:\Users\Yin\WorkBuddy\PeekShield\installer\PeekShield-1.0.0.0-win-x64-setup.exe"
InstallDir "${INSTALLDIR}"
RequestExecutionLevel user
ShowInstDetails show
ShowUninstDetails show

Section "Install"
    SetOutPath "$INSTDIR"
    File /r /x "*.nsi" /x "setup.cmd" /x "*.p7s" /x "*.h" /x "*.lib" /x "*.pdb" "C:\Users\Yin\WorkBuddy\PeekShield\dist\win-x64\*.*"
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
    Delete "$DESKTOP\${APPNAME}.lnk"
    RMDir /r "$INSTDIR"
    RMDir /r "$SMPROGRAMS\${APPNAME}"
    DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}"
SectionEnd
