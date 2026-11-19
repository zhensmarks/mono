//! Port `LocalUpdateService` + `UpdateInfo` (mode update LAN).
//!
//! Aplikasi membaca `manifest.json` dari folder update yang dibagi di jaringan
//! (default NAS `\\delapanmataair\...`), membandingkan versi, lalu menyiapkan
//! salinan payload ke folder instalasi lewat helper PowerShell setelah app
//! ditutup. Format manifest:
//! ```json
//! {"version":"1.1.0","notes":[...],"payload":"payload","exe":"pixa.exe","sha256":"...","sizeBytes":123}
//! ```

use std::path::{Path, PathBuf};

pub const DEFAULT_FOLDER: &str =
    r"\\delapanmataair\Editor\#MASTER DESIGN\SOFTWARE\#PROJECT ROBOT\BMachine\PixaCompact";
pub const DRIVE_LETTER_FOLDER: &str =
    r"U:\#MASTER DESIGN\SOFTWARE\#PROJECT ROBOT\BMachine\PixaCompact";
pub const MANIFEST_FILE: &str = "manifest.json";
pub const CHANGELOG_FILE: &str = "changelog.json";
pub const FOLDER_ENV_VAR: &str = "PXACOMPACT_UPDATE_DIR";

/// <summary>Port `UpdateChangelogSection`.</summary>
#[derive(Clone)]
pub struct ChangelogSection {
    pub version: String,
    pub notes: Vec<String>,
}

/// <summary>Port `UpdateNoteItem`.</summary>
#[derive(Clone)]
pub struct NoteItem {
    pub is_header: bool,
    pub text: String,
}

/// <summary>Port `UpdateInfo` (mode lokal).</summary>
#[derive(Default, Clone)]
pub struct UpdateInfo {
    pub is_update_available: bool,
    pub latest_version: String,
    pub current_version: String,
    pub source_folder: String,
    pub payload_folder: String,
    pub notes: Vec<String>,
    pub changelog: Vec<ChangelogSection>,
    pub sha256: String,
    pub size_bytes: u64,
}

impl UpdateInfo {
    /// <summary>Port `SizeText`.</summary>
    pub fn size_text(&self) -> String {
        if self.size_bytes > 0 {
            format!("{:.1} MB", self.size_bytes as f64 / 1048576.0)
        } else {
            String::new()
        }
    }
    /// <summary>Port `NotesText`.</summary>
    pub fn notes_text(&self) -> String {
        self.notes.join("\n")
    }
    /// <summary>Port `NotesInline`.</summary>
    pub fn notes_inline(&self) -> String {
        self.notes.join("  •  ")
    }
    /// <summary>Port `BuildNoteItems`.</summary>
    pub fn build_note_items(&self, single_section_limit: usize) -> Vec<NoteItem> {
        let mut items = Vec::new();
        if self.changelog.len() > 1 {
            for section in &self.changelog {
                items.push(NoteItem {
                    is_header: true,
                    text: format!("------- v{} -------", section.version),
                });
                for note in &section.notes {
                    items.push(NoteItem {
                        is_header: false,
                        text: note.clone(),
                    });
                }
            }
            return items;
        }
        for note in self.notes.iter().take(single_section_limit) {
            items.push(NoteItem {
                is_header: false,
                text: note.clone(),
            });
        }
        items
    }
}

/// <summary>Port `ParseVersion`: "(maj,min,build)" — versi sederhana.</summary>
pub fn parse_version(value: &str) -> Option<(u32, u32, u32)> {
    let clean = value.trim().trim_start_matches(['v', 'V']);
    let mut parts = clean.split('.');
    let a = parts.next()?.trim().parse().ok()?;
    let b = parts.next().unwrap_or("0").trim().parse().ok()?;
    let c = parts.next().unwrap_or("0").trim().parse().ok()?;
    Some((a, b, c))
}

/// <summary>Port `GetCurrentVersion` — versi app dari env (debug = 0.0.0 dev).</summary>
pub fn current_version() -> String {
    // Versi app Rust; bisa di-set env PXA_VERSION untuk rilis.
    std::env::var("PXA_VERSION").unwrap_or_else(|_| "0.0.0".into())
}

