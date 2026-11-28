use serde::{Deserialize, Serialize};
use std::fs;
use std::path::PathBuf;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DocTabItem {
    pub id: String,
    pub number: i32,
    pub title: String,
    pub name: String,
    pub address: String,
    pub logo_path: String,
    pub logo_path2: String,
    pub show_second_logo: bool,
}

impl DocTabItem {
    pub fn new(number: i32) -> Self {
        Self {
            id: format!("tab_{}", number),
            number,
            title: format!("Redaksi {}", number),
            name: String::new(),
            address: String::new(),
            logo_path: String::new(),
            logo_path2: String::new(),
            show_second_logo: false,
        }
    }
}

pub struct DocManager {
    pub tabs: Vec<DocTabItem>,
    pub active_index: usize,
    /// Daftar teks sumber (satu variasi per baris) — C# DocSettingsWindow.
    /// Kosong = field itu tidak diganti saat kirim ke Photoshop.
    pub name_sources: Vec<String>,
    pub address_sources: Vec<String>,
}

impl Default for DocManager {
    fn default() -> Self {
        Self {
            tabs: vec![DocTabItem::new(1)],
            active_index: 0,
            name_sources: Vec::new(),
            address_sources: Vec::new(),
        }
    }
}

impl DocManager {
    pub fn add_tab(&mut self) -> usize {
        let num = (self.tabs.len() + 1) as i32;
        self.tabs.push(DocTabItem::new(num));
        self.active_index = self.tabs.len() - 1;
        self.active_index
    }

    pub fn close_tab(&mut self, idx: usize) {
        if self.tabs.len() <= 1 {
            return;
        }
        if idx < self.tabs.len() {
            self.tabs.remove(idx);
            // Renumber
            for (i, t) in self.tabs.iter_mut().enumerate() {
                t.number = (i + 1) as i32;
                t.title = format!("Redaksi {}", t.number);
            }
            if self.active_index >= self.tabs.len() {
                self.active_index = self.tabs.len() - 1;
            }
        }
    }

    pub fn active_tab(&self) -> Option<&DocTabItem> {
        self.tabs.get(self.active_index)
    }

    pub fn active_tab_mut(&mut self) -> Option<&mut DocTabItem> {
        self.tabs.get_mut(self.active_index)
    }

    /// Serialisasi SEMUA tab ke JSON (DB key `Doc.Tabs`).
    pub fn to_json(&self) -> String {
        serde_json::to_string(&self.tabs).unwrap_or_else(|_| "[]".to_string())
    }

    /// Muat tab dari JSON `Doc.Tabs`; kosong/rusak -> satu tab default.
    /// Judul dinomori ulang supaya "Redaksi N" selalu berurutan.
    pub fn from_json(raw: &str) -> Self {
        let mut tabs: Vec<DocTabItem> =
            serde_json::from_str(raw).unwrap_or_default();
        tabs.retain(|t| !t.title.trim().is_empty() || !t.name.trim().is_empty());
        if tabs.is_empty() {
            return Self::default();
        }
        for (i, t) in tabs.iter_mut().enumerate() {
            t.number = (i + 1) as i32;
            t.title = format!("Redaksi {}", t.number);
            if t.id.trim().is_empty() {
                t.id = format!("tab_{}", t.number);
            }
        }
        Self {
            tabs,
            active_index: 0,
            name_sources: Vec::new(),
            address_sources: Vec::new(),
        }
    }

    pub fn set_active(&mut self, idx: usize) {
        if idx < self.tabs.len() {
            self.active_index = idx;
        }
    }

    /// Tulis field tab aktif (dipanggil tiap edit field di UI).
    pub fn update_active(
        &mut self,
        name: &str,
        address: &str,
        logo_path: &str,
        logo_path2: &str,
        show_second_logo: bool,
    ) {
        if let Some(t) = self.tabs.get_mut(self.active_index) {
            t.name = name.to_string();
            t.address = address.to_string();
            t.logo_path = logo_path.to_string();
            t.logo_path2 = logo_path2.to_string();
            t.show_second_logo = show_second_logo;
        }
    }

    /// Kosongkan field tab aktif (nama/alamat/logo) — C# ResetDocCommand.
    pub fn reset_active(&mut self) {
        if let Some(t) = self.tabs.get_mut(self.active_index) {
            t.name.clear();
            t.address.clear();
            t.logo_path.clear();
            t.logo_path2.clear();
            t.show_second_logo = false;
        }
    }

