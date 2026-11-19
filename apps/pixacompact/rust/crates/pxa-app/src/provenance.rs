//! Port `Services/OutputProvenance.cs` — indeks bukti output: catat bahwa output X
//! benar-benar berasal dari input Y (path + ukuran + mtime input saat diproses).
//! Dipakai untuk memutuskan skip: file output TANPA record tidak dipercaya lagi.
//! Disimpan di `output-index.json` di folder exe.

use std::collections::HashMap;
use std::fs;
use std::path::Path;

pub struct Entry {
    pub input: String,
    pub input_size: u64,
    pub input_mtime: u64, // LastWriteTimeUtc ticks
    pub output_size: u64,
}

pub struct Provenance {
    path: std::path::PathBuf,
    cache: HashMap<String, Entry>,
    dirty: bool,
}

impl Provenance {
    /// Muat (atau buat) index di `dir` (biasanya folder exe).
    pub fn load(dir: &Path) -> Self {
        let path = dir.join("output-index.json");
        let mut cache = HashMap::new();
        if path.exists() {
            if let Ok(text) = fs::read_to_string(&path) {
                // Format sederhana: satu JSON object key=outputPath.
                if let Ok(map) = serde_json::from_str::<HashMap<String, serde_json::Value>>(&text) {
                    for (k, v) in map {
                        if let (Some(inp), Some(isize), Some(mtime), Some(osize)) = (
                            v.get("input").and_then(|x| x.as_str()),
                            v.get("inputSize").and_then(|x| x.as_u64()),
                            v.get("inputMtimeTicks").and_then(|x| x.as_u64()),
                            v.get("outputSize").and_then(|x| x.as_u64()),
                        ) {
                            cache.insert(k, Entry { input: inp.to_string(), input_size: isize, input_mtime: mtime, output_size: osize });
                        }
                    }
                }
            }
        }
        Self { path, cache, dirty: false }
    }

    /// True HANYA kalau record membuktikan output berasal dari input ini
    /// (input tak berubah) dan file output valid (≥1024 byte).
    pub fn should_skip(&mut self, input: &Path, output: &Path) -> bool {
        let Ok(out_info) = output.metadata() else {
            return false
        };
        if out_info.len() < 1024 {
            return false;
        }
        let out_key = output.to_string_lossy().to_string();
        let Some(entry) = self.cache.get(&out_key) else {
            return false
        };
        let Ok(in_info) = input.metadata() else {
            return false
        };
        if in_info.len() != entry.input_size {
            return false;
        }
        // Bandingkan path input case-insensitive (Windows) — record menyimpan
        // apa yang diberikkan Record(); kita cocokkan basename bila canonical gagal.
        if !self.same_input(&entry.input, input) {
            return false;
        }
        let ticks = in_info
            .modified()
            .ok()
            .map(|t| filetime_ticks(&t))
            .unwrap_or(0);
        ticks == entry.input_mtime
    }

    /// Catat bahwa output baru ditulis dari input ini. Panggil setelah tulis sukses.
    pub fn record(&mut self, input: &Path, output: &Path) {
        let (Ok(in_info), Ok(out_info)) = (input.metadata(), output.metadata()) else {
            return
        };
        let mtime = in_info
            .modified()
            .ok()
            .map(|t| filetime_ticks(&t))
            .unwrap_or(0);
        self.cache.insert(
            output.to_string_lossy().to_string(),
            Entry {
                input: input.to_string_lossy().to_string(),
                input_size: in_info.len(),
                input_mtime: mtime,
                output_size: out_info.len(),
            },
        );
        self.dirty = true;
    }

    /// Simpan ke disk kalau ada perubahan. Panggil di akhir proses.
    pub fn save_if_dirty(&mut self) {
        if !self.dirty || self.cache.is_empty() {
            self.dirty = false;
            return;
        }
        // Serialize sederhana (kuncing = output path, case-insensitive di Windows).
        let mut map: HashMap<String, serde_json::Value> = HashMap::new();
        for (k, e) in &self.cache {
            map.insert(
                k.clone(),
                serde_json::json!({
                    "input": e.input,
                    "inputSize": e.input_size,
                    "inputMtimeTicks": e.input_mtime,
                    "outputSize": e.output_size,
                }),
            );
        }
        if let Ok(json) = serde_json::to_string(&map) {
            let tmp = self.path.with_extension("json.tmp");
            if fs::write(&tmp, &json).is_ok() {
                let _ = fs::rename(&tmp, &self.path);
                self.dirty = false;
            }
        }
    }

    // Helpers kecil
    fn same_input(&self, recorded: &str, actual: &Path) -> bool {
        let a = actual.to_string_lossy();
        #[cfg(windows)]
        {
            recorded.eq_ignore_ascii_case(&a) || {
                let rb = recorded.rsplit('\\').next().unwrap_or(recorded);
                let ab = a.rsplit(['\\', '/']).next().unwrap_or(&a);
                rb.eq_ignore_ascii_case(ab)
            }
        }
        #[cfg(not(windows))]
        {
            recorded == a
        }
    }
}

fn filetime_ticks(mtime: &std::time::SystemTime) -> u64 {
    mtime
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs() * 10_000_000 + (d.subsec_micros() as u64) * 10)
        .unwrap_or(0)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn record_then_should_skip_true() {
        let tmp = std::env::temp_dir().join("pxa-prov-test");
        let _ = fs::create_dir_all(&tmp);
        let input = tmp.join("in.png");
        let output = tmp.join("out.png");
        fs::write(&input, b"hello input").unwrap();
        fs::write(&output, &vec![0u8; 2000]).unwrap(); // ≥1024

        let mut p = Provenance::load(&tmp);
        assert!(!p.should_skip(&input, &output)); // belum record
        p.record(&input, &output);
        assert!(p.should_skip(&input, &output)); // sekarang trust
    }

    #[test]
    fn skip_false_when_input_changed() {
        let tmp = std::env::temp_dir().join("pxa-prov-test2");
        let _ = fs::create_dir_all(&tmp);
        let input = tmp.join("in.png");
        let output = tmp.join("out.png");
        fs::write(&input, b"original").unwrap();
        fs::write(&output, &vec![0u8; 2000]).unwrap();
        let mut p = Provenance::load(&tmp);
        p.record(&input, &output);
        // Ubah input
        fs::write(&input, b"changed").unwrap();
        assert!(!p.should_skip(&input, &output)); // input berubah → jangan skip
    }

    #[test]
    fn skip_false_when_output_small() {
        let tmp = std::env::temp_dir().join("pxa-prov-test3");
        let _ = fs::create_dir_all(&tmp);
        let input = tmp.join("in.png");
        let output = tmp.join("out.png");
        fs::write(&input, b"input data").unwrap();
        fs::write(&output, b"tiny").unwrap(); // < 1024
        let mut p = Provenance::load(&tmp);
        p.record(&input, &output);
        assert!(!p.should_skip(&input, &output));
    }
}