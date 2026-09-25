import os, stat, tarfile, zipfile, struct, io, shutil

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DIST = os.path.join(ROOT, "dist")
INSTALLER = os.path.join(ROOT, "installer")
ICON = os.path.join(ROOT, "installer_source", "Icon.png")
VERSION = "1.2.2.0"

def walk_files(base):
    out = []
    for dp, _, fns in os.walk(base):
        for fn in fns:
            if fn.lower().endswith(".pdb"):
                continue
            out.append(os.path.join(dp, fn))
    return out

def mode_for(path, rel):
    if os.path.isdir(path):
        return 0o755
    base = os.path.basename(path)
    ext = os.path.splitext(base)[1].lower()
    if base == "PeekShield" or ext == ".so" or ext in (".sh",):
        return 0o755
    return 0o644

def build_data_tar(src_dir, install_root, out_path):
    files = walk_files(src_dir)
    total = 0
    with tarfile.open(out_path, "w:gz") as tar:
        for f in sorted(files):
            rel = os.path.relpath(f, src_dir)
            arc = os.path.join(install_root, rel).replace("\\", "/")
            ti = tar.gettarinfo(f, arcname=arc)
            ti.mode = mode_for(f, rel)
            ti.uid = ti.gid = 0
            ti.uname = ti.gname = "root"
            if ti.isreg():
                with open(f, "rb") as fh:
                    tar.addfile(ti, fh)
            else:
                tar.addfile(ti)
            total += os.path.getsize(f)
        desktop = (
            "[Desktop Entry]\n"
            "Name=PeekShield 窥屿盾\n"
            "Comment=本地离线隐私防偷窥工具\n"
            "Exec=/opt/peekshield/PeekShield\n"
            "Icon=peekshield\n"
            "Terminal=false\n"
            "Type=Application\n"
            "Categories=Utility;Security;\n"
        )
        _add_bytes(tar, "usr/share/applications/peekshield.desktop", desktop.encode("utf-8"), 0o644)
        if os.path.exists(ICON):
            with open(ICON, "rb") as fh:
                _add_bytes(tar, "usr/share/pixmaps/peekshield.png", fh.read(), 0o644)
        sym = tarfile.TarInfo(name="usr/bin/peekshield")
        sym.type = tarfile.SYMTYPE
        sym.linkname = "/opt/peekshield/PeekShield"
        sym.uid = sym.gid = 0
        sym.uname = sym.gname = "root"
        tar.addfile(sym)
    return total

def _add_bytes(tar, arc, data, mode):
    ti = tarfile.TarInfo(name=arc)
    ti.size = len(data)
    ti.mode = mode
    ti.uid = ti.gid = 0
    ti.uname = ti.gname = "root"
    tar.addfile(ti, io.BytesIO(data))