    pub fn write_doc_info_json(&self, tab_idx: usize) -> Result<PathBuf, String> {
        let tab = match self.tabs.get(tab_idx) {
            Some(t) => t,
            None => return Err("Tab tidak ditemukan".to_string()),
        };

        let app_data = std::env::var("APPDATA").unwrap_or_else(|_| ".".to_string());
        let dir = PathBuf::from(app_data).join("BMachine.v2");
        if let Err(e) = fs::create_dir_all(&dir) {
            return Err(format!("Gagal membuat direktori AppData: {}", e));
        }

        let json_path = dir.join("doc_info.json");
        let payload = serde_json::json!({
            "name": tab.name,
            "address": tab.address,
            "nameSources": self.name_sources,
            "addressSources": self.address_sources,
            "logo": tab.logo_path,
            "logo2": tab.logo_path2
        });

        match fs::write(
            &json_path,
            serde_json::to_string_pretty(&payload).unwrap_or_default(),
        ) {
            Ok(_) => Ok(json_path),
            Err(e) => Err(format!("Gagal menulis doc_info.json: {}", e)),
        }
    }
}

/// Normalisasi teks layer C# (DocSettingsWindow): spasi berlebih
/// diabaikan, huruf tidak sensitif — "JL.  SARI" == "jl. sari".
pub fn normalize_doc_text(s: &str) -> String {
    s.split_whitespace().collect::<Vec<_>>().join(" ").to_lowercase()
}

/// Apakah teks layer cocok dengan daftar sumber (C# DocSettingsWindow)?
/// Daftar kosong = field tidak diganti -> selalu false di sini; pemanggil
/// yang memutuskan "tidak diganti" dari daftar kosong.
pub fn doc_text_matches_sources(text: &str, sources: &[String]) -> bool {
    let n = normalize_doc_text(text);
    if n.is_empty() || sources.is_empty() {
        return false;
    }
    sources.iter().any(|s| normalize_doc_text(s) == n)
}

/// Parse isi TextBox multiline pengaturan (satu variasi per baris).
pub fn parse_doc_sources(text: &str) -> Vec<String> {
    text.lines()
        .map(|l| l.trim().to_string())
        .filter(|l| !l.is_empty())
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn doc_tabs_json_roundtrip() {
        let mut m = DocManager::default();
        m.update_active("TK TEST", "JL. TEST 1", "/tmp/a.png", "", false);
        m.add_tab();
        m.update_active("SD TEST", "JL. TEST 2", "", "/tmp/b.png", true);
        let json = m.to_json();
        let m2 = DocManager::from_json(&json);
        assert_eq!(m2.tabs.len(), 2);
        assert_eq!(m2.tabs[0].name, "TK TEST");
        assert_eq!(m2.tabs[1].logo_path2, "/tmp/b.png");
        assert!(m2.tabs[1].show_second_logo);
    }

    #[test]
    fn doc_tabs_json_broken_falls_back_to_default() {
        let m = DocManager::from_json("bukan json");
        assert_eq!(m.tabs.len(), 1);
        let m = DocManager::from_json("[]");
        assert_eq!(m.tabs.len(), 1);
    }

    #[test]
    fn doc_close_tab_renumbers() {
        let mut m = DocManager::default();
        m.add_tab();
        m.add_tab();
        assert_eq!(m.tabs.len(), 3);
        m.close_tab(1);
        assert_eq!(m.tabs.len(), 2);
        assert_eq!(m.tabs[1].title, "Redaksi 2");
    }

    #[test]
    fn doc_text_matches_sources_rules() {
        let src = vec!["TK DELAPAN MATA AIR".to_string(), "JL. SARI ENDAH NO. 7A".to_string()];
        // Case-insensitive + spasi berlebih diabaikan.
        assert!(doc_text_matches_sources("tk  delapan mata air", &src));
        assert!(doc_text_matches_sources("JL. SARI  ENDAH NO. 7A", &src));
        assert!(!doc_text_matches_sources("TK LAIN", &src));
        // Daftar kosong = tidak cocok (field tidak diganti).
        assert!(!doc_text_matches_sources("TK DELAPAN MATA AIR", &[]));
        assert!(!doc_text_matches_sources("", &src));
    }

    #[test]
    fn doc_parse_sources_skips_empty_lines() {
        let v = parse_doc_sources("TK A\n\n  TK B  \n\n");
        assert_eq!(v, vec!["TK A".to_string(), "TK B".to_string()]);
    }
}
