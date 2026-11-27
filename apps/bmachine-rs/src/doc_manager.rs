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
            name: if number == 1 {
                "SDN RANCAIYUH I".to_string()
            } else {
                String::new()
            },
            address: if number == 1 {
                "Jl. Raya Rancaiyuh No. 12, Kec. Parongpong, Kab. Bandung Barat 40559".to_string()
            } else {
                String::new()
            },
            logo_path: String::new(),
            logo_path2: String::new(),
            show_second_logo: false,
        }
    }
}

pub struct DocManager {
    pub tabs: Vec<DocTabItem>,
    #[allow(dead_code)]
    pub active_index: usize,
}

impl Default for DocManager {
    fn default() -> Self {
        Self {
            tabs: vec![DocTabItem::new(1)],
            active_index: 0,
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
            "nameSources": ["TK DELAPAN MATA AIR"],
            "addressSources": ["JL. RAYA CIMAHI NO. 88"],
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
