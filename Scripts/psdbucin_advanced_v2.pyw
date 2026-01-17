# psdbucin_advanced_v2.pyw
# Advanced PSD Bucin V2.2
# Features:
# - Startup UI (Large Drag & Drop Zones)
# - Global Window Persistence
# - Manual PSD Selection with Preview
# - Dark Theme & Modern UI
# - Elegant Report Window (Treeview)

import os
import re
import shutil
import sys
import json
import threading
import tkinter as tk
from tkinter import filedialog, messagebox, ttk
from PIL import Image, ImageTk

# --- Optional Drag & Drop Support ---
try:
    from tkinterdnd2 import DND_FILES, TkinterDnD
    HAS_DND = True
except ImportError:
    HAS_DND = False

# --- Constants ---
SETTINGS_FILE = os.path.join(os.path.expanduser("~"), ".psdbucin_v2_settings.json")
DEFAULT_WIDTH = 1000
DEFAULT_HEIGHT = 700

# Theme Constants
COLOR_BG = "#18181b"          # Zinc-950
COLOR_SURFACE = "#27272a"     # Zinc-800
COLOR_SURFACE_HOVER = "#3f3f46" # Zinc-700
COLOR_FG = "#f4f4f5"          # Zinc-100
COLOR_ACCENT_BLUE = "#3b82f6" # Blue-500
COLOR_ACCENT_GREEN = "#22c55e"# Green-500
COLOR_ACCENT_RED = "#ef4444"  # Red-500
COLOR_BORDER = "#52525b"      # Zinc-600

FONT_MAIN = ("Segoe UI", 10)
FONT_BOLD = ("Segoe UI", 10, "bold")
FONT_TITLE = ("Segoe UI", 16, "bold")
FONT_BIG = ("Segoe UI", 24, "bold")
FONT_HINT = ("Segoe UI", 9)

# Regex
ONLY_PAREN = re.compile(r'^\(\s*(\d+)\s*\)$')
SPACE_FORM = re.compile(r'^(\d+)\s*\(\s*\d+\s*\)(?:\b.*)?$')
TIGHT_FORM = re.compile(r'^\d+\(\s*(\d+)\s*\)$')


# --- Settings Management ---
def load_settings():
    defaults = {
        "window_width": DEFAULT_WIDTH, 
        "window_height": DEFAULT_HEIGHT,
        "window_x": -1,
        "window_y": -1,
        "is_maximized": False,
        "shortcuts": [str(i+1) for i in range(9)],
        "last_master": "",
        "last_pilihan": ""
    }
    try:
        if os.path.exists(SETTINGS_FILE):
            with open(SETTINGS_FILE, 'r') as f:
                data = json.load(f)
                defaults.update(data)
    except Exception:
        pass
    return defaults

def save_settings(data):
    try:
        current = load_settings()
        current.update(data)
        with open(SETTINGS_FILE, 'w') as f:
            json.dump(current, f)
    except Exception:
        pass

# --- Utility Functions ---
def collect_psd_masters(master_dir):
    try:
         masters = [f for f in os.listdir(master_dir)
                   if f.lower().endswith(('.psd', '.psb'))
                   and os.path.isfile(os.path.join(master_dir, f))]
    except Exception:
        return []
    if not masters: return []
    masters.sort()
    return [(f, os.path.join(master_dir, f)) for f in masters]

def collect_jpgs_with_relpath(pilihan_dir):
    jpgs = []
    try:
        for root, _, files in os.walk(pilihan_dir):
            for fn in files:
                if fn.lower().endswith(('.jpg', '.jpeg')):
                    full = os.path.join(root, fn)
                    rel = os.path.relpath(full, pilihan_dir)
                    jpgs.append((full, rel))
    except Exception:
        return []
    jpgs.sort(key=lambda x: x[1].lower())
    return jpgs

def compute_target_name(jpg_name):
    n = os.path.splitext(jpg_name)[0].strip()
    m = ONLY_PAREN.match(n)
    if m: return m.group(1)
    m = SPACE_FORM.match(n)
    if m: return m.group(1)
    m = TIGHT_FORM.match(n)
    if m: return m.group(1)
    return n

def center_window(win):
    win.update_idletasks()
    w = win.winfo_width()
    h = win.winfo_height()
    if w < 200: w = DEFAULT_WIDTH
    if h < 200: h = DEFAULT_HEIGHT
    sw = win.winfo_screenwidth()
    sh = win.winfo_screenheight()
    x = (sw - w) // 2
    y = (sh - h) // 2
    win.geometry(f"{w}x{h}+{x}+{y}")

