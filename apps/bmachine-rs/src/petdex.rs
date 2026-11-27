//! PetDex loader — install/hapus pet animasi dari galeri PetDex (petdex.dev).
//!
//! Model seperti skill di Hermes: pet = paket (folder) yang di-install /
//! ditambah / dihapus dari dalam aplikasi. Satu pet = folder berisi
//! `pet.json` + spritesheet (WebP, kadang PNG). Install = unduh dua file itu
//! ke folder pets BMachine (`<APPDATA>/BMachine/Pets/<slug>/`); hapus =
//! hapus foldernya. Tidak ada akun / sinkronisasi PetDex — manifest dibaca
//! publik tanpa login (`https://petdex.dev/api/manifest`).
//!
//! Format atlas + tabel animasi di-port dari petdex-core (MIT,
//! github.com/crafter-station/petdex, `crates/petdex-core/src/animation.rs`):
//! atlas grid 8 kolom, frame 192×208, 9 state per baris (idle,
//! running-right, running-left, waving, jumping, failed, waiting, running,
//! review). Frame count + durasi per state adalah konvensi format (tidak
//! ada di pet.json) — lihat `PetState::frames`.
//!
//! CATATAN: field `spriteVersionNumber` di manifest kadang tidak cocok
//! dengan pet.json / sheet aslinya (Boba: manifest bilang 1, pet.json
//! bilang 2, sheet 1536×2288 = 11 baris). Jadi geometri (kolom/baris)
//! selalu diturunkan dari sheet yang sudah di-decode, bukan dari manifest.
//!
//! Kredit pembuat WAJIB ditampilkan (submittedBy) + link halaman pet
//! (`https://petdex.dev/pets/<slug>`); kebanyakan pet adalah fan art —
//! jangan diklaim sebagai milik BMachine.

use serde::{Deserialize, Serialize};
use std::fs;
use std::path::{Path, PathBuf};

pub const MANIFEST_URL: &str = "https://petdex.dev/api/manifest";
pub const PETDEX_SITE: &str = "https://petdex.dev";
pub const ATLAS_COLS: u32 = 8;
pub const FRAME_W: u32 = 192;
pub const FRAME_H: u32 = 208;

/// Folder pets BMachine — di samping DB (`<APPDATA>/BMachine/Pets/`).
pub fn pets_dir() -> PathBuf {
    let app_data = std::env::var("APPDATA").unwrap_or_else(|_| ".".to_string());
    Path::new(&app_data).join("BMachine").join("Pets")
}

pub fn pet_page_url(slug: &str) -> String {
    format!("{PETDEX_SITE}/pets/{slug}")
}

fn http_client(timeout_secs: u64) -> Result<reqwest::blocking::Client, String> {
    reqwest::blocking::Client::builder()
        .timeout(std::time::Duration::from_secs(timeout_secs))
        .build()
        .map_err(|e| e.to_string())
}

// ---------------------------------------------------------------------------
// Manifest galeri
// ---------------------------------------------------------------------------

/// `#[serde(default)]` saja TIDAK cukup untuk field dari galeri: galeri
/// mengirim `null` eksplisit (mis. `submittedBy: null` di sebagian
/// entri) dan serde menolak null untuk String/u32 — satu entri null
/// menggagalkan parse seluruh 5340 entri. Deserializer ini menerima
/// null → nilai kosong.
fn de_null_string<'de, D>(deserializer: D) -> Result<String, D::Error>
where
    D: serde::Deserializer<'de>,
{
    let opt = Option::<String>::deserialize(deserializer)?;
    Ok(opt.unwrap_or_default())
}

