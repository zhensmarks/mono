//! Deteksi window lain di layar untuk Auto-hide Dynamic Island
//! (ref layzee / Dynanotch, abeng 28 Nov 11:58–11:59).
//!
//! Tiga tingkat tampilan notch (mode Notch):
//! - Normal (tidak ada window maksimal/fullscreen) → pill penuh 150×32.
//! - Window lain MAKSIMAL (mis. browser) → garis tipis 4px di tengah
//!   atas — tidak nutupin tombol close tab.
//! - Window lain FULLSCREEN (video / game) → notch HILANG TOTAL dan
//!   tidak muncul walau mouse digerakkan ke atas; muncul lagi saat
//!   keluar fullscreen.
//! Mode Avatar Pet: maksimal → pet tetap tampil (blob tidak duduk di
//! tab bar); fullscreen → hilang juga (blob di atas game sama
//! mengganggunya).
//!
//! Deteksi:
//! - Windows: Win32 mentah (`GetForegroundWindow` + `GetWindowRect`
//!   vs ukuran layar + work area) lewat FFI `extern "C"` — TANPA
//!   crate baru (`windows-sys` tidak ada di cache offline).
//! - Linux: x11rb — `_NET_ACTIVE_WINDOW` + `_NET_WM_STATE`
//!   (FULLSCREEN / MAXIMIZED_VERT+HORZ) bila ada window manager;
//!   fallback geometri (jendela viewable terbesar) bila tidak ada WM
//!   (mis. Xvfb saat verifikasi).
//!
//! BATAS YANG JUJUR (bukan klaim): pemicu tingkat 2 adalah "window
//! foreground MAKSIMAL", BUKAN "video sedang diputar". Deteksi
//! "video sedang diputar" yang sebenarnya butuh Windows SMTC
//! (GlobalSystemMediaTransportControlsSessionManager, WinRT/COM) —
//! tidak diimplementasikan: plumbing COM/WinRT mentah tidak bisa
//! diuji dari VM Linux ini, dan `windows` crate tidak ada di cache
//! offline. Konsekuensinya: browser berjendela (tidak maksimal)
//! yang memutar video tetap menampilkan pill penuh, dan aplikasi
//! maksimal apa pun (bukan hanya browser) mendapat garis tipis.
//! Bila abeng minta deteksi video beneran, itu pekerjaan SMTC di
//! PC Windows asli (lihat "Masih terbuka" di PERUBAHAN.md).

/// Keadaan window foreground (aplikasi lain) terhadap layar.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ScreenState {
    /// Tidak ada window maksimal/fullscreen.
    Normal,
    /// Window foreground menutupi work area (layar minus taskbar)
    /// tapi bukan fullscreen — mis. browser maksimal.
    Maximized,
    /// Window foreground menutupi SELURUH layar — video/game
    /// fullscreen.
    Fullscreen,
}

/// Tampilan notch yang diinginkan.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum BarDisplay {
    /// Pill penuh (150×32 notch / blob 84×84 pet).
    Full,
    /// Garis tipis 4px (hanya mode notch).
    ThinLine,
    /// Window bar disembunyikan total (Rust `w.hide()`).
    Hidden,
}

/// Aturan tampilan: fungsi murni (di-unit-test).
/// - `auto_hide` mati → selalu pill penuh (perilaku lama).
/// - Fullscreen → hilang, apa pun modenya.
/// - Maksimal → garis tipis (notch) / tetap tampil (pet).
/// - Normal → pill penuh.
pub fn decide_bar_display(mode: &str, state: ScreenState, auto_hide: bool) -> BarDisplay {
    if !auto_hide {
        return BarDisplay::Full;
    }
    match state {
        ScreenState::Fullscreen => BarDisplay::Hidden,
        ScreenState::Maximized => {
            if mode == "pet" {
                BarDisplay::Full
            } else {
                BarDisplay::ThinLine
            }
        }
        ScreenState::Normal => BarDisplay::Full,
    }
}

