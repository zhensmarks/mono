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
import winreg
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

# Theme & Typography Dictionary
THEME = {
    "dark": {
        "bg": "#121212",          # Pure Dark
        "surface": "#1e1e1e",     # Surface Dark
        "surface_hover": "#2d2d2d", # Hover Dark
        "fg": "#ffffff",          # White text
        "text_muted": "#a1a1aa",  # Gray text
        "accent": "#38bdf8",      # Sky 400 (Terminal Blue)
        "accent_text": "#000000", # Black text on accent
        "success": "#10b981",     # Emerald 500
        "error": "#ef4444",       # Red 500
        "border": "#2d2d2d"       # Border Dark
    },
    "light": {
        "bg": "#f8fafc",          # Slate 50
        "surface": "#ffffff",     # White
        "surface_hover": "#e2e8f0", # Slate 200
        "fg": "#0f172a",          # Slate 900
        "text_muted": "#475569",  # Slate 600
        "accent": "#0284c7",      # Sky 600
        "accent_text": "#ffffff", # White text on accent
        "success": "#059669",     # Emerald 600
        "error": "#dc2626",       # Red 600
        "border": "#cbd5e1"       # Slate 300
    }
}

# Variabel Warna Global (menyesuaikan instansi pertama)
COLOR_BG = THEME["dark"]["bg"]
COLOR_SURFACE = THEME["dark"]["surface"]
COLOR_SURFACE_HOVER = THEME["dark"]["surface_hover"]
COLOR_FG = THEME["dark"]["fg"]
COLOR_ACCENT_BLUE = THEME["dark"]["accent"]
COLOR_ACCENT_GREEN = THEME["dark"]["success"]
COLOR_ACCENT_RED = THEME["dark"]["error"]
COLOR_BORDER = THEME["dark"]["border"]

FONT_MAIN = ("Consolas", 10)
FONT_BOLD = ("Consolas", 10, "bold")
FONT_TITLE = ("Consolas", 16, "bold")
FONT_BIG = ("Consolas", 24, "bold")
FONT_HINT = ("Consolas", 9)

