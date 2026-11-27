slint::include_modules!();

mod db;
mod doc_manager;
mod executor;
mod local_update;
mod process_log;
mod sheets_api;
mod trello_api;

use db::DatabaseService;
#[allow(unused_imports)]
use executor::ScriptExecutor;
use sheets_api::GoogleSheetsService;

use doc_manager::DocManager;
use local_update::LocalUpdateService;
use process_log::ProcessLogService;
use std::sync::{Arc, Mutex};

// TABEL GLOBAL EXPLORER — digenerate tools/gen_explorer_data.py.
// (folder-key, path, name, kind, ext, base, size, date, preview, loc)
// Dipakai search (rekursif, substring case-insensitive) & type-ahead.
// TABEL GLOBAL EXPLORER — digenerate tools/gen_explorer_data.py.
// (folder-key, path, name, kind, ext, base, size, date, preview, loc)
// Dipakai search (rekursif, substring case-insensitive) & type-ahead.
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

    // Tarik data live bila kredensial & list sudah ada di database
    if !init_trello_key.is_empty() && !init_trello_tok.is_empty() {
        let ui_weak = ui.as_weak();
        let k = init_trello_key.clone();
        let t = init_trello_tok.clone();
        let el = init_edit_list.clone();
        let rl = init_rev_list.clone();
        let ll = init_late_list.clone();
        std::thread::spawn(move || {
            if !el.is_empty() {
                if let Ok(cards) = trello_api::TrelloApiService::fetch_cards(&k, &t, &el) {
                    let count = format!("{}", cards.len());
                    let _ = ui_weak.upgrade_in_event_loop(move |u| {
                        u.set_stat_editing(count.into());
                    });
                }
            }
            if !rl.is_empty() {
                if let Ok(cards) = trello_api::TrelloApiService::fetch_cards(&k, &t, &rl) {
                    let count = format!("{}", cards.len());
                    let _ = ui_weak.upgrade_in_event_loop(move |u| {
                        u.set_stat_revision(count.into());
                    });
                }
            }
            if !ll.is_empty() {
                if let Ok(cards) = trello_api::TrelloApiService::fetch_cards(&k, &t, &ll) {
                    let count = format!("{}", cards.len());
                    let _ = ui_weak.upgrade_in_event_loop(move |u| {
                        u.set_stat_late(count.into());
                    });
                }
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

    // Helper trello & sheets service test
    let _ = trello_api::TrelloApiService::fetch_boards("dummy", "dummy");
    let _ = trello_api::TrelloApiService::fetch_lists("dummy", "dummy", "dummy");
    let _ = GoogleSheetsService::validate_credentials("credentials.json");
    let _ = GoogleSheetsService::get_range_url("dummy_sheet_id", "Sheet1", "A1:Z");

    // ---- FASE 5: BOOTSTRAPPER & SPLASH SCREEN ----
    // Sesuai siklus C# (Bootstrapper.cs): SQLite -> Theme & UI -> Preload Settings -> Python & Scripts -> Done
    {
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

    // ---- FASE 5: LOG PROGRESS WATCHER ----
    {
        let mut log_svc = ProcessLogService::default();
        log_svc.add_log("[SYSTEM] BMachine Core Initialized".to_string());
        let _recent = log_svc.get_recent_logs(5);
        let _ = ProcessLogService::check_progress_file();
        log_svc.clear();
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

    // ui.run() mengambil alih event loop; show() sudah dipanggil di atas.
    ui.run()?;
    Ok(())
}