/// <summary>Port `ResolveCandidates`.</summary>
pub fn resolve_candidates(configured: &str) -> Vec<String> {
    let mut list: Vec<String> = Vec::new();
    let mut add = |value: &str, list: &mut Vec<String>| {
        let value = value.trim().trim_matches('"').trim();
        if value.is_empty() {
            return;
        }
        // Tolak path relatif.
        if !(value.starts_with("\\\\") || value.starts_with('R') && value.len() > 2 && value.as_bytes()[1] == b':') {
            // Windows rooted: drive letter C:\ atau UNC \\server
            if !(value.starts_with("\\\\")
                || (value.len() >= 2
                    && value.as_bytes()[0].is_ascii_alphabetic()
                    && value.as_bytes()[1] == b':'))
            {
                return;
            }
        }
        if list.iter().any(|p| p.eq_ignore_ascii_case(value)) {
            return;
        }
        list.push(value.to_string());
    };
    add(&std::env::var(FOLDER_ENV_VAR).unwrap_or_default(), &mut list);
    add(configured, &mut list);
    add(DEFAULT_FOLDER, &mut list);
    add(DRIVE_LETTER_FOLDER, &mut list);
    list
}

/// Baca manifest dari satu folder. Return `Ok(UpdateInfo)` atau `Err(status)`.
fn read_manifest(folder: &str, latest_version: &mut UpdateInfo) -> Result<(), String> {
    let path = Path::new(folder).join(MANIFEST_FILE);
    if !path.exists() {
        return Err(format!("manifest.json tidak ditemukan di {folder}"));
    }
    let raw = std::fs::read_to_string(&path).map_err(|e| e.to_string())?;
    let v: serde_json::Value = serde_json::from_str(&raw).map_err(|e| e.to_string())?;
    let version_el = v.get("version").and_then(|x| x.as_str()).unwrap_or("");
    if version_el.trim().is_empty() {
        return Err("manifest.json tidak punya field 'version'.".into());
    }
    let payload = v.get("payload").and_then(|x| x.as_str()).unwrap_or("payload");
    let sha = v.get("sha256").and_then(|x| x.as_str()).unwrap_or("").trim().to_string();
    let size = v.get("sizeBytes").and_then(|x| x.as_u64()).unwrap_or(0);

    let mut notes: Vec<String> = Vec::new();
    match v.get("notes") {
        Some(serde_json::Value::Array(arr)) => {
            for n in arr {
                if let Some(s) = n.as_str() {
                    let t = s.trim();
                    if !t.is_empty() {
                        notes.push(t.to_string());
                    }
                }
            }
        }
        Some(serde_json::Value::String(s)) => {
            for line in s.lines() {
                let t = line.trim().trim_start_matches(['-', '*', '•']).trim();
                if !t.is_empty() {
                    notes.push(t.to_string());
                }
            }
        }
        _ => {}
    }

    let latest = version_el.trim().trim_start_matches(['v', 'V']).to_string();
    let current = current_version();
    let (c, l) = (parse_version(&current), parse_version(&latest));
    let is_update = match (c, l) {
        (Some(c), Some(l)) => l > c,
        _ => !latest.eq_ignore_ascii_case(&current),
    };
    let changelog = read_changelog(folder, &latest);
    *latest_version = UpdateInfo {
        is_update_available: is_update,
        latest_version: latest,
        current_version: current,
        source_folder: folder.to_string(),
        payload_folder: Path::new(folder).join(payload).to_string_lossy().into(),
        notes,
        changelog,
        sha256: sha,
        size_bytes: size,
    };
    Ok(())
}

