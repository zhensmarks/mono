# folder_locker_v2.py
# ---------------------------------------------------------------------
# Simplified DMA Folder Locker V2
# - Password disimpan terenkripsi di config file (hanya atasan yang tahu)
# - User biasa hanya perlu DROP folder dan klik LOCK/UNLOCK
# - Setup mode: python folder_locker_v2.py --setup
#
# Dependencies: pip install PySide6 cryptography send2trash
# ---------------------------------------------------------------------

import os
import sys
import json
import zlib
import secrets
import traceback
from pathlib import Path
from dataclasses import dataclass
from typing import Optional, List

try:
    from send2trash import send2trash
except ImportError:
    send2trash = None

from PySide6.QtCore import Qt, QThread, Signal, QMimeData
from PySide6.QtGui import QDragEnterEvent, QDropEvent, QImage, QPixmap
from PySide6.QtWidgets import (
    QApplication, QMainWindow, QWidget, QVBoxLayout, QHBoxLayout,
    QLabel, QPushButton, QProgressBar, QMessageBox, QInputDialog, QLineEdit,
    QListWidget, QListWidgetItem, QFrame
)

from cryptography.hazmat.primitives.kdf.pbkdf2 import PBKDF2HMAC
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

import pyotp
import qrcode
from io import BytesIO
from PIL import Image, ImageQt

# --- Constants ---
HEADER_MAGIC = b"DMA2"
SALT_SIZE = 16
NONCE_SIZE = 12
ITERATIONS = 200_000

CONFIG_DIR = Path(os.environ.get("APPDATA", Path.home())) / "DMALocker"
CONFIG_FILE = CONFIG_DIR / "config.enc"
MASTER_SALT = b"DMALockerV2Salt!"

# --- Styles ---
STYLE = """
QMainWindow {
    background-color: #1a1a1a;
}
QLabel {
    color: #e0e0e0;
    font-size: 13px;
}
QLabel#title {
    font-size: 18px;
    font-weight: bold;
    color: white;
}
QLabel#hint {
    color: #666;
    font-size: 11px;
}
QPushButton {
    background-color: #333;
    color: white;
    border: none;
    padding: 12px 30px;
    font-size: 14px;
    font-weight: bold;
    border-radius: 6px;
}
QPushButton:hover {
    background-color: #444;
}
QPushButton:disabled {
    background-color: #222;
    color: #555;
}
QPushButton#lock {
    background-color: #2563eb;
}
QPushButton#lock:hover {
    background-color: #3b82f6;
}
QPushButton#unlock {
    background-color: #16a34a;
}
QPushButton#unlock:hover {
    background-color: #22c55e;
}
QProgressBar {
    background-color: #333;
    border: none;
    height: 8px;
    border-radius: 4px;
}
QProgressBar::chunk {
    background-color: #3b82f6;
    border-radius: 4px;
}
QListWidget {
    background-color: #252525;
    border: 2px dashed #444;
    border-radius: 8px;
    color: #aaa;
    font-size: 11px;
}
QListWidget:focus {
    border-color: #3b82f6;
}
"""

# --- Crypto Helpers ---

def derive_key(password: str, salt: bytes) -> bytes:
    kdf = PBKDF2HMAC(algorithm=hashes.SHA256(), length=32, salt=salt, iterations=ITERATIONS)
    return kdf.derive(password.encode("utf-8"))

def encrypt_bytes(data: bytes, password: str) -> bytes:
    salt = secrets.token_bytes(SALT_SIZE)
    key = derive_key(password, salt)
    aes = AESGCM(key)
    nonce = secrets.token_bytes(NONCE_SIZE)
    comp = zlib.compress(data, level=6)
    ct = aes.encrypt(nonce, comp, None)
    return HEADER_MAGIC + salt + nonce + ct

def decrypt_bytes(blob: bytes, password: str) -> bytes:
    if not blob.startswith(HEADER_MAGIC) or len(blob) < 4 + SALT_SIZE + NONCE_SIZE + 16:
        raise ValueError("File bukan format .dma yang valid")
    salt = blob[4:4 + SALT_SIZE]
    nonce = blob[4 + SALT_SIZE:4 + SALT_SIZE + NONCE_SIZE]
    ct = blob[4 + SALT_SIZE + NONCE_SIZE:]
    key = derive_key(password, salt)
    aes = AESGCM(key)
    comp = aes.decrypt(nonce, ct, None)
    return zlib.decompress(comp)

# --- Config Management ---

# --- Config Management ---

