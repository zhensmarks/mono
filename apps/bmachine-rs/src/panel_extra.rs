//! Grup tambahan Panel Samping (28 Nov 2026):
//! - "master tambahan" — `AdditionalMasterPaths` dari C#
//!   (`PathSettingsViewModel` + `EditablePathItem` / `StoredPathEntry`);
//!   di C# daftar ini hidup di Settings → Paths, di port ditampilkan
//!   sebagai grup di dalam tab MASTER Panel Samping (keputusan penempatan
//!   dicatat di PERUBAHAN.md).
//! - "dokumen" — daftar dokumen kerja (`.doc` / `.docx` / `.pdf`);
//!   TIDAK ada spesifikasinya di C# maupun di dokumen port — interpretasi
//!   port: daftar kurasi user di Panel Samping (tab DOC), tambah lewat
//!   pemilih file, buka di aplikasi default, hapus dari daftar.
//!   Dicatat di PERUBAHAN.md supaya abeng bisa mengoreksi.
//!
//! Kunci DB (pola `batch_db_get`: key port dulu, lalu key C#):
//! - master: tulis `Paths.AdditionalMaster`, baca jatuh ke C#
//!   `Configs.Master.AdditionalPaths` (JSON array `StoredPathEntry`).
//! - dokumen: `Panel.Dokumen` (key port saja, tanpa padanan C#).

use serde::{Deserialize, Serialize};

/// Warna default root folder — SAMA dengan C# `EditablePathItem.DefaultColor`.
pub const DEFAULT_MASTER_COLOR: &str = "#4A9EFF";

/// Palet 8 warna — SAMA dengan palet pin Explorer (`ui/explorer.slint`
/// `pin-palette` + EXPLORER_SPEC §2): klik dot warna menggilir palet ini.
pub const MASTER_COLORS: [&str; 8] = [
    "#F97316", "#8B5CF6", "#10B981", "#3B82F6", "#F43F5E", "#FBBF24", "#06B6D4", "#EC4899",
];

/// Ikon yang boleh dipilih untuk root master — HANYA nama Lucide yang
/// terdaftar di `ALL_ICON_NAMES` (`src/icons.rs`); nama lain dirender
/// SvgIcon sebagai kosong. Klik ikon menggilir daftar ini.
pub const MASTER_ICONS: [&str; 8] = [
    "folder", "folder-open", "layers", "hard-drive", "home", "image", "zap", "grid",
];

/// Satu entri TAMBAHAN MASTER. Bentuk simpan SAMA dengan C#
/// `StoredPathEntry` (key PascalCase) supaya JSON tetap bisa dibaca C#.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AdditionalMasterEntry {
    #[serde(rename = "Path", default)]
    pub path: String,
    #[serde(rename = "Name", default)]
    pub name: String,
    #[serde(rename = "Color", default)]
    pub color: String,
    #[serde(rename = "Icon", default)]
    pub icon: String,
}

impl AdditionalMasterEntry {
    pub fn new(path: &str) -> Self {
        Self {
            name: folder_name(path),
            path: path.to_string(),
            color: String::new(),
            icon: String::new(),
        }
    }

    /// Nama tampil: nama yang diisi user, atau nama folder dari path
    /// (C# `DisplayName` / `AutoDetectedName`).
    pub fn display_name(&self) -> String {
        if self.name.trim().is_empty() {
            folder_name(&self.path)
        } else {
            self.name.clone()
        }
    }

    /// Warna efektif (fallback default C# bila kosong).
    pub fn effective_color(&self) -> String {
        if self.color.trim().is_empty() {
            DEFAULT_MASTER_COLOR.to_string()
        } else {
            self.color.clone()
        }
    }

    /// Ikon efektif (fallback "folder" bila kosong / tidak dikenal).
    pub fn effective_icon(&self) -> String {
        if MASTER_ICONS.contains(&self.icon.as_str()) {
            self.icon.clone()
        } else {
            "folder".to_string()
        }
    }
}

/// Nama folder dari path (C# `EditablePathItem.GetFolderName`): segmen
/// terakhir setelah `/` atau `\`, tanpa trailing separator.
pub fn folder_name(path: &str) -> String {
    let trimmed = path.trim_end_matches(['/', '\\']);
    match trimmed.rsplit(['/', '\\']).next() {
        Some(name) if !name.is_empty() => name.to_string(),
        _ => trimmed.to_string(),
    }
}