/// Port `ReadChangelog`: versi lebih baru dari app, tidak melewati versi manifest.
fn read_changelog(folder: &str, latest: &str) -> Vec<ChangelogSection> {
    let mut sections = Vec::new();
    let path = Path::new(folder).join(CHANGELOG_FILE);
    let Ok(raw) = std::fs::read_to_string(&path) else {
        return sections;
    };
    let Ok(doc) = serde_json::from_str::<serde_json::Value>(&raw) else {
        return sections;
    };
    let Some(arr) = doc.as_array() else {
        return sections;
    };
    let current = parse_version(&current_version());
    let latest_p = parse_version(latest);
    let mut list: Vec<ChangelogSection> = Vec::new();
    for entry in arr {
        let Some(obj) = entry.as_object() else {
            continue;
        };
        let version_text = obj
            .get("version")
            .and_then(|x| x.as_str())
            .unwrap_or("")
            .trim()
            .trim_start_matches(['v', 'V']);
        let parsed = parse_version(version_text);
        if parsed.is_none() {
            continue;
        }
        if let Some(c) = current {
            if parsed.unwrap() <= c {
                continue;
            }
        }
        if let Some(l) = latest_p {
            if parsed.unwrap() > l {
                continue;
            }
        }
        let mut notes = Vec::new();
        if let Some(serde_json::Value::Array(na)) = obj.get("notes") {
            for n in na {
                if let Some(s) = n.as_str() {
                    let t = s.trim();
                    if !t.is_empty() {
                        notes.push(t.to_string());
                    }
                }
            }
        }
        if notes.is_empty() {
            continue;
        }
        list.push(ChangelogSection {
            version: version_text.to_string(),
            notes,
        });
    }
    list.sort_by(|a, b| {
        match (parse_version(&a.version), parse_version(&b.version)) {
            (Some(va), Some(vb)) => va.cmp(&vb),
            _ => a.version.cmp(&b.version),
        }
    });
    sections = list;
    sections
}

/// <summary>Port `CheckAsync` (sinchron, per kandidat folder).</summary>
pub fn check(configured: &str, allow_fallback: bool) -> (UpdateInfo, String) {
    let candidates = if allow_fallback {
        resolve_candidates(configured)
    } else {
        let first = resolve_candidates(configured).into_iter().next().unwrap_or_default();
        vec![first]
    };
    let mut errors: Vec<String> = Vec::new();
    for candidate in &candidates {
        let mut info = UpdateInfo::default();
        if read_manifest(candidate, &mut info).is_ok() {
            let status = if info.is_update_available {
                format!("Versi baru v{} tersedia.", info.latest_version)
            } else {
                format!("Sudah versi terbaru (v{}).", info.current_version)
            };
            return (info, status);
        }
        errors.push(format!("gagal baca {candidate}"));
    }
    let mut fallback = UpdateInfo::default();
    fallback.current_version = current_version();
    fallback.latest_version = fallback.current_version.clone();
    fallback.source_folder = candidates.first().cloned().unwrap_or_default();
    let detail = if errors.is_empty() {
        "manifest.json tidak ditemukan".to_string()
    } else {
        errors.join(" | ")
    };
    (fallback, format!("Folder update tidak bisa dibaca ({detail})"))
}

/// <summary>Port `ApplyAsync`: tulis helper PowerShell yang menunggu proses keluar,
/// salin payload, buka lagi. Return `(ok, pesan)`. Pemanggil menutup app setelah sukses.</summary>
pub fn apply(info: &UpdateInfo) -> (bool, String) {
    if info.payload_folder.is_empty() {
        return (false, "Sumber update tidak valid.".into());
    }
    let payload = PathBuf::from(&info.payload_folder);
    if !payload.exists() {
        return (false, format!("Folder payload tidak ditemukan: {}", payload.display()));
    }
    let exe_name = std::env::args().next()
        .map(|a| Path::new(&a).file_name().map(|s| s.to_string_lossy().into_owned()))
        .flatten()
        .unwrap_or_else(|| "pixa.exe".to_string());
    let payload_exe = payload.join(&exe_name);
    if !payload_exe.exists() {
        return (
            false,
            format!("Payload tidak berisi {exe_name}."),
        );
    }
    if !info.sha256.trim().is_empty() {
        let actual = compute_sha256(&payload_exe).unwrap_or_default();
        if !actual.eq_ignore_ascii_case(info.sha256.trim()) {
            return (
                false,
                "Payload tidak cocok dengan manifest (SHA256 beda). Update dibatalkan.".into(),
            );
        }
    }
    let install_dir = std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(|d| d.to_path_buf()))
        .unwrap_or_default();
    let pid = std::process::id();
    let log = std::env::temp_dir().join("pixacompact-update.log");
    let script = build_apply_script(&payload, &install_dir, pid);
    let script_path = std::env::temp_dir().join(format!("pixacompact-update-{}.ps1", pid));
    if std::fs::write(&script_path, &script).is_err() {
        return (false, "Gagal menulis skrip update.".into());
    }
    let mut cmd = std::process::Command::new("powershell.exe");
    cmd.args([
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-WindowStyle",
        "Hidden",
        "-File",
        script_path.to_string_lossy().as_ref(),
    ]);
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        cmd.creation_flags(0x0800_0000); // CREATE_NO_WINDOW
    }
    let _ = cmd.spawn();
    let _ = log;
    (true, "Update disiapkan. Aplikasi akan menutup lalu terbuka lagi dengan versi baru.".into())
}

