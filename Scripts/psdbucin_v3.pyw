# psdbucin_v3.pyw
# Unified PSD Bucin V3.0
# Features:
# - Dual Mode: Manual (Select per Photo) & Auto (First PSD for All)
# - Unified UI with TextBox Input in DropZone
# - BMachine Integration (Progress Reporting)
# - Dark Theme & Modern UI

import os
import re
import shutil
import sys
import json
import threading
import time
import tempfile
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
SETTINGS_FILE = os.path.join(os.path.expanduser("~"), ".psdbucin_v3_settings.json")
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

def write_bmachine_result(title, lines):
    """Tulis hasil akhir ke file temp untuk BMachine."""
    try:
        result_file = os.path.join(tempfile.gettempdir(), 'bmachine_result.json')
        data = {'type': 'result', 'title': title, 'lines': lines}
        with open(result_file, 'w', encoding='utf-8') as f:
            json.dump(data, f, ensure_ascii=False)
    except Exception:
        pass

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
    masters = []
    try:
        for root, _, files in os.walk(master_dir):
            for f in files:
                if f.lower().endswith(('.psd', '.psb')):
                    full_path = os.path.join(root, f)
                    rel_path = os.path.relpath(full_path, master_dir)
                    masters.append((rel_path, full_path))
    except Exception:
        return []
        
    masters.sort(key=lambda x: x[0].lower())
    return masters

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
        self.path = initial_path
        self.title_text = title
        
        # 1. Title (Bottom)
        self.lbl_title = tk.Label(self, text=title, font=FONT_TITLE, bg=COLOR_SURFACE, fg="#a1a1aa")
        self.lbl_title.pack(side="bottom", pady=(5, 15))
        
        # 2. Footer (TextBox + Button) - Above Title
        self.footer = tk.Frame(self, bg=COLOR_SURFACE)
        self.footer.pack(side="bottom", fill="x", padx=10, pady=(0, 5))
        
        self.entry = tk.Entry(self.footer, bg=COLOR_BG, fg=COLOR_FG, 
                              insertbackground=COLOR_FG, relief="flat", font=FONT_MAIN)
        self.entry.pack(side="left", fill="x", expand=True, padx=(0, 5), ipady=4)
        self.entry.bind("<KeyRelease>", self.on_entry_input)
        self.entry.bind("<FocusOut>", self.on_entry_blur)
        
        self.btn_action = tk.Button(self.footer, text="📂", width=3, 
                                     bg=COLOR_SURFACE_HOVER, fg=COLOR_FG, 
                                     relief="flat", cursor="hand2",
                                     command=self.toggle_action)
        self.btn_action.pack(side="right")
        
        # 3. Content Area (Top/Center)
        self.inner = tk.Frame(self, bg=COLOR_SURFACE)
        self.inner.pack(side="top", expand=True, fill="both", padx=20, pady=(20, 5))
        
        self.content = tk.Frame(self.inner, bg=COLOR_SURFACE)
        self.content.place(relx=0.5, rely=0.5, anchor="center")
        
        self.lbl_path = tk.Label(self.content, text="Klik atau Drop Folder disini\n(Klik Kanan untuk Hapus)", font=FONT_HINT, bg=COLOR_SURFACE, fg="#52525b", wraplength=300)
        self.lbl_path.pack()
        
        # Events
        for w in [self, self.inner, self.content, self.lbl_title, self.lbl_path]:
            w.bind("<Button-1>", self.browse)
            w.bind("<Button-3>", self.clear)
            w.bind("<Enter>", self.on_hover)
            w.bind("<Leave>", self.on_leave)
        
        if HAS_DND:
            self.drop_target_register(DND_FILES)
            self.dnd_bind('<<Drop>>', self.on_drop)
            
        self.set_path(self.path) # Init display

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
            
    def on_entry_input(self, event=None):
        path = self.entry.get().strip()
        if os.path.exists(path):
             self.path = path.replace("/", "\\")
             self.update_visuals(self.path)
             if self.on_change: self.on_change()
        
    def on_entry_blur(self, event=None):
        path = self.entry.get().strip()
        if path and os.path.isdir(path):
            self.set_path(path)
        elif not path:
             self.set_path("")
            
    def toggle_action(self):
        if self.path:
             self.clear()
        else:
             self.browse()
            
    def set_path(self, path):
        self.path = path.replace("/", "\\") if path else ""
        current_entry = self.entry.get()
        if current_entry != self.path:
            self.entry.delete(0, "end")
            self.entry.insert(0, self.path)
        
        self.update_visuals(self.path)
        if self.on_change: self.on_change()
        
    def update_visuals(self, path):
        if path:
            self.lbl_title.config(fg="white")
            self.lbl_path.config(text=str(path), fg=COLOR_ACCENT_GREEN)
            self.btn_action.config(text="✕", bg=COLOR_ACCENT_RED, fg="white")
        else:
            self.lbl_title.config(fg="#a1a1aa")
            self.lbl_path.config(text="Klik atau Drop Folder disini\n(Klik Kanan untuk Hapus)", fg="#52525b")
            self.btn_action.config(text="📂", bg=COLOR_SURFACE_HOVER, fg="#a1a1aa")
        
    def get_path(self):
        return self.path