def build_control_tar(out_path, installed_size):
    control = (
        "Package: peekshield\n"
        "Version: %s\n"
        "Section: utils\n"
        "Priority: optional\n"
        "Architecture: amd64\n"
        "Maintainer: yty16\n"
        "Installed-Size: %d\n"
        "Description: 窥屿盾 (PeekShield) - 本地离线隐私防偷窥工具\n"
        " 基于本地摄像头 AI 人脸识别的桌面隐私保护工具，支持屏幕雾化、告警与机主验证。\n"
        % (VERSION, installed_size)
    )
    preinst = (
        "#!/bin/sh\n"
        "set -e\n"
        "ARCH=$(dpkg --print-architecture 2>/dev/null || uname -m)\n"
        "if [ \"$ARCH\" != \"amd64\" ] && [ \"$ARCH\" != \"x86_64\" ]; then\n"
        "  echo \"PeekShield 仅支持 amd64 架构。当前架构: $ARCH\"\n"
        "  exit 1\n"
        "fi\n"
        "NEED_MB=400\n"
        "AVAIL_MB=$(df -Pm /opt 2>/dev/null | awk 'NR==2 {print $4}')\n"
        "if [ -z \"$AVAIL_MB\" ]; then\n"
        "  AVAIL_MB=$(df -Pm / 2>/dev/null | awk 'NR==2 {print $4}')\n"
        "fi\n"
        "if [ \"$AVAIL_MB\" -lt \"$NEED_MB\" ]; then\n"
        "  echo \"磁盘可用空间不足。安装需要约 ${NEED_MB} MB，当前仅剩 ${AVAIL_MB} MB。\"\n"
        "  exit 1\n"
        "fi\n"
        "echo \"继续安装即表示您已阅读并同意 GPL-3.0 许可证与隐私告知。\"\n"
        "echo \"隐私告知摘要：所有人脸检测与特征提取均在本地完成，不会上传任何数据。\"\n"
        "pkill -f \"/opt/peekshield/PeekShield\" 2>/dev/null || true\n"
        "pkill -f \"/opt/peekshield/PeekShieldGuard\" 2>/dev/null || true\n"
        "exit 0\n"
    )
    postinst = (
        "#!/bin/sh\n"
        "chmod 755 /opt/peekshield/PeekShield\n"
        "chmod 755 /opt/peekshield/createdump 2>/dev/null || true\n"
        "update-desktop-database 2>/dev/null || true\n"
        "echo \"PeekShield 安装完成。启动命令: peekshield 或 /opt/peekshield/PeekShield\"\n"
        "exit 0\n"
    )
    prerm = (
        "#!/bin/sh\n"
        "echo \"正在停止 PeekShield...\"\n"
        "pkill -f \"/opt/peekshield/PeekShield\" 2>/dev/null || true\n"
        "pkill -f \"/opt/peekshield/PeekShieldGuard\" 2>/dev/null || true\n"
        "sleep 1\n"
        "exit 0\n"
    )
    postrm = (
        "#!/bin/sh\n"
        "if [ \"$1\" = \"remove\" ] || [ \"$1\" = \"purge\" ]; then\n"
        "  rm -rf /opt/peekshield\n"
        "  echo \"PeekShield 程序文件已删除。\"\n"
        "fi\n"
        "if [ \"$1\" = \"purge\" ]; then\n"
        "  for d in /home/*/.local/share/PeekShield /root/.local/share/PeekShield; do\n"
        "    if [ -d \"$d\" ]; then\n"
        "      rm -rf \"$d\"\n"
        "      echo \"已清空数据: $d\"\n"
        "    fi\n"
        "  done\n"
        "fi\n"
        "if [ \"$1\" = \"remove\" ]; then\n"
        "  echo \"配置与用户数据保留在 ~/.local/share/PeekShield。彻底删除请执行: dpkg --purge peekshield\"\n"
        "fi\n"
        "exit 0\n"
    )
    with tarfile.open(out_path, "w:gz") as tar:
        _add_bytes(tar, "control", control.encode("utf-8"), 0o644)
        _add_bytes(tar, "preinst", preinst.encode("utf-8"), 0o755)
        _add_bytes(tar, "postinst", postinst.encode("utf-8"), 0o755)
        _add_bytes(tar, "prerm", prerm.encode("utf-8"), 0o755)
        _add_bytes(tar, "postrm", postrm.encode("utf-8"), 0o755)

def write_ar(out_path, members):
    with open(out_path, "wb") as f:
        f.write(b"!<arch>\n")
        for name, data in members:
            nb = name if isinstance(name, str) else name.decode("ascii")
            if len(nb) > 15:
                nb = nb[:15] + "/"
            else:
                nb = nb.ljust(16)
            header = (
                nb
                + "0".rjust(12)
                + "0".rjust(6)
                + "0".rjust(6)
                + "100644".rjust(8)
                + str(len(data)).rjust(10)
                + "`\n"
            )
            f.write(header.encode("ascii"))
            f.write(data)
            if len(data) % 2 == 1:
                f.write(b"\n")

def build_deb():
    data_tar = os.path.join(INSTALLER, "_data.tar.gz")
    ctrl_tar = os.path.join(INSTALLER, "_control.tar.gz")
    size = build_data_tar(os.path.join(DIST, "linux-x64"), "opt/peekshield", data_tar)
    installed_size = (size + 1023) // 1024
    build_control_tar(ctrl_tar, installed_size)
    deb = os.path.join(INSTALLER, "PeekShield-linux-x64-%s.deb" % VERSION)
    with open(data_tar, "rb") as fh:
        d = fh.read()
    with open(ctrl_tar, "rb") as fh:
        c = fh.read()
    write_ar(deb, [("debian-binary", b"2.0\n"), ("control.tar.gz", c), ("data.tar.gz", d)])
    print("deb:", deb, os.path.getsize(deb))