def is_windows_dark_mode():
    try:
        registry = winreg.ConnectRegistry(None, winreg.HKEY_CURRENT_USER)
        key = winreg.OpenKey(registry, r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
        val, _ = winreg.QueryValueEx(key, "AppsUseLightTheme")
        return val == 0
    except Exception:
        pass
    try:
        # Fallback to system theme if Apps theme is missing
        registry = winreg.ConnectRegistry(None, winreg.HKEY_CURRENT_USER)
        key = winreg.OpenKey(registry, r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
        val, _ = winreg.QueryValueEx(key, "SystemUseLightTheme")
        return val == 0
    except Exception:
        return True # Default dark


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
        "last_pilihan": "",
        "ui_manual_mode": "autocomplete"
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

    def destroy(self):
        if HAS_DND:
            try: self.drop_target_unregister()
            except: pass
        super().destroy()

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
        self.btn_close = tk.Button(self.title_bar, text="✕", width=5, bg=COLOR_BG, fg="#a1a1aa", relief="flat", command=self.on_app_close)
        self.btn_close.pack(side="right", fill="y")
        
        self.btn_max = tk.Button(self.title_bar, text="☐", width=5, bg=COLOR_BG, fg="#a1a1aa", relief="flat", command=self.toggle_maximize)
        self.btn_max.pack(side="right", fill="y")

        self.btn_min = tk.Button(self.title_bar, text="─", width=5, bg=COLOR_BG, fg="#a1a1aa", relief="flat", command=self.minimize_window)
        self.btn_min.pack(side="right", fill="y")
        
        # Hover events for buttons
        for btn in [self.btn_close, self.btn_max, self.btn_min]:
             btn.bind("<Enter>", lambda e, b=btn: b.config(bg=COLOR_SURFACE_HOVER, fg=COLOR_FG))
             btn.bind("<Leave>", lambda e, b=btn: b.config(bg=COLOR_BG, fg="#a1a1aa"))
        # Exception for close red hover
        self.btn_close.bind("<Enter>", lambda e: self.btn_close.config(bg=COLOR_ACCENT_RED, fg="white"))
        
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
        self.grip = tk.Label(self.root, text="◢", bg=COLOR_BG, fg="#52525b", font=("Segoe UI", 12))
        self.grip.place(relx=1.0, rely=1.0, anchor="se")
        self.grip.bind("<ButtonPress-1>", self.start_resize)
        self.grip.bind("<B1-Motion>", self.do_resize)
        
        self.is_maximized_full = False
        self.prev_geom = None
        
        # Start Live Theme Tracking
        self.start_theme_poll()

    def start_theme_poll(self):
        current_dark = is_windows_dark_mode()
        if not hasattr(self, 'active_theme_dark') or self.active_theme_dark != current_dark:
            self.active_theme_dark = current_dark
            self.apply_live_theme()
        # Periksa lagi tiap 1.5 detik secara non-blocking
        self.root.after(1500, self.start_theme_poll)

    def apply_live_theme(self):
        global COLOR_BG, COLOR_SURFACE, COLOR_SURFACE_HOVER, COLOR_FG
        global COLOR_ACCENT_BLUE, COLOR_ACCENT_GREEN, COLOR_ACCENT_RED, COLOR_BORDER
        theme_key = "dark" if self.active_theme_dark else "light"
        t = THEME[theme_key]
        
        color_map = {
            THEME["light"]["bg"].lower(): t["bg"],
            THEME["dark"]["bg"].lower(): t["bg"],
            "#18181b": t["bg"], "black": t["bg"], "#000000": t["bg"],
            
            THEME["light"]["surface"].lower(): t["surface"],
            THEME["dark"]["surface"].lower(): t["surface"],
            "#27272a": t["surface"], "#1e1e1e": t["surface"],
            
            THEME["light"]["surface_hover"].lower(): t["surface_hover"],
            THEME["dark"]["surface_hover"].lower(): t["surface_hover"],
            "#3f3f46": t["surface_hover"],
            
            THEME["light"]["fg"].lower(): t["fg"],
            THEME["dark"]["fg"].lower(): t["fg"],
            "#f4f4f5": t["fg"], "white": t["fg"], "#ffffff": t["fg"], "#d4d4d8": t["fg"],
            
            THEME["light"]["accent"].lower(): t["accent"],
            THEME["dark"]["accent"].lower(): t["accent"],
            "#3b82f6": t["accent"], "#0ea5e9": t["accent"], "#0284c7": t["accent"],
            
            THEME["light"]["success"].lower(): t["success"],
            THEME["dark"]["success"].lower(): t["success"],
            "#22c55e": t["success"], "#10b981": t["success"], "#059669": t["success"],
            
            THEME["light"]["error"].lower(): t["error"],
            THEME["dark"]["error"].lower(): t["error"],
            "#ef4444": t["error"], "red": t["error"], "#dc2626": t["error"],
            
            THEME["light"]["border"].lower(): t["border"],
            THEME["dark"]["border"].lower(): t["border"],
            "#52525b": t["border"], "#cbd5e1": t["border"], "#334155": t["border"],
            
            "#a1a1aa": t["text_muted"],
            "#71717a": t["text_muted"],
            THEME["light"]["text_muted"].lower(): t["text_muted"],
            THEME["dark"]["text_muted"].lower(): t["text_muted"]
        }
        
        COLOR_BG = t["bg"]
        COLOR_SURFACE = t["surface"]
        COLOR_SURFACE_HOVER = t["surface_hover"]
        COLOR_FG = t["fg"]
        COLOR_ACCENT_BLUE = t["accent"]
        COLOR_ACCENT_GREEN = t["success"]
        COLOR_ACCENT_RED = t["error"]
        COLOR_BORDER = t["border"]
        
        def safe_color_swap(c):
            if not c: return c
            if isinstance(c, str):
                ci = c.lower()
                if ci in color_map: return color_map[ci]
            return c
            
        def convert_font(fn):
            if isinstance(fn, str):
                if "Segoe UI" in fn: return fn.replace("Segoe UI", "Consolas")
            elif isinstance(fn, tuple) or isinstance(fn, list):
                if fn[0] == "Segoe UI":
                    return ("Consolas",) + tuple(fn[1:])
            return fn
            
        def update_w(w):
            keys = w.keys()
            for prop in ['bg', 'background', 'fg', 'foreground', 'activebackground', 'activeforeground', 
                         'insertbackground', 'selectbackground', 'highlightbackground', 'highlightcolor']:
                if prop in keys:
                    try: 
                        val = w.cget(prop)
                        new_val = safe_color_swap(val)
                        if new_val != val: w.config(**{prop: new_val})
                    except: pass
            
            if 'font' in keys:
                try:
                    val = w.cget('font')
                    new_val = convert_font(val)
                    if new_val != val: w.config(font=new_val)
                except: pass
                
            for child in w.winfo_children():
                update_w(child)
                
        update_w(self.root)
        
        # Override special custom components
        if hasattr(self, 'spinner') and getattr(self, 'spinner', None):
            try:
                # LoadingSpinner Custom
                self.spinner.itemconfig("arc", outline=COLOR_ACCENT_BLUE)
            except: pass
            
        style = ttk.Style()
        style.configure('TRadiobutton', background=COLOR_BG, foreground=COLOR_FG, font=FONT_MAIN)
        style.configure('TScrollbar', background=COLOR_SURFACE, troughcolor=COLOR_BG)
        style.configure('Treeview', background=COLOR_BG, foreground=COLOR_FG, fieldbackground=COLOR_BG, font=FONT_MAIN)
        style.configure('Treeview.Heading', background=COLOR_SURFACE, foreground=t["text_muted"], font=FONT_BOLD)

        # Titlebar tweaks
        if hasattr(self, 'btn_close'):
            self.btn_close.bind("<Enter>", lambda e: self.btn_close.config(bg=COLOR_ACCENT_RED, fg=COLOR_FG))
            self.btn_close.bind("<Leave>", lambda e: self.btn_close.config(bg=COLOR_BG, fg=t["text_muted"]))
        if hasattr(self, 'btn_min'):
            self.btn_min.bind("<Enter>", lambda e: self.btn_min.config(bg=COLOR_SURFACE_HOVER, fg=COLOR_FG))
            self.btn_min.bind("<Leave>", lambda e: self.btn_min.config(bg=COLOR_BG, fg=t["text_muted"]))
        if hasattr(self, 'btn_max'):
            self.btn_max.bind("<Enter>", lambda e: self.btn_max.config(bg=COLOR_SURFACE_HOVER, fg=COLOR_FG))
            self.btn_max.bind("<Leave>", lambda e: self.btn_max.config(bg=COLOR_BG, fg=t["text_muted"]))

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
        try:
            self.root.overrideredirect(False)
            self.root.iconify()
            self.root.bind("<Map>", self._on_map)
        except Exception:
            self.root.withdraw()
            
    def _on_map(self, event):
        if str(event.widget) == ".":
            self.root.overrideredirect(True)
            self.root.unbind("<Map>")
        
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

    def start_manual_mode(self):
        try:
            self.prepare_data()
            self.setup_ui_manual_processing(self.master_dir)
            self.root.bind("r", self.rotate_manual)
            self.root.bind("R", self.rotate_manual)
        except Exception as e:
            messagebox.showerror("Gagal Memulai", str(e))
            
    def rotate_manual(self, event=None):
        if hasattr(self, 'original_image') and self.original_image:
            from PIL import Image
            self.original_image = self.original_image.transpose(Image.ROTATE_270)
            self.update_image_display()

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
        
        
        self.build_bottom_bar()
        self.root.bind("<Configure>", self.on_resize)
        self.load_next_image()
        
    def build_bottom_bar(self):
        if hasattr(self, 'bottom_bar') and self.bottom_bar.winfo_exists():
            self.bottom_bar.destroy()
            
        self.bottom_bar = tk.Frame(self.ui_root, bg=COLOR_BG)
        self.bottom_bar.grid(row=2, column=0, sticky="ew", padx=20, pady=10)
        
        self.psd_map = {name: path for name, path in self.psd_masters}
        cur_mode = self.settings.get("ui_manual_mode", "autocomplete")
        
        # Reset alias agar aman saat toggle
        self.psd_buttons = []
        if hasattr(self, 'psd_combobox'): del self.psd_combobox
        if hasattr(self, 'btn_skip'): del self.btn_skip
        
        self.root.unbind("<Return>")
        self.root.unbind_all("<MouseWheel>")
        
        if cur_mode == "autocomplete":
            self.all_psd_names = sorted(list(self.psd_map.keys()))
            
            container = tk.Frame(self.bottom_bar, bg=COLOR_BG)
            container.pack(pady=5)
            
            input_grid = tk.Frame(container, bg=COLOR_BG)
            input_grid.pack()
            
            tk.Label(input_grid, text="KETIK NAMA PSD:", font=FONT_BOLD, bg=COLOR_BG, fg="white").grid(row=0, column=0, padx=5)
            
            self.psd_entry = tk.Entry(input_grid, font=FONT_TITLE, width=30)
            self.psd_entry.grid(row=0, column=1, padx=5)
            
            self.btn_skip = tk.Button(input_grid, text="SKIP [ESC]", font=FONT_BOLD, bg=COLOR_ACCENT_RED, fg="white", 
                                 relief="flat", activebackground=COLOR_ACCENT_RED, activeforeground="white", cursor="hand2", padx=20, pady=5, command=self.skip_current)
            self.btn_skip.grid(row=0, column=2, padx=10)
            
            # Murni Listbox pasif di bawah kotak ketik
            self.psd_listbox = tk.Listbox(input_grid, font=FONT_MAIN, height=5, bg=COLOR_SURFACE, fg="white", selectbackground=COLOR_ACCENT_BLUE, relief="flat", highlightthickness=1, highlightcolor=COLOR_BORDER)
            self.psd_listbox.grid(row=1, column=1, sticky="ew", padx=5, pady=(5,0))
            
            self.psd_buttons = [] # Keep empty for toggle_inputs compatibility
            self.psd_combobox = self.psd_entry # alias dummy untuk toggle_inputs dan focus_set agar tetap jalan
            
            def update_listbox(filtered_names):
                self.psd_listbox.delete(0, tk.END)
                for name in filtered_names:
                    self.psd_listbox.insert(tk.END, name)
                if filtered_names:
                    self.psd_listbox.selection_set(0) # Highlight opsi teratas dengan aman (pasif)
                    
            update_listbox(self.all_psd_names)
            
            def handle_keyrelease(event):
                if event.keysym in ("Return", "Escape", "Tab", "Up", "Down"):
                    return
                    
                typed = self.psd_entry.get().lower()
                if not typed:
                    update_listbox(self.all_psd_names)
                else:
                    filtered = [n for n in self.all_psd_names if typed in n.lower()]
                    update_listbox(filtered)
                    
            def move_selection(event):
                sel = self.psd_listbox.curselection()
                if not sel: return "break"
                idx = sel[0]
                if event.keysym == 'Up' and idx > 0:
                    idx -= 1
                elif event.keysym == 'Down' and idx < self.psd_listbox.size() - 1:
                    idx += 1
                self.psd_listbox.selection_clear(0, tk.END)
                self.psd_listbox.selection_set(idx)
                self.psd_listbox.see(idx)
                return "break"
                
            def on_enter(event):
                selected = None
                sel = self.psd_listbox.curselection()
                
                if sel:
                    selected = self.psd_listbox.get(sel[0])
                else:
                    typed = self.psd_entry.get().lower()
                    filtered = [n for n in self.all_psd_names if typed in n.lower()]
                    if filtered: selected = filtered[0]
                    
                if selected:
                    self.psd_entry.delete(0, tk.END)
                    update_listbox(self.all_psd_names)
                    self.manual_process_init(selected, self.psd_map[selected])
                    
                return "break"
                
            def on_listbox_click(event):
                sel = self.psd_listbox.curselection()
                if sel:
                    selected = self.psd_listbox.get(sel[0])
                    self.psd_entry.delete(0, tk.END)
                    update_listbox(self.all_psd_names)
                    self.manual_process_init(selected, self.psd_map[selected])
            
            self.psd_entry.bind('<KeyRelease>', handle_keyrelease)
            self.psd_entry.bind('<Up>', move_selection)
            self.psd_entry.bind('<Down>', move_selection)
            self.psd_entry.bind('<Return>', on_enter)
            self.psd_listbox.bind('<Double-Button-1>', on_listbox_click)
            
            tk.Label(self.bottom_bar, text="Ketik nama template lalu Enter • ESC Skip • R Putar Gambar • 1-9 Shortcut Murni", font=FONT_HINT, bg=COLOR_BG, fg="#71717a").pack(pady=(5,10))
            self.psd_entry.focus_set()
            
        else:
            # Mode "button"
            self.btn_scroll_canvas = tk.Canvas(self.bottom_bar, bg=COLOR_BG, bd=0, highlightthickness=0, height=220)
            self.btn_scroll_canvas.pack(side="top", fill="x", expand=True)
            
            self.btn_container = tk.Frame(self.btn_scroll_canvas, bg=COLOR_BG)
            self.btn_scroll_window = self.btn_scroll_canvas.create_window((0, 0), window=self.btn_container, anchor="nw")
            
            self.btn_container.bind("<Configure>", lambda e: self.btn_scroll_canvas.configure(scrollregion=self.btn_scroll_canvas.bbox("all")))
            self.btn_scroll_canvas.bind("<Configure>", lambda e: self.btn_scroll_canvas.itemconfig(self.btn_scroll_window, width=e.width))
            
            def _on_mousewheel(event):
                try: self.btn_scroll_canvas.yview_scroll(int(-1*(event.delta/120)), "units")
                except: pass
            self.root.bind_all("<MouseWheel>", _on_mousewheel)
            
            tk.Label(self.bottom_bar, text="Key: 1-9 Pilih • ESC Skip • R Putar Gambar • Enter Auto", font=FONT_HINT, bg=COLOR_BG, fg="#71717a").pack(side="bottom", pady=(5,0))
            self.render_buttons()
            self.root.bind("<Return>", lambda e: self.select_first_psd_manual())
            
        self.root.bind("<Escape>", lambda e: self.skip_current())
        self.bind_shortcuts()
        
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
        mode = self.settings.get("ui_manual_mode", "autocomplete")
        if mode == "button" and hasattr(self, 'reflow_buttons'):
            self.reflow_buttons()
            
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
        if hasattr(self, 'psd_combobox'):
            self.psd_combobox.config(state=state)
            if enable: self.psd_combobox.focus_set()
            
        for btn in getattr(self, 'psd_buttons', []) + [getattr(self, 'btn_skip', None)]: 
            if btn: btn.config(state=state, cursor="hand2" if enable else "arrow")
        
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
            from PIL import ImageOps
            self.original_image = Image.open(full_jpg)
            self.original_image = ImageOps.exif_transpose(self.original_image)
            
            # Memaksa Potrait pintar berdasarkan Rasio Gambar
            if self.original_image.width > self.original_image.height:
                ratio = self.original_image.width / float(self.original_image.height)
                # Jika rasio sangat lebar (> 1.35 atau lebih dari 4:3), kemungkinan besar ini Grup Foto (Landscape aseli).
                # Jika rasio mepet (<= 1.35), kemungkinan ini potret diabadikan menyimpang (misal 4:3 sideways phone).
                if ratio < 1.35:
                    self.original_image = self.original_image.transpose(Image.ROTATE_90)
                
            self.update_image_display()
            if hasattr(self, 'psd_combobox'):
                self.psd_combobox.focus_set()
        except Exception as e:
            self.canvas.delete("all")
            self.canvas.create_text(self.canvas.winfo_width()//2, self.canvas.winfo_height()//2, 
                                    text=f"Error: {e}", fill="red", font=FONT_TITLE)

    def reflow_buttons(self, force=False):
        if not hasattr(self, 'psd_masters') or not hasattr(self, 'btn_container'): return
        try:
            geom = self.root.geometry()
            gw = int(geom.split('x')[0])
        except:
            gw = self.root.winfo_width()
        available_w = max(gw, 800) - 80
        
        if hasattr(self, '_last_flow_w') and abs(self._last_flow_w - available_w) < 50 and not force:
            return
        self._last_flow_w = available_w
        
        if hasattr(self, 'psd_buttons'):
            self.psd_buttons.clear()
        else:
            self.psd_buttons = []
            
        for child in self.btn_container.winfo_children():
            child.destroy()
            
        colors = [COLOR_ACCENT_BLUE, COLOR_ACCENT_GREEN, "#eab308", "#db2777", "#8b5cf6"]
        
        current_row = tk.Frame(self.btn_container, bg=COLOR_BG)
        current_row.pack(pady=5)
        current_w = 0
        
        def add_btn(text, cmd, color, is_skip=False):
            nonlocal current_row, current_w
            extra_pad = 15 if is_skip else 5
            est_w = (len(text) * 9) + 40 + (extra_pad * 2)
            
            if current_w + est_w > available_w and current_w > 0:
                current_row = tk.Frame(self.btn_container, bg=COLOR_BG)
                current_row.pack(pady=5)
                current_w = 0
                
            st = "disabled" if getattr(self, "is_processing", False) else "normal"
            crs = "arrow" if st == "disabled" else "hand2"
            
            btn = tk.Button(current_row, text=text, font=FONT_BOLD, state=st,
                           bg=color, fg="white", relief="flat", activebackground=color, activeforeground="white",
                           cursor=crs, padx=20, pady=10, command=cmd)
            btn.pack(side="left", padx=extra_pad)
            current_w += est_w
            return btn
            
        for i, (name, path) in enumerate(self.psd_masters):
            key = self.shortcuts[i] if i < len(self.shortcuts) else "?"
            c = colors[i % len(colors)]
            text = f"{name} [{key.upper()}]"
            b = add_btn(text, lambda n=name, p=path: self.manual_process_init(n, p), c)
            self.psd_buttons.append(b)
            
        self.btn_skip = add_btn("SKIP [ESC]", self.skip_current, COLOR_ACCENT_RED, is_skip=True)
        
        self.root.update_idletasks()
        if hasattr(self, 'btn_scroll_canvas'):
            rh = self.btn_container.winfo_reqheight()
            self.btn_scroll_canvas.configure(height=max(50, min(rh, 240)))

    def render_buttons(self):
        self.reflow_buttons(force=True)

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
        if hasattr(self, 'settings_overlay') and self.settings_overlay.winfo_exists():
            return
            
        current_mode = self.settings.get("ui_manual_mode", "autocomplete")
        self.settings_overlay = SettingsOverlay(self.ui_root, self.psd_masters, self.shortcuts, current_mode, self.on_settings_saved)
        self.settings_overlay.place(relx=0, rely=0, relwidth=1, relheight=1)
        self.settings_overlay.lift()

    def on_settings_saved(self, new_shortcuts, new_mode):
        self.shortcuts = new_shortcuts
        self.settings["shortcuts"] = new_shortcuts
        
        old_mode = self.settings.get("ui_manual_mode", "autocomplete")
        self.settings["ui_manual_mode"] = new_mode
        save_settings(self.settings)
        
        # Hot reload untuk mode manual jika jendela sedang terbuka
        if hasattr(self, 'bottom_bar') and self.bottom_bar.winfo_exists():
            if new_mode != old_mode:
                self.build_bottom_bar()
                if hasattr(self, 'update_image_display'):
                    self.update_image_display()
            else:
                self.bind_shortcuts()
                if new_mode == "button" and hasattr(self, 'render_buttons'):
                    self.render_buttons()
        else:
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

class SettingsOverlay(tk.Frame):
    def __init__(self, parent, masters, shortcuts, current_mode, callback):
        super().__init__(parent, bg=COLOR_BG)
        self.callback = callback
        self.entries = []
        
        # Header Navigasi Pengaturan
        header = tk.Frame(self, bg=COLOR_SURFACE)
        header.pack(side="top", fill="x")
        tk.Label(header, text="PENGATURAN", font=FONT_TITLE, bg=COLOR_SURFACE, fg="white").pack(side="left", padx=20, pady=15)
        tk.Button(header, text="TUTUP / BATAL", command=self.close_without_save, bg=COLOR_BORDER, fg="white", font=FONT_BOLD, relief="flat", padx=15, pady=5, cursor="hand2").pack(side="right", padx=20, pady=15)
        
        # Container frame membungkus canvas
        container = tk.Frame(self, bg=COLOR_BG)
        container.pack(fill="both", expand=True, padx=20, pady=20)

        self.canvas = tk.Canvas(container, bg=COLOR_BG, highlightthickness=0)
        self.scrollbar = ttk.Scrollbar(container, orient="vertical", command=self.canvas.yview)
        
        self.scrollable_frame = tk.Frame(self.canvas, bg=COLOR_BG)
        self.scrollable_frame.bind(
            "<Configure>",
            lambda e: self.canvas.configure(scrollregion=self.canvas.bbox("all"))
        )
        
        self.canvas_window = self.canvas.create_window((0, 0), window=self.scrollable_frame, anchor="nw")
        
        self.canvas.pack(side="left", fill="both", expand=True)
        self.scrollbar.pack(side="right", fill="y")
        self.canvas.configure(yscrollcommand=self.scrollbar.set)
        
        def on_canvas_configure(event):
            if event.widget == self.canvas:
                self.canvas.itemconfig(self.canvas_window, width=event.width)
                
        self.canvas.bind("<Configure>", on_canvas_configure)
        
        def _on_mousewheel(event):
            try: self.canvas.yview_scroll(int(-1*(event.delta/120)), "units")
            except: pass
            
        self.bind("<MouseWheel>", _on_mousewheel)
        self.canvas.bind("<MouseWheel>", _on_mousewheel)
        self.scrollable_frame.bind("<MouseWheel>", _on_mousewheel)
        
        pf = self.scrollable_frame
        
        # Mode UI Selection
        tk.Label(pf, text="Mode Visual Manual", font=FONT_TITLE, bg=COLOR_BG, fg="white").pack(pady=(15, 5))
        
        # SANGAT PENTING: deklarasikan 'self' sebagai parent var_mode agar memory scope aman
        self.var_mode = tk.StringVar(self, value=current_mode) 
        
        mode_frame = tk.Frame(pf, bg=COLOR_BG)
        mode_frame.pack(fill="x", padx=20, pady=5)
        
        style = ttk.Style()
        style.configure('TRadiobutton', background=COLOR_BG, foreground='white', font=FONT_MAIN)
        ttk.Radiobutton(mode_frame, text="Autocomplete TextBox (Ketik & Enter)", variable=self.var_mode, value="autocomplete", style="TRadiobutton").pack(anchor="w", pady=2)
        ttk.Radiobutton(mode_frame, text="Tombol Klasik (Deret Bawah)", variable=self.var_mode, value="button", style="TRadiobutton").pack(anchor="w", pady=2)
        
        tk.Label(pf, text="Atur Shortcut Keyboard", font=FONT_TITLE, bg=COLOR_BG, fg="white").pack(pady=(30,10))
        f = tk.Frame(pf, bg=COLOR_BG)
        f.pack(fill="both", padx=20)
        
        for i, (name, _) in enumerate(masters):
            val = shortcuts[i] if i < len(shortcuts) else "?"
            r = tk.Frame(f, bg=COLOR_BG)
            r.pack(fill="x", pady=2)
            tk.Label(r, text=name, width=28, anchor="w", bg=COLOR_BG, fg="#d4d4d8", font=FONT_MAIN).pack(side="left")
            e = tk.Entry(r, width=5, justify="center", font=FONT_BOLD)
            e.insert(0, val)
            e.pack(side="right")
            e.bind("<MouseWheel>", _on_mousewheel)
            self.entries.append(e)
            
        tk.Button(pf, text="SIMPAN PENGATURAN", command=self.save, bg=COLOR_ACCENT_BLUE, fg="white", font=FONT_BOLD, padx=40, pady=10, cursor="hand2").pack(pady=40)
        
        self.update_idletasks()
        self.canvas.configure(scrollregion=self.canvas.bbox("all"))
        
    def close_without_save(self):
        self.destroy()
        
    def save(self):
        res = [e.get().strip() or "?" for e in self.entries]
        selected_mode = self.var_mode.get()
        self.callback(res, selected_mode)
        self.destroy()

if __name__ == "__main__":
    if HAS_DND:
        root = TkinterDnD.Tk()
    else:
        root = tk.Tk()
    app = BucinAppV3(root)
    root.mainloop()