def get_config() -> Optional[dict]:
    if not CONFIG_FILE.exists():
        return None
    try:
        blob = CONFIG_FILE.read_bytes()
        machine_key = derive_key(os.getlogin() + "DMAv2", MASTER_SALT)
        aes = AESGCM(machine_key)
        nonce = blob[:NONCE_SIZE]
        ct = blob[NONCE_SIZE:]
        data_json = aes.decrypt(nonce, ct, None)
        return json.loads(data_json.decode("utf-8"))
    except Exception:
        return None

def save_config(data: dict) -> bool:
    try:
        CONFIG_DIR.mkdir(parents=True, exist_ok=True)
        machine_key = derive_key(os.getlogin() + "DMAv2", MASTER_SALT)
        aes = AESGCM(machine_key)
        nonce = secrets.token_bytes(NONCE_SIZE)
        json_bytes = json.dumps(data).encode("utf-8")
        ct = aes.encrypt(nonce, json_bytes, None)
        CONFIG_FILE.write_bytes(nonce + ct)
        return True
    except Exception:
        return False

# --- Worker Thread ---

@dataclass
class TaskConfig:
    folder: Path
    password: str
    mode: str

class Worker(QThread):
    progress = Signal(int)
    status = Signal(str)
    finished = Signal(bool, str)

    def __init__(self, cfg: TaskConfig):
        super().__init__()
        self.cfg = cfg

    def run(self):
        try:
            if self.cfg.mode == 'lock':
                self._lock_folder()
            else:
                self._unlock_folder()
            self.finished.emit(True, "Selesai!")
        except Exception as e:
            self.finished.emit(False, f"Gagal: {e}")

    def _iter_files(self, ext_filter=None):
        files = []
        for p in self.cfg.folder.rglob("*"):
            if p.is_file():
                if ext_filter is None or p.suffix.lower() in ext_filter:
                    files.append(p)
        return files

    def _lock_folder(self):
        files = self._iter_files()
        if not files:
            self.status.emit("Folder kosong.")
            return
        total = len(files)
        done = 0
        for f in files:
            if f.suffix == ".dma":
                done += 1
                self.progress.emit(int(done * 100 / total))
                continue
            try:
                data = f.read_bytes()
                blob = encrypt_bytes(data, self.cfg.password)
                out = f.with_suffix(f.suffix + ".dma")
                out.write_bytes(blob)
                if send2trash:
                    send2trash(str(f))
                else:
                    f.unlink(missing_ok=True)
                self.status.emit(f"LOCK: {f.name}")
            except Exception as e:
                self.status.emit(f"FAIL: {f.name}")
            done += 1
            self.progress.emit(int(done * 100 / total))

    def _unlock_folder(self):
        files = self._iter_files(ext_filter=['.dma'])
        if not files:
            self.status.emit("Tidak ada file .dma.")
            return
        total = len(files)
        done = 0
        for f in files:
            try:
                blob = f.read_bytes()
                data = decrypt_bytes(blob, self.cfg.password)
                orig = Path(str(f)[:-4])
                orig.parent.mkdir(parents=True, exist_ok=True)
                orig.write_bytes(data)
                f.unlink(missing_ok=True)
                self.status.emit(f"UNLOCK: {orig.name}")
            except Exception as e:
                self.status.emit(f"FAIL: {f.name}")
            done += 1
            self.progress.emit(int(done * 100 / total))

# --- Drop Area Widget ---