# --- Components ---
class LoadingSpinner(tk.Canvas):
    def __init__(self, master, size=60, bg=COLOR_BG, **kwargs):
        super().__init__(master, width=size, height=size, bg=bg, highlightthickness=0, **kwargs)
        self.size = size
        self.angle = 0
        self.running = False
        
    def start(self):
        if not self.running:
            self.running = True
            self.animate()
    def stop(self):
        self.running = False
        self.delete("all")
    def animate(self):
        if not self.running: return
        self.delete("all")
        extent = 120
        start = self.angle
        x0, y0 = 5, 5
        x1, y1 = self.size - 5, self.size - 5
        self.create_arc(x0, y0, x1, y1, start=start, extent=extent, style="arc", outline=COLOR_ACCENT_BLUE, width=5)
        self.create_arc(x0, y0, x1, y1, start=start+180, extent=extent, style="arc", outline=COLOR_ACCENT_GREEN, width=5)
        self.angle = (self.angle + 20) % 360
        self.after(40, self.animate)

class StatCard(tk.Frame):
    def __init__(self, parent, title, value, color, **kwargs):
        super().__init__(parent, bg=COLOR_SURFACE, padx=15, pady=10, **kwargs)
        tk.Label(self, text=str(value), font=("Segoe UI", 24, "bold"), fg=color, bg=COLOR_SURFACE).pack(anchor="w")
        tk.Label(self, text=title, font=("Segoe UI", 10), fg="#a1a1aa", bg=COLOR_SURFACE).pack(anchor="w")

class DropZone(tk.Frame):
    def __init__(self, master, title, icon="📂", initial_path="", on_change=None, **kwargs):
        super().__init__(master, bg=COLOR_SURFACE, highlightbackground=COLOR_BORDER, highlightthickness=2, cursor="hand2", **kwargs)
        self.on_change = on_change
        self.path = initial_path
        self.title_text = title
        self.default_icon = icon
        
        # UI Elements
        self.inner = tk.Frame(self, bg=COLOR_SURFACE)
        self.inner.place(relx=0.5, rely=0.5, anchor="center")
        
        self.lbl_icon = tk.Label(self.inner, text=icon, font=("Segoe UI Emoji", 48), bg=COLOR_SURFACE, fg="#71717a")
        self.lbl_icon.pack()
        
        self.lbl_title = tk.Label(self.inner, text=title, font=FONT_TITLE, bg=COLOR_SURFACE, fg="#a1a1aa")
        self.lbl_title.pack(pady=(5,0))
        
        self.lbl_path = tk.Label(self.inner, text="Klik atau Drop Folder disini", font=FONT_HINT, bg=COLOR_SURFACE, fg="#52525b", wraplength=300)
        self.lbl_path.pack(pady=(5,0))
        
        # Events
        self.bind("<Button-1>", self.browse)
        self.inner.bind("<Button-1>", self.browse)
        self.lbl_icon.bind("<Button-1>", self.browse)
        self.lbl_title.bind("<Button-1>", self.browse)
        self.lbl_path.bind("<Button-1>", self.browse)
        
        # Right click to clear
        self.bind("<Button-3>", self.clear)
        self.inner.bind("<Button-3>", self.clear)
        self.lbl_icon.bind("<Button-3>", self.clear)
        self.lbl_title.bind("<Button-3>", self.clear)
        self.lbl_path.bind("<Button-3>", self.clear)
        
        self.bind("<Enter>", self.on_hover)
        self.bind("<Leave>", self.on_leave)
        
        if HAS_DND:
            self.drop_target_register(DND_FILES)
            self.dnd_bind('<<Drop>>', self.on_drop)
            
        self.update_display(self.path)

    def on_hover(self, e):
        self.config(highlightbackground=COLOR_ACCENT_BLUE)
        
    def on_leave(self, e):
        self.config(highlightbackground=COLOR_BORDER)
        
    def browse(self, e=None):
        d = filedialog.askdirectory()
        if d: self.set_path(d)
        
    def clear(self, e=None):
        self.set_path("")
            
    def on_drop(self, event):
        path = event.data
        if path:
            if path.startswith('{') and path.endswith('}'): path = path[1:-1]
            if os.path.isdir(path): self.set_path(path)
            
    def set_path(self, path):
        self.path = path.replace("/", "\\")
        self.update_display(self.path)
        if self.on_change: self.on_change()
        
    def update_display(self, path):
        if path:
            self.lbl_icon.config(fg=COLOR_ACCENT_BLUE)
            self.lbl_title.config(fg="white")
            self.lbl_path.config(text=str(path), fg=COLOR_ACCENT_GREEN)
        else:
            self.lbl_icon.config(fg="#71717a")
            self.lbl_title.config(fg="#a1a1aa")
            self.lbl_path.config(text="(Klik Kanan untuk Hapus)\nKlik atau Drop Folder disini", fg="#52525b")
        
    def get_path(self):
        return self.path

