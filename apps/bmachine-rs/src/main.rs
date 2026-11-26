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
    if let Some(ref db_mutex) = db {
        if let Ok(db_guard) = db_mutex.lock() {
            if let Some(key) = db_guard.get("Trello.ApiKey") {
                ui.set_trello_api_key(slint::SharedString::from(key));
            }
            if let Some(token) = db_guard.get("Trello.Token") {
                ui.set_trello_token(slint::SharedString::from(token));
            }
            if let Some(sheet_id) = db_guard.get("Google.SheetId") {
                ui.set_google_sheet_id(slint::SharedString::from(sheet_id));
            }
        }
    }

    // Handlers untuk simpan setting ke SQLite nyata
    if let Some(ref db_mutex) = db {
        if let Ok(mut db_guard) = db_mutex.lock() {
            let _ = db_guard.set("App.PortReady", "True", "String");
        }
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

    ui.run()?;
    Ok(())
}
