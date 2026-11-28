use std::process::Command;

/// Hasil dialog pilih file/folder — dibedakan karena penanganannya
/// beda (27 Nov 2026, perbaikan export/import database):
/// - Picked: user memilih sesuatu → lanjut.
/// - Cancelled: user menutup/membatalkan dialog → DIAM, tanpa toast.
/// - Unavailable: dialog gagal / platform tidak mendukung → toast
///   jujur berisi penyebabnya (bukan "hanya tersedia di Windows"
///   saat sedang di Windows).
/// (Di Linux varian Picked/Cancelled tidak pernah dibangun — dialog
/// hanya ada di Windows — jadi dead_code di sini sengaja diizinkan.)
#[allow(dead_code)]
pub enum PickResult {
    Picked(String),
    Cancelled,
    Unavailable(String),
}

pub fn open_url(url: &str) -> Result<(), String> {
    let u = url.trim();
    if u.is_empty() {
        return Err("URL kosong".to_string());
    }
    #[cfg(target_os = "windows")]
    {
        Command::new("cmd")
            .args(["/c", "start", "", u])
            .spawn()
            .map_err(|e| format!("Gagal membuka browser: {}", e))?;
        return Ok(());
    }
    #[cfg(not(target_os = "windows"))]
    {
        Command::new("xdg-open")
            .arg(u)
            .spawn()
            .map_err(|e| format!("Gagal membuka browser: {}", e))?;
        Ok(())
    }
}

pub fn pick_folder(title: &str) -> PickResult {
    #[cfg(target_os = "windows")]
    {
        let script = format!(
            "Add-Type -AssemblyName System.Windows.Forms; \
             $d = New-Object System.Windows.Forms.FolderBrowserDialog; \
             $d.Description = '{}'; \
             $d.UseDescriptionForTitle = $true; \
             if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {{ Write-Output $d.SelectedPath }}",
            title.replace('\'', "''")
        );
        let out = match Command::new("powershell")
            .args(["-NoProfile", "-STA", "-Command", &script])
            .output()
        {
            Ok(o) => o,
            Err(e) => {
                return PickResult::Unavailable(format!(
                    "Gagal menjalankan dialog pilih folder: {}",
                    e
                ))
            }
        };
        if !out.status.success() {
            return PickResult::Unavailable(format!(
                "Dialog pilih folder gagal (PowerShell exit {}): {}",
                out.status.code().unwrap_or(-1),
                String::from_utf8_lossy(&out.stderr).trim()
            ));
        }
        let s = String::from_utf8_lossy(&out.stdout).trim().to_string();
        if s.is_empty() {
            PickResult::Cancelled
        } else {
            PickResult::Picked(s)
        }
    }
    #[cfg(not(target_os = "windows"))]
    {
        // Linux: zenity dulu, lalu kdialog; keduanya dialog folder
        // beneran (bukan toast). Dipakai BATCH "+ Tambah Folder" agar
        // bisa dites di Xvfb, dan supaya port Linux tidak buta.
        for (prog, args) in [
            ("zenity", vec!["--file-selection", "--directory", "--title", title]),
            ("kdialog", vec!["--getexistingdirectory", ".", "--title", title]),
        ] {
            let out = match Command::new(prog).args(&args).output() {
                Ok(o) => o,
                Err(_) => continue,
            };
            if !out.status.success() {
                return PickResult::Cancelled;
            }
            let s = String::from_utf8_lossy(&out.stdout).trim().to_string();
            if s.is_empty() {
                return PickResult::Cancelled;
            }
            return PickResult::Picked(s);
        }
        PickResult::Unavailable(
            "Pemilih folder butuh zenity atau kdialog (tidak terpasang).".to_string(),
        )
    }
}