/// Petakan key ikon C# (`EditablePathItem.AvailableIcons`) ke nama Lucide
/// port; key yang tidak punya padanan → "" (pakai default "folder").
pub fn map_csharp_icon(key: &str) -> &'static str {
    match key {
        "IconFolder" | "IconFolderFilled" => "folder",
        "IconFolderOpen" => "folder-open",
        "IconBolt" => "zap",
        "IconBookmark" => "book-open",
        "IconHome" | "IconHome2" => "home",
        "IconBox" => "hard-drive",
        "IconLayers" => "layers",
        _ => "",
    }
}

/// Parse JSON tersimpan jadi daftar entri — toleran seperti C#
/// `ParseStoredEntries`: array string (format lama) atau array objek
/// (`Path`/`Name`/`Color`/`Icon`, key boleh PascalCase atau camelCase).
/// Entri tanpa path dilewati; nama kosong → nama folder.
pub fn parse_additional_master(json: &str) -> Vec<AdditionalMasterEntry> {
    let Ok(value) = serde_json::from_str::<serde_json::Value>(json) else {
        return Vec::new();
    };
    let Some(arr) = value.as_array() else {
        return Vec::new();
    };
    let mut out = Vec::new();
    for elem in arr {
        if let Some(p) = elem.as_str() {
            if !p.trim().is_empty() {
                out.push(AdditionalMasterEntry::new(p));
            }
            continue;
        }
        let Some(obj) = elem.as_object() else {
            continue;
        };
        let get = |keys: &[&str]| -> String {
            keys.iter()
                .find_map(|k| obj.get(*k))
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string()
        };
        let path = get(&["Path", "path"]);
        if path.trim().is_empty() {
            continue;
        }
        let mut entry = AdditionalMasterEntry {
            path: path.clone(),
            name: get(&["Name", "name"]),
            color: get(&["Color", "color"]),
            icon: get(&["Icon", "icon"]),
        };
        if entry.name.trim().is_empty() {
            entry.name = folder_name(&path);
        }
        // Ikon dari C# dipetakan ke Lucide; ikon port sudah Lucide.
        if !MASTER_ICONS.contains(&entry.icon.as_str()) {
            entry.icon = map_csharp_icon(&entry.icon).to_string();
        }
        out.push(entry);
    }
    out
}

/// Warna berikutnya di palet (klik dot warna). Warna yang tidak ada di
/// palet (mis. hex C# bebas) → mulai dari palet[0].
pub fn next_color(current: &str) -> String {
    let cur = if current.trim().is_empty() {
        DEFAULT_MASTER_COLOR
    } else {
        current
    };
    match MASTER_COLORS.iter().position(|c| *c == cur) {
        Some(i) => MASTER_COLORS[(i + 1) % MASTER_COLORS.len()].to_string(),
        None => MASTER_COLORS[0].to_string(),
    }
}

/// Ikon berikutnya (klik ikon). Ikon yang tidak dikenal → palet[0].
pub fn next_icon(current: &str) -> String {
    match MASTER_ICONS.iter().position(|c| *c == current) {
        Some(i) => MASTER_ICONS[(i + 1) % MASTER_ICONS.len()].to_string(),
        None => MASTER_ICONS[0].to_string(),
    }
}

/// Parse warna hex "#RRGGBB" (atau "RRGGBB") jadi (r, g, b) untuk
/// `slint::Color`. Gagal → None (pemanggil pakai default).
pub fn parse_hex_color(hex: &str) -> Option<(u8, u8, u8)> {
    let h = hex.trim().trim_start_matches('#');
    if h.len() != 6 || !h.chars().all(|c| c.is_ascii_hexdigit()) {
        return None;
    }
    let r = u8::from_str_radix(&h[0..2], 16).ok()?;
    let g = u8::from_str_radix(&h[2..4], 16).ok()?;
    let b = u8::from_str_radix(&h[4..6], 16).ok()?;
    Some((r, g, b))
}

/// Ekstensi dokumen yang diterima grup DOKUMEN.
pub const DOC_EXTS: [&str; 3] = ["doc", "docx", "pdf"];

/// Satu entri DOKUMEN — hanya path file; nama + ikon diturunkan dari path.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DokumenEntry {
    #[serde(rename = "Path", default)]
    pub path: String,
}

impl DokumenEntry {
    pub fn file_name(&self) -> String {
        folder_name(&self.path)
    }
}