# --- Main Application ---
class BucinAppV3:
    def __init__(self, root):
        self.root = root
        self.root.title("PSD Bucin V3.0")
        self.root.configure(bg=COLOR_BG)
        
        self.settings = load_settings()
        
        # Restore Window Geometry
        w = self.settings.get("window_width", DEFAULT_WIDTH)
        h = self.settings.get("window_height", DEFAULT_HEIGHT)
        x = self.settings.get("window_x", -1)
        y = self.settings.get("window_y", -1)
        
        # --- Custom Title Bar & Chrome ---
        self.root.overrideredirect(True)
        if x != -1 and y != -1:
             self.root.geometry(f"{w}x{h}+{x}+{y}")
        else:
             sw = self.root.winfo_screenwidth()
             sh = self.root.winfo_screenheight()
             self.root.geometry(f"{w}x{h}+{(sw-w)//2}+{(sh-h)//2}")
             
        # Create Title Bar
        self.title_bar = tk.Frame(self.root, bg=COLOR_BG, relief="flat")
        self.title_bar.pack(side="top", fill="x")
        
        tk.Label(self.title_bar, text="PSD Bucin V3.0", font=("Segoe UI", 10, "bold"), bg=COLOR_BG, fg="#a1a1aa", padx=15, pady=10).pack(side="left")
        
        # Window Controls
        btn_close = tk.Button(self.title_bar, text="✕", width=5, bg=COLOR_BG, fg="#a1a1aa", relief="flat", command=self.on_app_close)
        btn_close.pack(side="right", fill="y")
        
        self.btn_max = tk.Button(self.title_bar, text="☐", width=5, bg=COLOR_BG, fg="#a1a1aa", relief="flat", command=self.toggle_maximize)
        self.btn_max.pack(side="right", fill="y")

        btn_min = tk.Button(self.title_bar, text="─", width=5, bg=COLOR_BG, fg="#a1a1aa", relief="flat", command=self.minimize_window)
        btn_min.pack(side="right", fill="y")
        
        # Hover events for buttons
        for btn in [btn_close, self.btn_max, btn_min]:
             btn.bind("<Enter>", lambda e, b=btn: b.config(bg=COLOR_SURFACE_HOVER, fg="white"))
             btn.bind("<Leave>", lambda e, b=btn: b.config(bg=COLOR_BG, fg="#a1a1aa"))
        # Exception for close red hover
        btn_close.bind("<Enter>", lambda e: btn_close.config(bg=COLOR_ACCENT_RED, fg="white"))
        
        # Drag Logic
        self.title_bar.bind("<ButtonPress-1>", self.start_move)
        self.title_bar.bind("<B1-Motion>", self.do_move)
        
        # Content Container (ui_root)
        self.main_border = tk.Frame(self.root, bg=COLOR_BORDER, padx=1, pady=1)
        self.main_border.pack(fill="both", expand=True)
        self.ui_root = tk.Frame(self.main_border, bg=COLOR_BG)
        self.ui_root.pack(fill="both", expand=True)
        
        self.psd_masters = []
        self.jpgs = []
        
        self.setup_ui_setup()
        
        # Taskbar Hack
        self.root.after(10, self.set_app_window)
        
        # Resize Grip
        self.grip = tk.Label(self.root, text="◢", bg=COLOR_BG, fg="#52525b", font=("Segoe UI", 12), cursor="size_nw_se")
        self.grip.place(relx=1.0, rely=1.0, anchor="se")
        self.grip.bind("<ButtonPress-1>", self.start_resize)
        self.grip.bind("<B1-Motion>", self.do_resize)
        
        self.is_maximized_full = False
        self.prev_geom = None

    def toggle_maximize(self):
        if self.is_maximized_full:
            # Restore
            if self.prev_geom:
                self.root.geometry(self.prev_geom)
            self.is_maximized_full = False
            self.btn_max.config(text="☐")
        else:
            # Maximize
            self.prev_geom = self.root.geometry()
            ws = self.root.winfo_screenwidth()
            hs = self.root.winfo_screenheight()
            self.root.geometry(f"{ws}x{hs-48}+0+0") # Assume 48px taskbar
            self.is_maximized_full = True
            self.btn_max.config(text="❐")

    def start_move(self, event):
        self.x = event.x
        self.y = event.y

    def do_move(self, event):
        if self.is_maximized_full: return # Don't move if max
        deltax = event.x - self.x
        deltay = event.y - self.y
        x = self.root.winfo_x() + deltax
        y = self.root.winfo_y() + deltay
        self.root.geometry(f"+{x}+{y}")
        
    def minimize_window(self):
        self.root.state('iconic') # Can be buggy with overrideredirect, but try
        self.root.iconify()
        
    def start_resize(self, event):
        self.x = event.x
        self.y = event.y
        self.w = self.root.winfo_width()
        self.h = self.root.winfo_height()

    def do_resize(self, event):
        nw = max(self.w + (event.x - self.x), 800)
        nh = max(self.h + (event.y - self.y), 348)
        self.root.geometry(f"{nw}x{nh}")

    def set_app_window(self):
        try:
            import ctypes
            GWL_EXSTYLE = -20
            WS_EX_APPWINDOW = 0x00040000
            WS_EX_TOOLWINDOW = 0x00000080
            hwnd = ctypes.windll.user32.GetParent(self.root.winfo_id())
            style = ctypes.windll.user32.GetWindowLongW(hwnd, GWL_EXSTYLE)
            style = style & ~WS_EX_TOOLWINDOW
            style = style | WS_EX_APPWINDOW
            ctypes.windll.user32.SetWindowLongW(hwnd, GWL_EXSTYLE, style)
            self.root.wm_withdraw()
            self.root.after(10, self.root.wm_deiconify)
        except: pass
            
    def on_app_close(self):
        self.settings["window_width"] = self.root.winfo_width()
        self.settings["window_height"] = self.root.winfo_height()
        self.settings["window_x"] = self.root.winfo_x()
        self.settings["window_y"] = self.root.winfo_y()
        save_settings(self.settings)
        self.root.destroy()
            
    def clear_ui(self):
        if hasattr(self, 'ui_root'):
             self.ui_root.destroy()
        
        self.ui_root = tk.Frame(self.main_border, bg=COLOR_BG)
        self.ui_root.pack(fill="both", expand=True)
            
    # --- UI: Setup Page ---
    def setup_ui_setup(self):
        self.clear_ui()
        
        # Main Grid
        self.ui_root.columnconfigure(0, weight=1)
        self.ui_root.columnconfigure(1, weight=1)
        self.ui_root.rowconfigure(0, weight=1) # Drop zones
        self.ui_root.rowconfigure(1, weight=0) # Button
        
        # Drop ZonesContainer
        zone_frame = tk.Frame(self.ui_root, bg=COLOR_BG)
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
        footer = tk.Frame(self.ui_root, bg=COLOR_BG)
        footer.grid(row=1, column=0, columnspan=2, sticky="ew", padx=20, pady=20)
        
        self.lbl_status = tk.Label(footer, text="Silakan pilih folder...", font=FONT_TITLE, bg=COLOR_BG, fg="#52525b")
        self.lbl_status.pack(side="left")
        
        # Buttons Container
        btn_container = tk.Frame(footer, bg=COLOR_BG)
        btn_container.pack(side="right")
        
        self.btn_manual = tk.Button(btn_container, text="MODE MANUAL", font=FONT_BOLD, 
                                    bg=COLOR_SURFACE, fg="#71717a", relief="flat", cursor="arrow",
                                    command=self.start_manual_mode, padx=20, pady=15, state="disabled")
        self.btn_manual.pack(side="left", padx=5)

        self.btn_auto = tk.Button(btn_container, text="MODE OTOMATIS", font=FONT_BOLD, 
                                    bg=COLOR_SURFACE, fg="#71717a", relief="flat", cursor="arrow",
                                    command=self.start_auto_mode, padx=20, pady=15, state="disabled")
        self.btn_auto.pack(side="left", padx=5)
        
        self.validate_inputs() 

    def validate_inputs(self):
        if not hasattr(self, 'dz_master') or not hasattr(self, 'dz_pilihan'): return
        m = self.dz_master.get_path()
        p = self.dz_pilihan.get_path()
        
        if not m or not p:
            self.lbl_status.config(text="Pilih kedua folder dulu...", fg="#52525b")
            self._set_buttons_state(False)
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
            # Recursive PSD count
            n_psd = 0
            for r, _, f in os.walk(m):
                for x in f:
                    if x.lower().endswith(('.psd', '.psb')): n_psd += 1
            
            # Recursive JPG count
            n_jpg = 0
            for r, _, f in os.walk(p):
                for x in f:
                    if x.lower().endswith(('.jpg','.jpeg')): n_jpg += 1
                    
            if n_psd == 0:
                self.lbl_status.config(text="⚠️ Tidak ada PSD di folder Master", fg=COLOR_ACCENT_RED)
                self._set_buttons_state(False)
                return
            if n_jpg == 0:
                self.lbl_status.config(text="⚠️ Tidak ada JPG di folder Foto", fg=COLOR_ACCENT_RED)
                self._set_buttons_state(False)
                return
                
            self.lbl_status.config(text=f"Siap: {n_psd} Template • {n_jpg} Foto", fg=COLOR_ACCENT_GREEN)
            self._set_buttons_state(True)
            
        except Exception as e:
            self.lbl_status.config(text=f"Error: {str(e)}", fg=COLOR_ACCENT_RED)

    def _set_buttons_state(self, enabled):
        state = "normal" if enabled else "disabled"
        bg_manual = COLOR_ACCENT_BLUE if enabled else COLOR_SURFACE
        bg_auto = COLOR_ACCENT_GREEN if enabled else COLOR_SURFACE
        fg = "white" if enabled else "#71717a"
        cursor = "hand2" if enabled else "arrow"
        
        self.btn_manual.config(state=state, bg=bg_manual, fg=fg, cursor=cursor)
        self.btn_auto.config(state=state, bg=bg_auto, fg=fg, cursor=cursor)

    def prepare_data(self):
        m = self.dz_master.get_path()
        p = self.dz_pilihan.get_path()
        
        # Re-verify
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
        
        self.master_dir = m

    # --- Mode: Manual ---
    def start_manual_mode(self):
        try:
            self.prepare_data()
            self.setup_ui_manual_processing(self.master_dir)
        except Exception as e:
            messagebox.showerror("Gagal Memulai", str(e))

    def setup_ui_manual_processing(self, master_dir):
        # ... Similar to advanced v2 processing UI ...
        self.clear_ui()
        self.ui_root.columnconfigure(0, weight=1)
        self.ui_root.columnconfigure(1, weight=0)
        self.ui_root.rowconfigure(0, weight=0)
        self.ui_root.rowconfigure(1, weight=1)
        self.ui_root.rowconfigure(2, weight=0)
        
        self.current_idx = -1
        self.logs = []
        self.is_processing = False
        self.original_image = None
        self.photo = None
        self.psd_buttons = []
        self.shortcuts = self.settings.get("shortcuts", [str(i+1) for i in range(9)])
        
        # Top Bar
        top_bar = tk.Frame(self.ui_root, bg=COLOR_BG)
        top_bar.grid(row=0, column=0, sticky="ew", padx=20, pady=10)
        
        self.lbl_progress = tk.Label(top_bar, text="0/0", font=FONT_BOLD, bg=COLOR_BG, fg=COLOR_ACCENT_BLUE)
        self.lbl_progress.pack(side="left")
        
        tk.Button(top_bar, text="⚙", command=self.open_settings, bg=COLOR_SURFACE, fg="white", relief="flat", cursor="hand2", width=3).pack(side="right")
        
        self.lbl_filename = tk.Label(top_bar, text="Loading...", font=FONT_TITLE, bg=COLOR_BG, fg="white")
        self.lbl_filename.place(relx=0.5, rely=0.5, anchor="center")
        
        # Content
        content = tk.Frame(self.ui_root, bg="#000000")
        content.grid(row=1, column=0, sticky="nsew", padx=20, pady=5)
        
        self.canvas = tk.Canvas(content, bg="#000000", highlightthickness=0)
        self.canvas.pack(fill="both", expand=True)
        
        # Overlay
        self.overlay = tk.Frame(content, bg="#000000")
        self.spinner = LoadingSpinner(self.overlay, bg="#000000")
        self.spinner.place(relx=0.5, rely=0.45, anchor="center")
        tk.Label(self.overlay, text="MEMPROSES...", bg="#000000", fg="white", font=FONT_HINT).place(relx=0.5, rely=0.6, anchor="center")
        
        
        # Bottom Bar
        self.bottom_bar = tk.Frame(self.ui_root, bg=COLOR_BG)
        self.bottom_bar.grid(row=2, column=0, sticky="ew", padx=20, pady=20)
        
        self.btn_container = tk.Frame(self.bottom_bar, bg=COLOR_BG)
        self.btn_container.pack(anchor="center")
        
        tk.Label(self.bottom_bar, text="Key: 1-9 Pilih • ESC Skip • Enter Auto", font=FONT_HINT, bg=COLOR_BG, fg="#71717a").pack(pady=(10,0))
        
        self.root.bind("<Configure>", self.on_resize)
        self.root.bind("<Escape>", lambda e: self.skip_current())
        self.root.bind("<Return>", lambda e: self.select_first_psd_manual())
        self.bind_shortcuts()
        
        self.render_buttons()
        self.load_next_image()
        
    # ... Manual Mode Handlers (Same as v2) ...
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
        
        # Report progress to BMachine
        report_progress(self.current_idx, len(self.jpgs), "Manual Process")

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
                           command=lambda n=name, p=path: self.manual_process_init(n, p))
            btn.pack(side="left", padx=5)
            self.psd_buttons.append(btn)
            
        self.btn_skip = tk.Button(self.btn_container, text="SKIP [ESC]", font=FONT_BOLD,
                                 bg=COLOR_ACCENT_RED, fg="white", relief="flat", activebackground=COLOR_ACCENT_RED, activeforeground="white",
                                 cursor="hand2", padx=20, pady=10, command=self.skip_current)
        self.btn_skip.pack(side="left", padx=15)

    def manual_process_init(self, name, path):
        if self.is_processing: return
        self.is_processing = True
        self.toggle_inputs(False)
        threading.Thread(target=self.manual_process_thread, args=(name, path)).start()
        
    def manual_process_thread(self, name, path):
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

    def select_psd_by_index(self, idx):
        if not self.is_processing and 0 <= idx < len(self.psd_masters):
            n, p = self.psd_masters[idx]
            self.manual_process_init(n, p)

    def select_first_psd_manual(self):
        if not self.is_processing and self.psd_masters:
            n, p = self.psd_masters[0]
            self.manual_process_init(n, p)

    def skip_current(self):
        if self.is_processing: return
        rel = self.jpgs[self.current_idx][1]
        self.logs.append(("SKIP", rel, "Dilewati user"))
        self.load_next_image()
        
    def process_done(self, log):
        self.logs.append(log)
        self.is_processing = False
        self.toggle_inputs(True)
        self.load_next_image()
        
    def open_settings(self):
        SettingsDialogV3(self.root, self.psd_masters, self.shortcuts, self.on_settings_saved)

    def on_settings_saved(self, new_shortcuts):
        self.shortcuts = new_shortcuts
        self.settings["shortcuts"] = new_shortcuts
        self.render_buttons()
        self.bind_shortcuts()

    # --- Mode: Automatic ---
    def start_auto_mode(self):
        try:
            self.prepare_data()
            self.setup_ui_auto_processing()
        except Exception as e:
            messagebox.showerror("Gagal Memulai", str(e))
            
    def setup_ui_auto_processing(self):
        self.clear_ui()
        # Simple Loading UI
        self.ui_root.columnconfigure(0, weight=1)
        self.ui_root.rowconfigure(0, weight=1)
        
        frame = tk.Frame(self.ui_root, bg=COLOR_BG)
        frame.place(relx=0.5, rely=0.5, anchor="center")
        
        self.spinner = LoadingSpinner(frame, bg=COLOR_BG, size=80)
        self.spinner.pack()
        self.spinner.start()
        
        tk.Label(frame, text="MODE OTOMATIS", font=FONT_TITLE, bg=COLOR_BG, fg=COLOR_ACCENT_GREEN).pack(pady=(20, 5))
        self.lbl_auto_status = tk.Label(frame, text="Memproses...", font=FONT_MAIN, bg=COLOR_BG, fg="white")
        self.lbl_auto_status.pack()
        
        self.btn_cancel = tk.Button(self.ui_root, text="BATAL", bg=COLOR_ACCENT_RED, fg="white", 
                                    relief="flat", padx=20, pady=10, command=self.cancel_auto)
        self.btn_cancel.place(relx=0.5, rely=0.8, anchor="center")
        
        self.is_cancelled = False
        self.logs = []
        threading.Thread(target=self.auto_process_thread).start()
        
    def cancel_auto(self):
        self.is_cancelled = True
        self.btn_cancel.config(text="MEMBATALKAN...", state="disabled")

    def auto_process_thread(self):
        # Auto Mode Strategy: Smart Matching (Folder Class Based)
        # 1. Build Map: { "Relative/Folder/Path": "Full/Path/To/Template.psd" }
        psd_map = {}
        fallback_psd = None
        if self.psd_masters:
             fallback_psd = self.psd_masters[0][1]

        for rel_name, full_path in self.psd_masters:
             # rel_name example: "KELAS A/Template.psd"
             # dirname: "KELAS A"
             p_dir = os.path.dirname(rel_name)
             # Normalize for matching
             p_dir_norm = p_dir.replace("\\", "/").strip().lower()
             if p_dir_norm not in psd_map:
                 psd_map[p_dir_norm] = full_path

        total = len(self.jpgs)
        
        for i, (full_jpg, rel_jpg) in enumerate(self.jpgs):
            if self.is_cancelled: break
            
            # Update UI
            msg = f"Memproses {i+1}/{total}: {rel_jpg}"
            self.root.after(0, lambda m=msg: self.lbl_auto_status.config(text=m))
            report_progress(i+1, total, rel_jpg) # BMachine
            
            try:
                # 2. Match JPG Folder to PSD Folder
                # rel_jpg example: "KELAS A/Anak1.jpg" -> "KELAS A"
                jpg_dir = os.path.dirname(rel_jpg)
                jpg_dir_norm = jpg_dir.replace("\\", "/").strip().lower()
                
                selected_master = psd_map.get(jpg_dir_norm)
                
                # Fallback Logic
                if not selected_master:
                     # Try finding partial match? No, stick to explicit matching first.
                     selected_master = fallback_psd
                     
                if not selected_master:
                     raise Exception("No Master PSD found")

                master_ext = os.path.splitext(selected_master)[1].lower()
                master_name = os.path.basename(selected_master)

                tname = compute_target_name(os.path.basename(rel_jpg))
                tdir = os.path.join(self.master_dir, os.path.dirname(rel_jpg))
                os.makedirs(tdir, exist_ok=True)
                dst = os.path.join(tdir, f"{tname}{master_ext}")
                
                if os.path.exists(dst):
                     self.logs.append(("EXIST", rel_jpg, f"Sudah ada"))
                else:
                    shutil.copy2(selected_master, dst)
                    self.logs.append(("OK", rel_jpg, f"Sukses -> {master_name}"))
                    
            except Exception as e:
                self.logs.append(("FAIL", rel_jpg, str(e)))
                
        self.root.after(0, self.finish_processing)
        
    # --- Report ---
    def finish_processing(self):
        self.setup_ui_report()
        # Save BMachine result
        summary = [f"Mode: {'Manual' if hasattr(self, 'psd_buttons') else 'Otomatis'}", 
                   f"Total: {len(self.logs)}"] + \
                  [f"{s}: {f} ({d})" for s,f,d in self.logs]
        write_bmachine_result("Laporan PSD Bucin", summary)

    def setup_ui_report(self):
        self.clear_ui()
        # ... Report UI (Same as v2) ...
        self.ui_root.columnconfigure(0, weight=1)
        self.ui_root.rowconfigure(0, weight=0)
        self.ui_root.rowconfigure(1, weight=1)
        self.ui_root.rowconfigure(2, weight=0)
        
        total = len(self.logs)
        ok = sum(1 for l in self.logs if l[0] == "OK")
        skip = sum(1 for l in self.logs if l[0] == "SKIP")
        fail = sum(1 for l in self.logs if l[0] == "FAIL")
        exist = sum(1 for l in self.logs if l[0] == "EXIST")
        
        # Header
        header = tk.Frame(self.ui_root, bg=COLOR_BG)
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
        tree_frame = tk.Frame(self.ui_root, bg=COLOR_SURFACE)
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
        footer = tk.Frame(self.ui_root, bg=COLOR_BG)
        footer.grid(row=2, column=0, sticky="ew", padx=40, pady=20)
        
        tk.Button(footer, text="KEMBALI KE MENU", command=self.setup_ui_setup, 
                  bg=COLOR_SURFACE, fg="white", font=FONT_BOLD, relief="flat", padx=20, pady=10).pack(side="right")

class SettingsDialogV3(tk.Toplevel):
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
    app = BucinAppV3(root)
    root.mainloop()