class DropArea(QListWidget):
    folderDropped = Signal(object)
    
    def __init__(self):
        super().__init__()
        self.setAcceptDrops(True)
        self.setMinimumHeight(200)
        self.folder: Optional[Path] = None
        self.reset_view()
        
    def reset_view(self):
        self.clear()
        self.folder = None
        placeholder = QListWidgetItem("Drop folder disini...")
        placeholder.setTextAlignment(Qt.AlignCenter)
        placeholder.setFlags(Qt.NoItemFlags)
        self.addItem(placeholder)
        
    def dragEnterEvent(self, event: QDragEnterEvent):
        if event.mimeData().hasUrls():
            event.accept()
            self.setStyleSheet("QListWidget { border-color: #3b82f6; }")
        else:
            event.ignore()
            
    def dragMoveEvent(self, event):
        if event.mimeData().hasUrls():
            event.accept()
        else:
            event.ignore()
        
    def dragLeaveEvent(self, event):
        self.setStyleSheet("")
        
    def dropEvent(self, event: QDropEvent):
        self.setStyleSheet("")
        urls = event.mimeData().urls()
        if urls:
            path = Path(urls[0].toLocalFile())
            if path.is_dir():
                self.load_folder(path)
                self.folderDropped.emit(path)

    def mousePressEvent(self, event):
        if event.button() == Qt.RightButton:
            self.reset_view()
            self.folderDropped.emit(None)
        else:
            super().mousePressEvent(event)
                
    def load_folder(self, folder: Path):
        self.clear()
        self.folder = folder
        
        # Count files
        all_files = list(folder.rglob("*"))
        files_normal = [f for f in all_files if f.is_file() and f.suffix.lower() != ".dma"]
        files_locked = [f for f in all_files if f.is_file() and f.suffix.lower() == ".dma"]
        
        # Header
        header = QListWidgetItem(f"Folder: {folder.name}")
        header.setFlags(Qt.NoItemFlags)
        header.setForeground(Qt.white)
        self.addItem(header)
        
        sep = QListWidgetItem(f"{'─' * 40}")
        sep.setFlags(Qt.NoItemFlags)
        self.addItem(sep)
        
        # Stats
        stat1 = QListWidgetItem(f"  File biasa: {len(files_normal)}")
        stat1.setFlags(Qt.NoItemFlags)
        self.addItem(stat1)
        
        stat2 = QListWidgetItem(f"  File terkunci (.dma): {len(files_locked)}")
        stat2.setFlags(Qt.NoItemFlags)
        self.addItem(stat2)
        
        # Preview (max 10 files)
        sep2 = QListWidgetItem("")
        sep2.setFlags(Qt.NoItemFlags)
        self.addItem(sep2)
        
        preview = (files_normal + files_locked)[:10]
        for f in preview:
            icon = "🔒" if f.suffix.lower() == ".dma" else "📄"
            item = QListWidgetItem(f"  {icon} {f.name}")
            item.setFlags(Qt.NoItemFlags)
            self.addItem(item)
            
        if len(files_normal) + len(files_locked) > 10:
            more = QListWidgetItem(f"  ... dan {len(files_normal) + len(files_locked) - 10} file lainnya")
            more.setFlags(Qt.NoItemFlags)
            self.addItem(more)

# --- Main UI ---

class MainWindow(QMainWindow):
    def __init__(self, config: dict):
        super().__init__()
        self.config = config
        self.password = config.get("password", "")
        self.totp_secret = config.get("totp", "")
        self.worker = None
        self.setWindowTitle("DMA Folder Locker V2")
        self.setMinimumSize(500, 450) # Increased size and made resizable
        self.resize(500, 500)
        self.setStyleSheet(STYLE)
        self.setup_ui()

    def setup_ui(self):
        w = QWidget()
        lay = QVBoxLayout(w)
        lay.setSpacing(15)
        lay.setContentsMargins(25, 25, 25, 25)

        # Title
        title = QLabel("Folder Locker")
        title.setObjectName("title")
        lay.addWidget(title)

        # Drop Area
        self.drop_area = DropArea()
        self.drop_area.folderDropped.connect(self.on_folder_dropped)
        lay.addWidget(self.drop_area)

        # Buttons
        r2 = QHBoxLayout()
        r2.setSpacing(10)
        self.btn_lock = QPushButton("LOCK")
        self.btn_lock.setObjectName("lock")
        self.btn_unlock = QPushButton("UNLOCK")
        self.btn_unlock.setObjectName("unlock")
        self.btn_lock.setEnabled(False)
        self.btn_unlock.setEnabled(False)
        self.btn_lock.clicked.connect(lambda: self.run('lock'))
        self.btn_unlock.clicked.connect(lambda: self.run('unlock'))
        r2.addWidget(self.btn_lock)
        r2.addWidget(self.btn_unlock)
        lay.addLayout(r2)

        # Progress
        self.pb = QProgressBar()
        self.pb.setValue(0)
        lay.addWidget(self.pb)

        # Status
        self.lb_status = QLabel("Drop folder untuk mulai")
        self.lb_status.setObjectName("hint")
        lay.addWidget(self.lb_status)

        self.setCentralWidget(w)
        
    def on_folder_dropped(self, folder):
        if folder:
            self.btn_lock.setEnabled(True)
            self.btn_unlock.setEnabled(True)
            self.lb_status.setText(f"Siap: {folder}")
        else:
            self.btn_lock.setEnabled(False)
            self.btn_unlock.setEnabled(False)
            self.lb_status.setText("Drop folder untuk mulai")

    def run(self, mode: str):
        folder = self.drop_area.folder
        if not folder or not folder.exists():
            QMessageBox.warning(self, "Error", "Folder tidak valid.")
            return

        # TOTP Check for Unlock
        if mode == 'unlock' and self.totp_secret:
            code, ok = QInputDialog.getText(self, "Keamanan", 
                "Masukkan Kode Authenticator (6 digit):", QLineEdit.Password)
            if not ok or not code:
                return
            
            totp = pyotp.TOTP(self.totp_secret)
            if not totp.verify(code):
                QMessageBox.critical(self, "Akses Ditolak", "Kode OTP Salah/Expired!")
                return

        self.pb.setValue(0)
        self.lb_status.setText("Memproses...")
        self.setEnabled(False)

        cfg = TaskConfig(folder=folder, password=self.password, mode=mode)
        self.worker = Worker(cfg)
        self.worker.progress.connect(self.pb.setValue)
        self.worker.status.connect(self.lb_status.setText)
        self.worker.finished.connect(self.on_done)
        self.worker.start()

    def on_done(self, ok: bool, msg: str):
        self.setEnabled(True)
        self.lb_status.setText(msg)
        self.drop_area.reset_view()
        self.btn_lock.setEnabled(False)
        self.btn_unlock.setEnabled(False)
        if ok:
            QMessageBox.information(self, "Selesai", msg)
        else:
            QMessageBox.critical(self, "Gagal", msg)

