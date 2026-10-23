#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
_photoshop_utils.py — Shared Photoshop COM + file-lock utilities
untuk copy-edit-sync workflow semua jenis master (Profesi, PasFoto, Manasik, Wisuda).
"""

import os
import sys
import time
import subprocess

try:
    import win32com.client
    HAS_WIN32COM = True
except ImportError:
    HAS_WIN32COM = False


def open_psd_files_in_photoshop(psd_paths: list, timeout_s: int = 120) -> bool:
    r"""
    Buka file PSD di Photoshop via COM.
    Prioritas: pywin32 (win32com) bila tersedia; fallback: PowerShell COM
    (New-Object -ComObject Photoshop.Application) — tanpa dependensi tambahan.
    
    Args:
        psd_paths: list of absolute PSD file paths
        timeout_s: waktu tunggu Photoshop siap (detik)
    
    Returns:
        True jika berhasil, False jika gagal
    
    Contoh:
        >>> open_psd_files_in_photoshop([r"D:\out\astronot.psd", r"D:\out\tni.psd"])
    """
    # Filter hanya file yang ada
    existing = [p for p in psd_paths if os.path.isfile(p)]
    if not existing:
        print("[WARNING] Tidak ada file PSD yang ditemukan untuk dibuka.", file=sys.stderr)
        return False

    if HAS_WIN32COM:
        try:
            print(f"[INFO] Membuka Photoshop COM (pywin32)...")
            ps_app = win32com.client.Dispatch("Photoshop.Application")

            print(f"[INFO] Membuka {len(existing)} file PSD di Photoshop...")
            for psd_path in existing:
                try:
                    print(f"  → {os.path.basename(psd_path)}")
                    ps_app.Open(psd_path)
                    time.sleep(0.5)  # Berikan waktu untuk load tab
                except Exception as e:
                    print(f"[WARNING] Gagal membuka {psd_path}: {e}", file=sys.stderr)

            print(f"[INFO] Semua file sudah dibuka. Tunggu user selesai edit + close tab...")
            return True
        except Exception as e:
            print(f"[ERROR] Gagal buka Photoshop COM (pywin32): {e}", file=sys.stderr)
            return False

    # Fallback: Photoshop COM lewat PowerShell (tanpa pywin32)
    return _open_via_powershell(existing)


def _open_via_powershell(psd_paths: list) -> bool:
    r"""
    Buka PSD di Photoshop via PowerShell COM — fallback ketika pywin32 absen.
    Proses powershell keluar setelah Open; dokumen tetap terbuka di Photoshop
    (Photoshop.Application tidak mati saat rilis COM reference).
    """
    import subprocess
    import tempfile
    ps1 = None
    try:
        items = ", ".join("'" + p.replace("'", "''") + "'" for p in psd_paths)
        script = (
            "$ErrorActionPreference = 'Stop'\n"
            "$ps = New-Object -ComObject Photoshop.Application\n"
            "$files = @(" + items + ")\n"
            "foreach ($f in $files) { $null = $ps.Open($f); Start-Sleep -Milliseconds 700 }\n"
            "Write-Output ('PS-OPEN-OK ' + $files.Count)\n"
        )
        fd, ps1 = tempfile.mkstemp(suffix=".ps1")
        os.close(fd)
        with open(ps1, "w", encoding="utf-8") as f:
            f.write(script)
        print(f"[INFO] Membuka Photoshop COM (PowerShell) untuk {len(psd_paths)} file...")
        r = subprocess.run(
            ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ps1],
            capture_output=True, text=True,
            timeout=max(90, 60 * len(psd_paths)),
        )
        out = (r.stdout or "") + (r.stderr or "")
        if "PS-OPEN-OK" in out:
            for p in psd_paths:
                print(f"  → {os.path.basename(p)}")
            print(f"[INFO] Semua file sudah dibuka. Tunggu user selesai edit + close tab...")
            return True
        print(f"[ERROR] PowerShell COM gagal membuka Photoshop: {out.strip()}", file=sys.stderr)
        return False
    except Exception as e:
        print(f"[ERROR] Gagal buka Photoshop COM (PowerShell): {e}", file=sys.stderr)
        return False
    finally:
        if ps1 and os.path.exists(ps1):
            try:
                os.remove(ps1)
            except Exception:
                pass


def watch_documents_closed(psd_paths, timeout_s=600):
    r"""
    Pantau Photoshop via COM (PowerShell watcher) sampai dokumen ditutup user.
    Yield path tiap dokumen tidak lagi terbuka di Photoshop — urutan bebas,
    bertahap (duplikasi bisa jalan sementara tab lain masih dibuka).

    PENTING: file PSD TIDAK dikunci Photoshop saat dokumen terbuka (terbukti di
    test nyata: file-lock selalu False → duplikasi prematur). Karena itu deteksi
    "user selesai edit + tutup" WAJIB lewat COM (Documents), bukan file lock.

    Returns generator; berhenti tanpa yield sisa bila timeout atau watcher gagal
    start (caller wajib caca sisa yang belum di-yield).
    """
    import subprocess
    import tempfile
    import threading
    import queue as _queue

    paths = list(psd_paths)
    if not paths:
        return

    fd_targets, targets_file = tempfile.mkstemp(suffix=".txt")
    os.close(fd_targets)
    with open(targets_file, "w", encoding="utf-8") as f:
        f.write("\n".join(paths))

    # Watcher: loop cek Photoshop.Documents; yield CLOSED per file yang hilang.
    # Basis match: FullName ATAU nama file (dokumen buka = Name pasti dikenal).
    # PENTING: saat Photoshop menampilkan DIALOG MODAL (mis save prompt), COM
    # mengembalikan Count=0 walau dokumen terbuka (terbukti probe) -> kalau salah
    # ditangani, semua target dianggap CLOSED (duplikasi prematur). Guard sehat:
    #   Count=0 + ActiveDocument MASIH bisa diakses -> modal/blackout -> skip poll
    #   Count=0 + ActiveDocument THROW             -> semua dokumen benar2 tertutup
    #   Count>0 + tak bisa baca nama sama sekali   -> query tak sehat -> skip poll
    watcher = (
        "$ErrorActionPreference = 'Stop'\n"
        "$targets = @(Get-Content '" + targets_file.replace("'", "''") + "' | Where-Object { $_ -ne '' })\n"
        "$remaining = @{}\n"
        "foreach ($t in $targets) { $remaining[$t] = $true }\n"
        "$ps = New-Object -ComObject Photoshop.Application\n"
        "Start-Sleep -Seconds 3\n"
        "while ($remaining.Count -gt 0) {\n"
        "  $count = -1\n"
        "  try { $count = $ps.Documents.Count } catch { $count = -1 }\n"
        "  $openFull = @{}\n"
        "  $openName = @{}\n"
        "  $gotAny = $false\n"
        "  if ($count -gt 0) {\n"
        "    foreach ($d in $ps.Documents) {\n"
        "      try { $fn = $d.FullName; if ($fn) { $openFull[$fn.ToLower()] = $true; $gotAny = $true } } catch {}\n"
        "      try { $nm = $d.Name; if ($nm) { $openName[$nm.ToLower()] = $true; $gotAny = $true } } catch {}\n"
        "    }\n"
        "  }\n"
        "  if ($count -gt 0 -and $gotAny) {\n"
        "    foreach ($t in @($remaining.Keys)) {\n"
        "      $base = [System.IO.Path]::GetFileName($t).ToLower()\n"
        "      $isOpen = $openFull.ContainsKey($t.ToLower()) -or $openName.ContainsKey($base)\n"
        "      if (-not $isOpen) { Write-Output ('CLOSED:' + $t); [void]$remaining.Remove($t) }\n"
        "    }\n"
        "  } elseif ($count -eq 0) {\n"
        "    $activeOk = $true\n"
        "    try { $null = $ps.ActiveDocument } catch { $activeOk = $false }\n"
        "    if (-not $activeOk) {\n"
        "      foreach ($t in @($remaining.Keys)) { Write-Output ('CLOSED:' + $t); [void]$remaining.Remove($t) }\n"
        "    }\n"
        "    # activeOk = modal/blackout -> diamkan, poll berikutnya\n"
        "  }\n"
        "  if ($remaining.Count -gt 0) { Start-Sleep -Milliseconds 1500 }\n"
        "}\n"
        "Write-Output 'ALL-CLOSED'\n"
    )
    fd_ps1, ps1 = tempfile.mkstemp(suffix=".ps1")
    os.close(fd_ps1)
    with open(ps1, "w", encoding="utf-8") as f:
        f.write(watcher)

    print("[INFO] Watcher Photoshop COM: menunggu user menutup tiap dokumen...")
    try:
        proc = subprocess.Popen(
            ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ps1],
            stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            text=True, encoding="utf-8", errors="replace",
        )
    except Exception as e:
        print(f"[ERROR] Gagal start watcher Photoshop COM: {e}", file=sys.stderr)
        for fpath in (targets_file, ps1):
            try: os.remove(fpath)
            except Exception: pass
        return

    q = _queue.Queue()

    def _reader():
        try:
            for line in proc.stdout:
                q.put(line.strip())
        except Exception:
            pass
        q.put(None)  # EOF

    t = threading.Thread(target=_reader, daemon=True)
    t.start()

    deadline = time.time() + timeout_s
    try:
        while True:
            remain = deadline - time.time()
            if remain <= 0:
                print(f"[WARNING] Timeout watcher ({timeout_s}s) — sisa dokumen tidak terdeteksi ditutup.", file=sys.stderr)
                return
            try:
                line = q.get(timeout=min(remain, 1.0))
            except _queue.Empty:
                continue
            if line is None:
                err = ""
                try:
                    err = (proc.stderr.read() or "").strip()
                except Exception:
                    pass
                print(f"[WARNING] Watcher Photoshop keluar lebih awal. {err}", file=sys.stderr)
                return
            if line.startswith("CLOSED:"):
                yield line[len("CLOSED:"):].strip()
            elif line == "ALL-CLOSED":
                return
    finally:
        try:
            if proc.poll() is None:
                proc.terminate()
        except Exception:
            pass
        for fpath in (targets_file, ps1):
            try: os.remove(fpath)
            except Exception: pass


def is_file_locked(file_path: str) -> bool:
    r"""
    Deteksi apakah file dikunci (dijalankan proses lain dengan share-mode tertutup).
    Catatan: Photoshop TIDAK mengunci file PSD saat dokumen terbuka — jangan
    pakai fungsi ini sebagai satu-satunya sinyal "user sedang edit". Lihat
    watch_documents_closed() untuk deteksi via COM.
    """
    if not os.path.isfile(file_path):
        return False
    
    try:
        with open(file_path, 'a'):
            pass
        return False
    except IOError:
        return True


def wait_for_file_unlock(file_path: str, timeout_s: int = 600, poll_interval_s: int = 2) -> bool:
    r"""
    Tunggu hingga file tidak lagi dikunci (user close di Photoshop).
    Polling dengan interval poll_interval_s detik.
    
    Args:
        file_path: absolute path ke PSD
        timeout_s: timeout total (detik, default 10 menit)
        poll_interval_s: interval polling (detik)
    
    Returns:
        True jika file terbuka (unlock), False jika timeout
    
    Contoh:
        >>> wait_for_file_unlock(r"D:\out\astronot.psd", timeout_s=600)
    """
    start_time = time.time()
    print(f"[INFO] Menunggu file dilepas: {os.path.basename(file_path)}")
    
    while time.time() - start_time < timeout_s:
        if not is_file_locked(file_path):
            elapsed = time.time() - start_time
            print(f"[INFO] File dilepas setelah {elapsed:.1f} detik: {os.path.basename(file_path)}")
            return True
        
        time.sleep(poll_interval_s)
    
    elapsed = time.time() - start_time
    print(f"[WARNING] Timeout menunggu file (>{timeout_s}s): {os.path.basename(file_path)}", file=sys.stderr)
    return False


def duplicate_psd(source_psd: str, output_dir: str, photo_count: int, base_name_prefix: str = "") -> int:
    r"""
    Duplikasi 1 PSD menjadi N file bernama 1.psd, 2.psd, ..., N.psd.
    
    Args:
        source_psd: path PSD yang sudah diedit, akan direplikasi
        output_dir: folder output untuk hasil duplikasi
        photo_count: jumlah duplikasi yang diinginkan
        base_name_prefix: prefix nama file (default "")
                         mis. "astronot_" → astronot_1.psd, astronot_2.psd, ...
    
    Returns:
        jumlah file yang berhasil diduplikasi
    
    Contoh:
        >>> duplicate_psd(r"D:\out\astronot.psd", r"D:\out", 5)
        # Hasilkan: 1.psd, 2.psd, 3.psd, 4.psd, 5.psd
    """
    if not os.path.isfile(source_psd):
        print(f"[ERROR] File sumber tidak ditemukan: {source_psd}", file=sys.stderr)
        return 0
    
    if not os.path.isdir(output_dir):
        try:
            os.makedirs(output_dir, exist_ok=True)
        except Exception as e:
            print(f"[ERROR] Gagal membuat output dir: {e}", file=sys.stderr)
            return 0
    
    count = 0
    for i in range(1, photo_count + 1):
        dst_name = f"{base_name_prefix}{i}.psd" if base_name_prefix else f"{i}.psd"
        dst_path = os.path.join(output_dir, dst_name)
        
        try:
            import shutil
            shutil.copy2(source_psd, dst_path)
            count += 1
            print(f"[INFO] Duplikasi: {dst_name}")
        except Exception as e:
            print(f"[WARNING] Gagal duplikasi {dst_name}: {e}", file=sys.stderr)
    
    return count


def manifest_add(manifest_path, edit_psd, label, jobs):
    r"""
    Tambah/merge satu entry ke manifest JSON copy-edit.
    Entry: {"edit_psd": <psd yang dibuka utk diedit>, "label": <nama item>,
            "jobs": [<path output tujuan duplikat>, ...]}
    Entry dengan edit_psd sama akan di-merge (jobs digabung, dedupe).
    """
    import json as _json
    data = {"entries": []}
    if os.path.isfile(manifest_path):
        try:
            with open(manifest_path, "r", encoding="utf-8") as f:
                loaded = _json.load(f)
            if isinstance(loaded, dict) and isinstance(loaded.get("entries"), list):
                data = loaded
        except Exception as e:
            print(f"[WARNING] Manifest lama gagal dibaca, dibuat baru: {e}", file=sys.stderr)

    norm = os.path.normcase(os.path.abspath(edit_psd))
    for entry in data["entries"]:
        if os.path.normcase(os.path.abspath(entry.get("edit_psd", ""))) == norm:
            have = {os.path.normcase(j) for j in entry.get("jobs", [])}
            for j in jobs:
                if os.path.normcase(os.path.abspath(j)) not in have:
                    entry.setdefault("jobs", []).append(j)
            break
    else:
        data["entries"].append({"edit_psd": edit_psd, "label": label, "jobs": list(jobs)})

    os.makedirs(os.path.dirname(os.path.abspath(manifest_path)) or ".", exist_ok=True)
    with open(manifest_path, "w", encoding="utf-8") as f:
        _json.dump(data, f, ensure_ascii=False, indent=2)
    return data


def run_copy_edit_phase2(manifest_path, timeout_s=1800):
    r"""
    FASE 2 (sinkronisasi): baca manifest -> buka SEMUA edit_psd di Photoshop
    SEKALIGUS -> pantau via COM -> tiap dokumen ditutup user, duplikasi semua
    jobs-nya (SKIP_EXISTING, nama & struktur folder sudah dihitung fase 1).

    Returns:
        True kalau SEMUA entry selesai; False kalau ada yang tersisa/skip.
    """
    import json as _json
    import shutil as _shutil

    if not os.path.isfile(manifest_path):
        print(f"[ERROR] Manifest tidak ditemukan: {manifest_path}", file=sys.stderr)
        return False
    with open(manifest_path, "r", encoding="utf-8") as f:
        data = _json.load(f)
    entries = [e for e in data.get("entries", []) if e.get("edit_psd")]
    if not entries:
        print("[INFO] Manifest kosong — tidak ada yang dibuka di Photoshop.")
        return True

    n_jobs = sum(len(e.get("jobs", [])) for e in entries)
    print(f"[SYNC] Manifest: {len(entries)} master edit, {n_jobs} tujuan duplikat.")

    edit_paths = [e["edit_psd"] for e in entries]
    missing = [p for p in edit_paths if not os.path.isfile(p)]
    if missing:
        for p in missing:
            print(f"[ERROR] File edit tidak ada: {p}", file=sys.stderr)
        return False

    if not open_psd_files_in_photoshop(edit_paths):
        print("[ERROR] Photoshop gagal dibuka — sinkronisasi dibatalkan. "
              "Jalankan ulang, atau pakai --mode=direct bila tanpa Photoshop.", file=sys.stderr)
        return False

    time.sleep(2)

    by_path = {os.path.normcase(os.path.abspath(e["edit_psd"])): e for e in entries}
    done_labels, done_jobs = set(), 0

    print(f"\n[WAIT] Tunggu user edit + tutup tiap dokumen (timeout {timeout_s}s)...")
    for closed in watch_documents_closed(edit_paths, timeout_s=timeout_s):
        entry = by_path.get(os.path.normcase(os.path.abspath(closed)))
        if entry is None or entry["label"] in done_labels:
            continue
        label = entry["label"]
        print(f"  [DITUTUP] {label} ({os.path.basename(closed)}) → sinkron {len(entry['jobs'])} file")
        for job in entry["jobs"]:
            if os.path.isfile(job):
                print(f"    - SKIP_EXISTING: {os.path.basename(job)}")
                continue
            try:
                os.makedirs(os.path.dirname(job), exist_ok=True)
                _shutil.copy2(closed, job)
                done_jobs += 1
                print(f"    - [OK] {os.path.basename(job)}")
            except Exception as e:
                print(f"    - [ERROR] {job}: {e}", file=sys.stderr)
        done_labels.add(label)

    leftover = [e["label"] for e in entries if e["label"] not in done_labels]
    for lab in leftover:
        print(f"  [SKIP] {lab}: tidak terdeteksi ditutup dalam {timeout_s}s — duplikasi DIBATALKAN.",
              file=sys.stderr)

    all_done = not leftover
    print(f"\n[SELESAI-SYNC] {len(done_labels)}/{len(entries)} master, {done_jobs} file tersalin.")
    print(f"SYNC_JSON:{_json.dumps({'entries': len(entries), 'done': len(done_labels), 'jobs_done': done_jobs})}")
    return all_done


# ---------------------------------------------------------------------------
# MANUAL (tombol) mode: manifest stabil + duplikasi atas permintaan user.
# Alur lama (fase 2) otomatis: buka -> tunggu tab ditutup -> gandakan. User tidak
# tahu kapan prosesnya jalan. Dengan fungsi di bawah, penggandaan jadi AKSI
# eksplisit: fase 1 tulis manifest ke path stabil, user edit PS sesuka hati,
# lalu klik tombol DUPLIKAT PSD.
# ---------------------------------------------------------------------------

MANIFEST_DIRNAME = "_bm_copy_edit"


def manifest_path_for(pilihan_path: str, output_base: str = "") -> str:
    """Path manifest STABIL per folder PILIHAN (bisa ditemukan lagi setelah proses lama mati).

    Manifest lama memakai nama temp ber-pid (`bm_master_manifest_<pid>.json`), jadi
    hilang dari jangkauan begitu proses fase 1 selesai — tombol terpisah tidak bisa
    mencarinya. Kunci = hash folder PILIHAN, supaya satu kali proses buat-master
    selalu bisa ditemukan ulang dari `<output_base>/_bm_copy_edit/manifest_<hash>.json`.
    """
    import hashlib
    import tempfile

    base = output_base or tempfile.gettempdir()
    key = hashlib.sha1(os.path.normcase(os.path.abspath(pilihan_path)).encode("utf-8")).hexdigest()[:12]
    folder = os.path.join(base, MANIFEST_DIRNAME)
    os.makedirs(folder, exist_ok=True)
    return os.path.join(folder, f"manifest_{key}.json")


def load_manifest(manifest_path: str) -> list:
    """Entry manifest yang punya ``edit_psd`` (list kosong kalau file tidak ada/rusak)."""
    import json as _json

    if not manifest_path or not os.path.isfile(manifest_path):
        return []
    try:
        with open(manifest_path, "r", encoding="utf-8") as f:
            data = _json.load(f)
    except Exception as e:
        print(f"[ERROR] Manifest tidak bisa dibaca ({manifest_path}): {e}", file=sys.stderr)
        return []
    return [e for e in (data.get("entries") or []) if e.get("edit_psd")]


def open_edit_psds(manifest_path: str) -> bool:
    """Buka tiap ``edit_psd`` manifest di Photoshop — TANPA menunggu, TANPA duplikasi."""
    entries = load_manifest(manifest_path)
    if not entries:
        print(f"[WARNING] Tidak ada template edit di manifest: {manifest_path}", file=sys.stderr)
        return False
    paths = [e["edit_psd"] for e in entries]
    print(f"[INFO] Membuka {len(paths)} template edit di Photoshop (tanpa auto-duplikat)...")
    return open_psd_files_in_photoshop(paths)


def run_duplicate_only(manifest_path: str, skip_existing: bool = True, overwrite_manual: bool = False) -> bool:
    """TOMBOL DUPLIKAT: salin tiap ``edit_psd`` ke semua job-nya. Tanpa Photoshop, tanpa watcher.

    ``skip_existing`` (default) menjaga file yang sudah ada — aman ditekan berulang.
    ``overwrite_manual`` = True memaksa timpa file yang sudah ada (untuk master yang
    kamu input manual di fase 1 tak terdeteksi jadi, lalu kamu salin sendiri ke output).
    Menggunakan _fastio batch_copy paralel (4 worker + robocopy /J unbuffered) agar
    I/O file besar (GB-an) tidak membanjiri OS cache dan tidak membuat window lain beku.
    """
    import json as _json
    import shutil as _shutil

    _fast_batch_copy = None
    try:
        from _fastio import batch_copy as _fast_batch_copy
        has_fastio = True
    except Exception:
        has_fastio = False

    entries = load_manifest(manifest_path)
    if not entries:
        print(f"[ERROR] Manifest kosong/tidak ada: {manifest_path}", file=sys.stderr)
        print("[ERROR] Jalankan dulu tombol master (BUAT MASTER / MANASIK / PAS FOTO / WISUDA / PROFESI).",
              file=sys.stderr)
        return False

    total_jobs = sum(len(e.get("jobs") or []) for e in entries)
    print(f"[DUPLIKAT] Manifest: {manifest_path}")
    print(f"[DUPLIKAT] {len(entries)} template edit -> {total_jobs} tujuan.")

    # Kumpulkan semua target pekerjaan
    all_copy_jobs = []
    skipped = 0
    for e in entries:
        src = e["edit_psd"]
        label = e.get("label") or os.path.basename(src)
        if not os.path.isfile(src):
            print(f"[ERROR] Template edit tidak ada: {src}", file=sys.stderr)
            continue
        print(f"  [SUMBER] {label}: {os.path.basename(src)} ({os.path.getsize(src) / 1048576:.1f} MB)")
        for job in (e.get("jobs") or []):
            if skip_existing and not overwrite_manual and os.path.isfile(job):
                print(f"    - SKIP_EXISTING: {os.path.basename(job)}")
                skipped += 1
                continue
            if overwrite_manual and os.path.isfile(job):
                print(f"    - [TIMPA] {os.path.basename(job)} (overwrite manual)")
            all_copy_jobs.append((src, job))

    done = failed = 0
    if all_copy_jobs:
        if has_fastio and _fast_batch_copy:
            print(f"\n--- MENYALIN {len(all_copy_jobs)} FILE (paralel 4 worker) ---")
            done, sk, failed = _fast_batch_copy(
                all_copy_jobs, workers=4, overwrite=True,
                on_done=lambda d: print(f"    - [OK] {os.path.basename(d)}"),
                on_error=lambda d, err: print(f"    - [ERROR] {d}: {err}", file=sys.stderr)
            )
            skipped += sk
        else:
            for src, job in all_copy_jobs:
                try:
                    os.makedirs(os.path.dirname(job), exist_ok=True)
                    _shutil.copy2(src, job)
                    done += 1
                    print(f"    - [OK] {os.path.basename(job)}")
                except Exception as ex:
                    failed += 1
                    print(f"    - [ERROR] {job}: {ex}", file=sys.stderr)

    print(f"\n[SELESAI-DUPLIKAT] {done} disalin, {skipped} sudah ada (skip), {failed} gagal.")
    print(f"SYNC_JSON:{_json.dumps({'entries': len(entries), 'jobs_done': done, 'skipped': skipped, 'failed': failed})}")
    return failed == 0


if __name__ == "__main__":
    # CLI: copy_edit_sync.py <manifest.json> [timeout_detik]
    #      copy_edit_sync.py <manifest.json> --duplikat            (tanpa Photoshop/watcher)
    #      copy_edit_sync.py <manifest.json> --buka-edit           (buka template saja)
    import json as _json
    if os.name == "nt":
        try:
            sys.stdout.reconfigure(encoding="utf-8")
            sys.stderr.reconfigure(encoding="utf-8")
        except Exception:
            pass
    if len(sys.argv) < 2:
        print("Usage: copy_edit_sync.py <manifest.json> [timeout_detik|--duplikat|--buka-edit]", file=sys.stderr)
        sys.exit(2)
    _flags = [a.lower() for a in sys.argv[2:]]
    if "--duplikat" in _flags:
        sys.exit(0 if run_duplicate_only(sys.argv[1]) else 1)
    if "--buka-edit" in _flags:
        sys.exit(0 if open_edit_psds(sys.argv[1]) else 1)
    _timeout = int(sys.argv[2]) if len(sys.argv) > 2 else 1800
    _ok = run_copy_edit_phase2(sys.argv[1], timeout_s=_timeout)
    sys.exit(0 if _ok else 1)