fn build_apply_script(payload: &Path, install: &Path, pid: u32) -> String {
    let q = |s: &str| format!("'{}'", s.replace('\'', "''"));
    let payload_s = q(payload.to_string_lossy().as_ref());
    let install_s = q(install.to_string_lossy().as_ref());
    let exe_s = format!("{}{}", install_s, q(&format!("\\pixa.exe")));
    let log_s = q(std::env::temp_dir().join("pixacompact-update.log").to_string_lossy().as_ref());
    format!(
        r#"$ErrorActionPreference='Continue'
$log={log_s}
$install={install_s}
$payload={payload_s}
$exe=Join-Path $install 'pixa.exe'
function Log($m){{Add-Content -LiteralPath $log -Value ("[{{}}] {{}}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'),$m)}}
try {{ Wait-Process -Id {pid} -Timeout 180 -ErrorAction SilentlyContinue }} catch {{}}
Start-Sleep -Milliseconds 800
if (Test-Path -LiteralPath $exe) {{ try {{ Copy-Item -LiteralPath $exe -Destination ($exe + '.bak') -Force }} catch {{}} }}
Copy-Item -Path (Join-Path $payload '*') -Destination $install -Recurse -Force -ErrorAction Continue
Log 'payload tersalin'
if (-not $?) {{ try {{ if (Test-Path ($exe + '.bak')) {{ Copy-Item -LiteralPath ($exe + '.bak') -Destination $exe -Force }} }} catch {{}} }}
Log 'membuka aplikasi'
Start-Process -FilePath $exe
Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
"#,
    )
}

fn compute_sha256(path: &Path) -> Option<String> {
    use sha2::{Digest, Sha256};
    let data = std::fs::read(path).ok()?;
    let mut h = Sha256::new();
    h.update(&data);
    Some(hex_lower(&h.finalize().to_vec()))
}

fn hex_lower(b: &[u8]) -> String {
    let mut s = String::with_capacity(b.len() * 2);
    for x in b {
        s.push_str(&format!("{x:02x}"));
    }
    s
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parse_version_cmp() {
        assert!(parse_version("1.2.3").unwrap() < parse_version("1.2.4").unwrap());
        assert!(parse_version("1.10.0").unwrap() > parse_version("1.9.9").unwrap());
        assert_eq!(parse_version("v2.0.1").unwrap(), (2, 0, 1));
    }

    #[test]
    fn resolve_candidates_dedup() {
        let list = resolve_candidates("");
        assert!(list.iter().any(|p| p == DEFAULT_FOLDER));
        // Dedup: drive letter + default tak duplikat.
        let unique = list.len();
        let mut sorted = list.clone();
        sorted.sort();
        sorted.dedup();
        assert_eq!(unique, sorted.len());
    }

    #[test]
    fn note_items_single_section() {
        let info = UpdateInfo {
            notes: vec!["a".into(), "b".into(), "c".into(), "d".into(), "e".into()],
            ..Default::default()
        };
        let items = info.build_note_items(4);
        assert_eq!(items.len(), 4);
        assert!(!items.iter().any(|i| i.is_header));
    }

    #[test]
    fn note_items_multi_section_headers() {
        let info = UpdateInfo {
            changelog: vec![
                ChangelogSection {
                    version: "1.0".into(),
                    notes: vec!["x".into()],
                },
                ChangelogSection {
                    version: "1.1".into(),
                    notes: vec!["y".into(), "z".into()],
                },
            ],
            ..Default::default()
        };
        let items = info.build_note_items(4);
        assert!(items.iter().filter(|i| i.is_header).count() == 2);
        assert!(items.iter().any(|i| i.text == "y"));
    }

    #[test]
    fn check_missing_folder() {
        let (info, status) = check(r"C:\__tidak_ada_folder_pxa__", false);
        assert!(!info.is_update_available);
        assert!(status.contains("tidak bisa dibaca") || status.contains("tidak ditemukan"));
    }
}
