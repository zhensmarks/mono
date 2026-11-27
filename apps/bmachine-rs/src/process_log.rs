use std::collections::VecDeque;
use std::fs;

pub struct ProcessLogService {
    logs: VecDeque<String>,
    max_logs: usize,
}

impl Default for ProcessLogService {
    fn default() -> Self {
        Self {
            logs: VecDeque::with_capacity(1000),
            max_logs: 1000,
        }
    }
}

impl ProcessLogService {
    pub fn add_log(&mut self, msg: String) {
        if self.logs.len() >= self.max_logs {
            self.logs.pop_front();
        }
        self.logs.push_back(msg);
    }

    pub fn clear(&mut self) {
        self.logs.clear();
    }

    pub fn check_progress_file() -> Option<(i32, i32, String)> {
        let temp = std::env::temp_dir();
        let path = temp.join("bmachine_progress.json");
        if !path.exists() {
            return None;
        }

        if let Ok(content) = fs::read_to_string(&path) {
            if let Ok(v) = serde_json::from_str::<serde_json::Value>(&content) {
                let current = v.get("current").and_then(|x| x.as_i64()).unwrap_or(0) as i32;
                let total = v.get("total").and_then(|x| x.as_i64()).unwrap_or(0) as i32;
                let file = v.get("file").and_then(|x| x.as_str()).unwrap_or("").to_string();
                return Some((current, total, file));
            }
        }
        None
    }

    // Hasil akhir proses (C# bmachine_result.json). true = sukses.
    pub fn check_result_file() -> Option<(bool, String)> {
        let temp = std::env::temp_dir();
        let path = temp.join("bmachine_result.json");
        if !path.exists() {
            return None;
        }
        if let Ok(content) = fs::read_to_string(&path) {
            if let Ok(v) = serde_json::from_str::<serde_json::Value>(&content) {
                let ok = v.get("success").and_then(|x| x.as_bool()).unwrap_or(true);
                let status = v
                    .get("status")
                    .and_then(|x| x.as_str())
                    .unwrap_or("Selesai")
                    .to_string();
                return Some((ok, status));
            }
        }
        None
    }

    // Benih log startup nyata (mencerminkan langkah bootstrapper C#).
    pub fn seed_boot(&mut self) {
        for line in [
            "[SYSTEM] BMachine Core Initialized",
            "> Membuka SQLite BMachine.db",
            "> Tema Dark diterapkan",
            "> Pengaturan dimuat dari database",
            "> Engine Python siap (Scripts/Master)",
            "> Dashboard siap",
        ] {
            self.add_log(line.to_string());
        }
    }

    // N baris terakhir dalam urutan kronologis (lama -> baru) untuk tampil di konsol.
    pub fn get_display_lines(&self, n: usize) -> Vec<String> {
        let start = self.logs.len().saturating_sub(n);
        self.logs.iter().skip(start).cloned().collect()
    }
}