fn de_null_u32<'de, D>(deserializer: D) -> Result<u32, D::Error>
where
    D: serde::Deserializer<'de>,
{
    let opt = Option::<u32>::deserialize(deserializer)?;
    Ok(opt.unwrap_or_default())
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ManifestEntry {
    #[serde(default, deserialize_with = "de_null_string")]
    pub slug: String,
    #[serde(rename = "displayName", default, deserialize_with = "de_null_string")]
    pub display_name: String,
    #[serde(default, deserialize_with = "de_null_string")]
    pub kind: String,
    #[serde(rename = "submittedBy", default, deserialize_with = "de_null_string")]
    pub submitted_by: String,
    #[serde(rename = "spritesheetUrl", default, deserialize_with = "de_null_string")]
    pub spritesheet_url: String,
    #[serde(rename = "petJsonUrl", default, deserialize_with = "de_null_string")]
    pub pet_json_url: String,
    #[serde(rename = "zipUrl", default, deserialize_with = "de_null_string")]
    pub zip_url: String,
    #[serde(rename = "spriteVersionNumber", default, deserialize_with = "de_null_u32")]
    pub sprite_version: u32,
}

#[derive(Debug, Deserialize)]
struct ManifestDoc {
    #[serde(default)]
    pets: Vec<ManifestEntry>,
}

/// Unduh manifest galeri (5340+ pet). Tanpa API key / login.
pub fn fetch_manifest() -> Result<Vec<ManifestEntry>, String> {
    let client = http_client(20)?;
    let res = client
        .get(MANIFEST_URL)
        .send()
        .map_err(|e| format!("Gagal ambil manifest PetDex: {e}"))?;
    if !res.status().is_success() {
        return Err(format!("Manifest PetDex error: {}", res.status()));
    }
    let doc: ManifestDoc = res
        .json()
        .map_err(|e| format!("Manifest PetDex bukan JSON: {e}"))?;
    Ok(doc.pets)
}

/// Cari pet di manifest: slug persis dulu (case-insensitive), lalu nama
/// mengandung query (case-insensitive, entri pertama).
pub fn find_in_manifest<'a>(pets: &'a [ManifestEntry], query: &str) -> Option<&'a ManifestEntry> {
    let q = query.trim().to_lowercase();
    if q.is_empty() {
        return None;
    }
    pets.iter()
        .find(|p| p.slug.to_lowercase() == q)
        .or_else(|| {
            pets.iter()
                .find(|p| p.display_name.to_lowercase().contains(&q))
        })
}

// ---------------------------------------------------------------------------
// pet.json + meta
// ---------------------------------------------------------------------------

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PetJson {
    #[serde(default, deserialize_with = "de_null_string")]
    pub id: String,
    #[serde(rename = "displayName", default, deserialize_with = "de_null_string")]
    pub display_name: String,
    #[serde(default, deserialize_with = "de_null_string")]
    pub description: String,
    #[serde(rename = "spriteVersionNumber", default, deserialize_with = "de_null_u32")]
    pub sprite_version: u32,
    #[serde(rename = "spritesheetPath", default, deserialize_with = "de_null_string")]
    pub spritesheet_path: String,
}

/// Info galeri yang tidak ada di pet.json (pembuat + versi manifest) —
/// ditulis sebagai sidecar `petdex-meta.json` saat install.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PetMeta {
    pub slug: String,
    pub display_name: String,
    pub submitted_by: String,
    pub sprite_version: u32,
}

#[derive(Debug, Clone)]
pub struct InstalledPet {
    pub slug: String,
    pub display_name: String,
    pub submitted_by: String,
    pub sprite_version: u32,
    pub description: String,
    pub dir: PathBuf,
}

fn read_pet_json(dir: &Path) -> Option<PetJson> {
    let text = fs::read_to_string(dir.join("pet.json")).ok()?;
    serde_json::from_str(&text).ok()
}

fn read_meta(dir: &Path) -> Option<PetMeta> {
    let text = fs::read_to_string(dir.join("petdex-meta.json")).ok()?;
    serde_json::from_str(&text).ok()
}