/// Klasifikasi rect window foreground (logika Windows, fungsi
/// murni — di-unit-test).
/// - Fullscreen: rect menutupi SELURUH layar.
/// - Maksimal: rect menutupi work area (boleh sedikit lebih besar —
///   jendela maksimal Win10/11 punya frame tak terlihat ~8px di
///   tiap sisi, jadi dicek "menutupi", bukan "sama persis").
/// - Urutan penting: fullscreen dicek dulu (fullscreen juga
///   menutupi work area).
pub fn classify_rect(
    fg: (i32, i32, i32, i32),
    screen: (i32, i32),
    work: Option<(i32, i32, i32, i32)>,
) -> ScreenState {
    let (l, t, r, b) = fg;
    let (sw, sh) = screen;
    if l <= 0 && t <= 0 && r >= sw && b >= sh {
        return ScreenState::Fullscreen;
    }
    if let Some((wl, wt, wr, wb)) = work {
        if l <= wl && t <= wt && r >= wr && b >= wb {
            return ScreenState::Maximized;
        }
    }
    ScreenState::Normal
}

/// Klasifikasi dari ukuran jendela vs layar (logika fallback
/// geometri Linux, fungsi murni — di-unit-test).
/// - Fullscreen: selebar + setinggi layar.
/// - Maksimal: selebar layar + setinggi work area (layar minus
///   taskbar/dock, toleransi 80px).
pub fn classify_coverage(w: i32, h: i32, sw: i32, sh: i32) -> ScreenState {
    if w >= sw && h >= sh {
        return ScreenState::Fullscreen;
    }
    if w >= sw && h >= sh - 80 {
        return ScreenState::Maximized;
    }
    ScreenState::Normal
}

/// Keadaan window foreground saat ini. Gagal baca (tidak ada
/// display, tidak ada window aktif, dst.) → Normal (fail safe:
/// notch tetap tampil).
pub fn foreground_screen_state() -> ScreenState {
    #[cfg(target_os = "windows")]
    {
        imp::foreground_screen_state()
    }
    #[cfg(target_os = "linux")]
    {
        imp::foreground_screen_state()
    }
    #[cfg(not(any(target_os = "windows", target_os = "linux")))]
    {
        ScreenState::Normal
    }
}

#[cfg(target_os = "windows")]
mod imp {
    use super::{classify_rect, ScreenState};
    use std::ffi::c_void;

    #[repr(C)]
    #[derive(Clone, Copy, Default)]
    struct Rect {
        left: i32,
        top: i32,
        right: i32,
        bottom: i32,
    }

    #[link(name = "user32")]
    extern "C" {
        fn GetForegroundWindow() -> *mut c_void;
        fn GetWindowRect(hwnd: *mut c_void, rect: *mut Rect) -> i32;
        fn GetSystemMetrics(index: i32) -> i32;
        fn SystemParametersInfoW(action: u32, param: u32, rect: *mut Rect, winini: u32) -> i32;
    }

    const SM_CXSCREEN: i32 = 0;
    const SM_CYSCREEN: i32 = 1;
    const SPI_GETWORKAREA: u32 = 0x0030;

    pub fn foreground_screen_state() -> ScreenState {
        unsafe {
            let hwnd = GetForegroundWindow();
            if hwnd.is_null() {
                return ScreenState::Normal;
            }
            let mut r = Rect::default();
            if GetWindowRect(hwnd, &mut r) == 0 {
                return ScreenState::Normal;
            }
            let sw = GetSystemMetrics(SM_CXSCREEN);
            let sh = GetSystemMetrics(SM_CYSCREEN);
            if sw <= 0 || sh <= 0 {
                return ScreenState::Normal;
            }
            classify_rect(
                (r.left, r.top, r.right, r.bottom),
                (sw, sh),
                work_area(),
            )
        }
    }

    fn work_area() -> Option<(i32, i32, i32, i32)> {
        unsafe {
            let mut r = Rect::default();
            if SystemParametersInfoW(SPI_GETWORKAREA, 0, &mut r, 0) == 0 {
                return None;
            }
            Some((r.left, r.top, r.right, r.bottom))
        }
    }
}

