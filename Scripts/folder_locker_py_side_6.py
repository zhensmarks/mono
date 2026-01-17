# FolderLocker_PySide6.py
# ---------------------------------------------------------------------
# A simple folder locker for Windows/macOS/Linux using AES-256 (AES-GCM)
# - LOCK: Encrypt every file in a folder (recursively) to *.dma
#         Originals moved to Recycle Bin (if send2trash is available)
#         or deleted after successful encryption.
# - UNLOCK: Decrypt *.dma back to original files/structure.
# - Optional: Quick Hide (Windows) → sets +H +S attributes (reversible).
#
# ⚠️ WARNING
# 1) Encrypted files are useless without the password. JANGAN LUPA PASSWORD.
# 2) Uji dulu pada folder kecil. Jangan matikan aplikasi saat proses berjalan.
# 3) File besar dibaca ke memori (praktis). Jika Anda menangani file > 200 MB,
#    pertimbangkan memori yang cukup. (Bisa diubah ke streaming di versi lanjut.)
#
# Dependencies:
#   pip install PySide6 cryptography send2trash
# send2trash optional; jika tidak ada, program akan fallback ke os.remove
# ---------------------------------------------------------------------

import os, sys, zlib, traceback, threading, stat, platform, ctypes
from pathlib import Path
from dataclasses import dataclass
from typing import List, Optional

try:
    from send2trash import send2trash
except Exception:
    send2trash = None

from PySide6.QtCore import Qt, QThread, Signal
from PySide6.QtWidgets import (
    QApplication, QMainWindow, QWidget, QFileDialog, QVBoxLayout, QHBoxLayout,
    QLabel, QPushButton, QLineEdit, QProgressBar, QCheckBox, QMessageBox
)

from cryptography.hazmat.primitives.kdf.pbkdf2 import PBKDF2HMAC
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
import secrets

HEADER_MAGIC = b"DMA1"  # 4 bytes header magic
SALT_SIZE = 16
NONCE_SIZE = 12
ITERATIONS = 200_000  # PBKDF2 iterations


# ---------------- crypto helpers ----------------

def derive_key(password: str, salt: bytes) -> bytes:
    kdf = PBKDF2HMAC(
        algorithm=hashes.SHA256(),
        length=32,
        salt=salt,
        iterations=ITERATIONS,
    )
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


# ---------------- worker ----------------

@dataclass
class TaskConfig:
    folder: Path
    password: str
    keep_to_trash: bool
    mode: str  # 'lock' | 'unlock'


class Worker(QThread):
    progress = Signal(int)          # 0..100
    status = Signal(str)            # message
    finished = Signal(bool, str)    # ok, msg

    def __init__(self, cfg: TaskConfig):
        super().__init__()
        self.cfg = cfg

    def run(self):
        try:
            if self.cfg.mode == 'lock':
                self._lock_folder()
            else:
                self._unlock_folder()
            self.finished.emit(True, "Selesai")
        except Exception as e:
            self.finished.emit(False, f"Gagal: {e}")

    def _iter_files(self, exts: Optional[List[str]] = None):
        root = self.cfg.folder
        all_files = []
        for p in root.rglob("*"):
            if p.is_file():
                if exts is None or p.suffix.lower() in exts:
                    all_files.append(p)
        return all_files

    def _lock_folder(self):
        files = self._iter_files(exts=None)  # semua file
        if not files:
            self.status.emit("Tidak ada file.")
            return
        total = len(files)
        done = 0
        for f in files:
            # skip jika sudah terenkripsi
            if f.suffix == ".dma":
                done += 1
                self.progress.emit(int(done * 100 / total))
                continue
            try:
                data = f.read_bytes()
                blob = encrypt_bytes(data, self.cfg.password)
                out = f.with_suffix(f.suffix + ".dma")
                out.write_bytes(blob)
                # move original to trash atau hapus
                if self.cfg.keep_to_trash and send2trash is not None:
                    send2trash(str(f))
                else:
                    f.unlink(missing_ok=True)
                self.status.emit(f"LOCK: {f.relative_to(self.cfg.folder)}")
            except Exception as e:
                self.status.emit(f"GAGAL: {f.name} -> {e}")
            done += 1
            self.progress.emit(int(done * 100 / total))

    def _unlock_folder(self):
        files = self._iter_files(exts=['.dma'])
        if not files:
            self.status.emit("Tidak ada file .dma.")
            return
        total = len(files)
        done = 0
        for f in files:
            try:
                blob = f.read_bytes()
                data = decrypt_bytes(blob, self.cfg.password)
                # pulihkan nama: hapus '.dma' saja
                orig = Path(str(f)[:-4])
                orig.parent.mkdir(parents=True, exist_ok=True)
                orig.write_bytes(data)
                f.unlink(missing_ok=True)
                self.status.emit(f"UNLOCK: {orig.relative_to(self.cfg.folder)}")
            except Exception as e:
                self.status.emit(f"GAGAL: {f.name} -> {e}")
            done += 1
            self.progress.emit(int(done * 100 / total))


