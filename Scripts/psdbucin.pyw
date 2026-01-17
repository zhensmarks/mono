# psdbucin.pyw (REWRITE: mirror folder structure)
# GUI (tkinter) • tanpa CMD (pythonw/.pyw)
# Semua JPG/JPEG di folder pilihan (termasuk subfolder) akan dibuatkan PSD/PSB di folder master dengan struktur folder yang sama (mirror).
# Semua notifikasi/dialog selalu center window.

import os, re, shutil, sys, json, tempfile
import tkinter as tk
from tkinter import filedialog, messagebox
import tkinter.scrolledtext as scrolledtext

ONLY_PAREN = re.compile(r'^\(\s*(\d+)\s*\)$')
SPACE_FORM = re.compile(r'^(\d+)\s*\(\s*\d+\s*\)(?:\b.*)?$')
TIGHT_FORM = re.compile(r'^\d+\(\s*(\d+)\s*\)$')

# --- BMachine Integration ---
def report_progress(current, total, filename):
    """Tulis progress ke file temp agar BMachine bisa membacanya."""
    try:
        progress_file = os.path.join(tempfile.gettempdir(), 'bmachine_progress.json')
        data = {'current': current, 'total': total, 'file': filename, 'status': 'processing'}
        with open(progress_file, 'w', encoding='utf-8') as f:
            json.dump(data, f)
    except Exception:
        pass  # Gagal tulis tidak fatal

# Center any Tk window/dialog
def center_window(win):
    win.update_idletasks()
    w = win.winfo_width()
    h = win.winfo_height()
    sw = win.winfo_screenwidth()
    sh = win.winfo_screenheight()
    x = (sw - w) // 2
    y = (sh - h) // 2
    win.geometry(f"{w}x{h}+{x}+{y}")

# --- Util ---
def pick_master(master_dir):
    masters = [f for f in os.listdir(master_dir)
               if f.lower().endswith(('.psd', '.psb'))
               and os.path.isfile(os.path.join(master_dir, f))]
    if not masters:
        raise RuntimeError("Tidak ada file .psd/.psb di folder MASTER.")
    masters.sort()
    return os.path.join(master_dir, masters[0])

def collect_jpgs_with_relpath(pilihan_dir):
    jpgs = []
    for root, _, files in os.walk(pilihan_dir):
        for fn in files:
            if fn.lower().endswith(('.jpg', '.jpeg')):
                full = os.path.join(root, fn)
                rel = os.path.relpath(full, pilihan_dir)
                jpgs.append((full, rel))
    if not jpgs:
        raise RuntimeError("Tidak ada JPG/JPEG di folder PILIHAN (termasuk subfolder).")
    return jpgs

def compute_target_name(jpg_name):
    n = os.path.splitext(jpg_name)[0].strip()
    m = ONLY_PAREN.match(n)
    if m:
        return m.group(1)
    m = SPACE_FORM.match(n)
    if m:
        return m.group(1)
    m = TIGHT_FORM.match(n)
    if m:
        return m.group(1)
    return n  # fallback: pakai nama asli

def show_result(root, master_file, master_ext, results, logs):
    win = tk.Toplevel(root)
    win.title("Ringkasan Mirror")
    win.geometry("350x350")
    win.minsize(300, 250)
    center_window(win)

    title = tk.Label(win, text="Ringkasan", font=("Segoe UI", 11, "bold"))
    title.pack(pady=(8, 4))

    txt = scrolledtext.ScrolledText(win, wrap="word")
    summary = []
    summary.append("Master  : " + os.path.basename(master_file))
    summary.append("Ekstensi: " + master_ext)
    summary.append(f"Total file: {len(results)}")
    summary.append("")
    summary.append("Detail:")
    summary.extend(logs)
    txt.insert("1.0", "\n".join(summary))
    txt.configure(state="disabled")
    # JANGAN .pack() txt dulu

    def close_and_exit():
        try:
            win.destroy()
        finally:
            try:
                root.quit()
            except Exception:
                pass
            try:
                root.destroy()
            except Exception:
                pass

    # --- PERUBAHAN DI SINI ---
    # 1. Buat tombol OK
    btn = tk.Button(win, text="OK", width=12, command=close_and_exit)
    
    # 2. Pack tombol di BAWAH jendela (side=tk.BOTTOM)
    #    Ini "memesan" tempat untuk tombol di bagian bawah.
    btn.pack(pady=(4, 10), side=tk.BOTTOM)
    
    # 3. SEKARANG pack area teks di atas tombol
    #    Ini akan mengisi sisa ruang yang tersedia.
    txt.pack(fill="both", expand=True, padx=8, pady=4)
    # --- AKHIR PERUBAHAN ---

    win.protocol("WM_DELETE_WINDOW", close_and_exit)
    center_window(win)

# --- Main ---
def run():
    root = tk.Tk()
    root.withdraw()
    root.update()
    center_window(root)

    master_dir = filedialog.askdirectory(title="Pilih folder MASTER (berisi PSD/PSB)", parent=root, mustexist=True)
    if not master_dir:
        root.destroy(); return
    center_window(root)

    pilihan_dir = filedialog.askdirectory(title="Pilih folder PILIHAN (akan dipindai JPG/JPEG RECURSIVE)", parent=root, mustexist=True)
    if not pilihan_dir:
        root.destroy(); return
    center_window(root)

    logs = []
    results = []
    master_file = pick_master(master_dir)
    master_ext = os.path.splitext(master_file)[1].lower()
    jpgs = collect_jpgs_with_relpath(pilihan_dir)

    total_files = len(jpgs)
    for idx, (full_jpg, rel_jpg) in enumerate(jpgs):
        # Report progress ke BMachine
        report_progress(idx + 1, total_files, os.path.basename(rel_jpg))
        
        target_name = compute_target_name(os.path.basename(rel_jpg))
        rel_folder = os.path.dirname(rel_jpg)
        target_folder = os.path.join(master_dir, rel_folder)
        os.makedirs(target_folder, exist_ok=True)
        dst = os.path.join(target_folder, f"{target_name}{master_ext}")
        try:
            if os.path.exists(dst):
                logs.append(f"LEWATI: {os.path.relpath(dst, master_dir)} sudah ada.")
            else:
                shutil.copy2(master_file, dst)
                logs.append(f"OK     : salin -> {os.path.relpath(dst, master_dir)}")
            results.append(dst)
        except Exception as e:
            logs.append(f"GAGAL  : {os.path.relpath(dst, master_dir)} ({e})")

    # Selesai processing
    messagebox.showinfo("Selesai", f"Proses selesai.\nTotal file: {len(results)}\nCek Log Panel BMachine untuk detail.")
    root.destroy()

def show_centered_error(msg):
    _tmp_root = tk.Tk(); _tmp_root.withdraw(); center_window(_tmp_root)
    messagebox.showerror("Error", msg, parent=_tmp_root)
    _tmp_root.destroy()

if __name__ == "__main__":
    try:
        run()
    except Exception as e:
        try:
            show_centered_error(str(e))
        except Exception:
            pass
        sys.exit(1)