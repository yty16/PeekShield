import os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
INSTALLER = os.path.join(ROOT, "installer")
VERSION = "1.2.5.6"

ARTIFACTS = [
    os.path.join(INSTALLER, f"PeekShield-{VERSION}-win-x64-setup.exe"),
    os.path.join(INSTALLER, f"PeekShield-linux-x64-{VERSION}.deb"),
    os.path.join(INSTALLER, f"PeekShield-osx-arm64-{VERSION}.app.zip"),
]

# 个人数据特征串：完整串，命中即为真实泄露
LEAK_FULL = [
    "3069505332@qq.com",
    "C:/Users/Yin",
    "C:\\Users\\Yin",
    "yty7816",
    "WorkBuddy/PeekShield",
    "WorkBuddy\\PeekShield",
]

# 短串：仅做信息提示，dlib 权重随机字节可能误报
LEAK_SHORT = ["Yin", "@qq.com", "Yin\\"]

# 预期存在的隐藏署名水印（非泄露）
WATERMARK = ["eXR5MTY="]

CHUNK = 1 << 20

def scan_file(path):
    hits = {}
    if not os.path.exists(path):
        return None, f"缺失: {path}"
    size = os.path.getsize(path)
    with open(path, "rb") as fh:
        overlap = 0
        prev = b""
        while True:
            data = fh.read(CHUNK)
            if not data:
                break
            buf = prev + data if overlap else data
            for s in LEAK_FULL:
                for enc in (s.encode("utf-8"), s.encode("utf-16-le")):
                    if enc in buf:
                        hits.setdefault(s, 0)
                        hits[s] += buf.count(enc)
            for s in LEAK_SHORT:
                for enc in (s.encode("utf-8"), s.encode("utf-16-le")):
                    n = buf.count(enc)
                    if n:
                        hits.setdefault("SHORT:" + s, 0)
                        hits["SHORT:" + s] += n
            for s in WATERMARK:
                if s.encode("utf-8") in buf:
                    hits.setdefault("WATERMARK:" + s, 0)
                    hits["WATERMARK:" + s] += buf.count(s.encode("utf-8"))
            prev = data[-(max(len(s.encode("utf-16-le")) for s in LEAK_FULL + WATERMARK) - 1):] if overlap else data[-32:]
            overlap = 1
    return size, hits

def main():
    print(f"== leak scan @ {VERSION} ==")
    total_leak = 0
    for art in ARTIFACTS:
        size, res = scan_file(art)
        if size is None:
            print(f"[MISS] {os.path.basename(art)}: {res}")
            continue
        print(f"\n[{os.path.basename(art)}] {size} B")
        leak_any = False
        for k, v in sorted(res.items()):
            if k.startswith("WATERMARK:"):
                print(f"   OK 水印在位 {k} x{v}")
            elif k.startswith("SHORT:"):
                print(f"   INFO 短串 {k} x{v} (可能误报)")
            else:
                print(f"   !! 泄露特征 {k} x{v}")
                leak_any = True
                total_leak += v
        if not leak_any and not any(k.startswith("!!") for k in res):
            # 仅统计非短串/水印
            real = [k for k in res if not k.startswith(("SHORT:", "WATERMARK:"))]
            if not real:
                print("   无个人数据特征命中")
    print("\n== 结论 ==")
    print("NO_LEAK" if total_leak == 0 else f"LEAK_FOUND({total_leak})")

if __name__ == "__main__":
    main()
