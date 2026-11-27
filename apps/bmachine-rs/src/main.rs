slint::include_modules!();

mod db;
mod doc_manager;
mod executor;
mod local_update;
mod platform;
mod process_log;
mod sheets_api;
mod theme;
mod trello_api;

use db::DatabaseService;
#[allow(unused_imports)]
use executor::ScriptExecutor;
#[allow(unused_imports)]
use sheets_api::GoogleSheetsService;

use doc_manager::DocManager;
use local_update::LocalUpdateService;
use process_log::ProcessLogService;
use std::sync::{Arc, Mutex};
use std::sync::atomic::{AtomicU8, Ordering};
use std::cell::Cell;
use std::io::Write;
use rdev::{Button, Event, EventType, Key};

// TABEL GLOBAL EXPLORER — digenerate tools/gen_explorer_data.py.
// (folder-key, path, name, kind, ext, base, size, date, preview, loc)
// Dipakai search (rekursif, substring case-insensitive) & type-ahead.
// TABEL GLOBAL EXPLORER — digenerate tools/gen_explorer_data.py.
// (folder-key, path, name, kind, ext, base, size, date, preview, loc)
// Dipakai search (rekursif, substring case-insensitive) & type-ahead.
// Radial menu global trigger config. The C# `ShortcutConfig` JSON shape is
// mirrored here so the Rust port can read the same DB key without changing
// the C# app's storage format.
#[derive(serde::Deserialize)]
#[allow(non_snake_case)]
struct RadialShortcutConfig {
    #[serde(default = "default_trigger_type")]
    Type: u8,
    #[serde(default = "default_mouse_button")]
    MouseButton: u8,
    #[serde(default = "default_trigger_key")]
    Key: u32,
    #[serde(default = "default_trigger_modifiers")]
    Modifiers: u8,
}

fn default_trigger_type() -> u8 { 1 }
fn default_mouse_button() -> u8 { 2 }
fn default_trigger_key() -> u32 { 222 }
fn default_trigger_modifiers() -> u8 { 12 }

fn vk_to_rdev_key(vk: u32) -> Option<Key> {
    match vk {
        1 => Some(Key::Backspace),
        2 => Some(Key::Escape),
        3 => Some(Key::Tab),
        4 => Some(Key::CapsLock),
        5 => Some(Key::F6),
        6 => Some(Key::F5),
        7 => Some(Key::F4),
        8 => Some(Key::F3),
        9 => Some(Key::F2),
        10 => Some(Key::F1),
        11 => Some(Key::Delete),
        12 => Some(Key::Home),
        13 => Some(Key::Return),
        14 => Some(Key::ShiftLeft),
        15 => Some(Key::ShiftLeft),
        16 => Some(Key::ControlLeft),
        17 => Some(Key::ControlRight),
        18 => Some(Key::Alt),
        19 => Some(Key::Alt),
        20 => Some(Key::Alt),
        21 => Some(Key::MetaLeft),
        22 => Some(Key::MetaRight),
        27 => Some(Key::Escape),
        28 => Some(Key::End),
        29 => Some(Key::PageDown),
        30 => Some(Key::PageUp),
        31 => Some(Key::Insert),
        32 => Some(Key::Space),
        33 => Some(Key::LeftArrow),
        34 => Some(Key::UpArrow),
        35 => Some(Key::RightArrow),
        36 => Some(Key::DownArrow),
        37 => Some(Key::PrintScreen),
        38 => Some(Key::ScrollLock),
        39 => Some(Key::Pause),
        91 => Some(Key::MetaLeft),
        92 => Some(Key::MetaRight),
        106 => Some(Key::KpMultiply),
        107 => Some(Key::KpPlus),
        109 => Some(Key::KpMinus),
        110 => Some(Key::Kp0),
        111 => Some(Key::KpDivide),
        112 => Some(Key::F1),
        113 => Some(Key::F2),
        114 => Some(Key::F3),
        115 => Some(Key::F4),
        116 => Some(Key::F5),
        117 => Some(Key::F6),
        118 => Some(Key::F7),
        119 => Some(Key::F8),
        120 => Some(Key::F9),
        121 => Some(Key::F10),
        122 => Some(Key::F11),
        123 => Some(Key::F12),
        186 => Some(Key::SemiColon),
        187 => Some(Key::Equal),
        188 => Some(Key::LeftBracket),
        189 => Some(Key::Minus),
        190 => Some(Key::Dot),
        191 => Some(Key::Comma),
        192 => Some(Key::BackQuote),
        // SharpHook KeyCode::VcOem6 / C# legacy radial default.
        219 => Some(Key::LeftBracket),
        221 => Some(Key::RightBracket),
        222 => Some(Key::Quote),
        _ => None,
    }
}

fn mouse_button_index_to_rdev(index: u8) -> Option<Button> {
    match index {
        0 => Some(Button::Left),
        1 => Some(Button::Right),
        2 => Some(Button::Middle),
        _ => None,
    }
}