#[cfg(target_os = "linux")]
mod imp {
    use super::{classify_coverage, ScreenState};
    use x11rb::connection::Connection;
    use x11rb::protocol::xproto::*;
    use x11rb::rust_connection::RustConnection;

    pub fn foreground_screen_state() -> ScreenState {
        let Ok((conn, screen_num)) = RustConnection::connect(None) else {
            return ScreenState::Normal;
        };
        let screen = &conn.setup().roots[screen_num];
        let root = screen.root;
        let (sw, sh) = (
            screen.width_in_pixels as i32,
            screen.height_in_pixels as i32,
        );
        // 1) Jendela aktif + state WM-nya (bila ada window manager).
        if let Some(state) = active_window_state(&conn, root, sw, sh) {
            return state;
        }
        // 2) Fallback tanpa WM (mis. Xvfb saat verifikasi): jendela
        // viewable terbesar — menutupi layar = fullscreen, menutupi
        // work area = maksimal.
        largest_window_state(&conn, root, sw, sh)
    }

    fn active_window_state(
        conn: &RustConnection,
        root: Window,
        sw: i32,
        sh: i32,
    ) -> Option<ScreenState> {
        let net_active = conn
            .intern_atom(false, b"_NET_ACTIVE_WINDOW")
            .ok()?
            .reply()
            .ok()?
            .atom;
        let prop = conn
            .get_property(false, root, net_active, AtomEnum::WINDOW, 0, 1)
            .ok()?
            .reply()
            .ok()?;
        let win = prop.value32()?.next()?;
        if win == 0 {
            return None;
        }
        let net_state = conn
            .intern_atom(false, b"_NET_WM_STATE")
            .ok()?
            .reply()
            .ok()?
            .atom;
        let fs = conn
            .intern_atom(false, b"_NET_WM_STATE_FULLSCREEN")
            .ok()?
            .reply()
            .ok()?
            .atom;
        let mv = conn
            .intern_atom(false, b"_NET_WM_STATE_MAXIMIZED_VERT")
            .ok()?
            .reply()
            .ok()?
            .atom;
        let mh = conn
            .intern_atom(false, b"_NET_WM_STATE_MAXIMIZED_HORZ")
            .ok()?
            .reply()
            .ok()?
            .atom;
        let states = conn
            .get_property(false, win, net_state, AtomEnum::ATOM, 0, 32)
            .ok()?
            .reply()
            .ok()?;
        let list: Vec<u32> = states.value32().map(|v| v.collect()).unwrap_or_default();
        if list.contains(&fs) {
            return Some(ScreenState::Fullscreen);
        }
        if list.contains(&mv) && list.contains(&mh) {
            return Some(ScreenState::Maximized);
        }
        // Jendela aktif ada tapi tanpa state WM yang dikenal →
        // klasifikasikan dari geometrinya.
        let g = conn.get_geometry(win).ok()?.reply().ok()?;
        Some(classify_coverage(g.width as i32, g.height as i32, sw, sh))
    }