def build_app_zip():
    app_dir = os.path.join(INSTALLER, "PeekShield.app")
    macos = os.path.join(app_dir, "Contents", "MacOS")
    res = os.path.join(app_dir, "Contents", "Resources")
    os.makedirs(macos, exist_ok=True)
    os.makedirs(res, exist_ok=True)
    src = os.path.join(DIST, "osx-arm64")
    for dp, _, fns in os.walk(src):
        for fn in fns:
            s = os.path.join(dp, fn)
            rel = os.path.relpath(s, src)
            t = os.path.join(macos, rel)
            os.makedirs(os.path.dirname(t), exist_ok=True)
            shutil.copy2(s, t)
    info = (
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        '<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">\n'
        '<plist version="1.0"><dict>\n'
        '<key>CFBundleName</key><string>PeekShield</string>\n'
        '<key>CFBundleDisplayName</key><string>PeekShield 窥屿盾</string>\n'
        '<key>CFBundleIdentifier</key><string>com.peekshield.app</string>\n'
        '<key>CFBundleVersion</key><string>%s</string>\n'
        '<key>CFBundleShortVersionString</key><string>%s</string>\n'
        '<key>CFBundleExecutable</key><string>PeekShield</string>\n'
        '<key>CFBundlePackageType</key><string>APPL</string>\n'
        '<key>CFBundleIconFile</key><string>AppIcon</string>\n'
        '<key>LSMinimumSystemVersion</key><string>11.0</string>\n'
        '<key>NSPrincipalClass</key><string>NSApplication</string>\n'
        '<key>NSHighResolutionCapable</key><true/>\n'
        '</dict></plist>\n' % (VERSION, VERSION)
    )
    with open(os.path.join(app_dir, "Contents", "Info.plist"), "w", encoding="utf-8") as fh:
        fh.write(info)
    if os.path.exists(ICON):
        shutil.copy2(ICON, os.path.join(res, "AppIcon.png"))

    readme = (
        "PeekShield 窥屿盾 macOS 版安装说明\n"
        "====================================\n\n"
        "1. 将 PeekShield.app 拖入“应用程序”文件夹。\n"
        "2. 首次启动请在“系统设置 > 隐私与安全性”中允许运行。\n"
        "3. 打开 PeekShield.app，按提示完成隐私授权与人脸录入。\n\n"
        "软件许可协议与隐私告知\n"
        "----------------------\n"
        "本软件依据 GNU General Public License v3.0 发布。\n"
        "所有人脸检测、特征提取、偷窥判定均在本地完成，不会上传任何摄像头画面、人脸图像或特征向量到服务器。\n"
        "继续安装/使用即表示您已阅读并同意上述条款。\n"
        "完整许可证与源代码：https://github.com/yty16/PeekShield\n\n"
        "卸载\n"
        "----\n"
        "双击运行“Uninstall PeekShield.command”，按提示完成卸载。\n"
    )
    with open(os.path.join(INSTALLER, "README.txt"), "w", encoding="utf-8") as fh:
        fh.write(readme)

    uninstall_cmd = (
        "#!/bin/bash\n"
        "APP_PATH=\"/Applications/PeekShield.app\"\n"
        "DATA_DIR=\"$HOME/Library/Application Support/PeekShield\"\n"
        "PREFS=\"$HOME/Library/Preferences/com.peekshield.app.plist\"\n\n"
        "echo \"正在卸载 PeekShield...\"\n"
        "pkill -f \"PeekShield\" 2>/dev/null || true\n"
        "sleep 1\n\n"
        "if [ -d \"$APP_PATH\" ]; then\n"
        "  rm -rf \"$APP_PATH\"\n"
        "  echo \"已删除 $APP_PATH\"\n"
        "fi\n\n"
        "read -p \"是否删除配置信息与用户数据？（输入 y 删除，其他保留）: \" ans\n"
        "if [ \"$ans\" = \"y\" ] || [ \"$ans\" = \"Y\" ]; then\n"
        "  rm -rf \"$DATA_DIR\"\n"
        "  rm -f \"$PREFS\"\n"
        "  echo \"已删除用户数据。\"\n"
        "else\n"
        "  echo \"保留用户数据：$DATA_DIR\"\n"
        "fi\n\n"
        "echo \"卸载完成。\"\n"
    )
    cmd_path = os.path.join(INSTALLER, "Uninstall PeekShield.command")
    with open(cmd_path, "w", encoding="utf-8") as fh:
        fh.write(uninstall_cmd)
    os.chmod(cmd_path, 0o755)

    zip_path = os.path.join(INSTALLER, "PeekShield-osx-arm64-%s.app.zip" % VERSION)
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
        for dp, _, fns in os.walk(app_dir):
            for fn in fns:
                full = os.path.join(dp, fn)
                arc = os.path.relpath(full, INSTALLER).replace("\\", "/")
                mode = mode_for(full, arc)
                zi = zipfile.ZipInfo(arc)
                zi.external_attr = (mode & 0xFFFF) << 16
                zi.compress_type = zipfile.ZIP_DEFLATED
                with open(full, "rb") as fh:
                    z.writestr(zi, fh.read())
        for extra in ["README.txt", "Uninstall PeekShield.command"]:
            full = os.path.join(INSTALLER, extra)
            if os.path.exists(full):
                arc = extra.replace("\\", "/")
                zi = zipfile.ZipInfo(arc)
                zi.external_attr = (0o755 if extra.endswith(".command") else 0o644) << 16
                zi.compress_type = zipfile.ZIP_DEFLATED
                with open(full, "rb") as fh:
                    z.writestr(zi, fh.read())
    print("app.zip:", zip_path, os.path.getsize(zip_path))

if __name__ == "__main__":
    build_deb()
    build_app_zip()
    print("done")