pub fn pick_file(title: &str, filter: &str) -> PickResult {
    #[cfg(target_os = "windows")]
    {
        let script = format!(
            "Add-Type -AssemblyName System.Windows.Forms; \
             $d = New-Object System.Windows.Forms.OpenFileDialog; \
             $d.Title = '{}'; \
             $d.Filter = '{}'; \
             if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {{ Write-Output $d.FileName }}",
            title.replace('\'', "''"),
            filter.replace('\'', "''")
        );
        let out = match Command::new("powershell")
            .args(["-NoProfile", "-STA", "-Command", &script])
            .output()
        {
            Ok(o) => o,
            Err(e) => {
                return PickResult::Unavailable(format!(
                    "Gagal menjalankan dialog pilih file: {}",
                    e
                ))
            }
        };
        if !out.status.success() {
            return PickResult::Unavailable(format!(
                "Dialog pilih file gagal (PowerShell exit {}): {}",
                out.status.code().unwrap_or(-1),
                String::from_utf8_lossy(&out.stderr).trim()
            ));
        }
        let s = String::from_utf8_lossy(&out.stdout).trim().to_string();
        if s.is_empty() {
            PickResult::Cancelled
        } else {
            PickResult::Picked(s)
        }
    }
    #[cfg(not(target_os = "windows"))]
    {
        let _ = (title, filter);
        PickResult::Unavailable(
            "Pemilih file hanya tersedia di Windows.".to_string(),
        )
    }
}

/// Salin teks ke clipboard sistem (BATCH: klik nama folder = salin
/// path — LANJUTKAN §10; harus BENERAN masuk clipboard, bukan toast).
/// - Windows: `clip` (bawaan).
/// - Linux: `xclip` → `xsel` → `wl-copy` (yang pertama ada).
/// Teks dikirim lewat stdin supaya karakter aneh (spasi, `&`, dll)
/// tidak pecah di shell.
pub fn copy_to_clipboard(text: &str) -> Result<(), String> {
    use std::io::Write;
    #[cfg(target_os = "windows")]
    {
        let mut child = Command::new("cmd")
            .args(["/c", "clip"])
            .stdin(std::process::Stdio::piped())
            .spawn()
            .map_err(|e| format!("Gagal menjalankan clip: {}", e))?;
        child
            .stdin
            .as_mut()
            .ok_or("stdin clip tidak tersedia")?
            .write_all(text.as_bytes())
            .map_err(|e| format!("Gagal menulis ke clip: {}", e))?;
        let st = child.wait().map_err(|e| format!("Gagal menunggu clip: {}", e))?;
        if st.success() {
            Ok(())
        } else {
            Err(format!("clip exit {}", st.code().unwrap_or(-1)))
        }
    }
    #[cfg(not(target_os = "windows"))]
    {
        let attempts: &[(&str, &[&str])] = &[
            ("xclip", &["-selection", "clipboard", "-i"]),
            ("xsel", &["--clipboard", "--input"]),
            ("wl-copy", &[]),
        ];
        let mut last_err = String::new();
        for (prog, args) in attempts {
            match Command::new(prog)
                .args(*args)
                .stdin(std::process::Stdio::piped())
                .spawn()
            {
                Ok(mut child) => {
                    let w = child.stdin.as_mut().unwrap().write_all(text.as_bytes());
                    if w.is_err() {
                        last_err = format!("Gagal menulis ke {}: {}", prog, w.unwrap_err());
                        continue;
                    }
                    match child.wait() {
                        Ok(st) if st.success() => return Ok(()),
                        Ok(st) => {
                            last_err = format!("{} exit {}", prog, st.code().unwrap_or(-1));
                            continue;
                        }
                        Err(e) => {
                            last_err = format!("Gagal menunggu {}: {}", prog, e);
                            continue;
                        }
                    }
                }
                Err(e) => {
                    last_err = format!("{} tidak ada: {}", prog, e);
                    continue;
                }
            }
        }
        Err(format!(
            "Clipboard tidak tersedia (xclip/xsel/wl-copy): {}",
            last_err
        ))
    }
}

