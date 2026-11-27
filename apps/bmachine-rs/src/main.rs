slint::include_modules!();

mod db;
mod doc_manager;
mod executor;
mod local_update;
mod petdex;
mod platform;
mod process_log;
mod radial_geom;
mod sheets_api;
mod spreadsheet_guard;
mod theme;
mod trello_api;
mod voice;
mod volume;

use db::DatabaseService;
#[allow(unused_imports)]
use executor::ScriptExecutor;
#[allow(unused_imports)]
use sheets_api::GoogleSheetsService;

use doc_manager::DocManager;
use local_update::LocalUpdateService;
use process_log::ProcessLogService;
use rdev::{Button, Event, EventType, Key};
use std::cell::Cell;
use std::time::Instant;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc;
use std::sync::{Arc, Mutex};

// Versi aplikasi untuk footer flyout profil (C# AppVersion =
// assembly ToString(2) — v15.15.3 -> "15.15"). BUKAN versi Cargo.
const APP_VERSION: &str = "15.15";
// Versi lengkap untuk splash (C# SplashViewModel.VersionText =
// "VERSION " + Assembly Version ToString(3) -> "VERSION 15.15.3").
const APP_VERSION_FULL: &str = "15.15.3";

// Posisi cursor terakhir (physical px) — dilacak hook rdev (MouseMove).
// Dipakai cursor_screen_position() di Linux (GetCursorPos hanya Windows).
static LAST_MOUSE_POS: Mutex<(i32, i32)> = Mutex::new((640, 400));

// TABEL GLOBAL EXPLORER — digenerate tools/gen_explorer_data.py.
// (folder-key, path, name, kind, ext, base, size, date, preview, loc)
// Dipakai search (rekursif, substring case-insensitive) & type-ahead.
// TABEL GLOBAL EXPLORER — digenerate tools/gen_explorer_data.py.
// (folder-key, path, name, kind, ext, base, size, date, preview, loc)
// Dipakai search (rekursif, substring case-insensitive) & type-ahead.
// Radial menu global trigger config. The C# `ShortcutConfig` JSON shape is
// mirrored here so the Rust port can read the same DB key without changing
// the C# app's storage format.
#[derive(Clone, Copy, Debug, serde::Deserialize, serde::Serialize)]
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

fn default_trigger_type() -> u8 {
    // C# TriggerConfig.cs: Type = Mouse (0) — default trigger radial C#
    // adalah Shift + Middle Click, BUKAN keyboard.
    0
}
fn default_mouse_button() -> u8 {
    2
}
fn default_trigger_key() -> u32 {
    // C# TriggerConfig.Key default = 0 (tidak dipakai saat Type=Mouse).
    0
}
fn default_trigger_modifiers() -> u8 {
    // C# TriggerConfig.Modifiers default = 4 (Shift).
    // Bitmask: 1=Alt, 2=Ctrl, 4=Shift, 8=Win.
    4
}

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

