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
from PIL import Image, ImageTk, ImageDraw

try:
    from tkinterdnd2 import DND_FILES, TkinterDnD
    HAS_DND = True
except ImportError:
    HAS_DND = False

SETTINGS_FILE = os.path.join(os.path.expanduser("~"), ".psdbucin_v4_settings.json")
DEFAULT_WIDTH = 1100
DEFAULT_HEIGHT = 700

def is_windows_dark_mode():
    try:
        registry = winreg.ConnectRegistry(None, winreg.HKEY_CURRENT_USER)
        key = winreg.OpenKey(registry, r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
        val, _ = winreg.QueryValueEx(key, "AppsUseLightTheme")
        return val == 0
    except Exception:
        return True

# 1000x Aesthetic Color Palette (Zinc / Indigo - Modern Dark Mode)
THEMES = {
    "dark": {
        "bg": "#09090B",            # Deepest Zinc
        "surface": "#18181B",       # Zinc 900 (Cards)
        "surface_hover": "#27272A", # Zinc 800
        "surface_active": "#3F3F46",# Zinc 700
        "fg": "#FAFAFA",            
        "text_muted": "#A1A1AA",    
        "accent_primary": "#6366F1",# Indigo 500
        "accent_hover": "#818CF8",  
        "success": "#22C55E",       
        "error": "#EF4444",         
        "warning": "#F59E0B",       
        "border": "#27272A",        # Soft Borders
    },
    "light": {
        "bg": "#F4F4F5",            
        "surface": "#FFFFFF",       
        "surface_hover": "#F4F4F5", 
        "surface_active": "#E4E4E7",
        "fg": "#09090B",            
        "text_muted": "#71717A",    
        "accent_primary": "#4F46E5",
        "accent_hover": "#6366F1",  
        "success": "#16A34A",       
        "error": "#DC2626",         
        "warning": "#D97706",       
        "border": "#E4E4E7",        
    }
}
THEME = THEMES["dark"] if is_windows_dark_mode() else THEMES["light"]

FONT_MAIN = ("Segoe UI Variable Display", 10) if "Segoe UI Variable Display" else ("Segoe UI", 10)
FONT_MAIN_BOLD = ("Segoe UI Variable Display", 10, "bold") if "Segoe UI Variable Display" else ("Segoe UI", 10, "bold")
FONT_TITLE = ("Segoe UI Variable Display", 13, "bold") if "Segoe UI Variable Display" else ("Segoe UI", 13, "bold")
FONT_BIG = ("Segoe UI Variable Display", 24, "bold") if "Segoe UI Variable Display" else ("Segoe UI", 24, "bold")
FONT_HINT = ("Segoe UI", 9)

def round_rectangle(canvas, x1, y1, x2, y2, radius=25, **kwargs):
    points = [x1+radius, y1, x1+radius, y1, x2-radius, y1, x2-radius, y1, x2, y1, x2, y1+radius, x2, y1+radius, x2, y2-radius, x2, y2-radius, x2, y2, x2-radius, y2, x2-radius, y2, x1+radius, y2, x1+radius, y2, x1, y2, x1, y2-radius, x1, y2-radius, x1, y1+radius, x1, y1+radius, x1, y1]
    return canvas.create_polygon(points, **kwargs, smooth=True)

ONLY_PAREN = re.compile(r'^\(\s*(\d+)\s*\)$')
SPACE_FORM = re.compile(r'^(\d+)\s*\(\s*\d+\s*\)(?:\b.*)?$')
TIGHT_FORM = re.compile(r'^\d+\(\s*(\d+)\s*\)$')

def compute_target_name(jpg_name):
    n = os.path.splitext(jpg_name)[0].strip()
    m = ONLY_PAREN.match(n)
    if m: return m.group(1)
    m = SPACE_FORM.match(n)
    if m: return m.group(1)
    m = TIGHT_FORM.match(n)
    if m: return m.group(1)
    return n

def report_progress(current, total, filename):
    try:
        progress_file = os.path.join(tempfile.gettempdir(), 'bmachine_progress.json')
        data = {'current': current, 'total': total, 'file': filename, 'status': 'processing'}
        with open(progress_file, 'w', encoding='utf-8') as f: json.dump(data, f)
    except Exception: pass

def write_bmachine_result(title, lines):
    try:
        result_file = os.path.join(tempfile.gettempdir(), 'bmachine_result.json')
        data = {'type': 'result', 'title': title, 'lines': lines}
        with open(result_file, 'w', encoding='utf-8') as f: json.dump(data, f, ensure_ascii=False)
    except Exception: pass

def load_settings():
    defaults = {
        "window_width": DEFAULT_WIDTH,
        "window_height": DEFAULT_HEIGHT,
        "window_x": -1, "window_y": -1,
        "shortcuts": [str(i+1) for i in range(9)],
        "last_master": "", "last_pilihan": "",
        "sidebar_expanded": True
    }
    try:
        if os.path.exists(SETTINGS_FILE):
            with open(SETTINGS_FILE, 'r') as f: defaults.update(json.load(f))
    except Exception: pass
    return defaults

def save_settings(data):
    try:
        current = load_settings()
        current.update(data)
        with open(SETTINGS_FILE, 'w') as f: json.dump(current, f)
    except Exception: pass

def parse_dnd_files(raw_data):
    paths = re.findall(r'\{[^\}]+\}|\S+', raw_data)
    return [p.strip('{}') for p in paths]

# --- UI Components ---
class SidebarButton(tk.Canvas):
    def __init__(self, parent, text, icon="", command=None, is_active=False):
        super().__init__(parent, highlightthickness=0, bg=THEME["surface"], height=48, cursor="hand2")
        self.command = command
        self.text = text
        self.icon = icon
        self.is_active = is_active
        self.is_hovered = False
        
        self.bind("<Configure>", self._draw)
        self.bind("<Enter>", self._on_enter)
        self.bind("<Leave>", self._on_leave)
        self.bind("<Button-1>", self._on_click)
        
    def set_active(self, active):
        self.is_active = active
        self._draw()
        
    def _draw(self, event=None):
        self.delete("all")
        w = self.winfo_width(); h = self.winfo_height()
        if w < 10 or h < 10: return
        
        bg_color = THEME["surface_active"] if self.is_active else THEME["surface"]
        fg_color = THEME["fg"] if self.is_active else THEME["text_muted"]
        
        if self.is_hovered and not self.is_active:
            bg_color = THEME["surface_hover"]
            fg_color = THEME["fg"]
            
        self.create_rectangle(0, 0, w, h, fill=bg_color, outline="")
        
        if self.is_active:
            self.create_rectangle(0, 10, 4, h-10, fill=THEME["accent_primary"], outline="")
            
        # Fixed alignment to prevent "jomplang"
        if w < 100: # Collapsed
            if self.icon:
                self.create_text(32.5, h/2, text=self.icon, font=("Segoe UI", 16), fill=fg_color, anchor="center")
        else: # Expanded
            if self.icon:
                self.create_text(32.5, h/2, text=self.icon, font=("Segoe UI", 16), fill=fg_color, anchor="center")
                self.create_text(65, h/2, text=self.text, font=FONT_MAIN_BOLD, fill=fg_color, anchor="w")

    def _on_enter(self, event): self.is_hovered = True; self._draw()
    def _on_leave(self, event): self.is_hovered = False; self._draw()
    def _on_click(self, event):
        if self.command: self.command()

class RoundedButton(tk.Canvas):
    def __init__(self, parent, text, command=None, bg=THEME["accent_primary"], fg="#FFFFFF", hover_bg=THEME["accent_hover"], radius=8, font=FONT_MAIN_BOLD, padx=24, pady=10, state="normal"):
        super().__init__(parent, highlightthickness=0, bg=THEME["bg"], cursor="hand2" if state=="normal" else "arrow")
        self.command = command
        self.bg_color = bg; self.hover_bg = hover_bg; self.fg_color = fg
        self.radius = radius; self.text = text; self.font = font; self.state = state
        self.padx = padx; self.pady = pady
        
        self.bind("<Configure>", self._queue_draw)
        self.bind("<Enter>", self._on_enter)
        self.bind("<Leave>", self._on_leave)
        self.bind("<Button-1>", self._on_click)
        self.bind("<ButtonRelease-1>", self._on_release)
        
        lbl = tk.Label(font=self.font, text=self.text)
        self.config(width=lbl.winfo_reqwidth() + (self.padx * 2), height=lbl.winfo_reqheight() + (self.pady * 2))
        
    def _queue_draw(self, event=None):
        if hasattr(self, '_draw_timer'): self.after_cancel(self._draw_timer)
        self._draw_timer = self.after(10, self._draw)
        
    def _draw(self, event=None):
        self.delete("all")
        w = self.winfo_width(); h = self.winfo_height()
        if w < 10 or h < 10: return
        c = self.bg_color if self.state == "normal" else THEME["surface_active"]
        tc = self.fg_color if self.state == "normal" else THEME["text_muted"]
        self.rect_id = round_rectangle(self, 0, 0, w, h, radius=self.radius, fill=c, outline="")
        self.create_text(w/2, h/2, text=self.text, font=self.font, fill=tc, justify="center")

    def _on_enter(self, e):
        if self.state == "normal": self.itemconfig(self.rect_id, fill=self.hover_bg)
    def _on_leave(self, e):
        if self.state == "normal": self.itemconfig(self.rect_id, fill=self.bg_color)
    def _on_click(self, e):
        if self.state == "normal": self.itemconfig(self.rect_id, fill=THEME["surface_active"])
    def _on_release(self, e):
        if self.state == "normal":
            self.itemconfig(self.rect_id, fill=self.hover_bg)
            if self.command: self.command()
            
    def config_state(self, state):
        self.state = state
        self.config(cursor="hand2" if state=="normal" else "arrow")
        self._draw()

class ModernEntry(tk.Frame):
    def __init__(self, parent, textvariable=None, **kwargs):
        super().__init__(parent, bg=THEME["border"], padx=1, pady=1)
        self.inner = tk.Frame(self, bg=THEME["bg"], padx=12, pady=10)
        self.inner.pack(fill="both", expand=True)
        self.entry = tk.Entry(self.inner, textvariable=textvariable, bg=THEME["bg"], fg=THEME["fg"], insertbackground=THEME["fg"], relief="flat", font=FONT_MAIN)
        self.entry.pack(fill="both", expand=True)

class ToggleSwitch(tk.Canvas):
    def __init__(self, parent, text="", variable=None, **kwargs):
        super().__init__(parent, height=26, bg=THEME["surface"], highlightthickness=0)
        self.variable = variable
        self.text = text
        self.bind("<Button-1>", self.toggle)
        self.bind("<Configure>", self._draw)
        if self.variable:
            self.variable.trace_add("write", lambda *a: self._queue_draw())

    def toggle(self, event):
        if self.variable: self.variable.set(not self.variable.get())
        
    def _queue_draw(self):
        if hasattr(self, '_draw_timer'): self.after_cancel(self._draw_timer)
        self._draw_timer = self.after(10, self._draw)

    def _draw(self, event=None):
        self.delete("all")
        w = self.winfo_width()
        is_on = self.variable.get() if self.variable else False
        
        pill_w = 40; pill_h = 22; pill_y = 2
        fill_color = THEME["accent_primary"] if is_on else THEME["border"]
        round_rectangle(self, 2, pill_y, 2 + pill_w, pill_y + pill_h, radius=11, fill=fill_color, outline="")
        
        circle_r = 18; circle_y = pill_y + 2
        if is_on: self.create_oval(2 + pill_w - circle_r - 2, circle_y, 2 + pill_w - 2, circle_y + circle_r, fill="#FFFFFF", outline="")
        else: self.create_oval(4, circle_y, 4 + circle_r, circle_y + circle_r, fill="#A1A1AA", outline="")
            
        if self.text:
            self.create_text(pill_w + 14, 13, text=self.text, font=FONT_MAIN, fill=THEME["fg"], anchor="w")

class SegmentedControl(tk.Frame):
    def __init__(self, parent, options, variable, **kwargs):
        super().__init__(parent, bg=THEME["border"], padx=1, pady=1)
        self.inner = tk.Frame(self, bg=THEME["bg"])
        self.inner.pack(fill="both", expand=True)
        self.variable = variable
        self.buttons = {}
        
        for val, text in options:
            btn = tk.Label(self.inner, text=text, font=FONT_MAIN_BOLD, bg=THEME["bg"], fg=THEME["text_muted"], padx=15, pady=8, cursor="hand2")
            btn.pack(side="left", fill="both", expand=True)
            btn.bind("<Button-1>", lambda e, v=val: self._select(v))
            self.buttons[val] = btn
            
        self.variable.trace_add("write", self._update_ui)
        self._update_ui()
        
    def _select(self, val): self.variable.set(val)
    def _update_ui(self, *args):
        current = self.variable.get()
        for val, btn in self.buttons.items():
            if val == current: btn.config(bg=THEME["surface_active"], fg=THEME["fg"])
            else: btn.config(bg=THEME["bg"], fg=THEME["text_muted"])

class ModernDropZone(tk.Canvas):
    # Retained for PSD Bucin mode
    def __init__(self, parent, title, icon="📁", initial_path="", mode="folder", on_change=None):
        super().__init__(parent, highlightthickness=0, bg=THEME["bg"], cursor="hand2")
        self.title = title
        self.icon = icon
        self.mode = mode
        self.paths = [initial_path] if initial_path else []
        self.on_change = on_change
        
        self.bind("<Configure>", self._queue_draw)
        self.bind("<Button-1>", self._browse)
        self.bind("<Button-3>", self._clear)
        self.bind("<Enter>", self._on_enter)
        self.bind("<Leave>", self._on_leave)
        
        if HAS_DND:
            self.drop_target_register(DND_FILES)
            self.dnd_bind('<<Drop>>', self._on_drop)
            
        self.is_hovered = False

    def _queue_draw(self, event=None):
        if hasattr(self, '_draw_timer'): self.after_cancel(self._draw_timer)
        self._draw_timer = self.after(10, self._draw)

    def _draw(self, event=None):
        self.delete("all")
        w = self.winfo_width(); h = self.winfo_height()
        if w < 10 or h < 10: return
        
        bg_color = THEME["surface"] if self.is_hovered else THEME["bg"]
        border_color = THEME["accent_primary"] if self.is_hovered else THEME["border"]
        if self.paths:
            border_color = THEME["success"]
            bg_color = THEME["surface"]
            
        round_rectangle(self, 2, 2, w-2, h-2, radius=12, fill=bg_color, outline=border_color, width=2, dash=(5,5) if not self.paths else ())
        
        center_y = h / 2
        if self.paths:
            self.create_text(w/2, center_y - 20, text=self.title, font=FONT_TITLE, fill=THEME["fg"])
            display_path = self.paths[0] if len(self.paths) == 1 else f"{len(self.paths)} File Terpilih"
            if len(display_path) >= 40: display_path = "..." + display_path[-37:]
            self.create_text(w/2, center_y + 10, text=display_path, font=FONT_MAIN, fill=THEME["success"])
            self.create_text(w/2, center_y + 35, text="(Klik Kanan untuk Reset)", font=FONT_HINT, fill=THEME["text_muted"])
        else:
            self.create_text(w/2, center_y - 20, text=self.icon, font=("Segoe UI", 32), fill=THEME["text_muted"])
            txt = "Pilih atau Tarik Folder" if self.mode == "folder" else "Pilih atau Tarik File"
            self.create_text(w/2, center_y + 20, text=f"{txt} {self.title}", font=FONT_TITLE, fill=THEME["text_muted"])

    def _on_enter(self, e): self.is_hovered = True; self._draw()
    def _on_leave(self, e): self.is_hovered = False; self._draw()

    def _browse(self, e):
        if self.mode == "folder":
            d = filedialog.askdirectory()
            if d:
                self.paths = [d.replace("/", "\\")]
                self._draw()
                if self.on_change: self.on_change(self.paths)
        elif self.mode == "files":
            files = filedialog.askopenfilenames()
            if files:
                self.paths = [f.replace("/", "\\") for f in files]
                self._draw()
                if self.on_change: self.on_change(self.paths)
        elif self.mode == "file":
            f = filedialog.askopenfilename()
            if f:
                self.paths = [f.replace("/", "\\")]
                self._draw()
                if self.on_change: self.on_change(self.paths)

    def _clear(self, e):
        self.paths = []
        self._draw()
        if self.on_change: self.on_change(self.paths)

    def _on_drop(self, event):
        if not event.data: return
        dropped = parse_dnd_files(event.data)
        
        if self.mode == "folder":
            for d in dropped:
                if os.path.isdir(d):
                    self.paths = [d.replace("/", "\\")]
                    break
        else:
            valid_files = [f.replace("/", "\\") for f in dropped if os.path.isfile(f)]
            if valid_files:
                if self.mode == "file": self.paths = [valid_files[0]]
                else: self.paths = valid_files
        self._draw()
        if self.on_change: self.on_change(self.paths)
        
    def get_paths(self): return self.paths
    def get_path(self): return self.paths[0] if self.paths else ""

class Spinner(tk.Canvas):
    def __init__(self, parent, size=60, **kwargs):
        super().__init__(parent, width=size, height=size, bg=THEME["bg"], highlightthickness=0, **kwargs)
        self.size = size
        self.angle = 0
        self.running = False
    def start(self):
        if not self.running:
            self.running = True
            self._animate()
    def stop(self):
        self.running = False
        self.delete("all")
    def _animate(self):
        if not self.running: return
        self.delete("all")
        self.create_arc(5, 5, self.size-5, self.size-5, start=self.angle, extent=100, style="arc", outline=THEME["accent_primary"], width=4)
        self.create_arc(5, 5, self.size-5, self.size-5, start=self.angle+180, extent=100, style="arc", outline=THEME["success"], width=4)
        self.angle = (self.angle + 15) % 360
        self.after(30, self._animate)

# --- Main App ---
class PremiumToolboxApp:
    def __init__(self, root):
        self.root = root
        self.root.title("BMachine Toolbox")
        self.root.configure(bg=THEME["bg"])
        self.settings = load_settings()
        self.restore_geometry()
        self.root.protocol("WM_DELETE_WINDOW", self._on_close)
        self._apply_titlebar_theme()
        
        # UI Structure
        self.sidebar_width_exp = 240
        self.sidebar_width_col = 65
        self.sidebar_expanded = self.settings.get("sidebar_expanded", True)
        
        self.sidebar = tk.Frame(self.root, bg=THEME["surface"], width=self.sidebar_width_exp if self.sidebar_expanded else self.sidebar_width_col)
        self.sidebar.pack(side="left", fill="y")
        self.sidebar.pack_propagate(False)
        
        # Fixed Sidebar Top to prevent "jomplang"
        self.sidebar_top = tk.Frame(self.sidebar, bg=THEME["surface"], height=70)
        self.sidebar_top.pack(fill="x", pady=(20, 10))
        self.sidebar_top.pack_propagate(False)
        
        self.btn_hamburger = tk.Label(self.sidebar_top, text="☰", font=("Segoe UI", 18), bg=THEME["surface"], fg=THEME["text_muted"], cursor="hand2")
        self.btn_hamburger.place(x=0, y=0, width=65, height=65)
        self.btn_hamburger.bind("<Button-1>", lambda e: self.toggle_sidebar())
        
        self.lbl_brand = tk.Label(self.sidebar_top, text="BMACHINE", font=FONT_TITLE, bg=THEME["surface"], fg=THEME["accent_primary"])
        self.lbl_brand.place(x=65, y=0, height=65)
        if not self.sidebar_expanded:
            self.lbl_brand.place_forget()
            
        self.content_area = tk.Frame(self.root, bg=THEME["bg"])
        self.content_area.pack(side="right", fill="both", expand=True)
        
        self.tabs = {}
        self.current_tab = None
        
        # Setup ttk styles universally
        style = ttk.Style()
        if "clam" in style.theme_names():
            style.theme_use("clam")
            
        style.configure("Treeview", background=THEME["bg"], foreground=THEME["fg"], fieldbackground=THEME["bg"], font=FONT_MAIN, rowheight=35, borderwidth=0)
        style.configure("Treeview.Heading", background=THEME["surface_active"], foreground=THEME["fg"], font=FONT_MAIN_BOLD, borderwidth=0, padding=8)
        style.map("Treeview", background=[('selected', THEME["surface_hover"])])
        
        self._add_tab("psd", "PSD Bucin", "🎨", self._build_psd_bucin)
        self._add_tab("rename", "Batch Rename", "🏷️", self._build_batch_rename)
        self._add_tab("dup", "File Duplicator", "📑", self._build_duplicator)
        
        self.switch_tab("psd")

    def toggle_sidebar(self):
        self.sidebar_expanded = not self.sidebar_expanded
        if self.sidebar_expanded:
            self.sidebar.config(width=self.sidebar_width_exp)
            self.lbl_brand.place(x=65, y=0, height=65)
        else:
            self.sidebar.config(width=self.sidebar_width_col)
            self.lbl_brand.place_forget()
        
        # Force redraw on buttons
        for tid in self.tabs:
            self.tabs[tid]["btn"]._draw()

    def _add_tab(self, tid, text, icon, builder_func):
        btn = SidebarButton(self.sidebar, text, icon, command=lambda: self.switch_tab(tid))
        btn.pack(fill="x")
        self.tabs[tid] = {"btn": btn, "builder": builder_func, "frame": None}

    def switch_tab(self, tid):
        if self.current_tab == tid: return
        
        if self.current_tab:
            self.tabs[self.current_tab]["btn"].set_active(False)
            if self.tabs[self.current_tab]["frame"]:
                self.tabs[self.current_tab]["frame"].pack_forget()
                
        self.current_tab = tid
        self.tabs[tid]["btn"].set_active(True)
        
        if not self.tabs[tid]["frame"]:
            f = tk.Frame(self.content_area, bg=THEME["bg"])
            self.tabs[tid]["builder"](f)
            self.tabs[tid]["frame"] = f
            
        self.tabs[tid]["frame"].pack(fill="both", expand=True)

    def _apply_titlebar_theme(self):
        is_dark = THEME == THEMES["dark"]
        try:
            import ctypes
            self.root.update_idletasks()
            hwnd = ctypes.windll.user32.GetParent(self.root.winfo_id())
            value = ctypes.c_int(1 if is_dark else 0)
            ctypes.windll.dwmapi.DwmSetWindowAttribute(hwnd, 20, ctypes.byref(value), ctypes.sizeof(value))
        except: pass

    def restore_geometry(self):
        w = self.settings.get("window_width", DEFAULT_WIDTH)
        h = self.settings.get("window_height", DEFAULT_HEIGHT)
        x = self.settings.get("window_x", -1)
        y = self.settings.get("window_y", -1)
        if x != -1 and y != -1: self.root.geometry(f"{w}x{h}+{x}+{y}")
        else:
            sw = self.root.winfo_screenwidth(); sh = self.root.winfo_screenheight()
            self.root.geometry(f"{w}x{h}+{(sw-w)//2}+{(sh-h)//2}")

    def _on_close(self):
        self.settings["window_width"] = self.root.winfo_width()
        self.settings["window_height"] = self.root.winfo_height()
        self.settings["window_x"] = self.root.winfo_x()
        self.settings["window_y"] = self.root.winfo_y()
        self.settings["sidebar_expanded"] = self.sidebar_expanded
        save_settings(self.settings)
        self.root.destroy()

    # =========================================================================
    # TAB 1: PSD BUCIN
    # =========================================================================
    def _build_psd_bucin(self, container):
        self.psd_container = tk.Frame(container, bg=THEME["bg"])
        self.psd_container.pack(fill="both", expand=True)
        self.show_psd_setup()
        
    def show_psd_setup(self):
        for w in self.psd_container.winfo_children(): w.destroy()
        
        container = self.psd_container
        container.rowconfigure(0, weight=0)
        container.rowconfigure(1, weight=1)
        container.rowconfigure(2, weight=0)
        container.columnconfigure(0, weight=1)
        container.columnconfigure(1, weight=1)
        
        header = tk.Frame(container, bg=THEME["bg"])
        header.grid(row=0, column=0, columnspan=2, sticky="ew", pady=(20, 15), padx=30)
        tk.Label(header, text="PSD Bucin", font=FONT_BIG, bg=THEME["bg"], fg=THEME["fg"]).pack(anchor="w")
        tk.Label(header, text="Pilih folder sumber untuk memulai proses konversi template", font=FONT_TITLE, bg=THEME["bg"], fg=THEME["text_muted"]).pack(anchor="w")
        
        self.dz_master = ModernDropZone(container, "Master PSD", "🎨", self.settings.get("last_master", ""), "folder", lambda x: self._validate_psd_setup())
        self.dz_master.grid(row=1, column=0, sticky="nsew", padx=(30, 10), pady=10)
        
        self.dz_photo = ModernDropZone(container, "Foto JPG", "📷", self.settings.get("last_pilihan", ""), "folder", lambda x: self._validate_psd_setup())
        self.dz_photo.grid(row=1, column=1, sticky="nsew", padx=(10, 30), pady=10)
        
        footer = tk.Frame(container, bg=THEME["bg"], height=65)
        footer.grid(row=2, column=0, columnspan=2, sticky="ew", pady=15, padx=30)
        footer.pack_propagate(False)
        
        self.lbl_status = tk.Label(footer, text="Menunggu Folder...", font=FONT_MAIN_BOLD, bg=THEME["bg"], fg=THEME["text_muted"])
        self.lbl_status.pack(side="left")
        
        btn_frame = tk.Frame(footer, bg=THEME["bg"])
        btn_frame.pack(side="right")
        
        self.btn_manual = RoundedButton(btn_frame, "MODE MANUAL", bg=THEME["surface"], fg=THEME["fg"], hover_bg=THEME["surface_hover"], command=self.start_manual)
        self.btn_manual.pack(side="left", padx=5)
        
        self.btn_auto = RoundedButton(btn_frame, "MODE OTOMATIS", bg=THEME["accent_primary"], hover_bg=THEME["accent_hover"], command=self.start_auto)
        self.btn_auto.pack(side="left", padx=5)
        
        self._validate_psd_setup()

    def _validate_psd_setup(self):
        m = self.dz_master.get_path(); p = self.dz_photo.get_path()
        if not m or not p:
            self.lbl_status.config(text="Pilih kedua folder terlebih dahulu", fg=THEME["text_muted"])
            self.btn_manual.config_state("disabled"); self.btn_auto.config_state("disabled")
            return
        try:
            n_psd = sum(1 for root, _, files in os.walk(m) for f in files if f.lower().endswith(('.psd', '.psb')))
            n_jpg = sum(1 for root, _, files in os.walk(p) for f in files if f.lower().endswith(('.jpg', '.jpeg', '.png')))
            if n_psd == 0:
                self.lbl_status.config(text="⚠️ Tidak ada PSD di folder Master", fg=THEME["error"])
                self.btn_manual.config_state("disabled"); self.btn_auto.config_state("disabled")
            elif n_jpg == 0:
                self.lbl_status.config(text="⚠️ Tidak ada Foto di folder", fg=THEME["error"])
                self.btn_manual.config_state("disabled"); self.btn_auto.config_state("disabled")
            else:
                self.lbl_status.config(text=f"Siap: {n_psd} Template • {n_jpg} Foto", fg=THEME["success"])
                self.btn_manual.config_state("normal"); self.btn_auto.config_state("normal")
        except Exception as e:
            self.lbl_status.config(text=f"Error: {e}", fg=THEME["error"])

    def _prepare_psd_data(self):
        m = self.dz_master.get_path(); p = self.dz_photo.get_path()
        self.psd_masters = []
        for root, _, files in os.walk(m):
            for f in files:
                if f.lower().endswith(('.psd', '.psb')):
                    full = os.path.join(root, f)
                    self.psd_masters.append((os.path.relpath(full, m), full))
        self.psd_masters.sort(key=lambda x: x[0].lower())
        
        self.jpgs = []
        for root, _, files in os.walk(p):
            for f in files:
                if f.lower().endswith(('.jpg', '.jpeg', '.png')):
                    full = os.path.join(root, f)
                    self.jpgs.append((full, os.path.relpath(full, p)))
        self.jpgs.sort(key=lambda x: x[1].lower())
        self.settings["last_master"] = m; self.settings["last_pilihan"] = p; save_settings(self.settings)
        self.master_dir = m

    def start_manual(self):
        self._prepare_psd_data()
        self.current_idx = -1
        self.logs = []
        self.is_processing = False
        self.shortcuts = self.settings.get("shortcuts", [str(i+1) for i in range(9)])
        
        for w in self.psd_container.winfo_children(): w.destroy()
        container = self.psd_container
        container.rowconfigure(0, weight=0); container.rowconfigure(1, weight=1); container.rowconfigure(2, weight=0); container.columnconfigure(0, weight=1)
        
        top_bar = tk.Frame(container, bg=THEME["surface"], height=65)
        top_bar.grid(row=0, column=0, sticky="ew")
        top_bar.pack_propagate(False)
        self.lbl_progress = tk.Label(top_bar, text="0/0", font=FONT_TITLE, bg=THEME["surface"], fg=THEME["accent_primary"])
        self.lbl_progress.pack(side="left", padx=25)
        self.lbl_filename = tk.Label(top_bar, text="Loading...", font=FONT_MAIN_BOLD, bg=THEME["surface"], fg=THEME["fg"])
        self.lbl_filename.pack(side="left", expand=True)
        RoundedButton(top_bar, "BATAL", bg=THEME["surface"], fg=THEME["text_muted"], hover_bg=THEME["surface_hover"], command=self.show_psd_setup, padx=15, pady=6).pack(side="right", padx=20, pady=10)
        
        self.canvas_frame = tk.Frame(container, bg="#000000")
        self.canvas_frame.grid(row=1, column=0, sticky="nsew")
        self.canvas = tk.Canvas(self.canvas_frame, bg="#000000", highlightthickness=0)
        self.canvas.pack(fill="both", expand=True)
        
        self.overlay = tk.Frame(self.canvas_frame, bg="#000000")
        self.spinner = Spinner(self.overlay, size=80)
        self.spinner.place(relx=0.5, rely=0.45, anchor="center")
        tk.Label(self.overlay, text="MEMPROSES...", font=FONT_TITLE, bg="#000000", fg="#FFFFFF").place(relx=0.5, rely=0.6, anchor="center")
        
        self.bottom_bar = tk.Frame(container, bg=THEME["surface"], height=100)
        self.bottom_bar.grid(row=2, column=0, sticky="ew")
        self.btn_container = tk.Frame(self.bottom_bar, bg=THEME["surface"])
        self.btn_container.pack(expand=True)
        
        colors = [THEME["accent_primary"], THEME["success"], THEME["warning"], "#8B5CF6", "#EC4899"]
        for i, (name, path) in enumerate(self.psd_masters):
            key = self.shortcuts[i] if i < len(self.shortcuts) else "?"
            c = colors[i % len(colors)]
            btn = RoundedButton(self.btn_container, f"{name} [{key}]", bg=c, hover_bg=c, command=lambda n=name, p=path: self.process_image(n, p))
            btn.pack(side="left", padx=5, pady=15)
            self.root.bind(key.lower(), lambda e, n=name, p=path: self.process_image(n, p))
            
        RoundedButton(self.btn_container, "SKIP [ESC]", bg=THEME["error"], hover_bg="#DC2626", command=self.skip_image).pack(side="left", padx=20, pady=15)
        self.root.bind("<Escape>", lambda e: self.skip_image())
        self.root.bind("<Configure>", self._on_resize_manual)
        
        self.load_next_image()

    def _on_resize_manual(self, event):
        if event.widget == self.root and hasattr(self, '_resize_timer'):
            self.root.after_cancel(self._resize_timer)
        self._resize_timer = self.root.after(100, self._draw_image)

    def load_next_image(self):
        self.current_idx += 1
        if self.current_idx >= len(self.jpgs):
            self.show_report_view()
            return
        full_jpg, rel_jpg = self.jpgs[self.current_idx]
        self.lbl_progress.config(text=f"{self.current_idx + 1} / {len(self.jpgs)}")
        self.lbl_filename.config(text=rel_jpg)
        report_progress(self.current_idx + 1, len(self.jpgs), rel_jpg)
        threading.Thread(target=self._load_image_async, args=(full_jpg,)).start()

    def _load_image_async(self, path):
        try:
            from PIL import ImageOps
            img = Image.open(path)
            img = ImageOps.exif_transpose(img)
            self.original_image = img
            self.root.after(0, self._draw_image)
        except Exception: pass

    def _draw_image(self):
        if not hasattr(self, 'canvas') or not hasattr(self, 'original_image'): return
        self.root.update_idletasks()
        cw, ch = self.canvas.winfo_width(), self.canvas.winfo_height()
        if cw < 10 or ch < 10: return
        iw, ih = self.original_image.size
        ratio = min(cw/iw, ch/ih)
        resized = self.original_image.resize((int(iw*ratio), int(ih*ratio)), Image.Resampling.LANCZOS)
        self.photo = ImageTk.PhotoImage(resized)
        self.canvas.delete("all")
        self.canvas.create_image(cw//2, ch//2, image=self.photo, anchor="center")

    def process_image(self, name, path):
        if self.is_processing: return
        self.is_processing = True
        self.overlay.place(relx=0, rely=0, relwidth=1, relheight=1)
        self.spinner.start()
        threading.Thread(target=self._process_worker, args=(name, path)).start()
        
    def _process_worker(self, name, path):
        full_jpg, rel_jpg = self.jpgs[self.current_idx]
        ext = os.path.splitext(path)[1].lower()
        tname = compute_target_name(os.path.basename(rel_jpg))
        tdir = os.path.join(self.master_dir, os.path.dirname(rel_jpg))
        try:
            os.makedirs(tdir, exist_ok=True)
            dst = os.path.join(tdir, f"{tname}{ext}")
            if os.path.exists(dst): log = ("EXIST", rel_jpg, f"Sudah ada ({name})")
            else: shutil.copy2(path, dst); log = ("OK", rel_jpg, f"Sukses -> {name}")
        except Exception as e: log = ("FAIL", rel_jpg, str(e))
        self.root.after(100, lambda: self._process_done(log))

    def _process_done(self, log):
        self.logs.append(log)
        self.spinner.stop()
        self.overlay.place_forget()
        self.is_processing = False
        self.load_next_image()
        
    def skip_image(self):
        if getattr(self, "is_processing", False): return
        self.logs.append(("SKIP", self.jpgs[self.current_idx][1], "Dilewati"))
        self.load_next_image()

    def start_auto(self):
        self._prepare_psd_data()
        self.logs = []
        self.is_cancelled = False
        for w in self.psd_container.winfo_children(): w.destroy()
        
        frame = tk.Frame(self.psd_container, bg=THEME["bg"])
        frame.place(relx=0.5, rely=0.5, anchor="center")
        self.spinner = Spinner(frame, size=100)
        self.spinner.pack()
        self.spinner.start()
        
        tk.Label(frame, text="MEMPROSES OTOMATIS", font=FONT_BIG, bg=THEME["bg"], fg=THEME["accent_primary"]).pack(pady=(30, 10))
        self.lbl_auto_status = tk.Label(frame, text="Memulai...", font=FONT_TITLE, bg=THEME["bg"], fg=THEME["text_muted"])
        self.lbl_auto_status.pack()
        self.btn_cancel = RoundedButton(self.psd_container, "BATAL", bg=THEME["error"], hover_bg="#DC2626", command=self._cancel_auto)
        self.btn_cancel.place(relx=0.5, rely=0.85, anchor="center")
        
        threading.Thread(target=self._auto_worker).start()

    def _cancel_auto(self):
        self.is_cancelled = True
        self.btn_cancel.config_state("disabled")
        self.lbl_auto_status.config(text="Membatalkan...", fg=THEME["warning"])

    def _auto_worker(self):
        psd_map = {}
        fallback_psd = self.psd_masters[0][1] if self.psd_masters else None
        for rel_name, full_path in self.psd_masters:
            p_dir = os.path.dirname(rel_name).replace("\\", "/").strip().lower()
            if p_dir not in psd_map: psd_map[p_dir] = full_path

        total = len(self.jpgs)
        for i, (full_jpg, rel_jpg) in enumerate(self.jpgs):
            if self.is_cancelled: break
            self.root.after(0, lambda msg=f"{i+1}/{total}: {rel_jpg}": self.lbl_auto_status.config(text=msg))
            report_progress(i+1, total, rel_jpg)
            try:
                jpg_dir = os.path.dirname(rel_jpg).replace("\\", "/").strip().lower()
                selected_master = psd_map.get(jpg_dir, fallback_psd)
                if not selected_master: raise Exception("Master PSD tidak ditemukan")
                master_ext = os.path.splitext(selected_master)[1].lower()
                master_name = os.path.basename(selected_master)
                tname = compute_target_name(os.path.basename(rel_jpg))
                tdir = os.path.join(self.master_dir, os.path.dirname(rel_jpg))
                os.makedirs(tdir, exist_ok=True)
                dst = os.path.join(tdir, f"{tname}{master_ext}")
                if os.path.exists(dst): self.logs.append(("EXIST", rel_jpg, "Sudah ada"))
                else: shutil.copy2(selected_master, dst); self.logs.append(("OK", rel_jpg, f"Sukses -> {master_name}"))
            except Exception as e: self.logs.append(("FAIL", rel_jpg, str(e)))
                
        self.root.after(0, self.show_report_view)

    def show_report_view(self):
        if hasattr(self, 'shortcuts'):
            for key in self.shortcuts: self.root.unbind(key.lower())
        self.root.unbind("<Escape>")
        write_bmachine_result("Laporan PSD Bucin", [f"Total: {len(self.logs)}"])
        
        for w in self.psd_container.winfo_children(): w.destroy()
        
        tk.Label(self.psd_container, text="LAPORAN SELESAI", font=FONT_BIG, bg=THEME["bg"], fg=THEME["fg"]).pack(pady=30)
        RoundedButton(self.psd_container, "KEMBALI", command=self.show_psd_setup).pack()

    # =========================================================================
    # TAB 2: POWER BATCH RENAME (1000x Aesthetic)
    # =========================================================================
    def _build_batch_rename(self, container):
        self.rename_paths = []
        
        # 1. Header (Top)
        header = tk.Frame(container, bg=THEME["bg"])
        header.pack(side="top", fill="x", padx=30, pady=(20, 5))
        tk.Label(header, text="Power Batch Rename", font=FONT_BIG, bg=THEME["bg"], fg=THEME["fg"]).pack(anchor="w")
        
        # 2. Footer (Bottom - Prevents vertical cut-off)
        footer = tk.Frame(container, bg=THEME["bg"])
        footer.pack(side="bottom", fill="x", padx=30, pady=15)
        self.btn_execute_rename = RoundedButton(footer, "EXECUTE RENAME", bg=THEME["accent_primary"], hover_bg=THEME["accent_hover"], command=self._execute_rename, padx=30)
        self.btn_execute_rename.pack(side="right")
        
        # 3. Main Area (Middle)
        main_area = tk.Frame(container, bg=THEME["bg"])
        main_area.pack(side="top", fill="both", expand=True, padx=30, pady=5)
        
        # Action Bar
        action_bar = tk.Frame(main_area, bg=THEME["bg"])
        action_bar.pack(fill="x", pady=(0, 10))
        
        RoundedButton(action_bar, "Pilih File...", bg=THEME["surface"], fg=THEME["fg"], hover_bg=THEME["surface_hover"], command=self._browse_rename_files, padx=15, pady=8).pack(side="left")
        RoundedButton(action_bar, "Bersihkan", bg=THEME["bg"], fg=THEME["text_muted"], hover_bg=THEME["surface_hover"], command=self._clear_rename_files, padx=15, pady=8).pack(side="left", padx=10)
        tk.Label(action_bar, text="* Atau drag & drop file ke arah area mana pun di tab ini", font=FONT_HINT, bg=THEME["bg"], fg=THEME["text_muted"]).pack(side="left", padx=5)
        
        self.var_search = tk.StringVar(); self.var_replace = tk.StringVar()
        self.var_use_regex = tk.BooleanVar(value=False); self.var_case = tk.BooleanVar(value=False)
        self.var_match_all = tk.BooleanVar(value=True); self.var_enum = tk.BooleanVar(value=False)
        self.var_apply = tk.StringVar(value="name")
        
        for var in [self.var_search, self.var_replace, self.var_use_regex, self.var_case, self.var_match_all, self.var_enum, self.var_apply]:
            var.trace_add("write", lambda *args: self._preview_rename())
            
        # Settings Card
        settings_card = tk.Frame(main_area, bg=THEME["border"], padx=1, pady=1)
        settings_card.pack(fill="x", pady=(0, 15))
        
        sc_inner = tk.Frame(settings_card, bg=THEME["surface"], padx=20, pady=20)
        sc_inner.pack(fill="both", expand=True)
        
        # Grid layout avoids horizontal squishing
        left_opts = tk.Frame(sc_inner, bg=THEME["surface"])
        left_opts.grid(row=0, column=0, sticky="nsew", padx=(0, 20))
        
        tk.Label(left_opts, text="Cari (Search for)", font=FONT_MAIN_BOLD, bg=THEME["surface"], fg=THEME["fg"]).pack(anchor="w", pady=(0, 5))
        ModernEntry(left_opts, textvariable=self.var_search).pack(fill="x", pady=(0, 10))
        tk.Label(left_opts, text="Ganti dengan (Replace with)", font=FONT_MAIN_BOLD, bg=THEME["surface"], fg=THEME["fg"]).pack(anchor="w", pady=(0, 5))
        ModernEntry(left_opts, textvariable=self.var_replace).pack(fill="x")
        
        right_opts = tk.Frame(sc_inner, bg=THEME["surface"])
        right_opts.grid(row=0, column=1, sticky="nsew")
        
        # Toggles in a 2x2 grid to save width
        toggles_grid = tk.Frame(right_opts, bg=THEME["surface"])
        toggles_grid.pack(fill="x")
        ToggleSwitch(toggles_grid, text="Regex", variable=self.var_use_regex).grid(row=0, column=0, sticky="w", padx=(0,15), pady=2)
        ToggleSwitch(toggles_grid, text="Match All", variable=self.var_match_all).grid(row=0, column=1, sticky="w", pady=2)
        ToggleSwitch(toggles_grid, text="Case", variable=self.var_case).grid(row=1, column=0, sticky="w", padx=(0,15), pady=2)
        ToggleSwitch(toggles_grid, text="Nomor Urut", variable=self.var_enum).grid(row=1, column=1, sticky="w", pady=2)
        
        apply_frame = tk.Frame(right_opts, bg=THEME["surface"])
        apply_frame.pack(fill="x", pady=(10, 0))
        tk.Label(apply_frame, text="Terapkan Ke:", font=FONT_MAIN_BOLD, bg=THEME["surface"], fg=THEME["fg"]).pack(anchor="w", pady=(0, 5))
        SegmentedControl(apply_frame, [("name", "Nama"), ("ext", "Ekstensi"), ("both", "Semua")], self.var_apply).pack(fill="x")
        
        sc_inner.columnconfigure(0, weight=1)
        sc_inner.columnconfigure(1, weight=1)
        
        # Treeview Preview
        tree_card = tk.Frame(main_area, bg=THEME["border"], padx=1, pady=1)
        tree_card.pack(fill="both", expand=True)
        tc_inner = tk.Frame(tree_card, bg=THEME["bg"])
        tc_inner.pack(fill="both", expand=True)
        
        self.tree_rename = ttk.Treeview(tc_inner, columns=("old", "new"), show="headings")
        self.tree_rename.heading("old", text="Nama Asli")
        self.tree_rename.heading("new", text="Nama Baru (Live Preview)")
        self.tree_rename.pack(side="left", fill="both", expand=True)
        
        vsb = ttk.Scrollbar(tc_inner, orient="vertical", command=self.tree_rename.yview)
        self.tree_rename.configure(yscrollcommand=vsb.set)
        vsb.pack(side="right", fill="y")
        
        if HAS_DND:
            main_area.drop_target_register(DND_FILES)
            main_area.dnd_bind('<<Drop>>', self._on_drop_rename)
            self.tree_rename.drop_target_register(DND_FILES)
            self.tree_rename.dnd_bind('<<Drop>>', self._on_drop_rename)

    def _browse_rename_files(self):
        files = filedialog.askopenfilenames()
        if files: self._add_rename_paths([f.replace("/", "\\") for f in files])
            
    def _clear_rename_files(self):
        self.rename_paths = []
        self._preview_rename()

    def _on_drop_rename(self, event):
        if not event.data: return
        dropped = parse_dnd_files(event.data)
        valid_files = [f.replace("/", "\\") for f in dropped if os.path.isfile(f)]
        if valid_files: self._add_rename_paths(valid_files)
            
    def _add_rename_paths(self, new_paths):
        for p in new_paths:
            if p not in self.rename_paths:
                self.rename_paths.append(p)
        self._preview_rename()

    def _preview_rename(self):
        if not hasattr(self, 'tree_rename'): return
        for item in self.tree_rename.get_children(): self.tree_rename.delete(item)
            
        search = self.var_search.get()
        replace = self.var_replace.get()
        use_regex = self.var_use_regex.get()
        case_sen = self.var_case.get()
        match_all = self.var_match_all.get()
        enum = self.var_enum.get()
        apply_to = self.var_apply.get()
        
        self.rename_plan = []
        enum_counter = 1
        
        for path in self.rename_paths:
            old_full = os.path.basename(path)
            name, ext = os.path.splitext(old_full)
            target_str = name if apply_to == "name" else (ext if apply_to == "ext" else old_full)
            new_target = target_str
            
            if search:
                try:
                    if use_regex:
                        flags = 0 if case_sen else re.IGNORECASE
                        count = 0 if match_all else 1
                        new_target = re.sub(search, replace, target_str, count=count, flags=flags)
                    else:
                        if not case_sen:
                            flags = re.IGNORECASE
                            count = 0 if match_all else 1
                            new_target = re.sub(re.escape(search), replace.replace('\\', r'\\'), target_str, count=count, flags=flags)
                        else:
                            if match_all: new_target = target_str.replace(search, replace)
                            else: new_target = target_str.replace(search, replace, 1)
                except Exception: new_target = target_str

            if enum and (new_target != target_str or not search):
                new_target = f"{new_target} ({enum_counter})"
                enum_counter += 1

            new_full = new_target + ext if apply_to == "name" else (name + new_target if apply_to == "ext" else new_target)
            new_path = os.path.join(os.path.dirname(path), new_full)
            self.rename_plan.append((path, new_path))
            
            if new_full != old_full: self.tree_rename.insert("", "end", values=(old_full, new_full))
            else: self.tree_rename.insert("", "end", values=(old_full, new_full), tags=("unchanged",))
                
        self.tree_rename.tag_configure("unchanged", foreground=THEME["text_muted"])

    def _execute_rename(self):
        if not hasattr(self, 'rename_plan') or not self.rename_plan: return
        success = 0
        for old, new in self.rename_plan:
            if old != new:
                try:
                    if not os.path.exists(new):
                        os.rename(old, new)
                        success += 1
                except: pass
        messagebox.showinfo("Berhasil", f"Berhasil merename {success} file.")
        self._clear_rename_files()


    # =========================================================================
    # TAB 3: FILE DUPLICATOR (1000x Aesthetic)
    # =========================================================================
    def _build_duplicator(self, container):
        self.dup_path = ""
        
        # 1. Header (Top)
        header = tk.Frame(container, bg=THEME["bg"])
        header.pack(side="top", fill="x", padx=30, pady=(20, 5))
        tk.Label(header, text="File Duplicator", font=FONT_BIG, bg=THEME["bg"], fg=THEME["fg"]).pack(anchor="w")
        
        # 2. Footer (Bottom)
        footer = tk.Frame(container, bg=THEME["bg"])
        footer.pack(side="bottom", fill="x", padx=30, pady=15)
        self.btn_execute_dup = RoundedButton(footer, "DUPLIKASI FILE", bg=THEME["accent_primary"], hover_bg=THEME["accent_hover"], command=self._execute_dup, padx=30)
        self.btn_execute_dup.pack(side="right")
        
        # 3. Main Area (Middle)
        main_area = tk.Frame(container, bg=THEME["bg"])
        main_area.pack(side="top", fill="both", expand=True, padx=30, pady=5)
        
        # Action Bar
        action_bar = tk.Frame(main_area, bg=THEME["bg"])
        action_bar.pack(fill="x", pady=(0, 15))
        
        RoundedButton(action_bar, "Pilih Master File...", bg=THEME["surface"], fg=THEME["fg"], hover_bg=THEME["surface_hover"], command=self._browse_dup_file, padx=15, pady=8).pack(side="left")
        self.lbl_dup_file = tk.Label(action_bar, text="* Atau tarik 1 file master ke area mana pun di tab ini", font=FONT_HINT, bg=THEME["bg"], fg=THEME["text_muted"])
        self.lbl_dup_file.pack(side="left", padx=15)
        
        # Settings Card
        settings_card = tk.Frame(main_area, bg=THEME["border"], padx=1, pady=1)
        settings_card.pack(fill="x", pady=(0, 15))
        
        sc_inner = tk.Frame(settings_card, bg=THEME["surface"], padx=25, pady=25)
        sc_inner.pack(fill="both", expand=True)
        
        tk.Label(sc_inner, text="Pola Nama Baru", font=FONT_MAIN_BOLD, bg=THEME["surface"], fg=THEME["fg"]).grid(row=0, column=0, sticky="w", pady=(0, 10))
        self.var_dup_pattern = tk.StringVar(value="Copy_{num}")
        ModernEntry(sc_inner, textvariable=self.var_dup_pattern).grid(row=0, column=1, sticky="ew", padx=15, pady=(0, 10))
        
        tk.Label(sc_inner, text="Nomor Awal", font=FONT_MAIN_BOLD, bg=THEME["surface"], fg=THEME["fg"]).grid(row=1, column=0, sticky="w", pady=10)
        self.var_dup_start = tk.StringVar(value="1")
        ModernEntry(sc_inner, textvariable=self.var_dup_start).grid(row=1, column=1, sticky="ew", padx=15, pady=10)
        
        tk.Label(sc_inner, text="Nomor Akhir", font=FONT_MAIN_BOLD, bg=THEME["surface"], fg=THEME["fg"]).grid(row=2, column=0, sticky="w", pady=10)
        self.var_dup_end = tk.StringVar(value="10")
        ModernEntry(sc_inner, textvariable=self.var_dup_end).grid(row=2, column=1, sticky="ew", padx=15, pady=10)
        
        tk.Label(sc_inner, text="* Gunakan {num} pada pola nama sebagai penanda angka otomatis", font=FONT_HINT, bg=THEME["surface"], fg=THEME["text_muted"]).grid(row=3, column=1, sticky="w", padx=15, pady=(5, 0))
        
        sc_inner.columnconfigure(1, weight=1)
        
        if HAS_DND:
            main_area.drop_target_register(DND_FILES)
            main_area.dnd_bind('<<Drop>>', self._on_drop_dup)

    def _browse_dup_file(self):
        f = filedialog.askopenfilename()
        if f: self._set_dup_file(f.replace("/", "\\"))

    def _on_drop_dup(self, event):
        if not event.data: return
        dropped = parse_dnd_files(event.data)
        valid_files = [f.replace("/", "\\") for f in dropped if os.path.isfile(f)]
        if valid_files: self._set_dup_file(valid_files[0])
            
    def _set_dup_file(self, path):
        self.dup_path = path
        short = path if len(path) < 60 else "..." + path[-57:]
        self.lbl_dup_file.config(text=f"Master File: {short}", fg=THEME["success"])

    def _execute_dup(self):
        if not self.dup_path or not os.path.exists(self.dup_path):
            messagebox.showwarning("Peringatan", "Pilih file master terlebih dahulu.")
            return
            
        pattern = self.var_dup_pattern.get().strip()
        if "{num}" not in pattern:
            messagebox.showwarning("Peringatan", "Pola nama harus mengandung {num}.")
            return
            
        try:
            start = int(self.var_dup_start.get().strip())
            end = int(self.var_dup_end.get().strip())
        except:
            messagebox.showwarning("Peringatan", "Nomor Awal dan Akhir harus berupa angka.")
            return
            
        if start > end: return
        
        ext = os.path.splitext(self.dup_path)[1]
        dir_path = os.path.dirname(self.dup_path)
        
        success = 0
        for i in range(start, end + 1):
            new_name = pattern.replace("{num}", str(i)) + ext
            new_path = os.path.join(dir_path, new_name)
            if not os.path.exists(new_path):
                try:
                    shutil.copy2(self.dup_path, new_path)
                    success += 1
                except: pass
                
        messagebox.showinfo("Berhasil", f"Berhasil menduplikasi menjadi {success} file.")

if __name__ == "__main__":
    if HAS_DND: root = TkinterDnD.Tk()
    else: root = tk.Tk()
    app = PremiumToolboxApp(root)
    root.mainloop()
