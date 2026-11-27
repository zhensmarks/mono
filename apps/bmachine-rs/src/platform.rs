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