# ... (StatCard same as before) ...

# --- Main Application ---
class BucinAppV2:
    def __init__(self, root):
        self.root = root
        self.root.title("Advanced PSD Bucin V2.3")
        self.root.configure(bg=COLOR_BG)
        
        self.settings = load_settings()
        
        # Restore Window Geometry
        w = self.settings.get("window_width", DEFAULT_WIDTH)
        h = self.settings.get("window_height", DEFAULT_HEIGHT)
        x = self.settings.get("window_x", -1)
        y = self.settings.get("window_y", -1)
        
        if x != -1 and y != -1:
            self.root.geometry(f"{w}x{h}+{x}+{y}")
        else:
            # First run center
            sw = self.root.winfo_screenwidth()
            sh = self.root.winfo_screenheight()
            cx = (sw - w) // 2
            cy = (sh - h) // 2
            self.root.geometry(f"{w}x{h}+{cx}+{cy}")
            
        self.root.minsize(800, 600)
        
        if self.settings.get("is_maximized", False):
            self.root.state('zoomed')
            
        # Global Close Handler
        self.root.protocol("WM_DELETE_WINDOW", self.on_app_close)
        
        self.psd_masters = []
        self.jpgs = []
        
        self.setup_ui_setup()
        
    def on_app_close(self):
        # Save Geometry
        is_maximized = self.root.state() == 'zoomed'
        self.settings["is_maximized"] = is_maximized
        
        if not is_maximized:
            self.settings["window_width"] = self.root.winfo_width()
            self.settings["window_height"] = self.root.winfo_height()
            self.settings["window_x"] = self.root.winfo_x()
            self.settings["window_y"] = self.root.winfo_y()
            
        save_settings(self.settings)
        self.root.destroy()
            
    def clear_ui(self):
        for widget in self.root.winfo_children():
            widget.destroy()
            
    # --- UI: Setup Page ---
    def setup_ui_setup(self):
        self.clear_ui()
        
        # Main Grid
        self.root.columnconfigure(0, weight=1)
        self.root.columnconfigure(1, weight=1)
        self.root.rowconfigure(0, weight=1) # Drop zones
        self.root.rowconfigure(1, weight=0) # Button
        
        # Drop ZonesContainer
        zone_frame = tk.Frame(self.root, bg=COLOR_BG)
        zone_frame.grid(row=0, column=0, columnspan=2, sticky="nsew", padx=20, pady=20)
        zone_frame.columnconfigure(0, weight=1)
        zone_frame.columnconfigure(1, weight=1)
        zone_frame.rowconfigure(0, weight=1)
        
        self.dz_master = DropZone(zone_frame, "Folder Master (PSD)", icon="🎨", 
                                  initial_path=self.settings.get("last_master", ""), 
                                  on_change=self.validate_inputs)
        self.dz_master.grid(row=0, column=0, sticky="nsew", padx=10, pady=10)
        
        self.dz_pilihan = DropZone(zone_frame, "Folder Foto (JPG)", icon="📷", 
                                   initial_path=self.settings.get("last_pilihan", ""), 
                                   on_change=self.validate_inputs)
        self.dz_pilihan.grid(row=0, column=1, sticky="nsew", padx=10, pady=10)
        
        # Footer Action
        footer = tk.Frame(self.root, bg=COLOR_BG)
        footer.grid(row=1, column=0, columnspan=2, sticky="ew", padx=20, pady=20)
        
        self.lbl_status = tk.Label(footer, text="Silakan pilih folder...", font=FONT_TITLE, bg=COLOR_BG, fg="#52525b")
        self.lbl_status.pack(side="left")
        
        self.btn_proses = tk.Button(footer, text="MULAI >", font=FONT_BOLD, 
                                    bg=COLOR_SURFACE, fg="#71717a", relief="flat", cursor="hand2",
                                    command=self.start_process, padx=40, pady=15, state="disabled")
        self.btn_proses.pack(side="right")
        
        self.validate_inputs() 

    def validate_inputs(self):
        m = self.dz_master.get_path()
        p = self.dz_pilihan.get_path()
        
        if not m or not p:
            self.lbl_status.config(text="Pilih kedua folder dulu...", fg="#52525b")
            self.btn_proses.config(state="disabled", bg=COLOR_SURFACE, fg="#71717a")
            return
            
        # Quick validation
        if not os.path.isdir(m):
             self.lbl_status.config(text="⚠️ Folder Master tidak valid", fg=COLOR_ACCENT_RED)
             return
        if not os.path.isdir(p):
             self.lbl_status.config(text="⚠️ Folder Foto tidak valid", fg=COLOR_ACCENT_RED)
             return
             
        # Count files (lightweight)
        try:
            n_psd = len([x for x in os.listdir(m) if x.lower().endswith(('.psd','.psb'))])
            # For JPGs just check root for speed or walk? Walk is safer
            n_jpg = 0
            for r, _, f in os.walk(p):
                for x in f:
                    if x.lower().endswith(('.jpg','.jpeg')): n_jpg += 1
                    
            if n_psd == 0:
                self.lbl_status.config(text="⚠️ Tidak ada PSD di folder Master", fg=COLOR_ACCENT_RED)
                self.btn_proses.config(state="disabled", bg=COLOR_SURFACE, fg="#71717a")
                return
            if n_jpg == 0:
                self.lbl_status.config(text="⚠️ Tidak ada JPG di folder Foto", fg=COLOR_ACCENT_RED)
                self.btn_proses.config(state="disabled", bg=COLOR_SURFACE, fg="#71717a")
                return
                
            self.lbl_status.config(text=f"Siap: {n_psd} Template • {n_jpg} Foto", fg=COLOR_ACCENT_GREEN)
            self.btn_proses.config(state="normal", bg=COLOR_ACCENT_BLUE, fg="white")
            
        except Exception as e:
            self.lbl_status.config(text=f"Error: {str(e)}", fg=COLOR_ACCENT_RED)

    def start_process(self):
        self.btn_proses.config(state="disabled", text="MEMUAT...", cursor="watch")
        self.root.update() # Force UI update
        
        try:
            m = self.dz_master.get_path()
            p = self.dz_pilihan.get_path()
            
            # Re-verify existence to be safe
            if not os.path.exists(m) or not os.path.exists(p):
                raise ValueError("Salah satu folder tidak dapat diakses.")
            
            self.psd_masters = collect_psd_masters(m)
            if not self.psd_masters:
                raise ValueError("Tidak ditemukan file PSD di folder Master.")
                
            self.jpgs = collect_jpgs_with_relpath(p)
            if not self.jpgs:
                raise ValueError("Tidak ditemukan file JPG di folder Foto.")
            
            self.settings["last_master"] = m
            self.settings["last_pilihan"] = p
            # Don't save settings immediately here if it risks lag, defer to close or finish
            
            self.setup_ui_processing(m)
            
        except Exception as e:
            messagebox.showerror("Gagal Memulai", f"Terjadi kesalahan:\n{str(e)}")
            self.btn_proses.config(state="normal", text="MULAI >", cursor="hand2")
            self.validate_inputs() # Re-validate to reset UI state

    # --- UI: Processing Page ---
    def setup_ui_processing(self, master_dir):
        self.clear_ui()
        # Clean grid weights from setup
        self.root.columnconfigure(0, weight=1)
        self.root.columnconfigure(1, weight=0)
        self.root.rowconfigure(0, weight=0)
        self.root.rowconfigure(1, weight=1)
        self.root.rowconfigure(2, weight=0)
        
        self.master_dir = master_dir
        self.current_idx = -1
        self.logs = []
        self.results = []
        self.is_processing = False
        self.original_image = None
        self.photo = None
        self.psd_buttons = []
        self.shortcuts = self.settings.get("shortcuts", [str(i+1) for i in range(9)])
        
        # Top Bar
        top_bar = tk.Frame(self.root, bg=COLOR_BG)
        top_bar.grid(row=0, column=0, sticky="ew", padx=20, pady=10)
        
        self.lbl_progress = tk.Label(top_bar, text="0/0", font=FONT_BOLD, bg=COLOR_BG, fg=COLOR_ACCENT_BLUE)
        self.lbl_progress.pack(side="left")
        
        tk.Button(top_bar, text="⚙", command=self.open_settings, bg=COLOR_SURFACE, fg="white", relief="flat", cursor="hand2", width=3).pack(side="right")
        
        self.lbl_filename = tk.Label(top_bar, text="Loading...", font=FONT_TITLE, bg=COLOR_BG, fg="white")
        self.lbl_filename.place(relx=0.5, rely=0.5, anchor="center")
        
        # Content
        content = tk.Frame(self.root, bg="#000000")
        content.grid(row=1, column=0, sticky="nsew", padx=20, pady=5)
        
        self.canvas = tk.Canvas(content, bg="#000000", highlightthickness=0)
        self.canvas.pack(fill="both", expand=True)
        
        # Overlay
        self.overlay = tk.Frame(content, bg="#000000")
        self.spinner = LoadingSpinner(self.overlay, bg="#000000")
        self.spinner.place(relx=0.5, rely=0.45, anchor="center")
        tk.Label(self.overlay, text="MEMPROSES...", bg="#000000", fg="white", font=FONT_HINT).place(relx=0.5, rely=0.6, anchor="center")
        
        # Bottom Bar
        self.bottom_bar = tk.Frame(self.root, bg=COLOR_BG)
        self.bottom_bar.grid(row=2, column=0, sticky="ew", padx=20, pady=20)
        
        self.btn_container = tk.Frame(self.bottom_bar, bg=COLOR_BG)
        self.btn_container.pack(anchor="center")
        
        tk.Label(self.bottom_bar, text="Key: 1-9 Pilih • ESC Skip • Enter Auto", font=FONT_HINT, bg=COLOR_BG, fg="#71717a").pack(pady=(10,0))
        
        self.root.bind("<Configure>", self.on_resize)
        self.root.bind("<Escape>", lambda e: self.skip_current())
        self.root.bind("<Return>", lambda e: self.select_first_psd())
        self.bind_shortcuts()
        
        self.render_buttons()
        self.load_next_image()
        
    def render_buttons(self):
        for btn in self.psd_buttons: btn.destroy()
        self.psd_buttons.clear()
        if hasattr(self, 'btn_skip'): self.btn_skip.destroy()
        
        colors = [COLOR_ACCENT_BLUE, COLOR_ACCENT_GREEN, "#eab308", "#db2777", "#8b5cf6"]
        
        for i, (name, path) in enumerate(self.psd_masters):
            key = self.shortcuts[i] if i < len(self.shortcuts) else "?"
            color = colors[i % len(colors)]
            
            btn = tk.Button(self.btn_container, text=f"{name} [{key.upper()}]", font=FONT_BOLD, 
                           bg=color, fg="white", relief="flat", activebackground=color, activeforeground="white",
                           cursor="hand2", padx=20, pady=10,
                           command=lambda n=name, p=path: self.process_init(n, p))
            btn.pack(side="left", padx=5)
            self.psd_buttons.append(btn)
            
        self.btn_skip = tk.Button(self.btn_container, text="SKIP [ESC]", font=FONT_BOLD,
                                 bg=COLOR_ACCENT_RED, fg="white", relief="flat", activebackground=COLOR_ACCENT_RED, activeforeground="white",
                                 cursor="hand2", padx=20, pady=10, command=self.skip_current)
        self.btn_skip.pack(side="left", padx=15)

    def bind_shortcuts(self):
        for i, key in enumerate(self.shortcuts):
            try:
                self.root.bind(key.lower(), lambda e, idx=i: self.select_psd_by_index(idx))
            except: pass

    def on_resize(self, event):
        if event.widget == self.root:
            if hasattr(self, '_resize_job'): self.root.after_cancel(self._resize_job)
            self._resize_job = self.root.after(100, self.update_image_display)

    def update_image_display(self):
        if not hasattr(self, 'canvas') or self.original_image is None: return
        self.root.update_idletasks()
        cw = self.canvas.winfo_width()
        ch = self.canvas.winfo_height()
        if cw < 10 or ch < 10: return
        
        iw, ih = self.original_image.size
        ratio = min(cw/iw, ch/ih)
        nw, nh = int(iw*ratio), int(ih*ratio)
        
        resized = self.original_image.resize((nw, nh), Image.Resampling.LANCZOS)
        self.photo = ImageTk.PhotoImage(resized)
        
        self.canvas.delete("all")
        self.canvas.create_image(cw//2, ch//2, image=self.photo, anchor="center")

    def toggle_inputs(self, enable):
        state = "normal" if enable else "disabled"
        for btn in self.psd_buttons + [self.btn_skip]: 
            btn.config(state=state, cursor="hand2" if enable else "arrow")
        
        if enable:
            self.spinner.stop()
            self.overlay.place_forget()
        else:
            self.overlay.place(relx=0, rely=0, relwidth=1, relheight=1)
            self.spinner.start()

    def load_next_image(self):
        self.current_idx += 1
        if self.current_idx >= len(self.jpgs):
            self.finish_processing()
            return
            
        full_jpg, rel_jpg = self.jpgs[self.current_idx]
        self.lbl_progress.config(text=f"{self.current_idx + 1}/{len(self.jpgs)}")
        self.lbl_filename.config(text=rel_jpg)
        
        try:
            self.original_image = Image.open(full_jpg)
            self.update_image_display()
        except Exception as e:
            self.canvas.delete("all")
            self.canvas.create_text(self.canvas.winfo_width()//2, self.canvas.winfo_height()//2, 
                                    text=f"Error: {e}", fill="red", font=FONT_TITLE)

    def select_psd_by_index(self, idx):
        if not self.is_processing and 0 <= idx < len(self.psd_masters):
            n, p = self.psd_masters[idx]
            self.process_init(n, p)
            
    def select_first_psd(self):
        if not self.is_processing and self.psd_masters:
            n, p = self.psd_masters[0]
            self.process_init(n, p)

    def process_init(self, name, path):
        if self.is_processing: return
        self.is_processing = True
        self.toggle_inputs(False)
        threading.Thread(target=self.process_thread, args=(name, path)).start()
        
    def process_thread(self, name, path):
        full_jpg, rel_jpg = self.jpgs[self.current_idx]
        ext = os.path.splitext(path)[1].lower()
        tname = compute_target_name(os.path.basename(rel_jpg))
        tdir = os.path.join(self.master_dir, os.path.dirname(rel_jpg))
        
        try:
            os.makedirs(tdir, exist_ok=True)
            dst = os.path.join(tdir, f"{tname}{ext}")
            
            if os.path.exists(dst):
                log = ("EXIST", rel_jpg, f"Sudah ada ({name})")
            else:
                shutil.copy2(path, dst)
                log = ("OK", rel_jpg, f"Sukses -> {name}")
        except Exception as e:
            log = ("FAIL", rel_jpg, str(e))
            
        self.root.after(0, lambda: self.process_done(log))
        
    def process_done(self, log):
        self.logs.append(log)
        self.is_processing = False
        self.toggle_inputs(True)
        self.load_next_image()

    def skip_current(self):
        if self.is_processing: return
        rel = self.jpgs[self.current_idx][1]
        self.logs.append(("SKIP", rel, "Dilewati user"))
        self.load_next_image()

    def open_settings(self):
        SettingsDialogV2(self.root, self.psd_masters, self.shortcuts, self.on_settings_saved)

    def on_settings_saved(self, new_shortcuts):
        self.shortcuts = new_shortcuts
        self.settings["shortcuts"] = new_shortcuts
        # Don't save size here, waiting for close
        self.render_buttons()
        self.bind_shortcuts()

    def finish_processing(self):
        self.setup_ui_report()

    # --- UI: Report Page ---
    def setup_ui_report(self):
        self.clear_ui()
        # Report layout grid
        self.root.columnconfigure(0, weight=1)
        self.root.rowconfigure(0, weight=0)
        self.root.rowconfigure(1, weight=1)
        self.root.rowconfigure(2, weight=0)
        
        total = len(self.logs)
        ok = sum(1 for l in self.logs if l[0] == "OK")
        skip = sum(1 for l in self.logs if l[0] == "SKIP")
        fail = sum(1 for l in self.logs if l[0] == "FAIL")
        exist = sum(1 for l in self.logs if l[0] == "EXIST")
        
        # Header
        header = tk.Frame(self.root, bg=COLOR_BG)
        header.grid(row=0, column=0, sticky="ew", padx=40, pady=(40, 20))
        tk.Label(header, text="Laporan Akhir", font=("Segoe UI", 24, "bold"), bg=COLOR_BG, fg="white").pack(anchor="w")
        
        # Stats
        cards = tk.Frame(header, bg=COLOR_BG)
        cards.pack(fill="x", pady=10)
        
        StatCard(cards, "Sukses", ok, COLOR_ACCENT_GREEN).pack(side="left", fill="x", expand=True, padx=5)
        StatCard(cards, "Dilewati", skip, "#fbbf24").pack(side="left", fill="x", expand=True, padx=5)
        StatCard(cards, "Gagal", fail, COLOR_ACCENT_RED).pack(side="left", fill="x", expand=True, padx=5)
        StatCard(cards, "Sudah Ada", exist, "#60a5fa").pack(side="left", fill="x", expand=True, padx=5)
        
        # Details
        tree_frame = tk.Frame(self.root, bg=COLOR_SURFACE)
        tree_frame.grid(row=1, column=0, sticky="nsew", padx=40)
        
        style = ttk.Style()
        style.theme_use("clam")
        style.configure("Treeview", background=COLOR_SURFACE, foreground="white", fieldbackground=COLOR_SURFACE, font=("Segoe UI", 10), rowheight=30, borderwidth=0)
        style.configure("Treeview.Heading", background=COLOR_BORDER, foreground="white", font=FONT_BOLD, borderwidth=0)
        style.map("Treeview", background=[('selected', '#3f3f46')])
        
        tree = ttk.Treeview(tree_frame, columns=("status", "file", "detail"), show="headings", selectmode="browse")
        tree.heading("status", text="STATUS")
        tree.heading("file", text="FILE")
        tree.heading("detail", text="DETAIL")
        
        tree.column("status", width=120, anchor="center")
        tree.column("file", width=400, anchor="w")
        tree.column("detail", width=300, anchor="w")
        
        vsb = ttk.Scrollbar(tree_frame, orient="vertical", command=tree.yview)
        tree.configure(yscrollcommand=vsb.set)
        
        tree.pack(side="left", fill="both", expand=True)
        vsb.pack(side="right", fill="y")
        
        tree.tag_configure("OK", foreground=COLOR_ACCENT_GREEN)
        tree.tag_configure("SKIP", foreground="#fbbf24")
        tree.tag_configure("FAIL", foreground=COLOR_ACCENT_RED)
        tree.tag_configure("EXIST", foreground="#60a5fa")
        
        for stat, f, det in self.logs:
            tree.insert("", "end", values=(stat, f, det), tags=(stat,))
            
        # Footer
        footer = tk.Frame(self.root, bg=COLOR_BG)
        footer.grid(row=2, column=0, sticky="ew", padx=40, pady=20)
        
        tk.Button(footer, text="KEMBALI KE MENU", command=self.setup_ui_setup, 
                  bg=COLOR_SURFACE, fg="white", font=FONT_BOLD, relief="flat", padx=20, pady=10).pack(side="right")

class SettingsDialogV2(tk.Toplevel):
    def __init__(self, parent, masters, shortcuts, callback):
        super().__init__(parent, bg=COLOR_BG)
        self.title("Settings")
        self.callback = callback
        self.entries = []
        center_window(self)
        tk.Label(self, text="Atur Shortcut", font=FONT_TITLE, bg=COLOR_BG, fg="white").pack(pady=15)
        f = tk.Frame(self, bg=COLOR_BG)
        f.pack(fill="both", padx=20)
        for i, (name, _) in enumerate(masters):
            val = shortcuts[i] if i < len(shortcuts) else "?"
            r = tk.Frame(f, bg=COLOR_BG)
            r.pack(fill="x", pady=2)
            tk.Label(r, text=name, width=20, anchor="w", bg=COLOR_BG, fg="white").pack(side="left")
            e = tk.Entry(r, width=5, justify="center")
            e.insert(0, val)
            e.pack(side="right")
            self.entries.append(e)
        tk.Button(self, text="SIMPAN", command=self.save, bg=COLOR_ACCENT_BLUE, fg="white", padx=15).pack(pady=20)
    def save(self):
        res = [e.get().strip() or "?" for e in self.entries]
        self.callback(res)
        self.destroy()

if __name__ == "__main__":
    if HAS_DND:
        root = TkinterDnD.Tk()
    else:
        root = tk.Tk()
    app = BucinAppV2(root)
    root.mainloop()