    fn largest_window_state(
        conn: &RustConnection,
        root: Window,
        sw: i32,
        sh: i32,
    ) -> ScreenState {
        let Some(tree) = conn
            .query_tree(root)
            .ok()
            .and_then(|c| c.reply().ok())
        else {
            return ScreenState::Normal;
        };
        let mut best: Option<(u32, u32)> = None;
        for child in tree.children {
            let Some(attrs) = conn
                .get_window_attributes(child)
                .ok()
                .and_then(|c| c.reply().ok())
            else {
                continue;
            };
            if attrs.map_state != MapState::VIEWABLE {
                continue;
            }
            let Some(g) = conn
                .get_geometry(child)
                .ok()
                .and_then(|c| c.reply().ok())
            else {
                continue;
            };
            if g.width == 0 || g.height == 0 {
                continue;
            }
            let area = g.width as u64 * g.height as u64;
            let bigger = best
                .map(|(bw, bh)| area > bw as u64 * bh as u64)
                .unwrap_or(true);
            if bigger {
                best = Some((g.width as u32, g.height as u32));
            }
        }
        match best {
            Some((w, h)) => classify_coverage(w as i32, h as i32, sw, sh),
            None => ScreenState::Normal,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn decide_bar_display_off_means_always_full() {
        for state in [ScreenState::Normal, ScreenState::Maximized, ScreenState::Fullscreen] {
            assert_eq!(decide_bar_display("notch", state, false), BarDisplay::Full);
            assert_eq!(decide_bar_display("pet", state, false), BarDisplay::Full);
        }
    }

    #[test]
    fn decide_bar_display_notch_three_levels() {
        assert_eq!(
            decide_bar_display("notch", ScreenState::Normal, true),
            BarDisplay::Full
        );
        assert_eq!(
            decide_bar_display("notch", ScreenState::Maximized, true),
            BarDisplay::ThinLine
        );
        assert_eq!(
            decide_bar_display("notch", ScreenState::Fullscreen, true),
            BarDisplay::Hidden
        );
    }

    #[test]
    fn decide_bar_display_pet_keeps_full_when_maximized() {
        assert_eq!(
            decide_bar_display("pet", ScreenState::Maximized, true),
            BarDisplay::Full
        );
        assert_eq!(
            decide_bar_display("pet", ScreenState::Fullscreen, true),
            BarDisplay::Hidden
        );
        assert_eq!(
            decide_bar_display("pet", ScreenState::Normal, true),
            BarDisplay::Full
        );
    }

    #[test]
    fn classify_rect_fullscreen_first() {
        // Menutupi seluruh layar → fullscreen (meski juga menutupi
        // work area — fullscreen dicek dulu).
        assert_eq!(
            classify_rect((0, 0, 1920, 1080), (1920, 1080), Some((0, 0, 1920, 1040))),
            ScreenState::Fullscreen
        );
        // Rect sedikit lebih besar dari layar (border tak terlihat)
        // tetap fullscreen.
        assert_eq!(
            classify_rect((-8, -8, 1928, 1088), (1920, 1080), Some((0, 0, 1920, 1040))),
            ScreenState::Fullscreen
        );
    }

    #[test]
    fn classify_rect_maximized_covers_work_area() {
        // Jendela maksimal: rect = work area persis.
        assert_eq!(
            classify_rect((0, 0, 1920, 1040), (1920, 1080), Some((0, 0, 1920, 1040))),
            ScreenState::Maximized
        );
        // Frame tak terlihat ~8px: rect sedikit lebih besar dari
        // work area tapi TIDAK menutupi layar penuh.
        assert_eq!(
            classify_rect((-8, -8, 1928, 1048), (1920, 1080), Some((0, 0, 1920, 1040))),
            ScreenState::Maximized
        );
    }

    #[test]
    fn classify_rect_windowed_is_normal() {
        assert_eq!(
            classify_rect((100, 100, 900, 700), (1920, 1080), Some((0, 0, 1920, 1040))),
            ScreenState::Normal
        );
        // Tanpa work area (SystemParametersInfo gagal): hanya
        // fullscreen yang dikenali, sisanya normal.
        assert_eq!(
            classify_rect((0, 0, 1920, 1040), (1920, 1080), None),
            ScreenState::Normal
        );
    }

    #[test]
    fn classify_coverage_screen_sizes() {
        // Layar Xvfb 1280×800.
        assert_eq!(classify_coverage(1280, 800, 1280, 800), ScreenState::Fullscreen);
        assert_eq!(classify_coverage(1280, 720, 1280, 800), ScreenState::Maximized);
        assert_eq!(classify_coverage(1280, 760, 1280, 800), ScreenState::Maximized);
        assert_eq!(classify_coverage(960, 640, 1280, 800), ScreenState::Normal);
        assert_eq!(classify_coverage(150, 32, 1280, 800), ScreenState::Normal);
        // Jendela lebih besar dari layar tetap fullscreen.
        assert_eq!(classify_coverage(1400, 900, 1280, 800), ScreenState::Fullscreen);
    }
}
