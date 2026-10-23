#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
_fastio.py — Utilitas I/O cepat untuk skrip master BMachine.

Mengatasi lag saat "Buat Master" / "Duplikat PSD" karena:
  1. Salin ribuan file kecil / puluhan PSD besar secara serial (I/O spike).
  2. `os.walk` + `listdir` di jaringan (\\server\...) berulang kali.

Fitur:
  - fast_copy(src, dst): salin file via `robocopy` (Windows, cepat + resume),
    fallback `shutil.copy2`.
  - batch_copy(jobs, workers): salin banyak file paralel (thread pool),
    dengan progress callback + pembatalan.
  - cached_listdir(path): cache hasil os.listdir (struktur master jarang berubah
    selama satu proses) — hindari stat/listdir jaringan berulang.
  - scan_files(root, exts): os.walk sekali untuk semua, bukan per-subfolder.

Semua fungsi safe (exception aman, tidak memblokir UI).
"""
import os
import sys
import shutil
import subprocess
import threading
from concurrent.futures import ThreadPoolExecutor, as_completed

# Flag pembatalan global (set True oleh UI bila user batal).
_CANCEL_EVENT = threading.Event()


def cancel_all():
    _CANCEL_EVENT.set()


def reset_cancel():
    _CANCEL_EVENT.clear()


def is_cancelled():
    return _CANCEL_EVENT.is_set()


# ---------------------------------------------------------------------------
# CACHE listdir / scandir
# ---------------------------------------------------------------------------
_LISTDIR_CACHE = {}
_LISTDIR_LOCK = threading.Lock()


def cached_listdir(path: str) -> list:
    """os.listdir dengan cache per-path (aman thread, TTL ringan).

    Struktur folder master umumnya statis selama satu sesi; cache ini
    memangkas stat jaringan berulang. Cache dibatasi ~512 entri.
    """
    with _LISTDIR_LOCK:
        hit = _LISTDIR_CACHE.get(path)
        if hit is not None:
            return hit
    try:
        result = os.listdir(path)
    except Exception:
        result = []
    with _LISTDIR_LOCK:
        if len(_LISTDIR_CACHE) >= 512:
            # Buang setengah paling lama sederhana (FIFO via dict order)
            for k in list(_LISTDIR_CACHE.keys())[:256]:
                _LISTDIR_CACHE.pop(k, None)
        _LISTDIR_CACHE[path] = result
    return result


def clear_listdir_cache():
    with _LISTDIR_LOCK:
        _LISTDIR_CACHE.clear()


# ---------------------------------------------------------------------------
# FAST COPY (robocopy / xcopy fallback, serial & paralel)
# ---------------------------------------------------------------------------
_ROBOCOPY = None


def _find_robocopy():
    global _ROBOCOPY
    if _ROBOCOPY is None:
        try:
            import shutil as _s
            _ROBOCOPY = _s.which("robocopy") or r"C:\Windows\System32\robocopy.exe"
            if not os.path.isfile(_ROBOCOPY):
                _ROBOCOPY = "robocopy"
        except Exception:
            _ROBOCOPY = "robocopy"
    return _ROBOCOPY


def fast_copy(src: str, dst: str, overwrite: bool = True) -> bool:
    """Salin 1 file secepat mungkin.

    - Windows: `robocopy /B` (block mode, cepat untuk file besar) bila ada;
      fallback `shutil.copy2`.
    - Non-Windows / gagal: `shutil.copy2`.
    - `overwrite=False`: lewati bila dst sudah ada (sama seperti skip_existing).

    Returns: True bila berhasil (atau dst sudah ada & overwrite=False).
    """
    if os.path.isfile(dst) and not overwrite:
        return True  # sudah ada, anggap oke
    if not os.path.isfile(src):
        return False

    if os.name == "nt":
        rc = _find_robocopy()
        try:
            dst_dir = os.path.dirname(os.path.abspath(dst))
            os.makedirs(dst_dir, exist_ok=True)
            flags = [rc, os.path.dirname(src), dst_dir,
                     os.path.basename(src), "/B", "/J", "/NFL", "/NDL",
                     "/NJH", "/NJS", "/R:1", "/W:1"]
            if not overwrite:
                flags.append("/XC")  # exclude changed (skip bila dst ada & sama)
            proc = subprocess.run(flags, stdout=subprocess.PIPE,
                                  stderr=subprocess.STDOUT, timeout=600)
            # robocopy exit 0/1 = sukses (1 = file disalin), >=8 gagal
            if proc.returncode < 8:
                return True
        except Exception:
            pass  # fallback ke shutil

    # Fallback / non-Windows
    try:
        os.makedirs(os.path.dirname(os.path.abspath(dst)), exist_ok=True)
        shutil.copy2(src, dst)
        return True
    except Exception:
        return False


def batch_copy(jobs, workers: int = 4, overwrite: bool = True,
               on_done=None, on_error=None, cancel_event=None):
    """Salin banyak (src, dst) paralel.

    Args:
      jobs: list of (src, dst) tuples.
      workers: jumlah thread salin (default 4 — aman untuk HDD & SSD).
      overwrite: timpa dst bila sudah ada.
      on_done(dst): dipanggil per file sukses (thread-safe, ringan).
      on_error(dst, err): dipanggil per file gagal.
      cancel_event: threading.Event; bila set() -> berhenti (tinggal di-flush).

    Returns: (done, skipped, failed) counts.
    """
    ev = cancel_event or _CANCEL_EVENT
    done = skipped = failed = 0
    if not jobs:
        return 0, 0, 0

    workers = max(1, min(workers, os.cpu_count() or 4, 8))

    def _one(item):
        nonlocal done, skipped, failed
        src, dst = item
        if ev.is_set():
            return
        if os.path.isfile(dst) and not overwrite:
            skipped += 1
            return
        ok = fast_copy(src, dst, overwrite=overwrite)
        if ok:
            done += 1
            if on_done:
                try: on_done(dst)
                except Exception: pass
        else:
            failed += 1
            if on_error:
                try: on_error(dst, "copy failed")
                except Exception: pass

    with ThreadPoolExecutor(max_workers=workers) as pool:
        futs = [pool.submit(_one, j) for j in jobs]
        for _ in as_completed(futs):
            if ev.is_set():
                for f in futs:
                    f.cancel()
                break

    return done, skipped, failed


# ---------------------------------------------------------------------------
# SCAN: os.walk sekali untuk semua ekstensi (bukan per-subfolder)
# ---------------------------------------------------------------------------
def scan_files(root: str, exts=(".jpg", ".jpeg")):
    """Kumpulkan semua file ber-ekstensi di `root` (rekursif).

    Returns: list of absolute paths (urut natural per folder).
    """
    from pathlib import Path
    exts = {e.lower() for e in exts}
    result = []
    for dirpath, _dirnames, filenames in os.walk(root):
        for fn in filenames:
            if os.path.splitext(fn)[1].lower() in exts:
                result.append(os.path.join(dirpath, fn))
    # Natural sort per folder parent
    def _key(p):
        d = os.path.dirname(p)
        b = os.path.basename(p)
        return (d, b)
    result.sort(key=_key)
    return result


if __name__ == "__main__":
    # Quick self-test
    print("PYTHON_FASTIO_SELFTEST")
    import tempfile
    tmp = tempfile.mkdtemp()
    s = os.path.join(tmp, "a.txt")
    with open(s, "w") as f:
        f.write("hello fastio")
    d = os.path.join(tmp, "out", "a.txt")
    ok = fast_copy(s, d)
    print(f"fast_copy ok={ok} dst_exists={os.path.isfile(d)}")
    jobs = [(s, os.path.join(tmp, "out", f"c{i}.txt")) for i in range(5)]
    dd, sk, ff = batch_copy(jobs, workers=3)
    print(f"batch_copy done={dd} skipped={sk} failed={ff}")
    print("FASTIO_SELFTEST_DONE")