/// Parse JSON `Panel.Dokumen` — toleran: array string atau array objek
/// `{Path}`. Entri tanpa path dilewati.
pub fn parse_panel_docs(json: &str) -> Vec<DokumenEntry> {
    let Ok(value) = serde_json::from_str::<serde_json::Value>(json) else {
        return Vec::new();
    };
    let Some(arr) = value.as_array() else {
        return Vec::new();
    };
    arr.iter()
        .filter_map(|elem| {
            if let Some(p) = elem.as_str() {
                return Some(p.to_string());
            }
            elem.as_object().and_then(|obj| {
                obj.get("Path")
                    .or_else(|| obj.get("path"))
                    .and_then(|v| v.as_str())
                    .map(|s| s.to_string())
            })
        })
        .filter(|p| !p.trim().is_empty())
        .map(|p| DokumenEntry { path: p })
        .collect()
}

/// Benar bila path berakhiran salah satu `DOC_EXTS` (case-insensitive).
pub fn is_document_path(path: &str) -> bool {
    let lower = path.to_lowercase();
    DOC_EXTS.iter().any(|ext| lower.ends_with(&format!(".{}", ext)))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parse_string_array_legacy() {
        let list = parse_additional_master(r#"["D:/Master/Pasfoto", "D:/Master/Wisuda"]"#);
        assert_eq!(list.len(), 2);
        assert_eq!(list[0].display_name(), "Pasfoto");
        assert_eq!(list[0].effective_color(), DEFAULT_MASTER_COLOR);
        assert_eq!(list[0].effective_icon(), "folder");
    }

    #[test]
    fn parse_objects_csharp_shape() {
        let json = r##"[{"Path":"D:/Master/X","Name":"Master X","Color":"#FF5722","Icon":"IconStar"}]"##;
        let list = parse_additional_master(json);
        assert_eq!(list.len(), 1);
        assert_eq!(list[0].display_name(), "Master X");
        assert_eq!(list[0].color, "#FF5722");
        // IconStar tidak punya padanan Lucide terdaftar -> default.
        assert_eq!(list[0].effective_icon(), "folder");
    }

    #[test]
    fn parse_objects_port_shape_roundtrip() {
        let entries = vec![
            AdditionalMasterEntry {
                path: "D:/A".into(),
                name: "A".into(),
                color: "#F97316".into(),
                icon: "layers".into(),
            },
            AdditionalMasterEntry::new("D:/B"),
        ];
        let json = serde_json::to_string(&entries).unwrap();
        assert!(json.contains("\"Path\":\"D:/A\""));
        let back = parse_additional_master(&json);
        assert_eq!(back, entries);
    }

    #[test]
    fn parse_ignores_bad_json_and_empty_paths() {
        assert!(parse_additional_master("bukan json").is_empty());
        assert!(parse_additional_master(r#"[{"Path":"  "},{"Name":"x"}]"#).is_empty());
        assert!(parse_additional_master(r#"{"Path":"D:/A"}"#).is_empty());
    }

    #[test]
    fn folder_name_handles_separators() {
        assert_eq!(folder_name("D:\\Master\\Pasfoto\\"), "Pasfoto");
        assert_eq!(folder_name("/home/x/master-a"), "master-a");
        assert_eq!(folder_name("D:/"), "D:");
    }

    #[test]
    fn color_and_icon_cycle() {
        assert_eq!(next_color(""), MASTER_COLORS[0]);
        assert_eq!(next_color("#F97316"), "#8B5CF6");
        assert_eq!(next_color("#EC4899"), "#F97316");
        assert_eq!(next_color("#FF5722"), MASTER_COLORS[0]);
        assert_eq!(next_icon(""), "folder");
        assert_eq!(next_icon("folder"), "folder-open");
        assert_eq!(next_icon("grid"), "folder");
    }

    #[test]
    fn hex_color_parse() {
        assert_eq!(parse_hex_color("#4A9EFF"), Some((0x4A, 0x9E, 0xFF)));
        assert_eq!(parse_hex_color("10B981"), Some((0x10, 0xB9, 0x81)));
        assert_eq!(parse_hex_color(""), None);
        assert_eq!(parse_hex_color("#ZZZ"), None);
        assert_eq!(parse_hex_color("#12345"), None);
    }

    #[test]
    fn docs_parse_and_ext_check() {
        let docs = parse_panel_docs(
            r#"["D:/Doc/panduan.docx", {"Path":"D:/Doc/brief.pdf"}, {"Path":"  "}, 42]"#,
        );
        assert_eq!(docs.len(), 2);
        assert_eq!(docs[0].file_name(), "panduan.docx");
        assert!(is_document_path("D:/x/laporan.DOCX"));
        assert!(is_document_path("D:/x/a.pdf"));
        assert!(!is_document_path("D:/x/foto.jpg"));
        assert!(!is_document_path("D:/x/docx"));
    }
}