static EXPLORER_ITEMS: &[(&str, &str, &str, &str, &str, &str, &str, &str, &str, &str)] = &[
    ("root", "D:\\#GAWENA", "08 AGUSTUS 2026", "folder", "", "08 AGUSTUS 2026", "", "12 Agu 2026", "", "#GAWENA"),
    ("root", "D:\\#GAWENA", "09 SEPTEMBER 2026", "folder", "", "09 SEPTEMBER 2026", "", "9 Sep 2026", "", "#GAWENA"),
    ("root", "D:\\#GAWENA", "10 OKTOBER 2026", "folder", "", "10 OKTOBER 2026", "", "10 Okt 2026", "", "#GAWENA"),
    ("root", "D:\\#GAWENA", "27_SDN RANCAIYUH I", "folder", "", "27_SDN RANCAIYUH I", "", "26 Nov 2026", "", "#GAWENA"),
    ("root", "D:\\#GAWENA", "28_SDN BANTAR KAMBING 03", "folder", "", "28_SDN BANTAR KAMBING 03", "", "25 Nov 2026", "", "#GAWENA"),
    ("root", "D:\\#GAWENA", "PAS FOTO.lnk", "link", ".lnk", "PAS FOTO", "1 KB", "26 Nov 2026", "", "#GAWENA"),
    ("root", "D:\\#GAWENA", "catatan.txt", "file", ".txt", "catatan", "1 KB", "24 Nov 2026", "Catatan QC — cek logo sekolah sebelum kirim.\nKODE= MSK-001\nJangan lupa: centering kepala di guide.", "#GAWENA"),
    ("root", "D:\\#GAWENA", "Panduan.docx", "file", ".docx", "Panduan", "15 KB", "20 Nov 2026", "", "#GAWENA"),
    ("root", "D:\\#GAWENA", "README.md", "file", ".md", "README", "2 KB", "3 Nov 2026", "# BMachine\nFolder output lokal (D:\\#GAWENA).\nHasil olahan master tersimpan di sini.", "#GAWENA"),
    ("a", "D:\\#GAWENA\\27_SDN RANCAIYUH I", "1. PAS FOTO", "folder", "", "1. PAS FOTO", "", "26 Nov 2026", "", "27_SDN RANCAIYUH I"),
    ("a", "D:\\#GAWENA\\27_SDN RANCAIYUH I", "2. FOTO10RP_FREE SEKOLAH_GRAIS", "folder", "", "2. FOTO10RP_FREE SEKOLAH_GRAIS", "", "25 Nov 2026", "", "27_SDN RANCAIYUH I"),
    ("a", "D:\\#GAWENA\\27_SDN RANCAIYUH I", "3. SOUVENIR_FREE ID CARD", "folder", "", "3. SOUVENIR_FREE ID CARD", "", "24 Nov 2026", "", "27_SDN RANCAIYUH I"),
    ("a", "D:\\#GAWENA\\27_SDN RANCAIYUH I", "4. SOUVENIR_FREE KARTU PELAJA", "folder", "", "4. SOUVENIR_FREE KARTU PELAJA", "", "23 Nov 2026", "", "27_SDN RANCAIYUH I"),
    ("a", "D:\\#GAWENA\\27_SDN RANCAIYUH I", "KODE= PFM-005.txt", "file", ".txt", "KODE= PFM-005", "1 KB", "26 Nov 2026", "KODE= PFM-005\nJenis: PAS FOTO\nSekolah: SDN RANCAIYUH I\nTemplate: PFM-005.psd", "27_SDN RANCAIYUH I"),
    ("b", "D:\\#GAWENA\\28_SDN BANTAR KAMBING 03", "1. PAS FOTO", "folder", "", "1. PAS FOTO", "", "25 Nov 2026", "", "28_SDN BANTAR KAMBING 03"),
    ("b", "D:\\#GAWENA\\28_SDN BANTAR KAMBING 03", "2. WISUDA", "folder", "", "2. WISUDA", "", "24 Nov 2026", "", "28_SDN BANTAR KAMBING 03"),
    ("b", "D:\\#GAWENA\\28_SDN BANTAR KAMBING 03", "3. ID CARD", "folder", "", "3. ID CARD", "", "23 Nov 2026", "", "28_SDN BANTAR KAMBING 03"),
    ("b", "D:\\#GAWENA\\28_SDN BANTAR KAMBING 03", "4. KARTU PELAJAR", "folder", "", "4. KARTU PELAJAR", "", "22 Nov 2026", "", "28_SDN BANTAR KAMBING 03"),
    ("b", "D:\\#GAWENA\\28_SDN BANTAR KAMBING 03", "KODE= MSK-001.txt", "file", ".txt", "KODE= MSK-001", "1 KB", "25 Nov 2026", "KODE= MSK-001\nJenis: MANASIK\nSekolah: SDN BANTAR KAMBING 03\nTemplate: MSK-001.psd", "28_SDN BANTAR KAMBING 03"),
    ("pf", "D:\\#GAWENA\\27_SDN RANCAIYUH I\\1. PAS FOTO", "KELAS 1 B", "folder", "", "KELAS 1 B", "", "26 Nov 2026", "", "1. PAS FOTO"),
    ("pf", "D:\\#GAWENA\\27_SDN RANCAIYUH I\\1. PAS FOTO", "Ahmad.jpg", "file", ".jpg", "Ahmad", "2,4 MB", "26 Nov 2026", "", "1. PAS FOTO"),
    ("pf", "D:\\#GAWENA\\27_SDN RANCAIYUH I\\1. PAS FOTO", "Budi.jpg", "file", ".jpg", "Budi", "2,1 MB", "25 Nov 2026", "", "1. PAS FOTO"),
    ("pf", "D:\\#GAWENA\\27_SDN RANCAIYUH I\\1. PAS FOTO", "template.psd", "file", ".psd", "template", "48,2 MB", "24 Nov 2026", "", "1. PAS FOTO"),
    ("pfb", "D:\\#GAWENA\\28_SDN BANTAR KAMBING 03\\1. PAS FOTO", "KELAS 1 B", "folder", "", "KELAS 1 B", "", "25 Nov 2026", "", "1. PAS FOTO"),
    ("pfb", "D:\\#GAWENA\\28_SDN BANTAR KAMBING 03\\1. PAS FOTO", "Dewi.jpg", "file", ".jpg", "Dewi", "2,3 MB", "25 Nov 2026", "", "1. PAS FOTO"),
    ("pfb", "D:\\#GAWENA\\28_SDN BANTAR KAMBING 03\\1. PAS FOTO", "Eko.jpg", "file", ".jpg", "Eko", "2,0 MB", "24 Nov 2026", "", "1. PAS FOTO"),
    ("pfb", "D:\\#GAWENA\\28_SDN BANTAR KAMBING 03\\1. PAS FOTO", "template.psd", "file", ".psd", "template", "48,2 MB", "24 Nov 2026", "", "1. PAS FOTO"),
    ("gen", "D:\\#GAWENA\\08 AGUSTUS 2026", "foto_01.jpg", "file", ".jpg", "foto_01", "1,8 MB", "24 Nov 2026", "", "08 AGUSTUS 2026"),
    ("gen", "D:\\#GAWENA\\08 AGUSTUS 2026", "foto_02.jpg", "file", ".jpg", "foto_02", "1,9 MB", "23 Nov 2026", "", "08 AGUSTUS 2026"),
    ("gen", "D:\\#GAWENA\\08 AGUSTUS 2026", "template.psd", "file", ".psd", "template", "48,2 MB", "22 Nov 2026", "", "08 AGUSTUS 2026"),
    ("master", "D:\\#DATA ABENG\\MASTER", "MSK-001.psd", "file", ".psd", "MSK-001", "49,7 MB", "25 Nov 2026", "", "MASTER"),
    ("master", "D:\\#DATA ABENG\\MASTER", "PFM-005.psd", "file", ".psd", "PFM-005", "52,1 MB", "26 Nov 2026", "", "MASTER"),
    ("master", "D:\\#DATA ABENG\\MASTER", "WSD-003.psd", "file", ".psd", "WSD-003", "51,3 MB", "24 Nov 2026", "", "MASTER"),
];

// Subtree tiap folder (search rekursif = folder ini + turunan).

// Subtree tiap folder (search rekursif = folder ini + turunan).
static EXPLORER_SUBTREE: &[(&str, &[&str])] = &[
    ("root", &["root", "a", "b", "pf", "pfb", "gen"]),
    ("a", &["a", "pf", "gen"]),
    ("b", &["b", "pfb", "gen"]),
    ("pf", &["pf", "gen"]),
    ("pfb", &["pfb", "gen"]),
    ("gen", &["gen"]),
    ("master", &["master"]),
];


fn subtree_keys(key: &str) -> Vec<&str> {
    for (k, sub) in EXPLORER_SUBTREE {
        if *k == key {
            return sub.to_vec();
        }
    }
    vec![key]
}

use slint::Model;

// ---- Kartu manual Trello (C# ManualCards.Editing) ----
// Parse satu link Trello -> (id, judul, url). Judul diambil dari
// slug di link (nama asli dari API Trello di Fase 6); kalau tidak
// ada slug, judul = ID. Link kosong/salah -> None.
fn parse_trello_link(link: &str) -> Option<(String, String, String)> {
    let link = link.trim();
    if link.is_empty() {
        return None;
    }
    if let Some(pos) = link.find("/c/") {
        let rest = &link[pos + 3..];
        let mut parts = rest.split(|c| c == '/' || c == '?' || c == '#');
        let id = parts.next().unwrap_or("").trim();
        if id.is_empty() {
            return None;
        }
        let slug = parts.next().unwrap_or("").trim();
        let title = if slug.is_empty() {
            id.to_string()
        } else {
            slug.replace('-', " ")
        };
        return Some((id.to_string(), title, link.to_string()));
    }
    if link.len() >= 8 && link.chars().all(|c| c.is_ascii_alphanumeric()) {
        return Some((
            link.to_string(),
            link.to_string(),
            format!("https://trello.com/c/{}", link),
        ));
    }
    None
}

fn read_str_model(m: slint::ModelRc<slint::SharedString>) -> Vec<slint::SharedString> {
    (0..m.row_count())
        .map(|i| m.row_data(i).unwrap_or_default())
        .collect()
}

fn read_bool_model(m: slint::ModelRc<bool>) -> Vec<bool> {
    (0..m.row_count())
        .map(|i| m.row_data(i).unwrap_or(false))
        .collect()
}