# --- Setup Mode ---

# --- Setup Mode ---

class QrDialog(QMainWindow):
    def __init__(self, provisioning_uri):
        super().__init__()
        self.setWindowTitle("Scan QR Code")
        self.resize(400, 500)
        
        w = QWidget()
        lay = QVBoxLayout(w)
        
        lbl = QLabel("Scan QR ini dengan Google Authenticator:")
        lbl.setAlignment(Qt.AlignCenter)
        lbl.setStyleSheet("color: black; font-size: 14px; font-weight: bold;")
        lay.addWidget(lbl)
        
        # Generate QR
        img = qrcode.make(provisioning_uri)
        buf = BytesIO()
        img.save(buf, format="PNG")
        qt_img = QImage.fromData(buf.getvalue())
        pix = QPixmap.fromImage(qt_img)
        
        lbl_img = QLabel()
        lbl_img.setPixmap(pix.scaled(300, 300, Qt.KeepAspectRatio))
        lbl_img.setAlignment(Qt.AlignCenter)
        lay.addWidget(lbl_img)
        
        btn = QPushButton("LANJUT (Saya sudah scan)")
        btn.clicked.connect(self.close)
        btn.setStyleSheet("background-color: #2563eb; color: white; padding: 10px;")
        lay.addWidget(btn)
        
        self.setCentralWidget(w)

def run_setup():
    app = QApplication(sys.argv)
    app.setStyle("Fusion")
    
    pwd, ok = QInputDialog.getText(None, "Setup Password", 
        "Masukkan password untuk mengunci folder:\n\n"
        "(Password ini akan disimpan terenkripsi)",
        QLineEdit.Password)
    
    if not ok or not pwd:
        return
        
    pwd2, ok = QInputDialog.getText(None, "Konfirmasi Password", 
        "Ulangi password:", QLineEdit.Password)
        
    if pwd != pwd2:
        QMessageBox.warning(None, "Error", "Password tidak cocok!")
        return
        
    # Generate TOTP
    secret = pyotp.random_base32()
    uri = pyotp.totp.TOTP(secret).provisioning_uri(name="FolderLocker", issuer_name="DMA")
    
    # Show QR
    qr_win = QrDialog(uri)
    qr_win.show()
    app.exec() # Wait for close
    
    # Save Config
    data = {"password": pwd, "totp": secret}
    if save_config(data):
        QMessageBox.information(None, "Berhasil", 
            f"Setup Selesai!\nPassword & Secret Key tersimpan.")
    else:
        QMessageBox.critical(None, "Gagal", "Gagal menyimpan config.")

# --- Entry Point ---

if __name__ == "__main__":
    if "--setup" in sys.argv:
        run_setup()
    else:
        app = QApplication(sys.argv)
        app.setStyle("Fusion")
        
        config = get_config()
        if not config:
            QMessageBox.critical(None, "Belum Setup", 
                "Konfigurasi belum ditemukan!\n\n"
                "Minta atasan untuk menjalankan:\n"
                "python folder_locker_v2.py --setup")
            sys.exit(1)
        
        mw = MainWindow(config)
        mw.show()
        sys.exit(app.exec())