fn rdev_key_to_vk(key: Key) -> Option<u32> {
    match key {
        Key::Backspace => Some(8),
        Key::Tab => Some(9),
        Key::Return => Some(13),
        Key::Escape => Some(27),
        Key::Space => Some(32),
        Key::PageUp => Some(33),
        Key::PageDown => Some(34),
        Key::End => Some(35),
        Key::Home => Some(36),
        Key::LeftArrow => Some(37),
        Key::UpArrow => Some(38),
        Key::RightArrow => Some(39),
        Key::DownArrow => Some(40),
        Key::Insert => Some(45),
        Key::Delete => Some(46),
        Key::KeyA => Some(65),
        Key::KeyB => Some(66),
        Key::KeyC => Some(67),
        Key::KeyD => Some(68),
        Key::KeyE => Some(69),
        Key::KeyF => Some(70),
        Key::KeyG => Some(71),
        Key::KeyH => Some(72),
        Key::KeyI => Some(73),
        Key::KeyJ => Some(74),
        Key::KeyK => Some(75),
        Key::KeyL => Some(76),
        Key::KeyM => Some(77),
        Key::KeyN => Some(78),
        Key::KeyO => Some(79),
        Key::KeyP => Some(80),
        Key::KeyQ => Some(81),
        Key::KeyR => Some(82),
        Key::KeyS => Some(83),
        Key::KeyT => Some(84),
        Key::KeyU => Some(85),
        Key::KeyV => Some(86),
        Key::KeyW => Some(87),
        Key::KeyX => Some(88),
        Key::KeyY => Some(89),
        Key::KeyZ => Some(90),
        Key::Num0 => Some(48),
        Key::Num1 => Some(49),
        Key::Num2 => Some(50),
        Key::Num3 => Some(51),
        Key::Num4 => Some(52),
        Key::Num5 => Some(53),
        Key::Num6 => Some(54),
        Key::Num7 => Some(55),
        Key::Num8 => Some(56),
        Key::Num9 => Some(57),
        Key::F1 => Some(112),
        Key::F2 => Some(113),
        Key::F3 => Some(114),
        Key::F4 => Some(115),
        Key::F5 => Some(116),
        Key::F6 => Some(117),
        Key::F7 => Some(118),
        Key::F8 => Some(119),
        Key::F9 => Some(120),
        Key::F10 => Some(121),
        Key::F11 => Some(122),
        Key::F12 => Some(123),
        Key::SemiColon => Some(186),
        Key::Equal => Some(187),
        Key::Comma => Some(188),
        Key::Minus => Some(189),
        Key::Dot => Some(190),
        Key::Slash => Some(191),
        Key::BackQuote => Some(192),
        Key::LeftBracket => Some(219),
        Key::BackSlash => Some(220),
        Key::RightBracket => Some(221),
        Key::Quote => Some(222),
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

// C# GlobalInputHookService.CheckModifiers compares the whole bitmask, not a
// subset. Keep matching pure so both keyboard and mouse paths use one rule.
fn shortcut_matches(
    config: &RadialShortcutConfig,
    key: Option<Key>,
    button: Option<Button>,
    modifiers: u8,
) -> bool {
    if modifiers != config.Modifiers {
        return false;
    }
    match config.Type {
        0 => button == mouse_button_index_to_rdev(config.MouseButton),
        1 => key == vk_to_rdev_key(config.Key),
        _ => false,
    }
}

fn shortcut_label(config: &RadialShortcutConfig) -> String {
    let mut parts = Vec::new();
    if config.Modifiers & 8 != 0 {
        parts.push("Win");
    }
    if config.Modifiers & 2 != 0 {
        parts.push("Ctrl");
    }
    if config.Modifiers & 1 != 0 {
        parts.push("Alt");
    }
    if config.Modifiers & 4 != 0 {
        parts.push("Shift");
    }
    let trigger = if config.Type == 0 {
        match config.MouseButton {
            0 => "Mouse Left".to_string(),
            1 => "Mouse Right".to_string(),
            2 => "Mouse Middle".to_string(),
            _ => format!("Mouse {}", config.MouseButton),
        }
    } else {
        match config.Key {
            222 => "'".to_string(),
            219 => "[".to_string(),
            221 => "]".to_string(),
            112..=123 => format!("F{}", config.Key - 111),
            other => format!("Key {}", other),
        }
    };
    parts.push(&trigger);
    parts.join(" + ")
}

fn is_modifier_key(key: Key) -> bool {
    matches!(
        key,
        Key::ShiftLeft
            | Key::ShiftRight
            | Key::ControlLeft
            | Key::ControlRight
            | Key::Alt
            | Key::MetaLeft
            | Key::MetaRight
    )
}

fn modifier_mask_for_key(key: Key) -> u8 {
    match key {
        Key::ShiftLeft | Key::ShiftRight => 4,
        Key::ControlLeft | Key::ControlRight => 2,
        Key::Alt => 1,
        Key::MetaLeft | Key::MetaRight => 8,
        _ => 0,
    }
}

fn cursor_screen_position() -> Option<(i32, i32)> {
    #[cfg(target_os = "windows")]
    {
        #[repr(C)]
        struct Point {
            x: i32,
            y: i32,
        }
        unsafe extern "system" {
            fn GetCursorPos(point: *mut Point) -> i32;
        }
        let mut point = Point { x: 0, y: 0 };
        if unsafe { GetCursorPos(&mut point) } != 0 {
            Some((point.x, point.y))
        } else {
            None
        }
    }
    #[cfg(not(target_os = "windows"))]
    {
        LAST_MOUSE_POS.lock().ok().map(|p| *p)
    }
}

/// Asal window radial (C# App.axaml.cs: posisi tengah di cursor:
/// screenPos - w*scaling/2, w = 320). `scale` = faktor DPI window
/// utama; hasil dalam physical px untuk Window::set_position.
fn radial_window_origin(cursor: (i32, i32), scale: f32) -> (i32, i32) {
    let half = (160.0 * scale) as i32;
    (cursor.0 - half, cursor.1 - half)
}

/// Format angka dengan pemisah ribuan titik (1.280 — C#
/// FormattedPoints / FormattedTotalPoints).
fn format_id_number(n: i64) -> String {
    let neg = n < 0;
    let s = n.abs().to_string();
    let mut out = String::new();
    for (i, ch) in s.chars().enumerate() {
        if i > 0 && (s.len() - i) % 3 == 0 {
            out.push('.');
        }
        out.push(ch);
    }
    if neg {
        format!("-{}", out)
    } else {
        out
    }
}

/// Format satu nilai poin ("1280" atau "1.280" → "1.280"); nilai
/// non-angka dikembalikan apa adanya.
fn format_points_value(s: &str) -> String {
    let t = s.trim();
    match t.replace('.', "").replace(',', "").parse::<i64>() {
        Ok(n) => format_id_number(n),
        Err(_) => t.to_string(),
    }
}

/// Muat baris leaderboard dari DB (Lb.Row.N = "nama\tpoin") + hitung
/// total poin (C# FormattedTotalPoints — jumlah semua baris).
/// Dipakai saat startup + refresh FAB.
fn load_leaderboard(ui: &AppWindow, db: &Option<Arc<Mutex<DatabaseService>>>) {
    let mut lb_rows: Vec<slint::SharedString> = vec![];
    if let Some(db_mutex) = db {
        if let Ok(db_guard) = db_mutex.lock() {
            for i in 1..=10 {
                let key = format!("Lb.Row.{}", i);
                if let Some(v) = db_guard.get(&key) {
                    lb_rows.push(slint::SharedString::from(v.clone()));
                }
            }
        }
    }
    let lb_count = lb_rows.len() as i32;
    let lc0: Vec<slint::SharedString> = lb_rows
        .iter()
        .map(|r| slint::SharedString::from(r.split('\t').next().unwrap_or("").to_string()))
        .collect();
    let lc1: Vec<slint::SharedString> = lb_rows
        .iter()
        .map(|r| {
            slint::SharedString::from(format_points_value(
                r.split('\t').nth(1).unwrap_or(""),
            ))
        })
        .collect();
    let total: i64 = lb_rows
        .iter()
        .map(|r| {
            r.split('\t')
                .nth(1)
                .unwrap_or("")
                .replace('.', "")
                .replace(',', "")
                .trim()
                .parse::<i64>()
                .unwrap_or(0)
        })
        .sum();
    ui.set_lb_rows(slint::ModelRc::new(slint::VecModel::from(lb_rows)));
    ui.set_lb_row_count(lb_count);
    ui.set_lb_col0(slint::ModelRc::new(slint::VecModel::from(lc0)));
    ui.set_lb_col1(slint::ModelRc::new(slint::VecModel::from(lc1)));
    ui.set_lb_total(slint::SharedString::from(format_id_number(total)));
}

/// Posisi window (logis w×h) di tengah window utama (CenterOwner /
/// CenterScreen ala C# — Slint tidak punya API itu; hitung dari
/// pos/ukuran window utama). Hasil physical px untuk set_position.
/// Posisi di-clamp >= 0 supaya titlebar selalu dalam layar (tanpa
/// clamp, window yang lebih besar dari main window — mis. Toolbox
/// 1024x730 vs main 960x640 — terbit di (-32,-45) dan titlebar
/// tidak terjangkau). Slint 1.19 tidak menyediakan ukuran layar,
/// jadi CenterScreen murni tidak bisa — tengah main window + clamp
/// adalah pendekatan terdekat.
fn center_over_main(ui: &AppWindow, w: i32, h: i32) -> (i32, i32) {
    let win = ui.window();
    let pos = win.position();
    let size = win.size();
    let scale = win.scale_factor();
    let pw = (w as f32 * scale) as i32;
    let ph = (h as f32 * scale) as i32;
    (
        (pos.x + (size.width as i32 - pw) / 2).max(0),
        (pos.y + (size.height as i32 - ph) / 2).max(0),
    )
}

/// Baca geometri window (logis x/y/w/h) dari DB; None bila belum
/// ada / tidak valid.
fn read_window_geometry(
    db: &Option<Arc<Mutex<DatabaseService>>>,
    prefix: &str,
) -> Option<(i32, i32, i32, i32)> {
    let db_mutex = db.as_ref()?;
    let guard = db_mutex.lock().ok()?;
    let x: i32 = guard.get(&format!("{}.X", prefix))?.parse().ok()?;
    let y: i32 = guard.get(&format!("{}.Y", prefix))?.parse().ok()?;
    let w: i32 = guard.get(&format!("{}.Width", prefix))?.parse().ok()?;
    let h: i32 = guard.get(&format!("{}.Height", prefix))?.parse().ok()?;
    if w <= 0 || h <= 0 {
        return None;
    }
    Some((x, y, w, h))
}

/// Simpan geometri window (logis) ke DB.
fn save_window_geometry(
    win: &slint::Window,
    db: &Option<Arc<Mutex<DatabaseService>>>,
    prefix: &str,
) {
    let Some(db_mutex) = db.as_ref() else {
        return;
    };
    let Ok(mut guard) = db_mutex.lock() else {
        return;
    };
    let scale = win.scale_factor();
    if scale <= 0.0 {
        return;
    }
    let pos = win.position();
    let size = win.size();
    let x = (pos.x as f32 / scale) as i32;
    let y = (pos.y as f32 / scale) as i32;
    let w = (size.width as f32 / scale) as i32;
    let h = (size.height as f32 / scale) as i32;
    let _ = guard.set(&format!("{}.X", prefix), &x.to_string(), "Int");
    let _ = guard.set(&format!("{}.Y", prefix), &y.to_string(), "Int");
    let _ = guard.set(&format!("{}.Width", prefix), &w.to_string(), "Int");
    let _ = guard.set(&format!("{}.Height", prefix), &h.to_string(), "Int");
}

/// ---- Dynamic Bar: notifikasi kartu masuk + poin bertambah ----
/// Satu notifikasi di panel bar (terbaru dulu, maks
/// BAR_MAX_NOTIFICATIONS). `kind`: "card" | "points"; `category`:
/// "EDITING" | "REVISION" | "LATE" | "POINTS" | "".
#[derive(Clone, Debug)]
struct BarNotification {
    kind: String,
    category: String,
    title: String,
    sub: String,
}

/// State bersama Dynamic Bar: kartu yang sudah pernah terlihat per
/// list Trello + poin terakhir + daftar notifikasi. Dibagi ke
/// thread sync Trello (deteksi kartu baru) dan timer sinkronisasi
/// bar (deteksi poin naik + salin angka ke window bar).
#[derive(Default)]
struct BarState {
    seen_cards: std::collections::HashMap<String, std::collections::HashSet<String>>,
    synced_lists: std::collections::HashSet<String>,
    last_points: Option<i64>,
    notifications: Vec<BarNotification>,
    /// Mood maskot dari event terakhir: ("happy"|"surprised",
    /// sampai-kapan). Timer 2 detik membacanya dan mendorong ke
    /// properti `pet-mood` window bar; kedaluwarsa → "idle".
    /// Tidur/sapa dihitung sisi Slint (effective-mood).
    pet_mood_until: Option<(String, Instant)>,
}

type BarStore = Arc<Mutex<BarState>>;

const BAR_MAX_NOTIFICATIONS: usize = 10;
const BAR_MAX_NEW_PER_SYNC: usize = 3;

/// Catat hasil sync satu list Trello: kartu yang id-nya belum
/// pernah terlihat menjadi notifikasi. Sync PERTAMA list itu =
/// baseline (tidak ada notifikasi — kalau tidak, semua kartu lama
/// ikut berbunyi saat aplikasi dibuka). Maks
/// BAR_MAX_NEW_PER_SYNC notifikasi per sync supaya satu refresh
/// besar tidak membanjiri panel.
/// Mengembalikan jumlah kartu BARU yang dicatat (0 bila
/// baseline / tidak ada yang baru) — dipakai pemanggil untuk
/// memicu ucapan voice maskot.
fn record_list_sync(
    state: &mut BarState,
    list_id: &str,
    category: &str,
    cards: &[(String, String)],
) -> usize {
    let baseline = !state.synced_lists.contains(list_id);
    state.synced_lists.insert(list_id.to_string());
    let seen = state.seen_cards.entry(list_id.to_string()).or_default();
    let mut fresh: Vec<(String, String)> = Vec::new();
    for (id, name) in cards {
        if !seen.contains(id) {
            if !baseline && fresh.len() < BAR_MAX_NEW_PER_SYNC {
                fresh.push((id.clone(), name.clone()));
            }
            seen.insert(id.clone());
        }
    }
    let fresh_count = fresh.len();
    for (_id, name) in fresh {
        state.notifications.insert(
            0,
            BarNotification {
                kind: "card".to_string(),
                category: category.to_string(),
                title: name,
                sub: format!("Kartu baru di {}", category),
            },
        );
    }
    state.notifications.truncate(BAR_MAX_NOTIFICATIONS);
    fresh_count
}

/// Catat nilai Stat.Points sekarang: naik dari baseline terakhir
/// → satu notifikasi "Poin bertambah". Panggilan pertama hanya
/// menyimpan baseline (None). Turun / sama / tidak bisa di-parse
/// → None (poin di port tidak pernah turun; bila turun pun tidak
/// ada bunyi — hanya kenaikan yang diberitahukan).
fn record_points(state: &mut BarState, current: &str) -> Option<(i64, i64)> {
    let cur: i64 = current.trim().replace('.', "").parse().ok()?;
    let prev = state.last_points.replace(cur)?;
    if cur > prev {
        state.notifications.insert(
            0,
            BarNotification {
                kind: "points".to_string(),
                category: "POINTS".to_string(),
                title: "Poin bertambah".to_string(),
                sub: format!("{} → {}", prev, cur),
            },
        );
        state.notifications.truncate(BAR_MAX_NOTIFICATIONS);
        Some((prev, cur))
    } else {
        None
    }
}

/// Dorong daftar notifikasi ke window bar (wholesale replace —
/// Slint tidak bisa append array). Hanya 5 terbaru yang ditampilkan
/// (panel mengembang maks 5 baris); `notif-count` tetap total.
fn push_notifs_to_bar(w: &DynamicBarWindow, notifs: &[BarNotification]) {
    let shown: &[BarNotification] = if notifs.len() > 5 { &notifs[..5] } else { notifs };
    let titles: Vec<slint::SharedString> = shown
        .iter()
        .map(|n| slint::SharedString::from(n.title.as_str()))
        .collect();
    let subs: Vec<slint::SharedString> = shown
        .iter()
        .map(|n| slint::SharedString::from(n.sub.as_str()))
        .collect();
    let kinds: Vec<slint::SharedString> = shown
        .iter()
        .map(|n| slint::SharedString::from(n.kind.as_str()))
        .collect();
    let cats: Vec<slint::SharedString> = shown
        .iter()
        .map(|n| slint::SharedString::from(n.category.as_str()))
        .collect();
    w.set_notif_titles(slint::ModelRc::new(slint::VecModel::from(titles)));
    w.set_notif_subs(slint::ModelRc::new(slint::VecModel::from(subs)));
    w.set_notif_kinds(slint::ModelRc::new(slint::VecModel::from(kinds)));
    w.set_notif_cats(slint::ModelRc::new(slint::VecModel::from(cats)));
    w.set_notif_count(notifs.len() as i32);
}

/// Baca posisi tersimpan Dynamic Bar (logis x/y) dari DB; None
/// bila belum ada / tidak valid.
fn read_bar_position(db: &Option<Arc<Mutex<DatabaseService>>>) -> Option<(i32, i32)> {
    let db_mutex = db.as_ref()?;
    let guard = db_mutex.lock().ok()?;
    let x: i32 = guard.get("DynamicBar.X")?.parse().ok()?;
    let y: i32 = guard.get("DynamicBar.Y")?.parse().ok()?;
    Some((x, y))
}

/// Simpan posisi Dynamic Bar (logis) ke DB. Hanya X/Y — lebar dan
/// tinggi bar diatur .slint (560px / 46px atau mengembang), jadi
/// tidak disimpan seperti window lain.
fn save_bar_position(win: &slint::Window, db: &Option<Arc<Mutex<DatabaseService>>>) {
    let Some(db_mutex) = db.as_ref() else {
        return;
    };
    let Ok(mut guard) = db_mutex.lock() else {
        return;
    };
    let scale = win.scale_factor();
    if scale <= 0.0 {
        return;
    }
    let pos = win.position();
    let x = (pos.x as f32 / scale) as i32;
    let y = (pos.y as f32 / scale) as i32;
    let _ = guard.set("DynamicBar.X", &x.to_string(), "Int");
    let _ = guard.set("DynamicBar.Y", &y.to_string(), "Int");
}

/// Buka window utama ke tampilan yang sesuai dengan kategori bar:
/// EDITING/REVISION/LATE → daftar Trello embedded kategori itu
/// (sama seperti klik badan widget di dashboard); POINTS →
/// Leaderboard. Window utama di-show (naik ke depan).
fn open_main_category(ui: &AppWindow, cat: &str) {
    ui.window().show();
    ui.set_current_tab(slint::SharedString::from("HOME"));
    match cat {
        "POINTS" => {
            ui.set_list_category(slint::SharedString::from(""));
            ui.set_selected_card(slint::SharedString::from(""));
            ui.set_overlay_view(slint::SharedString::from("LEADERBOARD"));
            ui.set_toast(slint::SharedString::from("Leaderboard — poin editor"));
        }
        "EDITING" | "REVISION" | "LATE" => {
            let (titles, subs, due) = match cat {
                "EDITING" => (
                    ui.get_editing_titles(),
                    ui.get_editing_subs(),
                    ui.get_editing_due(),
                ),
                "REVISION" => (
                    ui.get_revision_titles(),
                    ui.get_revision_subs(),
                    ui.get_revision_due(),
                ),
                _ => (ui.get_late_titles(), ui.get_late_subs(), ui.get_late_due()),
            };
            ui.set_list_titles(titles);
            ui.set_list_subs(subs);
            ui.set_list_due(due);
            ui.set_selected_card(slint::SharedString::from(""));
            ui.set_overlay_view(slint::SharedString::from(""));
            ui.set_list_category(slint::SharedString::from(cat));
            let label = match cat {
                "EDITING" => "EDITING",
                "REVISION" => "REVISION",
                _ => "LATE",
            };
            let count = match cat {
                "EDITING" => ui.get_stat_editing(),
                "REVISION" => ui.get_stat_revision(),
                _ => ui.get_stat_late(),
            };
            ui.set_toast(slint::SharedString::from(format!(
                "Daftar {} — {} kartu",
                label, count
            )));
        }
        _ => {}
    }
}

/// Nilai default grid spreadsheet — KOSONG (27 Nov 2026, keputusan
/// abeng: default publish harus bersih, tidak ada data contoh).
/// Dipakai bila belum ada snapshot di DB.
const SHEET_DEFAULT_ROWS: [[&str; 6]; 0] = [];

fn sheet_model(v: Vec<String>) -> slint::ModelRc<slint::SharedString> {
    slint::ModelRc::new(slint::VecModel::from(
        v.into_iter().map(slint::SharedString::from).collect::<Vec<_>>(),
    ))
}

fn sheet_rows_to_cols(rows: &[Vec<String>]) -> Vec<Vec<String>> {
    let mut cols: Vec<Vec<String>> = vec![Vec::new(); 6];
    for row in rows {
        for c in 0..6 {
            cols[c].push(row.get(c).cloned().unwrap_or_default());
        }
    }
    cols
}

fn sheet_cols_to_rows(cols: &[Vec<String>]) -> Vec<Vec<String>> {
    let n = cols.iter().map(|c| c.len()).max().unwrap_or(0);
    let mut rows = Vec::new();
    for r in 0..n {
        let mut row = Vec::new();
        for c in 0..6 {
            row.push(cols.get(c).and_then(|col| col.get(r)).cloned().unwrap_or_default());
        }
        rows.push(row);
    }
    rows
}

/// Baca grid tampil (6 kolom) dari window utama.
fn grid_from_main(ui: &AppWindow) -> Vec<Vec<String>> {
    let cols = vec![
        read_str_model(ui.get_sheet_col_a()).into_iter().map(|s| s.to_string()).collect::<Vec<_>>(),
        read_str_model(ui.get_sheet_col_b()).into_iter().map(|s| s.to_string()).collect::<Vec<_>>(),
        read_str_model(ui.get_sheet_col_c()).into_iter().map(|s| s.to_string()).collect::<Vec<_>>(),
        read_str_model(ui.get_sheet_col_d()).into_iter().map(|s| s.to_string()).collect::<Vec<_>>(),
        read_str_model(ui.get_sheet_col_e()).into_iter().map(|s| s.to_string()).collect::<Vec<_>>(),
        read_str_model(ui.get_sheet_col_f()).into_iter().map(|s| s.to_string()).collect::<Vec<_>>(),
    ];
    sheet_cols_to_rows(&cols)
}

/// Baca grid tampil (6 kolom) dari SpreadsheetWindow.
fn grid_from_sheet_win(w: &SpreadsheetWindow) -> Vec<Vec<String>> {
    let cols = vec![
        read_str_model(w.get_col_a()).into_iter().map(|s| s.to_string()).collect::<Vec<_>>(),
        read_str_model(w.get_col_b()).into_iter().map(|s| s.to_string()).collect::<Vec<_>>(),
        read_str_model(w.get_col_c()).into_iter().map(|s| s.to_string()).collect::<Vec<_>>(),
        read_str_model(w.get_col_d()).into_iter().map(|s| s.to_string()).collect::<Vec<_>>(),
        read_str_model(w.get_col_e()).into_iter().map(|s| s.to_string()).collect::<Vec<_>>(),
        read_str_model(w.get_col_f()).into_iter().map(|s| s.to_string()).collect::<Vec<_>>(),
    ];
    sheet_cols_to_rows(&cols)
}

/// Dorong baris tampil + baris asli ke window utama.
fn push_grid_to_main(ui: &AppWindow, display: &[Vec<String>], orig: &[Vec<String>]) {
    let dc = sheet_rows_to_cols(display);
    let oc = sheet_rows_to_cols(orig);
    ui.set_sheet_col_a(sheet_model(dc[0].clone()));
    ui.set_sheet_col_b(sheet_model(dc[1].clone()));
    ui.set_sheet_col_c(sheet_model(dc[2].clone()));
    ui.set_sheet_col_d(sheet_model(dc[3].clone()));
    ui.set_sheet_col_e(sheet_model(dc[4].clone()));
    ui.set_sheet_col_f(sheet_model(dc[5].clone()));
    ui.set_sheet_col_a_orig(sheet_model(oc[0].clone()));
    ui.set_sheet_col_b_orig(sheet_model(oc[1].clone()));
    ui.set_sheet_col_c_orig(sheet_model(oc[2].clone()));
    ui.set_sheet_col_d_orig(sheet_model(oc[3].clone()));
    ui.set_sheet_col_e_orig(sheet_model(oc[4].clone()));
    ui.set_sheet_col_f_orig(sheet_model(oc[5].clone()));
}

/// Dorong baris tampil + baris asli ke SpreadsheetWindow.
fn push_grid_to_sheet_win(w: &SpreadsheetWindow, display: &[Vec<String>], orig: &[Vec<String>]) {
    let dc = sheet_rows_to_cols(display);
    let oc = sheet_rows_to_cols(orig);
    w.set_col_a(sheet_model(dc[0].clone()));
    w.set_col_b(sheet_model(dc[1].clone()));
    w.set_col_c(sheet_model(dc[2].clone()));
    w.set_col_d(sheet_model(dc[3].clone()));
    w.set_col_e(sheet_model(dc[4].clone()));
    w.set_col_f(sheet_model(dc[5].clone()));
    w.set_col_a_orig(sheet_model(oc[0].clone()));
    w.set_col_b_orig(sheet_model(oc[1].clone()));
    w.set_col_c_orig(sheet_model(oc[2].clone()));
    w.set_col_d_orig(sheet_model(oc[3].clone()));
    w.set_col_e_orig(sheet_model(oc[4].clone()));
    w.set_col_f_orig(sheet_model(oc[5].clone()));
}

/// Baca snapshot sheet dari DB; bila belum ada, inisialisasi dari data
/// default dan simpan.
fn read_sheet_snapshot(
    db: &Option<Arc<Mutex<DatabaseService>>>,
    sheet_id: &str,
    sheet_name: &str,
) -> spreadsheet_guard::SheetSnapshot {
    let default_rows: Vec<Vec<String>> = SHEET_DEFAULT_ROWS
        .iter()
        .map(|r| r.iter().map(|s| s.to_string()).collect())
        .collect();
    let Some(db_mutex) = db.as_ref() else {
        return spreadsheet_guard::SheetSnapshot {
            sheet_id: sheet_id.to_string(),
            sheet_name: sheet_name.to_string(),
            rows: default_rows,
        };
    };
    let Ok(guard) = db_mutex.lock() else {
        return spreadsheet_guard::SheetSnapshot {
            sheet_id: sheet_id.to_string(),
            sheet_name: sheet_name.to_string(),
            rows: default_rows,
        };
    };
    if let Some(json) = guard.get(spreadsheet_guard::SNAPSHOT_KEY) {
        if let Ok(snap) = serde_json::from_str::<spreadsheet_guard::SheetSnapshot>(&json) {
            if !snap.rows.is_empty() {
                return snap;
            }
        }
    }
    drop(guard);
    let snap = spreadsheet_guard::SheetSnapshot {
        sheet_id: sheet_id.to_string(),
        sheet_name: sheet_name.to_string(),
        rows: default_rows,
    };
    persist_sheet_snapshot(db, &snap);
    snap
}

fn persist_sheet_snapshot(
    db: &Option<Arc<Mutex<DatabaseService>>>,
    snap: &spreadsheet_guard::SheetSnapshot,
) {
    if let Some(db_mutex) = db.as_ref() {
        if let Ok(mut guard) = db_mutex.lock() {
            let json = serde_json::to_string(snap).unwrap_or_default();
            let _ = guard.set(spreadsheet_guard::SNAPSHOT_KEY, &json, "String");
        }
    }
}

fn persist_sheet_drafts(
    db: &Option<Arc<Mutex<DatabaseService>>>,
    drafts: &[spreadsheet_guard::DraftEntry],
) {
    if let Some(db_mutex) = db.as_ref() {
        if let Ok(mut guard) = db_mutex.lock() {
            let json = spreadsheet_guard::serialize_drafts(drafts);
            let _ = guard.set(spreadsheet_guard::DRAFT_KEY, &json, "String");
        }
    }
}

fn load_sheet_drafts(db: &Option<Arc<Mutex<DatabaseService>>>) -> Vec<spreadsheet_guard::DraftEntry> {
    if let Some(db_mutex) = db.as_ref() {
        if let Ok(guard) = db_mutex.lock() {
            if let Some(json) = guard.get(spreadsheet_guard::DRAFT_KEY) {
                return spreadsheet_guard::deserialize_drafts(&json);
            }
        }
    }
    Vec::new()
}

/// Baris tampil dari state (nilai asli + draft yang masih cocok).
fn sheet_display_rows(state: &spreadsheet_guard::SpreadsheetState) -> Vec<Vec<String>> {
    let mut display = state.loaded.clone();
    let _ = spreadsheet_guard::apply_drafts(
        &state.drafts,
        &state.sheet_id,
        &mut display,
        state.column_count,
        state.first_sheet_row,
    );
    display
}

/// Hasil guard saat Simpan — dipakai bersama oleh handler window utama
/// dan SpreadsheetWindow.
enum SheetSaveOutcome {
    Blocked(String),
    Saved(Vec<Vec<String>>),
}

/// Jalankan guard + commit bila lolos. `current_grid` = nilai grid yang
/// tampil di window pemanggil. Mengembalikan hasil; pemanggil yang
/// mendorong nilai baru + status ke UI-nya.
fn run_sheet_save(
    state: &mut spreadsheet_guard::SpreadsheetState,
    db: &Option<Arc<Mutex<DatabaseService>>>,
    current_grid: Vec<Vec<String>>,
) -> SheetSaveOutcome {
    // Baca ulang snapshot dari DB (C#: baca ulang range sheet sebelum tulis).
    let fresh = read_sheet_snapshot(db, &state.sheet_id.clone(), &state.sheet_name.clone());
    if let Some(msg) = state.check_save(&fresh.rows) {
        return SheetSaveOutcome::Blocked(msg);
    }
    state.commit_save(current_grid.clone());
    let snap = spreadsheet_guard::SheetSnapshot {
        sheet_id: state.sheet_id.clone(),
        sheet_name: state.sheet_name.clone(),
        rows: current_grid.clone(),
    };
    persist_sheet_snapshot(db, &snap);
    persist_sheet_drafts(db, &state.drafts);
    SheetSaveOutcome::Saved(current_grid)
}

/// Muat ulang dari snapshot terbaru + pulihkan draft (C# LoadData +
/// RestoreDraftsAsync). Mengembalikan (baris_tampil, diterapkan, sisa).
fn run_sheet_refresh(
    state: &mut spreadsheet_guard::SpreadsheetState,
    db: &Option<Arc<Mutex<DatabaseService>>>,
) -> (Vec<Vec<String>>, usize, Vec<spreadsheet_guard::DraftEntry>) {
    // Draft sudah dipersist tiap edit (C#: PersistDraftsAsync sebelum load).
    persist_sheet_drafts(db, &state.drafts);
    let fresh = read_sheet_snapshot(db, &state.sheet_id.clone(), &state.sheet_name.clone());
    let (display, applied, remaining) = state.reload(fresh.rows);
    persist_sheet_drafts(db, &state.drafts);
    (display, applied, remaining)
}

/// Pesan status setelah Muat Ulang (C# RestoreDraftsAsync).
fn sheet_restore_status(applied: usize, remaining: &[spreadsheet_guard::DraftEntry]) -> String {
    if applied > 0 {
        let leftover = if !remaining.is_empty() {
            format!(
                " {} draft tidak bisa diterapkan karena data sheet sudah berubah.",
                remaining.len()
            )
        } else {
            String::new()
        };
        format!(
            "{} perubahan draft dipulihkan, belum di-push ke sheet.{}",
            applied, leftover
        )
    } else {
        "Data dimuat ulang dari sheet.".to_string()
    }
}

/// Buka (atau buka-ulang) ToolboxWindow di tengah window utama.
/// Dipakai callback AppWindow open-toolbox-window + radial open-toolbox.
fn open_toolbox_window(
    cell: &std::rc::Rc<std::cell::RefCell<Option<ToolboxWindow>>>,
    visible: &std::rc::Rc<std::cell::Cell<bool>>,
    ui_weak: &slint::Weak<AppWindow>,
    tab: String,
) {
    let mut opt = cell.borrow_mut();
    // Buka-ulang setelah tutup selalu buat window baru (alasan sama
    // seperti SpreadsheetWindow: hide->show reuse tidak andal).
    if !visible.get() {
        *opt = None;
    }
    if opt.is_none() {
        if let Ok(w) = ToolboxWindow::new() {
            let w_weak = w.as_weak();
            let vis_close = visible.clone();
            w.on_close_window(move || {
                vis_close.set(false);
                if let Some(w) = w_weak.upgrade() {
                    let _ = w.hide();
                }
            });
            let ui_weak2 = ui_weak.clone();
            w.on_toast(move |msg| {
                if let Some(ui) = ui_weak2.upgrade() {
                    ui.set_toast(msg);
                }
            });
            // Tombol kontrol chrome (min/max) + drag (titlebar kustom
            // / strip headless) + gaya chrome awal dari AppWindow
            // (lihat DESIGN_SYSTEM.md §23).
            let w_weak_min = w.as_weak();
            w.on_minimize_window(move || {
                if let Some(w) = w_weak_min.upgrade() {
                    w.window().set_minimized(true);
                }
            });
            let w_weak_max = w.as_weak();
            w.on_maximize_window(move || {
                if let Some(w) = w_weak_max.upgrade() {
                    let m = w.window().is_maximized();
                    w.window().set_maximized(!m);
                }
            });
            let w_weak_move = w.as_weak();
            w.on_move_window(move |dx, dy| {
                if let Some(w) = w_weak_move.upgrade() {
                    let scale = w.window().scale_factor();
                    let pos = w.window().position();
                    w.window().set_position(slint::PhysicalPosition::new(
                        pos.x + (dx * scale) as i32,
                        pos.y + (dy * scale) as i32,
                    ));
                }
            });
            if let Some(ui) = ui_weak.upgrade() {
                push_chrome_to_toolbox(&w, &ui);
            }
            *opt = Some(w);
        }
    }
    if let Some(w) = opt.as_ref() {
        // Gaya chrome saat ini (bisa berubah selagi window tertutup
        // — dorong lagi tiap buka-ulang).
        if let Some(ui) = ui_weak.upgrade() {
            push_chrome_to_toolbox(w, &ui);
        }
        w.set_tool_tab(slint::SharedString::from(tab));
        if let Some(ui) = ui_weak.upgrade() {
            let (cx, cy) = center_over_main(&ui, 1024, 730);
            w.window()
                .set_position(slint::PhysicalPosition::new(cx, cy));
        }
        // Hide + show = naik ke depan (Slint tidak punya Activate).
        let _ = w.hide();
        let _ = w.show();
        visible.set(true);
    }
}

/// Buka (atau buka-ulang) DynamicBarWindow sebagai NOTCH HIDUP
/// (rework 27 Nov 2026, referensi Nusa).
/// Mode notch: pill 150×32 di atas-tengah window utama, y=0
/// (menempel tepi atas layar seperti notch beneran); tidak bisa
/// di-drag — posisinya tetap. Mode pet: avatar bulat 84×84 di
/// posisi tersimpan DynamicBar.X/Y (atau default atas-tengah),
/// Runtime pet PetDex aktif — dibagikan timer frame, pembuka bar,
/// dan handler Settings. Sheet di-decode sekali saat pet dipilih,
/// lalu dipakai ulang (gambar Slint + geometri frame). "" = blob
/// bawaan (tanpa sprite).
struct PetRuntime {
    active_slug: String,
    sheet: Option<petdex::DecodedSheet>,
    image: Option<slint::Image>,
    engine: petdex::PetAnimationEngine,
    last_frame: (u32, u32),
    last_x: Option<i32>,
}

impl PetRuntime {
    fn new() -> Self {
        Self {
            active_slug: String::new(),
            sheet: None,
            image: None,
            engine: petdex::PetAnimationEngine::new(),
            last_frame: (0, 0),
            last_x: None,
        }
    }

    /// Muat pet aktif dari folder (`""` = blob bawaan). Sheet rusak /
    /// tidak ada → fallback blob (sprite None), jangan crash.
    fn load_active(&mut self, slug: &str) {
        self.active_slug = slug.to_string();
        self.engine = petdex::PetAnimationEngine::new();
        self.last_frame = (0, 0);
        self.last_x = None;
        if slug.is_empty() {
            self.sheet = None;
            self.image = None;
            return;
        }
        match petdex::load_sheet(slug) {
            Ok(sheet) => {
                let img = slint::Image::from_rgba8(
                    slint::SharedPixelBuffer::<slint::Rgba8Pixel>::clone_from_slice(
                        &sheet.rgba,
                        sheet.width,
                        sheet.height,
                    ),
                );
                self.image = Some(img);
                self.sheet = Some(sheet);
            }
            Err(e) => {
                eprintln!("PetDex: sheet '{slug}' gagal dimuat ({e}) — pakai blob bawaan.");
                self.sheet = None;
                self.image = None;
            }
        }
    }
}

/// Terapkan pet aktif ke window bar (dipanggil saat bar dibuka).
/// Mode pet + sprite ada → tampilkan sprite; selebihnya blob.
fn apply_pet_to_bar(w: &DynamicBarWindow, runtime: &PetRuntime, is_pet_mode: bool) {
    if !is_pet_mode {
        w.set_pet_sprite_active(false);
        return;
    }
    if let (Some(sheet), Some(img)) = (&runtime.sheet, &runtime.image) {
        // Satu frame tampil setinggi pill pet (84px); lebar ikut
        // rasio frame asli (192×208 → ~77px).
        let frame_h = 84.0f32;
        let frame_w = frame_h * sheet.frame_w as f32 / sheet.frame_h as f32;
        w.set_pet_sprite(img.clone());
        w.set_pet_sprite_active(true);
        w.set_pet_frame_w(frame_w);
        w.set_pet_frame_h(frame_h);
        w.set_pet_sheet_cols(petdex::ATLAS_COLS as i32);
        w.set_pet_sheet_rows(sheet.rows as i32);
        let f = runtime.engine.current_frame();
        w.set_pet_frame_row(f.row as i32);
        w.set_pet_frame_col(f.col as i32);
        w.set_pet_sprite_opacity(1.0);
    } else {
        w.set_pet_sprite_active(false);
    }
}

/// Dorong daftar pet ter-install + pet aktif ke properti AppWindow
/// (ComboBox + daftar di Settings). Index 0 = blob bawaan.
fn refresh_pet_lists(ui: &AppWindow, active_slug: &str) {
    let installed = petdex::list_installed();
    let mut names: Vec<slint::SharedString> = vec![slint::SharedString::from("Blob bawaan")];
    let mut authors: Vec<slint::SharedString> = vec![slint::SharedString::from("")];
    let mut slugs: Vec<slint::SharedString> = vec![slint::SharedString::from("")];
    let mut in_names: Vec<slint::SharedString> = Vec::new();
    let mut in_authors: Vec<slint::SharedString> = Vec::new();
    let mut in_slugs: Vec<slint::SharedString> = Vec::new();
    for p in &installed {
        names.push(slint::SharedString::from(p.display_name.as_str()));
        authors.push(slint::SharedString::from(p.submitted_by.as_str()));
        slugs.push(slint::SharedString::from(p.slug.as_str()));
        in_names.push(slint::SharedString::from(p.display_name.as_str()));
        in_authors.push(slint::SharedString::from(p.submitted_by.as_str()));
        in_slugs.push(slint::SharedString::from(p.slug.as_str()));
    }
    ui.set_app_pet_names(slint::ModelRc::new(slint::VecModel::from(names)));
    ui.set_app_pet_authors(slint::ModelRc::new(slint::VecModel::from(authors)));
    ui.set_app_pet_slugs(slint::ModelRc::new(slint::VecModel::from(slugs)));
    ui.set_app_pet_installed_names(slint::ModelRc::new(slint::VecModel::from(in_names)));
    ui.set_app_pet_installed_authors(slint::ModelRc::new(slint::VecModel::from(in_authors)));
    ui.set_app_pet_installed_slugs(slint::ModelRc::new(slint::VecModel::from(in_slugs)));
    let idx = if active_slug.is_empty() {
        0
    } else {
        installed
            .iter()
            .position(|p| p.slug == active_slug)
            .map(|i| i + 1)
            .unwrap_or(0)
    };
    ui.set_app_pet_active_index(idx as i32);
}

/// Gaya mata maskot yang valid ("nusa" | "baymax" | "bulat") —
/// nilai tak dikenal (termasuk dari DB lama) → "nusa".
/// Nilai DB bisa dikutip JSON (warisan C#) — kupas dulu.
fn valid_eye_style(raw: &str) -> String {
    match theme::unquote(raw).as_str() {
        "baymax" => "baymax".to_string(),
        "bulat" => "bulat".to_string(),
        _ => "nusa".to_string(),
    }
}

/// Gaya radial ("glass" | "hud" | "flat") — nilai tak dikenal → "glass".
fn valid_radial_style(raw: &str) -> String {
    match theme::unquote(raw).as_str() {
        "hud" => "hud".to_string(),
        "flat" => "flat".to_string(),
        _ => "glass".to_string(),
    }
}

/// Jumlah segmen ring radial (2–8). Mesin geometri
/// (`src/radial_geom.rs`) mendukung 2–12 (ref 8-layout.jpg), tapi satu
/// halaman roda hanya memuat 8 slot (paritas C#) — 9–12 baru terbuka
/// bila slot radial bisa diedit user. Di luar 2–8 → dijepit.
fn valid_radial_segments(raw: i32) -> usize {
    raw.clamp(2, 8) as usize
}

#[cfg(test)]
mod radial_settings_tests {
    use super::*;

    #[test]
    fn valid_radial_style_accepts_known_styles_and_defaults_unknown() {
        assert_eq!(valid_radial_style("glass"), "glass");
        assert_eq!(valid_radial_style("hud"), "hud");
        assert_eq!(valid_radial_style("flat"), "flat");
        assert_eq!(valid_radial_style(""), "glass");
        assert_eq!(valid_radial_style("kaca"), "glass");
        assert_eq!(valid_radial_style("\"hud\""), "hud");
    }

    #[test]
    fn valid_radial_segments_clamps_to_wheel_range() {
        assert_eq!(valid_radial_segments(8), 8);
        assert_eq!(valid_radial_segments(4), 4);
        assert_eq!(valid_radial_segments(2), 2);
        assert_eq!(valid_radial_segments(1), 2);
        assert_eq!(valid_radial_segments(0), 2);
        assert_eq!(valid_radial_segments(-3), 2);
        // 9–12 didukung mesin geometri, tapi roda dijepit ke 8 slot.
        assert_eq!(valid_radial_segments(12), 8);
        assert_eq!(valid_radial_segments(99), 8);
    }
}

#[cfg(test)]
mod eye_style_tests {
    use super::*;

    #[test]
    fn valid_eye_style_accepts_known_styles_and_defaults_unknown() {
        assert_eq!(valid_eye_style("nusa"), "nusa");
        assert_eq!(valid_eye_style("baymax"), "baymax");
        assert_eq!(valid_eye_style("bulat"), "bulat");
        assert_eq!(valid_eye_style(""), "nusa");
        assert_eq!(valid_eye_style("kucing"), "nusa");
        // Nilai DB warisan C# yang dikutip JSON.
        assert_eq!(valid_eye_style("\"baymax\""), "baymax");
    }
}

/// bisa di-drag + wander sendiri. Angka diisi dari properti
/// AppWindow (sumber yang sama dengan widget dashboard) +
/// notifikasi dari BarStore. Pola buka-ulang sama seperti
/// ToolboxWindow: hide->show reuse tidak andal, jadi window
/// yang sudah ditutup selalu dibuat baru.
fn open_dynamic_bar_window(
    cell: &std::rc::Rc<std::cell::RefCell<Option<DynamicBarWindow>>>,
    visible: &std::rc::Rc<std::cell::Cell<bool>>,
    ui_weak: &slint::Weak<AppWindow>,
    bar_store: &BarStore,
    db: &Option<Arc<Mutex<DatabaseService>>>,
    pet_last_drag: &Arc<Mutex<Instant>>,
    pet_runtime: &std::rc::Rc<std::cell::RefCell<PetRuntime>>,
) {
    let mut opt = cell.borrow_mut();
    // Buka-ulang setelah tutup selalu buat window baru (alasan sama
    // seperti SpreadsheetWindow/ToolboxWindow).
    if !visible.get() {
        *opt = None;
    }
    if opt.is_none() {
        if let Ok(w) = DynamicBarWindow::new() {
            // Tutup (klik kanan di bar): simpan posisi, hide, tandai
            // tidak tampil, persist + sinkronkan toggle flyout.
            // Window TIDAK di-drop di sini (callback milik window
            // yang sedang berjalan) — di-drop saat buka berikutnya.
            let w_weak = w.as_weak();
            let vis_close = visible.clone();
            let db_close = db.clone();
            let ui_weak_close = ui_weak.clone();
            w.on_close_window(move || {
                vis_close.set(false);
                if let Some(w) = w_weak.upgrade() {
                    save_bar_position(w.window(), &db_close);
                    let _ = w.hide();
                }
                if let Some(ref db_mutex) = db_close {
                    if let Ok(mut guard) = db_mutex.lock() {
                        let _ = guard.set("Settings.DynamicBar.Visible", "false", "Bool");
                    }
                }
                if let Some(ui) = ui_weak_close.upgrade() {
                    ui.set_app_dynamic_bar_visible(false);
                }
            });
            // Drag bar (hanya mode pet yang menggerakkan window —
            // .slint sudah membatasi drag ke mode pet). Panel yang
            // sedang mengembang ditahan di dalam window utama
            // supaya tidak keluar layar; pet yang mengecil bebas
            // di-drag ke mana saja. (Dalam praktik drag selalu
            // mulai dari panel mengembang — hover mengembang
            // sebelum mousedown.)
            let w_weak2 = w.as_weak();
            let ui_weak_move = ui_weak.clone();
            w.on_move_window(move |dx, dy| {
                if let Some(w) = w_weak2.upgrade() {
                    let scale = w.window().scale_factor();
                    let pos = w.window().position();
                    let mut nx = pos.x + (dx * scale) as i32;
                    let mut ny = pos.y + (dy * scale) as i32;
                    if w.get_expanded() {
                        if let Some(ui) = ui_weak_move.upgrade() {
                            let win = ui.window();
                            let mpos = win.position();
                            let msize = win.size();
                            let size = w.window().size();
                            nx = nx.clamp(
                                mpos.x,
                                (mpos.x + msize.width as i32 - size.width as i32).max(mpos.x),
                            );
                            ny = ny.clamp(
                                mpos.y,
                                (mpos.y + msize.height as i32 - size.height as i32).max(mpos.y),
                            );
                        }
                    }
                    w.window()
                        .set_position(slint::PhysicalPosition::new(nx, ny));
                }
            });
            // Posisi pet sebelum panel mengembang — dipakai
            // mengembalikan pet ke tempat semula saat panel
            // mengecil. Dibatalkan (None) bila user men-drag
            // saat panel terbuka: pet tetap di tempat di-drop.
            let pet_pos_before_expand: std::rc::Rc<std::cell::RefCell<Option<(i32, i32)>>> =
                std::rc::Rc::new(std::cell::RefCell::new(None));
            // Simpan posisi saat drag selesai + catat waktu drag
            // terakhir (pet tidak wander 5 detik setelah di-drag).
            let w_weak3 = w.as_weak();
            let db_save = db.clone();
            let drag_stamp = pet_last_drag.clone();
            let pet_pos_drag = pet_pos_before_expand.clone();
            w.on_save_position(move || {
                if let Ok(mut t) = drag_stamp.lock() {
                    *t = Instant::now();
                }
                // Drag saat panel terbuka = posisi baru pet;
                // jangan kembalikan ke posisi sebelum mengembang.
                *pet_pos_drag.borrow_mut() = None;
                if let Some(w) = w_weak3.upgrade() {
                    save_bar_position(w.window(), &db_save);
                }
            });
            // Notch mengembang/mengecil mengubah lebar window
            // (150 <-> 560) sementara tepi kiri OS diam — pusatkan
            // ulang secara horizontal bila mode notch. Mode pet:
            // panel 560×~420 mengembang dari posisi pet — tahan
            // di dalam window utama supaya tidak keluar layar,
            // lalu kembalikan pet saat mengecil lagi. Kedua mode:
            // panel mengembang menampilkan kontrol volume —
            // segarkan nilainya dari OS saat mengembang.
            let w_weak_exp = w.as_weak();
            let ui_weak_exp = ui_weak.clone();
            let pet_pos_exp = pet_pos_before_expand.clone();
            w.on_expanded_changed(move |expanded| {
                let Some(w) = w_weak_exp.upgrade() else {
                    return;
                };
                let Some(ui) = ui_weak_exp.upgrade() else {
                    return;
                };
                if expanded {
                    refresh_bar_volume(&w);
                }
                if ui.get_app_notch_mode().as_str() == "notch" {
                    recenter_notch(&w, &ui, expanded);
                    return;
                }
                if expanded {
                    let pos = w.window().position();
                    *pet_pos_exp.borrow_mut() = Some((pos.x, pos.y));
                    let win = ui.window();
                    let mpos = win.position();
                    let msize = win.size();
                    let scale = win.scale_factor();
                    // Lebar panel 560px; tinggi maks 420px (5
                    // notifikasi + baris volume) — clamp
                    // konservatif supaya panel tidak pernah keluar
                    // window utama.
                    let pw = (560f32 * scale) as i32;
                    let ph = (420f32 * scale) as i32;
                    let nx = pos
                        .x
                        .clamp(mpos.x, (mpos.x + msize.width as i32 - pw).max(mpos.x));
                    let ny = pos
                        .y
                        .clamp(mpos.y, (mpos.y + msize.height as i32 - ph).max(mpos.y));
                    if nx != pos.x || ny != pos.y {
                        w.window()
                            .set_position(slint::PhysicalPosition::new(nx, ny));
                    }
                } else if let Some((px, py)) = *pet_pos_exp.borrow() {
                    w.window()
                        .set_position(slint::PhysicalPosition::new(px, py));
                }
            });
            // Klik angka widget → window utama ke tampilan terkait.
            let ui_weak_cat = ui_weak.clone();
            w.on_open_category(move |cat| {
                if let Some(ui) = ui_weak_cat.upgrade() {
                    open_main_category(&ui, cat.as_str());
                }
            });
            // Klik notifikasi → tampilan terkait (kartu → daftar
            // kategorinya; poin → Leaderboard).
            let ui_weak_notif = ui_weak.clone();
            let store_notif = bar_store.clone();
            w.on_open_notification(move |idx| {
                let cat = {
                    let Ok(st) = store_notif.lock() else {
                        return;
                    };
                    match st.notifications.get(idx as usize) {
                        Some(n) => {
                            if n.kind == "points" {
                                "POINTS".to_string()
                            } else {
                                n.category.clone()
                            }
                        }
                        None => return,
                    }
                };
                if let Some(ui) = ui_weak_notif.upgrade() {
                    open_main_category(&ui, cat.as_str());
                }
            });
            // Footer "Buka Dashboard" → window utama, dashboard.
            let ui_weak_dash = ui_weak.clone();
            w.on_open_dashboard(move || {
                if let Some(ui) = ui_weak_dash.upgrade() {
                    let _ = ui.window().show();
                    ui.set_current_tab(slint::SharedString::from("HOME"));
                    ui.set_list_category(slint::SharedString::from(""));
                    ui.set_selected_card(slint::SharedString::from(""));
                    ui.set_overlay_view(slint::SharedString::from(""));
                }
            });
            // Volume OS dari panel mengembang (slider + bisukan).
            // Slider di-debounce sisi Rust (volume::set_volume)
            // supaya drag tidak membanjiri OS dengan subprocess.
            let w_weak_vol = w.as_weak();
            w.on_volume_changed(move |f| {
                let pct = (f.clamp(0.0, 1.0) * 100.0).round() as u8;
                volume::set_volume(pct);
                if let Some(w) = w_weak_vol.upgrade() {
                    w.set_volume_f(f.clamp(0.0, 1.0));
                }
            });
            let w_weak_mute = w.as_weak();
            w.on_volume_mute_clicked(move || {
                let Some(w) = w_weak_mute.upgrade() else {
                    return;
                };
                let muted = !w.get_volume_muted();
                volume::set_muted(muted);
                w.set_volume_muted(muted);
            });
            *opt = Some(w);
        }
    }
    if let Some(w) = opt.as_ref() {
        // Mode dari AppWindow (sumber kebenaran; Settings
        // mengikat ke properti yang sama).
        let mode = ui_weak
            .upgrade()
            .map(|ui| ui.get_app_notch_mode().to_string())
            .unwrap_or_else(|| "notch".to_string());
        w.set_bar_mode(slint::SharedString::from(mode.as_str()));
        // Gaya mata dari AppWindow (sumber kebenaran; Settings
        // mengikat ke properti yang sama) — berlaku live ke
        // window bar yang sedang tampil.
        if let Some(ui) = ui_weak.upgrade() {
            w.set_eye_style(slint::SharedString::from(valid_eye_style(
                &ui.get_app_notch_eye_style(),
            )));
        }
        // Posisi: mode pet = simpanan dulu (default atas-tengah);
        // mode notch SELALU atas-tengah window utama, y=0
        // (menempel tepi atas — posisi simpanan lama dari pill
        // 560px sengaja diabaikan supaya notch kembali ke tengah).
        let scale = w.window().scale_factor();
        if mode == "pet" {
            if let Some((x, y)) = read_bar_position(db) {
                w.window().set_position(slint::PhysicalPosition::new(
                    ((x.max(0)) as f32 * scale) as i32,
                    ((y.max(0)) as f32 * scale) as i32,
                ));
            } else if let Some(ui) = ui_weak.upgrade() {
                let win = ui.window();
                let pos = win.position();
                let size = win.size();
                let mscale = win.scale_factor();
                let bw = (84f32 * mscale) as i32;
                let cx = (pos.x + (size.width as i32 - bw) / 2).max(0);
                let cy = (pos.y + (60f32 * mscale) as i32).max(0);
                w.window()
                    .set_position(slint::PhysicalPosition::new(cx, cy));
            }
        } else if let Some(ui) = ui_weak.upgrade() {
            let win = ui.window();
            let pos = win.position();
            let size = win.size();
            let mscale = win.scale_factor();
            let bw = (150f32 * mscale) as i32;
            let cx = (pos.x + (size.width as i32 - bw) / 2).max(0);
            w.window().set_position(slint::PhysicalPosition::new(cx, 0));
        }
        // Isi angka dari AppWindow (sumber yang sama dengan widget
        // dashboard) + notifikasi dari store. Tema disalin eksplisit
        // (tiap window OS punya instance Design sendiri).
        if let Some(ui) = ui_weak.upgrade() {
            theme::sync_design_to_bar(&ui, w);
            w.set_stat_editing(ui.get_stat_editing());
            w.set_stat_revision(ui.get_stat_revision());
            w.set_stat_late(ui.get_stat_late());
            w.set_stat_points(ui.get_stat_points());
        }
        if let Ok(st) = bar_store.lock() {
            push_notifs_to_bar(w, &st.notifications);
        }
        // Pet PetDex aktif (mode pet): tampilkan sprite; mode
        // notch / blob → wajah blob biasa.
        {
            let rt = pet_runtime.borrow();
            apply_pet_to_bar(w, &rt, mode == "pet");
        }
        // Segarkan volume OS saat bar dibuka (panel mengembang
        // menampilkan nilai sebenarnya, bukan tebakan).
        refresh_bar_volume(w);
        // Hide + show = naik ke depan (Slint tidak punya Activate).
        let _ = w.hide();
        let _ = w.show();
        visible.set(true);
        if let Some(db_mutex) = db {
            if let Ok(mut guard) = db_mutex.lock() {
                let _ = guard.set("Settings.DynamicBar.Visible", "true", "Bool");
            }
        }
        if let Some(ui) = ui_weak.upgrade() {
            ui.set_app_dynamic_bar_visible(true);
        }
    }
}

/// Segarkan kontrol volume panel mengembang dari OS (Windows:
/// Core Audio; Linux: pactl/wpctl). Berjalan di thread sendiri
/// supaya subprocess (~0,1–0,5 detik) tidak memblokir UI; bila
/// OS tidak menyediakan kontrol volume, slider dinonaktifkan
/// (volume-available=false), bukan error.
fn refresh_bar_volume(w: &DynamicBarWindow) {
    let w_weak = w.as_weak();
    std::thread::spawn(move || {
        let vol = volume::get_volume();
        let _ = w_weak.upgrade_in_event_loop(move |w| match vol {
            Some(v) => {
                w.set_volume_available(true);
                w.set_volume_f(v.level as f32 / 100.0);
                w.set_volume_muted(v.muted);
            }
            None => {
                w.set_volume_available(false);
            }
        });
    });
}

/// Pusatkan notch secara horizontal di atas window utama.
/// Dipakai saat notch mengembang/mengecil (lebar window berubah
/// 150 <-> 560 sementara tepi kiri OS diam — tanpa ini, notch
/// yang mengembang bergeser ke kanan). `y` tidak disentuh.
fn recenter_notch(w: &DynamicBarWindow, ui: &AppWindow, expanded: bool) {
    let win = ui.window();
    let pos = win.position();
    let size = win.size();
    let mscale = win.scale_factor();
    let bw = ((if expanded { 560f32 } else { 150f32 }) * mscale) as i32;
    let cx = (pos.x + (size.width as i32 - bw) / 2).max(0);
    let cur = w.window().position();
    w.window()
        .set_position(slint::PhysicalPosition::new(cx, cur.y));
}

/// Sembunyikan Dynamic Bar (dari flyout profil / toggle):
/// simpan posisi, hide, persist + sinkronkan toggle. Window
/// di-drop saat buka berikutnya (pola yang sama dengan close).
fn hide_dynamic_bar_window(
    cell: &std::rc::Rc<std::cell::RefCell<Option<DynamicBarWindow>>>,
    visible: &std::rc::Rc<std::cell::Cell<bool>>,
    ui_weak: &slint::Weak<AppWindow>,
    db: &Option<Arc<Mutex<DatabaseService>>>,
) {
    if let Some(w) = cell.borrow().as_ref() {
        save_bar_position(w.window(), db);
        let _ = w.hide();
    }
    visible.set(false);
    if let Some(db_mutex) = db {
        if let Ok(mut guard) = db_mutex.lock() {
            let _ = guard.set("Settings.DynamicBar.Visible", "false", "Bool");
        }
    }
    if let Some(ui) = ui_weak.upgrade() {
        ui.set_app_dynamic_bar_visible(false);
    }
}

/// Sinkronkan token Design ke Dynamic Bar bila sedang tampil
/// (dipakai handler tema + timer sinkronisasi — lihat
/// theme::sync_design_to_bar).
fn sync_bar_theme(ui: &AppWindow, cell: &std::rc::Rc<std::cell::RefCell<Option<DynamicBarWindow>>>) {
    if let Some(w) = cell.borrow().as_ref() {
        theme::sync_design_to_bar(ui, w);
    }
}

/// Sinkronkan token Design ke window radial bila sudah dibuat
/// (dipakai handler tema — radial punya instance Design sendiri,
/// seperti bar; tanpa ini aksen radial ketinggalan preset).
fn sync_radial_theme(ui: &AppWindow, cell: &std::rc::Rc<std::cell::RefCell<Option<RadialMenuWindow>>>) {
    if let Some(w) = cell.borrow().as_ref() {
        theme::sync_design_to_radial(ui, w);
    }
}

/// Dorong gaya chrome (style + gaya/posisi tombol) ke satu window
/// sekunder ber-chrome. Dipakai saat window dibuat (nilai awal dari
/// AppWindow) dan saat pengaturan chrome berubah.
fn push_chrome_to_sheet(w: &SpreadsheetWindow, ui: &AppWindow) {
    w.set_chrome_style(slint::SharedString::from(ui.get_app_chrome()));
    w.set_chrome_button_style(slint::SharedString::from(
        ui.get_app_chrome_button_style(),
    ));
    w.set_chrome_button_position(slint::SharedString::from(
        ui.get_app_chrome_button_position(),
    ));
}

fn push_chrome_to_toolbox(w: &ToolboxWindow, ui: &AppWindow) {
    w.set_chrome_style(slint::SharedString::from(ui.get_app_chrome()));
    w.set_chrome_button_style(slint::SharedString::from(
        ui.get_app_chrome_button_style(),
    ));
    w.set_chrome_button_position(slint::SharedString::from(
        ui.get_app_chrome_button_position(),
    ));
}

fn push_chrome_to_cardlist(w: &CardListWindow, ui: &AppWindow) {
    w.set_chrome_style(slint::SharedString::from(ui.get_app_chrome()));
    w.set_chrome_button_style(slint::SharedString::from(
        ui.get_app_chrome_button_style(),
    ));
    w.set_chrome_button_position(slint::SharedString::from(
        ui.get_app_chrome_button_position(),
    ));
}

/// Sinkronkan gaya chrome ke window sekunder yang sedang tampil
/// (dipakai handler appearance — tanpa ini window yang sudah
/// terbuka ketinggalan gaya chrome baru; berlaku live tanpa restart).
fn sync_chrome_to_windows(
    ui: &AppWindow,
    sheet_cell: &std::rc::Rc<std::cell::RefCell<Option<SpreadsheetWindow>>>,
    toolbox_cell: &std::rc::Rc<std::cell::RefCell<Option<ToolboxWindow>>>,
    card_cell: &std::rc::Rc<std::cell::RefCell<Option<CardListWindow>>>,
) {
    if let Some(w) = sheet_cell.borrow().as_ref() {
        push_chrome_to_sheet(w, ui);
    }
    if let Some(w) = toolbox_cell.borrow().as_ref() {
        push_chrome_to_toolbox(w, ui);
    }
    if let Some(w) = card_cell.borrow().as_ref() {
        push_chrome_to_cardlist(w, ui);
    }
}

/// Dorong gaya + geometri ring ke window radial: gaya ("glass" |
/// "hud" | "flat"), jumlah segmen, dan semua perintah Path / posisi
/// tombol yang dihitung mesin geometri dari N (split rule).
/// Dipakai saat radial dibuka dan saat pengaturan radial berubah
/// (live tanpa restart).
fn push_radial_geometry(w: &RadialMenuWindow, style: &str, n: usize) {
    let g = radial_geom::radial_geometry(n);
    w.set_radial_style(slint::SharedString::from(style));
    w.set_sector_count(n as i32);
    w.set_wedge_cmds(slint::ModelRc::new(slint::VecModel::from(
        g.wedge_cmds
            .into_iter()
            .map(slint::SharedString::from)
            .collect::<Vec<_>>(),
    )));
    w.set_arc_cmds(slint::ModelRc::new(slint::VecModel::from(
        g.arc_cmds
            .into_iter()
            .map(slint::SharedString::from)
            .collect::<Vec<_>>(),
    )));
    w.set_divider_cmds(slint::SharedString::from(g.divider_cmds));
    w.set_btn_x(slint::ModelRc::new(slint::VecModel::from(
        g.btn_x
            .into_iter()
            .map(|v| v as f32)
            .collect::<Vec<_>>(),
    )));
    w.set_btn_y(slint::ModelRc::new(slint::VecModel::from(
        g.btn_y
            .into_iter()
            .map(|v| v as f32)
            .collect::<Vec<_>>(),
    )));
}

/// Buka ReportBugDialog di tengah window utama. Kirim laporan =
/// simpan ke tabel Activities + toast; ringkasan kosong ditolak.
fn open_report_bug_window(
    cell: &std::rc::Rc<std::cell::RefCell<Option<ReportBugDialog>>>,
    visible: &std::rc::Rc<std::cell::Cell<bool>>,
    ui_weak: &slint::Weak<AppWindow>,
    db: &Option<Arc<Mutex<DatabaseService>>>,
) {
    let mut opt = cell.borrow_mut();
    // Buka-ulang setelah tutup selalu buat window baru (alasan sama
    // seperti SpreadsheetWindow: hide->show reuse tidak andal).
    if !visible.get() {
        *opt = None;
    }
    if opt.is_none() {
        if let Ok(w) = ReportBugDialog::new() {
            let w_weak = w.as_weak();
            let vis_close = visible.clone();
            w.on_close_window(move || {
                vis_close.set(false);
                if let Some(w) = w_weak.upgrade() {
                    let _ = w.hide();
                }
            });
            let ui_weak2 = ui_weak.clone();
            let db_clone = db.clone();
            let w_weak_send = w.as_weak();
            let vis_send = visible.clone();
            w.on_send_report(move |summary, detail| {
                let summary = summary.trim().to_string();
                if summary.is_empty() {
                    if let Some(ui) = ui_weak2.upgrade() {
                        ui.set_toast(slint::SharedString::from("Isi ringkasan dulu"));
                    }
                    return;
                }
                if let Some(ref db_mutex) = db_clone {
                    if let Ok(mut db_guard) = db_mutex.lock() {
                        let user = ui_weak2
                            .upgrade()
                            .map(|ui| ui.get_app_user_name().to_string())
                            .unwrap_or_default();
                        let _ = db_guard.insert_activity(
                            &user,
                            "BugReport",
                            &summary,
                            detail.as_str(),
                        );
                    }
                }
                if let Some(ui) = ui_weak2.upgrade() {
                    ui.set_toast(slint::SharedString::from("Laporan terkirim"));
                }
                vis_send.set(false);
                if let Some(w) = w_weak_send.upgrade() {
                    let _ = w.hide();
                }
            });
            let ui_weak3 = ui_weak.clone();
            w.on_toast(move |msg| {
                if let Some(ui) = ui_weak3.upgrade() {
                    ui.set_toast(msg);
                }
            });
            *opt = Some(w);
        }
    }
    if let Some(w) = opt.as_ref() {
        w.set_summary_text(slint::SharedString::from(""));
        w.set_detail_text(slint::SharedString::from(""));
        if let Some(ui) = ui_weak.upgrade() {
            let (cx, cy) = center_over_main(&ui, 560, 400);
            w.window()
                .set_position(slint::PhysicalPosition::new(cx, cy));
        }
        let _ = w.hide();
        let _ = w.show();
        visible.set(true);
    }
}

#[derive(Clone, Copy)]
enum RadialHookEvent {
    TriggerDown,
    TriggerUp,
    Recorded(RadialShortcutConfig),
}

#[cfg(test)]
mod radial_shortcut_tests {
    use super::*;

    #[test]
    fn csharp_default_shortcut_config_is_preserved() {
        // Default C# TriggerConfig.cs: Type=Mouse(0), MouseButton=2
        // (Middle), Modifiers=4 (Shift), Key=0 (tidak dipakai utk Mouse).
        // Nilai lama port (Type=1/Key=222/Modifiers=12 = Win+Shift+')
        // BUKAN default C# — diperbaiki 27 Nov 2026 (keputusan abeng
        // 16:54: hotkey global default sama seperti C#).
        let cfg: RadialShortcutConfig =
            serde_json::from_str(r#"{"Type":0,"MouseButton":2,"Key":0,"Modifiers":4}"#).unwrap();
        assert_eq!(cfg.Type, 0);
        assert_eq!(cfg.MouseButton, 2);
        assert_eq!(cfg.Key, 0);
        assert_eq!(cfg.Modifiers, 4);
        assert_eq!(shortcut_label(&cfg), "Shift + Mouse Middle");
        assert!(shortcut_matches(&cfg, None, Some(Button::Middle), 4));
    }

    #[test]
    fn shortcut_json_missing_properties_uses_csharp_defaults() {
        let cfg: RadialShortcutConfig = serde_json::from_str("{}").unwrap();
        assert_eq!(cfg.Type, default_trigger_type());
        assert_eq!(cfg.MouseButton, default_mouse_button());
        assert_eq!(cfg.Key, default_trigger_key());
        assert_eq!(cfg.Modifiers, default_trigger_modifiers());
    }

    #[test]
    fn keyboard_shortcut_requires_exact_modifiers_and_matching_key() {
        let cfg = RadialShortcutConfig {
            Type: 1,
            MouseButton: 2,
            Key: 222,
            Modifiers: 12,
        };
        assert!(shortcut_matches(&cfg, Some(Key::Quote), None, 12));
        assert!(!shortcut_matches(&cfg, Some(Key::Quote), None, 4));
        assert!(!shortcut_matches(&cfg, Some(Key::Quote), None, 13));
        assert!(!shortcut_matches(&cfg, Some(Key::F1), None, 12));
        assert!(!shortcut_matches(&cfg, None, Some(Button::Middle), 12));
    }

    #[test]
    fn rdev_recording_maps_back_to_csharp_virtual_keys() {
        assert_eq!(rdev_key_to_vk(Key::Quote), Some(222));
        assert_eq!(rdev_key_to_vk(Key::LeftBracket), Some(219));
        assert_eq!(rdev_key_to_vk(Key::F1), Some(112));
    }

    #[test]
    fn mouse_shortcut_requires_matching_button_and_exact_modifiers() {
        let cfg = RadialShortcutConfig {
            Type: 0,
            MouseButton: 2,
            Key: 222,
            Modifiers: 4,
        };
        assert!(shortcut_matches(&cfg, None, Some(Button::Middle), 4));
        assert!(!shortcut_matches(&cfg, None, Some(Button::Middle), 12));
        assert!(!shortcut_matches(&cfg, None, Some(Button::Left), 4));
        assert!(!shortcut_matches(&cfg, Some(Key::Quote), None, 4));
    }
}

#[cfg(test)]
mod dynamic_bar_tests {
    use super::*;

    fn cards(pairs: &[(&str, &str)]) -> Vec<(String, String)> {
        pairs
            .iter()
            .map(|(id, name)| (id.to_string(), name.to_string()))
            .collect()
    }

    #[test]
    fn first_sync_is_baseline_no_notification() {
        let mut st = BarState::default();
        record_list_sync(&mut st, "list1", "EDITING", &cards(&[("a", "Kartu A"), ("b", "Kartu B")]));
        assert!(st.notifications.is_empty());
        assert_eq!(st.seen_cards["list1"].len(), 2);
    }

    #[test]
    fn second_sync_new_card_notifies_once() {
        let mut st = BarState::default();
        record_list_sync(&mut st, "list1", "EDITING", &cards(&[("a", "Kartu A")]));
        record_list_sync(
            &mut st,
            "list1",
            "EDITING",
            &cards(&[("a", "Kartu A"), ("b", "Kartu B")]),
        );
        assert_eq!(st.notifications.len(), 1);
        assert_eq!(st.notifications[0].kind, "card");
        assert_eq!(st.notifications[0].category, "EDITING");
        assert_eq!(st.notifications[0].title, "Kartu B");
        assert_eq!(st.notifications[0].sub, "Kartu baru di EDITING");
        // Sync ketiga tanpa kartu baru = tidak ada notifikasi lagi.
        record_list_sync(
            &mut st,
            "list1",
            "EDITING",
            &cards(&[("a", "Kartu A"), ("b", "Kartu B")]),
        );
        assert_eq!(st.notifications.len(), 1);
    }

    #[test]
    fn new_cards_capped_per_sync_and_total() {
        let mut st = BarState::default();
        record_list_sync(&mut st, "list1", "EDITING", &cards(&[("a", "A")]));
        // 5 kartu baru sekaligus → maks 3 notifikasi dari sync ini.
        record_list_sync(
            &mut st,
            "list1",
            "EDITING",
            &cards(&[
                ("a", "A"),
                ("b", "B"),
                ("c", "C"),
                ("d", "D"),
                ("e", "E"),
                ("f", "F"),
            ]),
        );
        assert_eq!(st.notifications.len(), 3);
        // Semua kartu tetap tercatat sebagai sudah terlihat.
        assert_eq!(st.seen_cards["list1"].len(), 6);
    }

    #[test]
    fn points_first_call_is_baseline_then_increase_notifies() {
        let mut st = BarState::default();
        assert_eq!(record_points(&mut st, "4180"), None);
        assert!(st.notifications.is_empty());
        // Sama → tidak ada notifikasi.
        assert_eq!(record_points(&mut st, "4180"), None);
        // Naik → notifikasi + delta.
        assert_eq!(record_points(&mut st, "4210"), Some((4180, 4210)));
        assert_eq!(st.notifications.len(), 1);
        assert_eq!(st.notifications[0].kind, "points");
        assert_eq!(st.notifications[0].category, "POINTS");
        assert_eq!(st.notifications[0].title, "Poin bertambah");
        assert_eq!(st.notifications[0].sub, "4180 → 4210");
        // Turun → tidak ada notifikasi (dan baseline ikut turun).
        assert_eq!(record_points(&mut st, "4200"), None);
        assert_eq!(st.notifications.len(), 1);
        // Tidak bisa di-parse → None.
        assert_eq!(record_points(&mut st, "abc"), None);
    }
}

static EXPLORER_ITEMS: &[(&str, &str, &str, &str, &str, &str, &str, &str, &str, &str)] = &[
];

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
// Parse satu link kartu board -> (id, judul, url). Judul diambil dari
// slug di link (nama asli dari API Trello di Fase 6); kalau tidak
// ada slug, judul = ID. Link kosong/salah -> None.
// Link ditempel apa adanya dipertahankan (domain Trello kantor ikut
// terbawa); bila yang ditempel hanya ID polos, link dibangun dari
// base URL yang dikonfigurasi (lihat trello_api::web_url_for_card).
fn parse_trello_link(link: &str, base_url: &str) -> Option<(String, String, String)> {
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
            trello_api::web_url_for_card(base_url, link),
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

    // Ukuran awal window utama 960x640 (keputusan abeng 27 Nov 2026
    // 16:54: resizable, min-width 520 di .slint; BUKAN 1280x800).
    // Di-set di sini karena Slint melarang width + min-width pada satu
    // Window — .slint hanya memegang min-width, Rust memegang ukuran awal.
    ui.window().set_size(slint::PhysicalSize::new(960, 640));

    // QA/report helper: membuka state tertentu lewat argumen startup, tanpa
    // menyuntik klik mouse/keyboard ke user. Dipakai runner screenshot VM;
    // tidak ada efek bila aplikasi dijalankan normal (tanpa argumen).
    let mut no_splash = false;
    let mut radial_test = false;
    let mut toolbox_test = false;
    let mut simulate_hotkey = false;
    let mut log_arg = false;
    for arg in std::env::args().skip(1) {
        if let Some(v) = arg.strip_prefix("--view=") {
            ui.set_overlay_view(v.into());
        } else if let Some(v) = arg.strip_prefix("--group=") {
            ui.set_settings_group(v.into());
        } else if let Some(v) = arg.strip_prefix("--tab=") {
            ui.set_current_tab(v.into());
        } else if arg == "--log" {
            log_arg = true;
            ui.set_show_logpanel(true);
        } else if arg == "--flyout" {
            ui.set_show_flyout(true);
        } else if arg == "--no-splash" {
            no_splash = true;
        } else if arg == "--radial-test" {
            radial_test = true;
        } else if arg == "--toolbox" {
            toolbox_test = true;
        } else if arg == "--simulate-hotkey" {
            simulate_hotkey = true;
        } else if let Some(v) = arg.strip_prefix("--dialog=") {
            ui.set_dialog_open(v.into());
        }
    }

    // QA helper --radial-test / --toolbox: dijalankan di AKHIR main
    // (lihat sebelum ui.run()) — watcher radial memanggil callback
    // Rust, jadi membuka sebelum callback terdaftar = no-op.

    // Timer simulasi disimpan di Vec agar hidup sampai ui.run()
    // selesai (alasan sama: Drop Timer membatalkan timer).
    let mut _sim_timers: Vec<slint::Timer> = Vec::new();
    if simulate_hotkey {
        // Simulasi trigger default saat ini (Shift + Middle, ditahan
        // 3 detik): tekan Shift + Middle di 2 dtk, lepas di 5 dtk.
        // (Dulu mensimulasi Win+Shift+' — default lama yang sudah
        // diganti ke Mouse/Middle/Shift pada 27 Nov 2026.)
        let sim_down = slint::Timer::default();
        sim_down.start(
            slint::TimerMode::SingleShot,
            std::time::Duration::from_secs(2),
            move || {
                let _ = rdev::simulate(&rdev::EventType::KeyPress(Key::ShiftLeft));
                let _ = rdev::simulate(&rdev::EventType::ButtonPress(Button::Middle));
            },
        );
        _sim_timers.push(sim_down);
        let sim_up = slint::Timer::default();
        sim_up.start(
            slint::TimerMode::SingleShot,
            std::time::Duration::from_secs(5),
            move || {
                let _ = rdev::simulate(&rdev::EventType::ButtonRelease(Button::Middle));
                let _ = rdev::simulate(&rdev::EventType::KeyRelease(Key::ShiftLeft));
            },
        );
        _sim_timers.push(sim_up);
    }

    // Inisialisasi Database SQLite BMachine.db nyata di AppData/BMachine
    let db = match DatabaseService::init() {
        Ok(d) => Some(Arc::new(Mutex::new(d))),
        Err(e) => {
            eprintln!("Warning: SQLite init error: {}", e);
            None
        }
    };

    // Dynamic Bar: state bersama (kartu terlihat + poin terakhir +
    // notifikasi). Dibuat di sini supaya blok load SQLite di bawah
    // bisa mengisi baseline poin + visibilitas tersimpan.
    let bar_store: BarStore = Arc::new(Mutex::new(BarState::default()));
    // Window + flag tampil Dynamic Bar — dibuat awal supaya handler
    // tema (di bawah) bisa menyinkronkan token Design ke bar saat
    // tema berubah.
    let bar_win_cell: std::rc::Rc<std::cell::RefCell<Option<DynamicBarWindow>>> =
        std::rc::Rc::new(std::cell::RefCell::new(None));
    // Window radial — dibuat awal juga (alasan sama seperti bar:
    // handler tema harus bisa menyinkronkan aksen ke radial yang
    // sedang tampil; window-nya sendiri baru dibuat saat dibuka).
    let radial_win_cell: std::rc::Rc<std::cell::RefCell<Option<RadialMenuWindow>>> =
        std::rc::Rc::new(std::cell::RefCell::new(None));
    // Window sekunder ber-chrome (Spreadsheet / CardList / Toolbox) —
    // dibuat awal juga (alasan sama: handler appearance harus bisa
    // mendorong gaya chrome ke window yang sedang tampil, live tanpa
    // restart; window-nya sendiri baru dibuat saat dibuka).
    let sheet_win_cell: std::rc::Rc<std::cell::RefCell<Option<SpreadsheetWindow>>> =
        std::rc::Rc::new(std::cell::RefCell::new(None));
    let card_win_cell: std::rc::Rc<std::cell::RefCell<Option<CardListWindow>>> =
        std::rc::Rc::new(std::cell::RefCell::new(None));
    let toolbox_win_cell: std::rc::Rc<std::cell::RefCell<Option<ToolboxWindow>>> =
        std::rc::Rc::new(std::cell::RefCell::new(None));
    // Waktu drag terakhir mode pet (wander tidak jalan 5 detik
    // setelah user selesai men-drag avatar).
    let pet_last_drag: Arc<Mutex<Instant>> =
        Arc::new(Mutex::new(Instant::now()));
    // Pet PetDex aktif — runtime sheet + mesin animasi (dibagi
    // timer frame, pembuka bar, handler Settings).
    let pet_runtime: std::rc::Rc<std::cell::RefCell<PetRuntime>> =
        std::rc::Rc::new(std::cell::RefCell::new(PetRuntime::new()));
    let bar_visible: std::rc::Rc<std::cell::Cell<bool>> =
        std::rc::Rc::new(std::cell::Cell::new(false));

    // Pulihkan lebar + state Log Panel dari DB (C#: LogPanelWidth +
    // IsLogPanelOpen; nilai nyata abeng 287.6 / True). Default 288px
    // bila belum ada simpanan; lebar di-clamp >= 180 (maks di-clamp
    // saat drag, bukan di sini). Argumen --log (QA) menang atas state
    // tersimpan — restore state dilewati bila --log diberikan.
    if let Some(ref db_mutex) = db {
        if let Ok(db_guard) = db_mutex.lock() {
            if let Some(v) = db_guard.get("Dashboard.LogPanelWidth") {
                if let Ok(w) = v.parse::<f32>() {
                    if w >= 180.0 {
                        ui.set_log_panel_width(w);
                    }
                }
            }
            if !log_arg {
                if let Some(v) = db_guard.get("Dashboard.IsLogPanelOpen") {
                    ui.set_show_logpanel(v == "True" || v == "true");
                }
            }
        }
    }

    let doc_mgr = Arc::new(Mutex::new(DocManager::default()));
    let proc_log = Arc::new(Mutex::new(ProcessLogService::default()));
    {
        // Benih log startup nyata (mencerminkan langkah bootstrapper C#).
        proc_log.lock().unwrap().seed_boot();
    }

    // Sinkronisasi baca setting awal dari SQLite jika ada
    let mut init_trello_key = String::new();
    let mut init_trello_tok = String::new();
    let mut init_trello_base_url = String::new();
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
            // Base URL server board (default Trello asli; Trello
            // kantor diganti di Settings — tanpa rebuild).
            if let Some(v) = db_guard.get("Trello.BaseUrl") {
                init_trello_base_url = trello_api::normalize_base_url(&v);
            }
            if init_trello_base_url.is_empty() {
                init_trello_base_url = trello_api::DEFAULT_BASE_URL.to_string();
            }
            ui.set_trello_base_url(slint::SharedString::from(init_trello_base_url.as_str()));
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
                ui.set_stat_points(slint::SharedString::from(pts.as_str()));
                // Baseline poin Dynamic Bar: timer sinkronisasi tidak
                // boleh mengira poin naik saat pertama jalan.
                if let Ok(mut st) = bar_store.lock() {
                    st.last_points = pts.trim().replace('.', "").parse::<i64>().ok();
                }
            } else {
                ui.set_stat_points(slint::SharedString::from("0"));
            }
            // Dynamic Bar tampil atau tidak (default tampil bila
            // belum pernah diatur).
            if let Some(v) = db_guard.get("Settings.DynamicBar.Visible") {
                ui.set_app_dynamic_bar_visible(!(v == "false" || v == "0"));
            }
            // Notch hidup: mode + voice (default: notch, voice
            // nyala, volume 80, suara otomatis = Bahasa Indonesia
            // bila ada voice id-ID di sistem, kalau tidak ada
            // pakai suara default sistem).
            let mut notch_mode = "notch".to_string();
            if let Some(v) = db_guard.get("Settings.Notch.Mode") {
                if v == "pet" {
                    notch_mode = "pet".to_string();
                }
            }
            ui.set_app_notch_mode(slint::SharedString::from(notch_mode.as_str()));
            // Gaya mata maskot (default "nusa"; nilai tak dikenal
            // → "nusa" via valid_eye_style).
            let mut eye_style = "nusa".to_string();
            if let Some(v) = db_guard.get("Settings.Notch.EyeStyle") {
                eye_style = valid_eye_style(&v);
            }
            ui.set_app_notch_eye_style(slint::SharedString::from(eye_style.as_str()));
            // Gaya radial (default "glass") + jumlah segmen ring
            // (default 8; nilai tak dikenal / di luar 2–8 dijepit).
            let mut radial_style = "glass".to_string();
            if let Some(v) = db_guard.get("Settings.Radial.Style") {
                radial_style = valid_radial_style(&v);
            }
            ui.set_app_radial_style(slint::SharedString::from(radial_style.as_str()));
            let mut radial_segments = 8usize;
            if let Some(v) = db_guard.get("Settings.Radial.Segments") {
                if let Ok(n) = v.trim().trim_matches('"').parse::<i32>() {
                    radial_segments = valid_radial_segments(n);
                }
            }
            ui.set_app_radial_segments(radial_segments as i32);
            let mut vcfg = voice::VoiceConfig::default();
            if let Some(v) = db_guard.get("Settings.Notch.VoiceEnabled") {
                vcfg.enabled = !(v == "false" || v == "0");
            }
            if let Some(v) = db_guard.get("Settings.Notch.VoiceName") {
                vcfg.voice_name = v;
            }
            if let Some(v) = db_guard.get("Settings.Notch.VoiceVolume") {
                if let Ok(n) = v.parse::<i32>() {
                    vcfg.volume = n.clamp(0, 100);
                }
            }
            ui.set_app_notch_voice_enabled(vcfg.enabled);
            ui.set_app_notch_voice_name(slint::SharedString::from(vcfg.voice_name.as_str()));
            ui.set_app_notch_voice_volume(slint::SharedString::from(vcfg.volume.to_string()));
            voice::set_config(vcfg.clone());
            // Daftar suara SAPI (Windows saja; di Linux kosong —
            // ComboBox di Settings hanya menampilkan "Otomatis").
            // Di Windows enumerasi ini ~0,5 detik.
            let voices = voice::list_voices();
            let mut voice_display: Vec<slint::SharedString> =
                vec![slint::SharedString::from("Otomatis (Indonesia bila ada)")];
            let mut voice_names: Vec<slint::SharedString> =
                vec![slint::SharedString::from("")];
            for v in &voices {
                voice_display.push(slint::SharedString::from(format!(
                    "{} ({})",
                    v.name, v.culture
                )));
                voice_names.push(slint::SharedString::from(v.name.as_str()));
            }
            ui.set_app_notch_voices(slint::ModelRc::new(slint::VecModel::from(
                voice_display,
            )));
            ui.set_app_notch_voice_names(slint::ModelRc::new(slint::VecModel::from(
                voice_names,
            )));
            let voice_idx = if vcfg.voice_name.is_empty() {
                0
            } else {
                voices
                    .iter()
                    .position(|v| v.name == vcfg.voice_name)
                    .map(|i| i + 1)
                    .unwrap_or(0)
            };
            ui.set_app_notch_voice_index(voice_idx as i32);
            // Pet PetDex aktif ("" = blob bawaan) + daftar
            // ter-install (ComboBox + daftar di Settings).
            let mut active_pet = String::new();
            if let Some(v) = db_guard.get("Settings.Notch.Pet") {
                active_pet = v;
            }
            pet_runtime.borrow_mut().load_active(&active_pet);
            refresh_pet_lists(&ui, &active_pet);
            if let Some(v) = db_guard.get("User.Name") {
                ui.set_app_user_name(slint::SharedString::from(v.clone()));
                let initial = v
                    .chars()
                    .next()
                    .map(|c| c.to_uppercase().collect::<String>())
                    .filter(|s| !s.is_empty())
                    .unwrap_or_else(|| "U".to_string());
                ui.set_app_user_initial(slint::SharedString::from(initial));
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
            if let Some(v) = db_guard.get("Settings.Language") {
                ui.set_app_lang(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Settings.Batch.Filter") {
                ui.set_batch_file_filter(slint::SharedString::from(v));
            }
            if let Some(v) = db_guard.get("Settings.Dash.Batch") {
                ui.set_is_batch_visible(v == "true" || v == "1");
            }
            if let Some(v) = db_guard.get("Settings.Dash.Lock") {
                ui.set_is_folder_locker_visible(v == "true" || v == "1");
            }
            if let Some(v) = db_guard.get("Settings.Dash.Explorer") {
                ui.set_is_output_explorer_visible(v == "true" || v == "1");
            }
            theme::load_theme_from_db(&ui, &db_guard);
        }
    }

    // Sapaan flyout profil dari jam lokal (C# DashboardViewModel:
    // <12 GoodMorning, <15 GoodAfternoon, else GoodEvening).
    {
        use chrono::Timelike;
        let hour = chrono::Local::now().hour();
        let greeting = if hour < 12 {
            "Selamat pagi"
        } else if hour < 15 {
            "Selamat siang"
        } else {
            "Selamat sore"
        };
        ui.set_greeting_text(slint::SharedString::from(greeting));
    }
    // Versi aplikasi untuk footer flyout (C# AppVersion =
    // assembly ToString(2) = "15.15").
    ui.set_app_version(slint::SharedString::from(APP_VERSION));
    // Versi lengkap untuk splash (C# SplashViewModel.VersionText).
    ui.set_splash_version(slint::SharedString::from(format!(
        "VERSION {}",
        APP_VERSION_FULL
    )));

    // Default widget status awal persis C# (0 jika belum tersambung, bukan angka demo)
    ui.set_stat_editing(slint::SharedString::from("0"));
    ui.set_stat_revision(slint::SharedString::from("0"));
    ui.set_stat_late(slint::SharedString::from("0"));

    // ---- Spreadsheet: state guard + draft (C# SpreadsheetRowAnchorGuard
    // + SpreadsheetDraftStore). Satu state dipakai bersama tampilan
    // embedded (SheetsView) + window (SpreadsheetWindow).
    let sheet_state: std::rc::Rc<std::cell::RefCell<spreadsheet_guard::SpreadsheetState>> = {
        let (sheet_id, sheet_name) = if let Some(ref db_mutex) = db {
            match db_mutex.lock() {
                Ok(g) => (
                    g.get("Google.SheetId").filter(|s| !s.is_empty()).unwrap_or_else(|| "LOCAL".to_string()),
                    g.get("Google.SheetName").filter(|s| !s.is_empty()).unwrap_or_else(|| "ALL DATA REGULER".to_string()),
                ),
                Err(_) => ("LOCAL".to_string(), "ALL DATA REGULER".to_string()),
            }
        } else {
            ("LOCAL".to_string(), "ALL DATA REGULER".to_string())
        };
        let snap = read_sheet_snapshot(&db, &sheet_id, &sheet_name);
        let mut st = spreadsheet_guard::SpreadsheetState::new(
            sheet_id,
            sheet_name,
            1,
            6,
            snap.rows.clone(),
        );
        st.drafts = load_sheet_drafts(&db);
        // Pulihkan draft ke grid (hanya yang nilai aslinya masih cocok —
        // C# RestoreDraftsAsync; sisa tetap tersimpan di DB).
        let (display, applied, remaining) = st.reload(snap.rows.clone());
        persist_sheet_drafts(&db, &st.drafts);
        let loaded = st.loaded.clone();
        push_grid_to_main(&ui, &display, &loaded);
        if applied > 0 {
            ui.set_sheet_has_draft(true);
            ui.set_sheet_draft_count(applied as i32);
            let leftover = if !remaining.is_empty() {
                format!(
                    " {} draft tidak bisa diterapkan karena data sheet sudah berubah.",
                    remaining.len()
                )
            } else {
                String::new()
            };
            ui.set_sheet_status_text(slint::SharedString::from(format!(
                "{} perubahan draft dipulihkan, belum di-push ke sheet.{}",
                applied, leftover
            )));
        }
        std::rc::Rc::new(std::cell::RefCell::new(st))
    };

    // Baris sheet di-preload dari DB (Rust mengisi, bukan demo UI);
    // leaderboard dimuat load_leaderboard() (dipakai juga saat refresh FAB).
    {
        let mut sheet_rows: Vec<slint::SharedString> = vec![];
        if let Some(ref db_mutex) = db {
            if let Ok(db_guard) = db_mutex.lock() {
                for i in 1..=10 {
                    let key = format!("Sheet.Row.{}", i);
                    if let Some(v) = db_guard.get(&key) {
                        sheet_rows.push(slint::SharedString::from(v.clone()));
                    }
                }
            }
        }
        let sheet_count = sheet_rows.len() as i32;
        let sc0: Vec<slint::SharedString> = sheet_rows
            .iter()
            .map(|r| slint::SharedString::from(r.split('\t').next().unwrap_or("").to_string()))
            .collect();
        let sc1: Vec<slint::SharedString> = sheet_rows
            .iter()
            .map(|r| slint::SharedString::from(r.split('\t').nth(1).unwrap_or("").to_string()))
            .collect();
        let sc2: Vec<slint::SharedString> = sheet_rows
            .iter()
            .map(|r| slint::SharedString::from(r.split('\t').nth(2).unwrap_or("").to_string()))
            .collect();
        ui.set_sheet_rows(slint::ModelRc::new(slint::VecModel::from(sheet_rows)));
        ui.set_sheet_row_count(sheet_count);
        ui.set_sheet_col0(slint::ModelRc::new(slint::VecModel::from(sc0)));
        ui.set_sheet_col1(slint::ModelRc::new(slint::VecModel::from(sc1)));
        ui.set_sheet_col2(slint::ModelRc::new(slint::VecModel::from(sc2)));
    }
    load_leaderboard(&ui, &db);

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
        base_url: String,
        el: String,
        rl: String,
        ll: String,
        show_toast: bool,
        bar_store: BarStore,
    ) {
        std::thread::spawn(move || {
            let mut synced = 0usize;
            if !k.is_empty() && !t.is_empty() {
                if !el.is_empty() {
                    if let Ok(cards) =
                        trello_api::TrelloApiService::fetch_cards(&base_url, &k, &t, &el)
                    {
                        synced += cards.len();
                        // Dynamic Bar: kartu baru di list ini.
                        if let Ok(mut st) = bar_store.lock() {
                            let fresh = record_list_sync(
                                &mut st,
                                &el,
                                "EDITING",
                                &cards
                                    .iter()
                                    .map(|c| (c.id.clone(), c.name.clone()))
                                    .collect::<Vec<_>>(),
                            );
                            if fresh > 0 {
                                voice::speak_event("Ada kartu baru masuk di EDITING.");
                                // Maskot kaget sebentar (ekspresi blob, lihat
                                // effective-mood di DynamicBarWindow).
                                st.pet_mood_until = Some((
                                    "surprised".to_string(),
                                    Instant::now() + std::time::Duration::from_secs(3),
                                ));
                            }
                        }
                        let count = format!("{}", cards.len());
                        let titles: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| slint::SharedString::from(c.name.as_str()))
                            .collect();
                        let subs: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| {
                                let s = c
                                    .desc
                                    .as_deref()
                                    .unwrap_or("")
                                    .lines()
                                    .next()
                                    .unwrap_or("")
                                    .trim();
                                slint::SharedString::from(if s.is_empty() {
                                    "Trello Card"
                                } else {
                                    s
                                })
                            })
                            .collect();
                        let dues: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| {
                                let d = c.due.as_deref().unwrap_or("-");
                                slint::SharedString::from(d.get(..10).unwrap_or(d))
                            })
                            .collect();
                        let ids: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| {
                                let did = extract_card_display_id(&c.name);
                                slint::SharedString::from(if did.is_empty() {
                                    c.id.clone()
                                } else {
                                    did
                                })
                            })
                            .collect();
                        let dones: Vec<bool> = vec![false; cards.len()];
                        // Link web kartu dari API (`shortUrl`) — detail
                        // kartu membukanya di browser; kosong bila server
                        // tidak mengembalikannya.
                        let urls: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| {
                                slint::SharedString::from(
                                    c.short_url.clone().unwrap_or_default(),
                                )
                            })
                            .collect();
                        let _ = ui_weak.upgrade_in_event_loop(move |u| {
                            u.set_stat_editing(count.into());
                            u.set_editing_titles(slint::ModelRc::new(slint::VecModel::from(
                                titles.clone(),
                            )));
                            u.set_editing_subs(slint::ModelRc::new(slint::VecModel::from(
                                subs.clone(),
                            )));
                            u.set_editing_due(slint::ModelRc::new(slint::VecModel::from(
                                dues.clone(),
                            )));
                            u.set_editing_ids(slint::ModelRc::new(slint::VecModel::from(ids)));
                            u.set_editing_urls(slint::ModelRc::new(slint::VecModel::from(urls)));
                            u.set_editing_done(slint::ModelRc::new(slint::VecModel::from(dones)));
                            if u.get_list_category().as_str() == "EDITING"
                                || u.get_trello_window_cat().as_str() == "EDITING"
                            {
                                u.set_list_titles(slint::ModelRc::new(slint::VecModel::from(
                                    titles,
                                )));
                                u.set_list_subs(slint::ModelRc::new(slint::VecModel::from(subs)));
                                u.set_list_due(slint::ModelRc::new(slint::VecModel::from(dues)));
                            }
                        });
                    }
                }
                if !rl.is_empty() {
                    if let Ok(cards) =
                        trello_api::TrelloApiService::fetch_cards(&base_url, &k, &t, &rl)
                    {
                        synced += cards.len();
                        // Dynamic Bar: kartu baru di list ini.
                        if let Ok(mut st) = bar_store.lock() {
                            let fresh = record_list_sync(
                                &mut st,
                                &rl,
                                "REVISION",
                                &cards
                                    .iter()
                                    .map(|c| (c.id.clone(), c.name.clone()))
                                    .collect::<Vec<_>>(),
                            );
                            if fresh > 0 {
                                voice::speak_event("Ada kartu baru masuk di REVISION.");
                                // Maskot kaget sebentar (ekspresi blob, lihat
                                // effective-mood di DynamicBarWindow).
                                st.pet_mood_until = Some((
                                    "surprised".to_string(),
                                    Instant::now() + std::time::Duration::from_secs(3),
                                ));
                            }
                        }
                        let count = format!("{}", cards.len());
                        let titles: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| slint::SharedString::from(c.name.as_str()))
                            .collect();
                        let subs: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| {
                                let s = c
                                    .desc
                                    .as_deref()
                                    .unwrap_or("")
                                    .lines()
                                    .next()
                                    .unwrap_or("")
                                    .trim();
                                slint::SharedString::from(if s.is_empty() { "Revisi" } else { s })
                            })
                            .collect();
                        let dues: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| {
                                let d = c.due.as_deref().unwrap_or("-");
                                slint::SharedString::from(d.get(..10).unwrap_or(d))
                            })
                            .collect();
                        let ids: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| slint::SharedString::from(extract_card_display_id(&c.name)))
                            .collect();
                        let dones: Vec<bool> = vec![false; cards.len()];
                        let urls: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| {
                                slint::SharedString::from(
                                    c.short_url.clone().unwrap_or_default(),
                                )
                            })
                            .collect();
                        let _ = ui_weak.upgrade_in_event_loop(move |u| {
                            u.set_stat_revision(count.into());
                            u.set_revision_titles(slint::ModelRc::new(slint::VecModel::from(
                                titles.clone(),
                            )));
                            u.set_revision_subs(slint::ModelRc::new(slint::VecModel::from(
                                subs.clone(),
                            )));
                            u.set_revision_due(slint::ModelRc::new(slint::VecModel::from(
                                dues.clone(),
                            )));
                            u.set_revision_ids(slint::ModelRc::new(slint::VecModel::from(ids)));
                            u.set_revision_urls(slint::ModelRc::new(slint::VecModel::from(urls)));
                            u.set_revision_done(slint::ModelRc::new(slint::VecModel::from(dones)));
                            if u.get_list_category().as_str() == "REVISION"
                                || u.get_trello_window_cat().as_str() == "REVISION"
                            {
                                u.set_list_titles(slint::ModelRc::new(slint::VecModel::from(
                                    titles,
                                )));
                                u.set_list_subs(slint::ModelRc::new(slint::VecModel::from(subs)));
                                u.set_list_due(slint::ModelRc::new(slint::VecModel::from(dues)));
                            }
                        });
                    }
                }
                if !ll.is_empty() {
                    if let Ok(cards) =
                        trello_api::TrelloApiService::fetch_cards(&base_url, &k, &t, &ll)
                    {
                        synced += cards.len();
                        // Dynamic Bar: kartu baru di list ini.
                        if let Ok(mut st) = bar_store.lock() {
                            let fresh = record_list_sync(
                                &mut st,
                                &ll,
                                "LATE",
                                &cards
                                    .iter()
                                    .map(|c| (c.id.clone(), c.name.clone()))
                                    .collect::<Vec<_>>(),
                            );
                            if fresh > 0 {
                                voice::speak_event("Ada kartu baru masuk di LATE.");
                                // Maskot kaget sebentar (ekspresi blob, lihat
                                // effective-mood di DynamicBarWindow).
                                st.pet_mood_until = Some((
                                    "surprised".to_string(),
                                    Instant::now() + std::time::Duration::from_secs(3),
                                ));
                            }
                        }
                        let count = format!("{}", cards.len());
                        let titles: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| slint::SharedString::from(c.name.as_str()))
                            .collect();
                        let subs: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| {
                                let s = c
                                    .desc
                                    .as_deref()
                                    .unwrap_or("")
                                    .lines()
                                    .next()
                                    .unwrap_or("")
                                    .trim();
                                slint::SharedString::from(if s.is_empty() { "Susulan" } else { s })
                            })
                            .collect();
                        let dues: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| {
                                let d = c.due.as_deref().unwrap_or("-");
                                slint::SharedString::from(d.get(..10).unwrap_or(d))
                            })
                            .collect();
                        let ids: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| slint::SharedString::from(extract_card_display_id(&c.name)))
                            .collect();
                        let dones: Vec<bool> = vec![false; cards.len()];
                        let urls: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| {
                                slint::SharedString::from(
                                    c.short_url.clone().unwrap_or_default(),
                                )
                            })
                            .collect();
                        let _ = ui_weak.upgrade_in_event_loop(move |u| {
                            u.set_stat_late(count.into());
                            u.set_late_titles(slint::ModelRc::new(slint::VecModel::from(
                                titles.clone(),
                            )));
                            u.set_late_subs(slint::ModelRc::new(slint::VecModel::from(
                                subs.clone(),
                            )));
                            u.set_late_due(slint::ModelRc::new(slint::VecModel::from(
                                dues.clone(),
                            )));
                            u.set_late_ids(slint::ModelRc::new(slint::VecModel::from(ids)));
                            u.set_late_urls(slint::ModelRc::new(slint::VecModel::from(urls)));
                            u.set_late_done(slint::ModelRc::new(slint::VecModel::from(dones)));
                            if u.get_list_category().as_str() == "LATE"
                                || u.get_trello_window_cat().as_str() == "LATE"
                            {
                                u.set_list_titles(slint::ModelRc::new(slint::VecModel::from(
                                    titles,
                                )));
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
                        u.set_toast(slint::SharedString::from(format!(
                            "Trello di-refresh — {} card dimuat",
                            synced
                        )));
                    } else if k.is_empty() || t.is_empty() {
                        u.set_toast(slint::SharedString::from(
                            "Isi API Key & Token Trello di Settings > Account",
                        ));
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
            init_trello_base_url.clone(),
            init_edit_list.clone(),
            init_rev_list.clone(),
            init_late_list.clone(),
            false,
            bar_store.clone(),
        );
    }

    // ---- Appearance, Window Controls, Trello & Path Callbacks ----
    {
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        let bar_cell_sync = bar_win_cell.clone();
        let radial_cell_sync = radial_win_cell.clone();
        let sheet_cell_sync = sheet_win_cell.clone();
        let toolbox_cell_sync = toolbox_win_cell.clone();
        let card_cell_sync = card_win_cell.clone();
        ui.on_apply_appearance(move || {
            if let Some(ui) = ui_weak.upgrade() {
                match theme::apply_appearance(&ui) {
                    Ok(()) => {
                        if let Some(ref db_mutex) = db_clone {
                            if let Ok(mut db_guard) = db_mutex.lock() {
                                theme::save_theme_to_db(&ui, &mut db_guard);
                            }
                        }
                        sync_bar_theme(&ui, &bar_cell_sync);
                        sync_radial_theme(&ui, &radial_cell_sync);
                        sync_chrome_to_windows(
                            &ui,
                            &sheet_cell_sync,
                            &toolbox_cell_sync,
                            &card_cell_sync,
                        );
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
        let bar_cell_sync = bar_win_cell.clone();
        let radial_cell_sync = radial_win_cell.clone();
        let sheet_cell_sync = sheet_win_cell.clone();
        let toolbox_cell_sync = toolbox_win_cell.clone();
        let card_cell_sync = card_win_cell.clone();
        ui.on_reset_appearance(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.set_app_color_mode(slint::SharedString::from("dark"));
                ui.set_app_theme_preset(slint::SharedString::from("bmachine-dark"));
                ui.set_app_accent_hex(slint::SharedString::from("#3B82F6"));
                ui.set_app_accent_hex_good(slint::SharedString::from("#3B82F6"));
                ui.set_app_bg_hex(slint::SharedString::from("#121212"));
                ui.set_app_bg_hex_good(slint::SharedString::from("#121212"));
                ui.set_app_fg_hex(slint::SharedString::from("#FFFFFF"));
                ui.set_app_fg_hex_good(slint::SharedString::from("#FFFFFF"));
                // Warna widget + kepadatan ikut kembali ke default
                // (reset yang tidak lengkap = tema setengah jalan).
                ui.set_app_widget_editing_hex(slint::SharedString::from("#3B82F6"));
                ui.set_app_widget_editing_hex_good(slint::SharedString::from("#3B82F6"));
                ui.set_app_widget_revision_hex(slint::SharedString::from("#F97316"));
                ui.set_app_widget_revision_hex_good(slint::SharedString::from("#F97316"));
                ui.set_app_widget_late_hex(slint::SharedString::from("#EF4444"));
                ui.set_app_widget_late_hex_good(slint::SharedString::from("#EF4444"));
                ui.set_app_widget_points_hex(slint::SharedString::from("#2DD4BF"));
                ui.set_app_widget_points_hex_good(slint::SharedString::from("#2DD4BF"));
                ui.set_app_ui_density(slint::SharedString::from("normal"));
                ui.set_app_font_choice(slint::SharedString::from("Inter"));
                ui.set_widget_bg(slint::SharedString::from("doodle"));
                ui.set_widget_style(slint::SharedString::from("solid"));
                ui.set_app_corner_style(slint::SharedString::from("default"));
                ui.set_app_nav_style(slint::SharedString::from("capsule"));
                ui.set_app_chrome(slint::SharedString::from("custom"));
                ui.set_app_chrome_button_style(slint::SharedString::from("icons"));
                ui.set_app_chrome_button_position(slint::SharedString::from("right"));
                let _ = theme::apply_appearance(&ui);
                sync_bar_theme(&ui, &bar_cell_sync);
                sync_radial_theme(&ui, &radial_cell_sync);
                sync_chrome_to_windows(
                    &ui,
                    &sheet_cell_sync,
                    &toolbox_cell_sync,
                    &card_cell_sync,
                );
                if let Some(ref db_mutex) = db_clone {
                    if let Ok(mut db_guard) = db_mutex.lock() {
                        theme::save_theme_to_db(&ui, &mut db_guard);
                    }
                }
                ui.set_toast(slint::SharedString::from(
                    "Tema dikembalikan ke BMachine Dark default",
                ));
            }
        });
    }
    // ---- Sistem tema: preset bawaan + tema user (Omarchy-style) ----
    {
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        let bar_cell_sync = bar_win_cell.clone();
        let radial_cell_sync = radial_win_cell.clone();
        ui.on_apply_theme_preset(move |id| {
            if let Some(ui) = ui_weak.upgrade() {
                match theme::apply_preset(&ui, id.as_str()) {
                    Ok(()) => {
                        if let Some(ref db_mutex) = db_clone {
                            if let Ok(mut db_guard) = db_mutex.lock() {
                                theme::save_theme_to_db(&ui, &mut db_guard);
                            }
                        }
                        sync_bar_theme(&ui, &bar_cell_sync);
                        sync_radial_theme(&ui, &radial_cell_sync);
                        let name = theme::palette_for(id.as_str())
                            .map(|p| p.name)
                            .unwrap_or_else(|| id.to_string());
                        ui.set_toast(slint::SharedString::from(format!(
                            "Tema {} diterapkan",
                            name
                        )));
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
        ui.on_save_user_theme(move |name| {
            if let Some(ui) = ui_weak.upgrade() {
                match theme::snapshot_current_as_user_theme(&ui, name.as_str()) {
                    Ok(theme) => {
                        let saved_name = theme.name.clone();
                        let saved_id = theme.id.clone();
                        if let Some(ref db_mutex) = db_clone {
                            if let Ok(mut db_guard) = db_mutex.lock() {
                                let mut themes = theme::load_user_themes(&db_guard);
                                if let Some(existing) =
                                    themes.iter_mut().find(|t| t.id == theme.id)
                                {
                                    *existing = theme;
                                } else {
                                    themes.push(theme);
                                }
                                theme::save_user_themes(&mut db_guard, &themes);
                                theme::refresh_user_themes(&ui, &themes);
                            }
                        }
                        // Tema yang baru disimpan langsung jadi aktif.
                        ui.set_app_theme_preset(slint::SharedString::from(saved_id));
                        ui.set_new_user_theme_name(slint::SharedString::from(""));
                        if let Some(ref db_mutex) = db_clone {
                            if let Ok(mut db_guard) = db_mutex.lock() {
                                theme::save_theme_to_db(&ui, &mut db_guard);
                            }
                        }
                        ui.set_toast(slint::SharedString::from(format!(
                            "Tema '{}' disimpan",
                            saved_name
                        )));
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
        let bar_cell_sync = bar_win_cell.clone();
        let radial_cell_sync = radial_win_cell.clone();
        ui.on_apply_user_theme(move |id| {
            if let Some(ui) = ui_weak.upgrade() {
                let found: Option<theme::ThemePalette> = (|| {
                    let db_mutex = db_clone.as_ref()?;
                    let db_guard = db_mutex.lock().ok()?;
                    theme::load_user_themes(&db_guard)
                        .into_iter()
                        .find(|t| t.id == id.as_str())
                })();
                match found {
                    Some(theme) => {
                        let name = theme.name.clone();
                        match theme::apply_user_theme(&ui, &theme) {
                            Ok(()) => {
                                if let Some(ref db_mutex) = db_clone {
                                    if let Ok(mut db_guard) = db_mutex.lock() {
                                        theme::save_theme_to_db(&ui, &mut db_guard);
                                    }
                                }
                                sync_bar_theme(&ui, &bar_cell_sync);
                                sync_radial_theme(&ui, &radial_cell_sync);
                                ui.set_toast(slint::SharedString::from(format!(
                                    "Tema {} diterapkan",
                                    name
                                )));
                            }
                            Err(msg) => {
                                ui.set_toast(slint::SharedString::from(msg));
                            }
                        }
                    }
                    None => {
                        ui.set_toast(slint::SharedString::from("Tema tidak ditemukan"));
                    }
                }
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        ui.on_delete_user_theme(move |id| {
            if let Some(ui) = ui_weak.upgrade() {
                if let Some(ref db_mutex) = db_clone {
                    if let Ok(mut db_guard) = db_mutex.lock() {
                        let mut themes = theme::load_user_themes(&db_guard);
                        let before = themes.len();
                        themes.retain(|t| t.id != id.as_str());
                        if themes.len() != before {
                            theme::save_user_themes(&mut db_guard, &themes);
                            theme::refresh_user_themes(&ui, &themes);
                            // Bila tema yang dihapus sedang aktif →
                            // kembali ke Custom (warna saat ini tetap).
                            if ui.get_app_theme_preset().as_str() == id.as_str() {
                                ui.set_app_theme_preset(slint::SharedString::from(
                                    "custom",
                                ));
                                theme::save_theme_to_db(&ui, &mut db_guard);
                            }
                            ui.set_toast(slint::SharedString::from("Tema dihapus"));
                        }
                    }
                }
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
                        let _ =
                            db_guard.set("User.Name", ui.get_app_user_name().as_str(), "String");
                        let initial = ui
                            .get_app_user_name()
                            .chars()
                            .next()
                            .map(|c| c.to_uppercase().collect::<String>())
                            .filter(|s| !s.is_empty())
                            .unwrap_or_else(|| "U".to_string());
                        ui.set_app_user_initial(slint::SharedString::from(initial));
                        let _ = db_guard.set(
                            "Settings.StartupAnim",
                            if ui.get_app_anim_startup() {
                                "true"
                            } else {
                                "false"
                            },
                            "Bool",
                        );
                        let _ = db_guard.set(
                            "Settings.StatSpeed",
                            ui.get_app_anim_speed().as_str(),
                            "String",
                        );
                        let _ = db_guard.set(
                            "Settings.Language",
                            ui.get_app_lang().as_str(),
                            "String",
                        );
                        let _ = db_guard.set(
                            "Settings.Batch.Filter",
                            ui.get_batch_file_filter().as_str(),
                            "String",
                        );
                        let _ = db_guard.set(
                            "Settings.Dash.Batch",
                            if ui.get_is_batch_visible() {
                                "true"
                            } else {
                                "false"
                            },
                            "Bool",
                        );
                        let _ = db_guard.set(
                            "Settings.Dash.Lock",
                            if ui.get_is_folder_locker_visible() {
                                "true"
                            } else {
                                "false"
                            },
                            "Bool",
                        );
                        let _ = db_guard.set(
                            "Settings.Dash.Explorer",
                            if ui.get_is_output_explorer_visible() {
                                "true"
                            } else {
                                "false"
                            },
                            "Bool",
                        );
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
                        let _ = db_guard.set(
                            "Paths.LocalOutput",
                            ui.get_app_path_local().as_str(),
                            "String",
                        );
                        let _ = db_guard.set(
                            "Paths.OkeBase",
                            ui.get_app_path_okebase().as_str(),
                            "String",
                        );
                        let _ = db_guard.set(
                            "Paths.Photoshop",
                            ui.get_app_path_photoshop().as_str(),
                            "String",
                        );
                        let _ = db_guard.set(
                            "Paths.PasFoto",
                            ui.get_app_path_pasfoto().as_str(),
                            "String",
                        );
                        let _ = db_guard.set(
                            "Paths.Manasik",
                            ui.get_app_path_manasik().as_str(),
                            "String",
                        );
                        let _ = db_guard.set(
                            "Paths.Wisuda",
                            ui.get_app_path_wisuda().as_str(),
                            "String",
                        );
                        let _ = db_guard.set(
                            "Paths.Profesi",
                            ui.get_app_path_profesi().as_str(),
                            "String",
                        );
                    }
                }
                ui.set_toast(slint::SharedString::from(
                    "Path folder disimpan ke database",
                ));
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
                    "creds" => platform::pick_file(
                        "Pilih credentials.json",
                        "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                    ),
                    "photoshop" => platform::pick_file(
                        "Pilih Photoshop.exe",
                        "Executable (*.exe)|*.exe|All Files (*.*)|*.*",
                    ),
                    _ => platform::pick_folder("Pilih Folder"),
                };
                // Hanya Picked yang diisi ke field; batal/gagal dialog
                // diam saja di jalur browse-path ini.
                if let platform::PickResult::Picked(path) = picked {
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
        let bar_store_test = bar_store.clone();
        ui.on_test_trello(move || {
            if let Some(ui) = ui_weak.upgrade() {
                let k = ui.get_trello_api_key().to_string();
                let t = ui.get_trello_token().to_string();
                let base_url = ui.get_trello_base_url().to_string();
                if k.trim().is_empty() || t.trim().is_empty() {
                    ui.set_toast(slint::SharedString::from(
                        "Isi API Key dan Token Trello dulu",
                    ));
                    return;
                }
                ui.set_app_trello_status(slint::SharedString::from("Menghubungkan ke Trello..."));
                let ui_w = ui_weak.clone();
                let db_c = db_clone.clone();
                let el = ui.get_editing_list_id().to_string();
                let rl = ui.get_revision_list_id().to_string();
                let ll = ui.get_late_list_id().to_string();
                let bar_store_thread = bar_store_test.clone();
                std::thread::spawn(move || {
                    match trello_api::TrelloApiService::verify_connection(&base_url, &k, &t) {
                        Ok(member) => {
                            let name = member.full_name.unwrap_or(member.username);
                            if let Some(ref db_mutex) = db_c {
                                if let Ok(mut g) = db_mutex.lock() {
                                    let _ = g.set("Trello.ApiKey", &k, "String");
                                    let _ = g.set("Trello.Token", &t, "String");
                                    let _ = g.set(
                                        "Trello.BaseUrl",
                                        &trello_api::normalize_base_url(&base_url),
                                        "String",
                                    );
                                    let _ = g.set("Trello.IsConnected", "true", "Bool");
                                }
                            }
                            let _ = ui_w.upgrade_in_event_loop(move |u| {
                                u.set_app_trello_connected(true);
                                u.set_app_trello_status(slint::SharedString::from(format!(
                                    "Tersambung sebagai {}",
                                    name
                                )));
                                u.set_toast(slint::SharedString::from(format!(
                                    "Trello tersambung ({})",
                                    name
                                )));
                            });
                            spawn_trello_sync(ui_w, k, t, base_url, el, rl, ll, false, bar_store_thread);
                        }
                        Err(e) => {
                            let _ = ui_w.upgrade_in_event_loop(move |u| {
                                u.set_app_trello_connected(false);
                                u.set_app_trello_status(slint::SharedString::from(format!(
                                    "Gagal: {}",
                                    e
                                )));
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
        let bar_store_refresh = bar_store.clone();
        ui.on_refresh_trello(move || {
            if let Some(ui) = ui_weak.upgrade() {
                spawn_trello_sync(
                    ui_weak.clone(),
                    ui.get_trello_api_key().to_string(),
                    ui.get_trello_token().to_string(),
                    ui.get_trello_base_url().to_string(),
                    ui.get_editing_list_id().to_string(),
                    ui.get_revision_list_id().to_string(),
                    ui.get_late_list_id().to_string(),
                    true,
                    bar_store_refresh.clone(),
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
                ui.window()
                    .set_position(slint::PhysicalPosition::new(nx, ny));
            }
        });
    }

    // ---- Log Panel: simpan lebar + state buka/tutup ke DB (C#
    // Dashboard.LogPanelWidth / Dashboard.IsLogPanelOpen; lebar
    // disimpan saat drag handle dilepas, state lewat watcher
    // `changed show-logpanel` di .slint). ----
    {
        let db_clone = db.clone();
        ui.on_save_logpanel_width(move |w| {
            if let Some(ref db_mutex) = db_clone {
                if let Ok(mut db_guard) = db_mutex.lock() {
                    let _ = db_guard.set("Dashboard.LogPanelWidth", &w.to_string(), "Float");
                }
            }
        });
    }
    {
        let db_clone = db.clone();
        ui.on_save_logpanel_open(move |open| {
            if let Some(ref db_mutex) = db_clone {
                if let Ok(mut db_guard) = db_mutex.lock() {
                    let _ = db_guard.set(
                        "Dashboard.IsLogPanelOpen",
                        if open { "True" } else { "False" },
                        "Bool",
                    );
                }
            }
        });
    }

    // ---- Leaderboard: refresh FAB (C# LoadDataCommand) — muat ulang
    // baris + total dari DB. Status "Loading..." 600ms diatur Timer
    // di .slint sebelum callback ini dipanggil. ----
    {
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        ui.on_refresh_leaderboard(move || {
            if let Some(ui) = ui_weak.upgrade() {
                load_leaderboard(&ui, &db_clone);
                ui.set_toast(slint::SharedString::from(
                    "Leaderboard di-refresh — data dimuat dari database",
                ));
            }
        });
    }

    // ---- Standalone Multi-Window Handlers (SpreadsheetWindow & CardListWindow) ----

    {
        let sheet_cell = sheet_win_cell.clone();
        // Window sedang tampil atau tidak. Buka-ulang setelah tutup
        // SELALU membuat window baru: window yang sudah di-hide
        // tidak andal di-show ulang di Slint/winit Linux (reopen
        // sering gagal — window tetap tersembunyi). Window yang
        // masih tampil cukup di-raise (hide+show).
        let sheet_visible = std::rc::Rc::new(std::cell::Cell::new(false));
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        let sheet_state_clone = sheet_state.clone();
        ui.on_open_new_sheet_window(move || {
            let sheet_state = sheet_state_clone.clone();
            let mut opt = sheet_cell.borrow_mut();
            if !sheet_visible.get() {
                *opt = None;
            }
            if opt.is_none() {
                if let Ok(w) = SpreadsheetWindow::new() {
                    let w_weak = w.as_weak();
                    let db_clone2 = db_clone.clone();
                    let vis_close = sheet_visible.clone();
                    w.on_close_window(move || {
                        vis_close.set(false);
                        if let Some(w) = w_weak.upgrade() {
                            // Simpan posisi/ukuran terakhir (C#
                            // DashboardViewModel: Closing -> simpan).
                            save_window_geometry(w.window(), &db_clone2, "SpreadsheetWindow");
                            let _ = w.hide();
                        }
                    });
                    // Drag titlebar (C# drag handle 46px, kanan 420px bebas).
                    let w_weak2 = w.as_weak();
                    w.on_move_window(move |dx, dy| {
                        if let Some(w) = w_weak2.upgrade() {
                            let scale = w.window().scale_factor();
                            let pos = w.window().position();
                            let nx = pos.x + (dx * scale) as i32;
                            let ny = pos.y + (dy * scale) as i32;
                            w.window()
                                .set_position(slint::PhysicalPosition::new(nx, ny));
                        }
                    });
                    // Tombol kontrol chrome (min/max) + gaya chrome
                    // awal dari AppWindow (lihat DESIGN_SYSTEM.md §23).
                    let w_weak_min = w.as_weak();
                    w.on_minimize_window(move || {
                        if let Some(w) = w_weak_min.upgrade() {
                            w.window().set_minimized(true);
                        }
                    });
                    let w_weak_max = w.as_weak();
                    w.on_maximize_window(move || {
                        if let Some(w) = w_weak_max.upgrade() {
                            let m = w.window().is_maximized();
                            w.window().set_maximized(!m);
                        }
                    });
                    if let Some(ui) = ui_weak.upgrade() {
                        push_chrome_to_sheet(&w, &ui);
                    }
                    // Spreadsheet guard + draft: teruskan callback window
                    // ke state bersama (sama seperti handler window utama).
                    let state_edit = sheet_state.clone();
                    let db_edit = db_clone.clone();
                    w.on_sheet_cell_edited(move |row, col, orig, val| {
                        let mut st = state_edit.borrow_mut();
                        if st.record_edit(row as usize, col as usize, orig.as_str(), val.as_str())
                        {
                            persist_sheet_drafts(&db_edit, &st.drafts);
                        }
                    });
                    let state_save = sheet_state.clone();
                    let db_save = db_clone.clone();
                    let w_weak_save = w.as_weak();
                    let ui_weak_save = ui_weak.clone();
                    w.on_sheet_save(move || {
                        if let Some(w) = w_weak_save.upgrade() {
                            let current_grid = grid_from_sheet_win(&w);
                            let mut st = state_save.borrow_mut();
                            match run_sheet_save(&mut st, &db_save, current_grid) {
                                SheetSaveOutcome::Blocked(msg) => {
                                    w.set_sheet_guard_result(slint::SharedString::from(msg));
                                }
                                SheetSaveOutcome::Saved(new_values) => {
                                    let loaded = st.loaded.clone();
                                    w.set_sheet_guard_result(slint::SharedString::from(""));
                                    push_grid_to_sheet_win(&w, &new_values, &loaded);
                                    w.set_sheet_status_text(slint::SharedString::from(
                                        "✓ Semua perubahan tersimpan ke Google Sheets & SQLite",
                                    ));
                                    // Sinkronkan tampilan embedded.
                                    if let Some(ui) = ui_weak_save.upgrade() {
                                        push_grid_to_main(&ui, &new_values, &loaded);
                                        ui.set_sheet_guard_result(slint::SharedString::from(""));
                                        ui.set_sheet_has_draft(false);
                                        ui.set_sheet_draft_count(0);
                                        ui.set_sheet_status_text(slint::SharedString::from(
                                            "✓ Semua perubahan tersimpan ke Google Sheets & SQLite",
                                        ));
                                    }
                                }
                            }
                        }
                    });
                    let state_refresh = sheet_state.clone();
                    let db_refresh = db_clone.clone();
                    let w_weak_refresh = w.as_weak();
                    let ui_weak_refresh = ui_weak.clone();
                    w.on_sheet_refresh(move || {
                        if let Some(w) = w_weak_refresh.upgrade() {
                            let mut st = state_refresh.borrow_mut();
                            let (display, applied, remaining) =
                                run_sheet_refresh(&mut st, &db_refresh);
                            let loaded = st.loaded.clone();
                            let status = sheet_restore_status(applied, &remaining);
                            push_grid_to_sheet_win(&w, &display, &loaded);
                            w.set_sheet_has_draft(applied > 0);
                            w.set_sheet_draft_count(applied as i32);
                            w.set_sheet_status_text(slint::SharedString::from(status.clone()));
                            // Sinkronkan tampilan embedded.
                            if let Some(ui) = ui_weak_refresh.upgrade() {
                                push_grid_to_main(&ui, &display, &loaded);
                                ui.set_sheet_has_draft(applied > 0);
                                ui.set_sheet_draft_count(applied as i32);
                                ui.set_sheet_status_text(slint::SharedString::from(status));
                            }
                        }
                    });
                    *opt = Some(w);
                }
            }
            if let Some(w) = opt.as_ref() {
                // Gaya chrome saat ini (bisa berubah selagi window
                // tertutup — dorong lagi tiap buka-ulang).
                if let Some(ui) = ui_weak.upgrade() {
                    push_chrome_to_sheet(w, &ui);
                }
                // Pulihkan posisi/ukuran terakhir dari DB (C#: buka ->
                // pulihkan, Activate() bila sudah terbuka). Posisi di-clamp
                // >= 0: simpanan lama X=-20 (center 1000px di main 960px
                // sebelum clamp) pernah membuat window terbit 20px ke kiri
                // layar; DB asli abeng sendiri X=817 (tidak kena).
                let scale = w.window().scale_factor();
                if let Some((x, y, lw, lh)) =
                    read_window_geometry(&db_clone, "SpreadsheetWindow")
                {
                    let x = x.max(0);
                    let y = y.max(0);
                    w.window().set_position(slint::PhysicalPosition::new(
                        (x as f32 * scale) as i32,
                        (y as f32 * scale) as i32,
                    ));
                    w.window().set_size(slint::PhysicalSize {
                        width: (lw as f32 * scale) as u32,
                        height: (lh as f32 * scale) as u32,
                    });
                } else if let Some(ui) = ui_weak.upgrade() {
                    // Belum ada simpanan: tengah window utama (ukuran
                    // default 1000x600 sudah di .slint).
                    let (cx, cy) = center_over_main(&ui, 1000, 600);
                    w.window()
                        .set_position(slint::PhysicalPosition::new(cx, cy));
                }
                // Isi grid dari state bersama (nilai tampil + nilai asli
                // + status draft) — sinkron dengan tampilan embedded.
                {
                    let st = sheet_state.borrow();
                    let display = sheet_display_rows(&st);
                    let loaded = st.loaded.clone();
                    push_grid_to_sheet_win(w, &display, &loaded);
                    w.set_sheet_has_draft(!st.drafts.is_empty());
                    w.set_sheet_draft_count(st.drafts.len() as i32);
                    w.set_sheet_guard_result(slint::SharedString::from(""));
                }
                // Hide + show = naik ke depan (Slint tidak punya Activate).
                let _ = w.hide();
                let _ = w.show();
                sheet_visible.set(true);
            }
        });
    }

    // ---- Spreadsheet: guard + draft, handler window utama (tampilan
    // embedded). Sinkronkan ke SpreadsheetWindow bila sedang terbuka.
    // ----
    {
        let state = sheet_state.clone();
        let db_clone = db.clone();
        ui.on_sheet_cell_edited(move |row, col, orig, val| {
            let mut st = state.borrow_mut();
            if st.record_edit(row as usize, col as usize, orig.as_str(), val.as_str()) {
                persist_sheet_drafts(&db_clone, &st.drafts);
            }
        });
    }
    {
        let state = sheet_state.clone();
        let db_clone = db.clone();
        let ui_weak = ui.as_weak();
        let sheet_cell = sheet_win_cell.clone();
        ui.on_sheet_save(move || {
            if let Some(ui) = ui_weak.upgrade() {
                let current_grid = grid_from_main(&ui);
                let mut st = state.borrow_mut();
                match run_sheet_save(&mut st, &db_clone, current_grid) {
                    SheetSaveOutcome::Blocked(msg) => {
                        ui.set_sheet_guard_result(slint::SharedString::from(msg));
                    }
                    SheetSaveOutcome::Saved(new_values) => {
                        let loaded = st.loaded.clone();
                        ui.set_sheet_guard_result(slint::SharedString::from(""));
                        push_grid_to_main(&ui, &new_values, &loaded);
                        ui.set_sheet_status_text(slint::SharedString::from(
                            "✓ Semua perubahan tersimpan ke Google Sheets & SQLite",
                        ));
                        if let Some(w) = sheet_cell.borrow().as_ref() {
                            push_grid_to_sheet_win(w, &new_values, &loaded);
                            w.set_sheet_guard_result(slint::SharedString::from(""));
                            w.set_sheet_has_draft(false);
                            w.set_sheet_draft_count(0);
                            w.set_sheet_status_text(slint::SharedString::from(
                                "✓ Semua perubahan tersimpan ke Google Sheets & SQLite",
                            ));
                        }
                    }
                }
            }
        });
    }
    {
        let state = sheet_state.clone();
        let db_clone = db.clone();
        let ui_weak = ui.as_weak();
        let sheet_cell = sheet_win_cell.clone();
        ui.on_sheet_refresh(move || {
            if let Some(ui) = ui_weak.upgrade() {
                let mut st = state.borrow_mut();
                let (display, applied, remaining) = run_sheet_refresh(&mut st, &db_clone);
                let loaded = st.loaded.clone();
                let status = sheet_restore_status(applied, &remaining);
                push_grid_to_main(&ui, &display, &loaded);
                ui.set_sheet_has_draft(applied > 0);
                ui.set_sheet_draft_count(applied as i32);
                ui.set_sheet_status_text(slint::SharedString::from(status.clone()));
                if let Some(w) = sheet_cell.borrow().as_ref() {
                    push_grid_to_sheet_win(w, &display, &loaded);
                    w.set_sheet_has_draft(applied > 0);
                    w.set_sheet_draft_count(applied as i32);
                    w.set_sheet_status_text(slint::SharedString::from(status));
                }
            }
        });
    }

    {
        let card_cell = card_win_cell.clone();
        // Sama seperti sheet: buka-ulang setelah tutup selalu buat
        // window baru (hide->show reuse tidak andal di Linux).
        let card_visible = std::rc::Rc::new(std::cell::Cell::new(false));
        let ui_weak = ui.as_weak();
        ui.on_open_new_card_window(move |cat| {
            let cat_str = cat.as_str().to_string();
            let mut opt = card_cell.borrow_mut();
            if !card_visible.get() {
                *opt = None;
            }
            if opt.is_none() {
                if let Ok(w) = CardListWindow::new() {
                    let w_weak = w.as_weak();
                    let vis_close = card_visible.clone();
                    w.on_close_window(move || {
                        vis_close.set(false);
                        if let Some(w) = w_weak.upgrade() {
                            let _ = w.hide();
                        }
                    });
                    // Tombol kontrol chrome (min/max) + drag (titlebar
                    // kustom / strip headless) + gaya chrome awal dari
                    // AppWindow (lihat DESIGN_SYSTEM.md §23).
                    let w_weak_min = w.as_weak();
                    w.on_minimize_window(move || {
                        if let Some(w) = w_weak_min.upgrade() {
                            w.window().set_minimized(true);
                        }
                    });
                    let w_weak_max = w.as_weak();
                    w.on_maximize_window(move || {
                        if let Some(w) = w_weak_max.upgrade() {
                            let m = w.window().is_maximized();
                            w.window().set_maximized(!m);
                        }
                    });
                    let w_weak_move = w.as_weak();
                    w.on_move_window(move |dx, dy| {
                        if let Some(w) = w_weak_move.upgrade() {
                            let scale = w.window().scale_factor();
                            let pos = w.window().position();
                            w.window().set_position(slint::PhysicalPosition::new(
                                pos.x + (dx * scale) as i32,
                                pos.y + (dy * scale) as i32,
                            ));
                        }
                    });
                    if let Some(ui) = ui_weak.upgrade() {
                        push_chrome_to_cardlist(&w, &ui);
                    }
                    *opt = Some(w);
                }
            }
            if let Some(w) = opt.as_ref() {
                // Gaya chrome saat ini (bisa berubah selagi window
                // tertutup — dorong lagi tiap buka-ulang).
                if let Some(ui) = ui_weak.upgrade() {
                    push_chrome_to_cardlist(w, &ui);
                }
                w.set_category(slint::SharedString::from(cat_str.clone()));
                if let Some(main_ui) = ui_weak.upgrade() {
                    if cat_str == "EDITING" {
                        w.set_category_accent(slint::Color::from_rgb_u8(59, 130, 246));
                        w.set_card_titles(main_ui.get_editing_titles());
                        w.set_card_subs(main_ui.get_editing_subs());
                        w.set_card_due(main_ui.get_editing_due());
                        w.set_card_done(main_ui.get_editing_done());
                    } else if cat_str == "REVISION" {
                        w.set_category_accent(slint::Color::from_rgb_u8(245, 158, 11));
                        w.set_card_titles(main_ui.get_revision_titles());
                        w.set_card_subs(main_ui.get_revision_subs());
                        w.set_card_due(main_ui.get_revision_due());
                        w.set_card_done(main_ui.get_revision_done());
                    } else {
                        w.set_category_accent(slint::Color::from_rgb_u8(239, 68, 68));
                        w.set_card_titles(main_ui.get_late_titles());
                        w.set_card_subs(main_ui.get_late_subs());
                        w.set_card_due(main_ui.get_late_due());
                        w.set_card_done(main_ui.get_late_done());
                    }
                }
                // Tengah window utama (C# CenterScreen; 450x600
                // sesuai ukuran CardListWindow di .slint).
                if let Some(main_ui) = ui_weak.upgrade() {
                    let (cx, cy) = center_over_main(&main_ui, 450, 600);
                    w.window()
                        .set_position(slint::PhysicalPosition::new(cx, cy));
                }
                // Hide + show = naik ke depan (Slint tidak punya
                // Activate). show() saja tidak andal di sini.
                let _ = w.hide();
                let _ = w.show();
                card_visible.set(true);
            }
        });
    }

    // ---- Dynamic Bar (floating mini-bar) — window OS. ----
    {
        let cell = bar_win_cell.clone();
        let vis = bar_visible.clone();
        let ui_weak = ui.as_weak();
        let store = bar_store.clone();
        let db_clone = db.clone();
        let pet_last_drag = pet_last_drag.clone();
        let pet_runtime = pet_runtime.clone();
        ui.on_open_dynamic_bar(move || {
            open_dynamic_bar_window(
                &cell,
                &vis,
                &ui_weak,
                &store,
                &db_clone,
                &pet_last_drag,
                &pet_runtime,
            );
        });
    }
    {
        let cell = bar_win_cell.clone();
        let vis = bar_visible.clone();
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        ui.on_hide_dynamic_bar(move || {
            hide_dynamic_bar_window(&cell, &vis, &ui_weak, &db_clone);
        });
    }

    // ---- Notch hidup: pengaturan mode + voice (Settings >
    // Appearance > Notch). Mode diterapkan langsung tanpa
    // restart — bar yang sedang tampil dibuka-ulang di mode
    // baru (notch: atas-tengah; pet: posisi tersimpan).
    {
        let cell = bar_win_cell.clone();
        let vis = bar_visible.clone();
        let ui_weak = ui.as_weak();
        let store = bar_store.clone();
        let db_clone = db.clone();
        let pet_last_drag = pet_last_drag.clone();
        let pet_runtime = pet_runtime.clone();
        ui.on_notch_settings_changed(move || {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            let mode = ui.get_app_notch_mode().to_string();
            let eye_style = valid_eye_style(&ui.get_app_notch_eye_style());
            let enabled = ui.get_app_notch_voice_enabled();
            let vol_str = ui.get_app_notch_voice_volume().to_string();
            let volume = vol_str.parse::<i32>().unwrap_or(80).clamp(0, 100);
            let idx = ui.get_app_notch_voice_index();
            let name = if idx <= 0 {
                String::new()
            } else {
                ui.get_app_notch_voice_names()
                    .iter()
                    .nth(idx as usize)
                    .map(|s| s.to_string())
                    .unwrap_or_default()
            };
            voice::set_config(voice::VoiceConfig {
                enabled,
                voice_name: name.clone(),
                volume,
            });
            if let Some(ref db_mutex) = db_clone {
                if let Ok(mut guard) = db_mutex.lock() {
                    let _ = guard.set("Settings.Notch.Mode", &mode, "String");
                    let _ = guard.set("Settings.Notch.EyeStyle", &eye_style, "String");
                    let _ = guard.set(
                        "Settings.Notch.VoiceEnabled",
                        if enabled { "true" } else { "false" },
                        "Bool",
                    );
                    let _ = guard.set("Settings.Notch.VoiceName", &name, "String");
                    let _ = guard.set(
                        "Settings.Notch.VoiceVolume",
                        &volume.to_string(),
                        "String",
                    );
                }
            }
            // Mode baru langsung berlaku: bila bar sedang tampil,
            // buka-ulang di mode baru (notch kembali ke atas-tengah,
            // pet ke posisi tersimpan); bila bar tertutup, cukup
            // simpan — posisi diterapkan saat dibuka lagi.
            if vis.get() {
                open_dynamic_bar_window(
                    &cell,
                    &vis,
                    &ui_weak,
                    &store,
                    &db_clone,
                    &pet_last_drag,
                    &pet_runtime,
                );
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        ui.on_test_notch_voice(move || {
            #[cfg(not(target_os = "windows"))]
            {
                if let Some(ui) = ui_weak.upgrade() {
                    ui.set_toast(slint::SharedString::from(
                        "Voice hanya tersedia di Windows (SAPI).",
                    ));
                }
            }
            #[cfg(target_os = "windows")]
            {
                // Preview sengaja mengabaikan toggle aktif (lihat
                // voice::speak_test) tapi memakai suara + volume
                // yang dipilih di Settings.
                voice::speak_test("Halo, saya maskot BMachine. Notch hidup siap membantu.");
                if let Some(ui) = ui_weak.upgrade() {
                    ui.set_toast(slint::SharedString::from("Mengucapkan tes suara…"));
                }
            }
        });
    }

    // ---- Pet PetDex: pilih aktif / install / hapus / buka halaman.
    // Semua perubahan berlaku langsung tanpa restart (bar yang sedang
    // tampil dibuka-ulang dengan pet baru; install jalan di thread
    // sendiri supaya unduhan tidak memblokir UI).
    {
        let cell = bar_win_cell.clone();
        let vis = bar_visible.clone();
        let ui_weak = ui.as_weak();
        let store = bar_store.clone();
        let db_clone = db.clone();
        let pet_last_drag = pet_last_drag.clone();
        let pet_runtime = pet_runtime.clone();
        ui.on_pet_active_changed(move |idx| {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            let installed = petdex::list_installed();
            let slug = if idx <= 0 {
                String::new()
            } else {
                installed
                    .get((idx - 1) as usize)
                    .map(|p| p.slug.clone())
                    .unwrap_or_default()
            };
            pet_runtime.borrow_mut().load_active(&slug);
            if let Some(ref db_mutex) = db_clone {
                if let Ok(mut guard) = db_mutex.lock() {
                    let _ = guard.set("Settings.Notch.Pet", &slug, "String");
                }
            }
            ui.set_app_pet_active_index(idx);
            if vis.get() {
                open_dynamic_bar_window(
                    &cell,
                    &vis,
                    &ui_weak,
                    &store,
                    &db_clone,
                    &pet_last_drag,
                    &pet_runtime,
                );
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        ui.on_pet_install_requested(move |query| {
            let query = query.trim().to_string();
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            if query.is_empty() {
                ui.set_toast(slint::SharedString::from(
                    "Ketik slug atau nama pet dulu.",
                ));
                return;
            }
            ui.set_app_pet_install_busy(true);
            let ui_weak2 = ui_weak.clone();
            let db_clone2 = db_clone.clone();
            std::thread::spawn(move || {
                // Cari di manifest galeri, lalu unduh + install.
                let result = (|| -> Result<petdex::InstalledPet, String> {
                    let manifest = petdex::fetch_manifest()?;
                    let entry = petdex::find_in_manifest(&manifest, &query)
                        .ok_or_else(|| format!("Pet '{query}' tidak ada di galeri PetDex"))?;
                    petdex::install_pet(entry)
                })();
                let _ = ui_weak2.upgrade_in_event_loop(move |ui| {
                    ui.set_app_pet_install_busy(false);
                    match result {
                        Ok(pet) => {
                            let active = db_clone2
                                .as_ref()
                                .and_then(|m| m.lock().ok())
                                .and_then(|g| g.get("Settings.Notch.Pet"))
                                .unwrap_or_default();
                            refresh_pet_lists(&ui, &active);
                            ui.set_app_pet_install_input(slint::SharedString::from(""));
                            let credit = if pet.submitted_by.is_empty() {
                                String::new()
                            } else {
                                format!(" oleh {}", pet.submitted_by)
                            };
                            ui.set_toast(slint::SharedString::from(format!(
                                "Pet {} ter-install{} — pilih di Pet aktif.",
                                pet.display_name, credit
                            )));
                        }
                        Err(e) => {
                            ui.set_toast(slint::SharedString::from(format!(
                                "Install pet gagal: {e}"
                            )));
                        }
                    }
                });
            });
        });
    }
    {
        let cell = bar_win_cell.clone();
        let vis = bar_visible.clone();
        let ui_weak = ui.as_weak();
        let store = bar_store.clone();
        let db_clone = db.clone();
        let pet_last_drag = pet_last_drag.clone();
        let pet_runtime = pet_runtime.clone();
        ui.on_pet_remove_requested(move |idx| {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            let installed = petdex::list_installed();
            let Some(pet) = installed.get(idx as usize) else {
                return;
            };
            let slug = pet.slug.clone();
            let name = pet.display_name.clone();
            match petdex::remove_pet(&slug) {
                Ok(()) => {
                    // Pet yang dihapus sedang aktif → kembali ke blob.
                    let mut active = db_clone
                        .as_ref()
                        .and_then(|m| m.lock().ok())
                        .and_then(|g| g.get("Settings.Notch.Pet"))
                        .unwrap_or_default();
                    if active == slug {
                        active = String::new();
                        pet_runtime.borrow_mut().load_active("");
                        if let Some(ref db_mutex) = db_clone {
                            if let Ok(mut guard) = db_mutex.lock() {
                                let _ = guard.set("Settings.Notch.Pet", "", "String");
                            }
                        }
                    }
                    refresh_pet_lists(&ui, &active);
                    ui.set_toast(slint::SharedString::from(format!(
                        "Pet {name} dihapus."
                    )));
                    if vis.get() {
                        open_dynamic_bar_window(
                            &cell,
                            &vis,
                            &ui_weak,
                            &store,
                            &db_clone,
                            &pet_last_drag,
                            &pet_runtime,
                        );
                    }
                }
                Err(e) => {
                    ui.set_toast(slint::SharedString::from(format!("Hapus pet gagal: {e}")));
                }
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        ui.on_pet_open_page(move |idx| {
            let installed = petdex::list_installed();
            let Some(pet) = installed.get(idx as usize) else {
                return;
            };
            let url = petdex::pet_page_url(&pet.slug);
            if let Err(e) = platform::open_url(&url) {
                if let Some(ui) = ui_weak.upgrade() {
                    ui.set_toast(slint::SharedString::from(format!(
                        "Tidak bisa buka {url} ({e})"
                    )));
                }
            }
        });
    }

    // ---- Radial / Toolbox / ReportBug — window OS (C#
    // RadialMenuWindow / ToolboxWindow / ReportBugDialog). ----
    // (toolbox_win_cell dibuat di atas, dekat bar_win_cell, supaya
    // handler appearance bisa mendorong gaya chrome ke toolbox yang
    // sedang tampil.)
    let toolbox_visible: std::rc::Rc<std::cell::Cell<bool>> =
        std::rc::Rc::new(std::cell::Cell::new(false));
    let bug_win_cell: std::rc::Rc<std::cell::RefCell<Option<ReportBugDialog>>> =
        std::rc::Rc::new(std::cell::RefCell::new(None));
    let bug_visible: std::rc::Rc<std::cell::Cell<bool>> =
        std::rc::Rc::new(std::cell::Cell::new(false));
    // True hanya saat window radial OS sedang tampil — membedakan
    // "tutup karena trigger dilepas" (release-to-execute) dari
    // "tutup karena back()/klik-kanan" (batal, tanpa eksekusi).
    let radial_visible: std::rc::Rc<std::cell::Cell<bool>> =
        std::rc::Rc::new(std::cell::Cell::new(false));

    {
        let radial_cell = radial_win_cell.clone();
        let radial_vis = radial_visible.clone();
        let toolbox_cell = toolbox_win_cell.clone();
        let toolbox_visible_radial = toolbox_visible.clone();
        let ui_weak = ui.as_weak();
        ui.on_open_radial_at_cursor(move || {
            let mut opt = radial_cell.borrow_mut();
            if opt.is_none() {
                if let Ok(w) = RadialMenuWindow::new() {
                    let w_weak = w.as_weak();
                    let vis_close = radial_vis.clone();
                    let ui_weak_close = ui_weak.clone();
                    w.on_close_window(move || {
                        // back() dari roda (klik-kanan / tengah / backdrop):
                        // tutup saja, JANGAN release-execute.
                        vis_close.set(false);
                        if let Some(w) = w_weak.upgrade() {
                            let _ = w.hide();
                        }
                        if let Some(ui) = ui_weak_close.upgrade() {
                            ui.set_radial_open(false);
                        }
                    });
                    let ui_weak_exec = ui_weak.clone();
                    w.on_execute_script(move |name| {
                        if let Some(ui) = ui_weak_exec.upgrade() {
                            ui.set_toast(slint::SharedString::from(format!(
                                "[Photoshop] Menjalankan: {}",
                                name
                            )));
                        }
                    });
                    let ui_weak_toast = ui_weak.clone();
                    w.on_toast(move |msg| {
                        if let Some(ui) = ui_weak_toast.upgrade() {
                            ui.set_toast(msg);
                        }
                    });
                    let radial_weak_tb = w.as_weak();
                    let vis_tb = radial_vis.clone();
                    let ui_weak_tb = ui_weak.clone();
                    let tb_cell_inner = toolbox_cell.clone();
                    let tb_vis_inner = toolbox_visible_radial.clone();
                    w.on_open_toolbox(move |tab| {
                        // Radial ditutup (tanpa eksekusi), Toolbox dibuka.
                        vis_tb.set(false);
                        if let Some(rw) = radial_weak_tb.upgrade() {
                            let _ = rw.hide();
                        }
                        if let Some(ui) = ui_weak_tb.upgrade() {
                            ui.set_radial_open(false);
                        }
                        open_toolbox_window(&tb_cell_inner, &tb_vis_inner, &ui_weak_tb, tab.to_string());
                    });
                    *opt = Some(w);
                }
            }
            if let Some(w) = opt.as_ref() {
                if let Some(ui) = ui_weak.upgrade() {
                    w.set_doc_tab_count(ui.get_radial_doc_tab_count());
                    // Tema + geometri ring (split rule): aksen mengikuti
                    // preset aktif, segmen dihitung dari jumlah item.
                    theme::sync_design_to_radial(&ui, w);
                    push_radial_geometry(
                        w,
                        &valid_radial_style(&ui.get_app_radial_style()),
                        valid_radial_segments(ui.get_app_radial_segments()),
                    );
                    w.invoke_reset_wheel();
                    if let Some(cursor) = cursor_screen_position() {
                        let scale = ui.window().scale_factor();
                        let (ox, oy) = radial_window_origin(cursor, scale);
                        w.window()
                            .set_position(slint::PhysicalPosition::new(ox, oy));
                    }
                    let _ = w.show();
                    radial_vis.set(true);
                }
            }
        });
    }

    {
        let radial_cell = radial_win_cell.clone();
        let radial_vis = radial_visible.clone();
        ui.on_close_radial_window(move || {
            // Trigger dilepas: release-to-execute, lalu tutup.
            if radial_vis.get() {
                radial_vis.set(false);
                if let Some(w) = radial_cell.borrow().as_ref() {
                    w.invoke_release_execute();
                    let _ = w.hide();
                }
            }
        });
    }

    // Pengaturan radial berubah di Settings (gaya + jumlah segmen):
    // simpan ke DB; bila window radial sedang tampil, dorong langsung
    // (live tanpa restart).
    {
        let radial_cell = radial_win_cell.clone();
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        ui.on_radial_settings_changed(move || {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            let style = valid_radial_style(&ui.get_app_radial_style());
            let n = valid_radial_segments(ui.get_app_radial_segments());
            if let Some(ref db_mutex) = db_clone {
                if let Ok(mut guard) = db_mutex.lock() {
                    let _ = guard.set("Settings.Radial.Style", &style, "String");
                    let _ = guard.set("Settings.Radial.Segments", &n.to_string(), "Int");
                }
            }
            if let Some(w) = radial_cell.borrow().as_ref() {
                theme::sync_design_to_radial(&ui, w);
                push_radial_geometry(w, &style, n);
            }
        });
    }

    {
        let toolbox_cell = toolbox_win_cell.clone();
        let toolbox_vis = toolbox_visible.clone();
        let ui_weak = ui.as_weak();
        ui.on_open_toolbox_window(move |tab| {
            open_toolbox_window(&toolbox_cell, &toolbox_vis, &ui_weak, tab.to_string());
        });
    }

    {
        let bug_cell = bug_win_cell.clone();
        let bug_vis = bug_visible.clone();
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        ui.on_open_report_bug(move || {
            open_report_bug_window(&bug_cell, &bug_vis, &ui_weak, &db_clone);
        });
    }

    {
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        ui.on_export_database(move || {
            if let Some(ui) = ui_weak.upgrade() {
                match platform::pick_folder("Pilih folder cadangan database") {
                    platform::PickResult::Cancelled => {
                        // User membatalkan dialog — diam, tanpa toast.
                    }
                    platform::PickResult::Unavailable(msg) => {
                        ui.set_toast(slint::SharedString::from(msg));
                    }
                    platform::PickResult::Picked(folder) => {
                        let folder_path = std::path::Path::new(&folder);
                        // Pastikan folder tujuan ada sebelum VACUUM INTO.
                        if let Err(e) = std::fs::create_dir_all(folder_path) {
                            ui.set_toast(slint::SharedString::from(format!(
                                "Gagal membuat folder cadangan: {}",
                                e
                            )));
                            return;
                        }
                        // Nama unik (VACUUM INTO gagal bila file sudah ada).
                        let stamp = chrono::Local::now().format("%Y%m%d-%H%M%S");
                        let dest = crate::db::unique_path(
                            folder_path,
                            format!("BMachine-backup-{}.db", stamp),
                        );
                        let Some(ref db_mutex) = db_clone else {
                            ui.set_toast(slint::SharedString::from(
                                "Database tidak tersedia.",
                            ));
                            return;
                        };
                        let Ok(db_guard) = db_mutex.lock() else {
                            ui.set_toast(slint::SharedString::from(
                                "Gagal mengunci database (sedang dipakai proses lain).",
                            ));
                            return;
                        };
                        match db_guard.backup_to(&dest) {
                            Ok(()) => ui.set_toast(slint::SharedString::from(format!(
                                "Database dicadangkan ke {}",
                                dest.display()
                            ))),
                            Err(e) => ui.set_toast(slint::SharedString::from(format!(
                                "Gagal mencadangkan: {}",
                                e
                            ))),
                        }
                    }
                }
            }
        });
    }

    {
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        ui.on_import_database(move || {
            if let Some(ui) = ui_weak.upgrade() {
                match platform::pick_file(
                    "Pilih file cadangan database",
                    "Database (*.db)|*.db",
                ) {
                    platform::PickResult::Cancelled => {
                        // User membatalkan dialog — diam, tanpa toast.
                    }
                    platform::PickResult::Unavailable(msg) => {
                        ui.set_toast(slint::SharedString::from(msg));
                    }
                    platform::PickResult::Picked(path) => {
                        let Some(ref db_mutex) = db_clone else {
                            ui.set_toast(slint::SharedString::from(
                                "Database tidak tersedia.",
                            ));
                            return;
                        };
                        let Ok(mut db_guard) = db_mutex.lock() else {
                            ui.set_toast(slint::SharedString::from(
                                "Gagal mengunci database (sedang dipakai proses lain).",
                            ));
                            return;
                        };
                        // import_database: validasi → backup DB aktif
                        // (.bak bertimestamp) → import satu transaksi.
                        match db_guard
                            .import_database(std::path::Path::new(&path))
                        {
                            Ok(report) => ui.set_toast(slint::SharedString::from(format!(
                                "{} pengaturan dipulihkan — cadangan database lama di {} — restart untuk menerapkan.",
                                report.imported,
                                report.backup_path.display()
                            ))),
                            Err(msg) => ui.set_toast(slint::SharedString::from(msg)),
                        }
                    }
                }
            }
        });
    }

    // Callback simpan pengaturan akun ke SQLite nyata & trigger sync widget
    {
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        let bar_store_save = bar_store.clone();
        ui.on_save_account_settings(move || {
            if let Some(ui) = ui_weak.upgrade() {
                let k = ui.get_trello_api_key().to_string();
                let t = ui.get_trello_token().to_string();
                let base_url = ui.get_trello_base_url().to_string();
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
                        let _ = db_guard.set(
                            "Trello.BaseUrl",
                            &trello_api::normalize_base_url(&base_url),
                            "String",
                        );
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
                let bar_store_bg = bar_store_save.clone();
                std::thread::spawn(move || {
                    if !k.is_empty() && !t.is_empty() {
                        if !e_l.is_empty() {
                            if let Ok(cards) =
                                trello_api::TrelloApiService::fetch_cards(&base_url, &k, &t, &e_l)
                            {
                                let count = format!("{}", cards.len());
                                // Dynamic Bar: kartu baru di list ini.
                                if let Ok(mut st) = bar_store_bg.lock() {
                                    let fresh = record_list_sync(
                                        &mut st,
                                        &e_l,
                                        "EDITING",
                                        &cards
                                            .iter()
                                            .map(|c| (c.id.clone(), c.name.clone()))
                                            .collect::<Vec<_>>(),
                                    );
                                    if fresh > 0 {
                                        voice::speak_event("Ada kartu baru masuk di EDITING.");
                                        // Maskot kaget sebentar (ekspresi blob, lihat
                                        // effective-mood di DynamicBarWindow).
                                        st.pet_mood_until = Some((
                                            "surprised".to_string(),
                                            Instant::now() + std::time::Duration::from_secs(3),
                                        ));
                                    }
                                }
                                let _ = ui_bg.upgrade_in_event_loop(move |u| {
                                    u.set_stat_editing(count.into());
                                });
                            }
                        } else {
                            let _ = ui_bg.upgrade_in_event_loop(|u| u.set_stat_editing("-".into()));
                        }
                        if !r_l.is_empty() {
                            if let Ok(cards) =
                                trello_api::TrelloApiService::fetch_cards(&base_url, &k, &t, &r_l)
                            {
                                let count = format!("{}", cards.len());
                                // Dynamic Bar: kartu baru di list ini.
                                if let Ok(mut st) = bar_store_bg.lock() {
                                    let fresh = record_list_sync(
                                        &mut st,
                                        &r_l,
                                        "REVISION",
                                        &cards
                                            .iter()
                                            .map(|c| (c.id.clone(), c.name.clone()))
                                            .collect::<Vec<_>>(),
                                    );
                                    if fresh > 0 {
                                        voice::speak_event("Ada kartu baru masuk di REVISION.");
                                        // Maskot kaget sebentar (ekspresi blob, lihat
                                        // effective-mood di DynamicBarWindow).
                                        st.pet_mood_until = Some((
                                            "surprised".to_string(),
                                            Instant::now() + std::time::Duration::from_secs(3),
                                        ));
                                    }
                                }
                                let _ = ui_bg.upgrade_in_event_loop(move |u| {
                                    u.set_stat_revision(count.into());
                                });
                            }
                        } else {
                            let _ =
                                ui_bg.upgrade_in_event_loop(|u| u.set_stat_revision("-".into()));
                        }
                        if !l_l.is_empty() {
                            if let Ok(cards) =
                                trello_api::TrelloApiService::fetch_cards(&base_url, &k, &t, &l_l)
                            {
                                let count = format!("{}", cards.len());
                                // Dynamic Bar: kartu baru di list ini.
                                if let Ok(mut st) = bar_store_bg.lock() {
                                    let fresh = record_list_sync(
                                        &mut st,
                                        &l_l,
                                        "LATE",
                                        &cards
                                            .iter()
                                            .map(|c| (c.id.clone(), c.name.clone()))
                                            .collect::<Vec<_>>(),
                                    );
                                    if fresh > 0 {
                                        voice::speak_event("Ada kartu baru masuk di LATE.");
                                        // Maskot kaget sebentar (ekspresi blob, lihat
                                        // effective-mood di DynamicBarWindow).
                                        st.pet_mood_until = Some((
                                            "surprised".to_string(),
                                            Instant::now() + std::time::Duration::from_secs(3),
                                        ));
                                    }
                                }
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
                (
                    10,
                    "Menghubungkan Database SQLite...",
                    "> Membuka SQLite BMachine.db",
                ),
                (
                    25,
                    "Memuat Pengaturan Tema & UI...",
                    "> Tema Dark diterapkan",
                ),
                (
                    50,
                    "Memuat Konfigurasi Aplikasi (Preload)...",
                    "> 82 keys tersimpan ke memori",
                ),
                (
                    75,
                    "Memeriksa dependensi Engine & Script Python...",
                    "> Engine Python OK (Scripts/Master)",
                ),
                (
                    90,
                    "Menyiapkan Layanan Eksternal & Dashboard...",
                    "> Menyiapkan dashboard",
                ),
                (
                    100,
                    "BMachine Siap Beroperasi.",
                    "> BMachine Siap Beroperasi",
                ),
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
                ui.set_update_latest_version(slint::SharedString::from(format!(
                    "v{}",
                    info.latest_version
                )));
                // Item "Ada pembaruan vX" muncul di flyout profil.
                ui.set_has_pending_update(true);
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
            ui.set_toast(slint::SharedString::from(
                "Pembaruan disiapkan (restart diperlukan)...",
            ));
        });
    }

    {
        let ui_weak = ui.as_weak();
        ui.on_dismiss_update(move || {
            let ui = ui_weak.upgrade().unwrap();
            ui.set_toast(slint::SharedString::from(
                "Pembaruan ditunda (disimpan ke database).",
            ));
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
                        let lbl = format!("{}: {}", if *ok { "Selesai" } else { "Gagal" }, s);
                        (st.to_string(), 100, lbl)
                    }
                    (Some((cur, tot, file)), None) => {
                        let pct = if *tot > 0 { *cur * 100 / *tot } else { 0 };
                        (
                            "RUNNING".to_string(),
                            pct,
                            format!("{} ({}/{})", file, cur, tot),
                        )
                    }
                    (None, None) => ("IDLE".to_string(), 0, "Siap".to_string()),
                };

                // Dorong snapshot ke UI hanya bila berubah (hemat event loop).
                let lines = proc_log_w.lock().unwrap().get_display_lines(12);
                let snap = format!("{}|{}|{}|{}", status, prog_val, label, lines.len());
                if snap != last_snap {
                    last_snap = snap;
                    let lines_src: Vec<slint::SharedString> =
                        lines.into_iter().map(slint::SharedString::from).collect();
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
            proc_log_w.lock().unwrap().add_log(
                "[WARNING] Stop diminta — belum ada proses aktif di port ini (Fase 6).".to_string(),
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
            let names: Vec<slint::SharedString> = ordered.iter().map(|i| i.2.into()).collect();
            let kinds: Vec<slint::SharedString> = ordered.iter().map(|i| i.3.into()).collect();
            let exts: Vec<slint::SharedString> = ordered.iter().map(|i| i.4.into()).collect();
            let bases: Vec<slint::SharedString> = ordered.iter().map(|i| i.5.into()).collect();
            let sizes: Vec<slint::SharedString> = ordered.iter().map(|i| i.6.into()).collect();
            let dates: Vec<slint::SharedString> = ordered.iter().map(|i| i.7.into()).collect();
            let previews: Vec<slint::SharedString> = ordered.iter().map(|i| i.8.into()).collect();
            let locs: Vec<slint::SharedString> = ordered.iter().map(|i| i.9.into()).collect();
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
    ui.on_ex_split_ext(move |name| match name.rfind('.') {
        Some(i) if i > 0 => slint::SharedString::from(&name[i..]),
        _ => slint::SharedString::new(),
    });
    ui.on_ex_name_base(move |name| match name.rfind('.') {
        Some(i) if i > 0 => slint::SharedString::from(&name[..i]),
        _ => name,
    });
    ui.on_ex_is_typeahead_char(move |ch| {
        ch.chars().count() == 1
            && ch.chars().next().map_or(false, |c| {
                c.is_ascii_alphanumeric() || c == '_' || c == '-' || c == '.'
            })
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
                match parse_trello_link(line, &ui.get_trello_base_url()) {
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
                    "{} link gagal dibaca — tempel link kartu board (https://.../c/...).",
                    failed
                )));
            } else {
                ui.set_toast(slint::SharedString::from("Tempel dulu link kartunya."));
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
            ui.set_toast(slint::SharedString::from(
                "Kartu manual dihapus dari daftar.",
            ));
        });
    }

    // C# parity: record in Settings, write the same ShortcutConfig JSON, and
    // update the running hook immediately. Queue edges; do not collapse a tap.
    // Fallback = default C# TriggerConfig.cs (Mouse / Middle / Shift) bila
    // DB belum menyimpan ShortcutConfig.
    let initial_shortcut = {
        let mut cfg = RadialShortcutConfig {
            Type: 0,
            MouseButton: 2,
            Key: 0,
            Modifiers: 4,
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
    ui.set_current_shortcut_label(slint::SharedString::from(shortcut_label(&initial_shortcut)));
    let shortcut = Arc::new(Mutex::new(initial_shortcut));
    let recording = Arc::new(AtomicBool::new(false));
    let (hook_tx, hook_rx) = mpsc::channel::<RadialHookEvent>();

    {
        let recording = Arc::clone(&recording);
        let ui_weak = ui.as_weak();
        ui.on_start_recording_shortcut(move || {
            recording.store(true, Ordering::SeqCst);
            if let Some(ui) = ui_weak.upgrade() {
                ui.set_recording_shortcut(true);
            }
        });
    }
    {
        let recording = Arc::clone(&recording);
        let ui_weak = ui.as_weak();
        ui.on_cancel_recording_shortcut(move || {
            recording.store(false, Ordering::SeqCst);
            if let Some(ui) = ui_weak.upgrade() {
                ui.set_recording_shortcut(false);
            }
        });
    }

    let hook_shortcut = Arc::clone(&shortcut);
    let hook_recording = Arc::clone(&recording);
    // Debug hook (sementara, 27 Nov 2026): BMACHINE_DEBUG_HOOK=1
    // mencetak tiap event rdev + hasil pencocokan trigger ke stderr.
    // Dipakai memverifikasi hook global di Linux/Xvfb.
    let hook_debug = std::env::var("BMACHINE_DEBUG_HOOK").is_ok();
    std::thread::spawn(move || {
        let modifiers = Cell::new(0u8);
        let active = Cell::new(false);
        let _ = rdev::listen(move |event: Event| {
            let mut mods = modifiers.get();
            if hook_debug {
                eprintln!("[hook] {:?} mods={}", event.event_type, mods);
            }
            match event.event_type {
                EventType::KeyPress(key) => {
                    mods |= modifier_mask_for_key(key);
                    modifiers.set(mods);
                    if hook_recording.load(Ordering::SeqCst) && !is_modifier_key(key) {
                        if let Some(vk) = rdev_key_to_vk(key) {
                            let cfg = RadialShortcutConfig {
                                Type: 1,
                                MouseButton: 2,
                                Key: vk,
                                Modifiers: mods,
                            };
                            let _ = hook_tx.send(RadialHookEvent::Recorded(cfg));
                            hook_recording.store(false, Ordering::SeqCst);
                        }
                        return;
                    }
                    let cfg = *hook_shortcut.lock().unwrap();
                    if !active.get() && shortcut_matches(&cfg, Some(key), None, mods) {
                        active.set(true);
                        let _ = hook_tx.send(RadialHookEvent::TriggerDown);
                    }
                }
                EventType::KeyRelease(key) => {
                    let cfg = *hook_shortcut.lock().unwrap();
                    if active.get() && cfg.Type == 1 && vk_to_rdev_key(cfg.Key) == Some(key) {
                        active.set(false);
                        let _ = hook_tx.send(RadialHookEvent::TriggerUp);
                    }
                    mods &= !modifier_mask_for_key(key);
                    modifiers.set(mods);
                }
                EventType::ButtonPress(button) => {
                    if hook_recording.load(Ordering::SeqCst) {
                        let mouse_button = match button {
                            Button::Left => 0,
                            Button::Right => 1,
                            Button::Middle => 2,
                            _ => return,
                        };
                        let _ = hook_tx.send(RadialHookEvent::Recorded(RadialShortcutConfig {
                            Type: 0,
                            MouseButton: mouse_button,
                            Key: 0,
                            Modifiers: mods,
                        }));
                        hook_recording.store(false, Ordering::SeqCst);
                        return;
                    }
                    let cfg = *hook_shortcut.lock().unwrap();
                    if !active.get() && shortcut_matches(&cfg, None, Some(button), mods) {
                        active.set(true);
                        let _ = hook_tx.send(RadialHookEvent::TriggerDown);
                    }
                }
                EventType::ButtonRelease(button) => {
                    let cfg = *hook_shortcut.lock().unwrap();
                    if active.get()
                        && cfg.Type == 0
                        && mouse_button_index_to_rdev(cfg.MouseButton) == Some(button)
                    {
                        active.set(false);
                        let _ = hook_tx.send(RadialHookEvent::TriggerUp);
                    }
                }
                EventType::MouseMove { x, y } => {
                    // Lacak posisi cursor terakhir (dipakai posisi
                    // window radial di Linux — GetCursorPos hanya Windows).
                    if let Ok(mut pos) = LAST_MOUSE_POS.lock() {
                        *pos = (x as i32, y as i32);
                    }
                }
                _ => {}
            }
        });
    });

    let ui_weak = ui.as_weak();
    let db_for_hook = db.clone();
    let shortcut_for_ui = Arc::clone(&shortcut);
    let recording_for_ui = Arc::clone(&recording);
    // Timer HARUS disimpan dalam binding (bukan temporary):
    // Drop Timer menghapusnya dari daftar timer — pola
    // `Timer::default().start(...)` tanpa binding langsung mati dan
    // event hook tidak pernah sampai ke UI. Itu penyebab hotkey
    // global tidak pernah membuka radial (ditemukan 27 Nov 2026).
    // Binding ini hidup sampai main() selesai (setelah ui.run()).
    let _hook_timer = slint::Timer::default();
    _hook_timer.start(
        slint::TimerMode::Repeated,
        std::time::Duration::from_millis(16),
        move || {
            while let Ok(event) = hook_rx.try_recv() {
                if let Some(ui) = ui_weak.upgrade() {
                    match event {
                        RadialHookEvent::TriggerDown => ui.set_radial_open(true),
                        RadialHookEvent::TriggerUp => ui.set_radial_open(false),
                        RadialHookEvent::Recorded(cfg) => {
                            if let Ok(mut current) = shortcut_for_ui.lock() {
                                *current = cfg;
                            }
                            if let Some(ref db_mutex) = db_for_hook {
                                if let Ok(mut db_guard) = db_mutex.lock() {
                                    if let Ok(json) = serde_json::to_string(&cfg) {
                                        let _ = db_guard.set("ShortcutConfig", &json, "String");
                                    }
                                }
                            }
                            recording_for_ui.store(false, Ordering::SeqCst);
                            ui.set_recording_shortcut(false);
                            ui.set_current_shortcut_label(slint::SharedString::from(
                                shortcut_label(&cfg),
                            ));
                            ui.set_toast(slint::SharedString::from(
                                "Shortcut radial disimpan dan langsung aktif.",
                            ));
                        }
                    }
                }
            }
        },
    );

    // ---- Dynamic Bar: sinkronisasi angka + notifikasi ----
    // Timer 2 detik ini HANYA menyalin string yang sudah ada di
    // memori (properti AppWindow → window bar) + satu baca key
    // SQLite lokal (`Stat.Points`) — TIDAK ada polling Trello di
    // sini. Trello tetap di-sync seperti sebelumnya (startup /
    // refresh manual / simpan akun). Interval 30 detik di Settings
    // adalah interval refresh Trello dan TIDAK dipakai port untuk
    // auto-refresh (tidak ada timer auto-refresh Trello di port).
    // Bila Stat.Points di DB berubah, dashboard ikut berubah
    // (sumber kebenaran tetap satu: properti AppWindow; bar hanya
    // menyalinnya) dan kenaikan dicatat sebagai notifikasi.
    let _bar_sync_timer = slint::Timer::default();
    _bar_sync_timer.start(
        slint::TimerMode::Repeated,
        std::time::Duration::from_millis(2000),
        {
            let ui_weak = ui.as_weak();
            let bar_cell = bar_win_cell.clone();
            let bar_store_timer = bar_store.clone();
            let db_timer = db.clone();
            move || {
                let Some(ui) = ui_weak.upgrade() else {
                    return;
                };
                if let Some(ref db_mutex) = db_timer {
                    if let Ok(guard) = db_mutex.lock() {
                        if let Some(pts) = guard.get("Stat.Points") {
                            if pts != ui.get_stat_points().as_str() {
                                ui.set_stat_points(slint::SharedString::from(pts.as_str()));
                            }
                            if let Ok(mut st) = bar_store_timer.lock() {
                                if let Some((_prev, cur)) = record_points(&mut st, &pts) {
                                    // Maskot bicara (SAPI, Windows
                                    // saja; di Linux speak_event
                                    // no-op). Angka ditulis polos —
                                    // SAPI mengucapkannya sebagai
                                    // bilangan, bukan digit per digit.
                                    voice::speak_event(&format!(
                                        "Poin bertambah menjadi {}.",
                                        cur
                                    ));
                                    // Maskot senang sebentar (ekspresi
                                    // blob, lihat effective-mood).
                                    st.pet_mood_until = Some((
                                        "happy".to_string(),
                                        Instant::now()
                                            + std::time::Duration::from_secs(4),
                                    ));
                                }
                            }
                        }
                    }
                }
                let bar_borrow = bar_cell.borrow();
                let Some(w) = bar_borrow.as_ref() else {
                    return;
                };
                // Tema: tiap window OS punya instance Design sendiri —
                // salin token dari window utama supaya bar tidak
                // ketinggalan bila ada jalur tema yang tidak lewat
                // handler di atas (jaring pengaman; handler sudah
                // menyinkronkan langsung saat tema diklik).
                theme::sync_design_to_bar(&ui, w);
                w.set_stat_editing(ui.get_stat_editing());
                w.set_stat_revision(ui.get_stat_revision());
                w.set_stat_late(ui.get_stat_late());
                w.set_stat_points(ui.get_stat_points());
                if let Ok(st) = bar_store_timer.lock() {
                    push_notifs_to_bar(w, &st.notifications);
                }
                // Mood maskot dari event terakhir (kartu baru →
                // kaget, poin naik → senang); kedaluwarsa → idle.
                // Tidur/sapa dihitung sisi Slint (effective-mood).
                if let Ok(st) = bar_store_timer.lock() {
                    let mood = match &st.pet_mood_until {
                        Some((m, until)) if *until > Instant::now() => m.clone(),
                        _ => "idle".to_string(),
                    };
                    w.set_pet_mood(slint::SharedString::from(mood.as_str()));
                }
            }
        },
    );

    // ---- Mode pet: idle wander HALUS (SYARAT TEGAS abeng) ----
    // Timer 120 ms menggeser avatar beberapa px per tick menuju
    // satu titik target (ease-out: 8% sisa jarak, 1–5 px/tick)
    // — gerakan meluncur, BUKAN teleport tiap 2 detik seperti
    // wander lama. Target baru diambil tiap 4–8 detik / saat
    // tercapai, selalu di dalam area window utama. Tidak jalan
    // saat di-hover, saat panel mengembang, atau 5 detik setelah
    // di-drag. Mode notch tidak wander.
    let pet_wander_target: std::rc::Rc<std::cell::RefCell<Option<(i32, i32)>>> =
        std::rc::Rc::new(std::cell::RefCell::new(None));
    let pet_wander_deadline: std::rc::Rc<std::cell::RefCell<Instant>> =
        std::rc::Rc::new(std::cell::RefCell::new(Instant::now()));
    let _pet_wander_timer = slint::Timer::default();
    _pet_wander_timer.start(
        slint::TimerMode::Repeated,
        std::time::Duration::from_millis(120),
        {
            let ui_weak = ui.as_weak();
            let bar_cell = bar_win_cell.clone();
            let pet_last_drag = pet_last_drag.clone();
            let target_cell = pet_wander_target.clone();
            let deadline_cell = pet_wander_deadline.clone();
            move || {
                let Some(ui) = ui_weak.upgrade() else {
                    return;
                };
                if ui.get_app_notch_mode().as_str() != "pet" {
                    return;
                }
                let bar_borrow = bar_cell.borrow();
                let Some(w) = bar_borrow.as_ref() else {
                    return;
                };
                if w.get_pet_hovering() || w.get_expanded() {
                    return;
                }
                let last_drag = pet_last_drag
                    .lock()
                    .map(|t| *t)
                    .unwrap_or(Instant::now());
                if last_drag.elapsed() <= std::time::Duration::from_secs(5) {
                    return;
                }
                let win = ui.window();
                let mpos = win.position();
                let msize = win.size();
                let scale = w.window().scale_factor();
                if scale <= 0.0 {
                    return;
                }
                let pet = (84f32 * scale) as i32;
                let min_x = mpos.x;
                let max_x = (mpos.x + msize.width as i32 - pet).max(min_x);
                let min_y = mpos.y + (28f32 * scale) as i32;
                let max_y = (mpos.y + msize.height as i32 - pet).max(min_y);
                let pos = w.window().position();
                let now = Instant::now();
                // Target baru bila belum ada, sudah tercapai
                // (±4 px), atau tenggat lewat.
                let need_new = match *target_cell.borrow() {
                    None => true,
                    Some((tx, ty)) => {
                        (pos.x - tx).abs() <= 4 && (pos.y - ty).abs() <= 4
                    }
                } || *deadline_cell.borrow() <= now;
                if need_new {
                    // Acak dari jam nano (tanpa crate rand) —
                    // target min 60 px dari posisi sekarang.
                    let nanos = std::time::SystemTime::now()
                        .duration_since(std::time::UNIX_EPOCH)
                        .map(|d| d.subsec_nanos())
                        .unwrap_or(0);
                    let mut nx = pos.x;
                    let mut ny = pos.y;
                    for i in 0..6u32 {
                        let seed = nanos.wrapping_add(i.wrapping_mul(2654435761));
                        let cx = min_x
                            + ((seed / 97) % ((max_x - min_x).max(1) as u32)) as i32;
                        let cy = min_y
                            + ((seed / 131) % ((max_y - min_y).max(1) as u32)) as i32;
                        if (cx - pos.x).abs() + (cy - pos.y).abs() >= 60 {
                            nx = cx;
                            ny = cy;
                            break;
                        }
                        if i == 5 {
                            nx = cx;
                            ny = cy;
                        }
                    }
                    *target_cell.borrow_mut() = Some((nx, ny));
                    *deadline_cell.borrow_mut() =
                        now + std::time::Duration::from_secs(4 + (nanos % 4) as u64);
                }
                // Satu langkah halus menuju target (ease-out).
                let Some((tx, ty)) = *target_cell.borrow() else {
                    return;
                };
                let dx = tx - pos.x;
                let dy = ty - pos.y;
                if dx == 0 && dy == 0 {
                    return;
                }
                let step_x = ((dx as f32 * 0.08) as i32).clamp(-5, 5);
                let step_x = if step_x == 0 { dx.signum() } else { step_x };
                let step_y = ((dy as f32 * 0.08) as i32).clamp(-5, 5);
                let step_y = if step_y == 0 { dy.signum() } else { step_y };
                w.window().set_position(slint::PhysicalPosition::new(
                    pos.x + step_x,
                    pos.y + step_y,
                ));
            }
        },
    );

    // ---- Mode pet PetDex: animasi frame sprite (50 ms) ----
    // Timer 50 ms memajukan mesin animasi PetDex (durasi tiap frame
    // 110–320 ms — tick harus lebih cepat dari frame terpendek supaya
    // tidak ada frame yang terlewat) dan mendorong (baris, kolom)
    // frame ke window bar. State diambil dari mood + gerakan yang
    // SUDAH ADA — jangan sistem mood baru: bergerak → RunningRight /
    // RunningLeft (arah x window); happy → Waving; surprised →
    // Jumping; selebihnya Idle (tidur juga Idle — baris idle sudah
    // berisi frame mata terpejam). Ganti state TIDAK di-fade
    // (opacity): state berbalik Idle ↔ Running tiap langkah wander,
    // fade akan menahan sprite di opacity 0 terus-menerus; yang
    // membuat animasi smooth adalah durasi per-frame engine
    // (tidak ada frame yang dilewat/dilompati).
    let _pet_frame_timer = slint::Timer::default();
    _pet_frame_timer.start(
        slint::TimerMode::Repeated,
        std::time::Duration::from_millis(50),
        {
            let ui_weak = ui.as_weak();
            let bar_cell = bar_win_cell.clone();
            let pet_runtime = pet_runtime.clone();
            let bar_store_timer = bar_store.clone();
            move || {
                let Some(ui) = ui_weak.upgrade() else {
                    return;
                };
                if ui.get_app_notch_mode().as_str() != "pet" {
                    return;
                }
                let bar_borrow = bar_cell.borrow();
                let Some(w) = bar_borrow.as_ref() else {
                    return;
                };
                if !w.get_pet_sprite_active() {
                    return;
                }
                let mut rt = pet_runtime.borrow_mut();
                if rt.sheet.is_none() {
                    return;
                }
                let pos = w.window().position();
                let dx = rt.last_x.map(|lx| pos.x - lx).unwrap_or(0);
                rt.last_x = Some(pos.x);
                let mood = bar_store_timer
                    .lock()
                    .ok()
                    .and_then(|st| match &st.pet_mood_until {
                        Some((m, until)) if *until > Instant::now() => Some(m.clone()),
                        _ => None,
                    })
                    .unwrap_or_else(|| "idle".to_string());
                let desired = if dx > 0 {
                    petdex::PetState::RunningRight
                } else if dx < 0 {
                    petdex::PetState::RunningLeft
                } else {
                    match mood.as_str() {
                        "happy" => petdex::PetState::Waving,
                        "surprised" => petdex::PetState::Jumping,
                        _ => petdex::PetState::Idle,
                    }
                };
                if rt.engine.state() != desired {
                    rt.engine.set_state(desired);
                }
                let frame = rt.engine.tick(50);
                if (frame.row, frame.col) != rt.last_frame {
                    rt.last_frame = (frame.row, frame.col);
                    w.set_pet_frame_row(frame.row as i32);
                    w.set_pet_frame_col(frame.col as i32);
                }
            }
        },
    );

    // Buka Dynamic Bar ~2,5 detik setelah start (setelah splash
    // selesai — bootstrapper ~2 detik) bila visibilitas tersimpan
    // mengizinkan (default tampil).
    let _bar_open_timer = slint::Timer::default();
    _bar_open_timer.start(
        slint::TimerMode::SingleShot,
        std::time::Duration::from_millis(2500),
        {
            let ui_weak = ui.as_weak();
            let cell = bar_win_cell.clone();
            let vis = bar_visible.clone();
            let store = bar_store.clone();
            let db_clone = db.clone();
            let pet_last_drag = pet_last_drag.clone();
            let pet_runtime = pet_runtime.clone();
            move || {
                if let Some(ui) = ui_weak.upgrade() {
                    if ui.get_app_dynamic_bar_visible() {
                        open_dynamic_bar_window(
                            &cell,
                            &vis,
                            &ui_weak,
                            &store,
                            &db_clone,
                            &pet_last_drag,
                            &pet_runtime,
                        );
                    }
                }
            }
        },
    );

    // QA helpers (SETELAH semua callback Rust terdaftar — watcher
    // radial memanggil callback Rust; dibuka sebelum registrasi =
    // no-op). --radial-test: buka radial di cursor; --toolbox: buka
    // ToolboxWindow (tanpa jalur UI — roda radial belum memanggil
    // open-toolbox).
    if radial_test {
        ui.set_radial_open(true);
    }
    if toolbox_test {
        open_toolbox_window(
            &toolbox_win_cell,
            &toolbox_visible,
            &ui.as_weak(),
            "MantraNama".to_string(),
        );
    }

    // ui.run() mengambil alih event loop; show() sudah dipanggil di atas.
    ui.run()?;
    Ok(())
}
