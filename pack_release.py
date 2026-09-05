import os, stat, tarfile, zipfile, struct, io

ROOT = os.path.dirname(os.path.abspath(__file__))
DIST = os.path.join(ROOT, "dist")
INSTALLER = os.path.join(ROOT, "installer")
ICON = os.path.join(ROOT, "installer_source", "Icon.png")
VERSION = "1.0.0.0"

def walk_files(base):
    out = []
    for dp, _, fns in os.walk(base):
        for fn in fns:
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
        "Maintainer: yty16 <3069505332@qq.com>\n"
        "Installed-Size: %d\n"
        "Description: 窥屿盾 (PeekShield) - 本地离线隐私防偷窥工具\n"
        " 基于本地摄像头 AI 人脸识别的桌面隐私保护工具，支持屏幕雾化、告警与机主验证。\n"
        % (VERSION, installed_size)
    )
    postinst = "#!/bin/sh\nchmod 755 /opt/peekshield/PeekShield\nchmod 755 /opt/peekshield/createdump 2>/dev/null\nupdate-desktop-database 2>/dev/null || true\n"
    with tarfile.open(out_path, "w:gz") as tar:
        _add_bytes(tar, "control", control.encode("utf-8"), 0o644)
        _add_bytes(tar, "postinst", postinst.encode("utf-8"), 0o755)

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
    os.remove(data_tar)
    os.remove(ctrl_tar)
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
            import shutil
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
    zip_path = os.path.join(INSTALLER, "PeekShield-osx-arm64-%s.app.zip" % VERSION)
    if os.path.exists(zip_path):
        os.remove(zip_path)
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
    import shutil as _s
    _s.rmtree(app_dir)
    print("app.zip:", zip_path, os.path.getsize(zip_path))

if __name__ == "__main__":
    build_deb()
    build_app_zip()
    print("done")
