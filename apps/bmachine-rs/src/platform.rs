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
        let _ = title;
        PickResult::Unavailable(
            "Pemilih folder hanya tersedia di Windows.".to_string(),
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