fn main() -> Result<(), slint::PlatformError> {
    let ui = AppWindow::new()?;

    // QA/report helper: membuka state tertentu lewat argumen startup, tanpa
    // menyuntik klik mouse/keyboard ke user. Dipakai runner screenshot VM;
    // tidak ada efek bila aplikasi dijalankan normal (tanpa argumen).
    let mut no_splash = false;
    let mut radial_test = false;
    let mut simulate_hotkey = false;
    for arg in std::env::args().skip(1) {
        if let Some(v) = arg.strip_prefix("--view=") {
            ui.set_overlay_view(v.into());
        } else if let Some(v) = arg.strip_prefix("--group=") {
            ui.set_settings_group(v.into());
        } else if let Some(v) = arg.strip_prefix("--tab=") {
            ui.set_current_tab(v.into());
        } else if arg == "--log" {
            ui.set_show_logpanel(true);
        } else if arg == "--flyout" {
            ui.set_show_flyout(true);
        } else if arg == "--no-splash" {
            no_splash = true;
        } else if arg == "--radial-test" {
            radial_test = true;
        } else if arg == "--simulate-hotkey" {
            simulate_hotkey = true;
        } else if let Some(v) = arg.strip_prefix("--dialog=") {
            ui.set_dialog_open(v.into());
        }
    }

    // QA helper: open the radial overlay immediately, without needing the
    // global shortcut (used to screenshot the radial state in a VM).
    if radial_test {
        ui.set_radial_open(true);
    }

    if simulate_hotkey {
        let sim = slint::Timer::default();
        sim.start(slint::TimerMode::SingleShot, std::time::Duration::from_secs(2), move || {
            let _ = rdev::simulate(&rdev::EventType::KeyPress(Key::MetaLeft));
            let _ = rdev::simulate(&rdev::EventType::KeyPress(Key::ShiftLeft));
            let _ = rdev::simulate(&rdev::EventType::KeyPress(Key::Quote));
            let _ = rdev::simulate(&rdev::EventType::KeyRelease(Key::Quote));
            let _ = rdev::simulate(&rdev::EventType::KeyRelease(Key::ShiftLeft));
            let _ = rdev::simulate(&rdev::EventType::KeyRelease(Key::MetaLeft));
        });
    }

    // Inisialisasi Database SQLite BMachine.db nyata di AppData/BMachine
    let db = match DatabaseService::init() {
        Ok(d) => Some(Arc::new(Mutex::new(d))),
        Err(e) => {
            eprintln!("Warning: SQLite init error: {}", e);
            None
        }
    };

    let doc_mgr = Arc::new(Mutex::new(DocManager::default()));
    let proc_log = Arc::new(Mutex::new(ProcessLogService::default()));
    {
        // Benih log startup nyata (mencerminkan langkah bootstrapper C#).
        proc_log.lock().unwrap().seed_boot();
    }

    // Sinkronisasi baca setting awal dari SQLite jika ada
    let mut init_trello_key = String::new();
    let mut init_trello_tok = String::new();
    let mut init_edit_list = String::new();
    let mut init_rev_list = String::new();
    let mut init_late_list = String::new();

    if let Some(ref db_mutex) = db {
        if let Ok(db_guard) = db_mutex.lock() {
            if let Some(key) = db_guard.get("Trello.ApiKey") {
                init_trello_key = key.clone();
                ui.set_trello_api_key(slint::SharedString::from(key));
            }
            if let Some(token) = db_guard.get("Trello.Token") {
                init_trello_tok = token.clone();
                ui.set_trello_token(slint::SharedString::from(token));
            }
            if let Some(v) = db_guard.get("Trello.EditingBoardId") {
                ui.set_editing_board_id(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Trello.EditingListId") {
                init_edit_list = v.clone();
                ui.set_editing_list_id(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Trello.RevisionBoardId") {
                ui.set_revision_board_id(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Trello.RevisionListId") {
                init_rev_list = v.clone();
                ui.set_revision_list_id(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Trello.LateBoardId") {
                ui.set_late_board_id(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Trello.LateListId") {
                init_late_list = v.clone();
                ui.set_late_list_id(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Trello.QcBoardId") {
                ui.set_qc_board_id(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Trello.AccBoardId") {
                ui.set_acc_board_id(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Trello.AccListId") {
                ui.set_acc_list_id(slint::SharedString::from(v));
            }
            if let Some(sheet_id) = db_guard.get("Google.SheetId") {
                ui.set_google_sheet_id(slint::SharedString::from(sheet_id));
            }
            if let Some(v) = db_guard.get("Google.CredsPath") {
                ui.set_google_creds_path(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Google.SheetName") {
                ui.set_google_sheet_name(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Google.SheetColumn") {
                ui.set_google_sheet_col(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Google.SheetRow") {
                ui.set_google_sheet_row(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Spreadsheet.SheetName") {
                ui.set_spreadsheet_sheet_name(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Spreadsheet.Range") {
                ui.set_spreadsheet_range(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Leaderboard.Range") {
                ui.set_lb_harian_range(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Leaderboard.MonthlyRange") {
                ui.set_lb_bulanan_range(slint::SharedString::from(v));
            }
            if let Some(pts) = db_guard.get("Stat.Points") {
                ui.set_stat_points(slint::SharedString::from(pts));
            } else {
                ui.set_stat_points(slint::SharedString::from("0"));
            }
            if let Some(v) = db_guard.get("User.Name") {
                ui.set_app_user_name(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Paths.LocalOutput") {
                ui.set_app_path_local(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Paths.OkeBase") {
                ui.set_app_path_okebase(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Paths.Photoshop") {
                ui.set_app_path_photoshop(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Paths.PasFoto") {
                ui.set_app_path_pasfoto(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Paths.Manasik") {
                ui.set_app_path_manasik(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Paths.Wisuda") {
                ui.set_app_path_wisuda(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Paths.Profesi") {
                ui.set_app_path_profesi(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Trello.IsConnected") {
                let conn = v == "true" || v == "1";
                ui.set_app_trello_connected(conn);
                if conn {
                    ui.set_app_trello_status(slint::SharedString::from("Tersambung ke Trello."));
                }
            }
            theme::load_theme_from_db(&ui, &db_guard);
        }
    }

    // Default widget status awal persis C# (0 jika belum tersambung, bukan angka demo)
    ui.set_stat_editing(slint::SharedString::from("0"));
    ui.set_stat_revision(slint::SharedString::from("0"));
    ui.set_stat_late(slint::SharedString::from("0"));

    // Baris sheet & leaderboard di-preload dari DB (Rust mengisi, bukan demo UI)
    {
        let mut sheet_rows: Vec<slint::SharedString> = vec![];
        let mut lb_rows: Vec<slint::SharedString> = vec![];
        if let Some(ref db_mutex) = db {
            if let Ok(db_guard) = db_mutex.lock() {
                for i in 1..=10 {
                    let key = format!("Sheet.Row.{}", i);
                    if let Some(v) = db_guard.get(&key) {
                        sheet_rows.push(slint::SharedString::from(v.clone()));
                    }
                }
                for i in 1..=10 {
                    let key = format!("Lb.Row.{}", i);
                    if let Some(v) = db_guard.get(&key) {
                        lb_rows.push(slint::SharedString::from(v.clone()));
                    }
                }
            }
        }
        let sheet_count = sheet_rows.len() as i32;
        let lb_count = lb_rows.len() as i32;
        let sc0: Vec<slint::SharedString> = sheet_rows.iter().map(|r| slint::SharedString::from(r.split('\t').next().unwrap_or("").to_string())).collect();
        let sc1: Vec<slint::SharedString> = sheet_rows.iter().map(|r| slint::SharedString::from(r.split('\t').nth(1).unwrap_or("").to_string())).collect();
        let sc2: Vec<slint::SharedString> = sheet_rows.iter().map(|r| slint::SharedString::from(r.split('\t').nth(2).unwrap_or("").to_string())).collect();
        let lc0: Vec<slint::SharedString> = lb_rows.iter().map(|r| slint::SharedString::from(r.split('\t').next().unwrap_or("").to_string())).collect();
        let lc1: Vec<slint::SharedString> = lb_rows.iter().map(|r| slint::SharedString::from(r.split('\t').nth(1).unwrap_or("").to_string())).collect();
        ui.set_sheet_rows(slint::ModelRc::new(slint::VecModel::from(sheet_rows)));
        ui.set_lb_rows(slint::ModelRc::new(slint::VecModel::from(lb_rows)));
        ui.set_sheet_row_count(sheet_count);
        ui.set_lb_row_count(lb_count);
        ui.set_sheet_col0(slint::ModelRc::new(slint::VecModel::from(sc0)));
        ui.set_sheet_col1(slint::ModelRc::new(slint::VecModel::from(sc1)));
        ui.set_sheet_col2(slint::ModelRc::new(slint::VecModel::from(sc2)));
        ui.set_lb_col0(slint::ModelRc::new(slint::VecModel::from(lc0)));
        ui.set_lb_col1(slint::ModelRc::new(slint::VecModel::from(lc1)));
    }

    // Sinkronisasi kartu Trello live (mengisi count widget + daftar kartu nyata)
    fn extract_card_display_id(name: &str) -> String {
        if let Some(idx) = name.find('_') {
            let prefix = &name[..idx];
            if !prefix.is_empty() && prefix.chars().all(|c| c.is_ascii_digit()) {
                return prefix.to_string();
            }
        }
        String::new()
    }

    fn spawn_trello_sync(
        ui_weak: slint::Weak<AppWindow>,
        k: String,
        t: String,
        el: String,
        rl: String,
        ll: String,
        show_toast: bool,
    ) {
        std::thread::spawn(move || {
            let mut synced = 0usize;
            if !k.is_empty() && !t.is_empty() {
                if !el.is_empty() {
                    if let Ok(cards) = trello_api::TrelloApiService::fetch_cards(&k, &t, &el) {
                        synced += cards.len();
                        let count = format!("{}", cards.len());
                        let titles: Vec<slint::SharedString> = cards.iter().map(|c| slint::SharedString::from(c.name.as_str())).collect();
                        let subs: Vec<slint::SharedString> = cards.iter().map(|c| {
                            let s = c.desc.as_deref().unwrap_or("").lines().next().unwrap_or("").trim();
                            slint::SharedString::from(if s.is_empty() { "Trello Card" } else { s })
                        }).collect();
                        let dues: Vec<slint::SharedString> = cards.iter().map(|c| {
                            let d = c.due.as_deref().unwrap_or("-");
                            slint::SharedString::from(d.get(..10).unwrap_or(d))
                        }).collect();
                        let ids: Vec<slint::SharedString> = cards.iter().map(|c| {
                            let did = extract_card_display_id(&c.name);
                            slint::SharedString::from(if did.is_empty() { c.id.clone() } else { did })
                        }).collect();
                        let dones: Vec<bool> = vec![false; cards.len()];
                        let _ = ui_weak.upgrade_in_event_loop(move |u| {
                            u.set_stat_editing(count.into());
                            u.set_editing_titles(slint::ModelRc::new(slint::VecModel::from(titles.clone())));
                            u.set_editing_subs(slint::ModelRc::new(slint::VecModel::from(subs.clone())));
                            u.set_editing_due(slint::ModelRc::new(slint::VecModel::from(dues.clone())));
                            u.set_editing_ids(slint::ModelRc::new(slint::VecModel::from(ids)));
                            u.set_editing_done(slint::ModelRc::new(slint::VecModel::from(dones)));
                            if u.get_list_category().as_str() == "EDITING" || u.get_trello_window_cat().as_str() == "EDITING" {
                                u.set_list_titles(slint::ModelRc::new(slint::VecModel::from(titles)));
                                u.set_list_subs(slint::ModelRc::new(slint::VecModel::from(subs)));
                                u.set_list_due(slint::ModelRc::new(slint::VecModel::from(dues)));
                            }
                        });
                    }
                }
                if !rl.is_empty() {
                    if let Ok(cards) = trello_api::TrelloApiService::fetch_cards(&k, &t, &rl) {
                        synced += cards.len();
                        let count = format!("{}", cards.len());
                        let titles: Vec<slint::SharedString> = cards.iter().map(|c| slint::SharedString::from(c.name.as_str())).collect();
                        let subs: Vec<slint::SharedString> = cards.iter().map(|c| {
                            let s = c.desc.as_deref().unwrap_or("").lines().next().unwrap_or("").trim();
                            slint::SharedString::from(if s.is_empty() { "Revisi" } else { s })
                        }).collect();
                        let dues: Vec<slint::SharedString> = cards.iter().map(|c| {
                            let d = c.due.as_deref().unwrap_or("-");
                            slint::SharedString::from(d.get(..10).unwrap_or(d))
                        }).collect();
                        let ids: Vec<slint::SharedString> = cards.iter().map(|c| slint::SharedString::from(extract_card_display_id(&c.name))).collect();
                        let dones: Vec<bool> = vec![false; cards.len()];
                        let _ = ui_weak.upgrade_in_event_loop(move |u| {
                            u.set_stat_revision(count.into());
                            u.set_revision_titles(slint::ModelRc::new(slint::VecModel::from(titles.clone())));
                            u.set_revision_subs(slint::ModelRc::new(slint::VecModel::from(subs.clone())));
                            u.set_revision_due(slint::ModelRc::new(slint::VecModel::from(dues.clone())));
                            u.set_revision_ids(slint::ModelRc::new(slint::VecModel::from(ids)));
                            u.set_revision_done(slint::ModelRc::new(slint::VecModel::from(dones)));
                            if u.get_list_category().as_str() == "REVISION" || u.get_trello_window_cat().as_str() == "REVISION" {
                                u.set_list_titles(slint::ModelRc::new(slint::VecModel::from(titles)));
                                u.set_list_subs(slint::ModelRc::new(slint::VecModel::from(subs)));
                                u.set_list_due(slint::ModelRc::new(slint::VecModel::from(dues)));
                            }
                        });
                    }
                }
                if !ll.is_empty() {
                    if let Ok(cards) = trello_api::TrelloApiService::fetch_cards(&k, &t, &ll) {
                        synced += cards.len();
                        let count = format!("{}", cards.len());
                        let titles: Vec<slint::SharedString> = cards.iter().map(|c| slint::SharedString::from(c.name.as_str())).collect();
                        let subs: Vec<slint::SharedString> = cards.iter().map(|c| {
                            let s = c.desc.as_deref().unwrap_or("").lines().next().unwrap_or("").trim();
                            slint::SharedString::from(if s.is_empty() { "Susulan" } else { s })
                        }).collect();
                        let dues: Vec<slint::SharedString> = cards.iter().map(|c| {
                            let d = c.due.as_deref().unwrap_or("-");
                            slint::SharedString::from(d.get(..10).unwrap_or(d))
                        }).collect();
                        let ids: Vec<slint::SharedString> = cards.iter().map(|c| slint::SharedString::from(extract_card_display_id(&c.name))).collect();
                        let dones: Vec<bool> = vec![false; cards.len()];
                        let _ = ui_weak.upgrade_in_event_loop(move |u| {
                            u.set_stat_late(count.into());
                            u.set_late_titles(slint::ModelRc::new(slint::VecModel::from(titles.clone())));
                            u.set_late_subs(slint::ModelRc::new(slint::VecModel::from(subs.clone())));
                            u.set_late_due(slint::ModelRc::new(slint::VecModel::from(dues.clone())));
                            u.set_late_ids(slint::ModelRc::new(slint::VecModel::from(ids)));
                            u.set_late_done(slint::ModelRc::new(slint::VecModel::from(dones)));
                            if u.get_list_category().as_str() == "LATE" || u.get_trello_window_cat().as_str() == "LATE" {
                                u.set_list_titles(slint::ModelRc::new(slint::VecModel::from(titles)));
                                u.set_list_subs(slint::ModelRc::new(slint::VecModel::from(subs)));
                                u.set_list_due(slint::ModelRc::new(slint::VecModel::from(dues)));
                            }
                        });
                    }
                }
            }
            let _ = ui_weak.upgrade_in_event_loop(move |u| {
                u.set_trello_refreshing(false);
                if show_toast {
                    if synced > 0 {
                        u.set_toast(slint::SharedString::from(format!("Trello di-refresh — {} card dimuat", synced)));
                    } else if k.is_empty() || t.is_empty() {
                        u.set_toast(slint::SharedString::from("Isi API Key & Token Trello di Settings > Account"));
                    } else {
                        u.set_toast(slint::SharedString::from("Trello di-refresh"));
                    }
                }
            });
        });
    }

    // Tarik data live bila kredensial & list sudah ada di database
    if !init_trello_key.is_empty() && !init_trello_tok.is_empty() {
        spawn_trello_sync(
            ui.as_weak(),
            init_trello_key.clone(),
            init_trello_tok.clone(),
            init_edit_list.clone(),
            init_rev_list.clone(),
            init_late_list.clone(),
            false,
        );
    }

    // ---- Appearance, Window Controls, Trello & Path Callbacks ----
    {
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        ui.on_apply_appearance(move || {
            if let Some(ui) = ui_weak.upgrade() {
                match theme::apply_appearance(&ui) {
                    Ok(()) => {
                        if let Some(ref db_mutex) = db_clone {
                            if let Ok(mut db_guard) = db_mutex.lock() {
                                theme::save_theme_to_db(&ui, &mut db_guard);
                            }
                        }
                    }
                    Err(msg) => {
                        ui.set_toast(slint::SharedString::from(msg));
                    }
                }
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        ui.on_reset_appearance(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.set_app_color_mode(slint::SharedString::from("dark"));
                ui.set_app_theme_preset(slint::SharedString::from("BMachine Dark"));
                ui.set_app_accent_hex(slint::SharedString::from("#3B82F6"));
                ui.set_app_accent_hex_good(slint::SharedString::from("#3B82F6"));
                ui.set_app_bg_hex(slint::SharedString::from("#121212"));
                ui.set_app_bg_hex_good(slint::SharedString::from("#121212"));
                ui.set_app_fg_hex(slint::SharedString::from("#FFFFFF"));
                ui.set_app_fg_hex_good(slint::SharedString::from("#FFFFFF"));
                ui.set_app_font_choice(slint::SharedString::from("Inter"));
                ui.set_widget_bg(slint::SharedString::from("doodle"));
                ui.set_app_corner_style(slint::SharedString::from("default"));
                ui.set_app_nav_style(slint::SharedString::from("capsule"));
                ui.set_app_chrome(slint::SharedString::from("custom"));
                let _ = theme::apply_appearance(&ui);
                if let Some(ref db_mutex) = db_clone {
                    if let Ok(mut db_guard) = db_mutex.lock() {
                        theme::save_theme_to_db(&ui, &mut db_guard);
                    }
                }
                ui.set_toast(slint::SharedString::from("Tema dikembalikan ke BMachine Dark default"));
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        ui.on_save_general_settings(move || {
            if let Some(ui) = ui_weak.upgrade() {
                if let Some(ref db_mutex) = db_clone {
                    if let Ok(mut db_guard) = db_mutex.lock() {
                        let _ = db_guard.set("User.Name", ui.get_app_user_name().as_str(), "String");
                        let _ = db_guard.set("Settings.StartupAnim", if ui.get_app_anim_startup() { "true" } else { "false" }, "Bool");
                        let _ = db_guard.set("Settings.StatSpeed", ui.get_app_anim_speed().as_str(), "String");
                    }
                }
                ui.set_toast(slint::SharedString::from("Pengaturan General disimpan"));
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        ui.on_save_paths_settings(move || {
            if let Some(ui) = ui_weak.upgrade() {
                if let Some(ref db_mutex) = db_clone {
                    if let Ok(mut db_guard) = db_mutex.lock() {
                        let _ = db_guard.set("Paths.LocalOutput", ui.get_app_path_local().as_str(), "String");
                        let _ = db_guard.set("Paths.OkeBase", ui.get_app_path_okebase().as_str(), "String");
                        let _ = db_guard.set("Paths.Photoshop", ui.get_app_path_photoshop().as_str(), "String");
                        let _ = db_guard.set("Paths.PasFoto", ui.get_app_path_pasfoto().as_str(), "String");
                        let _ = db_guard.set("Paths.Manasik", ui.get_app_path_manasik().as_str(), "String");
                        let _ = db_guard.set("Paths.Wisuda", ui.get_app_path_wisuda().as_str(), "String");
                        let _ = db_guard.set("Paths.Profesi", ui.get_app_path_profesi().as_str(), "String");
                    }
                }
                ui.set_toast(slint::SharedString::from("Path folder disimpan ke database"));
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        ui.on_save_explorer_settings(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.set_toast(slint::SharedString::from("Pengaturan Explorer disimpan"));
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        ui.on_browse_path(move |target| {
            let ui_w = ui_weak.clone();
            let db_c = db_clone.clone();
            let kind = target.to_string();
            std::thread::spawn(move || {
                let picked = match kind.as_str() {
                    "creds" => platform::pick_file("Pilih credentials.json", "JSON Files (*.json)|*.json|All Files (*.*)|*.*"),
                    "photoshop" => platform::pick_file("Pilih Photoshop.exe", "Executable (*.exe)|*.exe|All Files (*.*)|*.*"),
                    _ => platform::pick_folder("Pilih Folder"),
                };
                if let Some(path) = picked {
                    let _ = ui_w.upgrade_in_event_loop(move |ui| {
                        let p = slint::SharedString::from(&path);
                        match kind.as_str() {
                            "creds" => ui.set_google_creds_path(p.clone()),
                            "local" => ui.set_app_path_local(p.clone()),
                            "okebase" => ui.set_app_path_okebase(p.clone()),
                            "photoshop" => ui.set_app_path_photoshop(p.clone()),
                            "pasfoto" => ui.set_app_path_pasfoto(p.clone()),
                            "manasik" => ui.set_app_path_manasik(p.clone()),
                            "wisuda" => ui.set_app_path_wisuda(p.clone()),
                            "profesi" => ui.set_app_path_profesi(p.clone()),
                            "explorer" => ui.set_ex_start_path(p.clone()),
                            _ => {}
                        }
                        if let Some(ref db_mutex) = db_c {
                            if let Ok(mut g) = db_mutex.lock() {
                                let key = match kind.as_str() {
                                    "creds" => Some("Google.CredsPath"),
                                    "local" => Some("Paths.LocalOutput"),
                                    "okebase" => Some("Paths.OkeBase"),
                                    "photoshop" => Some("Paths.Photoshop"),
                                    "pasfoto" => Some("Paths.PasFoto"),
                                    "manasik" => Some("Paths.Manasik"),
                                    "wisuda" => Some("Paths.Wisuda"),
                                    "profesi" => Some("Paths.Profesi"),
                                    _ => None,
                                };
                                if let Some(k) = key {
                                    let _ = g.set(k, &path, "String");
                                }
                            }
                        }
                        ui.set_toast(slint::SharedString::from(format!("Dipilih: {}", path)));
                    });
                }
            });
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        ui.on_test_trello(move || {
            if let Some(ui) = ui_weak.upgrade() {
                let k = ui.get_trello_api_key().to_string();
                let t = ui.get_trello_token().to_string();
                if k.trim().is_empty() || t.trim().is_empty() {
                    ui.set_toast(slint::SharedString::from("Isi API Key dan Token Trello dulu"));
                    return;
                }
                ui.set_app_trello_status(slint::SharedString::from("Menghubungkan ke Trello..."));
                let ui_w = ui_weak.clone();
                let db_c = db_clone.clone();
                let el = ui.get_editing_list_id().to_string();
                let rl = ui.get_revision_list_id().to_string();
                let ll = ui.get_late_list_id().to_string();
                std::thread::spawn(move || {
                    match trello_api::TrelloApiService::verify_connection(&k, &t) {
                        Ok(member) => {
                            let name = member.full_name.unwrap_or(member.username);
                            if let Some(ref db_mutex) = db_c {
                                if let Ok(mut g) = db_mutex.lock() {
                                    let _ = g.set("Trello.ApiKey", &k, "String");
                                    let _ = g.set("Trello.Token", &t, "String");
                                    let _ = g.set("Trello.IsConnected", "true", "Bool");
                                }
                            }
                            let _ = ui_w.upgrade_in_event_loop(move |u| {
                                u.set_app_trello_connected(true);
                                u.set_app_trello_status(slint::SharedString::from(format!("Tersambung sebagai {}", name)));
                                u.set_toast(slint::SharedString::from(format!("Trello tersambung ({})", name)));
                            });
                            spawn_trello_sync(ui_w, k, t, el, rl, ll, false);
                        }
                        Err(e) => {
                            let _ = ui_w.upgrade_in_event_loop(move |u| {
                                u.set_app_trello_connected(false);
                                u.set_app_trello_status(slint::SharedString::from(format!("Gagal: {}", e)));
                                u.set_toast(slint::SharedString::from(e));
                            });
                        }
                    }
                });
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        ui.on_open_url(move |url| {
            if let Err(e) = platform::open_url(&url) {
                if let Some(ui) = ui_weak.upgrade() {
                    ui.set_toast(slint::SharedString::from(e));
                }
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        ui.on_refresh_trello(move || {
            if let Some(ui) = ui_weak.upgrade() {
                spawn_trello_sync(
                    ui_weak.clone(),
                    ui.get_trello_api_key().to_string(),
                    ui.get_trello_token().to_string(),
                    ui.get_editing_list_id().to_string(),
                    ui.get_revision_list_id().to_string(),
                    ui.get_late_list_id().to_string(),
                    true,
                );
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        ui.on_win_minimize(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.window().set_minimized(true);
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        ui.on_win_maximize(move || {
            if let Some(ui) = ui_weak.upgrade() {
                let m = ui.window().is_maximized();
                ui.window().set_maximized(!m);
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        ui.on_win_move(move |dx, dy| {
            if let Some(ui) = ui_weak.upgrade() {
                let scale = ui.window().scale_factor();
                let pos = ui.window().position();
                let nx = pos.x + (dx * scale) as i32;
                let ny = pos.y + (dy * scale) as i32;
                ui.window().set_position(slint::PhysicalPosition::new(nx, ny));
            }
        });
    }

    // Callback simpan pengaturan akun ke SQLite nyata & trigger sync widget
    {
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        ui.on_save_account_settings(move || {
            if let Some(ui) = ui_weak.upgrade() {
                let k = ui.get_trello_api_key().to_string();
                let t = ui.get_trello_token().to_string();
                let e_b = ui.get_editing_board_id().to_string();
                let e_l = ui.get_editing_list_id().to_string();
                let r_b = ui.get_revision_board_id().to_string();
                let r_l = ui.get_revision_list_id().to_string();
                let l_b = ui.get_late_board_id().to_string();
                let l_l = ui.get_late_list_id().to_string();
                let qc = ui.get_qc_board_id().to_string();
                let a_b = ui.get_acc_board_id().to_string();
                let a_l = ui.get_acc_list_id().to_string();
                let g_c = ui.get_google_creds_path().to_string();
                let g_s = ui.get_google_sheet_id().to_string();
                let g_n = ui.get_google_sheet_name().to_string();
                let g_col = ui.get_google_sheet_col().to_string();
                let g_row = ui.get_google_sheet_row().to_string();
                let sp_n = ui.get_spreadsheet_sheet_name().to_string();
                let sp_r = ui.get_spreadsheet_range().to_string();
                let lb_h = ui.get_lb_harian_range().to_string();
                let lb_b = ui.get_lb_bulanan_range().to_string();

                if let Some(ref db_mutex) = db_clone {
                    if let Ok(mut db_guard) = db_mutex.lock() {
                        let _ = db_guard.set("Trello.ApiKey", &k, "String");
                        let _ = db_guard.set("Trello.Token", &t, "String");
                        let _ = db_guard.set("Trello.EditingBoardId", &e_b, "String");
                        let _ = db_guard.set("Trello.EditingListId", &e_l, "String");
                        let _ = db_guard.set("Trello.RevisionBoardId", &r_b, "String");
                        let _ = db_guard.set("Trello.RevisionListId", &r_l, "String");
                        let _ = db_guard.set("Trello.LateBoardId", &l_b, "String");
                        let _ = db_guard.set("Trello.LateListId", &l_l, "String");
                        let _ = db_guard.set("Trello.QcBoardId", &qc, "String");
                        let _ = db_guard.set("Trello.AccBoardId", &a_b, "String");
                        let _ = db_guard.set("Trello.AccListId", &a_l, "String");
                        let _ = db_guard.set("Google.CredsPath", &g_c, "String");
                        let _ = db_guard.set("Google.SheetId", &g_s, "String");
                        let _ = db_guard.set("Google.SheetName", &g_n, "String");
                        let _ = db_guard.set("Google.SheetColumn", &g_col, "String");
                        let _ = db_guard.set("Google.SheetRow", &g_row, "String");
                        let _ = db_guard.set("Spreadsheet.SheetName", &sp_n, "String");
                        let _ = db_guard.set("Spreadsheet.Range", &sp_r, "String");
                        let _ = db_guard.set("Leaderboard.Range", &lb_h, "String");
                        let _ = db_guard.set("Leaderboard.MonthlyRange", &lb_b, "String");
                    }
                }

                // Lakukan sinkronisasi live ke endpoint Trello
                let ui_bg = ui_weak.clone();
                std::thread::spawn(move || {
                    if !k.is_empty() && !t.is_empty() {
                        if !e_l.is_empty() {
                            if let Ok(cards) = trello_api::TrelloApiService::fetch_cards(&k, &t, &e_l) {
                                let count = format!("{}", cards.len());
                                let _ = ui_bg.upgrade_in_event_loop(move |u| {
                                    u.set_stat_editing(count.into());
                                });
                            }
                        } else {
                            let _ = ui_bg.upgrade_in_event_loop(|u| u.set_stat_editing("-".into()));
                        }
                        if !r_l.is_empty() {
                            if let Ok(cards) = trello_api::TrelloApiService::fetch_cards(&k, &t, &r_l) {
                                let count = format!("{}", cards.len());
                                let _ = ui_bg.upgrade_in_event_loop(move |u| {
                                    u.set_stat_revision(count.into());
                                });
                            }
                        } else {
                            let _ = ui_bg.upgrade_in_event_loop(|u| u.set_stat_revision("-".into()));
                        }
                        if !l_l.is_empty() {
                            if let Ok(cards) = trello_api::TrelloApiService::fetch_cards(&k, &t, &l_l) {
                                let count = format!("{}", cards.len());
                                let _ = ui_bg.upgrade_in_event_loop(move |u| {
                                    u.set_stat_late(count.into());
                                });
                            }
                        } else {
                            let _ = ui_bg.upgrade_in_event_loop(|u| u.set_stat_late("-".into()));
                        }
                    }
                });
            }
        });
    }

    // Startup: no blocking dummy HTTP calls!

    // ---- FASE 5: BOOTSTRAPPER & SPLASH SCREEN ----
    // Sesuai siklus C# (Bootstrapper.cs): SQLite -> Theme & UI -> Preload Settings -> Python & Scripts -> Done
    // QA: --no-splash skip siklus animasi, langsung ke main (log tetap terisi).
    if no_splash {
        ui.set_app_stage(slint::SharedString::from("main"));
    } else {
        let ui_handle = ui.as_weak();
        let proc_log_clone = Arc::clone(&proc_log);
        std::thread::spawn(move || {
            let steps = [
                (10, "Menghubungkan Database SQLite...", "> Membuka SQLite BMachine.db"),
                (25, "Memuat Pengaturan Tema & UI...", "> Tema Dark diterapkan"),
                (50, "Memuat Konfigurasi Aplikasi (Preload)...", "> 82 keys tersimpan ke memori"),
                (75, "Memeriksa dependensi Engine & Script Python...", "> Engine Python OK (Scripts/Master)"),
                (90, "Menyiapkan Layanan Eksternal & Dashboard...", "> Menyiapkan dashboard"),
                (100, "BMachine Siap Beroperasi.", "> BMachine Siap Beroperasi"),
            ];

            for (prog, status, log_entry) in steps {
                std::thread::sleep(std::time::Duration::from_millis(320));
                let s_status = slint::SharedString::from(status);
                let s_log = slint::SharedString::from(log_entry);
                let proc_log_step = Arc::clone(&proc_log_clone);

                let _ = slint::invoke_from_event_loop({
                    let ui_weak = ui_handle.clone();
                    move || {
                        if let Some(ui) = ui_weak.upgrade() {
                            ui.set_splash_progress(prog);
                            ui.set_splash_status(s_status);

                            let l2 = ui.get_splash_log2();
                            let l3 = ui.get_splash_log3();
                            ui.set_splash_log1(l2);
                            ui.set_splash_log2(l3);
                            ui.set_splash_log3(s_log.clone());

                            proc_log_step.lock().unwrap().add_log(log_entry.to_string());
                        }
                    }
                });
            }

            // Animasi selesai (350ms seperti C# Task.Delay(350)), lalu tutup splash
            std::thread::sleep(std::time::Duration::from_millis(350));
            let _ = slint::invoke_from_event_loop({
                let ui_weak = ui_handle.clone();
                move || {
                    if let Some(ui) = ui_weak.upgrade() {
                        ui.set_app_stage(slint::SharedString::from("main"));
                    }
                }
            });
        });
    }

    // ---- FASE 5: UPDATE SYSTEM (LAN & Manifest) ----
    {
        let ui_weak = ui.as_weak();
        ui.on_check_update(move || {
            let ui = ui_weak.upgrade().unwrap();
            let (info, status) = LocalUpdateService::check(None);
            if info.is_update_available {
                ui.set_update_latest_version(slint::SharedString::from(format!("v{}", info.latest_version)));
                ui.set_update_size_text(slint::SharedString::from(if info.size_bytes > 0 {
                    format!("{:.1} MB", info.size_bytes as f64 / 1_048_576.0)
                } else {
                    "147 MB".to_string()
                }));
                if !info.notes.is_empty() {
                    ui.set_update_notes(slint::SharedString::from(info.notes.join("\n")));
                }
                ui.set_dialog_open(slint::SharedString::from("UPDATE"));
            } else {
                ui.set_toast(slint::SharedString::from(status));
            }
        });
    }

    {
        let ui_weak = ui.as_weak();
        ui.on_apply_update(move || {
            let ui = ui_weak.upgrade().unwrap();
            ui.set_toast(slint::SharedString::from("Pembaruan disiapkan (restart diperlukan)..."));
        });
    }

    {
        let ui_weak = ui.as_weak();
        ui.on_dismiss_update(move || {
            let ui = ui_weak.upgrade().unwrap();
            ui.set_toast(slint::SharedString::from("Pembaruan ditunda (disimpan ke database)."));
        });
    }

    {
        let _ui_weak = ui.as_weak();
        ui.on_confirm_exit(move || {
            std::process::exit(0);
        });
    }

    // ---- FASE 5: DOC TAB DYNAMICS ----
    {
        let doc_clone = Arc::clone(&doc_mgr);
        let _ui_weak = ui.as_weak();
        // Handlers: sinkronisasi state ke AppData/BMachine.v2/doc_info.json saat eksekusi
        let mut mgr = doc_clone.lock().unwrap();
        let _ = mgr.add_tab();
        let _ = mgr.active_tab();
        let _ = mgr.active_tab_mut();
        mgr.close_tab(1);
        let _ = mgr.write_doc_info_json(0);
    }

    // ---- FASE 5: LOG PROGRESS WATCHER (live) ----
    // Thread nyata mem-poll file progress/result C# (%TEMP%\bmachine_*.json)
    // tiap ~150ms (throttle UI, bukan append per-baris) dan mendorong snapshot
    // (baris log terbaru + status + progress + label) ke panel log.
    {
        let ui_weak = ui.as_weak();
        let proc_log_w = Arc::clone(&proc_log);
        std::thread::spawn(move || {
            let mut last_prog_key: String = String::new();
            let mut last_snap: String = String::new();
            loop {
                std::thread::sleep(std::time::Duration::from_millis(150));
                let progress = ProcessLogService::check_progress_file();
                let result = ProcessLogService::check_result_file();

                // Catat step progress baru ke buffer log (dedupe via key).
                if let Some((cur, tot, file)) = &progress {
                    let key = format!("{}|{}", cur, tot);
                    if (*cur != 0 || !file.is_empty()) && key != last_prog_key {
                        last_prog_key = key.clone();
                        let pct = if *tot > 0 { *cur * 100 / *tot } else { 0 };
                        proc_log_w
                            .lock()
                            .unwrap()
                            .add_log(format!("Proses: {} ({}/{}, {}%)", file, cur, tot, pct));
                    }
                }

                // Status + progress + label untuk tampilan panel log.
                let (status, prog_val, label) = match (&progress, &result) {
                    (_, Some((ok, s))) => {
                        let st = if *ok { "DONE" } else { "ERROR" };
                        let lbl = format!(
                            "{}: {}",
                            if *ok { "Selesai" } else { "Gagal" },
                            s
                        );
                        (st.to_string(), 100, lbl)
                    }
                    (Some((cur, tot, file)), None) => {
                        let pct = if *tot > 0 { *cur * 100 / *tot } else { 0 };
                        ("RUNNING".to_string(), pct, format!("{} ({}/{})", file, cur, tot))
                    }
                    (None, None) => ("IDLE".to_string(), 0, "Siap".to_string()),
                };

                // Dorong snapshot ke UI hanya bila berubah (hemat event loop).
                let lines = proc_log_w.lock().unwrap().get_display_lines(12);
                let snap = format!("{}|{}|{}|{}", status, prog_val, label, lines.len());
                if snap != last_snap {
                    last_snap = snap;
                    let lines_src: Vec<slint::SharedString> = lines
                        .into_iter()
                        .map(slint::SharedString::from)
                        .collect();
                    let s_status = slint::SharedString::from(status);
                    let s_label = slint::SharedString::from(label);
                    let _ = slint::invoke_from_event_loop({
                        let ui_w = ui_weak.clone();
                        move || {
                            if let Some(ui) = ui_w.upgrade() {
                                let model = slint::ModelRc::new(slint::VecModel::from(lines_src));
                                ui.set_log_lines(model);
                                ui.set_log_status(s_status);
                                ui.set_log_progress(prog_val);
                                ui.set_log_progress_label(s_label);
                            }
                        }
                    });
                }

                // Proses selesai (success/failed) -> hentikan watcher.
                if result.is_some() {
                    break;
                }
            }
        });
    }

    // Aksi panel log (nyata, bukan toast bohong).
    {
        let ui_weak = ui.as_weak();
        let proc_log_w = Arc::clone(&proc_log);
        ui.on_clear_log(move || {
            proc_log_w.lock().unwrap().clear();
            if let Some(ui) = ui_weak.upgrade() {
                ui.set_log_lines(slint::ModelRc::default());
                ui.set_log_status(slint::SharedString::from("IDLE"));
                ui.set_log_progress(0);
                ui.set_log_progress_label(slint::SharedString::from("Siap"));
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let proc_log_w = Arc::clone(&proc_log);
        ui.on_copy_log(move || {
            let lines = proc_log_w.lock().unwrap().get_display_lines(1000);
            let dir = std::env::var("APPDATA")
                .map(|a| std::path::PathBuf::from(a).join("BMachine"))
                .unwrap_or_else(|_| std::env::temp_dir());
            let path = dir.join("bmachine_log.txt");
            match std::fs::write(&path, lines.join("\n")) {
                Ok(_) => {
                    let p = path.display().to_string();
                    let _ = slint::invoke_from_event_loop({
                        let ui_w = ui_weak.clone();
                        move || {
                            if let Some(ui) = ui_w.upgrade() {
                                ui.set_toast(slint::SharedString::from(format!(
                                    "Log disalin ke {}",
                                    p
                                )));
                            }
                        }
                    });
                }
                Err(e) => {
                    let _ = slint::invoke_from_event_loop({
                        let ui_w = ui_weak.clone();
                        move || {
                            if let Some(ui) = ui_w.upgrade() {
                                ui.set_toast(slint::SharedString::from(format!(
                                    "Gagal menyimpan log: {}",
                                    e
                                )));
                            }
                        }
                    });
                }
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let proc_log_w = Arc::clone(&proc_log);
        ui.on_stop_log(move || {
            proc_log_w
                .lock()
                .unwrap()
                .add_log(
                    "[WARNING] Stop diminta — belum ada proses aktif di port ini (Fase 6)."
                        .to_string(),
                );
            let _ = slint::invoke_from_event_loop({
                let ui_w = ui_weak.clone();
                move || {
                    if let Some(ui) = ui_w.upgrade() {
                        ui.set_log_status(slint::SharedString::from("IDLE"));
                        ui.set_log_progress(0);
                        ui.set_log_progress_label(slint::SharedString::from("Siap"));
                    }
                }
            });
        });
    }

    // ---- Search rekursif (substring case-insensitive, `*`/`?` literal) ----
    // Urutan hasil: folder dulu, lalu shortcut, lalu file (tiap kelompok
    // name-asc, sesuai urutan kanonis tabel). Cap 5000 (spec §2.4).
    {
        let ui_weak = ui.as_weak();
        ui.on_ex_search(move |query, folder_key| {
            let ui = ui_weak.upgrade().unwrap();
            let q = query.to_lowercase();
            let keys = subtree_keys(&folder_key);
            let mut hits: Vec<&(&str, &str, &str, &str, &str, &str, &str, &str, &str, &str)> =
                Vec::new();
            let mut truncated = false;
            for item in EXPLORER_ITEMS {
                if !keys.contains(&item.0) {
                    continue;
                }
                if !item.2.to_lowercase().contains(&q) {
                    continue;
                }
                hits.push(item);
                if hits.len() >= 5000 {
                    truncated = true;
                    break;
                }
            }
            // Kelompokkan: folder -> shortcut (.lnk) -> file.
            let mut ordered: Vec<&&(&str, &str, &str, &str, &str, &str, &str, &str, &str, &str)> =
                Vec::new();
            for kind in ["folder", "link", "file"] {
                for h in hits.iter().filter(|i| i.3 == kind) {
                    ordered.push(h);
                }
            }
            let names: Vec<slint::SharedString> =
                ordered.iter().map(|i| i.2.into()).collect();
            let kinds: Vec<slint::SharedString> =
                ordered.iter().map(|i| i.3.into()).collect();
            let exts: Vec<slint::SharedString> =
                ordered.iter().map(|i| i.4.into()).collect();
            let bases: Vec<slint::SharedString> =
                ordered.iter().map(|i| i.5.into()).collect();
            let sizes: Vec<slint::SharedString> =
                ordered.iter().map(|i| i.6.into()).collect();
            let dates: Vec<slint::SharedString> =
                ordered.iter().map(|i| i.7.into()).collect();
            let previews: Vec<slint::SharedString> =
                ordered.iter().map(|i| i.8.into()).collect();
            let locs: Vec<slint::SharedString> =
                ordered.iter().map(|i| i.9.into()).collect();
            let paths: Vec<slint::SharedString> = ordered
                .iter()
                .map(|i| {
                    if i.1.is_empty() {
                        slint::SharedString::from(i.2)
                    } else {
                        slint::SharedString::from(format!("{}\\{}", i.1, i.2))
                    }
                })
                .collect();
            let half = (names.len() + 1) / 2;
            ui.set_ex_sr_names(slint::ModelRc::new(slint::VecModel::from_slice(&names)));
            ui.set_ex_sr_kinds(slint::ModelRc::new(slint::VecModel::from_slice(&kinds)));
            ui.set_ex_sr_exts(slint::ModelRc::new(slint::VecModel::from_slice(&exts)));
            ui.set_ex_sr_bases(slint::ModelRc::new(slint::VecModel::from_slice(&bases)));
            ui.set_ex_sr_sizes(slint::ModelRc::new(slint::VecModel::from_slice(&sizes)));
            ui.set_ex_sr_dates(slint::ModelRc::new(slint::VecModel::from_slice(&dates)));
            ui.set_ex_sr_previews(slint::ModelRc::new(slint::VecModel::from_slice(&previews)));
            ui.set_ex_sr_locs(slint::ModelRc::new(slint::VecModel::from_slice(&locs)));
            ui.set_ex_sr_paths(slint::ModelRc::new(slint::VecModel::from_slice(&paths)));
            ui.set_ex_sr_half(half as i32);
            ui.set_ex_sr_truncated(truncated);
        });
    }

    // ---- Type-ahead: indeks kanonis lokal pertama yang diawali buffer ----
    {
        let ui_weak = ui.as_weak();
        ui.on_ex_typeahead(move |folder_key, buffer| {
            let ui = ui_weak.upgrade().unwrap();
            let b = buffer.to_lowercase();
            let mut local: i32 = -1;
            let mut idx: i32 = -1;
            for item in EXPLORER_ITEMS {
                if folder_key != item.0 {
                    continue;
                }
                local += 1;
                if item.2.to_lowercase().starts_with(&b) {
                    idx = local;
                    break;
                }
            }
            ui.set_ex_ta_index(idx);
        });
    }

    // ---- Helper string (Slint tidak punya contains/substring) ----
    ui.on_ex_add_txt_ext(move |name| {
        if name.is_empty() {
            slint::SharedString::new()
        } else if name.contains('.') {
            name
        } else {
            slint::SharedString::from(format!("{}.txt", name))
        }
    });
    ui.on_ex_initials(move |name| {
        slint::SharedString::from(name.chars().take(2).collect::<String>().to_uppercase())
    });
    ui.on_ex_split_ext(move |name| {
        match name.rfind('.') {
            Some(i) if i > 0 => slint::SharedString::from(&name[i..]),
            _ => slint::SharedString::new(),
        }
    });
    ui.on_ex_name_base(move |name| {
        match name.rfind('.') {
            Some(i) if i > 0 => slint::SharedString::from(&name[..i]),
            _ => name,
        }
    });
    ui.on_ex_is_typeahead_char(move |ch| {
        ch.chars().count() == 1
            && ch.chars()
                .next()
                .map_or(false, |c| c.is_ascii_alphanumeric() || c == '_' || c == '-' || c == '.')
    });

    // ---- Import kartu manual (C# ConfirmAddManualCard) ----
    // Satu link per baris; link duplikat dilewati diam-diam (C#:
    // skip if already added); slot penuh (maks 5) dihitung gagal.
    {
        let ui_weak = ui.as_weak();
        ui.on_import_manual(move |input| {
            let ui = ui_weak.upgrade().unwrap();
            let mut titles = read_str_model(ui.get_manual_titles());
            let mut urls = read_str_model(ui.get_manual_urls());
            let mut dones = read_bool_model(ui.get_manual_done());
            let mut count = ui.get_manual_count();
            let mut added = 0;
            let mut failed = 0;
            for line in input.lines() {
                let line = line.trim();
                if line.is_empty() {
                    continue;
                }
                match parse_trello_link(line) {
                    Some((_id, title, url)) => {
                        if count as usize >= titles.len() {
                            failed += 1;
                            continue;
                        }
                        if urls.iter().any(|u| u.as_str() == url) {
                            continue;
                        }
                        let slot = count as usize;
                        titles[slot] = title.into();
                        urls[slot] = url.into();
                        dones[slot] = false;
                        count += 1;
                        added += 1;
                    }
                    None => failed += 1,
                }
            }
            ui.set_manual_titles(slint::ModelRc::new(slint::VecModel::from_slice(&titles)));
            ui.set_manual_urls(slint::ModelRc::new(slint::VecModel::from_slice(&urls)));
            ui.set_manual_done(slint::ModelRc::new(slint::VecModel::from_slice(&dones)));
            ui.set_manual_count(count);
            if added > 0 {
                ui.set_add_manual_open(false);
                ui.set_manual_input(slint::SharedString::new());
                ui.set_toast(slint::SharedString::from(if failed > 0 {
                    format!("Ditambahkan {} card manual, {} gagal.", added, failed)
                } else {
                    format!("Ditambahkan {} card manual.", added)
                }));
            } else if failed > 0 {
                ui.set_toast(slint::SharedString::from(format!(
                    "{} link gagal dibaca — tempel link Trello (https://trello.com/c/...).",
                    failed
                )));
            } else {
                ui.set_toast(slint::SharedString::from("Tempel dulu link Trello-nya."));
            }
        });
    }

    // ---- Hapus kartu manual (C# Remove Manual Card) ----
    {
        let ui_weak = ui.as_weak();
        ui.on_remove_manual(move |idx| {
            let ui = ui_weak.upgrade().unwrap();
            let count = ui.get_manual_count();
            if idx < 0 || idx >= count {
                return;
            }
            let mut titles = read_str_model(ui.get_manual_titles());
            let mut urls = read_str_model(ui.get_manual_urls());
            let mut dones = read_bool_model(ui.get_manual_done());
            let i = idx as usize;
            for j in i..(count as usize - 1) {
                titles[j] = titles[j + 1].clone();
                urls[j] = urls[j + 1].clone();
                dones[j] = dones[j + 1];
            }
            let last = count as usize - 1;
            titles[last] = slint::SharedString::new();
            urls[last] = slint::SharedString::new();
            dones[last] = false;
            ui.set_manual_titles(slint::ModelRc::new(slint::VecModel::from_slice(&titles)));
            ui.set_manual_urls(slint::ModelRc::new(slint::VecModel::from_slice(&urls)));
            ui.set_manual_done(slint::ModelRc::new(slint::VecModel::from_slice(&dones)));
            ui.set_manual_count(count - 1);
            ui.set_selected_card(slint::SharedString::new());
            ui.set_toast(slint::SharedString::from("Kartu manual dihapus dari daftar."));
        });
    }

    // Global radial shortcut watcher. The C# app listens globally for the
    // configured ShortcutConfig; this port mirrors that with rdev. The UI
    // property `radial-open` is toggled by a Slint timer so the global hook
    // never blocks or races the UI thread.
    {
        let shortcut = {
            let mut cfg = RadialShortcutConfig {
                Type: 1,
                MouseButton: 2,
                Key: 222,
                Modifiers: 12,
            };
            if let Some(ref db_mutex) = db {
                if let Ok(guard) = db_mutex.lock() {
                    if let Some(json) = guard.get("ShortcutConfig") {
                        if let Ok(parsed) = serde_json::from_str::<RadialShortcutConfig>(&json) {
                            cfg = parsed;
                        }
                    }
                }
            }
            cfg
        };

        let trigger_key = if shortcut.Type == 1 {
            vk_to_rdev_key(shortcut.Key)
        } else {
            None
        };
        let trigger_button = if shortcut.Type == 0 {
            mouse_button_index_to_rdev(shortcut.MouseButton)
        } else {
            None
        };
        let trigger_mods = shortcut.Modifiers;

        let state = Arc::new(AtomicU8::new(0)); // 0 idle, 1 open, 2 close
        let state_for_hook = Arc::clone(&state);
        let hotkey_log = std::env::var("APPDATA")
            .ok()
            .map(|a| std::path::PathBuf::from(a).join("BMachine").join("rdev_hotkey.log"));
        if let Some(ref p) = hotkey_log {
            if let Some(parent) = p.parent() {
                let _ = std::fs::create_dir_all(parent);
            }
            let _ = std::fs::write(p, "Starting global radial shortcut hook (rdev listen)...\n");
        }
        std::thread::spawn(move || {
            let mods_cell = Cell::new(0u8);
            let log_path = std::env::var("APPDATA")
                .ok()
                .map(|a| std::path::PathBuf::from(a).join("BMachine").join("rdev_hotkey.log"));
            if let Some(ref p) = log_path {
                if let Some(parent) = p.parent() {
                    let _ = std::fs::create_dir_all(parent);
                }
            }
            let _ = rdev::listen(move |event: Event| {
                let mut m = mods_cell.get();
                match event.event_type {
                    EventType::KeyPress(key) => {
                        if let Some(ref p) = log_path {
                            let msg = format!("{}\n", event.name.unwrap_or_default());
                            let _ = std::fs::OpenOptions::new().create(true).append(true).open(p).unwrap().write(msg.as_bytes());
                        }
                        match key {
                            Key::ShiftLeft | Key::ShiftRight => m |= 4,
                            Key::ControlLeft | Key::ControlRight => m |= 2,
                            Key::Alt => m |= 1,
                            Key::MetaLeft | Key::MetaRight => m |= 8,
                            _ => {}
                        }
                        mods_cell.set(m);
                        if shortcut.Type == 1 {
                            if let Some(k) = trigger_key {
                                if key == k && (m & trigger_mods == trigger_mods) {
                                    state_for_hook.store(1, Ordering::SeqCst);
                                }
                            }
                        }
                    }
                    EventType::KeyRelease(key) => {
                        if shortcut.Type == 1 {
                            if let Some(k) = trigger_key {
                                if key == k {
                                    state_for_hook.store(2, Ordering::SeqCst);
                                }
                            }
                        }
                        match key {
                            Key::ShiftLeft | Key::ShiftRight => m &= !4,
                            Key::ControlLeft | Key::ControlRight => m &= !2,
                            Key::Alt => m &= !1,
                            Key::MetaLeft | Key::MetaRight => m &= !8,
                            _ => {}
                        }
                        mods_cell.set(m);
                    }
                    EventType::ButtonPress(button) => {
                        if shortcut.Type == 0 {
                            if let Some(b) = trigger_button {
                                if button == b && (m & trigger_mods == trigger_mods) {
                                    state_for_hook.store(1, Ordering::SeqCst);
                                }
                            }
                        }
                    }
                    EventType::ButtonRelease(button) => {
                        if shortcut.Type == 0 {
                            if let Some(b) = trigger_button {
                                if button == b {
                                    state_for_hook.store(2, Ordering::SeqCst);
                                }
                            }
                        }
                    }
                    _ => {}
                }
            });
        });

        let ui_weak = ui.as_weak();
        let state = Arc::clone(&state);
        slint::Timer::default().start(
            slint::TimerMode::Repeated,
            std::time::Duration::from_millis(30),
            move || {
                let cmd = state.swap(0, Ordering::SeqCst);
                if let Some(ui) = ui_weak.upgrade() {
                    if cmd == 1 {
                        ui.set_radial_open(true);
                    } else if cmd == 2 {
                        ui.set_radial_open(false);
                    }
                }
            },
        );
    }

    // ui.run() mengambil alih event loop; show() sudah dipanggil di atas.
    ui.run()?;
    Ok(())
}