# ---------------- UI ----------------

class Main(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("DMA Folder Locker (AES-256)")
        self.resize(700, 260)

        w = QWidget(); lay = QVBoxLayout(w)

        # Folder picker
        r1 = QHBoxLayout()
        self.ed_folder = QLineEdit()
        self.ed_folder.setPlaceholderText("Pilih folder yang ingin dikunci / dibuka…")
        btn_browse = QPushButton("Browse…")
        r1.addWidget(self.ed_folder, 1)
        r1.addWidget(btn_browse)

        # Password
        r2 = QHBoxLayout()
        self.ed_pass = QLineEdit(); self.ed_pass.setEchoMode(QLineEdit.Password)
        self.ed_pass.setPlaceholderText("Password")
        self.ed_pass2 = QLineEdit(); self.ed_pass2.setEchoMode(QLineEdit.Password)
        self.ed_pass2.setPlaceholderText("Ulangi password (saat LOCK)")
        r2.addWidget(self.ed_pass, 1)
        r2.addWidget(self.ed_pass2, 1)

        # Options
        r3 = QHBoxLayout()
        self.chk_trash = QCheckBox("Kirim file asli ke Recycle Bin (lebih aman)")
        self.chk_hide = QCheckBox("Quick Hide (Windows): set atribut +H +S pada folder")
        r3.addWidget(self.chk_trash)
        r3.addWidget(self.chk_hide)

        # Buttons
        r4 = QHBoxLayout()
        self.btn_lock = QPushButton("LOCK (Encrypt)")
        self.btn_unlock = QPushButton("UNLOCK (Decrypt)")
        r4.addWidget(self.btn_lock)
        r4.addWidget(self.btn_unlock)

        # Status
        self.lb_status = QLabel("Siap.")
        self.pb = QProgressBar(); self.pb.setValue(0)

        lay.addLayout(r1)
        lay.addLayout(r2)
        lay.addLayout(r3)
        lay.addLayout(r4)
        lay.addWidget(self.lb_status)
        lay.addWidget(self.pb)
        self.setCentralWidget(w)

        btn_browse.clicked.connect(self.pick_folder)
        self.btn_lock.clicked.connect(lambda: self.run(mode='lock'))
        self.btn_unlock.clicked.connect(lambda: self.run(mode='unlock'))

        self.worker: Optional[Worker] = None

    def pick_folder(self):
        d = QFileDialog.getExistingDirectory(self, "Pilih Folder")
        if d:
            self.ed_folder.setText(d)

    def run(self, mode: str):
        folder = Path(self.ed_folder.text().strip())
        if not folder.exists() or not folder.is_dir():
            QMessageBox.warning(self, "Peringatan", "Folder tidak valid.")
            return
        pwd = self.ed_pass.text()
        if not pwd:
            QMessageBox.warning(self, "Peringatan", "Password tidak boleh kosong.")
            return
        if mode == 'lock':
            if pwd != self.ed_pass2.text():
                QMessageBox.warning(self, "Peringatan", "Konfirmasi password tidak sama.")
                return
        self.pb.setValue(0)
        self.lb_status.setText("Memulai…")
        self.setEnabled(False)

        cfg = TaskConfig(folder=folder, password=pwd, keep_to_trash=self.chk_trash.isChecked(), mode=mode)
        self.worker = Worker(cfg)
        self.worker.progress.connect(self.pb.setValue)
        self.worker.status.connect(self.lb_status.setText)
        self.worker.finished.connect(self.on_done)
        self.worker.start()

        # Quick Hide/Unhide (Windows only)
        if self.chk_hide.isChecked() and platform.system() == 'Windows':
            try:
                if mode == 'lock':
                    # +H +S
                    ctypes.windll.kernel32.SetFileAttributesW(str(folder), 0x02 | 0x04)
                else:
                    # remove H & S → set NORMAL (0x80)
                    ctypes.windll.kernel32.SetFileAttributesW(str(folder), 0x80)
            except Exception:
                pass

    def on_done(self, ok: bool, msg: str):
        self.setEnabled(True)
        self.lb_status.setText(msg)
        if ok:
            QMessageBox.information(self, "Selesai", msg)
        else:
            QMessageBox.critical(self, "Gagal", msg + "\n\n" + traceback.format_exc())


if __name__ == "__main__":
    app = QApplication(sys.argv)
    mw = Main()
    mw.show()
    sys.exit(app.exec())