/// Baca teks dari clipboard sistem (kebalikan copy_to_clipboard —
/// untuk Paste Path + Batch Rename dari clipboard, C# §2.7).
/// - Windows: PowerShell `Get-Clipboard -Raw`.
/// - Linux: `xclip -o` → `xsel -o` → `wl-paste` (yang pertama ada).
pub fn paste_from_clipboard() -> Result<String, String> {
    #[cfg(target_os = "windows")]
    {
        let out = Command::new("powershell")
            .args(["-NoProfile", "-Command", "Get-Clipboard -Raw"])
            .output()
            .map_err(|e| format!("Gagal menjalankan powershell: {}", e))?;
        if out.status.success() {
            Ok(String::from_utf8_lossy(&out.stdout).trim().to_string())
        } else {
            Err("Get-Clipboard gagal".to_string())
        }
    }
    #[cfg(not(target_os = "windows"))]
    {
        let attempts: &[(&str, &[&str])] = &[
            ("xclip", &["-selection", "clipboard", "-o"]),
            ("xsel", &["--clipboard", "--output"]),
            ("wl-paste", &[]),
        ];
        let mut last_err = String::new();
        for (prog, args) in attempts {
            match Command::new(prog).args(*args).output() {
                Ok(out) if out.status.success() => {
                    return Ok(String::from_utf8_lossy(&out.stdout).trim().to_string());
                }
                Ok(out) => {
                    last_err = format!("{} exit {}", prog, out.status.code().unwrap_or(-1));
                }
                Err(e) => {
                    last_err = format!("{} tidak ada: {}", prog, e);
                }
            }
        }
        Err(format!(
            "Clipboard tidak tersedia (xclip/xsel/wl-paste): {}",
            last_err
        ))
    }
}

/// Buka path dengan aplikasi default OS (C# `Process.Start(path)`):
/// folder -> Explorer, file -> aplikasi default.
/// - Windows: `cmd /c start`.
/// - Linux: `xdg-open`.
pub fn open_path(path: &str) -> Result<(), String> {
    let p = path.trim();
    if p.is_empty() {
        return Err("Path kosong".to_string());
    }
    #[cfg(target_os = "windows")]
    {
        Command::new("cmd")
            .args(["/c", "start", "", p])
            .spawn()
            .map_err(|e| format!("Gagal membuka {}: {}", p, e))?;
        Ok(())
    }
    #[cfg(not(target_os = "windows"))]
    {
        Command::new("xdg-open")
            .arg(p)
            .spawn()
            .map_err(|e| format!("Gagal membuka {}: {}", p, e))?;
        Ok(())
    }
}

/// Pindah path ke Recycle Bin / Trash (C# `Microsoft.VisualBasic.
/// FileIO.FileSystem.DeleteFile(path, RecycleOption.SendToRecycleBin)`).
/// - Windows: COM `Shell.Application` `NameSpace(0xA).MoveHere`.
/// - Linux: freedesktop Trash (`~/.local/share/Trash/files` +
///   `.trashinfo`); tanpa crate `trash` (tidak ada di cache offline).
/// Mengembalikan Ok(()) bila berhasil; Err(pesan) bila gagal — pemanggil
/// memutuskan fallback (hapus permanen dengan konfirmasi).
/// Salin satu path rekursif tanpa progress (untuk fallback trash lintas
/// filesystem). Symlink disalin sebagai symlink, bukan mengikuti target.
#[cfg(not(target_os = "windows"))]
fn copy_tree_plain(src: &std::path::Path, dst: &std::path::Path) -> Result<(), String> {
    let md = std::fs::symlink_metadata(src).map_err(|e| e.to_string())?;
    if md.file_type().is_symlink() {
        let target = std::fs::read_link(src).map_err(|e| e.to_string())?;
        #[cfg(unix)]
        std::os::unix::fs::symlink(&target, dst).map_err(|e| e.to_string())?;
        #[cfg(not(unix))]
        std::fs::copy(src, dst).map_err(|e| e.to_string())?;
        return Ok(());
    }
    if md.is_dir() {
        std::fs::create_dir_all(dst).map_err(|e| e.to_string())?;
        for ent in std::fs::read_dir(src).map_err(|e| e.to_string())? {
            let ent = ent.map_err(|e| e.to_string())?;
            copy_tree_plain(&ent.path(), &dst.join(ent.file_name()))?;
        }
        Ok(())
    } else {
        std::fs::copy(src, dst).map_err(|e| e.to_string())?;
        Ok(())
    }
}

