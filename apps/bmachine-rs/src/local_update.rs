use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::fs;
use std::path::Path;

pub const DEFAULT_NAS_FOLDER: &str =
    r"\\delapanmataair\Editor\#MASTER DESIGN\SOFTWARE\#PROJECT ROBOT\BMachine";
pub const DEFAULT_DRIVE_FOLDER: &str = r"U:\#MASTER DESIGN\SOFTWARE\#PROJECT ROBOT\BMachine";
pub const MANIFEST_FILE_NAME: &str = "manifest.json";
#[allow(dead_code)]
pub const CHANGELOG_FILE_NAME: &str = "changelog.json";

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ManifestComponentInfo {
    #[serde(default)]
    pub name: String,
    #[serde(default)]
    pub changed: bool,
    #[serde(default = "default_true")]
    pub included: bool,
    #[serde(default)]
    pub paths: Vec<String>,
    #[serde(default)]
    pub files: Option<HashMap<String, String>>,
    #[serde(default)]
    pub atomic: bool,
}

fn default_true() -> bool {
    true
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct UpdateInfo {
    #[serde(default)]
    pub is_update_available: bool,
    #[serde(default)]
    pub already_applied_without_restart: bool,
    #[serde(default)]
    pub latest_version: String,
    #[serde(default)]
    pub current_version: String,
    #[serde(default)]
    pub source_folder: String,
    #[serde(default)]
    pub payload_folder: String,
    #[serde(default)]
    pub notes: Vec<String>,
    #[serde(default)]
    pub sha256: String,
    #[serde(default)]
    pub size_bytes: i64,
    #[serde(default)]
    pub components: Vec<ManifestComponentInfo>,
}

impl Default for UpdateInfo {
    fn default() -> Self {
        Self {
            is_update_available: false,
            already_applied_without_restart: false,
            latest_version: "8.3.0".to_string(),
            current_version: "8.3.0".to_string(),
            source_folder: String::new(),
            payload_folder: String::new(),
            notes: Vec::new(),
            sha256: String::new(),
            size_bytes: 0,
            components: Vec::new(),
        }
    }
}

pub struct LocalUpdateService;

impl LocalUpdateService {
    pub fn resolve_candidates(configured: Option<&str>) -> Vec<String> {
        let mut list = Vec::new();
        let mut add = |p: Option<String>| {
            if let Some(val) = p {
                let trimmed = val.trim().trim_matches('"').to_string();
                if !trimmed.is_empty()
                    && !list
                        .iter()
                        .any(|existing: &String| existing.eq_ignore_ascii_case(&trimmed))
                {
                    list.push(trimmed);
                }
            }
        };

        if let Ok(env_dir) = std::env::var("BMACHINE_UPDATE_DIR") {
            add(Some(env_dir));
        }
        add(configured.map(|s| s.to_string()));
        add(Some(DEFAULT_NAS_FOLDER.to_string()));
        add(Some(DEFAULT_DRIVE_FOLDER.to_string()));
        list
    }

    pub fn check(folder: Option<&str>) -> (UpdateInfo, String) {
        let current_ver = "8.3.0";
        let mut info = UpdateInfo {
            current_version: current_ver.to_string(),
            latest_version: current_ver.to_string(),
            ..Default::default()
        };

        let candidates = Self::resolve_candidates(folder);
        let mut errors = Vec::new();

        for candidate in candidates {
            let manifest_path = Path::new(&candidate).join(MANIFEST_FILE_NAME);
            if !manifest_path.exists() {
                errors.push(format!("manifest.json tidak ditemukan di {}", candidate));
                continue;
            }

            match fs::read_to_string(&manifest_path) {
                Ok(content) => {
                    if let Ok(json) = serde_json::from_str::<serde_json::Value>(&content) {
                        let latest_ver = json
                            .get("version")
                            .and_then(|v| v.as_str())
                            .unwrap_or("")
                            .trim()
                            .trim_start_matches(|c| c == 'v' || c == 'V')
                            .to_string();
                        let payload_name = json
                            .get("payload")
                            .and_then(|v| v.as_str())
                            .unwrap_or("payload");
                        let payload_folder = Path::new(&candidate)
                            .join(payload_name)
                            .to_string_lossy()
                            .to_string();
                        let sha = json
                            .get("sha256")
                            .and_then(|v| v.as_str())
                            .unwrap_or("")
                            .trim()
                            .to_string();
                        let size = json.get("sizeBytes").and_then(|v| v.as_i64()).unwrap_or(0);

                        let mut notes = Vec::new();
                        if let Some(notes_val) = json.get("notes") {
                            if let Some(arr) = notes_val.as_array() {
                                for item in arr {
                                    if let Some(s) = item.as_str() {
                                        let t = s.trim();
                                        if !t.is_empty() {
                                            notes.push(t.to_string());
                                        }
                                    }
                                }
                            }
                        }

                        let mut components = Vec::new();
                        if let Some(comps_val) = json.get("components").and_then(|v| v.as_array()) {
                            for c in comps_val {
                                if let Ok(comp) =
                                    serde_json::from_value::<ManifestComponentInfo>(c.clone())
                                {
                                    components.push(comp);
                                }
                            }
                        }

                        info.source_folder = candidate.clone();
                        info.latest_version = latest_ver.clone();
                        info.payload_folder = payload_folder;
                        info.sha256 = sha;
                        info.size_bytes = size;
                        info.notes = notes;
                        info.components = components;

                        // Periksa versi
                        info.is_update_available =
                            latest_ver != current_ver && !latest_ver.is_empty();

                        let status = if info.is_update_available {
                            format!("Versi baru v{} tersedia.", latest_ver)
                        } else {
                            format!("Sudah versi terbaru (v{}).", current_ver)
                        };

                        return (info, status);
                    } else {
                        errors.push(format!("Format json salah di {}", candidate));
                    }
                }
                Err(e) => {
                    errors.push(format!("Gagal membaca {}: {}", candidate, e));
                }
            }
        }

        let detail = if !errors.is_empty() {
            errors.join(" | ")
        } else {
            "Folder update tidak bisa dibaca".to_string()
        };
        (info, format!("Gagal: {}", detail))
    }
}
