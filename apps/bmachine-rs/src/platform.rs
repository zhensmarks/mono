use std::process::Command;

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

pub fn pick_folder(title: &str) -> Option<String> {
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
        let out = Command::new("powershell")
            .args(["-NoProfile", "-STA", "-Command", &script])
            .output()
            .ok()?;
        let s = String::from_utf8_lossy(&out.stdout).trim().to_string();
        if s.is_empty() { None } else { Some(s) }
    }
    #[cfg(not(target_os = "windows"))]
    {
        let _ = title;
        None
    }
}

pub fn pick_file(title: &str, filter: &str) -> Option<String> {
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
        let out = Command::new("powershell")
            .args(["-NoProfile", "-STA", "-Command", &script])
            .output()
            .ok()?;
        let s = String::from_utf8_lossy(&out.stdout).trim().to_string();
        if s.is_empty() { None } else { Some(s) }
    }
    #[cfg(not(target_os = "windows"))]
    {
        let _ = (title, filter);
        None
    }
}