/// Daftar pet yang sudah ter-install (folder di pets_dir).
pub fn list_installed() -> Vec<InstalledPet> {
    let dir = pets_dir();
    let Ok(entries) = fs::read_dir(&dir) else {
        return Vec::new();
    };
    let mut out: Vec<InstalledPet> = Vec::new();
    for entry in entries.flatten() {
        let path = entry.path();
        if !path.is_dir() {
            continue;
        }
        let Some(pj) = read_pet_json(&path) else {
            continue;
        };
        let meta = read_meta(&path);
        let slug = path
            .file_name()
            .map(|s| s.to_string_lossy().to_string())
            .unwrap_or_default();
        out.push(InstalledPet {
            display_name: if pj.display_name.is_empty() {
                slug.clone()
            } else {
                pj.display_name
            },
            submitted_by: meta
                .as_ref()
                .map(|m| m.submitted_by.clone())
                .unwrap_or_default(),
            sprite_version: pj.sprite_version,
            description: pj.description,
            dir: path,
            slug,
        });
    }
    out.sort_by(|a, b| a.display_name.to_lowercase().cmp(&b.display_name.to_lowercase()));
    out
}

/// Install satu pet dari entri manifest: unduh pet.json + spritesheet,
/// validasi (decode sheet), tulis ke `Pets/<slug>/` + sidecar meta.
/// Mengembalikan info pet yang ter-install.
pub fn install_pet(entry: &ManifestEntry) -> Result<InstalledPet, String> {
    let client = http_client(30)?;
    // pet.json dulu — versi + nama file sheet datang dari sini.
    let pj_text = client
        .get(&entry.pet_json_url)
        .send()
        .map_err(|e| format!("Gagal unduh pet.json: {e}"))?
        .error_for_status()
        .map_err(|e| format!("pet.json error: {e}"))?
        .text()
        .map_err(|e| format!("Baca pet.json gagal: {e}"))?;
    let pj: PetJson =
        serde_json::from_str(&pj_text).map_err(|e| format!("pet.json bukan JSON: {e}"))?;
    let sheet_url = if entry.spritesheet_url.is_empty() {
        return Err("Manifest tidak punya spritesheetUrl".to_string());
    } else {
        entry.spritesheet_url.clone()
    };
    let sheet_bytes = client
        .get(&sheet_url)
        .send()
        .map_err(|e| format!("Gagal unduh spritesheet: {e}"))?
        .error_for_status()
        .map_err(|e| format!("Spritesheet error: {e}"))?
        .bytes()
        .map_err(|e| format!("Baca spritesheet gagal: {e}"))?;
    // Validasi: sheet harus bisa di-decode dan bergeometri atlas.
    let sheet = decode_sheet(&sheet_bytes)?;
    let _ = sheet;

    let dir = pets_dir().join(&entry.slug);
    fs::create_dir_all(&dir).map_err(|e| format!("Buat folder pet gagal: {e}"))?;
    let write_out = (|| -> Result<(), String> {
        fs::write(dir.join("pet.json"), &pj_text)
            .map_err(|e| format!("Tulis pet.json gagal: {e}"))?;
        let sheet_name = if pj.spritesheet_path.is_empty() {
            sheet_url
                .rsplit('/')
                .next()
                .unwrap_or("spritesheet.webp")
                .to_string()
        } else {
            Path::new(&pj.spritesheet_path)
                .file_name()
                .map(|s| s.to_string_lossy().to_string())
                .unwrap_or_else(|| "spritesheet.webp".to_string())
        };
        fs::write(dir.join(&sheet_name), &sheet_bytes)
            .map_err(|e| format!("Tulis spritesheet gagal: {e}"))?;
        let meta = PetMeta {
            slug: entry.slug.clone(),
            display_name: if pj.display_name.is_empty() {
                entry.display_name.clone()
            } else {
                pj.display_name.clone()
            },
            submitted_by: entry.submitted_by.clone(),
            sprite_version: pj.sprite_version,
        };
        let meta_text =
            serde_json::to_string_pretty(&meta).map_err(|e| format!("Meta gagal: {e}"))?;
        fs::write(dir.join("petdex-meta.json"), meta_text)
            .map_err(|e| format!("Tulis meta gagal: {e}"))?;
        Ok(())
    })();
    if let Err(e) = write_out {
        let _ = fs::remove_dir_all(&dir);
        return Err(e);
    }
    Ok(InstalledPet {
        slug: entry.slug.clone(),
        display_name: meta_display(&pj, entry),
        submitted_by: entry.submitted_by.clone(),
        sprite_version: pj.sprite_version,
        description: pj.description,
        dir,
    })
}

