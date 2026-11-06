"""Fixture: buktikan #MASTER dibuat per FOLDER GAMBAR (manasik/wisuda/pasfoto).

Meniru kasus nyata user: satu folder jenis berisi beberapa subfolder sekolah
(PAUD ARIFKI / PAUD ARROHMAN / RA. ARROHMAN) dengan .txt kode di folder induk.
Harapan: tiap subfolder punya #MASTER-nya SENDIRI.
"""
import os
import shutil
import subprocess
import sys

ROOT = r"D:\_bm_fix"
PIL = os.path.join(ROOT, "PILIHAN")
OUT = os.path.join(ROOT, "OUT")
M10 = os.path.join(ROOT, "MASTER 10RP")
M8 = os.path.join(ROOT, "MASTER 8R")
MASTER_PY = r"D:\#DATA ABENG\#PROJECT\mono\apps\bmachine\Scripts\Master"

MANASIK = os.path.join(PIL, "4. FOTO 8R_TM MANASIK_GRADASI")
PASFOTO = os.path.join(PIL, "5. FREE PAS FOTO")
WISUDA = os.path.join(PIL, "6. FOTO WISUDA_GRADASI")


def w(path, data=b"PSDDATA"):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(data)


def t(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        f.write(text + "\n")


def jpg(path, tag):
    w(path, b"JPEG" + tag.encode())


def build():
    for d in (ROOT,):
        if os.path.isdir(d):
            shutil.rmtree(d, ignore_errors=True)

    # master dummy (beberapa byte cukup; pipeline tidak membuka Photoshop)
    w(os.path.join(M10, "MSK-002.psd"), b"MSK002-10RP")
    w(os.path.join(M8, "MSK-002.psd"), b"MSK002-8R")
    w(os.path.join(M10, "WSD-001.psd"), b"WSD001-10RP")
    w(os.path.join(M8, "WSD-001.psd"), b"WSD001-8R")
    w(os.path.join(M10, "PFM-005.psd"), b"PFM005-10RP")

    # MANASIK: .txt di induk, gambar di 3 subfolder sekolah
    t(os.path.join(MANASIK, "MSK-002, TANPA TEMPEL UNTA.txt"), "MSK-002")
    jpg(os.path.join(MANASIK, "PAUD ARIFKI", "1.JPG"), "a")
    jpg(os.path.join(MANASIK, "PAUD ARIFKI", "2.JPG"), "b")
    jpg(os.path.join(MANASIK, "PAUD ARROHMAN", "3.JPG"), "c")
    jpg(os.path.join(MANASIK, "RA. ARROHMAN", "4.JPG"), "d")

    # PAS FOTO: .txt di induk, gambar di 2 subfolder sekolah
    t(os.path.join(PASFOTO, "NOTE.txt"), "PFM-005")
    jpg(os.path.join(PASFOTO, "PAUD ARIFKI", "1.JPG"), "e")
    jpg(os.path.join(PASFOTO, "PAUD ARROHMAN", "2.JPG"), "f")

    # WISUDA
    t(os.path.join(WISUDA, "WSD-001.txt"), "WSD-001")
    jpg(os.path.join(WISUDA, "ANGKATAN A", "1.JPG"), "g")


def run(name, args):
    r = subprocess.run([sys.executable, os.path.join(MASTER_PY, name)] + args,
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    print(f"--- {name} exit={r.returncode}")
    for line in (r.stdout or "").splitlines():
        if any(k in line for k in ("[EDIT]", "[ERROR]", "[SKIP]", "[GRUP]", "[MANIFEST]", "[MODE]")):
            print("   ", line.strip())
    if r.returncode != 0:
        print(r.stderr[-1500:])
    return r


def shown(path):
    return os.path.relpath(path, OUT)


def main():
    build()
    os.makedirs(OUT, exist_ok=True)
    manifest = os.path.join(ROOT, "manifest.json")

    # Semua jenis memakai SATU manifest (seperti buat_master.py yang mengirim
    # --manifest yang sama ke tiap skrip anak).
    ce = ["--mode=copy-edit", "--phase=1", f"--manifest={manifest}"]
    run("manasik.py", [M10, PIL, OUT, M8, "NONE"] + ce)
    run("wisuda.py", [M10, PIL, OUT, M8, "NONE"] + ce)
    run("pasfoto.py", [M10, PIL, OUT, "NONE"] + ce)

    found = []
    for root, dirs, files in os.walk(OUT):
        if os.path.basename(root) == "#MASTER":
            for f in files:
                found.append(shown(os.path.join(root, f)))
    found.sort()

    print("\n=== #MASTER yang dibuat ===")
    for f in found:
        print("  ", f)

    expect_tails = [
        r"4. FOTO 8R_TM MANASIK_GRADASI\PAUD ARIFKI\#MASTER\MSK-002.psd",
        r"4. FOTO 8R_TM MANASIK_GRADASI\PAUD ARROHMAN\#MASTER\MSK-002.psd",
        r"4. FOTO 8R_TM MANASIK_GRADASI\RA. ARROHMAN\#MASTER\MSK-002.psd",
        r"5. FREE PAS FOTO\PAUD ARIFKI\#MASTER\PFM-005.psd",
        r"5. FREE PAS FOTO\PAUD ARROHMAN\#MASTER\PFM-005.psd",
        r"6. FOTO WISUDA_GRADASI\ANGKATAN A\#MASTER\WSD-001.psd",
    ]

    print("\n=== ASSERT ===")
    ok = True
    for tail in expect_tails:
        hit = any(f.endswith(tail) for f in found)
        ok &= hit
        print(f"  {'OK  ' if hit else 'MISS'} {tail}")

    bad = [f for f in found
           if not any(f.endswith(t) for t in expect_tails)]
    if bad:
        ok = False
        print("  FAIL #MASTER tak terduga (mis. nyangkut di folder induk):")
        for f in bad:
            print("      ", f)
    else:
        print("  OK   tidak ada #MASTER di luar folder gambar")

    print("\nRESULT fase 1:", "PASS" if ok else "FAIL")

    # ---------------- fase 2: DUPLIKAT PSD dari manifest ----------------
    print("\n=== fase 2 (buat_master_duplikat) ===")
    run("buat_master_duplikat.py", [PIL, OUT, f"--manifest={manifest}"])

    got = {}
    for root, dirs, files in os.walk(ROOT):
        if os.path.basename(root) == "#MASTER":
            continue
        for f in files:
            if f.lower().endswith(".psd"):
                got.setdefault(os.path.basename(root), []).append(f)
    expect_jobs = {
        "PAUD ARIFKI": 2,   # manasik 1.JPG + 2.JPG (pasfoto 1.jpg nama sama -> beda folder sekolah)
        "PAUD ARROHMAN": 1,
        "RA. ARROHMAN": 1,
        "ANGKATAN A": 1,
    }
    for folder, n in expect_jobs.items():
        hits = [v for k, v in got.items() if k == folder]
        total = max((len(h) for h in hits), default=0)
        good = total >= n
        ok &= good
        print(f"  {'OK  ' if good else 'FAIL'} {folder}: {total} psd (harap >= {n})")

    # template di #MASTER tidak boleh hilang
    still = [f for f in found if any(f.endswith(t) for t in expect_tails)]
    print(f"  {'OK  ' if len(still) == len(expect_tails) else 'FAIL'} template #MASTER utuh ({len(still)}/{len(expect_tails)})")
    ok &= len(still) == len(expect_tails)

    print("\nRESULT:", "PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
