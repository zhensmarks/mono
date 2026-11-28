slint::include_modules!();

mod batch;
mod batch_exec;
mod db;
mod doc_manager;
mod executor;
mod explorer;
mod fullscreen;
mod icons;
mod local_update;
mod panel_extra;
mod petdex;
mod platform;
mod process_log;
mod radial_geom;
mod sheets_api;
mod sheet_model;
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
use std::path::{Path, PathBuf};

// Versi aplikasi untuk footer flyout profil (C# AppVersion =
// assembly ToString(2) — v15.15.3 -> "15.15"). BUKAN versi Cargo.
const APP_VERSION: &str = "15.15";
// Versi lengkap untuk splash (C# SplashViewModel.VersionText =
// "VERSION " + Assembly Version ToString(3) -> "VERSION 15.15.3").
const APP_VERSION_FULL: &str = "15.15.3";

// Posisi cursor terakhir (physical px) — dilacak hook rdev (MouseMove).
// Dipakai cursor_screen_position() di Linux (GetCursorPos hanya Windows).
static LAST_MOUSE_POS: Mutex<(i32, i32)> = Mutex::new((640, 400));

// Modifier keyboard saat ini (Shift/Ctrl) — dilacak hook rdev
// (KeyPress/KeyRelease). Dipakai BATCH: Slint TouchArea tidak
// mengekspos modifier key, jadi klik baris tree membaca state ini
// dari Rust (LANJUTKAN §10: Shift range + Ctrl toggle).
static BATCH_SHIFT_HELD: AtomicBool = AtomicBool::new(false);
static BATCH_CTRL_HELD: AtomicBool = AtomicBool::new(false);
/// Klik terakhir BATCH untuk deteksi klik ganda (panel, id, waktu) —
/// Slint `TouchArea.double-clicked` tidak bisa diandalkan di sini (lihat
/// `batch_handle_double_click`).
static BATCH_LAST_CLICK: Mutex<Option<(String, String, std::time::Instant)>> =
    Mutex::new(None);

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

/// Parse angka stat ("24", "4.180", "-") → f64; gagal → 0.
fn parse_stat_num(s: &str) -> f64 {
    let t = s.trim().replace('.', "").replace(',', "");
    t.parse::<f64>().unwrap_or(0.0)
}

/// Perintah Path busur gauge TERISI (DESIGN_SYSTEM.md §22): busur elips
/// parsial dari (6,40) ke titik ujung rasio — SAMA persis dengan busur
/// track penuh ("M 0 40 A 50 28 0 0 1 100 40"), hanya titik akhirnya
/// mengikuti rasio. Dihitung di Rust (bukan di .slint) karena .slint
/// tidak bisa mengubah angka jadi string untuk `commands`.
/// Geometri SAMA dengan properti .slint gauge-theta/gauge-end-x/
/// gauge-end-y di StatWidget (dot memakai rumus yang sama) — keduanya
/// harus tetap sinkron. Rasio di-clamp 0..1.
/// Geometri ujung-ke-ujung (koreksi abeng 28 Nov 13:07, ref Stress):
/// busur dari x=0 (tepi kiri card) ke x=100 (tepi kanan card), rx=50,
/// tanpa margin garis — dulu (6,40)→(94,40), rx=44, arc hanya ~80%
/// lebar card. Puncak tetap (50,12), baseline y=40.
fn gauge_arc_commands(ratio: f32) -> String {
    let r = ratio.clamp(0.0, 1.0);
    let theta = (1.0 - r) * std::f32::consts::PI;
    let end_x = 50.0 + 50.0 * theta.cos();
    let end_y = 40.0 - 28.0 * theta.sin();
    format!("M 0 40 A 50 28 0 0 1 {end_x:.2} {end_y:.2}")
}

/// Rasio gauge template widget (DESIGN_SYSTEM.md §22): tiap nilai
/// dibagi nilai TERBESAR keempat widget (EDITING/REVISION/LATE/
/// POINTS) → 0..1. Dipanggil timer sinkronisasi bar tiap 2 detik,
/// jadi rasio menyusul perubahan stat paling lambat 2 detik — sama
/// seperti salinan stat ke bar. Semua nol → semua rasio 0.
fn update_gauge_ratios(ui: &AppWindow) {
    let e = parse_stat_num(&ui.get_stat_editing());
    let r = parse_stat_num(&ui.get_stat_revision());
    let l = parse_stat_num(&ui.get_stat_late());
    let p = parse_stat_num(&ui.get_stat_points());
    let max = e.max(r).max(l).max(p);
    let ratio = |v: f64| if max > 0.0 { (v / max) as f32 } else { 0.0f32 };
    ui.set_gauge_editing(ratio(e));
    ui.set_gauge_revision(ratio(r));
    ui.set_gauge_late(ratio(l));
    ui.set_gauge_points(ratio(p));
    ui.set_gauge_arc_editing(slint::SharedString::from(gauge_arc_commands(ratio(e))));
    ui.set_gauge_arc_revision(slint::SharedString::from(gauge_arc_commands(ratio(r))));
    ui.set_gauge_arc_late(slint::SharedString::from(gauge_arc_commands(ratio(l))));
    ui.set_gauge_arc_points(slint::SharedString::from(gauge_arc_commands(ratio(p))));
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
const SHEET_DEFAULT_ROWS: [[&str; 7]; 0] = [];

fn sheet_model(v: Vec<String>) -> slint::ModelRc<slint::SharedString> {
    slint::ModelRc::new(slint::VecModel::from(
        v.into_iter().map(slint::SharedString::from).collect::<Vec<_>>(),
    ))
}

fn sheet_model_bool(v: Vec<bool>) -> slint::ModelRc<bool> {
    slint::ModelRc::new(slint::VecModel::from(v))
}

fn sheet_int_model(v: Vec<i32>) -> slint::ModelRc<i32> {
    slint::ModelRc::new(slint::VecModel::from(v))
}

/// Model nested [[string]] untuk grid Slint (satu baris = satu model
/// dalam).
fn str_nested_model(rows: &[Vec<String>]) -> slint::ModelRc<slint::ModelRc<slint::SharedString>> {
    slint::ModelRc::new(slint::VecModel::from(
        rows.iter().map(|r| sheet_model(r.clone())).collect::<Vec<_>>(),
    ))
}

/// Baca model nested [[string]] dari Slint kembali ke Vec<Vec<String>>.
fn read_nested_str_model(
    m: slint::ModelRc<slint::ModelRc<slint::SharedString>>,
) -> Vec<Vec<String>> {
    m.iter()
        .map(|row| row.iter().map(|s| s.to_string()).collect())
        .collect()
}

fn float_model(v: Vec<f32>) -> slint::ModelRc<f32> {
    slint::ModelRc::new(slint::VecModel::from(v))
}

/// Offset-x kumulatif (px) tiap kolom dari lebarnya.
fn col_x_offsets(cols: &[sheet_model::DynColumn]) -> Vec<f32> {
    let mut offs = Vec::with_capacity(cols.len());
    let mut acc = 0.0f32;
    for c in cols {
        offs.push(acc);
        acc += c.width_px;
    }
    offs
}

/// Hit-test kolom dari posisi x (px, koordinat isi Flickable):
/// x < 43px = gutter -> -1; selebihnya jalan di offset kumulatif
/// lebar kolom. Dihitung Rust karena Slint 1.19 tidak punya `for`
/// di dalam fungsi.
fn sheet_hit_col(x_px: f32, cols: &[sheet_model::DynColumn]) -> i32 {
    let px = x_px - 43.0;
    if px < 0.0 {
        return -1;
    }
    let mut acc = 0.0f32;
    for (i, c) in cols.iter().enumerate() {
        if px < acc + c.width_px {
            return i as i32;
        }
        acc += c.width_px;
    }
    cols.len() as i32 - 1
}

/// Hitung model sel-rows baru dari seleksi lama.
/// mode 0 = satu baris (klik biasa), 1 = tambah rentang anchor..r
/// (Shift+klik/seret), 2 = balik baris (Ctrl+klik).
fn sheet_select_rows(current: &[bool], row: usize, mode: i32, anchor: usize) -> Vec<bool> {
    let mut sel = current.to_vec();
    if row >= sel.len() || sel.is_empty() {
        return sel;
    }
    let anchor = anchor.min(sel.len() - 1);
    match mode {
        1 => {
            let (lo, hi) = if anchor <= row { (anchor, row) } else { (row, anchor) };
            for i in lo..=hi.min(sel.len() - 1) {
                sel[i] = true;
            }
        }
        2 => {
            sel[row] = !sel[row];
        }
        _ => {
            for v in sel.iter_mut() {
                *v = false;
            }
            sel[row] = true;
        }
    }
    sel
}

/// Label halaman ("Hal 1 dari 164") — dihitung Rust.
fn page_label(st: &spreadsheet_guard::SpreadsheetState, total: usize) -> String {
    if total == 0 {
        return "Hal 0 dari 0".to_string();
    }
    let pages = total.div_ceil(st.page_size);
    let cur = st.page_start / st.page_size + 1;
    format!("Hal {} dari {}", cur, pages)
}

/// Dorong definisi kolom + paging dari state ke window utama.
/// Dipanggil tiap push grid (kolom bisa berubah saat beralih
/// lokal <-> live).
fn push_sheet_defs_main(ui: &AppWindow, st: &spreadsheet_guard::SpreadsheetState) {
    let cols = &st.columns;
    ui.set_sheet_col_headers(sheet_model(
        cols.iter().map(|c| c.header.clone()).collect(),
    ));
    ui.set_sheet_col_letters(sheet_model(
        cols.iter().map(|c| c.letter.clone()).collect(),
    ));
    ui.set_sheet_col_kinds(sheet_model(
        cols.iter().map(|c| c.kind.as_str().to_string()).collect(),
    ));
    ui.set_sheet_col_locked(sheet_model_bool(cols.iter().map(|c| c.locked).collect()));
    ui.set_sheet_col_widths(float_model(cols.iter().map(|c| c.width_px).collect()));
    ui.set_sheet_col_x_offsets(float_model(col_x_offsets(cols)));
    ui.set_sheet_col_items(str_nested_model(
        &cols.iter().map(|c| c.items.clone()).collect::<Vec<_>>(),
    ));
    ui.set_sheet_key_col(st.key_column.map(|k| k as i32).unwrap_or(-1));
    ui.set_sheet_page_start(st.page_start as i32);
    ui.set_sheet_page_size(st.page_size as i32);
    ui.set_sheet_total_rows(st.loaded.len() as i32);
    ui.set_sheet_row_num_base(st.row_num_base());
    ui.set_sheet_page_label(slint::SharedString::from(page_label(st, st.loaded.len())));
}

/// Sama untuk SpreadsheetWindow.
fn push_sheet_defs_win(w: &SpreadsheetWindow, st: &spreadsheet_guard::SpreadsheetState) {
    let cols = &st.columns;
    w.set_col_headers(sheet_model(
        cols.iter().map(|c| c.header.clone()).collect(),
    ));
    w.set_col_letters(sheet_model(
        cols.iter().map(|c| c.letter.clone()).collect(),
    ));
    w.set_col_kinds(sheet_model(
        cols.iter().map(|c| c.kind.as_str().to_string()).collect(),
    ));
    w.set_col_locked(sheet_model_bool(cols.iter().map(|c| c.locked).collect()));
    w.set_col_widths(float_model(cols.iter().map(|c| c.width_px).collect()));
    w.set_col_x_offsets(float_model(col_x_offsets(cols)));
    w.set_col_items(str_nested_model(
        &cols.iter().map(|c| c.items.clone()).collect::<Vec<_>>(),
    ));
    w.set_key_col(st.key_column.map(|k| k as i32).unwrap_or(-1));
    w.set_page_start(st.page_start as i32);
    w.set_page_size(st.page_size as i32);
    w.set_total_rows(st.loaded.len() as i32);
    w.set_row_num_base(st.row_num_base());
    w.set_page_label(slint::SharedString::from(page_label(st, st.loaded.len())));
}

/// Dorong state kalender (bulan yang ditampilkan + sel hari) ke window
/// utama. `selected_day`: hari yang disorot — di-clamp ke jumlah hari
/// bulan itu (mis. pindah dari 31 Jan ke Feb jadi 28).
fn push_cal_state_main(ui: &AppWindow, year: i32, month: u32, selected_day: u32) {
    let days = sheet_model::days_in_month(year, month) as u32;
    let sel = selected_day.clamp(1, days);
    let (ty, tm, td) = sheet_model::today_ymd();
    ui.set_sheet_cal_year(year);
    ui.set_sheet_cal_month(month as i32);
    ui.set_sheet_cal_label(slint::SharedString::from(sheet_model::month_label(year, month)));
    ui.set_sheet_cal_cells(sheet_int_model(sheet_model::month_grid(year, month)));
    ui.set_sheet_cal_today(if year == ty && month == tm { td as i32 } else { 0 });
    ui.set_sheet_cal_days_in_month(days as i32);
    ui.set_sheet_cal_today_iso(slint::SharedString::from(sheet_model::today_iso()));
    ui.set_sheet_cal_selected_day(sel as i32);
}

/// Sama untuk SpreadsheetWindow.
fn push_cal_state_win(w: &SpreadsheetWindow, year: i32, month: u32, selected_day: u32) {
    let days = sheet_model::days_in_month(year, month) as u32;
    let sel = selected_day.clamp(1, days);
    let (ty, tm, td) = sheet_model::today_ymd();
    w.set_cal_year(year);
    w.set_cal_month(month as i32);
    w.set_cal_label(slint::SharedString::from(sheet_model::month_label(year, month)));
    w.set_cal_cells(sheet_int_model(sheet_model::month_grid(year, month)));
    w.set_cal_today(if year == ty && month == tm { td as i32 } else { 0 });
    w.set_cal_days_in_month(days as i32);
    w.set_cal_today_iso(slint::SharedString::from(sheet_model::today_iso()));
    w.set_cal_selected_day(sel as i32);
}

/// Tambah baris kosong di AKHIR data penuh (bukan akhir jendela —
/// bila jendela bukan halaman terakhir, lompat dulu ke halaman
/// terakhir). Baris baru belum ada di sheet — nilai aslinya ""
/// (jangkar UI saja; `st.loaded` tidak diubah sampai Simpan, supaya
/// guard tidak menganggap sheet berubah). Dipakai tombol "+ Baris"
/// di kedua tampilan. Mengembalikan jumlah baris jendela.
fn add_sheet_row(
    st: &mut spreadsheet_guard::SpreadsheetState,
    ui: &AppWindow,
    sheet_win: &std::rc::Rc<std::cell::RefCell<Option<SpreadsheetWindow>>>,
    grid: Vec<Vec<String>>,
) -> usize {
    // Baris baru di akhir data penuh.
    let full_len = st.loaded.len().max(st.page_start + grid.len());
    let last_start = full_len.saturating_sub(1) / st.page_size * st.page_size;
    let mut display = if st.page_start != last_start {
        st.page_start = last_start;
        st.window_rows(&sheet_display_rows(st))
    } else {
        grid
    };
    display.push(vec![String::new(); st.column_count]);
    // Nilai asli per baris tampil: baris baru + baris-baris baru
    // sebelumnya yang belum tersimpan semuanya "".
    let mut orig = st.window_rows(&st.loaded);
    while orig.len() < display.len() {
        orig.push(vec![String::new(); st.column_count]);
    }
    push_window_grid_to_main(ui, st, &display, &orig);
    if let Some(w) = sheet_win.borrow().as_ref() {
        push_window_grid_to_sheet_win(w, st, &display, &orig);
    }
    display.len()
}

/// Terapkan daftar edit sel (r, c, nilai baru) ke state bersama: catat
/// draft (nilai asli = grid saat ini), persist, dorong grid baru ke
/// kedua tampilan + perbarui status draft. Dipakai fill-down dan
/// clear-cells — keduanya edit massal dari seleksi. Mengembalikan
/// jumlah sel yang benar-benar berubah.
fn apply_sheet_edits_and_refresh(
    st: &mut spreadsheet_guard::SpreadsheetState,
    db: &Option<Arc<Mutex<DatabaseService>>>,
    ui: &AppWindow,
    sheet_win: &std::rc::Rc<std::cell::RefCell<Option<SpreadsheetWindow>>>,
    grid: &[Vec<String>],
    edits: &[(usize, usize, String)],
) -> usize {
    let mut changed = 0;
    for (r, c, val) in edits {
        let orig = grid
            .get(*r)
            .and_then(|row| row.get(*c))
            .map(|s| s.as_str())
            .unwrap_or("");
        // Edit (r, c) dari jendela halaman -> baris absolut.
        if st.record_edit(*r + st.page_start, *c, orig, val) {
            changed += 1;
        }
    }
    if changed > 0 {
        persist_sheet_drafts(db, &st.drafts);
        let display = sheet_display_rows(st);
        push_grid_to_main(ui, st, &display);
        ui.set_sheet_has_draft(!st.drafts.is_empty());
        ui.set_sheet_draft_count(st.drafts.len() as i32);
        if let Some(w) = sheet_win.borrow().as_ref() {
            push_grid_to_sheet_win(w, st, &display);
            w.set_sheet_has_draft(!st.drafts.is_empty());
            w.set_sheet_draft_count(st.drafts.len() as i32);
        }
    }
    changed
}

/// Baca grid tampil (jendela halaman, nested model) dari window utama.
fn grid_from_main(ui: &AppWindow) -> Vec<Vec<String>> {
    read_nested_str_model(ui.get_sheet_grid())
}

/// Baca grid tampil (jendela halaman, nested model) dari SpreadsheetWindow.
fn grid_from_sheet_win(w: &SpreadsheetWindow) -> Vec<Vec<String>> {
    read_nested_str_model(w.get_grid())
}

/// Gabungkan grid jendela halaman kembali ke grid penuh (dipakai
/// Simpan dengan paging): baris halaman menimpa irisan yang sama di
/// grid penuh; baris baru di luar grid penuh ditambahkan di akhir
/// (dengan celah diisi baris kosong bila ada lompatan).
fn merge_page_into_full(
    full: &[Vec<String>],
    page: Vec<Vec<String>>,
    page_start: usize,
    col_count: usize,
) -> Vec<Vec<String>> {
    let mut out = full.to_vec();
    for (i, row) in page.into_iter().enumerate() {
        let idx = page_start + i;
        let mut r = row;
        r.resize(col_count, String::new());
        if idx < out.len() {
            out[idx] = r;
        } else {
            while out.len() < idx {
                out.push(vec![String::new(); col_count]);
            }
            out.push(r);
        }
    }
    out
}

/// Dorong jendela halaman (sudah diiris) + definisi kolom + paging ke
/// window utama. Seleksi dipertahankan sepanjang mungkin (ukuran =
/// jendela; baris pertama terpilih bila sebelumnya kosong).
fn push_window_grid_to_main(
    ui: &AppWindow,
    st: &spreadsheet_guard::SpreadsheetState,
    win_display: &[Vec<String>],
    win_loaded: &[Vec<String>],
) {
    ui.set_sheet_grid(str_nested_model(win_display));
    ui.set_sheet_grid_orig(str_nested_model(win_loaded));
    push_sheet_defs_main(ui, st);
    let mut sel = read_bool_model(ui.get_sheet_sel_rows());
    sel.resize(win_display.len(), false);
    if sel.iter().all(|s| !*s) && !win_display.is_empty() {
        sel[0] = true;
    }
    ui.set_sheet_sel_rows(sheet_model_bool(sel));
}

/// Sama untuk SpreadsheetWindow.
fn push_window_grid_to_sheet_win(
    w: &SpreadsheetWindow,
    st: &spreadsheet_guard::SpreadsheetState,
    win_display: &[Vec<String>],
    win_loaded: &[Vec<String>],
) {
    w.set_grid(str_nested_model(win_display));
    w.set_grid_orig(str_nested_model(win_loaded));
    push_sheet_defs_win(w, st);
    let mut sel = read_bool_model(w.get_sel_rows());
    sel.resize(win_display.len(), false);
    if sel.iter().all(|s| !*s) && !win_display.is_empty() {
        sel[0] = true;
    }
    w.set_sel_rows(sheet_model_bool(sel));
}

/// Dorong baris tampil + baris asli (penuh — diiris ke jendela halaman
/// di sini) ke window utama.
fn push_grid_to_main(
    ui: &AppWindow,
    st: &spreadsheet_guard::SpreadsheetState,
    display: &[Vec<String>],
) {
    let win_display = st.window_rows(display);
    let win_loaded = st.window_rows(&st.loaded);
    push_window_grid_to_main(ui, st, &win_display, &win_loaded);
}

/// Dorong baris tampil + baris asli (penuh) ke SpreadsheetWindow.
fn push_grid_to_sheet_win(
    w: &SpreadsheetWindow,
    st: &spreadsheet_guard::SpreadsheetState,
    display: &[Vec<String>],
) {
    let win_display = st.window_rows(display);
    let win_loaded = st.window_rows(&st.loaded);
    push_window_grid_to_sheet_win(w, st, &win_display, &win_loaded);
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
            headers: Vec::new(),
            key_column: None,
            first_sheet_row: 1,
        };
    };
    let Ok(guard) = db_mutex.lock() else {
        return spreadsheet_guard::SheetSnapshot {
            sheet_id: sheet_id.to_string(),
            sheet_name: sheet_name.to_string(),
            rows: default_rows,
            headers: Vec::new(),
            key_column: None,
            first_sheet_row: 1,
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
        headers: Vec::new(),
        key_column: None,
        first_sheet_row: 1,
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
        state.key_column,
    );
    display
}

/// Kolom angka pertama dari state (live: QTY/dll; lokal: POIN col 4)
/// — dipakai auto-sum. Mengembalikan (indeks, nama header).
fn first_number_column(
    st: &spreadsheet_guard::SpreadsheetState,
) -> (usize, String) {
    st.columns
        .iter()
        .enumerate()
        .find(|(_, c)| c.kind == sheet_model::SheetColKind::Number)
        .map(|(i, c)| (i, c.header.clone()))
        .unwrap_or((4, "POIN".to_string()))
}

/// Pindah halaman grid (delta +1/-1); dorong ulang kedua tampilan bila
/// berubah (no-op bila sudah di ujung).
fn turn_sheet_page(
    st: &mut spreadsheet_guard::SpreadsheetState,
    ui: &AppWindow,
    sheet_win: &std::rc::Rc<std::cell::RefCell<Option<SpreadsheetWindow>>>,
    delta: i32,
) {
    let total = st.loaded.len();
    if !st.turn_page(delta, total) {
        return;
    }
    let display = sheet_display_rows(st);
    push_grid_to_main(ui, st, &display);
    if let Some(w) = sheet_win.borrow().as_ref() {
        push_grid_to_sheet_win(w, st, &display);
    }
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
        headers: state.headers.clone(),
        key_column: state.key_column,
        first_sheet_row: state.first_sheet_row,
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

// ================= Google Sheets LIVE (28 Nov 2026 — jantung kerjaan) =================
// Data spreadsheet + POINTS harus live dari Sheets API (seperti C#
// SpreadsheetViewModel / PointLeaderboardViewModel), bukan snapshot
// lokal. Alur: fetch di thread latar (UI tidak beku — splash tidak
// boleh tertahan) -> hasil dikirim lewat channel mpsc -> timer inbox
// (250 ms, jalan di thread UI) menerapkannya ke state. State adalah
// Rc<RefCell<..>> (tidak Send) jadi TIDAK BISA disentuh dari thread
// fetch — semua mutasi state terjadi di tick timer.

/// Konfigurasi Google Sheets dari DB — dibaca SEGAR tiap sinkronisasi
/// supaya perubahan di Settings langsung berlaku tanpa restart.
struct SheetsConfig {
    sheet_id: String,
    /// Nama tab spreadsheet (Spreadsheet.SheetName).
    sheet_name: String,
    /// Range tab (Spreadsheet.Range, default A1:Z).
    range: String,
    creds_path: String,
    /// Tab poin (Google.SheetName, default EDITOR HARIAN).
    points_sheet: String,
    /// Kolom poin (Google.SheetColumn, default J).
    points_col: String,
    /// Baris poin (Google.SheetRow, default 14).
    points_row: String,
    /// Override base URL API (Google.ApiBase) — default Google asli;
    /// dipakai mock server lokal saat verifikasi (pola Trello.BaseUrl).
    api_base: String,
    /// Override URL token (Google.TokenUrl).
    token_url: String,
}

impl SheetsConfig {
    fn from_db(db: &Option<Arc<Mutex<DatabaseService>>>) -> Self {
        let mut cfg = SheetsConfig {
            sheet_id: String::new(),
            sheet_name: "ALL DATA REGULER".to_string(),
            range: "A1:Z".to_string(),
            creds_path: String::new(),
            points_sheet: "EDITOR HARIAN".to_string(),
            points_col: "J".to_string(),
            points_row: "14".to_string(),
            api_base: sheets_api::DEFAULT_API_BASE.to_string(),
            token_url: sheets_api::DEFAULT_TOKEN_URL.to_string(),
        };
        if let Some(db_mutex) = db.as_ref() {
            if let Ok(g) = db_mutex.lock() {
                let get = |k: &str| g.get(k).filter(|s| !s.is_empty());
                if let Some(v) = get("Google.SheetId") {
                    cfg.sheet_id = v;
                }
                if let Some(v) = get("Spreadsheet.SheetName") {
                    cfg.sheet_name = v;
                }
                if let Some(v) = get("Spreadsheet.Range") {
                    cfg.range = v;
                }
                if let Some(v) = get("Google.CredsPath") {
                    cfg.creds_path = v;
                }
                if let Some(v) = get("Google.SheetName") {
                    cfg.points_sheet = v;
                }
                if let Some(v) = get("Google.SheetColumn") {
                    cfg.points_col = v;
                }
                if let Some(v) = get("Google.SheetRow") {
                    cfg.points_row = v;
                }
                if let Some(v) = get("Google.ApiBase") {
                    cfg.api_base = v;
                }
                if let Some(v) = get("Google.TokenUrl") {
                    cfg.token_url = v;
                }
            }
        }
        cfg
    }

    /// Lengkap untuk fetch live? (ID + file kredensial ada; "LOCAL"
    /// = model lokal tanpa sheet.)
    fn is_live_ready(&self) -> bool {
        !self.sheet_id.is_empty()
            && self.sheet_id != "LOCAL"
            && !self.creds_path.is_empty()
    }

    fn values_range(&self) -> String {
        format!("{}!{}", self.sheet_name, self.range)
    }

    fn points_range(&self) -> String {
        format!("{}!{}{}", self.points_sheet, self.points_col, self.points_row)
    }

    /// Nomor baris sheet (1-based) awal range (A1:Z -> 1, A3:Z -> 3).
    /// Dipakai menghitung nomor baris sheet absolut dari header_row
    /// (indeks di dalam values yang diambil).
    fn range_start_row(&self) -> i32 {
        range_start_row(&self.range)
    }
}

/// Nomor baris awal sebuah range A1 ("A1:Z" -> 1, "A3:Z" -> 3,
/// "J14" -> 14; tanpa angka -> 1).
fn range_start_row(range: &str) -> i32 {
    let after_bang = range.split('!').nth(1).unwrap_or(range);
    let digits: String = after_bang
        .chars()
        .skip_while(|c| !c.is_ascii_digit())
        .take_while(|c| c.is_ascii_digit())
        .collect();
    digits.parse::<i32>().unwrap_or(1).max(1)
}

/// Lebar kolom tersimpan (C# Configs.Spreadsheet.ColumnWidths =
/// JSON Dictionary<string,double> {"0":177,"1":103.2,...}).
fn read_column_widths(
    db: &Option<Arc<Mutex<DatabaseService>>>,
) -> std::collections::HashMap<usize, f32> {
    let mut out = std::collections::HashMap::new();
    if let Some(db_mutex) = db.as_ref() {
        if let Ok(g) = db_mutex.lock() {
            if let Some(raw) = g.get("Configs.Spreadsheet.ColumnWidths") {
                // C# kadang menyimpan string JSON yang di-escape
                // (string di dalam string) — kupas satu lapis bila perlu.
                let mut s = raw.trim().to_string();
                if s.starts_with('"') && s.ends_with('"') {
                    if let Ok(inner) = serde_json::from_str::<String>(&s) {
                        s = inner;
                    }
                }
                if let Ok(map) =
                    serde_json::from_str::<std::collections::HashMap<String, f64>>(&s)
                {
                    for (k, v) in map {
                        if let Ok(i) = k.parse::<usize>() {
                            out.insert(i, v as f32);
                        }
                    }
                }
            }
        }
    }
    out
}

/// Hasil fetch live: sheet (header + baris data + kolom dinamis) dan
/// nilai POINTS (satu sel EDITOR HARIAN!J14; None = gagal dibaca).
struct LiveFetch {
    live: sheets_api::LiveSheet,
    points: Option<String>,
    /// Nomor baris sheet (1-based) baris data pertama.
    first_sheet_row: i32,
}

/// Fetch sheet + POINTS dari Sheets API (jalan di thread latar).
/// Mengembalikan Err bila kredensial/ID tidak lengkap atau API gagal.
fn fetch_live_sheets(cfg: &SheetsConfig) -> Result<LiveFetch, String> {
    let creds = sheets_api::ServiceAccount::from_file(&cfg.creds_path)?;
    let mut client =
        sheets_api::SheetsClient::new(creds).with_bases(&cfg.api_base, &cfg.token_url);
    let values = client.get_values(&cfg.sheet_id, &cfg.values_range())?;
    let widths = std::collections::HashMap::new();
    let live = sheets_api::parse_live_sheet(values, &widths);
    if live.headers.is_empty() {
        return Err(
            "Sheet tidak mengembalikan header — periksa nama tab / range di Settings (Google Integration)"
                .to_string(),
        );
    }
    let points = client.get_cell(&cfg.sheet_id, &cfg.points_range()).ok();
    Ok(LiveFetch {
        first_sheet_row: cfg.range_start_row() + live.header_row as i32 + 1,
        live,
        points,
    })
}

/// Terapkan hasil fetch live ke state: kolom dinamis + kunci + jangkar
/// dibangun ulang, draft dipulihkan berdasarkan kunci (ANTI-MENCLOK),
/// snapshot lokal diperbarui ke data live terakhir (dipakai saat
/// offline). Mengembalikan (baris_tampil, diterapkan, sisa).
fn apply_live_to_state(
    st: &mut spreadsheet_guard::SpreadsheetState,
    db: &Option<Arc<Mutex<DatabaseService>>>,
    fetch: &LiveFetch,
) -> (Vec<Vec<String>>, usize, Vec<spreadsheet_guard::DraftEntry>) {
    let widths = read_column_widths(db);
    let widths_vec: Vec<f32> = fetch
        .live
        .columns
        .iter()
        .enumerate()
        .map(|(i, c)| widths.get(&i).copied().unwrap_or(c.width_px))
        .collect();
    let cols = sheets_api::to_dyn_columns(&fetch.live, Some(&widths_vec));
    st.set_live_columns(
        cols,
        fetch.live.key_col,
        fetch.live.headers.clone(),
        fetch.first_sheet_row,
    );
    let (display, applied, remaining) = st.reload(fetch.live.rows.clone());
    persist_sheet_snapshot(
        db,
        &spreadsheet_guard::SheetSnapshot {
            sheet_id: st.sheet_id.clone(),
            sheet_name: st.sheet_name.clone(),
            rows: fetch.live.rows.clone(),
            headers: fetch.live.headers.clone(),
            key_column: fetch.live.key_col,
            first_sheet_row: fetch.first_sheet_row,
        },
    );
    persist_sheet_drafts(db, &st.drafts);
    (display, applied, remaining)
}

/// Status jujur saat offline (dipakai startup + refresh gagal +
/// SpreadsheetWindow dibuka tanpa koneksi): jumlah baris lokal
/// ditampilkan APA ADANYA bersama keterangan belum terhubung —
//  status lama ("Belum terhubung ke Google Sheets" berdampingan
/// dengan 6 baris tanpa penjelasan) adalah bug tampilan.
fn offline_sheet_status(st: &spreadsheet_guard::SpreadsheetState) -> String {
    format!(
        "Data lokal ({} baris) · Belum terhubung ke Google Sheets",
        st.loaded.len()
    )
}

/// Status setelah fetch live berhasil (dipakai juga sebagai status
/// SpreadsheetWindow saat dibuka dalam mode live).
fn live_sheet_status(
    st: &spreadsheet_guard::SpreadsheetState,
    applied: usize,
    remaining: &[spreadsheet_guard::DraftEntry],
) -> String {
    let base = format!(
        "✓ Terhubung ke Google Sheets — {} baris · {} kolom ({})",
        st.loaded.len(),
        st.column_count,
        st.sheet_name
    );
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
            "{}. {} perubahan draft dipulihkan, belum di-push ke sheet.{}",
            base, applied, leftover
        )
    } else {
        base
    }
}

/// Cari indeks baris berdasarkan kunci tetap (NO FAKTUR) — ANTI-MENCLOK:
/// posisi baris tidak pernah dipakai untuk mengenali order.
fn find_row_by_key(
    rows: &[Vec<String>],
    key_col: Option<usize>,
    key: &str,
) -> Option<usize> {
    let k = key_col?;
    if key.is_empty() {
        return None;
    }
    rows.iter()
        .position(|r| r.get(k).map(|v| v.as_str()) == Some(key))
}

/// Rencana tulis live: update sel (range A1 -> nilai) + baris baru
/// (append). `pushed_drafts` = snapshot draft saat rencana dibuat —
/// dipakai mendeteksi draft yang masuk SELAMA save flight.
#[derive(Debug)]
struct LiveSavePlan {
    updates: Vec<(String, String)>,
    appends: Vec<Vec<String>>,
    draft_snapshot: Vec<spreadsheet_guard::DraftEntry>,
}

/// Hitung rencana tulis dari draft + grid penuh (jalan di thread UI,
/// setelah fresh fetch tiba). Err = simpan DIBLOKIR (pesan menyebut
/// NO FAKTUR-nya + minta Muat Ulang dulu).
fn compute_live_save(
    st: &spreadsheet_guard::SpreadsheetState,
    live: &sheets_api::LiveSheet,
    merged: &[Vec<String>],
    range_start_row: i32,
) -> Result<LiveSavePlan, String> {
    let first_row = range_start_row + live.header_row as i32 + 1;
    let mut updates: Vec<(String, String)> = Vec::new();
    // Draft berkunci -> update sel di baris yang KUNCINYA sama (bukan
    // nomor baris — sisipan di atas tidak menggeser).
    for d in &st.drafts {
        if d.row_key.is_empty() {
            continue; // baris baru — ditangani append di bawah
        }
        let Some(idx) = find_row_by_key(&live.rows, live.key_col, &d.row_key) else {
            return Err(format!(
                "NO FAKTUR {} tidak ditemukan lagi di sheet — barisnya mungkin dihapus dari luar. Muat Ulang dulu, lalu simpan kembali.",
                d.row_key
            ));
        };
        let Some(col) = live.columns.get(d.column_index as usize) else {
            continue;
        };
        let range = format!(
            "{}!{}{}",
            st.sheet_name,
            col.letter,
            first_row + idx as i32
        );
        updates.push((range, d.value.clone()));
    }
    // Baris baru = baris di grid yang kunci (NO FAKTUR)-nya masih
    // kosong. Baris baru TANPA NO FAKTUR memblokir simpan — tanpa
    // kunci, edit berikutnya tidak bisa dilacak (dan C# pun
    // mengharuskan NO FAKTUR terisi sebelum order dikenal).
    let mut appends: Vec<Vec<String>> = Vec::new();
    if let Some(kc) = live.key_col {
        for row in merged {
            let key = row.get(kc).map(|v| v.trim()).unwrap_or("");
            if key.is_empty() {
                // Baris yang seluruhnya kosong bukan order — lewati
                // (baris kosong di ekor grid).
                if row.iter().all(|c| c.trim().is_empty()) {
                    continue;
                }
                return Err(
                    "Ada baris baru tanpa NO FAKTUR — isi dulu NO FAKTUR baris baru sebelum menyimpan."
                        .to_string(),
                );
            }
        }
        // Baris baru yang kuncinya SUDAH terisi tapi belum ada di sheet
        // (belum pernah di-append) -> append. Baris yang kuncinya sudah
        // ada di live.rows bukan baris baru.
        for row in merged {
            let key = row.get(kc).map(|v| v.trim()).unwrap_or("");
            if key.is_empty() {
                continue;
            }
            if find_row_by_key(&live.rows, live.key_col, key).is_none() {
                appends.push(row.clone());
            }
        }
    }
    Ok(LiveSavePlan {
        updates,
        appends,
        draft_snapshot: st.drafts.clone(),
    })
}

/// Jalankan tulis live (jalan di thread latar): batchUpdate untuk sel
/// draft + append untuk baris baru.
fn push_live_updates(
    cfg: &SheetsConfig,
    plan: &LiveSavePlan,
) -> Result<(), String> {
    let creds = sheets_api::ServiceAccount::from_file(&cfg.creds_path)?;
    let mut client =
        sheets_api::SheetsClient::new(creds).with_bases(&cfg.api_base, &cfg.token_url);
    client.update_cells(&cfg.sheet_id, &plan.updates)?;
    for row in &plan.appends {
        client.append_row(&cfg.sheet_id, &cfg.values_range(), row)?;
    }
    Ok(())
}

/// Pesan yang dikirim thread fetch/tulis ke inbox timer (thread UI).
enum SheetsInboxMsg {
    /// Fetch startup/refresh berhasil.
    SyncLive(LiveFetch),
    /// Fetch startup/refresh gagal (state tidak disentuh).
    SyncFailed(String),
    /// Konfigurasi belum lengkap — mode lokal, status jujur.
    SyncOffline,
    /// Fresh fetch untuk Simpan berhasil (grid penuh ikut serta).
    SaveFresh {
        live: sheets_api::LiveSheet,
        range_start_row: i32,
        merged: Vec<Vec<String>>,
    },
    /// Fresh fetch untuk Simpan gagal.
    SaveFreshFailed(String),
    /// batchUpdate + append selesai.
    SavePushed,
    /// batchUpdate / append gagal (draft dipertahankan).
    SavePushFailed(String),
}

/// Konteks simpan live yang menunggu giliran kedua (setelah push
/// selesai): grid penuh saat Simpan diklik + snapshot draft saat
/// rencana dibuat (untuk mendeteksi draft yang masuk selama flight).
struct PendingLiveSave {
    merged: Vec<Vec<String>>,
    draft_snapshot: Vec<spreadsheet_guard::DraftEntry>,
}

/// Minta fetch live (startup / Muat Ulang) di thread latar.
fn request_live_fetch(
    db: Option<Arc<Mutex<DatabaseService>>>,
    tx: mpsc::Sender<SheetsInboxMsg>,
) {
    std::thread::spawn(move || {
        let cfg = SheetsConfig::from_db(&db);
        if !cfg.is_live_ready() {
            let _ = tx.send(SheetsInboxMsg::SyncOffline);
            return;
        }
        match fetch_live_sheets(&cfg) {
            Ok(fetch) => {
                let _ = tx.send(SheetsInboxMsg::SyncLive(fetch));
            }
            Err(e) => {
                let _ = tx.send(SheetsInboxMsg::SyncFailed(e));
            }
        }
    });
}

/// Minta fresh fetch untuk Simpan di thread latar (ANTI-MENCLOK:
/// selalu baca live terbaru SEBELUM tulis).
fn request_live_save_fetch(
    db: Option<Arc<Mutex<DatabaseService>>>,
    tx: mpsc::Sender<SheetsInboxMsg>,
    merged: Vec<Vec<String>>,
) {
    std::thread::spawn(move || {
        let cfg = SheetsConfig::from_db(&db);
        if !cfg.is_live_ready() {
            let _ = tx.send(SheetsInboxMsg::SaveFreshFailed(
                "Belum terhubung ke Google Sheets.".to_string(),
            ));
            return;
        }
        match fetch_live_sheets(&cfg) {
            Ok(fetch) => {
                let _ = tx.send(SheetsInboxMsg::SaveFresh {
                    live: fetch.live,
                    range_start_row: cfg.range_start_row(),
                    merged,
                });
            }
            Err(e) => {
                let _ = tx.send(SheetsInboxMsg::SaveFreshFailed(e));
            }
        }
    });
}

/// Minta push tulis live (batchUpdate + append) di thread latar.
fn request_live_push(
    db: Option<Arc<Mutex<DatabaseService>>>,
    tx: mpsc::Sender<SheetsInboxMsg>,
    plan: LiveSavePlan,
) {
    std::thread::spawn(move || {
        let cfg = SheetsConfig::from_db(&db);
        match push_live_updates(&cfg, &plan) {
            Ok(()) => {
                let _ = tx.send(SheetsInboxMsg::SavePushed);
            }
            Err(e) => {
                let _ = tx.send(SheetsInboxMsg::SavePushFailed(e));
            }
        }
    });
}

/// Set status text sheet di window utama + SpreadsheetWindow (bila terbuka).
fn set_sheet_status_both(
    ui: &AppWindow,
    sheet_win: &std::rc::Rc<std::cell::RefCell<Option<SpreadsheetWindow>>>,
    msg: &str,
) {
    ui.set_sheet_status_text(slint::SharedString::from(msg));
    if let Some(w) = sheet_win.borrow().as_ref() {
        w.set_sheet_status_text(slint::SharedString::from(msg));
    }
}

/// Sama untuk guard result (pesan blokir simpan).
fn set_sheet_guard_both(
    ui: &AppWindow,
    sheet_win: &std::rc::Rc<std::cell::RefCell<Option<SpreadsheetWindow>>>,
    msg: &str,
) {
    ui.set_sheet_guard_result(slint::SharedString::from(msg));
    if let Some(w) = sheet_win.borrow().as_ref() {
        w.set_sheet_guard_result(slint::SharedString::from(msg));
    }
}

/// Dorong grid + status draft ke kedua tampilan sheet.
fn push_sheet_grids_both(
    ui: &AppWindow,
    sheet_win: &std::rc::Rc<std::cell::RefCell<Option<SpreadsheetWindow>>>,
    st: &spreadsheet_guard::SpreadsheetState,
    display: &[Vec<String>],
) {
    push_grid_to_main(ui, st, display);
    ui.set_sheet_has_draft(!st.drafts.is_empty());
    ui.set_sheet_draft_count(st.drafts.len() as i32);
    if let Some(w) = sheet_win.borrow().as_ref() {
        push_grid_to_sheet_win(w, st, display);
        w.set_sheet_has_draft(!st.drafts.is_empty());
        w.set_sheet_draft_count(st.drafts.len() as i32);
    }
}

/// Terapkan nilai POINTS live ke widget + cache DB (C# LoadData:
/// satu sel {Google.SheetName}!{Col}{Row}). Kosong = biarkan cache lama.
fn apply_live_points(
    ui: &AppWindow,
    db: &Option<Arc<Mutex<DatabaseService>>>,
    points: Option<String>,
) {
    let Some(p) = points.filter(|v| !v.trim().is_empty()) else {
        return;
    };
    ui.set_stat_points(slint::SharedString::from(p.as_str()));
    if let Some(db_mutex) = db.as_ref() {
        if let Ok(mut g) = db_mutex.lock() {
            let _ = g.set("Stat.Points", &p, "String");
            let _ = g.set("Cache.GSheet.Points", &p, "String");
        }
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
                push_toggle_style_to_toolbox(&w, &ui);
            }
            *opt = Some(w);
        }
    }
    if let Some(w) = opt.as_ref() {
        // Gaya chrome saat ini (bisa berubah selagi window tertutup
        // — dorong lagi tiap buka-ulang).
        if let Some(ui) = ui_weak.upgrade() {
            push_chrome_to_toolbox(w, &ui);
            push_toggle_style_to_toolbox(w, &ui);
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
/// Mode notch: pill 150×32 di TENGAH ATAS LAYAR, y=0 (menempel
/// tepi atas layar seperti notch beneran — pin layar sejak
/// 27 Nov 2026, bukan tengah window utama lagi); tidak bisa
/// di-drag — posisinya tetap. Mode pet: avatar bulat 84×84 di
/// posisi tersimpan DynamicBar.X/Y (atau default tengah atas
/// layar, y=60),
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
            // sedang mengembang ditahan di dalam LAYAR supaya
            // tidak keluar layar (dulu: di dalam window utama —
            // diubah 27 Nov 2026, pet melayang di atas semua
            // window, bukan di dalam window BMachine); pet yang
            // mengecil bebas di-drag ke mana saja. (Dalam praktik
            // drag selalu mulai dari panel mengembang — hover
            // mengembang sebelum mousedown.)
            let w_weak2 = w.as_weak();
            let ui_weak_move = ui_weak.clone();
            w.on_move_window(move |dx, dy| {
                if let Some(w) = w_weak2.upgrade() {
                    let scale = w.window().scale_factor();
                    let pos = w.window().position();
                    let mut nx = pos.x + (dx * scale) as i32;
                    let mut ny = pos.y + (dy * scale) as i32;
                    if w.get_expanded() {
                        let size = w.window().size();
                        let (bx0, by0, bx1, by1) = match screen_size() {
                            Some((sw, sh)) => (0, 0, sw, sh),
                            None => match ui_weak_move.upgrade() {
                                Some(ui) => {
                                    let win = ui.window();
                                    let mpos = win.position();
                                    let msize = win.size();
                                    (
                                        mpos.x,
                                        mpos.y,
                                        mpos.x + msize.width as i32,
                                        mpos.y + msize.height as i32,
                                    )
                                }
                                None => (pos.x, pos.y, pos.x, pos.y),
                            },
                        };
                        nx = nx.clamp(bx0, (bx1 - size.width as i32).max(bx0));
                        ny = ny.clamp(by0, (by1 - size.height as i32).max(by0));
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
                    let scale = win.scale_factor();
                    // Lebar panel 560px; tinggi maks 420px (5
                    // notifikasi + baris volume) — clamp
                    // konservatif supaya panel tidak pernah keluar
                    // LAYAR (dulu: window utama — diubah 27 Nov
                    // 2026, pet melayang di atas semua window).
                    let pw = (560f32 * scale) as i32;
                    let ph = (420f32 * scale) as i32;
                    let (bx0, by0, bx1, by1) = match screen_size() {
                        Some((sw, sh)) => (0, 0, sw, sh),
                        None => {
                            let mpos = win.position();
                            let msize = win.size();
                            (
                                mpos.x,
                                mpos.y,
                                mpos.x + msize.width as i32,
                                mpos.y + msize.height as i32,
                            )
                        }
                    };
                    let nx = pos.x.clamp(bx0, (bx1 - pw).max(bx0));
                    let ny = pos.y.clamp(by0, (by1 - ph).max(by0));
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
        // Posisi: mode pet = simpanan dulu (default tengah atas
        // LAYAR, y=60); mode notch SELALU tengah atas LAYAR, y=0
        // (menempel tepi atas — keputusan abeng 27 Nov 22:44:
        // notch/pet melayang di atas semua window, bukan ikut
        // window utama; posisi simpanan lama dari pill 560px
        // sengaja diabaikan supaya notch kembali ke tengah).
        // Fallback bila ukuran layar tak terbaca: tengah window
        // utama (perilaku lama).
        let scale = w.window().scale_factor();
        if mode == "pet" {
            if let Some((x, y)) = read_bar_position(db) {
                w.window().set_position(slint::PhysicalPosition::new(
                    ((x.max(0)) as f32 * scale) as i32,
                    ((y.max(0)) as f32 * scale) as i32,
                ));
            } else if let Some(ui) = ui_weak.upgrade() {
                let bw = (84f32 * scale) as i32;
                let cy = (60f32 * scale) as i32;
                let cx = match screen_size() {
                    Some((sw, _)) => centered_x(sw, bw),
                    None => {
                        let win = ui.window();
                        let pos = win.position();
                        let size = win.size();
                        (pos.x + (size.width as i32 - bw) / 2).max(0)
                    }
                };
                w.window()
                    .set_position(slint::PhysicalPosition::new(cx, cy.max(0)));
            }
        } else if let Some(ui) = ui_weak.upgrade() {
            let bw = (150f32 * scale) as i32;
            let cx = match screen_size() {
                Some((sw, _)) => centered_x(sw, bw),
                None => {
                    let win = ui.window();
                    let pos = win.position();
                    let size = win.size();
                    (pos.x + (size.width as i32 - bw) / 2).max(0)
                }
            };
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

/// Ukuran layar (physical px) via `rdev::display_size()` —
/// SUDAH ada sebagai dependensi (dipakai hotkey global), jadi tanpa
/// crate baru dan build offline tetap jalan.
/// Windows = monitor utama (`GetSystemMetrics` SM_CXSCREEN /
/// SM_CYSCREEN); Linux = ukuran display X (bounding box virtual
/// bila multi-monitor — RandR per-monitor belum dipakai, jadi di
/// Linux multi-monitor "tengah layar" = tengah bounding box virtual;
/// lihat "Masih terbuka" di PERUBAHAN.md).
/// None bila display tidak bisa dibaca — pemanggil fallback ke
/// tengah window utama (perilaku lama sebelum 27 Nov 2026).
fn screen_size() -> Option<(i32, i32)> {
    let (w, h) = rdev::display_size().ok()?;
    if w == 0 || h == 0 || w > i32::MAX as u64 || h > i32::MAX as u64 {
        return None;
    }
    Some((w as i32, h as i32))
}

/// x agar bar selebar `bar_w` terpusat di wadah selebar
/// `container_w` (physical px); clamp >= 0 bila bar lebih lebar
/// dari wadah.
fn centered_x(container_w: i32, bar_w: i32) -> i32 {
    ((container_w - bar_w) / 2).max(0)
}

#[cfg(test)]
mod screen_center_tests {
    use super::*;

    #[test]
    fn centered_x_puts_bar_in_middle_of_screen() {
        // Layar Xvfb 1280×800: notch 150 → x=565, panel 560 → 360,
        // pet 84 → 598 (pusat 640 di semua lebar).
        assert_eq!(centered_x(1280, 150), 565);
        assert_eq!(centered_x(1280, 560), 360);
        assert_eq!(centered_x(1280, 84), 598);
        assert_eq!(centered_x(1920, 150), 885);
        // Bar lebih lebar dari layar → clamp 0, bukan negatif.
        assert_eq!(centered_x(100, 150), 0);
        assert_eq!(centered_x(0, 150), 0);
    }

    #[test]
    fn screen_size_returns_something_sane_when_display_exists() {
        // Di CI tanpa display boleh None; bila ada, harus > 0.
        if let Some((w, h)) = screen_size() {
            assert!(w > 0 && h > 0);
        }
    }
}

/// Pusatkan notch secara horizontal di TENGAH LAYAR (bukan window
/// utama — keputusan abeng 27 Nov 22:44: notch nempel di tengah
/// atas monitor ala Nusa/macOS, melayang di atas semua window).
/// Dipakai saat notch mengembang/mengecil (lebar window berubah
/// 150 <-> 560 sementara tepi kiri OS diam — tanpa ini, notch
/// yang mengembang bergeser ke kanan). `y` tidak disentuh.
/// Fallback: tengah window utama bila ukuran layar tak terbaca.
fn recenter_notch(w: &DynamicBarWindow, ui: &AppWindow, expanded: bool) {
    let scale = w.window().scale_factor();
    let bw = ((if expanded { 560f32 } else { 150f32 }) * scale) as i32;
    let cx = match screen_size() {
        Some((sw, _)) => centered_x(sw, bw),
        None => {
            let win = ui.window();
            let pos = win.position();
            let size = win.size();
            (pos.x + (size.width as i32 - bw) / 2).max(0)
        }
    };
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

/// Sinkronkan token Design ke panel Tugas terpisah bila sedang tampil
/// (dipakai handler tema — panel punya instance Design sendiri,
/// seperti bar/radial).
fn sync_task_theme(ui: &AppWindow, cell: &std::rc::Rc<std::cell::RefCell<Option<TaskManagerWindow>>>) {
    if let Some(w) = cell.borrow().as_ref() {
        theme::sync_design_to_task(ui, w);
    }
}

/// Dorong gaya chrome (style + gaya/posisi tombol) ke satu window
/// sekunder ber-chrome. Dipakai saat window dibuat (nilai awal dari
/// AppWindow) dan saat pengaturan chrome berubah.
fn push_chrome_to_sheet(w: &SpreadsheetWindow, ui: &AppWindow) {
    theme::sync_icons(&Design::get(ui), &Design::get(w));
    w.set_chrome_style(slint::SharedString::from(ui.get_app_chrome()));
    w.set_chrome_button_style(slint::SharedString::from(
        ui.get_app_chrome_button_style(),
    ));
    w.set_chrome_button_position(slint::SharedString::from(
        ui.get_app_chrome_button_position(),
    ));
}

fn push_chrome_to_toolbox(w: &ToolboxWindow, ui: &AppWindow) {
    theme::sync_icons(&Design::get(ui), &Design::get(w));
    w.set_chrome_style(slint::SharedString::from(ui.get_app_chrome()));
    w.set_chrome_button_style(slint::SharedString::from(
        ui.get_app_chrome_button_style(),
    ));
    w.set_chrome_button_position(slint::SharedString::from(
        ui.get_app_chrome_button_position(),
    ));
}

/// Dorong gaya toggle on/off ke ToolboxWindow (satu-satunya window
/// sekunder yang berisi Toggle — Auto-pair di ToolboxView).
fn push_toggle_style_to_toolbox(w: &ToolboxWindow, ui: &AppWindow) {
    w.set_toggle_style(slint::SharedString::from(ui.get_app_toggle_style()));
}

fn push_chrome_to_cardlist(w: &CardListWindow, ui: &AppWindow) {
    theme::sync_icons(&Design::get(ui), &Design::get(w));
    w.set_chrome_style(slint::SharedString::from(ui.get_app_chrome()));
    w.set_chrome_button_style(slint::SharedString::from(
        ui.get_app_chrome_button_style(),
    ));
    w.set_chrome_button_position(slint::SharedString::from(
        ui.get_app_chrome_button_position(),
    ));
}

fn push_chrome_to_batch(w: &BatchWindow, ui: &AppWindow) {
    theme::sync_icons(&Design::get(ui), &Design::get(w));
    w.set_chrome_style(slint::SharedString::from(ui.get_app_chrome()));
    w.set_chrome_button_style(slint::SharedString::from(
        ui.get_app_chrome_button_style(),
    ));
    w.set_chrome_button_position(slint::SharedString::from(
        ui.get_app_chrome_button_position(),
    ));
}

fn push_chrome_to_doc(w: &DocWindow, ui: &AppWindow) {
    theme::sync_icons(&Design::get(ui), &Design::get(w));
    w.set_chrome_style(slint::SharedString::from(ui.get_app_chrome()));
    w.set_chrome_button_style(slint::SharedString::from(
        ui.get_app_chrome_button_style(),
    ));
    w.set_chrome_button_position(slint::SharedString::from(
        ui.get_app_chrome_button_position(),
    ));
}

fn push_chrome_to_comment(w: &CommentWindow, ui: &AppWindow) {
    theme::sync_icons(&Design::get(ui), &Design::get(w));
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
    comment_cell: &std::rc::Rc<std::cell::RefCell<Option<CommentWindow>>>,
    batch_cell: &BatchWinCell,
    doc_cell: &DocWinCell,
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
    if let Some(w) = comment_cell.borrow().as_ref() {
        push_chrome_to_comment(w, ui);
    }
    if let Some(w) = batch_cell.borrow().as_ref() {
        push_chrome_to_batch(w, ui);
    }
    if let Some(w) = doc_cell.borrow().as_ref() {
        push_chrome_to_doc(w, ui);
    }
}

/// Buat CommentWindow bila belum ada + pasang handler chrome /
/// tutup (sekali per instans; dibuat baru tiap buka-ulang setelah
/// tutup, pola sama seperti CardListWindow — hide→show reuse
/// tidak andal di Linux). Saat ditutup, draft komentar ditulis
/// balik ke host pembuka (AppWindow atau CardListWindow, sesuai
/// `source`) — state komentar dibagi seperti C# (DataContext =
/// VM yang sama untuk CommentWindow + CardDetailPanelHost).
fn ensure_comment_window(
    cell: &std::rc::Rc<std::cell::RefCell<Option<CommentWindow>>>,
    vis: &std::rc::Rc<std::cell::Cell<bool>>,
    source: &std::rc::Rc<std::cell::Cell<u8>>,
    ui_weak: &slint::Weak<AppWindow>,
    card_cell: &std::rc::Rc<std::cell::RefCell<Option<CardListWindow>>>,
) {
    let mut opt = cell.borrow_mut();
    if !vis.get() {
        *opt = None;
    }
    if opt.is_some() {
        return;
    }
    if let Ok(w) = CommentWindow::new() {
        let w_weak = w.as_weak();
        let vis_close = vis.clone();
        let source_close = source.clone();
        let ui_weak_close = ui_weak.clone();
        let card_cell_close = card_cell.clone();
        w.on_close_window(move || {
            // Sync-back draft ke host pembuka sebelum disembunyikan.
            if let Some(w) = w_weak.upgrade() {
                let added = w.get_added_count();
                let d1 = w.get_draft_1();
                let d2 = w.get_draft_2();
                let d3 = w.get_draft_3();
                if source_close.get() == 1 {
                    if let Some(card) = card_cell_close.borrow().as_ref() {
                        card.set_detail_added_count(added);
                        card.set_detail_draft_1(d1);
                        card.set_detail_draft_2(d2);
                        card.set_detail_draft_3(d3);
                    }
                } else if let Some(ui) = ui_weak_close.upgrade() {
                    ui.set_detail_added_count(added);
                    ui.set_detail_draft_1(d1);
                    ui.set_detail_draft_2(d2);
                    ui.set_detail_draft_3(d3);
                }
            }
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
        // Tombol kontrol chrome (min/max) + drag (titlebar kustom /
        // strip headless) + gaya chrome awal dari AppWindow (lihat
        // DESIGN_SYSTEM.md §23).
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
            push_chrome_to_comment(&w, &ui);
        }
        *opt = Some(w);
    }
}

/// Isi + tampilkan CommentWindow untuk satu kartu (dipakai pembuka
/// dari AppWindow maupun CardListWindow).
#[allow(clippy::too_many_arguments)]
fn show_comment_window(
    cell: &std::rc::Rc<std::cell::RefCell<Option<CommentWindow>>>,
    vis: &std::rc::Rc<std::cell::Cell<bool>>,
    ui: &AppWindow,
    title: slint::SharedString,
    date: slint::SharedString,
    user_name: slint::SharedString,
    user_initial: slint::SharedString,
    added: i32,
    d1: slint::SharedString,
    d2: slint::SharedString,
    d3: slint::SharedString,
) {
    if let Some(w) = cell.borrow().as_ref() {
        push_chrome_to_comment(w, ui);
        w.set_card_title(title);
        w.set_event_date(date);
        w.set_user_name(user_name);
        w.set_user_initial(user_initial);
        w.set_added_count(added);
        w.set_draft_1(d1);
        w.set_draft_2(d2);
        w.set_draft_3(d3);
        // Tengah window utama (C# CenterScreen; 400x600 sesuai
        // ukuran CommentWindow di .slint).
        let (cx, cy) = center_over_main(ui, 400, 600);
        w.window()
            .set_position(slint::PhysicalPosition::new(cx, cy));
        // Hide + show = naik ke depan (Slint tidak punya Activate).
        // show() saja tidak andal di sini.
        let _ = w.hide();
        let _ = w.show();
        vis.set(true);
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

use slint::Model;
use slint::Global;

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

// ================= BATCH (Batch Master) =================

/// Target menu konteks BATCH yang sedang terbuka (dipegang Rust supaya
/// aksi menu tidak bergantung pada binding Slint yang bisa usang).
#[derive(Default, Clone)]
struct BatchCtxTarget {
    panel: Option<batch::BatchPanel>,
    id: String,
    name: String,
    is_dir: bool,
    is_root: bool,
    /// Parent (full path) untuk input inline subfolder baru.
    newfolder_parent: String,
}

fn batch_rows_model(rows: &[batch::FlatRow]) -> slint::ModelRc<BatchRow> {
    slint::ModelRc::new(slint::VecModel::from(
        rows.iter()
            .map(|r| BatchRow {
                id: slint::SharedString::from(r.id.as_str()),
                name: slint::SharedString::from(r.name.as_str()),
                path: slint::SharedString::from(r.path.as_str()),
                depth: r.depth,
                is_dir: r.is_dir,
                expanded: r.expanded,
                has_kids: r.has_kids,
                selected: r.selected,
                is_root: r.is_root,
            })
            .collect::<Vec<_>>(),
    ))
}

/// Dorong seluruh state BATCH ke Slint (dipanggil setelah tiap perubahan).
/// Sel BatchWindow terlepas (tear-off BATCH, 28 Nov 2026).
type BatchWinCell = std::rc::Rc<std::cell::RefCell<Option<BatchWindow>>>;

fn push_batch_to_ui(
    ui: &AppWindow,
    st: &batch::BatchState,
    batch_win: &BatchWinCell,
) {
    push_batch_to_ui_inner(ui, st);
    // Jendela BATCH terlepas (bila ada) ikut diperbarui dari AppWindow.
    sync_batch_to_open_window(batch_win, ui);
}

/// Isi push_batch_to_ui tanpa sinkronisasi window terlepas — dipakai
/// dari closure lintas-thread (Rc tidak Send; yang dikirim adalah
/// Weak<BatchWindow>, lalu sync_batch_window dipanggil manual di sana).
fn push_batch_to_ui_inner(ui: &AppWindow, st: &batch::BatchState) {
    ui.set_src_rows(batch_rows_model(&st.src_rows()));
    ui.set_out_rows(batch_rows_model(&st.out_rows()));
    ui.set_src_name(slint::SharedString::from(st.header_source_name()));
    let out_name = st
        .roots
        .first()
        .and_then(|r| r.output_path.file_name().map(|n| n.to_string_lossy().into_owned()))
        .unwrap_or_default();
    ui.set_selected_output(slint::SharedString::from(out_name));
    ui.set_batch_out_pending(st.any_output_pending());
    ui.set_batch_sel_count(st.selection.ids.len() as i32);
    ui.set_has_manifest(st.has_manifest);
    // Working set (dropdown di atas treeview): nama sekolah/folder
    // yang sudah dibuka; indeks = posisi SOURCE aktif di dalamnya
    // (-1 = SOURCE bukan dari working set).
    ui.set_batch_working_set_names(sheet_model(
        st.working_set.iter().map(|e| e.name.clone()).collect(),
    ));
    let ws_idx = st
        .roots
        .first()
        .and_then(|r| {
            let cur = r.source_path.to_string_lossy().to_ascii_lowercase();
            st.working_set
                .iter()
                .position(|e| e.path.to_ascii_lowercase() == cur)
                .map(|i| i as i32)
        })
        .unwrap_or(-1);
    ui.set_batch_working_set_idx(ws_idx);
    push_tasks_to_ui(ui, st);
}

/// Salin SELURUH state BATCH dari AppWindow ke BatchWindow terlepas.
/// Satu-satunya sumber kebenaran = AppWindow; window terlepas hanya
/// cermin — callback-nya meneruskan ke AppWindow via invoke_* (Rust),
/// jadi semua perubahan state mengalir lewat handler AppWindow.
fn sync_batch_window(w: &BatchWindow, ui: &AppWindow) {
    w.set_src_rows(ui.get_src_rows());
    w.set_out_rows(ui.get_out_rows());
    w.set_src_name(ui.get_src_name());
    w.set_selected_output(ui.get_selected_output());
    w.set_batch_out_pending(ui.get_batch_out_pending());
    w.set_batch_sel_count(ui.get_batch_sel_count());
    w.set_has_manifest(ui.get_has_manifest());
    w.set_batch_working_set_names(ui.get_batch_working_set_names());
    w.set_batch_working_set_idx(ui.get_batch_working_set_idx());
    w.set_newfolder_open(ui.get_newfolder_open());
    w.set_newfolder_panel(ui.get_newfolder_panel());
    w.set_newfolder_parent(ui.get_newfolder_parent());
    w.set_newfolder_name(ui.get_newfolder_name());
    w.set_ctx_node(ui.get_ctx_node());
    w.set_ctx_node_id(ui.get_ctx_node_id());
    w.set_ctx_panel(ui.get_ctx_panel());
    w.set_ctx_is_dir(ui.get_ctx_is_dir());
    w.set_ctx_is_root(ui.get_ctx_is_root());
    w.set_reguler_manage_open(ui.get_reguler_manage_open());
    w.set_reguler_dirs(ui.get_reguler_dirs());
    w.set_reguler_scan_dir_idx(ui.get_reguler_scan_dir_idx());
    w.set_reguler_scan_rows(ui.get_reguler_scan_rows());
    w.set_reguler_scan_status(ui.get_reguler_scan_status());
    w.set_reguler_filter(ui.get_reguler_filter());
    w.set_reguler_months(ui.get_reguler_months());
    w.set_reguler_month_idx(ui.get_reguler_month_idx());
    w.set_batch_scripts(ui.get_batch_scripts());
    w.set_batch_running(ui.get_batch_running());
}

/// Sinkronkan ke BatchWindow bila sedang terlepas (no-op bila tidak).
fn sync_batch_to_open_window(cell: &BatchWinCell, ui: &AppWindow) {
    if let Some(w) = cell.borrow().as_ref() {
        sync_batch_window(w, ui);
    }
}

/// Sel DocWindow terlepas (tear-off DOC, 28 Nov 2026) — pola sama
/// seperti BatchWinCell.
type DocWinCell = std::rc::Rc<std::cell::RefCell<Option<DocWindow>>>;

/// Dorong state redaksi aktif (DocManager) ke AppWindow.
fn push_doc_to_ui(ui: &AppWindow, mgr: &DocManager) {
    let titles: Vec<String> = mgr.tabs.iter().map(|t| t.title.clone()).collect();
    ui.set_doc_tab_titles(sheet_model(titles));
    ui.set_doc_tab_idx(mgr.active_index as i32);
    if let Some(t) = mgr.active_tab() {
        ui.set_doc_school(slint::SharedString::from(t.name.clone()));
        ui.set_doc_address(slint::SharedString::from(t.address.clone()));
        ui.set_doc_logo_path(slint::SharedString::from(t.logo_path.clone()));
        ui.set_doc_logo2_path(slint::SharedString::from(t.logo_path2.clone()));
        ui.set_doc_show_second_logo(t.show_second_logo);
        ui.set_doc_logo_exists(
            !t.logo_path.trim().is_empty() && std::path::Path::new(&t.logo_path).exists(),
        );
        ui.set_doc_logo2_exists(
            !t.logo_path2.trim().is_empty() && std::path::Path::new(&t.logo_path2).exists(),
        );
    }
}

/// Simpan SEMUA tab redaksi ke DB (key port `Doc.Tabs`, JSON array).
fn persist_doc_tabs(db: &Option<Arc<Mutex<DatabaseService>>>, mgr: &DocManager) {
    let Some(dbm) = db else { return };
    if let Ok(mut g) = dbm.lock() {
        let _ = g.set("Doc.Tabs", &mgr.to_json(), "String");
    }
}

/// Simpan daftar teks sumber pengaturan DOC ke DB (key port
/// `Doc.NameSources` / `Doc.AddressSources`, JSON array string).
fn persist_doc_sources(db: &Option<Arc<Mutex<DatabaseService>>>, mgr: &DocManager) {
    let Some(dbm) = db else { return };
    if let Ok(mut g) = dbm.lock() {
        if let Ok(json) = serde_json::to_string(&mgr.name_sources) {
            let _ = g.set("Doc.NameSources", &json, "String");
        }
        if let Ok(json) = serde_json::to_string(&mgr.address_sources) {
            let _ = g.set("Doc.AddressSources", &json, "String");
        }
    }
}

/// Salin SELURUH state DOC dari AppWindow ke DocWindow terlepas.
/// Satu-satunya sumber kebenaran = AppWindow; window terlepas hanya
/// cermin — callback-nya meneruskan ke AppWindow via invoke_* (Rust),
/// jadi semua perubahan state mengalir lewat handler AppWindow.
fn sync_doc_window(w: &DocWindow, ui: &AppWindow) {
    w.set_doc_school(ui.get_doc_school());
    w.set_doc_address(ui.get_doc_address());
    w.set_doc_tab_titles(ui.get_doc_tab_titles());
    w.set_doc_tab_idx(ui.get_doc_tab_idx());
    w.set_doc_show_second_logo(ui.get_doc_show_second_logo());
    w.set_doc_logo_path(ui.get_doc_logo_path());
    w.set_doc_logo2_path(ui.get_doc_logo2_path());
    w.set_doc_logo_exists(ui.get_doc_logo_exists());
    w.set_doc_logo2_exists(ui.get_doc_logo2_exists());
    w.set_doc_names(ui.get_doc_names());
    w.set_doc_paths(ui.get_doc_paths());
    w.set_doc_exists(ui.get_doc_exists());
    // doc-sel-idx (pilihan di daftar DOKUMEN) sengaja TIDAK disalin:
    // itu state lokal LogPanelView, bukan AppWindow; di window
    // terlepas pilihannya mulai dari -1 (tidak ada yang dipilih).
}

/// Sinkronkan ke DocWindow bila sedang terlepas (no-op bila tidak).
fn sync_doc_to_open_window(cell: &DocWinCell, ui: &AppWindow) {
    if let Some(w) = cell.borrow().as_ref() {
        sync_doc_window(w, ui);
    }
}

/// Simpan daftar TAMBAHAN MASTER ke DB (key port `Paths.AdditionalMaster`,
/// JSON array `StoredPathEntry` — bentuk sama dengan C#).
fn persist_additional_master(
    db: &Option<Arc<Mutex<DatabaseService>>>,
    entries: &[panel_extra::AdditionalMasterEntry],
) {
    let Some(dbm) = db else { return };
    if let Ok(mut g) = dbm.lock() {
        if let Ok(json) = serde_json::to_string(entries) {
            let _ = g.set("Paths.AdditionalMaster", &json, "String");
        }
    }
}

/// Simpan daftar DOKUMEN panel ke DB (key port `Panel.Dokumen`).
fn persist_panel_docs(db: &Option<Arc<Mutex<DatabaseService>>>, docs: &[panel_extra::DokumenEntry]) {
    let Some(dbm) = db else { return };
    if let Ok(mut g) = dbm.lock() {
        if let Ok(json) = serde_json::to_string(docs) {
            let _ = g.set("Panel.Dokumen", &json, "String");
        }
    }
}

/// Dorong grup TAMBAHAN MASTER + DOKUMEN Panel Samping ke UI.
/// `search` = isi `master-search` saat ini (filter nama/path,
/// case-insensitive, ala C# `MasterSearchText`); `view_map` diisi peta
/// indeks tampilan -> indeks asli supaya callback am-* (yang menerima
/// indeks tampilan) menyentuh entri yang benar.
fn push_panel_extra_to_ui(
    ui: &AppWindow,
    master: &[panel_extra::AdditionalMasterEntry],
    docs: &[panel_extra::DokumenEntry],
    search: &str,
    view_map: &Arc<Mutex<Vec<usize>>>,
) {
    let q = search.trim().to_lowercase();
    let mut names: Vec<slint::SharedString> = Vec::new();
    let mut paths: Vec<slint::SharedString> = Vec::new();
    let mut colors: Vec<slint::Color> = Vec::new();
    let mut icons: Vec<slint::SharedString> = Vec::new();
    let mut map: Vec<usize> = Vec::new();
    for (i, e) in master.iter().enumerate() {
        if !q.is_empty() {
            let hay = format!("{} {}", e.display_name(), e.path).to_lowercase();
            if !hay.contains(&q) {
                continue;
            }
        }
        names.push(slint::SharedString::from(e.display_name()));
        paths.push(slint::SharedString::from(e.path.clone()));
        let (r, g, b) =
            panel_extra::parse_hex_color(&e.effective_color()).unwrap_or((0x4A, 0x9E, 0xFF));
        colors.push(slint::Color::from_rgb_u8(r, g, b));
        icons.push(slint::SharedString::from(e.effective_icon()));
        map.push(i);
    }
    *view_map.lock().unwrap() = map;
    ui.set_am_names(slint::ModelRc::new(slint::VecModel::from_slice(&names)));
    ui.set_am_paths(slint::ModelRc::new(slint::VecModel::from_slice(&paths)));
    ui.set_am_colors(slint::ModelRc::new(slint::VecModel::from_slice(&colors)));
    ui.set_am_icons(slint::ModelRc::new(slint::VecModel::from_slice(&icons)));
    ui.set_doc_names(sheet_model(
        docs.iter().map(|d| d.file_name()).collect(),
    ));
    ui.set_doc_paths(sheet_model(docs.iter().map(|d| d.path.clone()).collect()));
    ui.set_doc_exists(sheet_model_bool(
        docs.iter().map(|d| Path::new(&d.path).exists()).collect(),
    ));
}

/// Dorong ulang grup panel dari state + `master-search` UI saat ini.
fn repush_panel_extra(
    ui: &AppWindow,
    master: &Arc<Mutex<Vec<panel_extra::AdditionalMasterEntry>>>,
    docs: &Arc<Mutex<Vec<panel_extra::DokumenEntry>>>,
    view_map: &Arc<Mutex<Vec<usize>>>,
) {
    let search = ui.get_master_search().to_string();
    push_panel_extra_to_ui(
        ui,
        &master.lock().unwrap(),
        &docs.lock().unwrap(),
        &search,
        view_map,
    );
}

/// Petakan indeks tampilan (hasil filter search) ke indeks asli daftar master.
fn panel_master_real_idx(view_map: &Arc<Mutex<Vec<usize>>>, view_idx: i32) -> Option<usize> {
    if view_idx < 0 {
        return None;
    }
    view_map.lock().unwrap().get(view_idx as usize).copied()
}

/// Simpan daftar Folder Reguler ke DB (JSON array RegulerDir).
fn persist_reguler_dirs(db: &Option<Arc<Mutex<DatabaseService>>>, dirs: &[batch::RegulerDir]) {
    let Some(dbm) = db else { return };
    if let Ok(mut g) = dbm.lock() {
        if let Ok(json) = serde_json::to_string(dirs) {
            let _ = g.set("Batch.FolderReguler", &json, "String");
        }
    }
}

/// Simpan working set (dropdown treeview) ke DB `Batch.WorkingSet`.
fn persist_working_set(db: &Option<Arc<Mutex<DatabaseService>>>, set: &[batch::WorkingSetEntry]) {
    let Some(dbm) = db else { return };
    if let Ok(mut g) = dbm.lock() {
        if let Ok(json) = serde_json::to_string(set) {
            let _ = g.set("Batch.WorkingSet", &json, "String");
        }
    }
}

/// State scan popup Kelola Folder Reguler (tahap 4).
struct RegulerScanState {
    dir: String,
    all: Vec<batch::RegulerRow>,
    filter: String,
    month: String,
    scanning: bool,
}

/// Jalankan scan satu direktori reguler di thread latar (supaya UI
/// tidak lag — master_creator: _RegularScanWorker). Hasil dikirim
/// lewat channel; timer 250ms membacanya.
fn spawn_reguler_scan(
    dir: &str,
    rx_holder: &Arc<Mutex<Option<std::sync::mpsc::Receiver<(String, Vec<batch::RegulerRow>)>>>>,
) {
    let (tx, rx) = std::sync::mpsc::channel::<(String, Vec<batch::RegulerRow>)>();
    *rx_holder.lock().unwrap() = Some(rx);
    let dir_owned = dir.to_string();
    std::thread::spawn(move || {
        let rows = batch::scan_reguler(std::path::Path::new(&dir_owned));
        let _ = tx.send((dir_owned, rows));
    });
}

/// Muat cache scan Folder Reguler dari DB `Batch.FolderReguler.Cache`.
fn load_reguler_cache(db: &Option<Arc<Mutex<DatabaseService>>>) -> Option<batch::RegulerCache> {
    let dbm = db.as_ref()?;
    let g = dbm.lock().ok()?;
    let v = g.get("Batch.FolderReguler.Cache")?;
    serde_json::from_str::<batch::RegulerCache>(v.as_str()).ok()
}

/// Simpan cache scan Folder Reguler ke DB.
fn save_reguler_cache(
    db: &Option<Arc<Mutex<DatabaseService>>>,
    cache: &batch::RegulerCache,
) {
    let Some(dbm) = db else { return };
    if let Ok(mut g) = dbm.lock() {
        if let Ok(json) = serde_json::to_string(cache) {
            let _ = g.set("Batch.FolderReguler.Cache", &json, "String");
        }
    }
}

/// Refresh INKREMENTAL Folder Reguler (28 Nov 2026, temuan
/// master_creator): daftar sudah tampil dari cache; worker latar
/// membandingkan fingerprint murah (daftar + mtime folder bulan
/// saja) dan hanya memindai ulang bulan yang berubah/baru;
/// bulan hilang dibuang barisnya. Selesai diam-diam; toast kecil
/// hanya kalau ada sekolah baru/hilang. Jangan tumpuk dengan scan
/// penuh yang sedang jalan (guard `scanning`).
#[allow(clippy::too_many_arguments)]
fn spawn_reguler_incremental(
    dir: String,
    cache: batch::RegulerCache,
    db: Option<Arc<Mutex<DatabaseService>>>,
    st: Arc<Mutex<batch::BatchState>>,
    scan_state: Arc<Mutex<RegulerScanState>>,
    ui_weak: slint::Weak<AppWindow>,
    batch_win: BatchWinCell,
) {
    // Weak (Send) — Rc di BatchWinCell tidak boleh masuk thread
    // (pola sama seperti handler pick-folder).
    let batch_w = batch_win.borrow().as_ref().map(|w| w.as_weak());
    std::thread::spawn(move || {
        {
            let sc = scan_state.lock().unwrap();
            if sc.scanning {
                return;
            }
        }
        let root = std::path::Path::new(&dir);
        let new_fps = batch::reguler_month_fingerprints(root);
        let (changed, removed) = batch::reguler_cache_diff(&cache, &new_fps);
        if changed.is_empty() && removed.is_empty() {
            eprintln!("[reguler-refresh] {}: tidak ada perubahan ({} bulan, {} baris dari cache)",
                dir, cache.months.len(), cache.rows.len());
            return;
        }
        eprintln!(
            "[reguler-refresh] {}: bulan berubah/baru={:?} hilang={:?} — hanya bulan itu yang dipindai ulang",
            dir, changed, removed
        );
        {
            let mut sc = scan_state.lock().unwrap();
            sc.scanning = true;
        }
        let mut fresh: Vec<batch::RegulerRow> = Vec::new();
        for bulan in &changed {
            fresh.extend(batch::scan_reguler_month(root, bulan, &root.join(bulan)));
        }
        let old_rows = cache.rows.clone();
        let rows = batch::reguler_merge_rows(&old_rows, &changed, &removed, fresh);
        let old_paths: std::collections::HashSet<&str> =
            old_rows.iter().map(|r| r.path.as_str()).collect();
        let new_paths: std::collections::HashSet<&str> =
            rows.iter().map(|r| r.path.as_str()).collect();
        let n_added = new_paths.difference(&old_paths).count();
        let n_removed = old_paths.difference(&new_paths).count();
        let new_cache = batch::RegulerCache {
            dir: dir.clone(),
            scanned_at: batch::now_unix(),
            months: new_fps,
            rows: rows.clone(),
        };
        save_reguler_cache(&db, &new_cache);
        let toast = if n_added > 0 && n_removed > 0 {
            format!(
                "Folder Reguler diperbarui: {} sekolah baru, {} dihapus.",
                n_added, n_removed
            )
        } else if n_added > 0 {
            format!("Folder Reguler diperbarui: {} sekolah baru.", n_added)
        } else if n_removed > 0 {
            format!("Folder Reguler diperbarui: {} sekolah dihapus.", n_removed)
        } else {
            String::new()
        };
        let _ = ui_weak.upgrade_in_event_loop(move |ui| {
            {
                let mut sc = scan_state.lock().unwrap();
                sc.all = rows;
                sc.scanning = false;
            }
            let s = st.lock().unwrap();
            let sc = scan_state.lock().unwrap();
            push_reguler_to_ui_inner(&ui, &s, &sc, false);
            if let Some(w) = batch_w.as_ref().and_then(|w| w.upgrade()) {
                sync_batch_window(&w, &ui);
            }
            if !toast.is_empty() {
                ui.set_toast(slint::SharedString::from(toast));
            }
        });
    });
}

/// Jadikan satu folder sekolah sebagai SOURCE BATCH (jalur cepat
/// daftar Folder Reguler / dropdown working set): resolusi PILIHAN
/// dulu (master_creator `_resolve_pilihan_dir`) — kalau sekolah
/// punya subfolder PILIHAN, sumbernya folder PILIHAN itu (treeview
/// menampilkan isinya; output ke folder sekolah); kalau tidak ada,
/// pakai folder sekolah apa adanya dan jangan gagal. Nama working
/// set tetap nama sekolah yang dipilih user. Tutup popup Kelola
/// bila terbuka; dorong UI + toast.
#[allow(clippy::too_many_arguments)]
fn load_school_as_source(
    ui: &AppWindow,
    st: &Arc<Mutex<batch::BatchState>>,
    db: &Option<Arc<Mutex<DatabaseService>>>,
    batch_win: &BatchWinCell,
    path: &str,
    school: &str,
    active_dir: Option<&str>,
) {
    let msg = {
        let mut s = st.lock().unwrap();
        let p = std::path::PathBuf::from(path);
        if !p.is_dir() {
            format!("Folder sekolah tidak ditemukan: {}", path)
        } else {
            if let Some(dir) = active_dir {
                s.reguler_active = dir.to_string();
            }
            let resolved = batch::resolve_pilihan_dir(&p).unwrap_or_else(|| p.clone());
            s.add_folder_named(&resolved, school);
            format!("Folder Reguler: {}", school)
        }
    };
    if let Some(dir) = active_dir {
        if let Some(dbm) = db {
            if let Ok(mut g) = dbm.lock() {
                let _ = g.set("Batch.FolderReguler.Active", dir, "String");
            }
        }
    }
    let ws = st.lock().unwrap().working_set.clone();
    persist_working_set(db, &ws);
    push_batch_to_ui(ui, &st.lock().unwrap(), batch_win);
    ui.set_reguler_manage_open(false);
    sync_batch_to_open_window(batch_win, ui);
    ui.set_toast(slint::SharedString::from(msg));
}

/// Dorong state Folder Reguler ke Slint: daftar direktori (status
/// warna + kunci + konteks output) + daftar sekolah hasil scan
/// (sudah difilter — dibaca tampilan daftar BATCH dan popup Kelola)
/// + daftar bulan + status scan.
fn push_reguler_to_ui(
    ui: &AppWindow,
    st: &batch::BatchState,
    scan: &RegulerScanState,
    run_active: bool,
    batch_win: &BatchWinCell,
) {
    push_reguler_to_ui_inner(ui, st, scan, run_active);
    sync_batch_to_open_window(batch_win, ui);
}

/// Isi push_reguler_to_ui tanpa sinkronisasi window terlepas
/// (alasan sama seperti push_batch_to_ui_inner).
fn push_reguler_to_ui_inner(
    ui: &AppWindow,
    st: &batch::BatchState,
    scan: &RegulerScanState,
    run_active: bool,
) {
    let in_use = |path: &str| {
        run_active
            && st.roots.iter().any(|r| {
                let sp = r.source_path.to_string_lossy();
                sp.as_ref() == path
                    || (sp.starts_with(path)
                        && sp[path.len()..].starts_with(std::path::MAIN_SEPARATOR))
            })
    };
    let dirs: Vec<RegulerDirRow> = st
        .reguler_dirs
        .iter()
        .map(|d| {
            let status = batch::reguler_status(d, in_use(&d.path));
            RegulerDirRow {
                path: slint::SharedString::from(d.path.as_str()),
                name: slint::SharedString::from(d.display_name().as_str()),
                status: slint::SharedString::from(status.key()),
                status_label: slint::SharedString::from(status.label()),
                locked: d.locked,
                last_output: slint::SharedString::from(d.last_output.as_str()),
            }
        })
        .collect();
    ui.set_reguler_dirs(slint::ModelRc::new(slint::VecModel::from(dirs)));
    let scan_idx = if scan.dir.is_empty() {
        -1
    } else {
        st.reguler_dirs
            .iter()
            .position(|d| d.path == scan.dir)
            .map(|i| i as i32)
            .unwrap_or(-1)
    };
    ui.set_reguler_scan_dir_idx(scan_idx);
    let filtered = batch::filter_reguler_rows(&scan.all, &scan.filter, &scan.month);
    let rows: Vec<RegulerSchoolRow> = filtered
        .iter()
        .map(|r| RegulerSchoolRow {
            bulan: slint::SharedString::from(r.bulan.as_str()),
            marketing: slint::SharedString::from(r.marketing.as_str()),
            sekolah: slint::SharedString::from(r.sekolah.as_str()),
            path: slint::SharedString::from(r.path.as_str()),
        })
        .collect();
    ui.set_reguler_scan_rows(slint::ModelRc::new(slint::VecModel::from(rows)));
    let mut months = vec!["Semua bulan".to_string()];
    months.extend(batch::reguler_months(&scan.all));
    let month_idx = if scan.month.is_empty() {
        0
    } else {
        months
            .iter()
            .position(|m| m == &scan.month)
            .map(|i| i as i32)
            .unwrap_or(0)
    };
    ui.set_reguler_months(sheet_model(months));
    ui.set_reguler_month_idx(month_idx);
    let status = if scan.scanning {
        "Memindai…".to_string()
    } else if scan.dir.is_empty() {
        "Pilih direktori dulu".to_string()
    } else if filtered.is_empty() {
        "Tidak ada sekolah".to_string()
    } else {
        format!("{} sekolah", filtered.len())
    };
    ui.set_reguler_scan_status(slint::SharedString::from(status.as_str()));
}

/// Dorong daftar tugas Task Manager ke Slint (dipanggil dari
/// push_batch_to_ui + timer drain saat ada pesan run baru).
/// Model baris tugas untuk Slint (dipakai window utama + panel Tugas
/// terpisah — satu builder supaya isinya selalu sama).
fn task_rows_model(st: &batch::BatchState) -> Vec<TaskRow> {
    st.tasks
        .iter()
        .map(|t| {
            let phases = t
                .phases
                .iter()
                .map(|p| {
                    let mark = match p.state {
                        batch::PhaseState::Done => "✓",
                        batch::PhaseState::Running => "▶",
                        batch::PhaseState::Failed => "✗",
                        batch::PhaseState::Waiting => "…",
                        batch::PhaseState::Pending => "○",
                    };
                    format!("{} {}", mark, p.label)
                })
                .collect::<Vec<_>>()
                .join("  ·  ");
            let lines = t.lines.iter().rev().take(3).cloned().collect::<Vec<_>>();
            let lines = lines.into_iter().rev().collect::<Vec<_>>().join("\n");
            TaskRow {
                id: slint::SharedString::from(t.id.as_str()),
                title: slint::SharedString::from(t.title.as_str()),
                state: slint::SharedString::from(t.state_key()),
                state_label: slint::SharedString::from(t.state_label()),
                progress: t.progress,
                phases: slint::SharedString::from(phases.as_str()),
                followup: slint::SharedString::from(
                    t.followups.first().map(|s| s.as_str()).unwrap_or(""),
                ),
                lines: slint::SharedString::from(lines.as_str()),
            }
        })
        .collect()
}

fn push_tasks_to_ui(ui: &AppWindow, st: &batch::BatchState) {
    ui.set_task_rows(slint::ModelRc::new(slint::VecModel::from(
        task_rows_model(st),
    )));
}

/// Dorong tugas ke panel Tugas terpisah (bila sedang terbuka). Dipanggil
/// di samping push_tasks_to_ui di semua titik yang mengubah tugas.
fn push_tasks_to_panel(
    cell: &std::rc::Rc<std::cell::RefCell<Option<TaskManagerWindow>>>,
    st: &batch::BatchState,
) {
    if let Some(w) = cell.borrow().as_ref() {
        w.set_task_rows(slint::ModelRc::new(slint::VecModel::from(
            task_rows_model(st),
        )));
    }
}

/// Posisi panel Tugas terpisah (tahap 5, master_creator
/// _position_task_panel: dock + main_geom + gap): menempel di KANAN
/// window utama + gap 8px (logis), y sejajar atas window utama.
/// Hasil physical px untuk set_position; clamp >= 0.
fn dock_task_panel_pos(ui: &AppWindow) -> (i32, i32) {
    let win = ui.window();
    let pos = win.position();
    let size = win.size();
    let gap = 8;
    ((pos.x + size.width as i32 + gap).max(0), pos.y.max(0))
}

/// Buka (atau buka-ulang) TaskManagerWindow: window OS terpisah di
/// samping window utama. Pola sama seperti open_dynamic_bar_window:
/// buka-ulang setelah tutup selalu buat window baru; posisi dihitung
/// dari geometri window utama saat dibuka.
#[allow(clippy::too_many_arguments)]
fn open_task_manager_window(
    ui: &AppWindow,
    cell: &std::rc::Rc<std::cell::RefCell<Option<TaskManagerWindow>>>,
    visible: &std::rc::Rc<std::cell::Cell<bool>>,
    st: &Arc<Mutex<batch::BatchState>>,
    handle: &Arc<batch_exec::BatchRunHandle>,
    db: &Option<Arc<Mutex<DatabaseService>>>,
    pl: &Arc<Mutex<ProcessLogService>>,
    batch_win: &BatchWinCell,
) {
    // Bersihkan slot bila panel ditandai tertutup — buka-ulang selalu
    // buat window baru (reuse hide→show tidak andal). Borrow dilepas
    // sebelum push_tasks_to_panel di bawah (borrow_mut yang ditahan
    // sambil borrow() = panic "RefCell already mutably borrowed").
    if !visible.get() {
        *cell.borrow_mut() = None;
    }
    if cell.borrow().is_none() {
        if let Ok(w) = TaskManagerWindow::new() {
            let ui_weak = ui.as_weak();
            let st_c = st.clone();
            let handle_c = handle.clone();
            let pl_c = pl.clone();
            let cell_c = cell.clone();
            // Batalkan tugas dari panel (logika sama dengan tombol
            // Batalkan di kartu tugas window utama).
            w.on_cancel(move |id| {
                let Some(ui) = ui_weak.upgrade() else {
                    return;
                };
                batch_task_cancel_ui(&ui, &st_c, &handle_c, &pl_c, &cell_c, id.as_str());
            });
            let ui_weak = ui.as_weak();
            let st_f = st.clone();
            let handle_f = handle.clone();
            let db_f = db.clone();
            let pl_f = pl.clone();
            let cell_f = cell.clone();
            let vis_f = visible.clone();
            let batch_win_f = batch_win.clone();
            // Lanjutan (follow-up) dari panel — sama seperti tombol
            // "Lanjutkan" di kartu / BUAT MASTER di panel OUTPUT.
            w.on_followup(move |id| {
                let Some(ui) = ui_weak.upgrade() else {
                    return;
                };
                batch_task_followup_ui(
                    &ui, &st_f, &handle_f, &db_f, &pl_f, &cell_f, &vis_f, id.as_str(),
                    &batch_win_f,
                );
            });
            let ui_weak = ui.as_weak();
            let cell_close = cell.clone();
            let vis_close = visible.clone();
            // × di panel: sembunyikan window + bersihkan slot agar
            // buka-ulang membuat window baru (bukan orphan yang tetap
            // tampil — on_closed lama hanya mengubah flag, window
            // tidak pernah di-hide sehingga menumpuk).
            w.on_closed(move || {
                if let Some(w2) = cell_close.borrow().as_ref() {
                    let _ = w2.hide();
                }
                *cell_close.borrow_mut() = None;
                vis_close.set(false);
                if let Some(ui) = ui_weak.upgrade() {
                    ui.set_task_manager_open(false);
                }
            });
            *cell.borrow_mut() = Some(w);
        }
    }
    if let Some(w) = cell.borrow().as_ref() {
        theme::sync_design_to_task(ui, w);
        push_tasks_to_panel(cell, &st.lock().unwrap());
        let (x, y) = dock_task_panel_pos(ui);
        w.window()
            .set_position(slint::PhysicalPosition::new(x, y));
        let _ = w.show();
        visible.set(true);
        ui.set_task_manager_open(true);
    }
}

/// Buat BatchWindow bila belum ada + pasang handler (sekali per instans;
/// dibuat baru tiap lepas-ulang setelah dock — pola sama seperti
/// TaskManagerWindow: hide->show reuse tidak andal di Linux).
/// Semua callback BATCH diteruskan ke AppWindow via invoke_* — AppWindow
/// tetap satu-satunya sumber kebenaran; BatchWindow hanya cermin yang
/// disinkronkan lewat sync_batch_window. Chrome disinkronkan via
/// push_chrome_to_batch (warna tema TIDAK ikut — celah tema #13, sama
/// seperti Spreadsheet/Toolbox/CardList).
fn ensure_batch_window(
    ui: &AppWindow,
    cell: &BatchWinCell,
    db: &Option<Arc<Mutex<DatabaseService>>>,
) {
    if cell.borrow().is_some() {
        return;
    }
    if let Ok(w) = BatchWindow::new() {
        // --- Chrome window (pola CardListWindow). ---
        let ui_weak = ui.as_weak();
        let cell_c = cell.clone();
        let db_c = db.clone();
        // x titlebar -> dock (kembali ke dashboard).
        w.on_close_window(move || {
            if let Some(ui) = ui_weak.upgrade() {
                dock_batch_window(&ui, &cell_c, &db_c);
            }
        });
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
        // Toast diteruskan ke AppWindow (toast hanya ada di dashboard).
        let ui_weak = ui.as_weak();
        w.on_batch_toast(move |t| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.set_toast(t);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_pick_folder(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_pick_folder();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_close_ctx(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_close_ctx();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_ctx_new_subfolder(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_ctx_new_subfolder();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_ctx_replace_auto(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_ctx_replace_auto();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_ctx_replace_manual(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_ctx_replace_manual();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_ctx_copy_path(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_ctx_copy_path();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_ctx_copy_output_path(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_ctx_copy_output_path();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_ctx_delete(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_ctx_delete();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_cancel_new_folder(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_cancel_new_folder();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_copy_master(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_copy_master();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_stop(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_stop();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_replace_auto(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_replace_auto();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_refresh(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_refresh();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_clear(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_clear();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_reguler_manage(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_reguler_manage();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_reguler_close(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_reguler_close();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_reguler_add(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_reguler_add();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_list_open_school(move |a| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_list_open_school(a);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_working_set_picked(move |a| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_working_set_picked(a);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_back_to_list(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_back_to_list();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_reguler_refresh(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_reguler_refresh();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_open_task_manager(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_open_task_manager();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_add_folder(move |a| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_add_folder(a);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_run_script(move |a| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_run_script(a);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_reguler_filter_changed(move |a| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_reguler_filter_changed(a);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_row_clicked(move |a, b| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_row_clicked(a, b);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_row_right_down(move |a, b| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_row_right_down(a, b);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_row_right_up(move |a, b| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_row_right_up(a, b);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_toggle_expand(move |a, b| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_toggle_expand(a, b);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_open_ctx(move |a, b| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_open_ctx(a, b);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_confirm_new_folder(move |a, b| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_confirm_new_folder(a, b);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_reguler_remove(move |a| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_reguler_remove(a);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_reguler_toggle_lock(move |a| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_reguler_toggle_lock(a);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_reguler_select_dir(move |a| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_reguler_select_dir(a);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_batch_reguler_month_picked(move |a| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_batch_reguler_month_picked(a);
            }
        });
        // OS x (chrome native) -> dock juga, bukan tutup proses.
        let ui_weak2 = ui.as_weak();
        let cell_c2 = cell.clone();
        let db_c2 = db.clone();
        w.window().on_close_requested(move || {
            if let Some(ui) = ui_weak2.upgrade() {
                dock_batch_window(&ui, &cell_c2, &db_c2);
            }
            slint::CloseRequestResponse::HideWindow
        });
        // Geometri tersimpan (kunci "BatchWindow", pola Spreadsheet).
        let scale = w.window().scale_factor();
        if let Some((x, y, lw, lh)) = read_window_geometry(db, "BatchWindow") {
            let x = x.max(0);
            let y = y.max(0);
            w.window().set_position(slint::PhysicalPosition::new(
                (x as f32 * scale) as i32,
                (y as f32 * scale) as i32,
            ));
            w.window().set_size(slint::PhysicalSize::new(
                (lw as f32 * scale) as u32,
                (lh as f32 * scale) as u32,
            ));
        }
        push_chrome_to_batch(&w, ui);
        sync_batch_window(&w, ui);
        *cell.borrow_mut() = Some(w);
    }
}

/// Lepas BATCH ke window OS sendiri. `pos` = posisi kiri-atas (physical
/// px) bila Some; None = di tengah window utama (dari ukuran window
/// sebenarnya). Sekali lepas, AppWindow menampilkan placeholder sampai dock.
fn detach_batch_window(
    ui: &AppWindow,
    cell: &BatchWinCell,
    db: &Option<Arc<Mutex<DatabaseService>>>,
    pos: Option<(i32, i32)>,
) {
    if ui.get_batch_detached() {
        return;
    }
    ensure_batch_window(ui, cell, db);
    let pos = match pos {
        Some(p) => p,
        None => {
            let (lw, lh) = match cell.borrow().as_ref() {
                Some(w) => {
                    let scale = w.window().scale_factor().max(1.0);
                    let size = w.window().size();
                    (
                        (size.width as f32 / scale) as i32,
                        (size.height as f32 / scale) as i32,
                    )
                }
                None => (1100, 700),
            };
            center_over_main(ui, lw, lh)
        }
    };
    if let Some(w) = cell.borrow().as_ref() {
        w.window()
            .set_position(slint::PhysicalPosition::new(pos.0, pos.1));
        let _ = w.hide();
        let _ = w.show();
    }
    ui.set_batch_detached(true);
}

/// Kembalikan BATCH ke dashboard: sembunyikan + buang window terlepas
/// (lepas-ulang selalu buat window baru), simpan geometrinya.
fn dock_batch_window(
    ui: &AppWindow,
    cell: &BatchWinCell,
    db: &Option<Arc<Mutex<DatabaseService>>>,
) {
    if let Some(w) = cell.borrow().as_ref() {
        save_window_geometry(w.window(), db, "BatchWindow");
        let _ = w.hide();
    }
    *cell.borrow_mut() = None;
    ui.set_batch_detached(false);
}

/// Buat DocWindow bila belum ada + pasang handler (sekali per instans;
/// dibuat baru tiap lepas-ulang setelah dock — pola sama seperti
/// BatchWindow: hide->show reuse tidak andal di Linux).
/// Semua callback DOC diteruskan ke AppWindow via invoke_* — AppWindow
/// tetap satu-satunya sumber kebenaran; DocWindow hanya cermin yang
/// disinkronkan lewat sync_doc_window. Chrome disinkronkan via
/// push_chrome_to_doc (warna tema TIDAK ikut — celah tema #13, sama
/// seperti Spreadsheet/Toolbox/CardList/BatchWindow).
fn ensure_doc_window(
    ui: &AppWindow,
    cell: &DocWinCell,
    db: &Option<Arc<Mutex<DatabaseService>>>,
) {
    if cell.borrow().is_some() {
        return;
    }
    if let Ok(w) = DocWindow::new() {
        // --- Chrome window (pola BatchWindow). ---
        let ui_weak = ui.as_weak();
        let cell_c = cell.clone();
        let db_c = db.clone();
        // x titlebar -> dock (kembali ke tab panel).
        w.on_close_window(move || {
            if let Some(ui) = ui_weak.upgrade() {
                dock_doc_window(&ui, &cell_c, &db_c);
            }
        });
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
        // Toast diteruskan ke AppWindow (toast hanya ada di dashboard).
        let ui_weak = ui.as_weak();
        w.on_doc_toast(move |t| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.set_toast(t);
            }
        });
        // Semua callback DOC diteruskan ke AppWindow via invoke_* —
        // AppWindow tetap satu-satunya sumber kebenaran; DocWindow hanya
        // cermin yang disinkronkan lewat sync_doc_window.
        let ui_weak = ui.as_weak();
        w.on_doc_open_settings(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_doc_open_settings();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_doc_toggle_second_logo(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_doc_toggle_second_logo();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_doc_reset(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_doc_reset();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_doc_sync(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_doc_sync();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_doc_tab_add(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_doc_tab_add();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_doc_tab_select(move |a| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_doc_tab_select(a);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_doc_tab_close(move |a| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_doc_tab_close(a);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_doc_school_edited(move |a| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_doc_school_edited(a);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_doc_address_edited(move |a| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_doc_address_edited(a);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_doc_from_ps(move |a| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_doc_from_ps(a);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_doc_copy_field(move |a| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_doc_copy_field(a);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_doc_pick_logo(move |a| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_doc_pick_logo(a);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_doc_clear_logo(move |a| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_doc_clear_logo(a);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_doc_send_logo(move |a| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_doc_send_logo(a);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_doc_paste_logo(move |a| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_doc_paste_logo(a);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_doc_add(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_doc_add();
            }
        });
        let ui_weak = ui.as_weak();
        w.on_doc_open(move |a| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_doc_open(a);
            }
        });
        let ui_weak = ui.as_weak();
        w.on_doc_remove(move |a| {
            if let Some(ui) = ui_weak.upgrade() {
                ui.invoke_doc_remove(a);
            }
        });
        // OS x (chrome native) -> dock juga, bukan tutup proses.
        let ui_weak2 = ui.as_weak();
        let cell_c2 = cell.clone();
        let db_c2 = db.clone();
        w.window().on_close_requested(move || {
            if let Some(ui) = ui_weak2.upgrade() {
                dock_doc_window(&ui, &cell_c2, &db_c2);
            }
            slint::CloseRequestResponse::HideWindow
        });
        // Geometri tersimpan (kunci "DocWindow", pola BatchWindow).
        let scale = w.window().scale_factor();
        if let Some((x, y, lw, lh)) = read_window_geometry(db, "DocWindow") {
            let x = x.max(0);
            let y = y.max(0);
            w.window().set_position(slint::PhysicalPosition::new(
                (x as f32 * scale) as i32,
                (y as f32 * scale) as i32,
            ));
            w.window().set_size(slint::PhysicalSize::new(
                (lw as f32 * scale) as u32,
                (lh as f32 * scale) as u32,
            ));
        }
        push_chrome_to_doc(&w, ui);
        sync_doc_window(&w, ui);
        *cell.borrow_mut() = Some(w);
    }
}

/// Lepas DOC ke window OS sendiri. `pos` = posisi kiri-atas (physical
/// px) bila Some; None = di tengah window utama (dari ukuran window
/// sebenarnya). Sekali lepas, tab DOC menampilkan placeholder sampai dock.
fn detach_doc_window(
    ui: &AppWindow,
    cell: &DocWinCell,
    db: &Option<Arc<Mutex<DatabaseService>>>,
    pos: Option<(i32, i32)>,
) {
    if ui.get_doc_detached() {
        return;
    }
    ensure_doc_window(ui, cell, db);
    // Posisi: drag grip memberi posisi eksplisit (titlebar di bawah
    // pointer); tombol Pop Out tanpa geometri tersimpan -> pusatkan di
    // atas window utama; bila ada geometri tersimpan, JANGAN timpa —
    // ensure_doc_window sudah menaruh window di posisi terakhir saat
    // dock (C# mengembalikan window ke posisi terakhirnya).
    let pos: Option<(i32, i32)> = match pos {
        Some(p) => Some(p),
        None => match read_window_geometry(db, "DocWindow") {
            Some(_) => None,
            None => {
                let (lw, lh) = match cell.borrow().as_ref() {
                    Some(w) => {
                        let scale = w.window().scale_factor().max(1.0);
                        let size = w.window().size();
                        (
                            (size.width as f32 / scale) as i32,
                            (size.height as f32 / scale) as i32,
                        )
                    }
                    None => (560, 640),
                };
                Some(center_over_main(ui, lw, lh))
            }
        },
    };
    if let Some(w) = cell.borrow().as_ref() {
        if let Some((px, py)) = pos {
            w.window()
                .set_position(slint::PhysicalPosition::new(px, py));
        }
        let _ = w.hide();
        let _ = w.show();
    }
    ui.set_doc_detached(true);
}

/// Kembalikan DOC ke tab panel: sembunyikan + buang window terlepas
/// (lepas-ulang selalu buat window baru), simpan geometrinya.
fn dock_doc_window(
    ui: &AppWindow,
    cell: &DocWinCell,
    db: &Option<Arc<Mutex<DatabaseService>>>,
) {
    if let Some(w) = cell.borrow().as_ref() {
        save_window_geometry(w.window(), db, "DocWindow");
        let _ = w.hide();
    }
    *cell.borrow_mut() = None;
    ui.set_doc_detached(false);
}

/// Buka DocSettingsWindow ("Pengaturan DOC", C#). Dibuat baru tiap
/// dibuka (pola ReportBugDialog). Simpan = tulis daftar sumber ke DB +
/// DocManager; Batal/x = tutup tanpa menyimpan.
fn open_doc_settings_window(
    ui_weak: &slint::Weak<AppWindow>,
    db: &Option<Arc<Mutex<DatabaseService>>>,
    doc_mgr: &Arc<Mutex<DocManager>>,
) {
    let Ok(w) = DocSettingsWindow::new() else {
        return;
    };
    {
        let mgr = doc_mgr.lock().unwrap();
        w.set_name_text(slint::SharedString::from(mgr.name_sources.join("\n")));
        w.set_address_text(slint::SharedString::from(
            mgr.address_sources.join("\n"),
        ));
    }
    let w_weak = w.as_weak();
    w.on_close_window(move || {
        if let Some(w) = w_weak.upgrade() {
            let _ = w.hide();
        }
    });
    let ui_weak2 = ui_weak.clone();
    let db_clone = db.clone();
    let mgr_clone = doc_mgr.clone();
    let w_weak_save = w.as_weak();
    w.on_save_settings(move |name_text, address_text| {
        let name_sources = doc_manager::parse_doc_sources(&name_text);
        let address_sources = doc_manager::parse_doc_sources(&address_text);
        {
            let mut mgr = mgr_clone.lock().unwrap();
            mgr.name_sources = name_sources;
            mgr.address_sources = address_sources;
            persist_doc_sources(&db_clone, &mgr);
        }
        if let Some(ui) = ui_weak2.upgrade() {
            ui.set_toast(slint::SharedString::from(
                "Pengaturan teks DOC disimpan.",
            ));
        }
        if let Some(w) = w_weak_save.upgrade() {
            let _ = w.hide();
        }
    });
    let ui_weak3 = ui_weak.clone();
    w.on_toast(move |msg| {
        if let Some(ui) = ui_weak3.upgrade() {
            ui.set_toast(msg);
        }
    });
    if let Some(ui) = ui_weak.upgrade() {
        let (cx, cy) = center_over_main(&ui, 520, 460);
        w.window()
            .set_position(slint::PhysicalPosition::new(cx, cy));
    }
    let _ = w.show();
}


/// Batalkan tugas Task Manager — dipakai tombol Batalkan di kartu tugas
/// (window utama + panel terpisah). Tugas yang sedang berjalan = bunuh
/// tree proses (sama seperti tombol Stop); tugas antre / menunggu save
/// langsung ditandai Dibatalkan.
fn batch_task_cancel_ui(
    ui: &AppWindow,
    st: &Arc<Mutex<batch::BatchState>>,
    handle: &Arc<batch_exec::BatchRunHandle>,
    pl: &Arc<Mutex<ProcessLogService>>,
    task_cell: &std::rc::Rc<std::cell::RefCell<Option<TaskManagerWindow>>>,
    id: &str,
) {
    let is_active = {
        let mut s = st.lock().unwrap();
        let is_current = !s.current_task_id.is_empty() && s.current_task_id == id;
        is_current
            && matches!(
                s.task_mut(id).map(|t| t.state),
                Some(batch::TaskState::Running) | Some(batch::TaskState::WaitingSave)
            )
    };
    if is_active && handle.is_running() {
        handle.stop();
        pl.lock()
            .unwrap()
            .add_log("[WARNING] Proses dihentikan oleh User.".to_string());
        push_log_lines(ui, pl);
    } else {
        let mut s = st.lock().unwrap();
        if let Some(t) = s.task_mut(id) {
            t.state = batch::TaskState::Cancelled;
            // Dibatalkan = ditinggalkan: follow-up ikut hilang agar
            // tombol "Lanjutkan" tidak membangkitkan tugas yang
            // sudah dibatalkan.
            t.followups.clear();
        }
        push_tasks_to_ui(ui, &s);
        push_tasks_to_panel(task_cell, &s);
    }
    ui.set_toast(slint::SharedString::from("Tugas dibatalkan."));
}

/// Jalankan follow-up tugas yang menunggu save (Fase 2 =
/// buat_master_duplikat.py, dengan gate manifest yang sama seperti
/// tombol BUAT MASTER di panel OUTPUT). Dipakai kartu tugas window
/// utama + panel terpisah.
#[allow(clippy::too_many_arguments)]
fn batch_task_followup_ui(
    ui: &AppWindow,
    st: &Arc<Mutex<batch::BatchState>>,
    handle: &Arc<batch_exec::BatchRunHandle>,
    db: &Option<Arc<Mutex<DatabaseService>>>,
    pl: &Arc<Mutex<ProcessLogService>>,
    task_cell: &std::rc::Rc<std::cell::RefCell<Option<TaskManagerWindow>>>,
    task_visible: &std::rc::Rc<std::cell::Cell<bool>>,
    id: &str,
    batch_win: &BatchWinCell,
) {
    let followup = {
        let s = st.lock().unwrap();
        s.tasks
            .iter()
            .find(|t| t.id == id)
            .and_then(|t| t.followups.first().cloned())
    };
    let Some(f) = followup else {
        return;
    };
    if f == "BUAT MASTER (duplikat)" {
        let has_manifest = st.lock().unwrap().has_manifest;
        if !has_manifest {
            ui.set_toast(slint::SharedString::from(
                "Manifest belum ada — jalankan script buat_master dulu.",
            ));
            return;
        }
        if let Err(msg) = batch_start_run(
            ui,
            handle,
            db,
            st,
            pl,
            task_cell,
            task_visible,
            "buat_master_duplikat.py",
            batch_win,
        ) {
            ui.set_toast(slint::SharedString::from(msg));
        }
    }
}

/// Baca satu key DB (port dulu, lalu key C# sebagai fallback — DB asli
/// abeng memakai key C# `Configs.Master.*`).
fn batch_db_get(db: &Option<Arc<Mutex<DatabaseService>>>, port_key: &str, csharp_key: &str) -> String {
    let Some(dbm) = db else { return String::new() };
    let Ok(g) = dbm.lock() else { return String::new() };
    g.get(port_key)
        .or_else(|| g.get(csharp_key))
        .unwrap_or_default()
}

/// Folder `Scripts/` (batch_wrapper.py + Master/*.py + Action/*.jsx):
/// 1. override Settings `Configs.System.ScriptsPath`,
/// 2. `<exe dir>/Scripts`, 3. `<cwd>/Scripts` (dev).
fn batch_scripts_dir(db: &Option<Arc<Mutex<DatabaseService>>>) -> std::path::PathBuf {
    let override_path = batch_db_get(db, "Configs.System.ScriptsPath", "Configs.System.ScriptsPath");
    if !override_path.trim().is_empty() {
        return std::path::PathBuf::from(override_path.trim());
    }
    if let Ok(exe) = std::env::current_exe() {
        if let Some(dir) = exe.parent() {
            let cand = dir.join("Scripts");
            if cand.is_dir() {
                return cand;
            }
        }
    }
    std::env::current_dir()
        .unwrap_or_else(|_| std::path::PathBuf::from("."))
        .join("Scripts")
}

/// Tulis wrapper `bmachine_replace_auto_<unik>.jsx` (C#
/// LaunchReplaceScriptAsync): konteks JSON + evalFile replace.jsx.
/// Mengembalikan path wrapper.
fn batch_write_replace_wrapper(
    pairs: &[(std::path::PathBuf, std::path::PathBuf)],
    scripts_dir: &std::path::Path,
) -> Result<std::path::PathBuf, String> {
    let replace_jsx = scripts_dir.join("Action").join("replace.jsx");
    if !replace_jsx.is_file() {
        return Err(format!("replace.jsx tidak ditemukan di {}", replace_jsx.display()));
    }
    let folders: Vec<serde_json::Value> = pairs
        .iter()
        .map(|(o, s)| {
            serde_json::json!({"SourcePath": s.to_string_lossy(), "OutputPath": o.to_string_lossy()})
        })
        .collect();
    let first_out = &pairs[0].0;
    let ctx = serde_json::json!({
        "SourceFolders": folders,
        "OutputBasePath": first_out.parent().map(|p| p.to_string_lossy().into_owned()).unwrap_or_else(|| first_out.to_string_lossy().into_owned()),
        "UseInput": true,
        "UseOutput": true,
        "MasterTemplatePath": first_out.to_string_lossy(),
        "BatchAutoReplace": true,
    });
    let wrapper = format!(
        "$.global.BMachineBatchContext = {};\n$.evalFile(new File({}));\ndelete $.global.BMachineBatchContext;\n",
        serde_json::to_string(&ctx).map_err(|e| e.to_string())?,
        serde_json::to_string(&replace_jsx.to_string_lossy()).map_err(|e| e.to_string())?
    );
    let uniq = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_nanos())
        .unwrap_or(0);
    let path = std::env::temp_dir().join(format!("bmachine_replace_auto_{:x}.jsx", uniq));
    std::fs::write(&path, wrapper).map_err(|e| format!("Gagal menulis wrapper: {}", e))?;
    Ok(path)
}

/// Jalankan Replace (Auto) untuk pasangan-pasangan (output, source):
/// validasi (kedua folder harus ada — pasangan tidak valid di-skip +
/// dicatat, seperti C#), tulis wrapper, jalankan di Photoshop.
/// Di Linux tidak ada Photoshop: wrapper tetap ditulis (bukti pasangan
/// benar) + toast jujur; eksekusi Photoshop hanya di Windows.
fn batch_launch_replace(
    pairs_in: Vec<(std::path::PathBuf, std::path::PathBuf)>,
    db: &Option<Arc<Mutex<DatabaseService>>>,
    proc_log: &Arc<Mutex<ProcessLogService>>,
) -> String {
    let mut pairs: Vec<(std::path::PathBuf, std::path::PathBuf)> = Vec::new();
    let mut skipped = 0;
    for (o, s) in pairs_in {
        if !o.is_dir() || !s.is_dir() {
            skipped += 1;
            proc_log.lock().unwrap().add_log(format!(
                "[WARN] Replace: lewati pasangan (folder tidak ada): {} | {}",
                o.display(),
                s.display()
            ));
            continue;
        }
        pairs.push((o, s));
    }
    if pairs.is_empty() {
        return "Replace: tidak ada pasangan folder yang valid.".to_string();
    }
    let scripts_dir = batch_scripts_dir(db);
    let wrapper = match batch_write_replace_wrapper(&pairs, &scripts_dir) {
        Ok(p) => p,
        Err(e) => return format!("Replace gagal: {}", e),
    };
    let ps_path = batch_db_get(db, "Paths.Photoshop", "Configs.Master.PhotoshopPath");
    #[cfg(target_os = "windows")]
    {
        if ps_path.trim().is_empty() || !std::path::Path::new(ps_path.trim()).is_file() {
            return format!(
                "Replace: {} pasangan siap, wrapper ditulis di {} — atur dulu executable Photoshop di Settings → Paths.",
                pairs.len(),
                wrapper.display()
            );
        }
        match std::process::Command::new(ps_path.trim()).arg(&wrapper).spawn() {
            Ok(_) => {
                let msg = format!("Replace diluncurkan ({} pasangan) ke Photoshop.", pairs.len());
                proc_log.lock().unwrap().add_log(format!("[INFO] {}", msg));
                for (o, s) in &pairs {
                    proc_log.lock().unwrap().add_log(format!(
                        "[INFO]   Master: {} | Input: {}",
                        o.display(),
                        s.display()
                    ));
                }
                proc_log.lock().unwrap().add_log(format!("[INFO] Replace wrapper: {}", wrapper.display()));
                if skipped > 0 {
                    format!("{} ({} pasangan dilewati: folder tidak ada)", msg, skipped)
                } else {
                    msg
                }
            }
            Err(e) => format!("Replace gagal diluncurkan ke Photoshop: {}", e),
        }
    }
    #[cfg(not(target_os = "windows"))]
    {
        let _ = ps_path;
        let msg = format!(
            "Replace: {} pasangan siap, wrapper ditulis di {} — Photoshop hanya ada di Windows, jalankan wrapper ini di Photoshop PC.{}",
            pairs.len(),
            wrapper.display(),
            if skipped > 0 {
                format!(" ({} pasangan dilewati: folder tidak ada)", skipped)
            } else {
                String::new()
            }
        );
        proc_log.lock().unwrap().add_log(format!("[INFO] {}", msg));
        for (o, s) in &pairs {
            proc_log.lock().unwrap().add_log(format!(
                "[INFO]   Master: {} | Input: {}",
                o.display(),
                s.display()
            ));
        }
        msg
    }
}

/// Dorong 12 baris log terakhir ke panel log. Dipakai aksi BATCH yang
/// menulis ke ProcessLogService di luar watcher proses — watcher hanya
/// jalan saat ada proses aktif, jadi tanpa dorongan ini baris baru
/// tidak tampil sampai proses berikutnya jalan (ketemu saat verifikasi
/// GUI 28 Nov 2026: isi file teks tidak muncul di Log Panel).
fn push_log_lines(ui: &AppWindow, pl: &Arc<Mutex<ProcessLogService>>) {
    let lines: Vec<slint::SharedString> = pl
        .lock()
        .unwrap()
        .get_display_lines(12)
        .into_iter()
        .map(slint::SharedString::from)
        .collect();
    ui.set_log_lines(slint::ModelRc::new(slint::VecModel::from(lines)));
}

/// Aksi klik ganda BATCH (folder = expand/collapse, file teks = buka
/// di Log Panel). Dipanggil dari deteksi klik-ganda di Rust — Slint
/// `TouchArea.double-clicked` tidak bisa diandalkan di sini karena
/// tiap klik tunggal mengganti model `src-rows`/`out-rows` wholesale,
/// sehingga elemen baris dibuat ulang dan deteksi double-click winit
/// kehilangan jejak klik pertama (ketemu saat verifikasi GUI 28 Nov
/// 2026: double-click file teks tidak pernah membuka Log Panel).
fn batch_handle_double_click(
    ui_weak: &slint::Weak<AppWindow>,
    st: &Arc<Mutex<batch::BatchState>>,
    _pl: &Arc<Mutex<ProcessLogService>>,
    panel: batch::BatchPanel,
    id: &str,
    batch_win: &BatchWinCell,
) {
    let (is_dir, path, _name) = {
        let s = st.lock().unwrap();
        match batch::find_node(&s.roots, panel, id) {
            Some(n) => (n.is_dir, n.path.clone(), n.name.clone()),
            None => (false, std::path::PathBuf::new(), String::new()),
        }
    };
    if is_dir {
        {
            let mut s = st.lock().unwrap();
            batch::toggle_expand(&mut s.roots, panel, id);
        }
        if let Some(ui) = ui_weak.upgrade() {
            push_batch_to_ui(&ui, &st.lock().unwrap(), batch_win);
        }
        return;
    }
    if path.as_os_str().is_empty() {
        return;
    }
    // File teks dulu dibuka isinya di tab LOG panel — tab LOG dihapus
    // (keputusan abeng 28 Nov 07:05: tab LOG dihapus, FILES/MASTER/DOC
    // tetap), jadi semua file kini dibuka di aplikasi default.
    let _ = platform::open_url(&path.to_string_lossy());
}

/// Master template paths per jenis script (C# `ExecuteMaster`):
/// wisuda → Wisuda10RP/Wisuda8R; manasik → Manasik10RP/Manasik8R;
/// profesi → Profesi/Sporty/Profesi8R; pasfoto → PasFoto; lain →
/// kosong (`buat_master` tidak butuh master — auto-detect).
fn batch_master_paths(
    db: &Option<Arc<Mutex<DatabaseService>>>,
    script_file: &str,
) -> (String, String, String) {
    let lower = script_file.to_ascii_lowercase();
    if lower.contains("wisuda") {
        (
            batch_db_get(db, "Paths.Wisuda", "Configs.Master.Wisuda10RP"),
            batch_db_get(db, "Paths.Wisuda8R", "Configs.Master.Wisuda8R"),
            String::new(),
        )
    } else if lower.contains("manasik") {
        (
            batch_db_get(db, "Paths.Manasik", "Configs.Master.Manasik10RP"),
            batch_db_get(db, "Paths.Manasik8R", "Configs.Master.Manasik8R"),
            String::new(),
        )
    } else if lower.contains("profesi") {
        (
            batch_db_get(db, "Paths.Profesi", "Configs.Master.Profesi"),
            batch_db_get(db, "Paths.Sporty", "Configs.Master.Sporty"),
            batch_db_get(db, "Paths.Profesi8R", "Configs.Master.Profesi8R"),
        )
    } else if lower.contains("pasfoto") || lower.contains("pas_foto") {
        (
            batch_db_get(db, "Paths.PasFoto", "Configs.Master.PasFoto"),
            String::new(),
            String::new(),
        )
    } else {
        (String::new(), String::new(), String::new())
    }
}

/// Mulai satu run script BATCH (C# `ExecuteMaster`): validasi, lalu
/// `batch_exec::start_batch_run` (thread worker, stream via channel).
/// Mengembalikan Err(pesan toast) bila tidak bisa jalan.
#[allow(clippy::too_many_arguments)]
fn batch_start_run(
    ui: &AppWindow,
    handle: &Arc<batch_exec::BatchRunHandle>,
    db: &Option<Arc<Mutex<DatabaseService>>>,
    st: &Arc<Mutex<batch::BatchState>>,
    pl: &Arc<Mutex<ProcessLogService>>,
    task_cell: &std::rc::Rc<std::cell::RefCell<Option<TaskManagerWindow>>>,
    task_visible: &std::rc::Rc<std::cell::Cell<bool>>,
    script_file: &str,
    batch_win: &BatchWinCell,
) -> Result<(), String> {
    if handle.is_running() {
        return Err("Proses sedang berjalan — tekan Stop dulu.".to_string());
    }
    let (roots, output_base, okebase) = {
        let s = st.lock().unwrap();
        if s.roots.is_empty() {
            return Err("Pilih folder sumber dulu.".to_string());
        }
        let roots: Vec<(String, String)> = s
            .roots
            .iter()
            .map(|r| {
                (
                    r.source_path.to_string_lossy().into_owned(),
                    r.output_path.to_string_lossy().into_owned(),
                )
            })
            .collect();
        (roots, s.local_output.clone(), s.oke_base.clone())
    };
    let is_buat_master = script_file
        .to_ascii_lowercase()
        .contains("buat_master");
    let (master, master2, master3) = batch_master_paths(db, script_file);
    if master.trim().is_empty() && !is_buat_master {
        return Err(format!(
            "Master Template belum diset untuk '{}' di Settings > Paths.",
            script_file
        ));
    }
    let scripts_dir = batch_scripts_dir(db);
    let script_path = scripts_dir.join("Master").join(script_file);
    if !script_path.is_file() {
        return Err(format!(
            "Script '{}' tidak ditemukan di {} — cek folder Scripts di Settings.",
            script_file,
            scripts_dir.join("Master").display()
        ));
    }
    let wrapper = scripts_dir.join("batch_wrapper.py");
    if !wrapper.is_file() {
        return Err(format!(
            "batch_wrapper.py tidak ditemukan di {} — atur ScriptsPath di Settings.",
            scripts_dir.display()
        ));
    }
    let user_name = {
        let n = batch_db_get(db, "User.Name", "User.Name");
        if n.trim().is_empty() {
            "USER".to_string()
        } else {
            n
        }
    };
    {
        let mut lg = pl.lock().unwrap();
        if !master.trim().is_empty() {
            lg.add_log(format!("[INFO] Master 1: {}", master.trim()));
        }
        if !master2.trim().is_empty() {
            lg.add_log(format!("[INFO] Master 2: {}", master2.trim()));
        }
        if !master3.trim().is_empty() {
            lg.add_log(format!("[INFO] Master 3: {}", master3.trim()));
        }
        lg.add_log(format!("[INFO] Memproses {} folder...", roots.len()));
    }
    push_log_lines(ui, pl);
    // Task Manager (tahap 5): satu tugas per run — fasenya = tiap
    // root SOURCE. Fase 2 (duplikat) yang dipicu dari panel akan
    // melanjutkan tugas Fase 1 yang sedang menunggu save.
    {
        let mut s = st.lock().unwrap();
        let is_duplikat = script_file.to_ascii_lowercase().contains("duplikat");
        if is_duplikat {
            for t in s.tasks.iter_mut() {
                if t.state == batch::TaskState::WaitingSave {
                    t.state = batch::TaskState::Running;
                    t.phases.push(batch::TaskPhase {
                        label: "BUAT MASTER (duplikat)".to_string(),
                        state: batch::PhaseState::Running,
                    });
                    t.followups.clear();
                }
            }
        }
        let labels: Vec<String> = roots
            .iter()
            .map(|(src, _)| {
                std::path::Path::new(src)
                    .file_name()
                    .map(|n| n.to_string_lossy().into_owned())
                    .unwrap_or_else(|| src.clone())
            })
            .collect();
        let title = if labels.len() == 1 {
            format!("{} — {}", script_file, labels[0])
        } else {
            format!("{} — {} folder", script_file, labels.len())
        };
        let id = format!(
            "{}-{}",
            script_file,
            std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .map(|d| d.as_millis())
                .unwrap_or(0)
        );
        let label_refs: Vec<&str> = labels.iter().map(|l| l.as_str()).collect();
        let mut task = batch::BatchTask::new(&id, &title, &label_refs);
        task.state = batch::TaskState::Running;
        if let Some(first) = task.phases.first_mut() {
            first.state = batch::PhaseState::Running;
        }
        s.push_task(task);
        push_tasks_to_ui(ui, &s);
        push_tasks_to_panel(task_cell, &s);
    }
    // Task Manager tampil otomatis saat run dimulai (window terpisah
    // di samping window utama, ala master_creator) — user menutupnya
    // via × di panel atau tombol "☰ Tugas".
    open_task_manager_window(ui, task_cell, task_visible, st, handle, db, pl, batch_win);
    batch_exec::start_batch_run(
        Arc::clone(handle),
        wrapper,
        script_file.to_string(),
        roots,
        master,
        master2,
        master3,
        output_base,
        okebase,
        user_name,
    );
    ui.set_batch_running(true);
    sync_batch_to_open_window(batch_win, ui);
    Ok(())
}

// ---- Explorer nyata (src/explorer.rs): helper Rust <-> Slint ----
// Dipakai callback ex-* di bawah + timer watcher (polling mtime).

type ExDb = Option<Arc<Mutex<DatabaseService>>>;
type ExState = Arc<Mutex<explorer::ExplorerState>>;

fn ex_db_get(db: &ExDb, key: &str) -> String {
    match db {
        Some(m) => m.lock().ok().and_then(|g| g.get(key)).unwrap_or_default(),
        None => String::new(),
    }
}

fn ex_db_set(db: &ExDb, key: &str, value: &str) {
    if let Some(m) = db {
        if let Ok(mut g) = m.lock() {
            let _ = g.set(key, value, "String");
        }
    }
}

fn ex_db_delete(db: &ExDb, key: &str) {
    if let Some(m) = db {
        if let Ok(mut g) = m.lock() {
            let _ = g.delete(key);
        }
    }
}

/// Baca Vec<SharedString> dari model Slint (untuk callback ber-arg array).
fn read_i32_model(m: slint::ModelRc<i32>) -> Vec<i32> {
    m.iter().collect()
}

/// Pengaturan tampilan satu folder: per-folder (bila ada) else global.
/// Mengembalikan (sort, desc, group, layout, local_only).
fn ex_view_settings(db: &ExDb, path: &Path) -> (String, bool, String, String, bool) {
    let local_key = format!(
        "Configs.Explorer.FolderSettings.{}",
        explorer::folder_settings_key(path)
    );
    let raw = ex_db_get(db, &local_key);
    if !raw.is_empty() {
        if let Ok(v) = serde_json::from_str::<serde_json::Value>(&raw) {
            let sort = v
                .get("SortBy")
                .and_then(|x| x.as_str())
                .unwrap_or("Name")
                .to_string();
            let desc = v
                .get("IsSortDescending")
                .and_then(|x| x.as_bool())
                .unwrap_or(false);
            let layout = v
                .get("LayoutMode")
                .and_then(|x| x.as_str())
                .unwrap_or("Vertical")
                .to_string();
            let group = v
                .get("GroupBy")
                .and_then(|x| x.as_str())
                .unwrap_or("Date")
                .to_string();
            return (sort, desc, group, layout, true);
        }
    }
    let sort = ex_db_get(db, "Configs.Explorer.SortBy");
    let sort = if sort.is_empty() {
        "Name".to_string()
    } else {
        sort
    };
    let desc = ex_db_get(db, "Configs.Explorer.IsSortDescending") == "true";
    let group = ex_db_get(db, "Configs.Explorer.GroupBy");
    let group = if group.is_empty() {
        "Date".to_string()
    } else {
        group
    };
    let layout = ex_db_get(db, "Configs.Explorer.ViewMode");
    let layout = if layout.is_empty() {
        "Vertical".to_string()
    } else {
        layout
    };
    (sort, desc, group, layout, false)
}

/// Simpan pengaturan tampilan: per-folder bila local_only, else global
/// (+ hapus override per-folder bila ada).
fn ex_save_view_settings(
    db: &ExDb,
    path: &Path,
    sort: &str,
    desc: bool,
    group: &str,
    layout: &str,
    local_only: bool,
) {
    let local_key = format!(
        "Configs.Explorer.FolderSettings.{}",
        explorer::folder_settings_key(path)
    );
    if local_only {
        let dto = serde_json::json!({
            "SortBy": sort,
            "IsSortDescending": desc,
            "LayoutMode": layout,
            "GroupBy": group,
        });
        ex_db_set(db, &local_key, &dto.to_string());
    } else {
        ex_db_delete(db, &local_key);
        ex_db_set(db, "Configs.Explorer.SortBy", sort);
        ex_db_set(
            db,
            "Configs.Explorer.IsSortDescending",
            if desc { "true" } else { "false" },
        );
        ex_db_set(db, "Configs.Explorer.GroupBy", group);
        ex_db_set(db, "Configs.Explorer.ViewMode", layout);
    }
}

fn ex_shared_vec(v: &[String]) -> Vec<slint::SharedString> {
    v.iter().map(|s| slint::SharedString::from(s.as_str())).collect()
}

/// Dorong folder saat ini (state.items) + semua urutan + view settings
/// + status root ke properti root ex-* (terikat ke ExplorerView).
fn ex_push_folder(ui: &AppWindow, st: &explorer::ExplorerState, db: &ExDb) {
    let items = &st.items;
    let names: Vec<slint::SharedString> =
        items.iter().map(|i| slint::SharedString::from(i.name.as_str())).collect();
    let kinds: Vec<slint::SharedString> =
        items.iter().map(|i| slint::SharedString::from(i.kind.as_str())).collect();
    let exts: Vec<slint::SharedString> =
        items.iter().map(|i| slint::SharedString::from(i.ext.as_str())).collect();
    let bases: Vec<slint::SharedString> =
        items.iter().map(|i| slint::SharedString::from(i.base.as_str())).collect();
    let sizes: Vec<slint::SharedString> = items
        .iter()
        .map(|i| slint::SharedString::from(i.size_display.as_str()))
        .collect();
    let dates: Vec<slint::SharedString> = items
        .iter()
        .map(|i| slint::SharedString::from(i.date_display.as_str()))
        .collect();
    let previews: Vec<slint::SharedString> =
        items.iter().map(|_| slint::SharedString::new()).collect();
    let paths: Vec<slint::SharedString> = items
        .iter()
        .map(|i| slint::SharedString::from(i.path.to_string_lossy().into_owned()))
        .collect();
    let isdir: Vec<bool> = items.iter().map(|i| i.is_dir).collect();
    let o = explorer::compute_orders(items);
    ui.set_ex_it_names(slint::ModelRc::new(slint::VecModel::from_slice(&names)));
    ui.set_ex_it_kinds(slint::ModelRc::new(slint::VecModel::from_slice(&kinds)));
    ui.set_ex_it_exts(slint::ModelRc::new(slint::VecModel::from_slice(&exts)));
    ui.set_ex_it_bases(slint::ModelRc::new(slint::VecModel::from_slice(&bases)));
    ui.set_ex_it_sizes(slint::ModelRc::new(slint::VecModel::from_slice(&sizes)));
    ui.set_ex_it_dates(slint::ModelRc::new(slint::VecModel::from_slice(&dates)));
    ui.set_ex_it_previews(slint::ModelRc::new(slint::VecModel::from_slice(&previews)));
    ui.set_ex_it_paths(slint::ModelRc::new(slint::VecModel::from_slice(&paths)));
    ui.set_ex_it_isdir(slint::ModelRc::new(slint::VecModel::from_slice(&isdir)));
    ui.set_ex_ord_name_asc(slint::ModelRc::new(slint::VecModel::from_slice(&o.name_asc)));
    ui.set_ex_ord_name_desc(slint::ModelRc::new(slint::VecModel::from_slice(&o.name_desc)));
    ui.set_ex_ord_date_asc(slint::ModelRc::new(slint::VecModel::from_slice(&o.date_asc)));
    ui.set_ex_ord_date_desc(slint::ModelRc::new(slint::VecModel::from_slice(&o.date_desc)));
    ui.set_ex_ord_type_asc(slint::ModelRc::new(slint::VecModel::from_slice(&o.type_asc)));
    ui.set_ex_ord_type_desc(slint::ModelRc::new(slint::VecModel::from_slice(&o.type_desc)));
    ui.set_ex_ord_grp_date(slint::ModelRc::new(slint::VecModel::from_slice(&o.grp_date)));
    ui.set_ex_ord_grp_date_hdr(slint::ModelRc::new(slint::VecModel::from_slice(
        &ex_shared_vec(&o.grp_date_hdr),
    )));
    ui.set_ex_ord_grp_type(slint::ModelRc::new(slint::VecModel::from_slice(&o.grp_type)));
    ui.set_ex_ord_grp_type_hdr(slint::ModelRc::new(slint::VecModel::from_slice(
        &ex_shared_vec(&o.grp_type_hdr),
    )));
    ui.set_ex_ord_split_l(slint::ModelRc::new(slint::VecModel::from_slice(&o.split_l)));
    ui.set_ex_ord_split_r(slint::ModelRc::new(slint::VecModel::from_slice(&o.split_r)));
    ui.set_ex_root_path(slint::SharedString::from(
        st.root_path.to_string_lossy().into_owned(),
    ));
    ui.set_ex_root_missing(st.root_missing);
    ui.set_ex_root_error(slint::SharedString::from(st.root_error.as_str()));
    let can_up = !st.cur_path.as_os_str().is_empty()
        && !st.root_path.as_os_str().is_empty()
        && st.cur_path != st.root_path;
    ui.set_ex_can_up(can_up);
    let (sort, desc, group, layout, local_only) = ex_view_settings(db, &st.cur_path);
    ui.set_ex_view_sort(slint::SharedString::from(sort));
    ui.set_ex_view_desc(desc);
    ui.set_ex_view_group(slint::SharedString::from(group));
    ui.set_ex_view_layout(slint::SharedString::from(layout));
    ui.set_ex_view_local_only(local_only);
    ui.set_ex_preview_text(slint::SharedString::new());
}

/// Baca folder dari disk + dorong ke UI. Path kosong = resolve root.
/// Mengembalikan true bila state berhasil didorong (termasuk empty state
/// root-belum-disetting / root-hilang); false hanya bila path tidak ada
/// di disk (Slint tidak jadi navigasi, toast sudah diset).
fn ex_load_folder(ui: &AppWindow, st: &mut explorer::ExplorerState, db: &ExDb, path: &Path) -> bool {
    let mut p = path.to_path_buf();
    if p.as_os_str().is_empty() {
        p = explorer::resolve_root(|k| ex_db_get(db, k));
        if p.as_os_str().is_empty() {
            st.root_path = PathBuf::new();
            st.root_missing = true;
            st.root_error = String::new();
            st.cur_path = PathBuf::new();
            st.items.clear();
            st.cur_mtime = None;
            ex_push_folder(ui, st, db);
            ui.set_ex_cur_path(slint::SharedString::from(""));
            return true;
        }
    }
    if !p.is_dir() {
        let is_root = !st.root_path.as_os_str().is_empty() && p == st.root_path;
        if is_root {
            st.root_missing = true;
            st.root_error = format!("Folder tidak ditemukan: {}", p.display());
            st.cur_path = p.clone();
            st.items.clear();
            st.cur_mtime = None;
            ex_push_folder(ui, st, db);
            ui.set_ex_cur_path(slint::SharedString::from(p.to_string_lossy().into_owned()));
            return true;
        }
        ui.set_toast(slint::SharedString::from(format!(
            "Folder tidak ditemukan: {}",
            p.display()
        )));
        return false;
    }
    st.root_missing = false;
    st.root_error = String::new();
    if st.root_path.as_os_str().is_empty() {
        st.root_path = explorer::resolve_root(|k| ex_db_get(db, k));
    }
    st.cur_path = p.clone();
    st.items = explorer::read_folder(&p, st.show_hidden);
    st.cur_mtime = std::fs::metadata(&p).and_then(|m| m.modified()).ok();
    st.last_own_change = Some(std::time::Instant::now());
    ex_push_folder(ui, st, db);
    // Path saat ini (dipakai tab EXPLORER panel compact) — selalu
    // diperbarui tiap load, supaya panel tidak ketinggalan overlay.
    ui.set_ex_cur_path(slint::SharedString::from(p.to_string_lossy().into_owned()));
    true
}

/// Baca ulang folder saat ini (dipakai task-poll + watcher + save settings).
fn ex_reload_cur(ui: &AppWindow, st: &mut explorer::ExplorerState, db: &ExDb) {
    let path = st.cur_path.clone();
    if path.as_os_str().is_empty() {
        return;
    }
    let _ = ex_load_folder(ui, st, db, &path);
}

/// Dorong Task Monitor ke properti root ex-tm-*.
/// Urutan tampil ala C# (Insert(0) = terbaru dulu): 3 tugas TERBARU,
/// bukan 3 tertua — kalau tidak, tugas baru tak pernah terlihat sampai
/// yang lama dibersihkan. Daftar disimpan maks 50 (C# FileOperationManager).
fn ex_push_tasks(ui: &AppWindow, st: &explorer::ExplorerState) {
    let mut kinds = vec![String::new(); 3];
    let mut names = vec![String::new(); 3];
    let mut progs = vec![0i32; 3];
    let mut statuses = vec![String::new(); 3];
    let mut sizes = vec![String::new(); 3];
    let mut speeds = vec![String::new(); 3];
    let mut etas = vec![String::new(); 3];
    let mut routes = vec![String::new(); 3];
    let mut errors = vec![String::new(); 3];
    let n = st.tasks.len();
    let start = n.saturating_sub(3);
    for (i, t) in st.tasks[start..].iter().rev().enumerate() {
        kinds[i] = t.kind.to_uppercase();
        names[i] = t.name.clone();
        progs[i] = t.prog_pct();
        statuses[i] = t.status();
        sizes[i] = t.size_str();
        speeds[i] = t.speed_str();
        etas[i] = t.eta_str();
        routes[i] = t.route_str();
        errors[i] = t.error();
    }
    ui.set_ex_tm_kind(slint::ModelRc::new(slint::VecModel::from_slice(
        &ex_shared_vec(&kinds),
    )));
    ui.set_ex_tm_name(slint::ModelRc::new(slint::VecModel::from_slice(
        &ex_shared_vec(&names),
    )));
    ui.set_ex_tm_prog(slint::ModelRc::new(slint::VecModel::from_slice(&progs)));
    ui.set_ex_tm_status(slint::ModelRc::new(slint::VecModel::from_slice(
        &ex_shared_vec(&statuses),
    )));
    ui.set_ex_tm_size(slint::ModelRc::new(slint::VecModel::from_slice(
        &ex_shared_vec(&sizes),
    )));
    ui.set_ex_tm_speed(slint::ModelRc::new(slint::VecModel::from_slice(
        &ex_shared_vec(&speeds),
    )));
    ui.set_ex_tm_eta(slint::ModelRc::new(slint::VecModel::from_slice(
        &ex_shared_vec(&etas),
    )));
    ui.set_ex_tm_route(slint::ModelRc::new(slint::VecModel::from_slice(
        &ex_shared_vec(&routes),
    )));
    ui.set_ex_tm_error(slint::ModelRc::new(slint::VecModel::from_slice(
        &ex_shared_vec(&errors),
    )));
    ui.set_ex_tm_visible(!st.tasks.is_empty());
}

/// Petakan indeks kartu tampil (0 = terbaru) ke indeks st.tasks.
/// Kebalikan dari ex_push_tasks (rev + 3 terbaru).
fn tm_task_idx(st: &explorer::ExplorerState, display_i: usize) -> Option<usize> {
    let n = st.tasks.len();
    if display_i >= 3 || display_i >= n {
        return None;
    }
    Some(n - 1 - display_i)
}

/// Dorong Quick Access (maks 8) ke properti root ex-qa-*.
fn ex_push_qa(ui: &AppWindow, st: &explorer::ExplorerState) {
    let mut paths = vec![String::new(); 8];
    let mut labels = vec![String::new(); 8];
    for (i, (name, path)) in st.qa.iter().take(8).enumerate() {
        paths[i] = path.clone();
        labels[i] = name.clone();
    }
    ui.set_ex_qa_paths(slint::ModelRc::new(slint::VecModel::from_slice(
        &ex_shared_vec(&paths),
    )));
    ui.set_ex_qa_labels(slint::ModelRc::new(slint::VecModel::from_slice(
        &ex_shared_vec(&labels),
    )));
}

/// Muat Quick Access + pinned tabs dari DB, dorong ke UI.
/// Pinned tabs dipulihkan di depan; tab aktif = tab baru di belakangnya
/// (tempat folder yang dibuka) — supaya path pin tidak tertimpa nav.
/// Bila semua 4 tab ter-pin, tab aktif = 0 (pin pertama ikut nav).
fn ex_load_qa_pins(ui: &AppWindow, st: &mut explorer::ExplorerState, db: &ExDb) {
    st.qa.clear();
    let raw = ex_db_get(db, "Configs.Explorer.QuickAccess");
    if !raw.is_empty() {
        if let Ok(v) = serde_json::from_str::<serde_json::Value>(&raw) {
            if let Some(arr) = v.as_array() {
                for it in arr {
                    let name = it
                        .get("Name")
                        .and_then(|x| x.as_str())
                        .unwrap_or("")
                        .to_string();
                    let path = it
                        .get("Path")
                        .and_then(|x| x.as_str())
                        .unwrap_or("")
                        .to_string();
                    if !path.is_empty() {
                        st.qa.push((name, path));
                    }
                }
            }
        }
    }
    ex_push_qa(ui, st);

    let mut paths = vec![String::new(); 4];
    let mut names = vec![String::new(); 4];
    let mut pinned = vec![false; 4];
    let mut labels = vec![String::new(); 4];
    let mut colors = vec![0i32; 4];
    let mut saved = 0usize;
    let raw = ex_db_get(db, "Explorer_PinnedTabs");
    if !raw.is_empty() {
        if let Ok(v) = serde_json::from_str::<serde_json::Value>(&raw) {
            let mut saved_paths: Vec<String> = Vec::new();
            if let Some(arr) = v.get("PinnedPaths").and_then(|x| x.as_array()) {
                for p in arr {
                    if let Some(s) = p.as_str() {
                        saved_paths.push(s.to_string());
                    }
                }
            }
            let saved_labels: Vec<String> = v
                .get("CustomLabels")
                .and_then(|x| x.as_array())
                .map(|arr| {
                    arr.iter()
                        .map(|x| x.as_str().unwrap_or("").to_string())
                        .collect()
                })
                .unwrap_or_default();
            let saved_colors: Vec<i32> = v
                .get("CustomColors")
                .and_then(|x| x.as_array())
                .map(|arr| arr.iter().map(|x| x.as_i64().unwrap_or(0) as i32).collect())
                .unwrap_or_default();
            for (i, p) in saved_paths.iter().take(4).enumerate() {
                if p.is_empty() {
                    continue;
                }
                paths[i] = p.clone();
                let base = Path::new(p)
                    .file_name()
                    .map(|n| n.to_string_lossy().into_owned())
                    .unwrap_or_else(|| p.clone());
                names[i] = base.clone();
                pinned[i] = true;
                let lab = saved_labels.get(i).cloned().unwrap_or_default();
                labels[i] = if lab.is_empty() {
                    base.chars().take(2).collect::<String>().to_uppercase()
                } else {
                    lab
                };
                colors[i] = saved_colors.get(i).copied().unwrap_or(0);
                saved += 1;
            }
        }
    }
    let count = if saved >= 4 { 4 } else { saved as i32 + 1 };
    let active = if saved >= 4 { 0 } else { saved as i32 };
    ui.set_ex_tabs_path(slint::ModelRc::new(slint::VecModel::from_slice(
        &ex_shared_vec(&paths),
    )));
    ui.set_ex_tabs_name(slint::ModelRc::new(slint::VecModel::from_slice(
        &ex_shared_vec(&names),
    )));
    ui.set_ex_tabs_pinned(slint::ModelRc::new(slint::VecModel::from_slice(&pinned)));
    ui.set_ex_tabs_label(slint::ModelRc::new(slint::VecModel::from_slice(
        &ex_shared_vec(&labels),
    )));
    ui.set_ex_tabs_color(slint::ModelRc::new(slint::VecModel::from_slice(&colors)));
    ui.set_ex_tab_count(count);
    ui.set_ex_active_tab(active);
}

/// Pekerja satu tugas file: copy (dedup "x - Copy") / move (merge bila
/// tujuan sudah ada — C# MoveDirectoryAsync: Directory.Move bila tujuan
/// belum ada, copy+delete = merge bila sudah ada; file ditimpa) /
/// delete. Mengembalikan pasangan (dari, ke) untuk undo (copy/move);
/// delete -> kosong.
fn ex_run_file_task(
    kind: &str,
    srcs: &[PathBuf],
    dst: &Path,
    cancel: &AtomicBool,
    paused: &AtomicBool,
    progress: &Arc<Mutex<explorer::Progress>>,
    status: &Arc<Mutex<String>>,
) -> Result<Vec<(PathBuf, PathBuf)>, String> {
    use std::sync::atomic::Ordering as AtomicOrdering;
    let mut pairs: Vec<(PathBuf, PathBuf)> = Vec::new();
    match kind {
        "copy" => {
            for src in srcs {
                if cancel.load(AtomicOrdering::Relaxed) {
                    return Err("cancelled".to_string());
                }
                let name = src
                    .file_name()
                    .map(|n| n.to_string_lossy().into_owned())
                    .unwrap_or_default();
                let is_dir = src.is_dir();
                let (base, ext) = if is_dir {
                    (name.clone(), String::new())
                } else {
                    (explorer::base_of(&name), explorer::ext_of(&name))
                };
                let target = dst.join(explorer::dedup_name(dst, &base, &ext));
                explorer::copy_recursive(src, &target, cancel, paused, progress)?;
                pairs.push((src.clone(), target));
            }
            Ok(pairs)
        }
        "move" => {
            for src in srcs {
                if cancel.load(AtomicOrdering::Relaxed) {
                    return Err("cancelled".to_string());
                }
                let name = src
                    .file_name()
                    .map(|n| n.to_string_lossy().into_owned())
                    .unwrap_or_default();
                let target = dst.join(&name);
                // Tujuan sudah ada + keduanya folder = merge (C#:
                // status "Merging..." + copy+delete). move_one sendiri
                // sudah merge (rename gagal -> copy_recursive yang
                // create_dir_all + timpa file, lalu hapus sumber);
                // di sini hanya tandai statusnya.
                if target.exists() && src.is_dir() && target.is_dir() {
                    if let Ok(mut s) = status.lock() {
                        *s = "merging".to_string();
                    }
                }
                explorer::move_one(src, &target, cancel, paused, progress)?;
                pairs.push((src.clone(), target));
            }
            Ok(pairs)
        }
        "delete" => {
            for src in srcs {
                explorer::delete_recursive(src, cancel, paused, progress)?;
            }
            Ok(pairs)
        }
        _ => Err(format!("Jenis tugas tidak dikenal: {}", kind)),
    }
}

/// Jalankan satu tugas file di thread pekerja (serial via task_gate).
/// Daftar maks 50 ala C# (terlama non-berjalan digeser); tampil 3 terbaru.
/// Bila semuanya berjalan (hanya mungkin 1 karena antrean serial),
/// tolak (false).
fn ex_spawn_task(
    ex_state: &ExState,
    kind: &str,
    name: String,
    srcs: Vec<PathBuf>,
    dst: PathBuf,
    is_cut: bool,
    total: u64,
) -> bool {
    use std::sync::atomic::Ordering as AtomicOrdering;
    let mut st = match ex_state.lock() {
        Ok(s) => s,
        Err(_) => return false,
    };
    if st.tasks.len() >= 50 {
        if let Some(pos) = st
            .tasks
            .iter()
            .position(|t| !matches!(t.status().as_str(), "run" | "merging" | "cancelling"))
        {
            st.tasks.remove(pos);
        } else {
            return false;
        }
    }
    let task = explorer::FileTask::new(kind, name.clone(), srcs.clone(), dst.clone(), is_cut, total);
    let status = task.status.clone();
    let error = task.error.clone();
    let progress = task.progress.clone();
    let cancel = task.cancel.clone();
    let paused = task.paused.clone();
    let started = task.started.clone();
    let gate = st.task_gate.clone();
    let ex_state2 = ex_state.clone();
    let kind_owned = kind.to_string();
    st.tasks.push(task);
    drop(st);
    std::thread::spawn(move || {
        // Serial: tunggu giliran (tugas lain selesai dulu). Waktu mulai
        // dihitung dari sini, bukan dari spawn (kecepatan/ETA nyata).
        let _guard = gate.lock().unwrap();
        if let Ok(mut s) = started.lock() {
            *s = Some(std::time::Instant::now());
        }
        let status_c = status.clone();
        let res = ex_run_file_task(&kind_owned, &srcs, &dst, &cancel, &paused, &progress, &status_c);
        match res {
            Ok(pairs) => {
                // Paksa progress 100% (add terakhir bisa tertahan throttle).
                if let Ok(mut p) = progress.lock() {
                    p.finish();
                }
                if !pairs.is_empty() && (kind_owned == "copy" || kind_owned == "move") {
                    if let Ok(mut s2) = ex_state2.lock() {
                        s2.push_undo(explorer::UndoEntry {
                            kind: kind_owned.clone(),
                            name: name.clone(),
                            pairs,
                        });
                    }
                }
                if let Ok(mut s) = status.lock() {
                    // Batal ditekan di detik terakhir: jangan klaim selesai.
                    *s = if cancel.load(AtomicOrdering::Relaxed) {
                        "cancelled".to_string()
                    } else {
                        "done".to_string()
                    };
                }
            }
            Err(e) if e == "cancelled" => {
                if let Ok(mut s) = status.lock() {
                    *s = "cancelled".to_string();
                }
            }
            Err(e) => {
                if let Ok(mut er) = error.lock() {
                    *er = e;
                }
                if let Ok(mut s) = status.lock() {
                    *s = "failed".to_string();
                }
            }
        }
    });
    true
}

/// Nama tampil satu path (untuk toast / nama tugas).
fn ex_display_name(p: &Path) -> String {
    p.file_name()
        .map(|n| n.to_string_lossy().into_owned())
        .unwrap_or_else(|| p.to_string_lossy().into_owned())
}

/// Daftar path dari string gabungan "\n" (Slint sel-paths-joined).
fn ex_split_joined(joined: &str) -> Vec<PathBuf> {
    joined
        .lines()
        .filter(|s| !s.trim().is_empty())
        .map(PathBuf::from)
        .collect()
}

/// Nama file tidak boleh mengandung karakter ini (aturan Windows, C#).
fn ex_bad_name(name: &str) -> bool {
    name.contains(['/', '\\', ':', '*', '?', '"', '<', '>', '|'])
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

    // Explorer nyata (src/explorer.rs): state bersama — folder saat ini,
    // clipboard, undo, tugas file, Quick Access. Dibuat awal supaya
    // callback settings + timer watcher di bawah bisa memakainya.
    let explorer_state: ExState = Arc::new(Mutex::new(explorer::ExplorerState::default()));

    // Explorer Settings: muat dari DB ke properti root (sekali saat startup;
    // ExplorerSettingsView terikat <=> ke sini).
    {
        let path = ex_db_get(&db, "Configs.Explorer.DefaultPath");
        ui.set_ex_set_path(slint::SharedString::from(path));
        let layout = ex_db_get(&db, "Configs.Explorer.ViewMode");
        let view = match layout.as_str() {
            "Horizontal" => "List",
            "Thumbnail" => "Thumbnails",
            "Tiles" => "Tiles",
            "Split" => "Split",
            _ => "Details",
        };
        ui.set_ex_set_view(slint::SharedString::from(view));
        let sort = ex_db_get(&db, "Configs.Explorer.SortBy");
        ui.set_ex_set_sort(slint::SharedString::from(if sort.is_empty() {
            "Name".to_string()
        } else {
            sort
        }));
        let group = ex_db_get(&db, "Configs.Explorer.GroupBy");
        ui.set_ex_set_group(slint::SharedString::from(if group.is_empty() {
            "Date".to_string()
        } else {
            group
        }));
        ui.set_ex_set_hidden(ex_db_get(&db, "Configs.Explorer.ShowHiddenFiles") == "true");
        let ext = ex_db_get(&db, "Configs.Explorer.ShowFileExtensions");
        ui.set_ex_set_ext(ext != "false"); // default tampil
        let zoom = ex_db_get(&db, "Configs.Explorer.ContentZoom")
            .parse::<i32>()
            .unwrap_or(100);
        ui.set_ex_set_zoom(zoom.clamp(75, 200));
        let showbar = ex_db_get(&db, "Configs.Explorer.ShowPathBar");
        ui.set_ex_set_showbar(showbar != "false"); // default tampil
        ui.set_ex_show_pathbar(showbar != "false");
        let font = ex_db_get(&db, "Configs.Explorer.FontFamily");
        let font = if font.is_empty() { "Inter".to_string() } else { font };
        ui.set_ex_set_font(slint::SharedString::from(font.as_str()));
        ui.set_ex_font(slint::SharedString::from(font.as_str()));
    }
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
    // CommentWindow (komentar kartu di window terpisah) — dibuat
    // saat segmen panah split button Comment diklik; satu window
    // dipakai ulang untuk semua kartu (dibuat baru tiap buka-ulang
    // setelah tutup, pola sama seperti CardListWindow).
    let comment_win_cell: std::rc::Rc<std::cell::RefCell<Option<CommentWindow>>> =
        std::rc::Rc::new(std::cell::RefCell::new(None));
    let comment_visible: std::rc::Rc<std::cell::Cell<bool>> =
        std::rc::Rc::new(std::cell::Cell::new(false));
    // Asal pembuka CommentWindow (untuk sync-back draft saat
    // ditutup): 0 = AppWindow (detail embedded / Trello window),
    // 1 = CardListWindow.
    let comment_source: std::rc::Rc<std::cell::Cell<u8>> =
        std::rc::Rc::new(std::cell::Cell::new(0));
    // BatchWindow terlepas (tear-off BATCH, 28 Nov 2026) — dibuat saat
    // tab BATCH diseret keluar; None = BATCH masih embedded di dashboard.
    let batch_win_cell: BatchWinCell =
        std::rc::Rc::new(std::cell::RefCell::new(None));
    // DocWindow terlepas (tear-off DOC, 28 Nov 2026) — pola sama dengan
    // BatchWindow; None = DOC masih di tab Panel Samping.
    let doc_win_cell: DocWinCell = std::rc::Rc::new(std::cell::RefCell::new(None));
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
    // Muat tab redaksi tersimpan (DB `Doc.Tabs`) + daftar teks sumber
    // pengaturan DOC (`Doc.NameSources` / `Doc.AddressSources`).
    if let Some(dbm) = &db {
        if let Ok(g) = dbm.lock() {
            if let Some(json) = g.get("Doc.Tabs") {
                let mut mgr = doc_mgr.lock().unwrap();
                *mgr = DocManager::from_json(&json);
            }
            if let Some(json) = g.get("Doc.NameSources") {
                if let Ok(v) = serde_json::from_str::<Vec<String>>(&json) {
                    doc_mgr.lock().unwrap().name_sources = v;
                }
            }
            if let Some(json) = g.get("Doc.AddressSources") {
                if let Ok(v) = serde_json::from_str::<Vec<String>>(&json) {
                    doc_mgr.lock().unwrap().address_sources = v;
                }
            }
        }
    }
    {
        let mgr = doc_mgr.lock().unwrap();
        push_doc_to_ui(&ui, &mgr);
    }
    let proc_log = Arc::new(Mutex::new(ProcessLogService::default()));

    // BATCH state (Batch Master): roots + seleksi + Folder Reguler.
    // LocalOutput/OkeBase dibaca dari DB (key port dulu, lalu key C#).
    let batch_state = Arc::new(Mutex::new(batch::BatchState::default()));
    let batch_ctx = Arc::new(Mutex::new(BatchCtxTarget::default()));
    {
        let mut st = batch_state.lock().unwrap();
        st.local_output = batch_db_get(&db, "Paths.LocalOutput", "Configs.Master.LocalOutput");
        st.oke_base = batch_db_get(&db, "Paths.OkeBase", "Configs.Master.OkeBase");
        if let Some(ref dbm) = db {
            if let Ok(g) = dbm.lock() {
                if let Some(v) = g.get("Batch.FolderReguler") {
                    // Format baru: JSON array RegulerDir {path, locked,
                    // last_output}; format lama (array string) dimigrasi.
                    if let Ok(list) = serde_json::from_str::<Vec<batch::RegulerDir>>(v.as_str()) {
                        st.reguler_dirs = list.into_iter().filter(|d| !d.path.trim().is_empty()).collect();
                    } else if let Ok(list) = serde_json::from_str::<Vec<String>>(v.as_str()) {
                        st.reguler_dirs = list
                            .into_iter()
                            .filter(|p| !p.trim().is_empty())
                            .map(|p| batch::RegulerDir::new(&p))
                            .collect();
                    }
                }
                if let Some(v) = g.get("Batch.FolderReguler.Active") {
                    st.reguler_active = v.clone();
                }
                if let Some(v) = g.get("Batch.WorkingSet") {
                    if let Ok(list) = serde_json::from_str::<Vec<batch::WorkingSetEntry>>(v.as_str()) {
                        st.working_set = list
                            .into_iter()
                            .filter(|e| !e.path.trim().is_empty())
                            .take(batch::WORKING_SET_MAX)
                            .collect();
                    }
                }
            }
        }
    }
    // Dorong state BATCH awal ke UI (drop zone kosong sampai folder dipilih).
    push_batch_to_ui(&ui, &batch_state.lock().unwrap(), &batch_win_cell);

    // Panel Samping — grup TAMBAHAN MASTER + DOKUMEN (28 Nov 2026).
    // Master: key port `Paths.AdditionalMaster`, baca jatuh ke C#
    // `Configs.Master.AdditionalPaths`; dokumen: `Panel.Dokumen`.
    let panel_master = Arc::new(Mutex::new(Vec::<panel_extra::AdditionalMasterEntry>::new()));
    let panel_docs = Arc::new(Mutex::new(Vec::<panel_extra::DokumenEntry>::new()));
    // Peta indeks tampilan (hasil filter `master-search`) -> indeks asli;
    // callback am-* menerima indeks tampilan (lihat push_panel_extra_to_ui).
    let panel_master_view = Arc::new(Mutex::new(Vec::<usize>::new()));
    {
        if let Some(ref dbm) = db {
            if let Ok(g) = dbm.lock() {
                let raw = g
                    .get("Paths.AdditionalMaster")
                    .or_else(|| g.get("Configs.Master.AdditionalPaths"))
                    .unwrap_or_default();
                *panel_master.lock().unwrap() = panel_extra::parse_additional_master(&raw);
                if let Some(v) = g.get("Panel.Dokumen") {
                    *panel_docs.lock().unwrap() = panel_extra::parse_panel_docs(&v);
                }
            }
        }
    }
    push_panel_extra_to_ui(
        &ui,
        &panel_master.lock().unwrap(),
        &panel_docs.lock().unwrap(),
        "",
        &panel_master_view,
    );

    // BATCH executor (tahap 3): handle run + daftar script master dari
    // Scripts/Master/*.py (nama/urutan dari scripts.json bila ada).
    let batch_run_handle = Arc::new(batch_exec::BatchRunHandle::new());
    let batch_scripts = Arc::new(Mutex::new(batch_exec::scan_master_scripts(
        &batch_scripts_dir(&db),
    )));
    {
        let names: Vec<String> = batch_scripts
            .lock()
            .unwrap()
            .iter()
            .map(|e| e.display.clone())
            .collect();
        if !names.is_empty() {
            ui.set_batch_scripts(sheet_model(names));
        }
    }

    // Folder Reguler — state scan popup Kelola (tahap 4): hasil scan
    // penuh + filter aktif. Scan jalan di thread latar; hasilnya masuk
    // lewat channel, dibaca timer 250ms (pola _sheets_inbox_timer).
    let reguler_scan_state = Arc::new(Mutex::new(RegulerScanState {
        dir: String::new(),
        all: Vec::new(),
        filter: String::new(),
        month: db
            .as_ref()
            .and_then(|m| m.lock().ok())
            .and_then(|g| g.get("Batch.FolderReguler.Month"))
            .unwrap_or_default(),
        scanning: false,
    }));
    let reguler_scan_rx: Arc<
        Mutex<Option<std::sync::mpsc::Receiver<(String, Vec<batch::RegulerRow>)>>>,
    > = Arc::new(Mutex::new(None));

    // Daftar Folder Reguler = tampilan awal BATCH (28 Nov 2026):
    // tampil INSTAN dari cache scan terakhir (DB), lalu worker latar
    // refresh inkremental (hanya bulan yang mtime-nya berubah).
    // Tanpa cache (baru pertama kali / ganti direktori) → scan penuh.
    {
        let dir = {
            let s = batch_state.lock().unwrap();
            if !s.reguler_active.is_empty()
                && s.reguler_dirs.iter().any(|d| d.path == s.reguler_active)
            {
                s.reguler_active.clone()
            } else {
                s.reguler_dirs
                    .first()
                    .map(|d| d.path.clone())
                    .unwrap_or_default()
            }
        };
        if !dir.is_empty() && std::path::Path::new(&dir).is_dir() {
            let cache = load_reguler_cache(&db);
            let cache_hit = cache.as_ref().map(|c| c.dir == dir).unwrap_or(false);
            if cache_hit {
                let cache = cache.unwrap();
                {
                    let mut sc = reguler_scan_state.lock().unwrap();
                    sc.dir = dir.clone();
                    sc.all = cache.rows.clone();
                    sc.scanning = false;
                }
                // Dorong daftar dari cache sekarang (tampil instan).
                {
                    let s = batch_state.lock().unwrap();
                    let sc = reguler_scan_state.lock().unwrap();
                    push_reguler_to_ui_inner(&ui, &s, &sc, false);
                }
                spawn_reguler_incremental(
                    dir,
                    cache,
                    db.clone(),
                    batch_state.clone(),
                    reguler_scan_state.clone(),
                    ui.as_weak(),
                    batch_win_cell.clone(),
                );
            } else {
                {
                    let mut sc = reguler_scan_state.lock().unwrap();
                    sc.dir = dir.clone();
                    sc.scanning = true;
                }
                spawn_reguler_scan(&dir, &reguler_scan_rx);
            }
        }
    }

    // Task Manager — window OS terpisah (tahap 5): cell + flag tampil,
    // pola sama seperti sheet_win / DynamicBarWindow (buka-ulang selalu
    // buat window baru).
    let task_win: std::rc::Rc<std::cell::RefCell<Option<TaskManagerWindow>>> =
        std::rc::Rc::new(std::cell::RefCell::new(None));
    let task_win_visible: std::rc::Rc<std::cell::Cell<bool>> =
        std::rc::Rc::new(std::cell::Cell::new(false));

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
            // Interval auto-refresh Trello (detik) — key baru
            // `Trello.RefreshInterval`; kosong = default 60 (C#
            // `Settings.Interval.*`). Field Settings selama ini mati
            // (default "30" hardcode, tidak tersimpan) — mulai run ini
            // field itu hidup dan dibaca timer auto-refresh.
            if let Some(v) = db_guard.get("Trello.RefreshInterval") {
                ui.set_app_trello_interval(slint::SharedString::from(v));
            } else {
                ui.set_app_trello_interval(slint::SharedString::from("60"));
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
            // Gaya toggle on/off (default "classic"; nilai tak
            // dikenal → "classic" via valid_toggle_style).
            let mut toggle_style = "classic".to_string();
            if let Some(v) = db_guard.get("Settings.ToggleStyle") {
                toggle_style = theme::valid_toggle_style(&v);
            }
            ui.set_app_toggle_style(slint::SharedString::from(toggle_style.as_str()));
            // Paket ikon (default "lucide"; nilai tak dikenal →
            // "lucide" via valid_icon_pack). Path didorong ke Design
            // window utama di sini — sebelum ui.run() — supaya ikon
            // splash + dashboard sudah benar sejak frame pertama.
            let mut icon_pack = "lucide".to_string();
            if let Some(v) = db_guard.get("Settings.IconPack") {
                icon_pack = icons::valid_icon_pack(&v);
            }
            ui.set_app_icon_pack(slint::SharedString::from(icon_pack.as_str()));
            theme::push_icons(&Design::get(&ui), &icon_pack);
            // Sidebar Settings ciut/lebarkan (default terbuka;
            // nilai tak dikenal → terbuka).
            let mut sidebar_collapsed = false;
            if let Some(v) = db_guard.get("Settings.SidebarCollapsed") {
                sidebar_collapsed = v == "true" || v == "1";
            }
            ui.set_app_settings_sidebar_collapsed(sidebar_collapsed);
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
            // Auto-hide Dynamic Island ala layzee (default ON;
            // nilai tak dikenal → ON).
            let mut notch_auto_hide = true;
            if let Some(v) = db_guard.get("Settings.Notch.AutoHide") {
                notch_auto_hide = !(v == "false" || v == "0");
            }
            ui.set_app_notch_auto_hide(notch_auto_hide);
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
                    // NAMA TAB spreadsheet = Spreadsheet.SheetName (bukan
                    // Google.SheetName — itu tab POIN, mis. EDITOR HARIAN;
                    // memakai tab poin di sini membuat simpan live menulis
                    // ke tab yang salah — ditemukan saat verifikasi mock
                    // 28 Nov 2026: status menampilkan "(EDITOR HARIAN)").
                    g.get("Spreadsheet.SheetName").filter(|s| !s.is_empty()).unwrap_or_else(|| "ALL DATA REGULER".to_string()),
                ),
                Err(_) => ("LOCAL".to_string(), "ALL DATA REGULER".to_string()),
            }
        } else {
            ("LOCAL".to_string(), "ALL DATA REGULER".to_string())
        };
        let snap = read_sheet_snapshot(&db, &sheet_id, &sheet_name);
        // Snapshot dari sesi live sebelumnya (offline): pulihkan kolom
        // dari header tersimpan supaya data terakhir tampil dengan
        // header yang benar (bukan 7 kolom lokal) — mode tetap lokal
        // (live=false) sampai fetch live berhasil.
        let restored_live_cols: Option<(
            Vec<sheet_model::DynColumn>,
            Option<usize>,
            Vec<String>,
            i32,
        )> = if !snap.headers.is_empty() && !snap.rows.is_empty() {
            let mut values = vec![snap.headers.clone()];
            values.extend(snap.rows.iter().cloned());
            let widths = read_column_widths(&db);
            let live = sheets_api::parse_live_sheet(values, &widths);
            if live.headers.is_empty() {
                None
            } else {
                let widths_vec: Vec<f32> = live
                    .columns
                    .iter()
                    .enumerate()
                    .map(|(i, c)| widths.get(&i).copied().unwrap_or(c.width_px))
                    .collect();
                Some((
                    sheets_api::to_dyn_columns(&live, Some(&widths_vec)),
                    live.key_col,
                    live.headers.clone(),
                    snap.first_sheet_row,
                ))
            }
        } else {
            None
        };
        let (init_cols, init_first_row, init_key) = match &restored_live_cols {
            Some((cols, key, _, first)) => (cols.len(), *first, *key),
            None => (7, 1, Some(0)),
        };
        let mut st = spreadsheet_guard::SpreadsheetState::new(
            sheet_id,
            sheet_name,
            init_first_row,
            init_cols,
            snap.rows.clone(),
            init_key,
        );
        if let Some((cols, key, headers, _)) = restored_live_cols {
            st.set_live_columns(cols, key, headers, init_first_row);
            st.live = false; // kolom live, data lokal — belum terhubung
        } else {
            st.set_headers(
                sheet_model::SHEET_COLUMNS
                    .iter()
                    .map(|c| c.header.to_string())
                    .collect(),
            );
        }
        st.drafts = load_sheet_drafts(&db);
        // Pulihkan draft ke grid (hanya yang nilai aslinya masih cocok —
        // C# RestoreDraftsAsync; sisa tetap tersimpan di DB).
        let (display, applied, remaining) = st.reload(snap.rows.clone());
        persist_sheet_drafts(&db, &st.drafts);
        push_grid_to_main(&ui, &st, &display);
        // Kalender bulan ini.
        {
            let (ty, tm, td) = sheet_model::today_ymd();
            push_cal_state_main(&ui, ty, tm, td);
        }
        // Jumlah draft hidup = diterapkan + sisa (keduanya belum
        // tersimpan); status "dipulihkan" tetap pakai `applied`.
        if !st.drafts.is_empty() {
            ui.set_sheet_has_draft(true);
            ui.set_sheet_draft_count(st.drafts.len() as i32);
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

    // ---- Google Sheets LIVE: channel inbox + pemicu fetch startup ----
    // Thread fetch/tulis mengirim SheetsInboxMsg lewat channel ini;
    // timer _sheets_inbox_timer (di bawah, dekat _bar_sync_timer) yang
    // menerapkan ke state di thread UI. Dibuat di sini supaya fetch
    // startup bisa langsung diminta (tidak menahan splash).
    let (sheets_tx_raw, sheets_rx) = mpsc::channel::<SheetsInboxMsg>();
    let sheets_tx = std::rc::Rc::new(sheets_tx_raw);
    let sheets_inbox: std::rc::Rc<std::cell::RefCell<Option<mpsc::Receiver<SheetsInboxMsg>>>> =
        std::rc::Rc::new(std::cell::RefCell::new(Some(sheets_rx)));
    let sheets_pending_save: std::rc::Rc<
        std::cell::RefCell<Option<PendingLiveSave>>,
    > = std::rc::Rc::new(std::cell::RefCell::new(None));
    let sheets_op_deadline: std::rc::Rc<std::cell::RefCell<Option<Instant>>> =
        std::rc::Rc::new(std::cell::RefCell::new(None));
    // Fetch live saat startup (thread latar — splash tidak tertahan).
    // Bila konfigurasi belum lengkap, status jujur offline langsung.
    {
        let cfg = SheetsConfig::from_db(&db);
        if cfg.is_live_ready() {
            ui.set_sheet_status_text(slint::SharedString::from(
                "Menghubungkan ke Google Sheets…",
            ));
            *sheets_op_deadline.borrow_mut() = Some(Instant::now());
            request_live_fetch(db.clone(), (*sheets_tx).clone());
        } else {
            let st = sheet_state.borrow();
            ui.set_sheet_status_text(slint::SharedString::from(offline_sheet_status(&st)));
        }
    }

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

    // Interval auto-refresh Trello (detik) dari properti Settings
    // (`app-trello-interval`, key DB `Trello.RefreshInterval`).
    // Override env `BMACHINE_TRELLO_INTERVAL_SECS` HANYA untuk tes
    // (mempercepat siklus verifikasi — tanpa clamp, tanpa minimum) —
    // di produksi variabel ini tidak diset sehingga jalurnya selalu
    // lewat parse + clamp (default 60, min 30).
    fn trello_refresh_interval(ui: &AppWindow) -> std::time::Duration {
        if let Ok(v) = std::env::var("BMACHINE_TRELLO_INTERVAL_SECS") {
            if let Ok(s) = v.trim().parse::<u64>() {
                if s > 0 {
                    return std::time::Duration::from_secs(s);
                }
            }
        }
        std::time::Duration::from_secs(trello_api::parse_refresh_interval_seconds(
            &ui.get_app_trello_interval(),
        ))
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
        trello_state: std::sync::Arc<trello_api::TrelloSyncState>,
    ) {
        std::thread::spawn(move || {
            let mut synced = 0usize;
            if !k.is_empty() && !t.is_empty() {
                // Skip bila list ini masih di-fetch thread lain
                // (auto-refresh / refresh manual — `if (!IsRefreshing)`
                // ala C#). `try_begin` juga me-reset `last_sync` list
                // ini, jadi refresh manual ikut me-reset timer.
                if !el.is_empty() && trello_state.try_begin(0) {
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
                        let descs: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| {
                                slint::SharedString::from(
                                    c.desc.clone().unwrap_or_default(),
                                )
                            })
                            .collect();
                        // Label Trello kartu (nama label; maks 2 dipakai
                        // detail: label-1 + label-2) + jumlah lampiran
                        // (badges.attachments) — tampil di detail kartu
                        // (ref trello-sidepanel-ref.jpg, 28 Nov 2026).
                        let label1s: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| {
                                let n = c
                                    .labels
                                    .as_ref()
                                    .and_then(|l| l.first())
                                    .and_then(|l| l.name.clone())
                                    .unwrap_or_default();
                                slint::SharedString::from(n)
                            })
                            .collect();
                        let label2s: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| {
                                let n = c
                                    .labels
                                    .as_ref()
                                    .and_then(|l| l.get(1))
                                    .and_then(|l| l.name.clone())
                                    .unwrap_or_default();
                                slint::SharedString::from(n)
                            })
                            .collect();
                        let atts: Vec<i32> = cards
                            .iter()
                            .map(|c| {
                                c.badges
                                    .as_ref()
                                    .and_then(|b| b.attachments)
                                    .unwrap_or(0) as i32
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
                            u.set_editing_desc(slint::ModelRc::new(slint::VecModel::from(descs.clone())));
                            u.set_editing_label1(slint::ModelRc::new(slint::VecModel::from(label1s.clone())));
                            u.set_editing_label2(slint::ModelRc::new(slint::VecModel::from(label2s.clone())));
                            u.set_editing_atts(slint::ModelRc::new(slint::VecModel::from(atts.clone())));
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
                    trello_state.finish(0);
                }
                if !rl.is_empty() && trello_state.try_begin(1) {
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
                        let descs: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| {
                                slint::SharedString::from(
                                    c.desc.clone().unwrap_or_default(),
                                )
                            })
                            .collect();
                        // Label Trello kartu (nama label; maks 2 dipakai
                        // detail: label-1 + label-2) + jumlah lampiran
                        // (badges.attachments) — tampil di detail kartu
                        // (ref trello-sidepanel-ref.jpg, 28 Nov 2026).
                        let label1s: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| {
                                let n = c
                                    .labels
                                    .as_ref()
                                    .and_then(|l| l.first())
                                    .and_then(|l| l.name.clone())
                                    .unwrap_or_default();
                                slint::SharedString::from(n)
                            })
                            .collect();
                        let label2s: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| {
                                let n = c
                                    .labels
                                    .as_ref()
                                    .and_then(|l| l.get(1))
                                    .and_then(|l| l.name.clone())
                                    .unwrap_or_default();
                                slint::SharedString::from(n)
                            })
                            .collect();
                        let atts: Vec<i32> = cards
                            .iter()
                            .map(|c| {
                                c.badges
                                    .as_ref()
                                    .and_then(|b| b.attachments)
                                    .unwrap_or(0) as i32
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
                            u.set_revision_desc(slint::ModelRc::new(slint::VecModel::from(descs.clone())));
                            u.set_revision_label1(slint::ModelRc::new(slint::VecModel::from(label1s.clone())));
                            u.set_revision_label2(slint::ModelRc::new(slint::VecModel::from(label2s.clone())));
                            u.set_revision_atts(slint::ModelRc::new(slint::VecModel::from(atts.clone())));
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
                    trello_state.finish(1);
                }
                if !ll.is_empty() && trello_state.try_begin(2) {
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
                        let descs: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| {
                                slint::SharedString::from(
                                    c.desc.clone().unwrap_or_default(),
                                )
                            })
                            .collect();
                        // Label Trello kartu (nama label; maks 2 dipakai
                        // detail: label-1 + label-2) + jumlah lampiran
                        // (badges.attachments) — tampil di detail kartu
                        // (ref trello-sidepanel-ref.jpg, 28 Nov 2026).
                        let label1s: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| {
                                let n = c
                                    .labels
                                    .as_ref()
                                    .and_then(|l| l.first())
                                    .and_then(|l| l.name.clone())
                                    .unwrap_or_default();
                                slint::SharedString::from(n)
                            })
                            .collect();
                        let label2s: Vec<slint::SharedString> = cards
                            .iter()
                            .map(|c| {
                                let n = c
                                    .labels
                                    .as_ref()
                                    .and_then(|l| l.get(1))
                                    .and_then(|l| l.name.clone())
                                    .unwrap_or_default();
                                slint::SharedString::from(n)
                            })
                            .collect();
                        let atts: Vec<i32> = cards
                            .iter()
                            .map(|c| {
                                c.badges
                                    .as_ref()
                                    .and_then(|b| b.attachments)
                                    .unwrap_or(0) as i32
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
                            u.set_late_desc(slint::ModelRc::new(slint::VecModel::from(descs.clone())));
                            u.set_late_label1(slint::ModelRc::new(slint::VecModel::from(label1s.clone())));
                            u.set_late_label2(slint::ModelRc::new(slint::VecModel::from(label2s.clone())));
                            u.set_late_atts(slint::ModelRc::new(slint::VecModel::from(atts.clone())));
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
                    trello_state.finish(2);
                }
            }
            // Sync otomatis / startup (show_toast=false) diam-diam:
            // jangan sentuh `trello-refreshing` (timer .slint 600ms
            // akan toast tiap tick bila flag ini disentuh) dan jangan
            // toast. Hanya refresh manual yang menutup flag + toast.
            if show_toast {
                let _ = ui_weak.upgrade_in_event_loop(move |u| {
                    u.set_trello_refreshing(false);
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
                });
            }
        });
    }

    // Status sinkronisasi Trello per list (dipakai startup / refresh
    // manual / timer auto-refresh — lihat `TrelloSyncState`).
    let trello_sync_state = std::sync::Arc::new(trello_api::TrelloSyncState::new());

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
            trello_sync_state.clone(),
        );
    }

    // ---- Appearance, Window Controls, Trello & Path Callbacks ----
    {
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        let bar_cell_sync = bar_win_cell.clone();
        let radial_cell_sync = radial_win_cell.clone();
        let task_cell_sync = task_win.clone();
        let sheet_cell_sync = sheet_win_cell.clone();
        let toolbox_cell_sync = toolbox_win_cell.clone();
        let card_cell_sync = card_win_cell.clone();
        let comment_cell_sync = comment_win_cell.clone();
        let batch_cell_sync = batch_win_cell.clone();
        let doc_cell_sync = doc_win_cell.clone();
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
                        sync_task_theme(&ui, &task_cell_sync);
                        sync_chrome_to_windows(
                            &ui,
                            &sheet_cell_sync,
                            &toolbox_cell_sync,
                            &card_cell_sync,
                            &comment_cell_sync,
                            &batch_cell_sync,
                            &doc_cell_sync,
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
        let task_cell_sync = task_win.clone();
        let sheet_cell_sync = sheet_win_cell.clone();
        let toolbox_cell_sync = toolbox_win_cell.clone();
        let card_cell_sync = card_win_cell.clone();
        let comment_cell_sync = comment_win_cell.clone();
        let batch_cell_sync = batch_win_cell.clone();
        let doc_cell_sync = doc_win_cell.clone();
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
                sync_task_theme(&ui, &task_cell_sync);
                sync_chrome_to_windows(
                    &ui,
                    &sheet_cell_sync,
                    &toolbox_cell_sync,
                    &card_cell_sync,
                    &comment_cell_sync,
                    &batch_cell_sync,
                    &doc_cell_sync,
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
        let task_cell_sync = task_win.clone();
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
                        sync_task_theme(&ui, &task_cell_sync);
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
        let task_cell_sync = task_win.clone();
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
                                sync_task_theme(&ui, &task_cell_sync);
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
                        // Interval auto-refresh Trello: simpan hasil
                        // clamp (min 30, default 60) + tulis balik ke
                        // properti supaya field menampilkan nilai yang
                        // dipakai timer (bukan ketikan mentah).
                        let interval_secs =
                            trello_api::parse_refresh_interval_seconds(
                                &ui.get_app_trello_interval(),
                            );
                        let _ = db_guard.set(
                            "Trello.RefreshInterval",
                            &interval_secs.to_string(),
                            "Int",
                        );
                        ui.set_app_trello_interval(slint::SharedString::from(
                            interval_secs.to_string(),
                        ));
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
        let db_c = db.clone();
        let ex_state = explorer_state.clone();
        ui.on_save_explorer_settings(move || {
            let Some(ui) = ui_weak.upgrade() else { return; };
            let path = ui.get_ex_set_path().to_string();
            let view = ui.get_ex_set_view().to_string();
            let sort = ui.get_ex_set_sort().to_string();
            let group = ui.get_ex_set_group().to_string();
            let hidden = ui.get_ex_set_hidden();
            let show_ext = ui.get_ex_set_ext();
            let zoom = ui.get_ex_set_zoom();
            let showbar = ui.get_ex_set_showbar();
            let font = ui.get_ex_set_font().to_string();
            ex_db_set(&db_c, "Configs.Explorer.DefaultPath", &path);
            // Details->Vertical, List->Horizontal, Thumbnails->Thumbnail,
            // Tiles->Tiles, Split->Split (C# ViewMode vs tampilan Settings).
            let layout = match view.as_str() {
                "List" => "Horizontal",
                "Thumbnails" => "Thumbnail",
                "Tiles" => "Tiles",
                "Split" => "Split",
                _ => "Vertical",
            };
            ex_db_set(&db_c, "Configs.Explorer.ViewMode", layout);
            ex_db_set(&db_c, "Configs.Explorer.SortBy", &sort);
            ex_db_set(&db_c, "Configs.Explorer.GroupBy", &group);
            ex_db_set(
                &db_c,
                "Configs.Explorer.ShowHiddenFiles",
                if hidden { "true" } else { "false" },
            );
            ex_db_set(
                &db_c,
                "Configs.Explorer.ShowFileExtensions",
                if show_ext { "true" } else { "false" },
            );
            ex_db_set(&db_c, "Configs.Explorer.ContentZoom", &zoom.to_string());
            ex_db_set(
                &db_c,
                "Configs.Explorer.ShowPathBar",
                if showbar { "true" } else { "false" },
            );
            ex_db_set(
                &db_c,
                "Configs.Explorer.FontFamily",
                if font.is_empty() { "Inter" } else { font.as_str() },
            );
            // Berlaku langsung tanpa restart: hidden files + root baru +
            // path bar + font. Reload SELALU (dulu hanya bila overlay ==
            // "EXPLORER" — dari EXPLORER-SETTINGS tidak pernah reload,
            // jadi sort/group/layout baru tidak terlihat saat kembali).
            ui.set_ex_show_pathbar(showbar);
            ui.set_ex_font(slint::SharedString::from(
                if font.is_empty() { "Inter" } else { font.as_str() },
            ));
            {
                let mut st = ex_state.lock().unwrap();
                st.show_hidden = hidden;
                if !path.is_empty() {
                    st.root_path = PathBuf::from(&path);
                }
                ex_reload_cur(&ui, &mut st, &db_c);
            }
            ui.set_toast(slint::SharedString::from("Pengaturan Explorer disimpan"));
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
                            "explorer" => ui.set_ex_set_path(p.clone()),
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
                                    "explorer" => Some("Configs.Explorer.DefaultPath"),
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
        let trello_state_test = trello_sync_state.clone();
    // ================= BATCH callbacks (Stage 2 + stub tahap 3/4/5) =================
    {
        // -- Tambah folder: dialog folder (thread, blocking) --
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        let db_c = db.clone();
        ui.on_batch_pick_folder(move || {
            let ui_w = ui_weak.clone();
            let st_c = st.clone();
            let db_cc = db_c.clone();
            // Weak (Send) — Rc di BatchWinCell tidak boleh masuk thread.
            let batch_w = batch_c.borrow().as_ref().map(|w| w.as_weak());
            std::thread::spawn(move || {
                match platform::pick_folder("Pilih Folder Sumber") {
                    platform::PickResult::Picked(path) => {
                        let p = std::path::PathBuf::from(&path);
                        let msg = {
                            let mut s = st_c.lock().unwrap();
                            if !p.is_dir() {
                                format!("Bukan folder: {}", path)
                            } else {
                                s.add_folder(&p);
                                let out = s
                                    .roots
                                    .first()
                                    .map(|r| r.output_path.display().to_string())
                                    .unwrap_or_default();
                                format!(
                                    "SOURCE: {}  →  Output: {}",
                                    p.file_name()
                                        .map(|n| n.to_string_lossy().into_owned())
                                        .unwrap_or(path.clone()),
                                    out
                                )
                            }
                        };
                        // Working set ikut berubah (add_folder mencatat
                        // sumber baru) — klon SEBELUM st_c dipindah ke
                        // closure event-loop di bawah.
                        let ws = st_c.lock().unwrap().working_set.clone();
                        let _ = ui_w.upgrade_in_event_loop(move |ui| {
                            {
                                let s = st_c.lock().unwrap();
                                push_batch_to_ui_inner(&ui, &s);
                                if let Some(w) =
                                    batch_w.as_ref().and_then(|w| w.upgrade())
                                {
                                    sync_batch_window(&w, &ui);
                                }
                            }
                            ui.set_toast(slint::SharedString::from(msg));
                        });
                        persist_working_set(&db_cc, &ws);
                    }
                    platform::PickResult::Cancelled => {}
                    platform::PickResult::Unavailable(e) => {
                        let _ = ui_w.upgrade_in_event_loop(move |ui| {
                            ui.set_toast(slint::SharedString::from(format!(
                                "Pemilih folder tidak tersedia: {}",
                                e
                            )));
                        });
                    }
                }
            });
        });
    }
    {
        // -- Dari Explorer: kirim folder ke BATCH (satu argumen = full path) --
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        let db_c = db.clone();
        ui.on_batch_add_folder(move |path| {
            let p = std::path::PathBuf::from(path.to_string());
            let msg = {
                let mut s = st.lock().unwrap();
                if !p.is_dir() {
                    format!("Folder tidak ditemukan: {}", p.display())
                } else {
                    s.add_folder(&p);
                    format!(
                        "Dikirim ke BATCH: {}",
                        p.file_name()
                            .map(|n| n.to_string_lossy().into_owned())
                            .unwrap_or_else(|| p.display().to_string())
                    )
                }
            };
            // Working set ikut berubah — simpan.
            let ws = st.lock().unwrap().working_set.clone();
            persist_working_set(&db_c, &ws);
            if let Some(ui) = ui_weak.upgrade() {
                push_batch_to_ui(&ui, &st.lock().unwrap(), &batch_c);
                ui.set_toast(slint::SharedString::from(msg));
            }
        });
    }
    {
        // -- Klik nama: seleksi + salin path (LANJUTKAN §10). --
        // Klik ganda dideteksi di sini (bukan lewat Slint
        // `double-clicked` — lihat batch_handle_double_click).
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        let pl = proc_log.clone();
        ui.on_batch_row_clicked(move |panel_s, id| {
            let panel = if panel_s == "OUTPUT" {
                batch::BatchPanel::Output
            } else {
                batch::BatchPanel::Source
            };
            let is_dbl = batch::is_double_click(
                &mut BATCH_LAST_CLICK.lock().unwrap(),
                panel_s.as_str(),
                id.as_str(),
                std::time::Instant::now(),
            );
            if is_dbl {
                batch_handle_double_click(&ui_weak, &st, &pl, panel, &id, &batch_c);
                return;
            }
            let shift = BATCH_SHIFT_HELD.load(Ordering::SeqCst);
            let ctrl = BATCH_CTRL_HELD.load(Ordering::SeqCst);
            let (toast, master_name) = {
                let mut s = st.lock().unwrap();
                let visible = match panel {
                    batch::BatchPanel::Source => s.src_rows(),
                    batch::BatchPanel::Output => s.out_rows(),
                };
                let plain = !shift && !ctrl;
                s.selection.click(panel, &visible, &id, shift, ctrl);
                let mut toast = String::new();
                let mut master = String::new();
                if plain {
                    if let Some(n) = batch::find_node(&s.roots, panel, &id) {
                        master = n.name.clone();
                        let path = n.path.to_string_lossy().into_owned();
                        match platform::copy_to_clipboard(&path) {
                            Ok(_) => toast = format!("Path disalin: {}", path),
                            Err(e) => toast = format!("Gagal menyalin path: {}", e),
                        }
                    }
                }
                (toast, master)
            };
            if let Some(ui) = ui_weak.upgrade() {
                if !master_name.is_empty() {
                    ui.set_selected_master(slint::SharedString::from(master_name));
                }
                push_batch_to_ui(&ui, &st.lock().unwrap(), &batch_c);
                if !toast.is_empty() {
                    ui.set_toast(slint::SharedString::from(toast));
                }
            }
        });
    }
    {
        // -- Klik kanan DOWN pada nama: simpan seleksi, lalu expand/collapse. --
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        ui.on_batch_row_right_down(move |panel_s, id| {
            let panel = if panel_s == "OUTPUT" {
                batch::BatchPanel::Output
            } else {
                batch::BatchPanel::Source
            };
            {
                let mut s = st.lock().unwrap();
                s.selection.save();
                if let Some(n) = batch::find_node(&s.roots, panel, &id) {
                    if n.is_dir {
                        batch::toggle_expand(&mut s.roots, panel, &id);
                    }
                }
            }
            if let Some(ui) = ui_weak.upgrade() {
                push_batch_to_ui(&ui, &st.lock().unwrap(), &batch_c);
            }
        });
    }
    {
        // -- Klik kanan UP: snapshot seleksi, siapkan target menu konteks. --
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        let ctx = batch_ctx.clone();
        ui.on_batch_row_right_up(move |panel_s, id| {
            let panel = if panel_s == "OUTPUT" {
                batch::BatchPanel::Output
            } else {
                batch::BatchPanel::Source
            };
            let (name, is_dir, is_root) = {
                let mut s = st.lock().unwrap();
                s.selection.snapshot();
                if s.selection.ids.is_empty() {
                    let visible = match panel {
                        batch::BatchPanel::Source => s.src_rows(),
                        batch::BatchPanel::Output => s.out_rows(),
                    };
                    s.selection.click(panel, &visible, &id, false, false);
                }
                match batch::find_node(&s.roots, panel, &id) {
                    Some(n) => {
                        let is_root = s
                            .roots
                            .iter()
                            .any(|r| r.source_path == n.path || r.output_path == n.path);
                        (n.name.clone(), n.is_dir, is_root)
                    }
                    None => (id.to_string(), true, false),
                }
            };
            *ctx.lock().unwrap() = BatchCtxTarget {
                panel: Some(panel),
                id: id.to_string(),
                name: name.clone(),
                is_dir,
                is_root,
                newfolder_parent: String::new(),
            };
            if let Some(ui) = ui_weak.upgrade() {
                push_batch_to_ui(&ui, &st.lock().unwrap(), &batch_c);
                ui.set_ctx_node(slint::SharedString::from(name));
                ui.set_ctx_node_id(slint::SharedString::from(id.to_string()));
                ui.set_ctx_panel(slint::SharedString::from(panel_s.to_string()));
                ui.set_ctx_is_dir(is_dir);
                ui.set_ctx_is_root(is_root);
                sync_batch_to_open_window(&batch_c, &ui);
            }
        });
    }
    {
        // -- Menu konteks dibuka dari tombol ⋯ (tidak mengganggu seleksi). --
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        let ctx = batch_ctx.clone();
        ui.on_batch_open_ctx(move |panel_s, id| {
            let panel = if panel_s == "OUTPUT" {
                batch::BatchPanel::Output
            } else {
                batch::BatchPanel::Source
            };
            let (name, is_dir, is_root) = {
                let s = st.lock().unwrap();
                match batch::find_node(&s.roots, panel, &id) {
                    Some(n) => {
                        let is_root = s
                            .roots
                            .iter()
                            .any(|r| r.source_path == n.path || r.output_path == n.path);
                        (n.name.clone(), n.is_dir, is_root)
                    }
                    None => (id.to_string(), true, false),
                }
            };
            *ctx.lock().unwrap() = BatchCtxTarget {
                panel: Some(panel),
                id: id.to_string(),
                name: name.clone(),
                is_dir,
                is_root,
                newfolder_parent: String::new(),
            };
            if let Some(ui) = ui_weak.upgrade() {
                ui.set_ctx_node(slint::SharedString::from(name));
                ui.set_ctx_node_id(slint::SharedString::from(id.to_string()));
                ui.set_ctx_panel(slint::SharedString::from(panel_s.to_string()));
                ui.set_ctx_is_dir(is_dir);
                ui.set_ctx_is_root(is_root);
                sync_batch_to_open_window(&batch_c, &ui);
            }
        });
    }
    {
        // -- Tutup menu konteks (klik di luar / Esc / klik kanan). --
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let ctx = batch_ctx.clone();
        ui.on_batch_close_ctx(move || {
            *ctx.lock().unwrap() = BatchCtxTarget::default();
            if let Some(ui) = ui_weak.upgrade() {
                ui.set_ctx_node(slint::SharedString::from(""));
                ui.set_ctx_node_id(slint::SharedString::from(""));
                sync_batch_to_open_window(&batch_c, &ui);
            }
        });
    }
    {
        // -- Expand/collapse via chevron. --
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        ui.on_batch_toggle_expand(move |panel_s, id| {
            let panel = if panel_s == "OUTPUT" {
                batch::BatchPanel::Output
            } else {
                batch::BatchPanel::Source
            };
            {
                let mut s = st.lock().unwrap();
                batch::toggle_expand(&mut s.roots, panel, &id);
            }
            if let Some(ui) = ui_weak.upgrade() {
                push_batch_to_ui(&ui, &st.lock().unwrap(), &batch_c);
            }
        });
    }
    {
        // -- Menu konteks: Subfolder baru (buka input inline). --
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let ctx = batch_ctx.clone();
        let st = batch_state.clone();
        ui.on_batch_ctx_new_subfolder(move || {
            let t = ctx.lock().unwrap().clone();
            let panel = t.panel.unwrap_or(batch::BatchPanel::Source);
            let parent_path = {
                let s = st.lock().unwrap();
                match batch::find_node(&s.roots, panel, &t.id) {
                    Some(n) if n.is_dir => n.path.clone(),
                    Some(n) => n
                        .path
                        .parent()
                        .map(|p| p.to_path_buf())
                        .unwrap_or_else(|| n.path.clone()),
                    None => return,
                }
            };
            ctx.lock().unwrap().newfolder_parent = parent_path.display().to_string();
            if let Some(ui) = ui_weak.upgrade() {
                ui.set_newfolder_open(true);
                ui.set_newfolder_panel(slint::SharedString::from(
                    if panel == batch::BatchPanel::Output {
                        "OUTPUT"
                    } else {
                        "SOURCE"
                    },
                ));
                ui.set_newfolder_parent(slint::SharedString::from(parent_path.display().to_string()));
                ui.set_newfolder_name(slint::SharedString::from(""));
                ui.set_ctx_node(slint::SharedString::from(""));
                sync_batch_to_open_window(&batch_c, &ui);
            }
        });
    }
    {
        // -- Konfirmasi subfolder baru (Enter / tombol Buat). --
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        let ctx = batch_ctx.clone();
        // Nama + panel diterima sebagai argumen dari BatchView — saat
        // terlepas, nilainya hidup di BatchWindow, bukan AppWindow
        // (membaca ui.get_newfolder_name() di sini akan kosong).
        ui.on_batch_confirm_new_folder(move |name, panel_s| {
            let name = name.to_string();
            let panel_s = panel_s.to_string();
            let parent = ctx.lock().unwrap().newfolder_parent.clone();
            let name = name.trim().to_string();
            let toast: String = if name.is_empty() {
                "Nama folder tidak boleh kosong.".to_string()
            } else if name.contains(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) {
                "Nama folder tidak boleh mengandung / \\ : * ? \" < > |".to_string()
            } else {
                let panel = if panel_s == "OUTPUT" {
                    batch::BatchPanel::Output
                } else {
                    batch::BatchPanel::Source
                };
                let new_path = std::path::PathBuf::from(&parent).join(&name);
                match std::fs::create_dir(&new_path) {
                    Ok(_) => {
                        let mut s = st.lock().unwrap();
                        batch::rescan_node(&mut s.roots, panel, &parent);
                        format!("Subfolder '{}' dibuat di {}", name, parent)
                    }
                    Err(e) => format!("Gagal membuat subfolder: {}", e),
                }
            };
            if let Some(ui) = ui_weak.upgrade() {
                if toast.starts_with("Subfolder") {
                    ui.set_newfolder_open(false);
                    ui.set_newfolder_name(slint::SharedString::from(""));
                }
                push_batch_to_ui(&ui, &st.lock().unwrap(), &batch_c);
                ui.set_toast(slint::SharedString::from(toast));
            }
        });
    }
    {
        // -- Batal subfolder baru (Esc / tombol Batal). --
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        ui.on_batch_cancel_new_folder(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.set_newfolder_open(false);
                ui.set_newfolder_name(slint::SharedString::from(""));
                sync_batch_to_open_window(&batch_c, &ui);
            }
        });
    }
    {
        // -- Menu konteks: Salin path (folder terpilih). --
        let ui_weak = ui.as_weak();
        let ctx = batch_ctx.clone();
        ui.on_batch_ctx_copy_path(move || {
            let t = ctx.lock().unwrap().clone();
            let msg = match platform::copy_to_clipboard(&t.id) {
                Ok(_) => format!("Path disalin: {}", t.id),
                Err(e) => format!("Gagal menyalin path: {}", e),
            };
            if let Some(ui) = ui_weak.upgrade() {
                ui.set_ctx_node(slint::SharedString::from(""));
                ui.set_toast(slint::SharedString::from(msg));
            }
        });
    }
    {
        // -- Menu konteks: Salin path OUTPUT pasangan folder terpilih. --
        let ui_weak = ui.as_weak();
        let ctx = batch_ctx.clone();
        let st = batch_state.clone();
        ui.on_batch_ctx_copy_output_path(move || {
            let t = ctx.lock().unwrap().clone();
            let panel = t.panel.unwrap_or(batch::BatchPanel::Source);
            let out_path = {
                let s = st.lock().unwrap();
                match batch::resolve_replace_pair(&s.roots, panel, &t.id) {
                    Some((o, _)) => o.to_string_lossy().into_owned(),
                    None => t.id.clone(),
                }
            };
            let msg = match platform::copy_to_clipboard(&out_path) {
                Ok(_) => format!("Output path disalin: {}", out_path),
                Err(e) => format!("Gagal menyalin path: {}", e),
            };
            if let Some(ui) = ui_weak.upgrade() {
                ui.set_ctx_node(slint::SharedString::from(""));
                ui.set_toast(slint::SharedString::from(msg));
            }
        });
    }
    {
        // -- Menu konteks: Hapus dari disk + dari tree. --
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let ctx = batch_ctx.clone();
        let st = batch_state.clone();
        ui.on_batch_ctx_delete(move || {
            let t = ctx.lock().unwrap().clone();
            let panel = t.panel.unwrap_or(batch::BatchPanel::Source);
            let path = std::path::PathBuf::from(&t.id);
            let msg = if !path.exists() {
                format!("Sudah tidak ada: {}", t.name)
            } else {
                let r = if path.is_dir() {
                    std::fs::remove_dir_all(&path)
                } else {
                    std::fs::remove_file(&path)
                };
                match r {
                    Ok(_) => {
                        let mut s = st.lock().unwrap();
                        s.selection.ids.retain(|x| x != &t.id);
                        batch::remove_from_tree(&mut s.roots, panel, &path);
                        format!("Dihapus dari disk: {}", t.name)
                    }
                    Err(e) => format!("Gagal menghapus {}: {}", t.name, e),
                }
            };
            if let Some(ui) = ui_weak.upgrade() {
                ui.set_ctx_node(slint::SharedString::from(""));
                push_batch_to_ui(&ui, &st.lock().unwrap(), &batch_c);
                ui.set_toast(slint::SharedString::from(msg));
            }
        });
    }
    {
        // -- Menu konteks: Replace (Auto) — semua yang terseleksi sekaligus. --
        let ui_weak = ui.as_weak();
        let ctx = batch_ctx.clone();
        let st = batch_state.clone();
        let db_c = db.clone();
        let pl = proc_log.clone();
        ui.on_batch_ctx_replace_auto(move || {
            let t = ctx.lock().unwrap().clone();
            let panel = t.panel.unwrap_or(batch::BatchPanel::Source);
            let ids: Vec<String> = {
                let s = st.lock().unwrap();
                if s.selection.ids.len() > 1 {
                    s.selection.ids.clone()
                } else {
                    vec![t.id.clone()]
                }
            };
            let pairs = {
                let s = st.lock().unwrap();
                batch::resolve_replace_pairs(&s.roots, panel, &ids)
            };
            let msg = if pairs.is_empty() {
                "Replace: folder yang dipilih bukan direktori / bukan bagian dari pasangan BATCH."
                    .to_string()
            } else {
                batch_launch_replace(pairs, &db_c, &pl)
            };
            if let Some(ui) = ui_weak.upgrade() {
                push_log_lines(&ui, &pl);
                ui.set_ctx_node(slint::SharedString::from(""));
                ui.set_toast(slint::SharedString::from(msg));
            }
        });
    }
    {
        // -- Menu konteks: Replace (Manual) — tanya folder pasangan via picker. --
        let ui_weak = ui.as_weak();
        let ctx = batch_ctx.clone();
        let st = batch_state.clone();
        let db_c = db.clone();
        let pl = proc_log.clone();
        ui.on_batch_ctx_replace_manual(move || {
            let t = ctx.lock().unwrap().clone();
            let panel = t.panel.unwrap_or(batch::BatchPanel::Source);
            let title = if panel == batch::BatchPanel::Output {
                "Pilih Folder Source (Input) yang cocok"
            } else {
                "Pilih Folder Output (Master) yang cocok"
            };
            let ui_w = ui_weak.clone();
            let st_c = st.clone();
            let db_cc = db_c.clone();
            let pl_c = pl.clone();
            std::thread::spawn(move || {
                let msg = match platform::pick_folder(title) {
                    platform::PickResult::Picked(p) => {
                        let picked_path = std::path::PathBuf::from(&p);
                        let base_pair = {
                            let s = st_c.lock().unwrap();
                            batch::resolve_replace_pair(&s.roots, panel, &t.id)
                        };
                        match base_pair {
                            Some((mut o, mut s2)) => {
                                match panel {
                                    batch::BatchPanel::Source => o = picked_path,
                                    batch::BatchPanel::Output => s2 = picked_path,
                                }
                                batch_launch_replace(vec![(o, s2)], &db_cc, &pl_c)
                            }
                            None => "Replace: folder yang dipilih bukan direktori / bukan bagian dari pasangan BATCH.".to_string(),
                        }
                    }
                    platform::PickResult::Cancelled => return,
                    platform::PickResult::Unavailable(e) => {
                        format!("Pemilih folder tidak tersedia: {}", e)
                    }
                };
                let pl_ui = pl_c.clone();
                let _ = ui_w.upgrade_in_event_loop(move |ui| {
                    push_log_lines(&ui, &pl_ui);
                    ui.set_ctx_node(slint::SharedString::from(""));
                    ui.set_toast(slint::SharedString::from(msg));
                });
            });
        });
    }
    {
        // -- Replace (Auto) dari tombol aksi baris A — seleksi saja, tanpa fallback ctx. --
        let ui_weak = ui.as_weak();
        let st = batch_state.clone();
        let db_c = db.clone();
        let pl = proc_log.clone();
        ui.on_batch_replace_auto(move || {
            let (panel, ids) = {
                let s = st.lock().unwrap();
                (
                    s.selection.panel.unwrap_or(batch::BatchPanel::Source),
                    s.selection.ids.clone(),
                )
            };
            if ids.is_empty() {
                if let Some(ui) = ui_weak.upgrade() {
                    ui.set_toast(slint::SharedString::from(
                        "Pilih folder dulu (klik nama folder).",
                    ));
                }
                return;
            }
            let pairs = {
                let s = st.lock().unwrap();
                batch::resolve_replace_pairs(&s.roots, panel, &ids)
            };
            let msg = if pairs.is_empty() {
                "Replace: folder yang dipilih bukan direktori / bukan bagian dari pasangan BATCH."
                    .to_string()
            } else {
                batch_launch_replace(pairs, &db_c, &pl)
            };
            if let Some(ui) = ui_weak.upgrade() {
                push_log_lines(&ui, &pl);
                ui.set_toast(slint::SharedString::from(msg));
            }
        });
    }
    {
        // -- Refresh: muat ulang SOURCE & OUTPUT dari disk. --
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        ui.on_batch_refresh(move || {
            {
                let mut s = st.lock().unwrap();
                batch::rescan_all(&mut s.roots);
            }
            if let Some(ui) = ui_weak.upgrade() {
                push_batch_to_ui(&ui, &st.lock().unwrap(), &batch_c);
                ui.set_toast(slint::SharedString::from("Memuat ulang SOURCE & OUTPUT…"));
            }
        });
    }
    {
        // -- Bersihkan SOURCE (file tetap ada di disk). --
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        ui.on_batch_clear(move || {
            {
                let mut s = st.lock().unwrap();
                s.clear();
            }
            if let Some(ui) = ui_weak.upgrade() {
                push_batch_to_ui(&ui, &st.lock().unwrap(), &batch_c);
                ui.set_toast(slint::SharedString::from(
                    "SOURCE dibersihkan (file tetap ada di disk).",
                ));
            }
        });
    }
    {
        // -- Folder Reguler: buka popup Kelola (manajemen direktori
        // saja — daftar sekolah sudah di tampilan utama, dari cache /
        // scan latar; membuka popup TIDAK memindai ulang).
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        let scan = reguler_scan_state.clone();
        let handle = batch_run_handle.clone();
        ui.on_batch_reguler_manage(move || {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            let run_active = handle.is_running();
            push_reguler_to_ui(&ui, &st.lock().unwrap(), &scan.lock().unwrap(), run_active, &batch_c);
            ui.set_reguler_manage_open(true);
            sync_batch_to_open_window(&batch_c, &ui);
        });
    }
    {
        // -- Folder Reguler: tutup popup Kelola (×, Tutup, klik di luar,
        // klik kanan, Esc).
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        ui.on_batch_reguler_close(move || {
            if let Some(ui) = ui_weak.upgrade() {
                ui.set_reguler_manage_open(false);
                sync_batch_to_open_window(&batch_c, &ui);
            }
        });
    }
    {
        // -- Folder Reguler: tambah direktori (dialog pilih folder).
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        let db_c = db.clone();
        let scan = reguler_scan_state.clone();
        let handle = batch_run_handle.clone();
        ui.on_batch_reguler_add(move || {
            let ui_w = ui_weak.clone();
            let st_c = st.clone();
            let db_cc = db_c.clone();
            let scan_c = scan.clone();
            let handle_c = handle.clone();
            // Weak (Send) — Rc di BatchWinCell tidak boleh masuk thread.
            let batch_w = batch_c.borrow().as_ref().map(|w| w.as_weak());
            std::thread::spawn(move || {
                match platform::pick_folder("Pilih Folder Reguler") {
                    platform::PickResult::Picked(path) => {
                        let p = std::path::PathBuf::from(&path);
                        let msg = {
                            let mut s = st_c.lock().unwrap();
                            if !p.is_dir() {
                                format!("Bukan folder: {}", path)
                            } else if s
                                .reguler_dirs
                                .iter()
                                .any(|d| d.path.to_ascii_lowercase() == path.to_ascii_lowercase())
                            {
                                format!("Sudah ada di daftar: {}", path)
                            } else {
                                s.reguler_dirs.push(batch::RegulerDir::new(&path));
                                persist_reguler_dirs(&db_cc, &s.reguler_dirs);
                                format!("Folder Reguler ditambah: {}", path)
                            }
                        };
                        let _ = ui_w.upgrade_in_event_loop(move |ui| {
                            let run_active = handle_c.is_running();
                            push_reguler_to_ui_inner(
                                &ui,
                                &st_c.lock().unwrap(),
                                &scan_c.lock().unwrap(),
                                run_active,
                            );
                            push_batch_to_ui_inner(&ui, &st_c.lock().unwrap());
                            if let Some(w) =
                                batch_w.as_ref().and_then(|w| w.upgrade())
                            {
                                sync_batch_window(&w, &ui);
                            }
                            ui.set_toast(slint::SharedString::from(msg));
                        });
                    }
                    _ => {}
                }
            });
        });
    }
    {
        // -- Folder Reguler: hapus direktori (terkunci = tolak).
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        let db_c = db.clone();
        let scan = reguler_scan_state.clone();
        let handle = batch_run_handle.clone();
        ui.on_batch_reguler_remove(move |idx| {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            let msg = {
                let mut s = st.lock().unwrap();
                let Some(d) = s.reguler_dirs.get(idx as usize) else {
                    return;
                };
                if d.locked {
                    format!("Direktori terkunci — buka kunci dulu: {}", d.display_name())
                } else {
                    let removed = d.path.clone();
                    s.reguler_dirs.remove(idx as usize);
                    persist_reguler_dirs(&db_c, &s.reguler_dirs);
                    let mut sc = scan.lock().unwrap();
                    if sc.dir == removed {
                        sc.dir.clear();
                        sc.all.clear();
                        sc.scanning = false;
                    }
                    format!("Folder Reguler dihapus: {}", removed)
                }
            };
            let run_active = handle.is_running();
            push_reguler_to_ui(&ui, &st.lock().unwrap(), &scan.lock().unwrap(), run_active, &batch_c);
            push_batch_to_ui(&ui, &st.lock().unwrap(), &batch_c);
            ui.set_toast(slint::SharedString::from(msg));
        });
    }
    {
        // -- Folder Reguler: kunci/buka kunci direktori.
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        let db_c = db.clone();
        let scan = reguler_scan_state.clone();
        let handle = batch_run_handle.clone();
        ui.on_batch_reguler_toggle_lock(move |idx| {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            {
                let mut s = st.lock().unwrap();
                if let Some(d) = s.reguler_dirs.get_mut(idx as usize) {
                    d.locked = !d.locked;
                }
                persist_reguler_dirs(&db_c, &s.reguler_dirs);
            }
            let run_active = handle.is_running();
            push_reguler_to_ui(&ui, &st.lock().unwrap(), &scan.lock().unwrap(), run_active, &batch_c);
        });
    }
    {
        // -- Folder Reguler: pilih direktori → tampil dari cache bila
        // ada (lalu refresh inkremental), kalau tidak scan penuh.
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        let db_c = db.clone();
        let scan = reguler_scan_state.clone();
        let rxh = reguler_scan_rx.clone();
        let handle = batch_run_handle.clone();
        ui.on_batch_reguler_select_dir(move |idx| {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            let dir = {
                let s = st.lock().unwrap();
                s.reguler_dirs
                    .get(idx as usize)
                    .map(|d| d.path.clone())
                    .unwrap_or_default()
            };
            if dir.is_empty() {
                return;
            }
            let cache = load_reguler_cache(&db_c);
            let cache_hit = cache.as_ref().map(|c| c.dir == dir).unwrap_or(false);
            if cache_hit {
                let cache = cache.unwrap();
                {
                    let mut sc = scan.lock().unwrap();
                    sc.dir = dir.clone();
                    sc.all = cache.rows.clone();
                    sc.scanning = false;
                }
                let run_active = handle.is_running();
                push_reguler_to_ui(&ui, &st.lock().unwrap(), &scan.lock().unwrap(), run_active, &batch_c);
                spawn_reguler_incremental(
                    dir,
                    cache,
                    db_c.clone(),
                    st.clone(),
                    scan.clone(),
                    ui_weak.clone(),
                    batch_c.clone(),
                );
            } else {
                {
                    let mut sc = scan.lock().unwrap();
                    sc.dir = dir.clone();
                    sc.all.clear();
                    sc.scanning = true;
                }
                spawn_reguler_scan(&dir, &rxh);
                let run_active = handle.is_running();
                push_reguler_to_ui(&ui, &st.lock().unwrap(), &scan.lock().unwrap(), run_active, &batch_c);
            }
        });
    }
    {
        // -- Folder Reguler: ketik di kolom cari → filter tabel.
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        let scan = reguler_scan_state.clone();
        let handle = batch_run_handle.clone();
        ui.on_batch_reguler_filter_changed(move |text| {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            {
                let mut sc = scan.lock().unwrap();
                sc.filter = text.to_string();
            }
            let run_active = handle.is_running();
            push_reguler_to_ui(&ui, &st.lock().unwrap(), &scan.lock().unwrap(), run_active, &batch_c);
        });
    }
    {
        // -- Folder Reguler: filter bulan (dipersisten ke DB).
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        let db_c = db.clone();
        let scan = reguler_scan_state.clone();
        let handle = batch_run_handle.clone();
        ui.on_batch_reguler_month_picked(move |idx| {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            let month = {
                let sc = scan.lock().unwrap();
                if idx <= 0 {
                    String::new()
                } else {
                    let mut months = vec!["Semua bulan".to_string()];
                    months.extend(batch::reguler_months(&sc.all));
                    months.get(idx as usize).cloned().unwrap_or_default()
                }
            };
            {
                let mut sc = scan.lock().unwrap();
                sc.month = month.clone();
            }
            if let Some(ref dbm) = db_c {
                if let Ok(mut g) = dbm.lock() {
                    let _ = g.set("Batch.FolderReguler.Month", &month, "String");
                }
            }
            let run_active = handle.is_running();
            push_reguler_to_ui(&ui, &st.lock().unwrap(), &scan.lock().unwrap(), run_active, &batch_c);
        });
    }
    {
        // -- Folder Reguler (daftar): klik satu kali pada baris sekolah
        // = pilih sekolah itu dan pindah ke treeview (jalur cepat,
        // seperti pilih direktori di master_creator). Resolusi PILIHAN
        // di load_school_as_source.
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        let db_c = db.clone();
        let scan = reguler_scan_state.clone();
        ui.on_batch_list_open_school(move |idx| {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            let (path, school, dir) = {
                let sc = scan.lock().unwrap();
                let rows = batch::filter_reguler_rows(&sc.all, &sc.filter, &sc.month);
                match rows.get(idx as usize) {
                    Some(r) => (r.path.clone(), r.sekolah.clone(), sc.dir.clone()),
                    None => return,
                }
            };
            load_school_as_source(&ui, &st, &db_c, &batch_c, &path, &school, Some(&dir));
        });
    }
    {
        // -- Treeview: ganti sekolah via dropdown working set.
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        let db_c = db.clone();
        ui.on_batch_working_set_picked(move |idx| {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            let entry = {
                let s = st.lock().unwrap();
                s.working_set.get(idx as usize).cloned()
            };
            let Some(entry) = entry else {
                return;
            };
            load_school_as_source(&ui, &st, &db_c, &batch_c, &entry.path, &entry.name, None);
        });
    }
    {
        // -- Treeview: kembali ke daftar Folder Reguler (SOURCE
        // dikosongkan; working set tetap tersimpan).
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        ui.on_batch_back_to_list(move || {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            {
                let mut s = st.lock().unwrap();
                s.clear();
            }
            push_batch_to_ui(&ui, &st.lock().unwrap(), &batch_c);
            sync_batch_to_open_window(&batch_c, &ui);
        });
    }
    {
        // -- Folder Reguler: tombol Refresh (↻) — paksa scan penuh
        // direktori aktif (abaikan cache), lalu simpan cache baru.
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        let scan = reguler_scan_state.clone();
        let rxh = reguler_scan_rx.clone();
        let handle = batch_run_handle.clone();
        ui.on_batch_reguler_refresh(move || {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            let dir = scan.lock().unwrap().dir.clone();
            if dir.is_empty() {
                ui.set_toast(slint::SharedString::from("Pilih direktori dulu (Kelola)."));
                return;
            }
            {
                let mut sc = scan.lock().unwrap();
                sc.scanning = true;
            }
            spawn_reguler_scan(&dir, &rxh);
            let run_active = handle.is_running();
            push_reguler_to_ui(&ui, &st.lock().unwrap(), &scan.lock().unwrap(), run_active, &batch_c);
        });
    }
    {
        // -- Jalankan script footer (C# ExecuteMaster): SEMUA root SOURCE
        // berurutan via batch_wrapper.py; log mengalir live; Stop = bunuh
        // tree proses. Nama tampil (alias scripts.json) -> nama file. --
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let handle = batch_run_handle.clone();
        let st = batch_state.clone();
        let db_c = db.clone();
        let pl = proc_log.clone();
        let scripts = batch_scripts.clone();
        let task_cell = task_win.clone();
        let task_visible = task_win_visible.clone();
        ui.on_batch_run_script(move |name| {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            let filename = {
                let list = scripts.lock().unwrap();
                let lower = name.to_string().to_ascii_lowercase();
                list.iter()
                    .find(|e| e.display == name.to_string())
                    .or_else(|| {
                        list.iter().find(|e| {
                            e.filename
                                .trim_end_matches(".py")
                                .to_ascii_lowercase()
                                .starts_with(&lower)
                        })
                    })
                    .map(|e| e.filename.clone())
                    .unwrap_or_else(|| {
                        let n = name.to_string();
                        if n.ends_with(".py") {
                            n
                        } else {
                            format!("{}.py", n)
                        }
                    })
            };
            if let Err(msg) = batch_start_run(
                &ui, &handle, &db_c, &st, &pl, &task_cell, &task_visible, &filename, &batch_c,
            ) {
                ui.set_toast(slint::SharedString::from(msg));
            }
        });
    }
    {
        // -- Stop (C# KillProcess): bunuh tree wrapper + python. --
        let ui_weak = ui.as_weak();
        let handle = batch_run_handle.clone();
        let pl = proc_log.clone();
        ui.on_batch_stop(move || {
            handle.stop();
            pl.lock()
                .unwrap()
                .add_log("[WARNING] Proses dihentikan oleh User.".to_string());
            if let Some(ui) = ui_weak.upgrade() {
                push_log_lines(&ui, &pl);
                ui.set_toast(slint::SharedString::from("Proses dihentikan."));
            }
        });
    }
    {
        // -- BUAT MASTER di panel OUTPUT (DESIGN_SYSTEM.md §16.4, fase 2 =
        // buat_master_duplikat.py): BUTUH manifest (Fase 1 dulu); tanpa
        // manifest = toast error dan berhenti. --
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let handle = batch_run_handle.clone();
        let st = batch_state.clone();
        let db_c = db.clone();
        let pl = proc_log.clone();
        let task_cell = task_win.clone();
        let task_visible = task_win_visible.clone();
        ui.on_batch_copy_master(move || {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            let has_manifest = st.lock().unwrap().has_manifest;
            if !has_manifest {
                ui.set_toast(slint::SharedString::from(
                    "Manifest belum ada — jalankan script buat_master dulu.",
                ));
                return;
            }
            if let Err(msg) = batch_start_run(
                &ui,
                &handle,
                &db_c,
                &st,
                &pl,
                &task_cell,
                &task_visible,
                "buat_master_duplikat.py",
                &batch_c,
            ) {
                ui.set_toast(slint::SharedString::from(msg));
            }
        });
    }
    {
        // -- Task Manager (tahap 5): buka/tutup panel tugas terpisah
        // (window OS di samping window utama). Panel tampil otomatis
        // saat run dimulai.
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let st = batch_state.clone();
        let handle = batch_run_handle.clone();
        let db_c = db.clone();
        let pl = proc_log.clone();
        let task_cell = task_win.clone();
        let task_visible = task_win_visible.clone();
        ui.on_batch_open_task_manager(move || {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            if task_visible.get() {
                task_visible.set(false);
                if let Some(w) = task_cell.borrow().as_ref() {
                    let _ = w.hide();
                }
                ui.set_task_manager_open(false);
            } else {
                open_task_manager_window(
                    &ui,
                    &task_cell,
                    &task_visible,
                    &st,
                    &handle,
                    &db_c,
                    &pl,
                    &batch_c,
                );
            }
        });
    }
    {
        // -- Task Manager: batalkan tugas (logika bersama — lihat
        // batch_task_cancel_ui; panel terpisah memanggil yang sama).
        let ui_weak = ui.as_weak();
        let handle = batch_run_handle.clone();
        let st = batch_state.clone();
        let pl = proc_log.clone();
        let task_cell = task_win.clone();
        ui.on_batch_task_cancel(move |id| {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            batch_task_cancel_ui(&ui, &st, &handle, &pl, &task_cell, id.as_str());
        });
    }
    {
        // -- Task Manager: jalankan follow-up tugas yang menunggu save
        // (Fase 2 = buat_master_duplikat.py — logika bersama, lihat
        // batch_task_followup_ui; panel terpisah memanggil yang sama).
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let handle = batch_run_handle.clone();
        let st = batch_state.clone();
        let db_c = db.clone();
        let pl = proc_log.clone();
        let task_cell = task_win.clone();
        let task_visible = task_win_visible.clone();
        ui.on_batch_task_followup(move |id| {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            batch_task_followup_ui(
                &ui,
                &st,
                &handle,
                &db_c,
                &pl,
                &task_cell,
                &task_visible,
                id.as_str(),
                &batch_c,
            );
        });
    }

    {
        // -- Tear-off BATCH (28 Nov 2026): seret grip tab BATCH keluar
        // -> window OS sendiri; tutup window itu -> kembali ke dashboard.
        // Jujur soal batas Slint: pointer event berhenti saat kursor
        // keluar window (tidak ada grab pointer OS), jadi lepas terjadi
        // saat grip digeser > 48px DI DALAM window; window baru terbit
        // dengan titlebar di bawah pointer supaya drag bisa dilanjutkan
        // dari titlebar-nya (lihat DESIGN_SYSTEM.md §16.10).
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let db_c = db.clone();
        ui.on_batch_tear_off(move |x, y| {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            // Posisi pointer (logis, relatif window utama) -> fisik.
            let win = ui.window();
            let pos = win.position();
            let scale = win.scale_factor();
            let px = pos.x + (x * scale) as i32;
            let py = pos.y + (y * scale) as i32;
            // Titlebar window baru di bawah pointer (240px dari kiri,
            // 14px dari atas) supaya drag bisa dilanjutkan; clamp >= 0.
            let nx = (px - (240.0 * scale) as i32).max(0);
            let ny = (py - (14.0 * scale) as i32).max(0);
            detach_batch_window(&ui, &batch_c, &db_c, Some((nx, ny)));
        });
    }
    {
        // -- Tear-off via tombol "Lepas" (drop zone): window di tengah
        // window utama (posisi dihitung dari ukuran window sebenarnya).
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let db_c = db.clone();
        ui.on_batch_tear_off_center(move || {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            detach_batch_window(&ui, &batch_c, &db_c, None);
        });
    }
    {
        // -- Dock: placeholder "Kembalikan ke Dashboard".
        let ui_weak = ui.as_weak();
        let batch_c = batch_win_cell.clone();
        let db_c = db.clone();
        ui.on_batch_dock(move || {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            dock_batch_window(&ui, &batch_c, &db_c);
        });
    }

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
                let trello_state_thread = trello_state_test.clone();
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
                            spawn_trello_sync(ui_w, k, t, base_url, el, rl, ll, false, bar_store_thread, trello_state_thread);
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
        let trello_state_refresh = trello_sync_state.clone();
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
                    trello_state_refresh.clone(),
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
        // Salinan Rc untuk dipakai di dalam closure buka-window (move) —
        // sheets_tx / sheets_op_deadline sendiri masih dipakai handler
        // window utama di bawah.
        let tx_sheet_win = sheets_tx.clone();
        let deadline_sheet_win = sheets_op_deadline.clone();
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
                        // Baris dari jendela halaman -> baris absolut.
                        let abs_row = row as usize + st.page_start;
                        if st.record_edit(abs_row, col as usize, orig.as_str(), val.as_str())
                        {
                            persist_sheet_drafts(&db_edit, &st.drafts);
                        }
                    });
                    let state_save = sheet_state.clone();
                    let db_save = db_clone.clone();
                    let w_weak_save = w.as_weak();
                    let ui_weak_save = ui_weak.clone();
                    let tx_save_w = tx_sheet_win.clone();
                    let deadline_save_w = deadline_sheet_win.clone();
                    w.on_sheet_save(move || {
                        if let Some(w) = w_weak_save.upgrade() {
                            let current_grid = grid_from_sheet_win(&w);
                            let mut st = state_save.borrow_mut();
                            // Grid window = satu halaman: gabungkan dulu
                            // ke grid penuh sebelum guard + commit.
                            let page_start = st.page_start;
                            let col_count = st.column_count;
                            let full_display = sheet_display_rows(&st);
                            let merged = merge_page_into_full(
                                &full_display,
                                current_grid,
                                page_start,
                                col_count,
                            );
                            // Mode live: baca data live terbaru DULU
                            // (thread latar), guard + tulis di tick inbox.
                            if st.live {
                                if st.saving_live {
                                    if let Some(ui) = ui_weak_save.upgrade() {
                                        ui.set_toast(slint::SharedString::from(
                                            "Masih menyimpan ke Google Sheets — tunggu sebentar.",
                                        ));
                                    }
                                    return;
                                }
                                st.saving_live = true;
                                w.set_sheet_status_text(slint::SharedString::from(
                                    "Menyimpan ke Google Sheets…",
                                ));
                                *deadline_save_w.borrow_mut() = Some(Instant::now());
                                request_live_save_fetch(
                                    db_save.clone(),
                                    (*tx_save_w).clone(),
                                    merged,
                                );
                                return;
                            }
                            let saved_msg =
                                "✓ Perubahan tersimpan lokal (SQLite) — belum terhubung ke Google Sheets";
                            match run_sheet_save(&mut st, &db_save, merged) {
                                SheetSaveOutcome::Blocked(msg) => {
                                    w.set_sheet_guard_result(slint::SharedString::from(msg));
                                }
                                SheetSaveOutcome::Saved(new_values) => {
                                    w.set_sheet_guard_result(slint::SharedString::from(""));
                                    push_grid_to_sheet_win(&w, &st, &new_values);
                                    w.set_sheet_status_text(slint::SharedString::from(saved_msg));
                                    // Sinkronkan tampilan embedded.
                                    if let Some(ui) = ui_weak_save.upgrade() {
                                        push_grid_to_main(&ui, &st, &new_values);
                                        ui.set_sheet_guard_result(slint::SharedString::from(""));
                                        ui.set_sheet_has_draft(false);
                                        ui.set_sheet_draft_count(0);
                                        ui.set_sheet_status_text(slint::SharedString::from(saved_msg));
                                    }
                                }
                            }
                        }
                    });
                    let state_refresh = sheet_state.clone();
                    let db_refresh = db_clone.clone();
                    let w_weak_refresh = w.as_weak();
                    let ui_weak_refresh = ui_weak.clone();
                    let tx_refresh_w = tx_sheet_win.clone();
                    let deadline_refresh_w = deadline_sheet_win.clone();
                    w.on_sheet_refresh(move || {
                        if let Some(w) = w_weak_refresh.upgrade() {
                            if SheetsConfig::from_db(&db_refresh).is_live_ready() {
                                {
                                    let st = state_refresh.borrow();
                                    if st.saving_live {
                                        if let Some(ui) = ui_weak_refresh.upgrade() {
                                            ui.set_toast(slint::SharedString::from(
                                                "Masih menyimpan ke Google Sheets — tunggu sebentar.",
                                            ));
                                        }
                                        return;
                                    }
                                }
                                w.set_sheet_status_text(slint::SharedString::from(
                                    "Memuat ulang dari Google Sheets…",
                                ));
                                *deadline_refresh_w.borrow_mut() = Some(Instant::now());
                                request_live_fetch(
                                    db_refresh.clone(),
                                    (*tx_refresh_w).clone(),
                                );
                                return;
                            }
                            let mut st = state_refresh.borrow_mut();
                            let (display, applied, remaining) =
                                run_sheet_refresh(&mut st, &db_refresh);
                            let status = sheet_restore_status(applied, &remaining);
                            push_grid_to_sheet_win(&w, &st, &display);
                            let live_drafts = applied + remaining.len();
                            w.set_sheet_has_draft(live_drafts > 0);
                            w.set_sheet_draft_count(live_drafts as i32);
                            w.set_sheet_status_text(slint::SharedString::from(status.clone()));
                            // Sinkronkan tampilan embedded.
                            if let Some(ui) = ui_weak_refresh.upgrade() {
                                push_grid_to_main(&ui, &st, &display);
                                ui.set_sheet_has_draft(live_drafts > 0);
                                ui.set_sheet_draft_count(live_drafts as i32);
                                ui.set_sheet_status_text(slint::SharedString::from(status));
                            }
                        }
                    });
                    // Pindah halaman jendela grid.
                    let state_page = sheet_state.clone();
                    let ui_weak_page = ui_weak.clone();
                    let sheet_page = sheet_cell.clone();
                    w.on_sheet_prev_page(move || {
                        if let Some(ui) = ui_weak_page.upgrade() {
                            let mut st = state_page.borrow_mut();
                            turn_sheet_page(&mut st, &ui, &sheet_page, -1);
                        }
                    });
                    let state_page2 = sheet_state.clone();
                    let ui_weak_page2 = ui_weak.clone();
                    let sheet_page2 = sheet_cell.clone();
                    w.on_sheet_next_page(move || {
                        if let Some(ui) = ui_weak_page2.upgrade() {
                            let mut st = state_page2.borrow_mut();
                            turn_sheet_page(&mut st, &ui, &sheet_page2, 1);
                        }
                    });
                    // Toast window -> toast window utama (sebelum ini
                    // callback toast SpreadsheetWindow tidak di-wire, jadi
                    // semua toast dari komponen spreadsheet di window ini
                    // hilang — termasuk "Spreadsheet tersimpan").
                    let ui_weak_toast = ui_weak.clone();
                    w.on_toast(move |msg| {
                        if let Some(ui) = ui_weak_toast.upgrade() {
                            ui.set_toast(msg);
                        }
                    });
                    // Fill down (Ctrl+D) — baris teratas seleksi -> baris
                    // lain, kolom fokus saja (C# FillDown row-based).
                    let state_fd = sheet_state.clone();
                    let db_fd = db_clone.clone();
                    let w_weak_fd = w.as_weak();
                    let ui_weak_fd = ui_weak.clone();
                    let sheet_fd = sheet_cell.clone();
                    w.on_sheet_fill_down(move || {
                        if let Some(w) = w_weak_fd.upgrade() {
                            let grid = grid_from_sheet_win(&w);
                            let sel = read_bool_model(w.get_sel_rows());
                            let focus = w.get_focus_col() as usize;
                            let edits = sheet_model::fill_down(&grid, focus, &sel, &state_fd.borrow().columns);
                            let mut st = state_fd.borrow_mut();
                            if let Some(ui) = ui_weak_fd.upgrade() {
                                let n = apply_sheet_edits_and_refresh(
                                    &mut st, &db_fd, &ui, &sheet_fd, &grid, &edits,
                                );
                                let msg = if n == 0 {
                                    "Fill down butuh minimal 2 baris terpilih".to_string()
                                } else {
                                    format!("✓ Fill down: {} sel disalin ke bawah", n)
                                };
                                w.set_sheet_status_text(slint::SharedString::from(msg.clone()));
                                ui.set_toast(slint::SharedString::from(msg));
                            }
                        }
                    });
                    // Hapus isi (Delete/Backspace) — sel terpilih di kolom
                    // fokus saja, sel terkunci dilewati.
                    let state_cl = sheet_state.clone();
                    let db_cl = db_clone.clone();
                    let w_weak_cl = w.as_weak();
                    let ui_weak_cl = ui_weak.clone();
                    let sheet_cl = sheet_cell.clone();
                    w.on_sheet_clear_cells(move || {
                        if let Some(w) = w_weak_cl.upgrade() {
                            let grid = grid_from_sheet_win(&w);
                            let sel = read_bool_model(w.get_sel_rows());
                            let focus = w.get_focus_col() as usize;
                            let edits = sheet_model::clear_cells(&grid, focus, &sel, &state_cl.borrow().columns);
                            let mut st = state_cl.borrow_mut();
                            if let Some(ui) = ui_weak_cl.upgrade() {
                                let n = apply_sheet_edits_and_refresh(
                                    &mut st, &db_cl, &ui, &sheet_cl, &grid, &edits,
                                );
                                let letter = st
                                    .columns
                                    .get(focus)
                                    .map(|c| c.letter.as_str())
                                    .unwrap_or("?");
                                let msg = if n == 0 {
                                    format!(
                                        "Tidak ada sel berisi di kolom {} yang terpilih",
                                        letter
                                    )
                                } else {
                                    format!("✓ {} sel di kolom {} dikosongkan", n, letter)
                                };
                                w.set_sheet_status_text(slint::SharedString::from(msg.clone()));
                                ui.set_toast(slint::SharedString::from(msg));
                            }
                        }
                    });
                    // Auto-sum kolom angka pertama dari SELURUH data
                    // (bukan hanya halaman tampil — sebelumnya hardcode).
                    let state_sum = sheet_state.clone();
                    let w_weak_sum = w.as_weak();
                    let ui_weak_sum = ui_weak.clone();
                    w.on_sheet_auto_sum(move || {
                        if let Some(w) = w_weak_sum.upgrade() {
                            let st = state_sum.borrow();
                            let display = sheet_display_rows(&st);
                            let (col, name) = first_number_column(&st);
                            let total = sheet_model::sum_column(&display, col);
                            let msg = format!("∑ Total {}: {} ({} baris)", name, total, display.len());
                            w.set_sheet_status_text(slint::SharedString::from(msg.clone()));
                            if let Some(ui) = ui_weak_sum.upgrade() {
                                ui.set_toast(slint::SharedString::from(msg));
                            }
                        }
                    });
                    // Kalender tanggal: buka di bulan nilai sel (atau
                    // bulan ini bila kosong/bukan tanggal), geser bulan
                    // dengan hari terpilih yang sama (di-clamp).
                    let w_weak_cal = w.as_weak();
                    w.on_sheet_cal_open(move |val| {
                        if let Some(w) = w_weak_cal.upgrade() {
                            let (y, m, d) = sheet_model::parse_iso_date(val.as_str())
                                .unwrap_or_else(sheet_model::today_ymd);
                            push_cal_state_win(&w, y, m, d);
                        }
                    });
                    let w_weak_nav = w.as_weak();
                    w.on_sheet_cal_nav(move |delta| {
                        if let Some(w) = w_weak_nav.upgrade() {
                            let (ny, nm) = sheet_model::shift_month(
                                w.get_cal_year(),
                                w.get_cal_month() as u32,
                                delta,
                            );
                            let sel = w.get_cal_selected_day() as u32;
                            push_cal_state_win(&w, ny, nm, sel);
                        }
                    });
                    // Tambah baris kosong (maks 6).
                    let state_add = sheet_state.clone();
                    let w_weak_add = w.as_weak();
                    let ui_weak_add = ui_weak.clone();
                    let sheet_add = sheet_cell.clone();
                    w.on_sheet_add_row(move || {
                        if let Some(w) = w_weak_add.upgrade() {
                            let grid = grid_from_sheet_win(&w);
                            let mut st = state_add.borrow_mut();
                            if let Some(ui) = ui_weak_add.upgrade() {
                                let n = add_sheet_row(&mut st, &ui, &sheet_add, grid);
                                ui.set_toast(slint::SharedString::from(format!(
                                    "Baris {} ditambahkan",
                                    n
                                )));
                            }
                        }
                    });
                    // Type-to-edit: satu karakter boleh memulai edit bila
                    // bukan karakter kontrol (Enter/Tab/Esc dsb).
                    w.on_sheet_is_typeahead_char(move |text| {
                        text.chars().next().map(|c| !c.is_control()).unwrap_or(false)
                    });
                    // Hit-test kolom + seleksi baris (Rust — alasan sama
                    // seperti tampilan embedded).
                    {
                        let state = sheet_state.clone();
                        w.on_sheet_hit_col(move |x| {
                            let st = state.borrow();
                            sheet_hit_col(x, &st.columns)
                        });
                    }
                    {
                        let w_weak = w.as_weak();
                        w.on_sheet_select_rows(move |r, _c, mode, anchor| {
                            if let Some(w) = w_weak.upgrade() {
                                let cur = read_bool_model(w.get_sel_rows());
                                let sel = sheet_select_rows(
                                    &cur,
                                    r.max(0) as usize,
                                    mode,
                                    anchor.max(0) as usize,
                                );
                                return sheet_model_bool(sel);
                            }
                            sheet_model_bool(Vec::new())
                        });
                    }
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
                    push_grid_to_sheet_win(w, &st, &display);
                    w.set_sheet_has_draft(!st.drafts.is_empty());
                    w.set_sheet_draft_count(st.drafts.len() as i32);
                    if !st.drafts.is_empty() {
                        w.set_sheet_status_text(slint::SharedString::from(format!(
                            "Perubahan belum disimpan ({} sel)",
                            st.drafts.len()
                        )));
                    } else {
                        // Status jujur (bukan default .slint): live ->
                        // terhubung, offline -> data lokal + belum
                        // terhubung.
                        let status = if st.live {
                            live_sheet_status(&st, 0, &[])
                        } else {
                            offline_sheet_status(&st)
                        };
                        w.set_sheet_status_text(slint::SharedString::from(status));
                    }
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
            // Baris dari jendela halaman -> baris absolut.
            let abs_row = row as usize + st.page_start;
            if st.record_edit(abs_row, col as usize, orig.as_str(), val.as_str()) {
                persist_sheet_drafts(&db_clone, &st.drafts);
            }
        });
    }
    {
        let state = sheet_state.clone();
        let db_clone = db.clone();
        let ui_weak = ui.as_weak();
        let sheet_cell = sheet_win_cell.clone();
        let tx_save = sheets_tx.clone();
        let deadline_save = sheets_op_deadline.clone();
        ui.on_sheet_save(move || {
            if let Some(ui) = ui_weak.upgrade() {
                let current_grid = grid_from_main(&ui);
                let mut st = state.borrow_mut();
                // Grid window = satu halaman: gabungkan dulu ke grid
                // penuh sebelum guard + commit.
                let page_start = st.page_start;
                let col_count = st.column_count;
                let full_display = sheet_display_rows(&st);
                let merged = merge_page_into_full(
                    &full_display,
                    current_grid,
                    page_start,
                    col_count,
                );
                // Mode live: ANTI-MENCLOK — baca data live terbaru
                // DULU (thread latar), guard + tulis di tick inbox.
                // Simpan lokal tetap sinkron seperti sebelumnya.
                if st.live {
                    if st.saving_live {
                        ui.set_toast(slint::SharedString::from(
                            "Masih menyimpan ke Google Sheets — tunggu sebentar.",
                        ));
                        return;
                    }
                    st.saving_live = true;
                    ui.set_sheet_status_text(slint::SharedString::from(
                        "Menyimpan ke Google Sheets…",
                    ));
                    *deadline_save.borrow_mut() = Some(Instant::now());
                    request_live_save_fetch(
                        db_clone.clone(),
                        (*tx_save).clone(),
                        merged,
                    );
                    return;
                }
                let saved_msg =
                    "✓ Perubahan tersimpan lokal (SQLite) — belum terhubung ke Google Sheets";
                match run_sheet_save(&mut st, &db_clone, merged) {
                    SheetSaveOutcome::Blocked(msg) => {
                        ui.set_sheet_guard_result(slint::SharedString::from(msg));
                    }
                    SheetSaveOutcome::Saved(new_values) => {
                        ui.set_sheet_guard_result(slint::SharedString::from(""));
                        push_grid_to_main(&ui, &st, &new_values);
                        ui.set_sheet_status_text(slint::SharedString::from(saved_msg));
                        if let Some(w) = sheet_cell.borrow().as_ref() {
                            push_grid_to_sheet_win(w, &st, &new_values);
                            w.set_sheet_guard_result(slint::SharedString::from(""));
                            w.set_sheet_has_draft(false);
                            w.set_sheet_draft_count(0);
                            w.set_sheet_status_text(slint::SharedString::from(saved_msg));
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
        let tx_refresh = sheets_tx.clone();
        let deadline_refresh = sheets_op_deadline.clone();
        ui.on_sheet_refresh(move || {
            if let Some(ui) = ui_weak.upgrade() {
                // Bila konfigurasi live lengkap: ambil data live terbaru
                // di thread latar (hasil diterapkan tick inbox) — snapshot
                // lokal saja tidak cukup (ANTI-MENCLOK). Tanpa konfigurasi:
                // muat ulang lokal seperti sebelumnya.
                if SheetsConfig::from_db(&db_clone).is_live_ready() {
                    {
                        let st = state.borrow();
                        if st.saving_live {
                            ui.set_toast(slint::SharedString::from(
                                "Masih menyimpan ke Google Sheets — tunggu sebentar.",
                            ));
                            return;
                        }
                    }
                    ui.set_sheet_status_text(slint::SharedString::from(
                        "Memuat ulang dari Google Sheets…",
                    ));
                    *deadline_refresh.borrow_mut() = Some(Instant::now());
                    request_live_fetch(db_clone.clone(), (*tx_refresh).clone());
                    return;
                }
                let mut st = state.borrow_mut();
                let (display, applied, remaining) = run_sheet_refresh(&mut st, &db_clone);
                let status = sheet_restore_status(applied, &remaining);
                push_grid_to_main(&ui, &st, &display);
                let live_drafts = applied + remaining.len();
                ui.set_sheet_has_draft(live_drafts > 0);
                ui.set_sheet_draft_count(live_drafts as i32);
                ui.set_sheet_status_text(slint::SharedString::from(status.clone()));
                if let Some(w) = sheet_cell.borrow().as_ref() {
                    push_grid_to_sheet_win(w, &st, &display);
                    w.set_sheet_has_draft(live_drafts > 0);
                    w.set_sheet_draft_count(live_drafts as i32);
                    w.set_sheet_status_text(slint::SharedString::from(status));
                }
            }
        });
    }

    // ---- Spreadsheet: pindah halaman jendela grid (tampilan embedded).
    {
        let state = sheet_state.clone();
        let ui_weak = ui.as_weak();
        let sheet_cell = sheet_win_cell.clone();
        ui.on_sheet_prev_page(move || {
            if let Some(ui) = ui_weak.upgrade() {
                let mut st = state.borrow_mut();
                turn_sheet_page(&mut st, &ui, &sheet_cell, -1);
            }
        });
    }
    {
        let state = sheet_state.clone();
        let ui_weak = ui.as_weak();
        let sheet_cell = sheet_win_cell.clone();
        ui.on_sheet_next_page(move || {
            if let Some(ui) = ui_weak.upgrade() {
                let mut st = state.borrow_mut();
                turn_sheet_page(&mut st, &ui, &sheet_cell, 1);
            }
        });
    }

    // ---- Spreadsheet beneran: fill-down / hapus / auto-sum / kalender,
    // handler tampilan embedded (sinkron ke SpreadsheetWindow bila terbuka).
    {
        let state = sheet_state.clone();
        let db_clone = db.clone();
        let ui_weak = ui.as_weak();
        let sheet_cell = sheet_win_cell.clone();
        ui.on_sheet_fill_down(move || {
            if let Some(ui) = ui_weak.upgrade() {
                let grid = grid_from_main(&ui);
                let sel = read_bool_model(ui.get_sheet_sel_rows());
                let focus = ui.get_sheet_focus_col() as usize;
                let edits = sheet_model::fill_down(&grid, focus, &sel, &state.borrow().columns);
                let mut st = state.borrow_mut();
                let n = apply_sheet_edits_and_refresh(
                    &mut st, &db_clone, &ui, &sheet_cell, &grid, &edits,
                );
                let msg = if n == 0 {
                    "Fill down butuh minimal 2 baris terpilih".to_string()
                } else {
                    format!("✓ Fill down: {} sel disalin ke bawah", n)
                };
                ui.set_sheet_status_text(slint::SharedString::from(msg.clone()));
                ui.set_toast(slint::SharedString::from(msg));
            }
        });
    }
    {
        let state = sheet_state.clone();
        let db_clone = db.clone();
        let ui_weak = ui.as_weak();
        let sheet_cell = sheet_win_cell.clone();
        ui.on_sheet_clear_cells(move || {
            if let Some(ui) = ui_weak.upgrade() {
                let grid = grid_from_main(&ui);
                let sel = read_bool_model(ui.get_sheet_sel_rows());
                let focus = ui.get_sheet_focus_col() as usize;
                let edits = sheet_model::clear_cells(&grid, focus, &sel, &state.borrow().columns);
                let mut st = state.borrow_mut();
                let n = apply_sheet_edits_and_refresh(
                    &mut st, &db_clone, &ui, &sheet_cell, &grid, &edits,
                );
                let letter = st
                    .columns
                    .get(focus)
                    .map(|c| c.letter.as_str())
                    .unwrap_or("?");
                let msg = if n == 0 {
                    format!("Tidak ada sel berisi di kolom {} yang terpilih", letter)
                } else {
                    format!("✓ {} sel di kolom {} dikosongkan", n, letter)
                };
                ui.set_sheet_status_text(slint::SharedString::from(msg.clone()));
                ui.set_toast(slint::SharedString::from(msg));
            }
        });
    }
    {
        let state = sheet_state.clone();
        let ui_weak = ui.as_weak();
        ui.on_sheet_auto_sum(move || {
            if let Some(ui) = ui_weak.upgrade() {
                let st = state.borrow();
                let display = sheet_display_rows(&st);
                let (col, name) = first_number_column(&st);
                let total = sheet_model::sum_column(&display, col);
                let msg = format!("∑ Total {}: {} ({} baris)", name, total, display.len());
                ui.set_sheet_status_text(slint::SharedString::from(msg.clone()));
                ui.set_toast(slint::SharedString::from(msg));
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        ui.on_sheet_cal_open(move |val| {
            if let Some(ui) = ui_weak.upgrade() {
                let (y, m, d) = sheet_model::parse_iso_date(val.as_str())
                    .unwrap_or_else(sheet_model::today_ymd);
                push_cal_state_main(&ui, y, m, d);
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        ui.on_sheet_cal_nav(move |delta| {
            if let Some(ui) = ui_weak.upgrade() {
                let (ny, nm) = sheet_model::shift_month(
                    ui.get_sheet_cal_year(),
                    ui.get_sheet_cal_month() as u32,
                    delta,
                );
                let sel = ui.get_sheet_cal_selected_day() as u32;
                push_cal_state_main(&ui, ny, nm, sel);
            }
        });
    }
    {
        let state = sheet_state.clone();
        let ui_weak = ui.as_weak();
        let sheet_cell = sheet_win_cell.clone();
        ui.on_sheet_add_row(move || {
            if let Some(ui) = ui_weak.upgrade() {
                let grid = grid_from_main(&ui);
                let mut st = state.borrow_mut();
                let n = add_sheet_row(&mut st, &ui, &sheet_cell, grid);
                ui.set_toast(slint::SharedString::from(format!(
                    "Baris {} ditambahkan",
                    n
                )));
            }
        });
    }
    {
        ui.on_sheet_is_typeahead_char(move |text| {
            text.chars().next().map(|c| !c.is_control()).unwrap_or(false)
        });
    }

    // ---- Spreadsheet: hit-test kolom + seleksi baris (dihitung Rust —
    // Slint 1.19 tidak punya `for` di dalam fungsi, jadi .slint hanya
    // memanggil callback ini).
    {
        let state = sheet_state.clone();
        ui.on_sheet_hit_col(move |x| {
            let st = state.borrow();
            sheet_hit_col(x, &st.columns)
        });
    }
    {
        let ui_weak = ui.as_weak();
        ui.on_sheet_select_rows(move |r, _c, mode, anchor| {
            if let Some(ui) = ui_weak.upgrade() {
                let cur = read_bool_model(ui.get_sheet_sel_rows());
                let sel = sheet_select_rows(
                    &cur,
                    r.max(0) as usize,
                    mode,
                    anchor.max(0) as usize,
                );
                return sheet_model_bool(sel);
            }
            sheet_model_bool(Vec::new())
        });
    }

    {
        let card_cell = card_win_cell.clone();
        // Sama seperti sheet: buka-ulang setelah tutup selalu buat
        // window baru (hide->show reuse tidak andal di Linux).
        let card_visible = std::rc::Rc::new(std::cell::Cell::new(false));
        // Untuk segmen panah split button Comment di detail CardList:
        // buka CommentWindow (state dibagi — lihat ensure/show di atas).
        let comment_cell = comment_win_cell.clone();
        let comment_vis = comment_visible.clone();
        let comment_src = comment_source.clone();
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
                    // Segmen panah split button Comment di detail
                    // kartu: buka komentar kartu terpilih di
                    // CommentWindow (C# OnOpenCommentWindowClicked).
                    let comment_cell_cw = comment_cell.clone();
                    let comment_vis_cw = comment_vis.clone();
                    let comment_src_cw = comment_src.clone();
                    let ui_weak_cw = ui_weak.clone();
                    let card_cell_cw = card_cell.clone();
                    let card_weak_cw = w.as_weak();
                    w.on_open_comment_window(move || {
                        let Some(card) = card_weak_cw.upgrade() else {
                            return;
                        };
                        let Some(ui) = ui_weak_cw.upgrade() else {
                            return;
                        };
                        ensure_comment_window(
                            &comment_cell_cw,
                            &comment_vis_cw,
                            &comment_src_cw,
                            &ui_weak_cw,
                            &card_cell_cw,
                        );
                        comment_src_cw.set(1);
                        let idx = card.get_selected_idx() as usize;
                        let titles = card.get_card_titles();
                        let title = if idx < titles.row_count() {
                            titles.row_data(idx).unwrap_or_default()
                        } else {
                            slint::SharedString::from("")
                        };
                        let dues = card.get_card_due();
                        let date = if idx < dues.row_count() {
                            dues.row_data(idx).unwrap_or_default()
                        } else {
                            slint::SharedString::from("")
                        };
                        show_comment_window(
                            &comment_cell_cw,
                            &comment_vis_cw,
                            &ui,
                            title,
                            date,
                            ui.get_app_user_name(),
                            ui.get_app_user_initial(),
                            card.get_detail_added_count(),
                            card.get_detail_draft_1(),
                            card.get_detail_draft_2(),
                            card.get_detail_draft_3(),
                        );
                    });
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

    // ---- CommentWindow: komentar kartu di window OS terpisah ----
    // Segmen panah (▸) split button Comment di detail kartu (C#
    // OnOpenCommentWindowClicked — muat komentar kartu yang diklik,
    // buka window, tutup panel Comments inline).
    {
        let comment_cell = comment_win_cell.clone();
        let comment_vis = comment_visible.clone();
        let comment_src = comment_source.clone();
        let card_cell = card_win_cell.clone();
        let ui_weak = ui.as_weak();
        ui.on_open_comment_window(move || {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            ensure_comment_window(
                &comment_cell,
                &comment_vis,
                &comment_src,
                &ui_weak,
                &card_cell,
            );
            comment_src.set(0);
            let title = ui.get_selected_card();
            let idx = ui.get_selected_index() as usize;
            let date = if ui.get_selected_is_manual() {
                slint::SharedString::from("—")
            } else {
                let dues = ui.get_list_due();
                if idx < dues.row_count() {
                    dues.row_data(idx).unwrap_or_default()
                } else {
                    slint::SharedString::from("")
                }
            };
            show_comment_window(
                &comment_cell,
                &comment_vis,
                &ui,
                title,
                date,
                ui.get_app_user_name(),
                ui.get_app_user_initial(),
                ui.get_detail_added_count(),
                ui.get_detail_draft_1(),
                ui.get_detail_draft_2(),
                ui.get_detail_draft_3(),
            );
            // C#: tutup panel Comments inline saat window dibuka.
            if ui.get_detail_panel() == "comments" {
                ui.set_detail_panel(slint::SharedString::from(""));
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
            let auto_hide = ui.get_app_notch_auto_hide();
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
                    let _ = guard.set(
                        "Settings.Notch.AutoHide",
                        if auto_hide { "true" } else { "false" },
                        "Bool",
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

    // Gaya toggle on/off berubah di Settings (Appearance): simpan ke
    // DB; bila ToolboxWindow sedang tampil, dorong langsung (live
    // tanpa restart). Toggle di window utama ikut via binding
    // app-toggle-style — tidak perlu dorongan Rust.
    {
        let toolbox_cell = toolbox_win_cell.clone();
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        ui.on_toggle_settings_changed(move || {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            let style = theme::valid_toggle_style(&ui.get_app_toggle_style());
            if let Some(ref db_mutex) = db_clone {
                if let Ok(mut guard) = db_mutex.lock() {
                    let _ = guard.set("Settings.ToggleStyle", &style, "String");
                }
            }
            if let Some(w) = toolbox_cell.borrow().as_ref() {
                push_toggle_style_to_toolbox(w, &ui);
            }
        });
    }

    // Paket ikon berubah di Settings (Appearance): validasi +
    // simpan ke DB; dorong path ke Design window utama + semua
    // window sekunder yang sedang tampil (live tanpa restart).
    {
        let bar_cell = bar_win_cell.clone();
        let radial_cell = radial_win_cell.clone();
        let task_cell = task_win.clone();
        let sheet_cell = sheet_win_cell.clone();
        let toolbox_cell = toolbox_win_cell.clone();
        let card_cell = card_win_cell.clone();
        let comment_cell = comment_win_cell.clone();
        let batch_cell = batch_win_cell.clone();
        let doc_cell = doc_win_cell.clone();
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        ui.on_icon_pack_settings_changed(move || {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            let pack = icons::valid_icon_pack(&ui.get_app_icon_pack());
            ui.set_app_icon_pack(slint::SharedString::from(pack.as_str()));
            if let Some(ref db_mutex) = db_clone {
                if let Ok(mut guard) = db_mutex.lock() {
                    let _ = guard.set("Settings.IconPack", &pack, "String");
                }
            }
            theme::push_icons(&Design::get(&ui), &pack);
            sync_bar_theme(&ui, &bar_cell);
            sync_radial_theme(&ui, &radial_cell);
            sync_task_theme(&ui, &task_cell);
            sync_chrome_to_windows(&ui, &sheet_cell, &toolbox_cell, &card_cell, &comment_cell, &batch_cell, &doc_cell);
        });
    }

    {
        let ui_weak = ui.as_weak();
        let db_clone = db.clone();
        ui.on_sidebar_collapsed_changed(move || {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            let collapsed = ui.get_app_settings_sidebar_collapsed();
            if let Some(ref db_mutex) = db_clone {
                if let Ok(mut guard) = db_mutex.lock() {
                    let _ = guard.set(
                        "Settings.SidebarCollapsed",
                        if collapsed { "true" } else { "false" },
                        "Bool",
                    );
                }
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
    // Tab DOC di Panel Samping + DocWindow terlepas di-wiring lewat
    // handler `on_doc_*` (lihat blok handler DOC di bawah, dekat
    // handler doc-add/doc-open/doc-remove) — DocManager asli dari
    // startup (`doc_mgr`), BUKAN manajer default sesaat seperti dulu
    // di sini (blok lama itu mati: add_tab/close_tab/write pada
    // manajer yang langsung dibuang).

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

    // ---- Explorer nyata (src/explorer.rs) ----
    // State bersama Explorer dibuat di awal main() (setelah bar_store);
    // di sini hanya inisialisasi show-hidden dari DB.
    {
        let mut st = explorer_state.lock().unwrap();
        st.show_hidden = ex_db_get(&db, "Configs.Explorer.ShowHiddenFiles") == "true";
    }

    // ---- Search rekursif (substring case-insensitive, `*`/`?` literal) ----
    // Jalan di thread latar; generasi membuang hasil basi (debounce 300ms
    // di Slint me-restart search — hasil query lama tidak boleh menimpa).
    // Urutan hasil: folder dulu, lalu shortcut, lalu file (tiap kelompok
    // name-asc). Cap 5000 (spec §2.4).
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        ui.on_ex_search(move |query, path| {
            let my_gen = {
                let mut st = ex_state.lock().unwrap();
                st.search_gen += 1;
                st.search_gen
            };
            let ui_w2 = ui_weak.clone();
            let ex2 = ex_state.clone();
            let root = PathBuf::from(path.as_str());
            let q = query.to_string();
            let show_hidden = ex_state.lock().map(|s| s.show_hidden).unwrap_or(false);
            std::thread::spawn(move || {
                let (hits, truncated) = explorer::search_recursive(&root, &q, show_hidden);
                let ui_w3 = ui_w2.clone();
                let _ = slint::invoke_from_event_loop(move || {
                    let Some(ui) = ui_w3.upgrade() else { return; };
                    let Ok(mut st) = ex2.lock() else { return; };
                    if st.search_gen != my_gen {
                        return; // hasil basi — query baru sudah jalan
                    }
                    st.searching = false;
                    let names: Vec<slint::SharedString> = hits
                        .iter()
                        .map(|h| slint::SharedString::from(h.name.as_str()))
                        .collect();
                    let kinds: Vec<slint::SharedString> = hits
                        .iter()
                        .map(|h| slint::SharedString::from(h.kind.as_str()))
                        .collect();
                    let exts: Vec<slint::SharedString> = hits
                        .iter()
                        .map(|h| slint::SharedString::from(h.ext.as_str()))
                        .collect();
                    let bases: Vec<slint::SharedString> = hits
                        .iter()
                        .map(|h| slint::SharedString::from(h.base.as_str()))
                        .collect();
                    let sizes: Vec<slint::SharedString> = hits
                        .iter()
                        .map(|h| slint::SharedString::from(h.size_display.as_str()))
                        .collect();
                    let dates: Vec<slint::SharedString> = hits
                        .iter()
                        .map(|h| slint::SharedString::from(h.date_display.as_str()))
                        .collect();
                    let previews: Vec<slint::SharedString> =
                        hits.iter().map(|_| slint::SharedString::new()).collect();
                    let locs: Vec<slint::SharedString> = hits
                        .iter()
                        .map(|h| slint::SharedString::from(h.loc.as_str()))
                        .collect();
                    let paths: Vec<slint::SharedString> = hits
                        .iter()
                        .map(|h| slint::SharedString::from(h.path.to_string_lossy().into_owned()))
                        .collect();
                    let half = (names.len() + 1) / 2;
                    ui.set_ex_sr_names(slint::ModelRc::new(slint::VecModel::from_slice(&names)));
                    ui.set_ex_sr_kinds(slint::ModelRc::new(slint::VecModel::from_slice(&kinds)));
                    ui.set_ex_sr_exts(slint::ModelRc::new(slint::VecModel::from_slice(&exts)));
                    ui.set_ex_sr_bases(slint::ModelRc::new(slint::VecModel::from_slice(&bases)));
                    ui.set_ex_sr_sizes(slint::ModelRc::new(slint::VecModel::from_slice(&sizes)));
                    ui.set_ex_sr_dates(slint::ModelRc::new(slint::VecModel::from_slice(&dates)));
                    ui.set_ex_sr_previews(slint::ModelRc::new(slint::VecModel::from_slice(
                        &previews,
                    )));
                    ui.set_ex_sr_locs(slint::ModelRc::new(slint::VecModel::from_slice(&locs)));
                    ui.set_ex_sr_paths(slint::ModelRc::new(slint::VecModel::from_slice(&paths)));
                    ui.set_ex_sr_half(half as i32);
                    ui.set_ex_sr_truncated(truncated);
                });
            });
        });
    }

    // ---- Type-ahead: indeks kanonis pertama yang diawali buffer ----
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        ui.on_ex_typeahead(move |path, buffer| {
            let Some(ui) = ui_weak.upgrade() else { return; };
            let st = ex_state.lock().unwrap();
            let b = buffer.to_lowercase();
            let p = PathBuf::from(path.as_str());
            let items = if p == st.cur_path {
                st.items.clone()
            } else {
                explorer::read_folder(&p, st.show_hidden)
            };
            let mut idx: i32 = -1;
            for (i, it) in items.iter().enumerate() {
                if it.name.to_lowercase().starts_with(&b) {
                    idx = i as i32;
                    break;
                }
            }
            ui.set_ex_ta_index(idx);
        });
    }

    // ---- Navigasi: load folder / parent / base-name / is-dir / index ----
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        let db_c = db.clone();
        // Clone untuk handler nav-up panel (closure di bawah me-move
        // aslinya ke on_ex_load_folder).
        let ui_weak_up = ui_weak.clone();
        let ex_state_up = ex_state.clone();
        let db_c_up = db_c.clone();
        ui.on_ex_load_folder(move |path| {
            let Some(ui) = ui_weak.upgrade() else {
                return false;
            };
            let mut st = ex_state.lock().unwrap();
            ex_load_folder(&ui, &mut st, &db_c, Path::new(path.as_str()))
        });
        // Naik satu tingkat dari folder saat ini (panel compact) —
        // tidak boleh di atas root Explorer (C# Up).
        ui.on_ex_nav_up(move || {
            let Some(ui) = ui_weak_up.upgrade() else {
                return;
            };
            let mut st = ex_state_up.lock().unwrap();
            let cur = st.cur_path.clone();
            let root = st.root_path.clone();
            if cur.as_os_str().is_empty() || cur == root {
                return;
            }
            if let Some(par) = cur.parent() {
                if !par.as_os_str().is_empty() {
                    let par = par.to_path_buf();
                    ex_load_folder(&ui, &mut st, &db_c_up, &par);
                }
            }
        });
    }
    ui.on_ex_parent(move |path| {
        let p = Path::new(path.as_str());
        match p.parent() {
            Some(par) if !par.as_os_str().is_empty() => {
                slint::SharedString::from(par.to_string_lossy().into_owned())
            }
            _ => slint::SharedString::new(),
        }
    });
    ui.on_ex_base_name(move |path| {
        let p = Path::new(path.as_str());
        match p.file_name() {
            Some(n) => slint::SharedString::from(n.to_string_lossy().into_owned()),
            None => slint::SharedString::from(path.as_str()),
        }
    });
    ui.on_ex_is_dir(move |path| Path::new(path.as_str()).is_dir());
    ui.on_ex_is_file(move |path| Path::new(path.as_str()).is_file());
    {
        let ex_state = explorer_state.clone();
        ui.on_ex_index_of(move |name| {
            let st = ex_state.lock().unwrap();
            for (i, it) in st.items.iter().enumerate() {
                if it.name == name.as_str() {
                    return i as i32;
                }
            }
            -1
        });
    }
    {
        let ex_state = explorer_state.clone();
        ui.on_ex_name_exists(move |name| {
            let st = ex_state.lock().unwrap();
            std::fs::symlink_metadata(st.cur_path.join(name.as_str())).is_ok()
        });
    }
    {
        let ui_weak = ui.as_weak();
        ui.on_ex_preview(move |path| {
            let Some(ui) = ui_weak.upgrade() else { return; };
            let text = std::fs::read_to_string(path.as_str())
                .map(|s| s.chars().take(4000).collect::<String>())
                .unwrap_or_default();
            ui.set_ex_preview_text(slint::SharedString::from(text));
        });
    }

    // ---- Buat folder / file baru ----
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        let db_c = db.clone();
        ui.on_ex_create_folder(move |dir, name| {
            let Some(ui) = ui_weak.upgrade() else {
                return false;
            };
            let name = name.trim().to_string();
            if name.is_empty() {
                ui.set_toast(slint::SharedString::from("Nama tidak boleh kosong."));
                return false;
            }
            if ex_bad_name(&name) {
                ui.set_toast(slint::SharedString::from(
                    "Nama tidak boleh mengandung / \\ : * ? \" < > |",
                ));
                return false;
            }
            let target = Path::new(dir.as_str()).join(&name);
            if std::fs::symlink_metadata(&target).is_ok() {
                ui.set_toast(slint::SharedString::from(format!("Nama sudah ada: {}", name)));
                return false;
            }
            match std::fs::create_dir(&target) {
                Ok(()) => {
                    let mut st = ex_state.lock().unwrap();
                    st.push_undo(explorer::UndoEntry {
                        kind: "new".to_string(),
                        name: format!("Buat folder {}", name),
                        pairs: vec![(PathBuf::new(), target)],
                    });
                    st.last_own_change = Some(Instant::now());
                    ex_reload_cur(&ui, &mut st, &db_c);
                    ui.set_toast(slint::SharedString::from(format!("Folder dibuat: {}", name)));
                    true
                }
                Err(e) => {
                    ui.set_toast(slint::SharedString::from(format!(
                        "Gagal membuat folder: {}",
                        e
                    )));
                    false
                }
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        let db_c = db.clone();
        ui.on_ex_create_file(move |dir, name| {
            let Some(ui) = ui_weak.upgrade() else {
                return false;
            };
            let name = name.trim().to_string();
            if name.is_empty() {
                ui.set_toast(slint::SharedString::from("Nama tidak boleh kosong."));
                return false;
            }
            if ex_bad_name(&name) {
                ui.set_toast(slint::SharedString::from(
                    "Nama tidak boleh mengandung / \\ : * ? \" < > |",
                ));
                return false;
            }
            let target = Path::new(dir.as_str()).join(&name);
            if std::fs::symlink_metadata(&target).is_ok() {
                ui.set_toast(slint::SharedString::from(format!("Nama sudah ada: {}", name)));
                return false;
            }
            match std::fs::File::create(&target) {
                Ok(_) => {
                    let mut st = ex_state.lock().unwrap();
                    st.push_undo(explorer::UndoEntry {
                        kind: "new".to_string(),
                        name: format!("Buat file {}", name),
                        pairs: vec![(PathBuf::new(), target)],
                    });
                    st.last_own_change = Some(Instant::now());
                    ex_reload_cur(&ui, &mut st, &db_c);
                    ui.set_toast(slint::SharedString::from(format!("File dibuat: {}", name)));
                    true
                }
                Err(e) => {
                    ui.set_toast(slint::SharedString::from(format!("Gagal membuat file: {}", e)));
                    false
                }
            }
        });
    }

    // ---- Rename ----
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        let db_c = db.clone();
        ui.on_ex_rename(move |old_path, new_name| {
            let Some(ui) = ui_weak.upgrade() else {
                return false;
            };
            let old = PathBuf::from(old_path.as_str());
            let new_name = new_name.trim().to_string();
            let old_name = ex_display_name(&old);
            if new_name.is_empty() {
                ui.set_toast(slint::SharedString::from("Nama tidak boleh kosong."));
                return false;
            }
            if ex_bad_name(&new_name) {
                ui.set_toast(slint::SharedString::from(
                    "Nama tidak boleh mengandung / \\ : * ? \" < > |",
                ));
                return false;
            }
            let new = old.with_file_name(&new_name);
            if std::fs::symlink_metadata(&new).is_ok() {
                ui.set_toast(slint::SharedString::from(format!(
                    "Nama sudah ada: {}",
                    new_name
                )));
                return false;
            }
            match std::fs::rename(&old, &new) {
                Ok(()) => {
                    let mut st = ex_state.lock().unwrap();
                    st.push_undo(explorer::UndoEntry {
                        kind: "rename".to_string(),
                        name: format!("Rename {} → {}", old_name, new_name),
                        pairs: vec![(old, new)],
                    });
                    st.last_own_change = Some(Instant::now());
                    ex_reload_cur(&ui, &mut st, &db_c);
                    ui.set_toast(slint::SharedString::from(format!(
                        "Rename: {} → {}",
                        old_name, new_name
                    )));
                    true
                }
                Err(e) => {
                    ui.set_toast(slint::SharedString::from(format!("Gagal rename: {}", e)));
                    false
                }
            }
        });
    }

    // ---- Hapus (Recycle Bin sinkron; permanen via Task Monitor) ----
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        let db_c = db.clone();
        ui.on_ex_delete(move |joined, permanent| {
            let Some(ui) = ui_weak.upgrade() else {
                return false;
            };
            let paths = ex_split_joined(joined.as_str());
            if paths.is_empty() {
                ui.set_toast(slint::SharedString::from("Pilih item dulu."));
                return false;
            }
            if permanent {
                let total: u64 = paths.iter().map(|p| explorer::dir_size(p)).sum();
                let name = if paths.len() == 1 {
                    ex_display_name(&paths[0])
                } else {
                    format!("{} item", paths.len())
                };
                let ok = ex_spawn_task(
                    &ex_state,
                    "delete",
                    name,
                    paths,
                    PathBuf::new(),
                    false,
                    total,
                );
                if ok {
                    // Dorong TM langsung: task bisa selesai sebelum tick
                    // poll pertama (300ms), kalau tidak TM tak pernah muncul.
                    if let Ok(st) = ex_state.lock() {
                        ex_push_tasks(&ui, &st);
                    }
                    ui.set_toast(slint::SharedString::from(
                        "Menghapus permanen (lihat Task Monitor)…",
                    ));
                    true
                } else {
                    ui.set_toast(slint::SharedString::from("Task Monitor penuh (50 tugas)."));
                    false
                }
            } else {
                let mut done = 0usize;
                let mut failed = String::new();
                for p in &paths {
                    match platform::move_to_trash(&p.to_string_lossy()) {
                        Ok(()) => done += 1,
                        Err(e) => failed = e,
                    }
                }
                let mut st = ex_state.lock().unwrap();
                st.push_undo(explorer::UndoEntry {
                    kind: "recycle".to_string(),
                    name: format!("Hapus {} item", done),
                    pairs: vec![],
                });
                st.last_own_change = Some(Instant::now());
                ex_reload_cur(&ui, &mut st, &db_c);
                if done == paths.len() {
                    ui.set_toast(slint::SharedString::from(format!(
                        "Dipindahkan ke Recycle Bin: {} item.",
                        done
                    )));
                    true
                } else {
                    ui.set_toast(slint::SharedString::from(format!(
                        "Sebagian gagal dipindah ke Recycle Bin ({} dari {}): {}",
                        done,
                        paths.len(),
                        failed
                    )));
                    done > 0
                }
            }
        });
    }

    // ---- Duplikat (1–100 salinan, "x - Copy" berurutan) ----
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        ui.on_ex_duplicate(move |path, count| {
            let Some(ui) = ui_weak.upgrade() else {
                return false;
            };
            let src = PathBuf::from(path.as_str());
            let count = (count.max(1).min(100)) as usize;
            let Some(parent) = src.parent().map(|p| p.to_path_buf()) else {
                return false;
            };
            let name = ex_display_name(&src);
            let total = explorer::dir_size(&src) * count as u64;
            // srcs = src diulang N kali -> worker copy membuat N salinan
            // berurutan dengan dedup ("x - Copy", "x - Copy (2)", ...).
            let srcs = vec![src.clone(); count];
            let ok = ex_spawn_task(
                &ex_state,
                "copy",
                format!("Duplikat {} ({} salinan)", name, count),
                srcs,
                parent,
                false,
                total,
            );
            if ok {
                // Dorong TM langsung (lihat delete: task cepat selesai
                // sebelum tick poll pertama).
                if let Ok(st) = ex_state.lock() {
                    ex_push_tasks(&ui, &st);
                }
                ui.set_toast(slint::SharedString::from(format!(
                    "Menduplikasi {} ({} salinan)…",
                    name, count
                )));
                true
            } else {
                ui.set_toast(slint::SharedString::from("Task Monitor penuh (50 tugas)."));
                false
            }
        });
    }

    // ---- Clipboard: copy / cut / paste ----
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        ui.on_ex_clipboard(move |joined, mode| {
            let Some(ui) = ui_weak.upgrade() else {
                return false;
            };
            let paths = ex_split_joined(joined.as_str());
            if paths.is_empty() {
                ui.set_toast(slint::SharedString::from("Pilih item dulu."));
                return false;
            }
            let mut st = ex_state.lock().unwrap();
            st.clipboard_src = st.cur_path.clone();
            st.clipboard = paths.clone();
            st.clipboard_mode = mode.to_string();
            ui.set_ex_cb_count(paths.len() as i32);
            ui.set_toast(slint::SharedString::from(if mode.as_str() == "cut" {
                format!("Dipotong: {} item.", paths.len())
            } else {
                format!("Disalin: {} item.", paths.len())
            }));
            true
        });
    }
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        ui.on_ex_paste(move || {
            let Some(ui) = ui_weak.upgrade() else {
                return false;
            };
            let (srcs, mode, src_folder) = {
                let st = ex_state.lock().unwrap();
                (
                    st.clipboard.clone(),
                    st.clipboard_mode.clone(),
                    st.clipboard_src.clone(),
                )
            };
            if srcs.is_empty() {
                ui.set_toast(slint::SharedString::from("Clipboard kosong."));
                return false;
            }
            let dst = ex_state.lock().unwrap().cur_path.clone();
            if dst.as_os_str().is_empty() {
                ui.set_toast(slint::SharedString::from("Buka folder dulu."));
                return false;
            }
            if mode == "cut" && src_folder == dst {
                ui.set_toast(slint::SharedString::from("Sudah di folder ini"));
                return false;
            }
            for s in &srcs {
                if explorer::ExplorerState::is_inside(s, &dst) {
                    ui.set_toast(slint::SharedString::from(
                        "Tidak bisa paste folder ke dalam folder itu sendiri.",
                    ));
                    return false;
                }
            }
            if mode == "cut" {
                // C# MoveFileBackground: folder ke folder yang sudah ada =
                // merge, file ke file yang sudah ada = timpa. Yang diblok
                // hanya benturan jenis (folder vs file).
                for s in &srcs {
                    if let Some(n) = s.file_name() {
                        let t = dst.join(n);
                        if t.exists() {
                            let both_dir = s.is_dir() && t.is_dir();
                            let both_file = s.is_file() && t.is_file();
                            if !both_dir && !both_file {
                                ui.set_toast(slint::SharedString::from(format!(
                                    "\"{}\" benturan jenis dengan yang ada di folder tujuan",
                                    n.to_string_lossy()
                                )));
                                return false;
                            }
                        }
                    }
                }
            }
            let is_cut = mode == "cut";
            let kind = if is_cut { "move" } else { "copy" };
            let total: u64 = srcs.iter().map(|p| explorer::dir_size(p)).sum();
            let name = if srcs.len() == 1 {
                ex_display_name(&srcs[0])
            } else {
                format!("{} item", srcs.len())
            };
            let ok = ex_spawn_task(&ex_state, kind, name, srcs, dst, is_cut, total);
            if ok {
                // Dorong TM langsung (lihat delete: task cepat selesai
                // sebelum tick poll pertama).
                if let Ok(st) = ex_state.lock() {
                    ex_push_tasks(&ui, &st);
                }
                ui.set_toast(slint::SharedString::from(if is_cut {
                    "Memindahkan (lihat Task Monitor)…"
                } else {
                    "Menyalin (lihat Task Monitor)…"
                }));
                true
            } else {
                ui.set_toast(slint::SharedString::from("Task Monitor penuh (50 tugas)."));
                false
            }
        });
    }

    // ---- Undo (maks 50, C# UndoStack) ----
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        let db_c = db.clone();
        ui.on_ex_undo(move || {
            let Some(ui) = ui_weak.upgrade() else {
                return false;
            };
            let entry = {
                let mut st = ex_state.lock().unwrap();
                st.undo.pop()
            };
            let Some(entry) = entry else {
                ui.set_toast(slint::SharedString::from("Tidak ada yang bisa di-undo."));
                return false;
            };
            match entry.kind.as_str() {
                "recycle" => {
                    ui.set_toast(slint::SharedString::from(
                        "Sudah di Recycle Bin — restore dari sana.",
                    ));
                    true
                }
                "new" | "copy" => {
                    let mut ok = true;
                    for (_, to) in &entry.pairs {
                        let cancel = AtomicBool::new(false);
                        let paused = AtomicBool::new(false);
                        let prog = Arc::new(Mutex::new(explorer::Progress::new(0)));
                        if explorer::delete_recursive(to, &cancel, &paused, &prog).is_err() {
                            ok = false;
                        }
                    }
                    let mut st = ex_state.lock().unwrap();
                    st.last_own_change = Some(Instant::now());
                    ex_reload_cur(&ui, &mut st, &db_c);
                    ui.set_toast(slint::SharedString::from(format!(
                        "Undo: {} {}.",
                        entry.name,
                        if ok { "dibatalkan" } else { "sebagian gagal" }
                    )));
                    ok
                }
                "rename" | "move" => {
                    let mut ok = true;
                    for (from, to) in &entry.pairs {
                        if std::fs::rename(to, from).is_err() {
                            ok = false;
                        }
                    }
                    let mut st = ex_state.lock().unwrap();
                    st.last_own_change = Some(Instant::now());
                    ex_reload_cur(&ui, &mut st, &db_c);
                    // Seleksi mengikuti item yang dipulihkan (nama asal);
                    // view menyeleksi ulang via changed ex-undo-select.
                    if ok {
                        if let Some((from, _)) = entry.pairs.first() {
                            ui.set_ex_undo_select(slint::SharedString::from(
                                ex_display_name(from),
                            ));
                        }
                    }
                    ui.set_toast(slint::SharedString::from(format!(
                        "Undo: {} {}.",
                        entry.name,
                        if ok { "dibatalkan" } else { "sebagian gagal" }
                    )));
                    ok
                }
                // Batch rename dibalikkan BERURUTAN TERBALIK (C# §2.7).
                "batchrename" => {
                    let mut ok = true;
                    for (from, to) in entry.pairs.iter().rev() {
                        if std::fs::rename(to, from).is_err() {
                            ok = false;
                        }
                    }
                    let mut st = ex_state.lock().unwrap();
                    st.last_own_change = Some(Instant::now());
                    ex_reload_cur(&ui, &mut st, &db_c);
                    ui.set_toast(slint::SharedString::from(format!(
                        "Undo: {} {}.",
                        entry.name,
                        if ok { "dibatalkan" } else { "sebagian gagal" }
                    )));
                    ok
                }
                _ => true,
            }
        });
    }

    // ---- Buka file (aplikasi default; .psd/.tif/.webp = Photoshop
    // smart-open di C# — bridge Photoshop belum ada di port, jadi dibuka
    // di aplikasi default + toast jujur) ----
    {
        let ui_weak = ui.as_weak();
        ui.on_ex_open_file(move |path, ext| {
            let Some(ui) = ui_weak.upgrade() else {
                return false;
            };
            let name = ex_display_name(Path::new(path.as_str()));
            // Panel compact memanggil dengan ext "" — turunkan dari path.
            let ext = if ext.as_str().is_empty() {
                Path::new(path.as_str())
                    .extension()
                    .map(|e| format!(".{}", e.to_string_lossy().to_lowercase()))
                    .unwrap_or_default()
            } else {
                ext.to_lowercase()
            };
            match platform::open_path(path.as_str()) {
                Ok(()) => {
                    if matches!(ext.as_str(), ".psd" | ".tif" | ".tiff" | ".webp") {
                        ui.set_toast(slint::SharedString::from(format!(
                            "Photoshop smart-open belum tersedia — dibuka di aplikasi default: {}",
                            name
                        )));
                    } else {
                        ui.set_toast(slint::SharedString::from(format!(
                            "Dibuka di aplikasi default: {}",
                            name
                        )));
                    }
                    true
                }
                Err(e) => {
                    ui.set_toast(slint::SharedString::from(format!("Gagal membuka: {}", e)));
                    false
                }
            }
        });
    }

    // ---- Panel Samping: grup TAMBAHAN MASTER + DOKUMEN (28 Nov 2026) ----
    // Indeks callback am-* = indeks TAMPILAN (hasil filter master-search);
    // dipetakan ke indeks asli lewat panel_master_view.
    {
        let ui_weak = ui.as_weak();
        let db_c = db.clone();
        let master_c = panel_master.clone();
        let docs_c = panel_docs.clone();
        let view_c = panel_master_view.clone();
        ui.on_am_add(move || {
            let Some(ui) = ui_weak.upgrade() else { return };
            match platform::pick_folder("Pilih folder master tambahan") {
                platform::PickResult::Cancelled => {}
                platform::PickResult::Unavailable(msg) => {
                    ui.set_toast(slint::SharedString::from(msg));
                }
                platform::PickResult::Picked(path) => {
                    let mut list = master_c.lock().unwrap();
                    if list.iter().any(|e| e.path.eq_ignore_ascii_case(&path)) {
                        ui.set_toast(slint::SharedString::from(
                            "Folder itu sudah ada di daftar master tambahan.",
                        ));
                        return;
                    }
                    let name = panel_extra::AdditionalMasterEntry::new(&path).display_name();
                    list.push(panel_extra::AdditionalMasterEntry::new(&path));
                    persist_additional_master(&db_c, &list);
                    drop(list);
                    repush_panel_extra(&ui, &master_c, &docs_c, &view_c);
                    ui.set_toast(slint::SharedString::from(format!(
                        "Master tambahan ditambahkan: {}",
                        name
                    )));
                }
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_c = db.clone();
        let master_c = panel_master.clone();
        let docs_c = panel_docs.clone();
        let view_c = panel_master_view.clone();
        ui.on_am_delete(move |view_idx| {
            let Some(ui) = ui_weak.upgrade() else { return };
            let Some(real) = panel_master_real_idx(&view_c, view_idx) else { return };
            let mut list = master_c.lock().unwrap();
            if real >= list.len() {
                return;
            }
            let name = list[real].display_name();
            list.remove(real);
            persist_additional_master(&db_c, &list);
            drop(list);
            repush_panel_extra(&ui, &master_c, &docs_c, &view_c);
            ui.set_toast(slint::SharedString::from(format!(
                "Master tambahan dihapus: {}",
                name
            )));
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_c = db.clone();
        let master_c = panel_master.clone();
        let docs_c = panel_docs.clone();
        let view_c = panel_master_view.clone();
        ui.on_am_move(move |view_idx, dir| {
            let Some(ui) = ui_weak.upgrade() else { return };
            // Tukar posisi dua entri ASLI yang bersebelahan di tampilan
            // (tetap benar saat search memfilter — yang ditukar adalah
            // tetangga yang terlihat, bukan tetangga di daftar penuh).
            let (a, b) = {
                let map = view_c.lock().unwrap();
                let Some(&ra) = map.get(view_idx as usize) else { return };
                let Some(&rb) = map.get((view_idx + dir) as usize) else {
                    return;
                };
                (ra, rb)
            };
            if a == b {
                return;
            }
            let mut list = master_c.lock().unwrap();
            if a < list.len() && b < list.len() {
                list.swap(a, b);
                persist_additional_master(&db_c, &list);
                drop(list);
                repush_panel_extra(&ui, &master_c, &docs_c, &view_c);
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_c = db.clone();
        let master_c = panel_master.clone();
        let docs_c = panel_docs.clone();
        let view_c = panel_master_view.clone();
        ui.on_am_set_name(move |view_idx, name| {
            let Some(ui) = ui_weak.upgrade() else { return };
            let name = name.trim().to_string();
            if name.is_empty() {
                ui.set_toast(slint::SharedString::from("Nama tidak boleh kosong."));
                return;
            }
            let Some(real) = panel_master_real_idx(&view_c, view_idx) else { return };
            let mut list = master_c.lock().unwrap();
            if real >= list.len() {
                return;
            }
            list[real].name = name;
            persist_additional_master(&db_c, &list);
            drop(list);
            repush_panel_extra(&ui, &master_c, &docs_c, &view_c);
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_c = db.clone();
        let master_c = panel_master.clone();
        let docs_c = panel_docs.clone();
        let view_c = panel_master_view.clone();
        ui.on_am_set_path(move |view_idx, path| {
            let Some(ui) = ui_weak.upgrade() else { return };
            let path = path.trim().to_string();
            if path.is_empty() {
                ui.set_toast(slint::SharedString::from("Path tidak boleh kosong."));
                return;
            }
            let Some(real) = panel_master_real_idx(&view_c, view_idx) else { return };
            let mut list = master_c.lock().unwrap();
            if real >= list.len() {
                return;
            }
            // Ala C#: bila nama lama adalah nama folder otomatis, nama ikut
            // path baru.
            let old_auto = panel_extra::folder_name(&list[real].path);
            if list[real].name.trim().is_empty() || list[real].name == old_auto {
                list[real].name = panel_extra::folder_name(&path);
            }
            list[real].path = path.clone();
            persist_additional_master(&db_c, &list);
            drop(list);
            repush_panel_extra(&ui, &master_c, &docs_c, &view_c);
            if !Path::new(&path).exists() {
                ui.set_toast(slint::SharedString::from(
                    "Path disimpan, tapi folder itu tidak ada di disk.",
                ));
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_c = db.clone();
        let master_c = panel_master.clone();
        let docs_c = panel_docs.clone();
        let view_c = panel_master_view.clone();
        ui.on_am_cycle_color(move |view_idx| {
            let Some(ui) = ui_weak.upgrade() else { return };
            let Some(real) = panel_master_real_idx(&view_c, view_idx) else { return };
            let mut list = master_c.lock().unwrap();
            if real >= list.len() {
                return;
            }
            let cur = list[real].effective_color();
            list[real].color = panel_extra::next_color(&cur);
            persist_additional_master(&db_c, &list);
            drop(list);
            repush_panel_extra(&ui, &master_c, &docs_c, &view_c);
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_c = db.clone();
        let master_c = panel_master.clone();
        let docs_c = panel_docs.clone();
        let view_c = panel_master_view.clone();
        ui.on_am_cycle_icon(move |view_idx| {
            let Some(ui) = ui_weak.upgrade() else { return };
            let Some(real) = panel_master_real_idx(&view_c, view_idx) else { return };
            let mut list = master_c.lock().unwrap();
            if real >= list.len() {
                return;
            }
            let cur = list[real].effective_icon();
            list[real].icon = panel_extra::next_icon(&cur);
            persist_additional_master(&db_c, &list);
            drop(list);
            repush_panel_extra(&ui, &master_c, &docs_c, &view_c);
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_c = db.clone();
        let master_c = panel_master.clone();
        let docs_c = panel_docs.clone();
        let view_c = panel_master_view.clone();
        ui.on_am_pick_path(move |view_idx| {
            let Some(ui) = ui_weak.upgrade() else { return };
            let Some(real) = panel_master_real_idx(&view_c, view_idx) else { return };
            match platform::pick_folder("Pilih folder master") {
                platform::PickResult::Cancelled => {}
                platform::PickResult::Unavailable(msg) => {
                    ui.set_toast(slint::SharedString::from(msg));
                }
                platform::PickResult::Picked(path) => {
                    let mut list = master_c.lock().unwrap();
                    if real >= list.len() {
                        return;
                    }
                    let old_auto = panel_extra::folder_name(&list[real].path);
                    if list[real].name.trim().is_empty() || list[real].name == old_auto {
                        list[real].name = panel_extra::folder_name(&path);
                    }
                    list[real].path = path;
                    persist_additional_master(&db_c, &list);
                    drop(list);
                    repush_panel_extra(&ui, &master_c, &docs_c, &view_c);
                }
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let master_c = panel_master.clone();
        let docs_c = panel_docs.clone();
        let view_c = panel_master_view.clone();
        ui.on_am_search_changed(move || {
            let Some(ui) = ui_weak.upgrade() else { return };
            repush_panel_extra(&ui, &master_c, &docs_c, &view_c);
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_c = db.clone();
        let master_c = panel_master.clone();
        let docs_c = panel_docs.clone();
        let view_c = panel_master_view.clone();
        ui.on_doc_add(move || {
            let Some(ui) = ui_weak.upgrade() else { return };
            match platform::pick_file(
                "Pilih dokumen",
                "Dokumen (*.doc;*.docx;*.pdf)|*.doc;*.docx;*.pdf",
            ) {
                platform::PickResult::Cancelled => {}
                platform::PickResult::Unavailable(msg) => {
                    ui.set_toast(slint::SharedString::from(msg));
                }
                platform::PickResult::Picked(path) => {
                    if !panel_extra::is_document_path(&path) {
                        ui.set_toast(slint::SharedString::from(
                            "Bukan dokumen (.doc / .docx / .pdf).",
                        ));
                        return;
                    }
                    let mut list = docs_c.lock().unwrap();
                    if list.iter().any(|d| d.path.eq_ignore_ascii_case(&path)) {
                        ui.set_toast(slint::SharedString::from(
                            "Dokumen itu sudah ada di daftar.",
                        ));
                        return;
                    }
                    let name = panel_extra::DokumenEntry { path: path.clone() }.file_name();
                    list.push(panel_extra::DokumenEntry { path });
                    persist_panel_docs(&db_c, &list);
                    drop(list);
                    repush_panel_extra(&ui, &master_c, &docs_c, &view_c);
                    ui.set_toast(slint::SharedString::from(format!(
                        "Dokumen ditambahkan: {}",
                        name
                    )));
                }
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let docs_c = panel_docs.clone();
        ui.on_doc_open(move |idx| {
            let Some(ui) = ui_weak.upgrade() else { return };
            let Some(entry) = docs_c.lock().unwrap().get(idx as usize).cloned() else {
                return;
            };
            if !Path::new(&entry.path).exists() {
                ui.set_toast(slint::SharedString::from(format!(
                    "File tidak ditemukan: {}",
                    entry.file_name()
                )));
                return;
            }
            match platform::open_path(&entry.path) {
                Ok(()) => {
                    ui.set_toast(slint::SharedString::from(format!(
                        "Dibuka di aplikasi default: {}",
                        entry.file_name()
                    )));
                }
                Err(e) => {
                    ui.set_toast(slint::SharedString::from(format!("Gagal membuka: {}", e)));
                }
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_c = db.clone();
        let master_c = panel_master.clone();
        let docs_c = panel_docs.clone();
        let view_c = panel_master_view.clone();
        ui.on_doc_remove(move |idx| {
            let Some(ui) = ui_weak.upgrade() else { return };
            let mut list = docs_c.lock().unwrap();
            if idx < 0 || (idx as usize) >= list.len() {
                return;
            }
            let name = list[idx as usize].file_name();
            list.remove(idx as usize);
            persist_panel_docs(&db_c, &list);
            drop(list);
            repush_panel_extra(&ui, &master_c, &docs_c, &view_c);
            ui.set_toast(slint::SharedString::from(format!(
                "Dokumen dihapus dari daftar: {}",
                name
            )));
        });
    }

    // ---- Panel DOC: Instan Redaksi — tab + window mengambang ----
    // (28 Nov 2026; spesifikasi C# di EXPLORER_SPEC.md bagian Tab DOC;
    // pola tear-off = BATCH c208473, DESIGN_SYSTEM.md §16.10.)
    // Satu-satunya sumber kebenaran = DocManager (`doc_mgr`) + prop
    // AppWindow; DocWindow hanya cermin (sync_doc_to_open_window).
    // Catatan: edit dari DocWindow TIDAK memperbarui prop AppWindow
    // otomatis (window terpisah, tanpa binding .slint) — handler di
    // sini memakai argumen callback sebagai nilai baru, lalu set prop
    // AppWindow secara eksplisit sebelum persist + sync balik.
    {
        let ui_weak = ui.as_weak();
        let db_c = db.clone();
        let mgr_c = doc_mgr.clone();
        ui.on_doc_open_settings(move || {
            let Some(ui) = ui_weak.upgrade() else { return };
            open_doc_settings_window(&ui_weak, &db_c, &mgr_c);
            let _ = ui;
        });
    }
    {
        let ui_weak = ui.as_weak();
        let doc_c = doc_win_cell.clone();
        let db_c = db.clone();
        ui.on_doc_pop_out(move || {
            let Some(ui) = ui_weak.upgrade() else { return };
            detach_doc_window(&ui, &doc_c, &db_c, None);
        });
    }
    {
        let ui_weak = ui.as_weak();
        let doc_c = doc_win_cell.clone();
        let db_c = db.clone();
        ui.on_doc_tear_off(move |x, y| {
            let Some(ui) = ui_weak.upgrade() else { return };
            let win = ui.window();
            let pos = win.position();
            let scale = win.scale_factor();
            let px = pos.x + (x * scale) as i32;
            let py = pos.y + (y * scale) as i32;
            let nx = (px - (240.0 * scale) as i32).max(0);
            let ny = (py - (14.0 * scale) as i32).max(0);
            detach_doc_window(&ui, &doc_c, &db_c, Some((nx, ny)));
        });
    }
    {
        let ui_weak = ui.as_weak();
        let doc_c = doc_win_cell.clone();
        let db_c = db.clone();
        ui.on_doc_dock(move || {
            let Some(ui) = ui_weak.upgrade() else { return };
            dock_doc_window(&ui, &doc_c, &db_c);
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_c = db.clone();
        let mgr_c = doc_mgr.clone();
        let doc_c = doc_win_cell.clone();
        ui.on_doc_toggle_second_logo(move || {
            let Some(ui) = ui_weak.upgrade() else { return };
            let second = {
                let mut mgr = mgr_c.lock().unwrap();
                if let Some(t) = mgr.active_tab_mut() {
                    t.show_second_logo = !t.show_second_logo;
                }
                let s = mgr.active_tab().map(|t| t.show_second_logo).unwrap_or(false);
                persist_doc_tabs(&db_c, &mgr);
                s
            };
            ui.set_doc_show_second_logo(second);
            ui.set_toast(slint::SharedString::from(if second {
                "Logo alternatif ditampilkan."
            } else {
                "Logo alternatif disembunyikan."
            }));
            sync_doc_to_open_window(&doc_c, &ui);
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_c = db.clone();
        let mgr_c = doc_mgr.clone();
        let doc_c = doc_win_cell.clone();
        ui.on_doc_reset(move || {
            let Some(ui) = ui_weak.upgrade() else { return };
            {
                let mut mgr = mgr_c.lock().unwrap();
                mgr.reset_active();
                persist_doc_tabs(&db_c, &mgr);
                push_doc_to_ui(&ui, &mgr);
            }
            sync_doc_to_open_window(&doc_c, &ui);
            ui.set_toast(slint::SharedString::from("Redaksi direset."));
        });
    }
    {
        let ui_weak = ui.as_weak();
        let mgr_c = doc_mgr.clone();
        ui.on_doc_sync(move || {
            let Some(ui) = ui_weak.upgrade() else { return };
            let mgr = mgr_c.lock().unwrap();
            match mgr.write_doc_info_json(mgr.active_index) {
                Ok(path) => {
                    ui.set_toast(slint::SharedString::from(format!(
                        "doc_info.json ditulis: {}",
                        path.display()
                    )));
                }
                Err(e) => {
                    ui.set_toast(slint::SharedString::from(e));
                }
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_c = db.clone();
        let mgr_c = doc_mgr.clone();
        let doc_c = doc_win_cell.clone();
        ui.on_doc_tab_add(move || {
            let Some(ui) = ui_weak.upgrade() else { return };
            {
                let mut mgr = mgr_c.lock().unwrap();
                mgr.add_tab();
                persist_doc_tabs(&db_c, &mgr);
                push_doc_to_ui(&ui, &mgr);
            }
            sync_doc_to_open_window(&doc_c, &ui);
        });
    }
    {
        let ui_weak = ui.as_weak();
        let mgr_c = doc_mgr.clone();
        let doc_c = doc_win_cell.clone();
        ui.on_doc_tab_select(move |idx| {
            let Some(ui) = ui_weak.upgrade() else { return };
            {
                let mut mgr = mgr_c.lock().unwrap();
                mgr.set_active(idx as usize);
                push_doc_to_ui(&ui, &mgr);
            }
            sync_doc_to_open_window(&doc_c, &ui);
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_c = db.clone();
        let mgr_c = doc_mgr.clone();
        let doc_c = doc_win_cell.clone();
        ui.on_doc_tab_close(move |idx| {
            let Some(ui) = ui_weak.upgrade() else { return };
            {
                let mut mgr = mgr_c.lock().unwrap();
                mgr.close_tab(idx as usize);
                persist_doc_tabs(&db_c, &mgr);
                push_doc_to_ui(&ui, &mgr);
            }
            sync_doc_to_open_window(&doc_c, &ui);
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_c = db.clone();
        let mgr_c = doc_mgr.clone();
        let doc_c = doc_win_cell.clone();
        ui.on_doc_school_edited(move |text| {
            let Some(ui) = ui_weak.upgrade() else { return };
            ui.set_doc_school(text.clone());
            {
                let mut mgr = mgr_c.lock().unwrap();
                mgr.update_active(
                    &text,
                    &ui.get_doc_address(),
                    &ui.get_doc_logo_path(),
                    &ui.get_doc_logo2_path(),
                    ui.get_doc_show_second_logo(),
                );
                persist_doc_tabs(&db_c, &mgr);
            }
            sync_doc_to_open_window(&doc_c, &ui);
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_c = db.clone();
        let mgr_c = doc_mgr.clone();
        let doc_c = doc_win_cell.clone();
        ui.on_doc_address_edited(move |text| {
            let Some(ui) = ui_weak.upgrade() else { return };
            ui.set_doc_address(text.clone());
            {
                let mut mgr = mgr_c.lock().unwrap();
                mgr.update_active(
                    &ui.get_doc_school(),
                    &text,
                    &ui.get_doc_logo_path(),
                    &ui.get_doc_logo2_path(),
                    ui.get_doc_show_second_logo(),
                );
                persist_doc_tabs(&db_c, &mgr);
            }
            sync_doc_to_open_window(&doc_c, &ui);
        });
    }
    {
        let ui_weak = ui.as_weak();
        ui.on_doc_from_ps(move |field| {
            let Some(ui) = ui_weak.upgrade() else { return };
            // Ambil teks dari layer Photoshop = butuh Photoshop di
            // Windows (jsx); di Linux port ini belum ada — jujur saja.
            let _ = field;
            ui.set_toast(slint::SharedString::from(
                "Ambil dari Photoshop butuh Photoshop di Windows.",
            ));
        });
    }
    {
        let ui_weak = ui.as_weak();
        ui.on_doc_copy_field(move |field| {
            let Some(ui) = ui_weak.upgrade() else { return };
            let text = if field.as_str() == "address" {
                ui.get_doc_address().to_string()
            } else {
                ui.get_doc_school().to_string()
            };
            match platform::copy_to_clipboard(&text) {
                Ok(()) => {
                    ui.set_toast(slint::SharedString::from("Disalin ke clipboard."));
                }
                Err(e) => {
                    ui.set_toast(slint::SharedString::from(e));
                }
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_c = db.clone();
        let mgr_c = doc_mgr.clone();
        let doc_c = doc_win_cell.clone();
        ui.on_doc_pick_logo(move |n| {
            let Some(ui) = ui_weak.upgrade() else { return };
            match platform::pick_file(
                "Pilih logo",
                "Gambar (*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif",
            ) {
                platform::PickResult::Cancelled => {}
                platform::PickResult::Unavailable(msg) => {
                    ui.set_toast(slint::SharedString::from(msg));
                }
                platform::PickResult::Picked(path) => {
                    let is2 = n == 2;
                    if is2 {
                        ui.set_doc_logo2_path(slint::SharedString::from(path.clone()));
                    } else {
                        ui.set_doc_logo_path(slint::SharedString::from(path.clone()));
                    }
                    {
                        let mut mgr = mgr_c.lock().unwrap();
                        mgr.update_active(
                            &ui.get_doc_school(),
                            &ui.get_doc_address(),
                            &ui.get_doc_logo_path(),
                            &ui.get_doc_logo2_path(),
                            ui.get_doc_show_second_logo(),
                        );
                        persist_doc_tabs(&db_c, &mgr);
                        push_doc_to_ui(&ui, &mgr);
                    }
                    sync_doc_to_open_window(&doc_c, &ui);
                    ui.set_toast(slint::SharedString::from(format!(
                        "Logo {} dipilih: {}",
                        if is2 { "2" } else { "1" },
                        std::path::Path::new(&path)
                            .file_name()
                            .map(|f| f.to_string_lossy().to_string())
                            .unwrap_or(path)
                    )));
                }
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_c = db.clone();
        let mgr_c = doc_mgr.clone();
        let doc_c = doc_win_cell.clone();
        ui.on_doc_clear_logo(move |n| {
            let Some(ui) = ui_weak.upgrade() else { return };
            if n == 2 {
                ui.set_doc_logo2_path(slint::SharedString::from(""));
            } else {
                ui.set_doc_logo_path(slint::SharedString::from(""));
            }
            {
                let mut mgr = mgr_c.lock().unwrap();
                mgr.update_active(
                    &ui.get_doc_school(),
                    &ui.get_doc_address(),
                    &ui.get_doc_logo_path(),
                    &ui.get_doc_logo2_path(),
                    ui.get_doc_show_second_logo(),
                );
                persist_doc_tabs(&db_c, &mgr);
                push_doc_to_ui(&ui, &mgr);
            }
            sync_doc_to_open_window(&doc_c, &ui);
        });
    }
    {
        let ui_weak = ui.as_weak();
        ui.on_doc_send_logo(move |n| {
            let Some(ui) = ui_weak.upgrade() else { return };
            // Kirim logo ke Photoshop = butuh Photoshop di Windows.
            let _ = n;
            ui.set_toast(slint::SharedString::from(
                "Kirim ke Photoshop butuh Photoshop di Windows.",
            ));
        });
    }
    {
        let ui_weak = ui.as_weak();
        let db_c = db.clone();
        let mgr_c = doc_mgr.clone();
        let doc_c = doc_win_cell.clone();
        ui.on_doc_paste_logo(move |n| {
            let Some(ui) = ui_weak.upgrade() else { return };
            match platform::paste_from_clipboard() {
                Ok(text) => {
                    let t = text.trim().to_string();
                    let p = std::path::Path::new(&t);
                    let is_img = p.exists()
                        && p.extension()
                            .map(|e| {
                                matches!(
                                    e.to_string_lossy().to_lowercase().as_str(),
                                    "png" | "jpg" | "jpeg" | "webp" | "bmp" | "gif"
                                )
                            })
                            .unwrap_or(false);
                    if !is_img {
                        ui.set_toast(slint::SharedString::from(
                            "Clipboard bukan path gambar yang ada.",
                        ));
                        return;
                    }
                    if n == 2 {
                        ui.set_doc_logo2_path(slint::SharedString::from(t));
                    } else {
                        ui.set_doc_logo_path(slint::SharedString::from(t));
                    }
                    {
                        let mut mgr = mgr_c.lock().unwrap();
                        mgr.update_active(
                            &ui.get_doc_school(),
                            &ui.get_doc_address(),
                            &ui.get_doc_logo_path(),
                            &ui.get_doc_logo2_path(),
                            ui.get_doc_show_second_logo(),
                        );
                        persist_doc_tabs(&db_c, &mgr);
                        push_doc_to_ui(&ui, &mgr);
                    }
                    sync_doc_to_open_window(&doc_c, &ui);
                    ui.set_toast(slint::SharedString::from("Logo ditempel dari clipboard."));
                }
                Err(e) => {
                    ui.set_toast(slint::SharedString::from(e));
                }
            }
        });
    }

    // ---- Salin path (clipboard sistem, beneran) ----
    {
        let ui_weak = ui.as_weak();
        ui.on_ex_copy_path(move |text| {
            let Some(ui) = ui_weak.upgrade() else {
                return false;
            };
            match platform::copy_to_clipboard(text.as_str()) {
                Ok(()) => {
                    ui.set_toast(slint::SharedString::from("Path disalin."));
                    true
                }
                Err(e) => {
                    ui.set_toast(slint::SharedString::from(format!(
                        "Gagal menyalin path: {}",
                        e
                    )));
                    false
                }
            }
        });
    }

    // ---- Salin nama (clipboard sistem, beneran) ----
    {
        let ui_weak = ui.as_weak();
        ui.on_ex_copy_name(move |text| {
            let Some(ui) = ui_weak.upgrade() else {
                return false;
            };
            match platform::copy_to_clipboard(text.as_str()) {
                Ok(()) => {
                    ui.set_toast(slint::SharedString::from("Nama disalin."));
                    true
                }
                Err(e) => {
                    ui.set_toast(slint::SharedString::from(format!(
                        "Gagal menyalin nama: {}",
                        e
                    )));
                    false
                }
            }
        });
    }

    // ---- Paste Path (Ctrl+Shift+V, C# §2.7): navigasi ke path teks di
    // clipboard; bila file -> folder induknya. Jalur dikirim lewat prop
    // ex-paste-path, view menavigasi di changed (pola ex-undo-select).
    {
        let ui_weak = ui.as_weak();
        ui.on_ex_paste_path_req(move || {
            let Some(ui) = ui_weak.upgrade() else {
                return;
            };
            match platform::paste_from_clipboard() {
                Ok(text) => {
                    let p = PathBuf::from(text.trim());
                    if p.as_os_str().is_empty() {
                        ui.set_toast(slint::SharedString::from("Clipboard kosong."));
                        return;
                    }
                    let target = if p.is_file() {
                        p.parent().map(|d| d.to_path_buf()).unwrap_or(p)
                    } else if p.is_dir() {
                        p
                    } else {
                        ui.set_toast(slint::SharedString::from(
                            "Path di clipboard tidak ditemukan.",
                        ));
                        return;
                    };
                    ui.set_ex_paste_path(slint::SharedString::from(
                        target.to_string_lossy().into_owned(),
                    ));
                }
                Err(e) => {
                    ui.set_toast(slint::SharedString::from(format!(
                        "Gagal membaca clipboard: {}",
                        e
                    )));
                }
            }
        });
    }

    // ---- Batch rename dari clipboard (C# §2.7): baca teks clipboard
    // per baris; item terpilih diurutkan berdasarkan Nama (bukan urutan
    // seleksi/tampilan); file: ekstensi asli dipertahankan bila nama baru
    // belum punya ekstensi; entri undo "batchrename". Toast memakai
    // jumlah yang BENAR-BENAR berhasil (jangan tiru bug C# yang memakai
    // Min meski sebagian gagal — EXPLORER_SPEC §8.2).
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        let db_c = db.clone();
        ui.on_ex_batch_rename(move |joined| {
            let Some(ui) = ui_weak.upgrade() else {
                return false;
            };
            let names = match platform::paste_from_clipboard() {
                Ok(t) => t
                    .lines()
                    .map(|l| l.trim().to_string())
                    .filter(|l| !l.is_empty())
                    .collect::<Vec<String>>(),
                Err(e) => {
                    ui.set_toast(slint::SharedString::from(format!(
                        "Gagal membaca clipboard: {}",
                        e
                    )));
                    return false;
                }
            };
            if names.is_empty() {
                ui.set_toast(slint::SharedString::from(
                    "Clipboard kosong — salin daftar nama baru dulu.",
                ));
                return false;
            }
            let mut srcs = ex_split_joined(joined.as_str());
            // Urut berdasarkan Nama (C#), bukan urutan seleksi.
            srcs.sort_by(|a, b| {
                batch::natural_cmp(
                    &a.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default(),
                    &b.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default(),
                )
            });
            let mut pairs: Vec<(PathBuf, PathBuf)> = Vec::new();
            let mut done = 0usize;
            for (src, new_name) in srcs.iter().zip(names.iter()) {
                if ex_bad_name(new_name) {
                    continue;
                }
                let parent = match src.parent() {
                    Some(p) => p.to_path_buf(),
                    None => continue,
                };
                let target = if src.is_dir() {
                    parent.join(new_name)
                } else if new_name.contains('.') {
                    // Nama baru sudah punya ekstensi -> pakai apa adanya.
                    parent.join(new_name)
                } else {
                    // File + nama baru tanpa ekstensi -> ekstensi asli
                    // dipertahankan (C#).
                    let ext = src
                        .extension()
                        .map(|e| e.to_string_lossy().into_owned())
                        .unwrap_or_default();
                    if ext.is_empty() {
                        parent.join(new_name)
                    } else {
                        parent.join(format!("{}.{}", new_name, ext))
                    }
                };
                if target.exists() || std::fs::rename(src, &target).is_err() {
                    continue;
                }
                pairs.push((src.clone(), target));
                done += 1;
            }
            if done == 0 {
                ui.set_toast(slint::SharedString::from(
                    "Batch rename gagal — periksa nama di clipboard.",
                ));
                return false;
            }
            {
                let mut st = ex_state.lock().unwrap();
                st.push_undo(explorer::UndoEntry {
                    kind: "batchrename".to_string(),
                    name: format!("Batch rename {} item", done),
                    pairs,
                });
                st.last_own_change = Some(Instant::now());
                ex_reload_cur(&ui, &mut st, &db_c);
            }
            ui.set_toast(slint::SharedString::from(format!(
                "Batch rename: {} item diganti.",
                done
            )));
            true
        });
    }

    // ---- Guard hapus: folder yang sedang dibuka (+ leluhurnya, root)
    // tidak bisa dihapus (C#). Mengembalikan nama pemblokir, "" bila OK.
    {
        let ex_state = explorer_state.clone();
        ui.on_ex_delete_blocked(move |joined, tabs_joined| {
            let paths = ex_split_joined(joined.as_str());
            let tabs = ex_split_joined(tabs_joined.as_str());
            let st = ex_state.lock().unwrap();
            let mut open: Vec<PathBuf> = vec![st.cur_path.clone()];
            open.extend(tabs.iter().cloned());
            if !st.root_path.as_os_str().is_empty() {
                open.push(st.root_path.clone());
            }
            for p in &paths {
                for o in &open {
                    if o.as_os_str().is_empty() {
                        continue;
                    }
                    if *p == *o || explorer::ExplorerState::is_inside(p, o) {
                        return slint::SharedString::from(ex_display_name(p));
                    }
                }
            }
            slint::SharedString::new()
        });
    }

    // ---- Task Monitor: poll / cancel / retry / remove / clear-done ----
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        let db_c = db.clone();
        ui.on_ex_task_poll(move || {
            let Some(ui) = ui_weak.upgrade() else { return; };
            let mut st = match ex_state.lock() {
                Ok(s) => s,
                Err(_) => return,
            };
            let mut need_reload = false;
            let mut clear_cut = false;
            let mut msgs: Vec<String> = Vec::new();
            for t in st.tasks.iter_mut() {
                // Status terminal HANYA done/failed/cancelled — "merging"
                // dan "cancelling" masih berjalan; kalau dianggap terminal
                // di sini, notified keburu true dan toast+reload hilang.
                let terminal =
                    matches!(t.status().as_str(), "done" | "failed" | "cancelled");
                if terminal && !t.notified {
                    t.notified = true;
                    match t.status().as_str() {
                        "done" => {
                            msgs.push(format!("Selesai: {}", t.name));
                            need_reload = true;
                            if t.is_cut {
                                clear_cut = true;
                            }
                        }
                        "failed" => {
                            let e = t.error.lock().map(|e| e.clone()).unwrap_or_default();
                            msgs.push(format!("Gagal: {} — {}", t.name, e));
                            need_reload = true;
                        }
                        "cancelled" => {
                            msgs.push(format!("Dibatalkan: {}", t.name));
                            need_reload = true;
                        }
                        _ => {}
                    }
                }
            }
            if clear_cut {
                st.clipboard.clear();
                st.clipboard_mode.clear();
                ui.set_ex_cb_count(0);
            }
            if need_reload {
                st.last_own_change = Some(Instant::now());
                ex_reload_cur(&ui, &mut st, &db_c);
            }
            ex_push_tasks(&ui, &st);
            if !msgs.is_empty() {
                ui.set_toast(slint::SharedString::from(msgs.join(" | ")));
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        ui.on_ex_task_cancel(move |i| {
            // C# CancelTask: status langsung "Cancelling…" + flag batal;
            // pekerja mengubahnya jadi "Cancelled" saat berhenti.
            let idx = {
                let Ok(st) = ex_state.lock() else { return; };
                tm_task_idx(&st, i as usize)
            };
            let Some(idx) = idx else { return };
            let mut push = false;
            if let Ok(st) = ex_state.lock() {
                if let Some(t) = st.tasks.get(idx) {
                    if matches!(t.status().as_str(), "run" | "merging") {
                        if let Ok(mut s) = t.status.lock() {
                            *s = "cancelling".to_string();
                        }
                        t.cancel.store(true, Ordering::Relaxed);
                        push = true;
                    }
                }
            }
            if push {
                if let Some(ui) = ui_weak.upgrade() {
                    if let Ok(st) = ex_state.lock() {
                        ex_push_tasks(&ui, &st);
                    }
                }
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        ui.on_ex_task_pause(move |i| {
            // Jeda (TeraCopy, disetujui abeng 28 Nov 2026 08:05): pekerja
            // berhenti antar chunk; tugas menahan giliran antrean serial
            // (thread tetap memegang gate) sampai lanjut/dibatalkan.
            let idx = {
                let Ok(st) = ex_state.lock() else { return; };
                tm_task_idx(&st, i as usize)
            };
            let Some(idx) = idx else { return };
            let mut push = false;
            if let Ok(st) = ex_state.lock() {
                if let Some(t) = st.tasks.get(idx) {
                    let cur = t.status();
                    if cur == "run" || cur == "merging" {
                        if let Ok(mut pf) = t.paused_from.lock() {
                            *pf = cur;
                        }
                        t.paused.store(true, Ordering::Relaxed);
                        if let Ok(mut s) = t.status.lock() {
                            *s = "paused".to_string();
                        }
                        push = true;
                    }
                }
            }
            if push {
                if let Some(ui) = ui_weak.upgrade() {
                    if let Ok(st) = ex_state.lock() {
                        ex_push_tasks(&ui, &st);
                    }
                }
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        ui.on_ex_task_resume(move |i| {
            // Lanjutkan dari titik berhenti (BUKAN dari nol): pekerja
            // hanya menunggu flag dicabut, posisi file sudah di tempat.
            let idx = {
                let Ok(st) = ex_state.lock() else { return; };
                tm_task_idx(&st, i as usize)
            };
            let Some(idx) = idx else { return };
            let mut push = false;
            if let Ok(st) = ex_state.lock() {
                if let Some(t) = st.tasks.get(idx) {
                    if t.status() == "paused" {
                        let back = t
                            .paused_from
                            .lock()
                            .map(|s| s.clone())
                            .unwrap_or_else(|_| "run".to_string());
                        let back = if back.is_empty() { "run".to_string() } else { back };
                        t.paused.store(false, Ordering::Relaxed);
                        if let Ok(mut s) = t.status.lock() {
                            *s = back;
                        }
                        push = true;
                    }
                }
            }
            if push {
                if let Some(ui) = ui_weak.upgrade() {
                    if let Ok(st) = ex_state.lock() {
                        ex_push_tasks(&ui, &st);
                    }
                }
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        ui.on_ex_task_retry(move |i| {
            let params = {
                let mut st = match ex_state.lock() {
                    Ok(s) => s,
                    Err(_) => return,
                };
                let Some(idx) = tm_task_idx(&st, i as usize) else {
                    return;
                };
                let Some(t) = st.tasks.get(idx) else {
                    return;
                };
                if matches!(
                    t.status().as_str(),
                    "run" | "merging" | "cancelling" | "paused"
                ) {
                    return;
                }
                let p = (
                    t.kind.clone(),
                    t.name.clone(),
                    t.srcs.clone(),
                    t.dst.clone(),
                    t.is_cut,
                    t.total,
                );
                st.tasks.remove(idx);
                p
            };
            ex_spawn_task(
                &ex_state, &params.0, params.1, params.2, params.3, params.4, params.5,
            );
            if let Some(ui) = ui_weak.upgrade() {
                if let Ok(st) = ex_state.lock() {
                    ex_push_tasks(&ui, &st);
                }
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        ui.on_ex_task_remove(move |i| {
            let mut st = match ex_state.lock() {
                Ok(s) => s,
                Err(_) => return,
            };
            let Some(idx) = tm_task_idx(&st, i as usize) else {
                return;
            };
            if let Some(t) = st.tasks.get(idx) {
                if matches!(
                    t.status().as_str(),
                    "run" | "merging" | "cancelling" | "paused"
                ) {
                    return;
                }
                st.tasks.remove(idx);
            }
            drop(st);
            if let Some(ui) = ui_weak.upgrade() {
                if let Ok(st) = ex_state.lock() {
                    ex_push_tasks(&ui, &st);
                }
            }
        });
    }
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        ui.on_ex_task_clear_done(move || {
            let mut st = match ex_state.lock() {
                Ok(s) => s,
                Err(_) => return,
            };
            st.tasks.retain(|t| t.status() == "run");
            drop(st);
            if let Some(ui) = ui_weak.upgrade() {
                if let Ok(st) = ex_state.lock() {
                    ex_push_tasks(&ui, &st);
                }
            }
        });
    }

    // ---- Quick Access: tambah / hapus (DB Configs.Explorer.QuickAccess) ----
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        let db_c = db.clone();
        ui.on_ex_qa_add(move |path| {
            let Some(ui) = ui_weak.upgrade() else {
                return false;
            };
            let path = path.as_str();
            if path.is_empty() {
                return false;
            }
            let name = Path::new(path)
                .file_name()
                .map(|n| n.to_string_lossy().into_owned())
                .unwrap_or_else(|| path.to_string());
            let mut st = ex_state.lock().unwrap();
            if st.qa.iter().any(|(_, p)| p == path) {
                ui.set_toast(slint::SharedString::from("Sudah ada di Quick Access."));
                return false;
            }
            if st.qa.len() >= 8 {
                ui.set_toast(slint::SharedString::from("Quick Access penuh (8)."));
                return false;
            }
            st.qa.push((name.clone(), path.to_string()));
            let arr: Vec<serde_json::Value> = st
                .qa
                .iter()
                .map(|(n, p)| {
                    serde_json::json!({"Name": n, "Path": p, "IsDynamic": false, "Icon": ""})
                })
                .collect();
            ex_db_set(
                &db_c,
                "Configs.Explorer.QuickAccess",
                &serde_json::Value::Array(arr).to_string(),
            );
            ex_push_qa(&ui, &st);
            ui.set_toast(slint::SharedString::from(format!(
                "Ditambah ke Quick Access: {}",
                name
            )));
            true
        });
    }
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        let db_c = db.clone();
        ui.on_ex_qa_remove(move |i| {
            let Some(ui) = ui_weak.upgrade() else {
                return false;
            };
            let mut st = ex_state.lock().unwrap();
            if (i as usize) >= st.qa.len() {
                return false;
            }
            let (name, _) = st.qa.remove(i as usize);
            let arr: Vec<serde_json::Value> = st
                .qa
                .iter()
                .map(|(n, p)| {
                    serde_json::json!({"Name": n, "Path": p, "IsDynamic": false, "Icon": ""})
                })
                .collect();
            ex_db_set(
                &db_c,
                "Configs.Explorer.QuickAccess",
                &serde_json::Value::Array(arr).to_string(),
            );
            ex_push_qa(&ui, &st);
            ui.set_toast(slint::SharedString::from(format!(
                "Pin dihapus: {}",
                name
            )));
            true
        });
    }

    // ---- Pinned tabs: simpan (DB Explorer_PinnedTabs) ----
    {
        let db_c = db.clone();
        ui.on_ex_pins_save(move |paths, _names, pinned, labels, colors| {
            let paths = read_str_model(paths);
            let pinned = read_bool_model(pinned);
            let labels = read_str_model(labels);
            let colors = read_i32_model(colors);
            let pinned_paths: Vec<String> = paths
                .iter()
                .zip(pinned.iter())
                .filter(|(_, p)| **p)
                .map(|(p, _)| p.to_string())
                .collect();
            let pinned_labels: Vec<String> = labels
                .iter()
                .zip(pinned.iter())
                .filter(|(_, p)| **p)
                .map(|(l, _)| l.to_string())
                .collect();
            let pinned_colors: Vec<i32> = colors
                .iter()
                .zip(pinned.iter())
                .filter(|(_, p)| **p)
                .map(|(c, _)| *c)
                .collect();
            let dto = serde_json::json!({
                "PinnedPaths": pinned_paths,
                "CustomLabels": pinned_labels,
                "CustomColors": pinned_colors,
            });
            ex_db_set(&db_c, "Explorer_PinnedTabs", &dto.to_string());
        });
    }

    // ---- View settings berubah (sort/desc/group/layout/local-only) ----
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        let db_c = db.clone();
        ui.on_ex_view_changed(move || {
            let Some(ui) = ui_weak.upgrade() else { return; };
            let sort = ui.get_ex_view_sort().to_string();
            let desc = ui.get_ex_view_desc();
            let group = ui.get_ex_view_group().to_string();
            let layout = ui.get_ex_view_layout().to_string();
            let local_only = ui.get_ex_view_local_only();
            let cur = ex_state.lock().map(|s| s.cur_path.clone()).unwrap_or_default();
            ex_save_view_settings(&db_c, &cur, &sort, desc, &group, &layout, local_only);
        });
    }

    // ---- Overlay dibuka: muat QA + pinned tabs dari DB ----
    {
        let ui_weak = ui.as_weak();
        let ex_state = explorer_state.clone();
        let db_c = db.clone();
        ui.on_ex_open(move || {
            let Some(ui) = ui_weak.upgrade() else { return; };
            let mut st = ex_state.lock().unwrap();
            st.root_path = explorer::resolve_root(|k| ex_db_get(&db_c, k));
            st.show_hidden = ex_db_get(&db_c, "Configs.Explorer.ShowHiddenFiles") == "true";
            ex_load_qa_pins(&ui, &mut st, &db_c);
            // Panel compact (Ctrl+L) butuh folder termuat — muat root
            // bila state belum pernah dimuat; jangan reset bila pengguna
            // sudah bernavigasi di overlay.
            if st.cur_path.as_os_str().is_empty() {
                let root = st.root_path.clone();
                ex_load_folder(&ui, &mut st, &db_c, &root);
            }
        });
    }

    // ---- Resolve root Explorer (dipakai open-explorer + tab baru) ----
    {
        let db_c = db.clone();
        ui.on_ex_resolve_root(move || {
            slint::SharedString::from(
                explorer::resolve_root(|k| ex_db_get(&db_c, k))
                    .to_string_lossy()
                    .into_owned(),
            )
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

    ui.on_ex_join_path(move |dir, name| {
        slint::SharedString::from(
            Path::new(dir.as_str())
                .join(name.as_str())
                .to_string_lossy()
                .into_owned(),
        )
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
                    BATCH_SHIFT_HELD.store(mods & 4 != 0, Ordering::SeqCst);
                    BATCH_CTRL_HELD.store(mods & 2 != 0, Ordering::SeqCst);
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
                    BATCH_SHIFT_HELD.store(mods & 4 != 0, Ordering::SeqCst);
                    BATCH_CTRL_HELD.store(mods & 2 != 0, Ordering::SeqCst);
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
    // sini. Trello di-sync saat startup / refresh manual / simpan
    // akun / tes koneksi, PLUS timer auto-refresh 1 detik di bawah
    // (`_trello_auto_timer`: cek elapsed per list terhadap interval
    // Settings > General "Interval refresh Trello", key DB
    // `Trello.RefreshInterval`, default 60 detik).
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
            let explorer_state_timer = explorer_state.clone();
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
                // Rasio gauge template widget — menyusul stat
                // (timer ini juga yang menyalin stat ke bar).
                update_gauge_ratios(&ui);

                // Explorer watcher (polling mtime 2 detik — tidak ada
                // crate notify di cache offline; C# FileSystemWatcher
                // Created/Deleted/Renamed, debounce 400ms). Diam 3 detik
                // setelah operasi kita sendiri (rename/buat/hapus/paste
                // mengubah mtime folder) supaya tidak reload sendiri.
                if ui.get_overlay_view() == "EXPLORER" {
                    if let Ok(mut st) = explorer_state_timer.lock() {
                        let recent_own = st
                            .last_own_change
                            .map(|t| t.elapsed() < std::time::Duration::from_secs(3))
                            .unwrap_or(false);
                        if !recent_own && !st.cur_path.as_os_str().is_empty() {
                            let cur_mtime = std::fs::metadata(&st.cur_path)
                                .and_then(|m| m.modified())
                                .ok();
                            if cur_mtime != st.cur_mtime {
                                let path = st.cur_path.clone();
                                if !ex_load_folder(&ui, &mut st, &db_timer, &path) {
                                    // Folder saat ini dihapus di luar:
                                    // naik ke induk (sampai root).
                                    let mut p = path.clone();
                                    while let Some(par) =
                                        p.parent().map(|x| x.to_path_buf())
                                    {
                                        if par.as_os_str().is_empty() {
                                            break;
                                        }
                                        if ex_load_folder(&ui, &mut st, &db_timer, &par) {
                                            ui.set_toast(slint::SharedString::from(format!(
                                                "Folder sudah tidak ada, kembali ke {}",
                                                par.display()
                                            )));
                                            break;
                                        }
                                        p = par;
                                    }
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

    // ---- Dynamic Island auto-hide (layzee, abeng 28 Nov
    // 11:58–11:59): 3 tingkat — pill penuh (normal), garis tipis
    // 4px (window lain maksimal, mis. browser), hilang total
    // (fullscreen video/game; muncul lagi saat keluar fullscreen).
    // Toggle Settings → Dynamic Island → Auto-hide, key DB
    // `Settings.Notch.AutoHide` (Bool, default ON). Timer 500ms —
    // bukan 2 detik seperti sync bar — supaya hide/show responsif
    // saat user masuk/keluar fullscreen. Timer ini SATU-SATUNYA
    // otoritas hide/show otomatis: tidak mengubah
    // `Settings.DynamicBar.Visible` (tutup user) — sembunyi
    // otomatis dilacak cell `autohidden` sendiri; `last_display`
    // mencegah set prop / hide-show berulang tiap tick.
    let _bar_autohide_timer = slint::Timer::default();
    _bar_autohide_timer.start(
        slint::TimerMode::Repeated,
        std::time::Duration::from_millis(500),
        {
            let ui_weak = ui.as_weak();
            let bar_cell = bar_win_cell.clone();
            let bar_vis = bar_visible.clone();
            let autohidden = std::rc::Rc::new(std::cell::Cell::new(false));
            let last_display =
                std::rc::Rc::new(std::cell::Cell::new(fullscreen::BarDisplay::Full));
            move || {
                let Some(ui) = ui_weak.upgrade() else {
                    return;
                };
                if !bar_vis.get() {
                    // Bar ditutup user — bukan urusan auto-hide.
                    autohidden.set(false);
                    last_display.set(fullscreen::BarDisplay::Full);
                    return;
                }
                let borrow = bar_cell.borrow();
                let Some(w) = borrow.as_ref() else {
                    autohidden.set(false);
                    last_display.set(fullscreen::BarDisplay::Full);
                    return;
                };
                let want = fullscreen::decide_bar_display(
                    &ui.get_app_notch_mode(),
                    fullscreen::foreground_screen_state(),
                    ui.get_app_notch_auto_hide(),
                );
                match want {
                    fullscreen::BarDisplay::Hidden => {
                        if !autohidden.get() {
                            // Panel yang sedang mengembang ditutup
                            // dulu supaya tidak membeku terbuka saat
                            // bar muncul lagi.
                            w.set_expanded(false);
                            let _ = w.hide();
                            autohidden.set(true);
                        }
                        last_display.set(fullscreen::BarDisplay::Hidden);
                    }
                    _ => {
                        if autohidden.get() {
                            let _ = w.show();
                            autohidden.set(false);
                            if ui.get_app_notch_mode() == "notch" {
                                recenter_notch(w, &ui, false);
                            }
                        }
                        if want != last_display.get() {
                            w.set_bar_display(slint::SharedString::from(match want {
                                fullscreen::BarDisplay::Full => "full",
                                fullscreen::BarDisplay::ThinLine => "line",
                                fullscreen::BarDisplay::Hidden => "full", // tak tercapai
                            }));
                            last_display.set(want);
                        }
                    }
                }
            }
        },
    );

    // Rasio gauge pertama (sebelum tick timer 2 detik pertama).
    update_gauge_ratios(&ui);

    // ---- Trello auto-refresh: timer 1 detik (ala C#
    // DashboardViewModel: DispatcherTimer 1 detik + cek elapsed per
    // list terhadap interval — default 60 detik, field Settings >
    // General "Interval refresh Trello", key DB
    // `Trello.RefreshInterval`; skip bila list masih di-refresh —
    // `if (!IsRefreshing)`). BUKAN realtime (webhook/push sengaja
    // tidak dipakai — bikin PC lag, keputusan abeng 28 Nov 00:30).
    // Tick diam-diam: tidak toast, tidak menyentuh
    // `trello-refreshing` (timer .slint 600ms akan toast bila flag
    // itu disentuh). Sync hanya mengganti array model — sama seperti
    // refresh manual — jadi detail kartu yang terbuka + `selected-card`
    // tidak disentuh. List yang jatuh tempo saja yang di-sync
    // (yang lain dilewat dengan string kosong).
    let _trello_auto_timer = slint::Timer::default();
    _trello_auto_timer.start(
        slint::TimerMode::Repeated,
        std::time::Duration::from_secs(1),
        {
            let ui_weak = ui.as_weak();
            let state = trello_sync_state.clone();
            let bar_store_timer = bar_store.clone();
            move || {
                let Some(ui) = ui_weak.upgrade() else {
                    return;
                };
                let interval = trello_refresh_interval(&ui);
                let k = ui.get_trello_api_key().to_string();
                let t = ui.get_trello_token().to_string();
                if k.is_empty() || t.is_empty() {
                    return;
                }
                let base_url = ui.get_trello_base_url().to_string();
                let el = if state.is_due(0, interval) && !state.is_running(0) {
                    ui.get_editing_list_id().to_string()
                } else {
                    String::new()
                };
                let rl = if state.is_due(1, interval) && !state.is_running(1) {
                    ui.get_revision_list_id().to_string()
                } else {
                    String::new()
                };
                let ll = if state.is_due(2, interval) && !state.is_running(2) {
                    ui.get_late_list_id().to_string()
                } else {
                    String::new()
                };
                if el.is_empty() && rl.is_empty() && ll.is_empty() {
                    return;
                }
                spawn_trello_sync(
                    ui_weak.clone(),
                    k,
                    t,
                    base_url,
                    el,
                    rl,
                    ll,
                    false,
                    bar_store_timer.clone(),
                    state.clone(),
                );
            }
        },
    );

    // ---- Google Sheets LIVE: inbox timer (250 ms) ----
    // Thread fetch/tulis mengirim SheetsInboxMsg lewat channel; timer
    // ini (jalan di thread UI) yang menerapkan ke state Rc<RefCell>
    // + mendorong grid + status. Timeout 45 detik: operasi yang
    // menggantung dibatalkan dengan pesan (saving_live direset, draft
    // dipertahankan).
    let _sheets_inbox_timer = slint::Timer::default();
    _sheets_inbox_timer.start(
        slint::TimerMode::Repeated,
        std::time::Duration::from_millis(250),
        {
            let ui_weak = ui.as_weak();
            let db_inbox = db.clone();
            let state_inbox = sheet_state.clone();
            let sheet_cell_inbox = sheet_win_cell.clone();
            let inbox_inbox = sheets_inbox.clone();
            let pending_inbox = sheets_pending_save.clone();
            let deadline_inbox = sheets_op_deadline.clone();
            let tx_inbox = (*sheets_tx).clone();
            move || {
                // Timeout operasi yang menggantung.
                if let Some(started) = *deadline_inbox.borrow() {
                    if started.elapsed() > std::time::Duration::from_secs(45) {
                        *deadline_inbox.borrow_mut() = None;
                        let mut st = state_inbox.borrow_mut();
                        let was_saving = st.saving_live;
                        st.saving_live = false;
                        *pending_inbox.borrow_mut() = None;
                        if let Some(ui) = ui_weak.upgrade() {
                            let msg = if was_saving {
                                "Penyimpanan ke Google Sheets kehabisan waktu — perubahan masih tersimpan sebagai draft."
                            } else {
                                "Pengambilan data Google Sheets kehabisan waktu — menampilkan data terakhir."
                            };
                            set_sheet_status_both(&ui, &sheet_cell_inbox, msg);
                        }
                    }
                }
                let mut rx_guard = inbox_inbox.borrow_mut();
                let Some(rx) = rx_guard.as_mut() else {
                    return;
                };
                while let Ok(msg) = rx.try_recv() {
                    match msg {
                        SheetsInboxMsg::SyncLive(fetch) => {
                            let Some(ui) = ui_weak.upgrade() else {
                                continue;
                            };
                            let mut st = state_inbox.borrow_mut();
                            if st.saving_live {
                                // Simpan sedang jalan — grid fetch ini
                                // sudah basi dibanding rencana simpan;
                                // buang, tapi POINTS tetap dipakai.
                                drop(st);
                                apply_live_points(&ui, &db_inbox, fetch.points);
                                continue;
                            }
                            let (display, applied, remaining) =
                                apply_live_to_state(&mut st, &db_inbox, &fetch);
                            let status = live_sheet_status(&st, applied, &remaining);
                            push_sheet_grids_both(&ui, &sheet_cell_inbox, &st, &display);
                            set_sheet_status_both(&ui, &sheet_cell_inbox, &status);
                            apply_live_points(&ui, &db_inbox, fetch.points);
                            *deadline_inbox.borrow_mut() = None;
                        }
                        SheetsInboxMsg::SyncFailed(e) => {
                            let Some(ui) = ui_weak.upgrade() else {
                                continue;
                            };
                            set_sheet_status_both(
                                &ui,
                                &sheet_cell_inbox,
                                &format!(
                                    "Gagal mengambil data live dari Google Sheets: {} — menampilkan data terakhir.",
                                    e
                                ),
                            );
                            *deadline_inbox.borrow_mut() = None;
                        }
                        SheetsInboxMsg::SyncOffline => {
                            let Some(ui) = ui_weak.upgrade() else {
                                continue;
                            };
                            let mut st = state_inbox.borrow_mut();
                            st.live = false; // kolom dipertahankan
                            let status = offline_sheet_status(&st);
                            drop(st);
                            set_sheet_status_both(&ui, &sheet_cell_inbox, &status);
                            *deadline_inbox.borrow_mut() = None;
                        }
                        SheetsInboxMsg::SaveFresh {
                            live,
                            range_start_row,
                            merged,
                        } => {
                            let Some(ui) = ui_weak.upgrade() else {
                                continue;
                            };
                            let mut st = state_inbox.borrow_mut();
                            // Guard ANTI-MENCLOK terhadap data live terbaru.
                            if let Some(block_msg) = st.check_save(&live.rows) {
                                st.saving_live = false;
                                *deadline_inbox.borrow_mut() = None;
                                drop(st);
                                set_sheet_guard_both(&ui, &sheet_cell_inbox, &block_msg);
                                // Guard saja tidak cukup: guard-result hanya
                                // dibaca commit-save() secara sinkron, jadi
                                // blokir async ini TIDAK terlihat kalau
                                // status bar tidak ikut ditulis (28 Nov
                                // 2026: status macet di "Menyimpan…").
                                set_sheet_status_both(&ui, &sheet_cell_inbox, &block_msg);
                                continue;
                            }
                            match compute_live_save(&st, &live, &merged, range_start_row) {
                                Err(block_msg) => {
                                    st.saving_live = false;
                                    *deadline_inbox.borrow_mut() = None;
                                    drop(st);
                                    set_sheet_guard_both(&ui, &sheet_cell_inbox, &block_msg);
                                    set_sheet_status_both(&ui, &sheet_cell_inbox, &block_msg);
                                }
                                Ok(plan) => {
                                    if plan.updates.is_empty() && plan.appends.is_empty() {
                                        // Tidak ada yang perlu di-push — tapi
                                        // data live segar SUDAH di tangan:
                                        // terapkan supaya baris yang
                                        // disisipkan dari luar ikut terlihat
                                        // (jangan commit_save(merged) — itu
                                        // malah menimpa loaded dengan grid
                                        // basi).
                                        let fetch = LiveFetch {
                                            first_sheet_row: range_start_row
                                                + live.header_row as i32
                                                + 1,
                                            live,
                                            points: None,
                                        };
                                        let (display, _applied, _remaining) =
                                            apply_live_to_state(
                                                &mut st, &db_inbox, &fetch,
                                            );
                                        push_sheet_grids_both(
                                            &ui,
                                            &sheet_cell_inbox,
                                            &st,
                                            &display,
                                        );
                                        set_sheet_guard_both(&ui, &sheet_cell_inbox, "");
                                        set_sheet_status_both(
                                            &ui,
                                            &sheet_cell_inbox,
                                            "✓ Tidak ada perubahan untuk disimpan",
                                        );
                                        st.saving_live = false;
                                        *deadline_inbox.borrow_mut() = None;
                                    } else {
                                        *pending_inbox.borrow_mut() = Some(PendingLiveSave {
                                            merged: merged.clone(),
                                            draft_snapshot: plan.draft_snapshot.clone(),
                                        });
                                        set_sheet_status_both(
                                            &ui,
                                            &sheet_cell_inbox,
                                            "Menyimpan ke Google Sheets…",
                                        );
                                        request_live_push(
                                            db_inbox.clone(),
                                            tx_inbox.clone(),
                                            plan,
                                        );
                                    }
                                }
                            }
                        }
                        SheetsInboxMsg::SaveFreshFailed(e) => {
                            let Some(ui) = ui_weak.upgrade() else {
                                continue;
                            };
                            let mut st = state_inbox.borrow_mut();
                            st.saving_live = false;
                            *deadline_inbox.borrow_mut() = None;
                            drop(st);
                            let msg = format!(
                                "Gagal membaca data terbaru dari Google Sheets: {} — simpan dibatalkan, perubahan masih tersimpan sebagai draft.",
                                e
                            );
                            set_sheet_guard_both(&ui, &sheet_cell_inbox, &msg);
                            // Sama seperti blokir guard: status bar harus
                            // ikut ditulis, kalau tidak pesan ini tidak
                            // terlihat (guard-result hanya dibaca sinkron).
                            set_sheet_status_both(&ui, &sheet_cell_inbox, &msg);
                        }
                        SheetsInboxMsg::SavePushed => {
                            let Some(ui) = ui_weak.upgrade() else {
                                continue;
                            };
                            let Some(pending) = pending_inbox.borrow_mut().take() else {
                                continue;
                            };
                            let mut st = state_inbox.borrow_mut();
                            // Draft yang masuk SELAMA flight dipertahankan:
                            // commit dulu, lalu rekam ulang dengan nilai
                            // asli = nilai sel saat ini (supaya
                            // apply_drafts tetap cocok).
                            let current_drafts = st.drafts.clone();
                            st.commit_save(pending.merged.clone());
                            persist_sheet_snapshot(
                                &db_inbox,
                                &spreadsheet_guard::SheetSnapshot {
                                    sheet_id: st.sheet_id.clone(),
                                    sheet_name: st.sheet_name.clone(),
                                    rows: pending.merged.clone(),
                                    headers: st.headers.clone(),
                                    key_column: st.key_column,
                                    first_sheet_row: st.first_sheet_row,
                                },
                            );
                            for d in &current_drafts {
                                let was_pushed = pending.draft_snapshot.iter().any(|s| {
                                    s.row_key == d.row_key
                                        && s.column_index == d.column_index
                                        && s.sheet_row_number == d.sheet_row_number
                                        && s.value == d.value
                                });
                                if was_pushed {
                                    continue;
                                }
                                let row0 = find_row_by_key(&st.loaded, st.key_column, &d.row_key)
                                    .or_else(|| {
                                        let idx = d.sheet_row_number - st.first_sheet_row;
                                        if idx >= 0 && (idx as usize) < st.loaded.len() {
                                            Some(idx as usize)
                                        } else {
                                            None
                                        }
                                    });
                                if let Some(r) = row0 {
                                    let orig = st
                                        .loaded
                                        .get(r)
                                        .and_then(|row| row.get(d.column_index as usize))
                                        .cloned()
                                        .unwrap_or_default();
                                    st.record_edit(r, d.column_index as usize, &orig, &d.value);
                                }
                            }
                            persist_sheet_drafts(&db_inbox, &st.drafts);
                            let display = sheet_display_rows(&st);
                            push_sheet_grids_both(&ui, &sheet_cell_inbox, &st, &display);
                            set_sheet_guard_both(&ui, &sheet_cell_inbox, "");
                            let late_n = st.drafts.len();
                            let status = if late_n > 0 {
                                format!(
                                    "✓ Semua perubahan tersimpan ke Google Sheets & SQLite ({} perubahan baru masih tersimpan sebagai draft)",
                                    late_n
                                )
                            } else {
                                "✓ Semua perubahan tersimpan ke Google Sheets & SQLite"
                                    .to_string()
                            };
                            set_sheet_status_both(&ui, &sheet_cell_inbox, &status);
                            st.saving_live = false;
                            *deadline_inbox.borrow_mut() = None;
                        }
                        SheetsInboxMsg::SavePushFailed(e) => {
                            let Some(ui) = ui_weak.upgrade() else {
                                continue;
                            };
                            let mut st = state_inbox.borrow_mut();
                            st.saving_live = false;
                            *deadline_inbox.borrow_mut() = None;
                            *pending_inbox.borrow_mut() = None;
                            drop(st);
                            let msg = format!(
                                "Gagal menyimpan ke Google Sheets: {} — perubahan masih tersimpan sebagai draft.",
                                e
                            );
                            set_sheet_guard_both(&ui, &sheet_cell_inbox, &msg);
                            set_sheet_status_both(&ui, &sheet_cell_inbox, &msg);
                        }
                    }
                }
            }
        },
    );

    // ---- Mode pet: idle wander HALUS (SYARAT TEGAS abeng) ----
    // Timer 120 ms menggeser avatar beberapa px per tick menuju
    // satu titik target (ease-out: 8% sisa jarak, 1–5 px/tick)
    // — gerakan meluncur, BUKAN teleport tiap 2 detik seperti
    // wander lama. Target baru diambil tiap 4–8 detik / saat
    // tercapai, selalu di dalam LAYAR (bukan window utama —
    // diubah 27 Nov 2026, pet melayang di atas semua window).
    // Tidak jalan saat di-hover, saat panel mengembang, atau
    // 5 detik setelah di-drag. Mode notch tidak wander.
    let pet_wander_target: std::rc::Rc<std::cell::RefCell<Option<(i32, i32)>>> =
        std::rc::Rc::new(std::cell::RefCell::new(None));
    let pet_wander_deadline: std::rc::Rc<std::cell::RefCell<Instant>> =
        std::rc::Rc::new(std::cell::RefCell::new(Instant::now()));
    // Bounds wander (min_x, min_y, max_x, max_y, physical px) —
    // di-refresh hanya saat target baru dipilih, supaya tidak
    // buka koneksi X tiap tick 120 ms.
    let pet_wander_bounds: std::rc::Rc<std::cell::RefCell<Option<(i32, i32, i32, i32)>>> =
        std::rc::Rc::new(std::cell::RefCell::new(None));
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
            let bounds_cell = pet_wander_bounds.clone();
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
                let scale = w.window().scale_factor();
                if scale <= 0.0 {
                    return;
                }
                let pet = (84f32 * scale) as i32;
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
                    // Bounds = LAYAR; fallback: bounds terakhir /
                    // window utama bila ukuran layar tak terbaca.
                    let bounds = match screen_size() {
                        Some((sw, sh)) => {
                            (0, 0, (sw - pet).max(0), (sh - pet).max(0))
                        }
                        None => match *bounds_cell.borrow() {
                            Some(b) => b,
                            None => {
                                let win = ui.window();
                                let mpos = win.position();
                                let msize = win.size();
                                let min_y = mpos.y + (28f32 * scale) as i32;
                                (
                                    mpos.x,
                                    min_y,
                                    (mpos.x + msize.width as i32 - pet).max(mpos.x),
                                    (mpos.y + msize.height as i32 - pet).max(min_y),
                                )
                            }
                        },
                    };
                    *bounds_cell.borrow_mut() = Some(bounds);
                    let (min_x, min_y, max_x, max_y) = bounds;
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

    // BATCH executor: kuras channel log run tiap 200ms (pola sama
    // seperti _sheets_inbox_timer). Setelah run berakhir: refresh OUTPUT
    // (existence), has-manifest bila buat_master sukses, toast
    // "[SUCCESS] Batch Selesai.".
    let _batch_exec_timer = slint::Timer::default();
    _batch_exec_timer.start(
        slint::TimerMode::Repeated,
        std::time::Duration::from_millis(200),
        {
            let ui_weak = ui.as_weak();
            let handle = batch_run_handle.clone();
            let st = batch_state.clone();
            let pl = proc_log.clone();
            let task_cell = task_win.clone();
            let batch_c = batch_win_cell.clone();
            move || {
                let Some(ui) = ui_weak.upgrade() else {
                    return;
                };
                let mut rx_guard = handle.rx.lock().unwrap();
                let Some(rx) = rx_guard.as_mut() else {
                    return;
                };
                let mut changed = false;
                let mut finished: Option<bool> = None; // Some(true)=AllDone, Some(false)=Stopped
                while let Ok(msg) = rx.try_recv() {
                    match msg {
                        batch_exec::BatchExecMsg::Line(l) => {
                            pl.lock().unwrap().add_log(l.clone());
                            // Task Manager (tahap 5): baris log hidup
                            // masuk ke detail tugas aktif.
                            let mut s = st.lock().unwrap();
                            if let Some(t) = s.active_task_mut() {
                                t.append_line(&l);
                                push_tasks_to_ui(&ui, &s);
                                push_tasks_to_panel(&task_cell, &s);
                            }
                            changed = true;
                        }
                        batch_exec::BatchExecMsg::RootDone { root, code } => {
                            let mut lg = pl.lock().unwrap();
                            if code == 0 {
                                lg.add_log(format!("[INFO] Selesai: {}", root));
                            } else {
                                lg.add_log(format!("[ERROR] Gagal (exit {}): {}", code, root));
                            }
                            // Task Manager: fase root ini selesai.
                            let mut s = st.lock().unwrap();
                            if let Some(t) = s.active_task_mut() {
                                t.set_phase_by_root(
                                    &root,
                                    if code == 0 {
                                        batch::PhaseState::Done
                                    } else {
                                        batch::PhaseState::Failed
                                    },
                                );
                                push_tasks_to_ui(&ui, &s);
                                push_tasks_to_panel(&task_cell, &s);
                            }
                            changed = true;
                        }
                        batch_exec::BatchExecMsg::Error(e) => {
                            pl.lock().unwrap().add_log(format!("[ERROR] {}", e));
                            // Task Manager: tugas gagal (AllDone yang
                            // datang sesudahnya tidak menimpa status
                            // ini — lihat penjaga `Failed` di bawah).
                            let mut s = st.lock().unwrap();
                            if let Some(t) = s.active_task_mut() {
                                t.state = batch::TaskState::Failed;
                                t.append_line(&e);
                                push_tasks_to_ui(&ui, &s);
                                push_tasks_to_panel(&task_cell, &s);
                            }
                            changed = true;
                        }
                        batch_exec::BatchExecMsg::AllDone => {
                            finished = Some(true);
                        }
                        batch_exec::BatchExecMsg::Stopped => {
                            finished = Some(false);
                        }
                    }
                }
                if changed {
                    push_log_lines(&ui, &pl);
                }
                if let Some(ok) = finished {
                    {
                        let mut s = st.lock().unwrap();
                        batch::rescan_all(&mut s.roots);
                        if ok && !handle.failed.load(Ordering::SeqCst) {
                            let script = handle.script_file.lock().unwrap().clone();
                            if script.to_ascii_lowercase().contains("buat_master") {
                                s.has_manifest = true;
                            }
                        }
                        // Task Manager (tahap 5): selesaikan tugas
                        // aktif — atau masuk "Menunggu save" bila ini
                        // Fase 1 (buat_master, bukan duplikat) dan ada
                        // Fase 2 yang harus jalan setelah Photoshop
                        // selesai menyimpan.
                        if let Some(t) = s.active_task_mut() {
                            let title = t.title.to_ascii_lowercase();
                            let is_fase1 =
                                title.contains("buat_master") && !title.contains("duplikat");
                            let failed_before = t.state == batch::TaskState::Failed;
                            if ok && is_fase1 && !failed_before {
                                t.state = batch::TaskState::WaitingSave;
                                t.followups = vec!["BUAT MASTER (duplikat)".to_string()];
                                for p in t.phases.iter_mut() {
                                    if p.state == batch::PhaseState::Running {
                                        p.state = batch::PhaseState::Done;
                                    }
                                }
                            } else {
                                t.state = if ok && !failed_before {
                                    batch::TaskState::Done
                                } else {
                                    batch::TaskState::Failed
                                };
                                for p in t.phases.iter_mut() {
                                    if p.state == batch::PhaseState::Running {
                                        p.state = if ok && !failed_before {
                                            batch::PhaseState::Done
                                        } else {
                                            batch::PhaseState::Failed
                                        };
                                    }
                                }
                                // Fase 2 selesai → tugas Fase 1 yang
                                // dilanjutkan ikut selesai (atau kembali
                                // menunggu save bila Fase 2 dibatalkan).
                                // Tugas itu dikenali dari fase
                                // "BUAT MASTER (duplikat)": saat duplikat
                                // dimulai, batch_start_run mengubahnya
                                // jadi Running + menambah fase itu — jadi
                                // state WaitingSave sudah tidak ada dan
                                // pencarian WaitingSave tidak pernah
                                // cocok (bug: tugas Fase 1 macet
                                // "Berjalan" selamanya).
                                if title.contains("duplikat") {
                                    for other in s.tasks.iter_mut() {
                                        // Hanya tugas yang masih berjalan
                                        // (jangan bangkitkan yang sudah
                                        // Dibatalkan user saat Fase 2 jalan).
                                        if other.state != batch::TaskState::Running {
                                            continue;
                                        }
                                        let has_dup_phase = other.phases.iter().any(|p| {
                                            p.label == "BUAT MASTER (duplikat)"
                                        });
                                        if !has_dup_phase {
                                            continue;
                                        }
                                        if !ok {
                                            // Dibatalkan: kembali ke posisi
                                            // sebelum Fase 2 — menunggu save,
                                            // follow-up utuh lagi.
                                            other.state = batch::TaskState::WaitingSave;
                                            other.followups =
                                                vec!["BUAT MASTER (duplikat)".to_string()];
                                            other.phases.retain(|p| {
                                                p.label != "BUAT MASTER (duplikat)"
                                            });
                                        } else if !failed_before {
                                            other.state = batch::TaskState::Done;
                                            other.followups.clear();
                                            for p in other.phases.iter_mut() {
                                                if p.state == batch::PhaseState::Running
                                                    || p.state == batch::PhaseState::Pending
                                                {
                                                    p.state = batch::PhaseState::Done;
                                                }
                                            }
                                        } else {
                                            other.state = batch::TaskState::Failed;
                                            for p in other.phases.iter_mut() {
                                                if p.label == "BUAT MASTER (duplikat)" {
                                                    p.state = batch::PhaseState::Failed;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        if !ok {
                            if let Some(t) = s.active_task_mut() {
                                t.state = batch::TaskState::Cancelled;
                            }
                        }
                    }
                    push_batch_to_ui(&ui, &st.lock().unwrap(), &batch_c);
                    push_tasks_to_panel(&task_cell, &st.lock().unwrap());
                    ui.set_batch_running(false);
                    sync_batch_to_open_window(&batch_c, &ui);
                    let toast = if ok {
                        if handle.failed.load(Ordering::SeqCst) {
                            "[SUCCESS] Batch Selesai (ada folder yang gagal — cek log)."
                        } else {
                            "[SUCCESS] Batch Selesai."
                        }
                    } else {
                        "[WARNING] Proses dihentikan oleh User."
                    };
                    ui.set_toast(slint::SharedString::from(toast));
                    pl.lock().unwrap().add_log(toast.to_string());
                    push_log_lines(&ui, &pl);
                }
            }
        },
    );

    // Folder Reguler: kuras hasil scan latar tiap 250ms (pola
    // _batch_exec_timer). Hasil scan masuk ke daftar BATCH + popup
    // Kelola; bulan dipilih ulang dari hasil scan. Scan penuh yang
    // selesai juga menyimpan cache (DB) untuk buka instan berikutnya.
    let _reguler_scan_timer = slint::Timer::default();
    _reguler_scan_timer.start(
        slint::TimerMode::Repeated,
        std::time::Duration::from_millis(250),
        {
            let ui_weak = ui.as_weak();
            let st = batch_state.clone();
            let scan = reguler_scan_state.clone();
            let rxh = reguler_scan_rx.clone();
            let handle = batch_run_handle.clone();
            let batch_c = batch_win_cell.clone();
            let db_c = db.clone();
            move || {
                let Some(ui) = ui_weak.upgrade() else {
                    return;
                };
                // Hasil scan didorong SELALU (bukan hanya saat popup
                // Kelola terbuka): daftar Folder Reguler di tampilan
                // awal BATCH membaca baris yang sama (28 Nov 2026).
                let mut rx_guard = rxh.lock().unwrap();
                let Some(rx) = rx_guard.as_mut() else {
                    return;
                };
                let mut got: Option<(String, Vec<batch::RegulerRow>)> = None;
                while let Ok(msg) = rx.try_recv() {
                    got = Some(msg);
                }
                drop(rx_guard);
                if let Some((dir, rows)) = got {
                    let mut sc = scan.lock().unwrap();
                    // Abaikan hasil scan lama bila pengguna sudah pindah
                    // direktori sementara scan lama masih berjalan.
                    if sc.dir != dir {
                        return;
                    }
                    sc.all = rows.clone();
                    sc.scanning = false;
                    // Simpan cache scan penuh (buka instan berikutnya).
                    let cache = batch::RegulerCache {
                        dir: dir.clone(),
                        scanned_at: batch::now_unix(),
                        months: batch::reguler_month_fingerprints(std::path::Path::new(&dir)),
                        rows,
                    };
                    eprintln!(
                        "[reguler-scan] {}: scan penuh selesai, {} baris, {} bulan — cache disimpan",
                        dir,
                        cache.rows.len(),
                        cache.months.len()
                    );
                    save_reguler_cache(&db_c, &cache);
                    let run_active = handle.is_running();
                    push_reguler_to_ui(&ui, &st.lock().unwrap(), &sc, run_active, &batch_c);
                }
            }
        },
    );

    // ui.run() mengambil alih event loop; show() sudah dipanggil di atas.
    ui.run()?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn range_start_row_parsing() {
        assert_eq!(range_start_row("A1:Z"), 1);
        assert_eq!(range_start_row("A3:Z"), 3);
        assert_eq!(range_start_row("ALL DATA REGULER!A3:Z"), 3);
        assert_eq!(range_start_row("EDITOR HARIAN!J14"), 14);
        assert_eq!(range_start_row(""), 1);
    }

    #[test]
    fn gauge_stat_num_parsing() {
        assert_eq!(parse_stat_num("24"), 24.0);
        assert_eq!(parse_stat_num("4.180"), 4180.0);
        assert_eq!(parse_stat_num("0"), 0.0);
        assert_eq!(parse_stat_num("-"), 0.0);
        assert_eq!(parse_stat_num(""), 0.0);
        assert_eq!(parse_stat_num(" 12 "), 12.0);
    }

    #[test]
    fn gauge_arc_commands_follows_ellipse() {
        // Ujung busur = titik pada elips (50+50cos, 40-28sin), sudut
        // theta = (1-rasio)*180 derajat — rumus SAMA dengan .slint
        // (gauge-theta/gauge-end-x/gauge-end-y di StatWidget).
        // Ujung-ke-ujung: x=0 (tepi kiri) .. x=100 (tepi kanan).
        assert_eq!(gauge_arc_commands(0.0), "M 0 40 A 50 28 0 0 1 0.00 40.00");
        assert_eq!(gauge_arc_commands(1.0), "M 0 40 A 50 28 0 0 1 100.00 40.00");
        assert_eq!(gauge_arc_commands(0.4), "M 0 40 A 50 28 0 0 1 34.55 13.37");
        assert_eq!(gauge_arc_commands(0.72), "M 0 40 A 50 28 0 0 1 81.87 18.43");
        // Clamp di luar 0..1.
        assert_eq!(gauge_arc_commands(-0.5), gauge_arc_commands(0.0));
        assert_eq!(gauge_arc_commands(1.5), gauge_arc_commands(1.0));
    }

    #[test]
    fn find_row_by_key_uses_key_not_position() {
        let rows = vec![
            vec!["6CCC001".to_string(), "A".into()],
            vec!["6CCC002".to_string(), "B".into()],
        ];
        assert_eq!(find_row_by_key(&rows, Some(0), "6CCC002"), Some(1));
        assert_eq!(find_row_by_key(&rows, Some(0), "6CCC999"), None);
        assert_eq!(find_row_by_key(&rows, Some(0), ""), None);
        assert_eq!(find_row_by_key(&rows, None, "6CCC001"), None);
    }

    fn live_sheet_adr() -> sheets_api::LiveSheet {
        // Header baris 3 (indeks 2), 2 kolom: NO FAKTUR + EDITOR.
        let values = vec![
            vec!["PT DELAPAN MATA AIR".into(), "".into()],
            vec!["".into(), "".into()],
            vec!["NO FAKTUR".into(), "EDITOR".into()],
            vec!["6CCC001".into(), "NISSA".into()],
            vec!["6CCC002".into(), "CAESAR".into()],
        ];
        sheets_api::parse_live_sheet(values, &std::collections::HashMap::new())
    }

    fn state_with_draft() -> spreadsheet_guard::SpreadsheetState {
        let loaded = vec![
            vec!["6CCC001".into(), "NISSA".into()],
            vec!["6CCC002".into(), "CAESAR".into()],
        ];
        let mut st = spreadsheet_guard::SpreadsheetState::new(
            "id".into(),
            "ALL DATA REGULER".into(),
            4,
            2,
            loaded,
            Some(0),
        );
        // Edit EDITOR baris 6CCC002 -> TESTER (draft berkunci).
        st.record_edit(1, 1, "CAESAR", "TESTER");
        st
    }

    #[test]
    fn compute_live_save_update_lands_on_key_after_insert_above() {
        let st = state_with_draft();
        let mut live = live_sheet_adr();
        // Sisip satu baris DI ATAS data (baris sheet 4) — seperti order
        // baru masuk sementara aplikasi tidak update.
        live.rows.insert(0, vec!["6CCC000".into(), "BARU".into()]);
        let merged = vec![
            vec!["6CCC001".into(), "NISSA".into()],
            vec!["6CCC002".into(), "TESTER".into()],
        ];
        let plan = compute_live_save(&st, &live, &merged, 1).expect("rencana harus jadi");
        // Baris 6CCC002 kini di baris sheet 6 (4 + 2), BUKAN baris 5.
        assert_eq!(
            plan.updates,
            vec![("ALL DATA REGULER!B6".to_string(), "TESTER".to_string())]
        );
        assert!(plan.appends.is_empty());
    }

    #[test]
    fn compute_live_save_blocks_new_row_without_faktur() {
        let st = state_with_draft();
        let live = live_sheet_adr();
        let merged = vec![
            vec!["6CCC001".into(), "NISSA".into()],
            vec!["6CCC002".into(), "TESTER".into()],
            vec!["".into(), "EDITOR BARU".into()], // baris baru tanpa NO FAKTUR
        ];
        let err = compute_live_save(&st, &live, &merged, 1).expect_err("harus diblokir");
        assert!(err.contains("NO FAKTUR"), "pesan: {}", err);
    }

    #[test]
    fn compute_live_save_appends_new_row_with_key() {
        let st = state_with_draft();
        let live = live_sheet_adr();
        let merged = vec![
            vec!["6CCC001".into(), "NISSA".into()],
            vec!["6CCC002".into(), "TESTER".into()],
            vec!["6CCC003".into(), "NISSA".into()], // baris baru berkunci
        ];
        let plan = compute_live_save(&st, &live, &merged, 1).expect("rencana harus jadi");
        assert_eq!(plan.appends.len(), 1);
        assert_eq!(plan.appends[0][0], "6CCC003");
    }

    #[test]
    fn compute_live_save_blocks_missing_key_row() {
        let st = state_with_draft();
        let mut live = live_sheet_adr();
        live.rows.retain(|r| r[0] != "6CCC002"); // baris dihapus dari luar
        let merged = vec![
            vec!["6CCC001".into(), "NISSA".into()],
            vec!["6CCC002".into(), "TESTER".into()],
        ];
        let err = compute_live_save(&st, &live, &merged, 1).expect_err("harus diblokir");
        assert!(err.contains("6CCC002"), "pesan: {}", err);
    }
}