fn meta_display(pj: &PetJson, entry: &ManifestEntry) -> String {
    if !pj.display_name.is_empty() {
        pj.display_name.clone()
    } else if !entry.display_name.is_empty() {
        entry.display_name.clone()
    } else {
        entry.slug.clone()
    }
}

/// Hapus pet ter-install (hapus foldernya + dari daftar).
pub fn remove_pet(slug: &str) -> Result<(), String> {
    let dir = pets_dir().join(slug);
    if !dir.exists() {
        return Err(format!("Pet '{slug}' tidak ditemukan"));
    }
    fs::remove_dir_all(&dir).map_err(|e| format!("Hapus pet gagal: {e}"))
}

// ---------------------------------------------------------------------------
// Decode spritesheet
// ---------------------------------------------------------------------------

#[derive(Debug, Clone)]
pub struct DecodedSheet {
    pub width: u32,
    pub height: u32,
    /// Frame dalam pixel asli (192×208 untuk sheet standar).
    pub frame_w: u32,
    pub frame_h: u32,
    /// Jumlah baris state di atlas (9 untuk v1, 11 untuk v2).
    pub rows: u32,
    pub rgba: Vec<u8>,
}

/// Decode sheet (WebP/PNG) + validasi geometri atlas.
pub fn decode_sheet(bytes: &[u8]) -> Result<DecodedSheet, String> {
    let img = image::load_from_memory(bytes).map_err(|e| format!("Spritesheet tidak bisa di-decode: {e}"))?;
    let rgba = img.to_rgba8();
    let (width, height) = (rgba.width(), rgba.height());
    if width % ATLAS_COLS != 0 || width == 0 || height == 0 {
        return Err(format!("Lebar sheet {width} bukan kelipatan {ATLAS_COLS}"));
    }
    let frame_w = width / ATLAS_COLS;
    if height % FRAME_H != 0 {
        return Err(format!("Tinggi sheet {height} bukan kelipatan {FRAME_H}"));
    }
    let rows = height / FRAME_H;
    if rows < 9 {
        return Err(format!("Sheet hanya {rows} baris, butuh minimal 9"));
    }
    Ok(DecodedSheet {
        width,
        height,
        frame_w,
        frame_h: FRAME_H,
        rows,
        rgba: rgba.into_raw(),
    })
}

/// Muat + decode sheet pet ter-install (nama file dari pet.json,
/// fallback: cari *.webp / *.png di folder).
pub fn load_sheet(slug: &str) -> Result<DecodedSheet, String> {
    let dir = pets_dir().join(slug);
    let pj = read_pet_json(&dir).ok_or_else(|| format!("pet.json '{slug}' tidak ditemukan"))?;
    let mut candidates: Vec<PathBuf> = Vec::new();
    if !pj.spritesheet_path.is_empty() {
        candidates.push(dir.join(&pj.spritesheet_path));
        if let Some(name) = Path::new(&pj.spritesheet_path).file_name() {
            candidates.push(dir.join(name));
        }
    }
    candidates.push(dir.join("spritesheet.webp"));
    candidates.push(dir.join("spritesheet.png"));
    if let Ok(entries) = fs::read_dir(&dir) {
        for e in entries.flatten() {
            let p = e.path();
            if let Some(ext) = p.extension().and_then(|s| s.to_str()) {
                if ext.eq_ignore_ascii_case("webp") || ext.eq_ignore_ascii_case("png") {
                    candidates.push(p);
                }
            }
        }
    }
    for path in candidates {
        if let Ok(bytes) = fs::read(&path) {
            if let Ok(sheet) = decode_sheet(&bytes) {
                return Ok(sheet);
            }
        }
    }
    Err(format!("Spritesheet '{slug}' tidak ditemukan / rusak"))
}