pub fn move_to_trash(path: &str) -> Result<(), String> {
    let p = std::path::Path::new(path);
    if !p.exists() && std::fs::symlink_metadata(p).is_err() {
        return Err(format!("{} tidak ditemukan", path));
    }
    #[cfg(target_os = "windows")]
    {
        // PowerShell COM; path di-escape untuk single-quote.
        let esc = path.replace('\'', "''");
        let script = format!(
            "$s = New-Object -ComObject Shell.Application; \
             $s.NameSpace(0xA).MoveHere('{}')",
            esc
        );
        let out = Command::new("powershell")
            .args(["-NoProfile", "-Command", &script])
            .output()
            .map_err(|e| format!("Gagal menjalankan PowerShell: {}", e))?;
        if out.status.success() {
            Ok(())
        } else {
            Err(format!(
                "Gagal memindahkan ke Recycle Bin: {}",
                String::from_utf8_lossy(&out.stderr).trim()
            ))
        }
    }
    #[cfg(not(target_os = "windows"))]
    {
        let home = std::env::var("HOME").map_err(|_| "HOME tidak disetting".to_string())?;
        let trash_files = std::path::Path::new(&home).join(".local/share/Trash/files");
        let trash_info = std::path::Path::new(&home).join(".local/share/Trash/info");
        std::fs::create_dir_all(&trash_files).map_err(|e| e.to_string())?;
        std::fs::create_dir_all(&trash_info).map_err(|e| e.to_string())?;
        let name = p
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_else(|| "item".to_string());
        let mut dest = trash_files.join(&name);
        let mut n = 2;
        while dest.exists() || std::fs::symlink_metadata(&dest).is_ok() {
            dest = trash_files.join(format!("{} {}", name, n));
            n += 1;
        }
        // Path asal absolut untuk .trashinfo (spec freedesktop: Path =
        // lokasi asal, bukan lokasi di Trash) — dihitung SEBELUM pindah.
        let orig_abs = std::fs::canonicalize(p).unwrap_or_else(|_| p.to_path_buf());
        match std::fs::rename(p, &dest) {
            Ok(()) => {}
            // Lintas filesystem (mis. /tmp tmpfs vs /home): rename gagal
            // EXDEV — spec freedesktop: salin lalu hapus.
            Err(e) if e.kind() == std::io::ErrorKind::CrossesDevices => {
                copy_tree_plain(p, &dest)?;
                if p.is_dir() && !p.is_symlink() {
                    std::fs::remove_dir_all(p).map_err(|e| e.to_string())?;
                } else {
                    std::fs::remove_file(p).map_err(|e| e.to_string())?;
                }
            }
            Err(e) => return Err(e.to_string()),
        }
        // File .trashinfo (spec freedesktop): Path absolut asal + tanggal hapus.
        let info_name = format!(
            "{}.trashinfo",
            dest.file_name().unwrap().to_string_lossy()
        );
        let now = chrono::Local::now().format("%Y-%m-%dT%H:%M:%S").to_string();
        let content = format!("[Trash Info]\nPath={}\nDeletionDate={}\n", orig_abs.display(), now);
        let _ = std::fs::write(trash_info.join(info_name), content);
        Ok(())
    }
}