// ---------------------------------------------------------------------------
// Animasi — port petdex-core/src/animation.rs (MIT)
// ---------------------------------------------------------------------------

/// 9 state animasi PetDex, urutan baris atlas 0..8.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PetState {
    Idle = 0,
    RunningRight = 1,
    RunningLeft = 2,
    Waving = 3,
    Jumping = 4,
    Failed = 5,
    Waiting = 6,
    Running = 7,
    Review = 8,
}

impl PetState {
    pub const COUNT: usize = 9;

    pub const ALL: [PetState; Self::COUNT] = [
        PetState::Idle,
        PetState::RunningRight,
        PetState::RunningLeft,
        PetState::Waving,
        PetState::Jumping,
        PetState::Failed,
        PetState::Waiting,
        PetState::Running,
        PetState::Review,
    ];

    pub fn row(self) -> u32 {
        self as u32
    }

    pub fn slug(self) -> &'static str {
        match self {
            PetState::Idle => "idle",
            PetState::RunningRight => "running-right",
            PetState::RunningLeft => "running-left",
            PetState::Waving => "waving",
            PetState::Jumping => "jumping",
            PetState::Failed => "failed",
            PetState::Waiting => "waiting",
            PetState::Running => "running",
            PetState::Review => "review",
        }
    }

    /// Daftar frame state: (kolom, durasi_ms). Frame terakhir tiap
    /// state lebih lama (jeda napas sebelum loop) — sama persis dengan
    /// petdex-core.
    pub fn frames(self) -> &'static [(u32, u32)] {
        match self {
            PetState::Idle => &[(0, 280), (1, 110), (2, 110), (3, 140), (4, 140), (5, 320)],
            PetState::RunningRight => &[
                (0, 120), (1, 120), (2, 120), (3, 120), (4, 120), (5, 120), (6, 120), (7, 220),
            ],
            PetState::RunningLeft => &[
                (0, 120), (1, 120), (2, 120), (3, 120), (4, 120), (5, 120), (6, 120), (7, 220),
            ],
            PetState::Waving => &[(0, 140), (1, 140), (2, 140), (3, 280)],
            PetState::Jumping => &[(0, 140), (1, 140), (2, 140), (3, 140), (4, 280)],
            PetState::Failed => &[
                (0, 140), (1, 140), (2, 140), (3, 140), (4, 140), (5, 140), (6, 140), (7, 240),
            ],
            PetState::Waiting => &[(0, 150), (1, 150), (2, 150), (3, 150), (4, 150), (5, 260)],
            PetState::Running => &[(0, 120), (1, 120), (2, 120), (3, 120), (4, 120), (5, 220)],
            PetState::Review => &[(0, 150), (1, 150), (2, 150), (3, 150), (4, 150), (5, 280)],
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct FrameOutput {
    pub state: PetState,
    pub row: u32,
    pub col: u32,
    pub frame_index: u32,
    pub frame_dur_ms: u32,
}

/// Mesin animasi: set_state reset ke frame 0; tick(dt) maju mengikuti
/// durasi tiap frame (bukan frame-rate tetap) — port petdex-core.
#[derive(Debug, Clone)]
pub struct PetAnimationEngine {
    state: PetState,
    frame_index: usize,
    elapsed_ms: u32,
}

impl Default for PetAnimationEngine {
    fn default() -> Self {
        Self::new()
    }
}

impl PetAnimationEngine {
    pub fn new() -> Self {
        Self {
            state: PetState::Idle,
            frame_index: 0,
            elapsed_ms: 0,
        }
    }

    pub fn state(&self) -> PetState {
        self.state
    }

    pub fn set_state(&mut self, state: PetState) {
        if self.state != state {
            self.state = state;
            self.frame_index = 0;
            self.elapsed_ms = 0;
        }
    }

    pub fn current_frame(&self) -> FrameOutput {
        let frames = self.state.frames();
        let (col, dur) = frames[self.frame_index];
        FrameOutput {
            state: self.state,
            row: self.state.row(),
            col,
            frame_index: self.frame_index as u32,
            frame_dur_ms: dur,
        }
    }

    pub fn tick(&mut self, dt_ms: u32) -> FrameOutput {
        let frames = self.state.frames();
        let mut remaining = self.elapsed_ms.saturating_add(dt_ms);
        loop {
            let dur = frames[self.frame_index].1;
            if dur == 0 || remaining < dur {
                self.elapsed_ms = remaining;
                break;
            }
            remaining -= dur;
            self.frame_index = (self.frame_index + 1) % frames.len();
        }
        self.current_frame()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn all_nine_states_match_petdex_core_table() {
        // (state, row, frame_count, first_dur, last_dur) — sama dengan
        // test petdex-core `all_nine_states_match_sprite_zig_table`.
        let expected: [(PetState, u32, usize, u32, u32); 9] = [
            (PetState::Idle, 0, 6, 280, 320),
            (PetState::RunningRight, 1, 8, 120, 220),
            (PetState::RunningLeft, 2, 8, 120, 220),
            (PetState::Waving, 3, 4, 140, 280),
            (PetState::Jumping, 4, 5, 140, 280),
            (PetState::Failed, 5, 8, 140, 240),
            (PetState::Waiting, 6, 6, 150, 260),
            (PetState::Running, 7, 6, 120, 220),
            (PetState::Review, 8, 6, 150, 280),
        ];
        assert_eq!(PetState::ALL.len(), 9);
        for (state, row, count, first_dur, last_dur) in expected {
            assert_eq!(state.row(), row);
            let frames = state.frames();
            assert_eq!(frames.len(), count);
            assert_eq!(frames[0].0, 0);
            assert_eq!(frames[0].1, first_dur);
            assert_eq!(frames[count - 1].0, (count - 1) as u32);
            assert_eq!(frames[count - 1].1, last_dur);
        }
    }

    #[test]
    fn idle_blink_timing_is_exact() {
        // Sama dengan test petdex-core `idle_blink_timing_is_exact`.
        let mut engine = PetAnimationEngine::new();
        assert_eq!(engine.current_frame().row, 0);
        assert_eq!(engine.current_frame().col, 0);
        let f = engine.tick(279);
        assert_eq!(f.col, 0);
        let f = engine.tick(1);
        assert_eq!(f.col, 1);
        assert_eq!(f.frame_dur_ms, 110);
        let f = engine.tick(110);
        assert_eq!(f.col, 2);
        let f = engine.tick(110);
        assert_eq!(f.col, 3);
        let f = engine.tick(140);
        assert_eq!(f.col, 4);
        let f = engine.tick(140);
        assert_eq!(f.col, 5);
        assert_eq!(f.frame_dur_ms, 320);
        let f = engine.tick(320);
        assert_eq!(f.col, 0);
        assert_eq!(f.frame_index, 0);
    }

    #[test]
    fn set_state_resets_to_first_frame() {
        let mut engine = PetAnimationEngine::new();
        engine.tick(500);
        assert_ne!(engine.current_frame().col, 0);
        engine.set_state(PetState::Waving);
        assert_eq!(engine.current_frame().row, 3);
        assert_eq!(engine.current_frame().col, 0);
        // Set state yang sama tidak me-reset.
        engine.tick(200);
        let col = engine.current_frame().col;
        engine.set_state(PetState::Waving);
        assert_eq!(engine.current_frame().col, col);
    }

    #[test]
    fn find_in_manifest_matches_slug_then_name() {
        let pets = vec![
            ManifestEntry {
                slug: "boba".into(),
                display_name: "Boba".into(),
                kind: "creature".into(),
                submitted_by: "railly".into(),
                spritesheet_url: "https://x/s.webp".into(),
                pet_json_url: "https://x/p.json".into(),
                zip_url: "".into(),
                sprite_version: 2,
            },
            ManifestEntry {
                slug: "duck".into(),
                display_name: "White Duck".into(),
                kind: "character".into(),
                submitted_by: "a".into(),
                spritesheet_url: "".into(),
                pet_json_url: "".into(),
                zip_url: "".into(),
                sprite_version: 1,
            },
        ];
        assert_eq!(find_in_manifest(&pets, "BOBA").unwrap().slug, "boba");
        assert_eq!(find_in_manifest(&pets, "duck").unwrap().slug, "duck");
        assert_eq!(
            find_in_manifest(&pets, "white").unwrap().slug,
            "duck"
        );
        assert!(find_in_manifest(&pets, "tidak-ada").is_none());
        assert!(find_in_manifest(&pets, "  ").is_none());
    }

    #[test]
    fn pet_page_url_is_stable() {
        assert_eq!(pet_page_url("boba"), "https://petdex.dev/pets/boba");
    }

    #[test]
    fn decode_sheet_roundtrip_png() {
        // Encode PNG 8×1872 (8 kolom × 9 baris frame 1×208 — geometri
        // minimal yang valid) lalu decode — geometri harus kembali.
        let mut img = image::RgbaImage::new(8, 1872);
        for px in img.pixels_mut() {
            *px = image::Rgba([255, 0, 0, 255]);
        }
        let mut bytes: Vec<u8> = Vec::new();
        use image::ImageEncoder;
        image::codecs::png::PngEncoder::new(&mut bytes)
            .write_image(&img, 8, 1872, image::ExtendedColorType::Rgba8)
            .unwrap();
        let sheet = decode_sheet(&bytes).unwrap();
        assert_eq!(sheet.width, 8);
        assert_eq!(sheet.height, 1872);
        assert_eq!(sheet.frame_w, 1);
        assert_eq!(sheet.frame_h, 208);
        assert_eq!(sheet.rows, 9);
    }

    #[test]
    fn decode_sheet_rejects_bad_geometry() {
        let img = image::RgbaImage::new(100, 100);
        let mut bytes: Vec<u8> = Vec::new();
        use image::ImageEncoder;
        image::codecs::png::PngEncoder::new(&mut bytes)
            .write_image(&img, 100, 100, image::ExtendedColorType::Rgba8)
            .unwrap();
        assert!(decode_sheet(&bytes).is_err());
    }

    #[test]
    fn manifest_parses_null_submitted_by() {
        // Regresi: galeri mengirim `submittedBy: null` di sebagian
        // entri — satu null sempat menggagalkan parse seluruh
        // manifest (serde `default` tidak menerima null eksplisit).
        let doc: ManifestDoc = serde_json::from_str(
            r#"{"generatedAt":"2026-11-27T01:20:42.031Z","total":2,"pets":[
                {"slug":"boba","displayName":"Boba","kind":"creature","submittedBy":"railly","spritesheetUrl":"https://x/s.webp","petJsonUrl":"https://x/p.json","zipUrl":"","spriteVersionNumber":1},
                {"slug":"noname","displayName":"No Name","kind":"creature","submittedBy":null,"spritesheetUrl":"https://x/s2.webp","petJsonUrl":"https://x/p2.json","zipUrl":"","spriteVersionNumber":null}
            ]}"#,
        )
        .expect("manifest dengan null harus ter-parse");
        assert_eq!(doc.pets.len(), 2);
        assert_eq!(doc.pets[1].submitted_by, "");
        assert_eq!(doc.pets[1].sprite_version, 0);
    }

    // Cek live (diabaikan di suite biasa — butuh jaringan):
    // unduh Boba sungguhan dari galeri PetDex dan install ke
    // folder pets. Jalankan manual:
    //   APPDATA=/tmp/petdex-live cargo test --offline live_install_boba -- --ignored
    #[test]
    #[ignore]
    fn live_install_boba() {
        let manifest = fetch_manifest().expect("manifest fetch");
        let entry = find_in_manifest(&manifest, "boba").expect("boba ada di manifest");
        let pet = install_pet(entry).expect("install boba");
        assert_eq!(pet.slug, "boba");
    }
}
