use crate::db::DatabaseService;
use crate::{AppWindow, Design, DynamicBarWindow, RadialMenuWindow};
use serde::{Deserialize, Serialize};
use slint::{Color, Global, SharedString, VecModel};

/// Satu tema = satu palet warna penuh (mode + aksen + background +
/// foreground + warna 4 widget + gaya latar widget). Inilah unit
/// customisasi ala Omarchy: "change the palette, not three apps" —
/// satu palet dibaca seluruh permukaan (dashboard, nav, Settings,
/// Trello, spreadsheet, dialog, radial, log panel) lewat token
/// global `Design`.
/// Key SQLite: preset bawaan di `Appearance.Palette` (kompatibel C#)
/// + `Settings.ThemePreset`; tema user di `Settings.Theme.UserThemes`
/// (JSON array); gaya latar widget di `Settings.WidgetStyle`.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct ThemePalette {
    pub id: String,
    pub name: String,
    pub mode: String,
    pub accent: String,
    pub bg: String,
    pub fg: String,
    pub editing: String,
    pub revision: String,
    pub late: String,
    pub points: String,
    /// Gaya permukaan kartu widget ("solid" | "gradient" | "glass" |
    /// "outline"). `#[serde(default)]` supaya tema user lama (JSON
    /// tanpa field ini) tetap terbaca — default "solid".
    #[serde(default = "default_widget_style")]
    pub widget_style: String,
}

/// Gaya latar widget default (tema user lama tanpa field ini).
fn default_widget_style() -> String {
    "solid".to_string()
}

/// Validasi gaya latar widget — nilai tak dikenal → "solid".
pub fn valid_widget_style(raw: &str) -> String {
    match unquote(raw).as_str() {
        "gradient" => "gradient".to_string(),
        "glass" => "glass".to_string(),
        "outline" => "outline".to_string(),
        _ => "solid".to_string(),
    }
}

/// Validasi gaya window chrome — nilai tak dikenal → "custom".
/// "custom" = titlebar kustom 28px; "headless" = tanpa titlebar;
/// "native" = chrome bawaan OS; "wrapped" = titlebar kustom + konten
/// dalam panel rounded inset (lihat DESIGN_SYSTEM.md §23).
pub fn valid_chrome_style(raw: &str) -> String {
    match unquote(raw).to_lowercase().as_str() {
        "headless" => "headless".to_string(),
        "native" => "native".to_string(),
        "wrapped" => "wrapped".to_string(),
        _ => "custom".to_string(),
    }
}

/// Validasi gaya tombol kontrol window — nilai tak dikenal → "icons".
/// "icons" = glyph ─ □ ×; "dots" = traffic-light pastel ala macOS
/// (tiga dot dalam pill putih, ref chrome-buttons-ref.jpg).
pub fn valid_chrome_button_style(raw: &str) -> String {
    match unquote(raw).to_lowercase().as_str() {
        "dots" => "dots".to_string(),
        _ => "icons".to_string(),
    }
}

/// Validasi posisi tombol kontrol window — nilai tak dikenal → "right".
/// "left" = gaya macOS (kiri), "right" = gaya Windows (kanan).
pub fn valid_chrome_button_position(raw: &str) -> String {
    match unquote(raw).to_lowercase().as_str() {
        "left" => "left".to_string(),
        _ => "right".to_string(),
    }
}

/// Validasi gaya toggle on/off — nilai tak dikenal → "classic".
/// "classic" = track 44×24 isi aksen + thumb putih 18px (bawaan);
/// "pill" = pill hitam lebar 64×24, knob putih geser berisi teks ON/OFF
/// (ref toggle-ref.gif); "outline" = track transparan border 2px +
/// thumb lingkaran solid.
pub fn valid_toggle_style(raw: &str) -> String {
    match unquote(raw).to_lowercase().as_str() {
        "pill" => "pill".to_string(),
        "outline" => "outline".to_string(),
        _ => "classic".to_string(),
    }
}

impl ThemePalette {
    fn new(
        id: &str,
        name: &str,
        mode: &str,
        accent: &str,
        bg: &str,
        fg: &str,
        editing: &str,
        revision: &str,
        late: &str,
        points: &str,
    ) -> Self {
        Self {
            id: id.to_string(),
            name: name.to_string(),
            mode: mode.to_string(),
            accent: accent.to_string(),
            bg: bg.to_string(),
            fg: fg.to_string(),
            editing: editing.to_string(),
            revision: revision.to_string(),
            late: late.to_string(),
            points: points.to_string(),
            widget_style: default_widget_style(),
        }
    }

    /// Gaya latar widget preset ini (rantai setelah `new`).
    /// Nilai divalidasi — tak dikenal → "solid".
    fn with_widget_style(mut self, style: &str) -> Self {
        self.widget_style = valid_widget_style(style);
        self
    }
}

/// 8 preset terkurasi (opinionated, bukan color-picker bebas).
/// 5 dari port (BMachine Dark/Light, Midnight OLED, Catppuccin Mocha,
/// Emerald Studio) + 3 dari C# `AppearancePalette` (Spider Red, Gwen
/// Pink, Armor Red — nilai diambil persis dari
/// `AppearancePalette.cs`; Classic ≈ BMachine Dark, Midnight Black ≈
/// Midnight OLED, Symbiote ≈ monokrom — tidak diduplikasi).
/// Catppuccin Mocha memakai nilai palet Catppuccin (mauve/peach/red/
/// green) — hex palet publik, bukan aset berlisensi.
/// Gaya latar widget tiap preset (opinionated): BMachine Dark/Light
/// + Armor = "solid" (bawaan); Midnight OLED = "outline" (hitam
/// pekat, border aksen saja); Catppuccin Mocha + Spider Red =
/// "gradient" (tinta aksen memudar); Emerald Studio + Gwen Pink =
/// "glass" (permukaan terangkat).
pub fn builtin_presets() -> Vec<ThemePalette> {
    vec![
        ThemePalette::new(
            "bmachine-dark",
            "BMachine Dark",
            "dark",
            "#3B82F6",
            "#121212",
            "#FFFFFF",
            "#3B82F6",
            "#F97316",
            "#EF4444",
            "#2DD4BF",
        ),
        ThemePalette::new(
            "bmachine-light",
            "BMachine Light",
            "light",
            "#3B82F6",
            "#F5F5F5",
            "#111111",
            "#3B82F6",
            "#F97316",
            "#EF4444",
            "#10B981",
        ),
        ThemePalette::new(
            "midnight-oled",
            "Midnight OLED",
            "dark",
            "#38BDF8",
            "#000000",
            "#F8FAFC",
            "#38BDF8",
            "#F97316",
            "#EF4444",
            "#2DD4BF",
        )
        .with_widget_style("outline"),
        ThemePalette::new(
            "catppuccin-mocha",
            "Catppuccin Mocha",
            "dark",
            "#CBA6F7",
            "#1E1E2E",
            "#CDD6F4",
            "#CBA6F7",
            "#FAB387",
            "#F38BA8",
            "#A6E3A1",
        )
        .with_widget_style("gradient"),
        ThemePalette::new(
            "emerald-studio",
            "Emerald Studio",
            "dark",
            "#10B981",
            "#0F1715",
            "#ECFDF5",
            "#10B981",
            "#F59E0B",
            "#EF4444",
            "#34D399",
        )
        .with_widget_style("glass"),
        ThemePalette::new(
            "spider",
            "Spider Red",
            "dark",
            "#D9272E",
            "#171717",
            "#FFFFFF",
            "#D9272E",
            "#1557A5",
            "#EF4444",
            "#F97316",
        )
        .with_widget_style("gradient"),
        ThemePalette::new(
            "gwen",
            "Gwen Pink",
            "dark",
            "#F2A2C0",
            "#24242E",
            "#FFFFFF",
            "#F2A2C0",
            "#65D6D6",
            "#EF4444",
            "#10B981",
        )
        .with_widget_style("glass"),
        ThemePalette::new(
            "armor",
            "Armor Red",
            "dark",
            "#A71930",
            "#252525",
            "#FFFFFF",
            "#A71930",
            "#D9A52E",
            "#EF4444",
            "#10B981",
        ),
    ]
}

pub fn palette_for(id: &str) -> Option<ThemePalette> {
    builtin_presets().into_iter().find(|p| p.id == id)
}

/// Nama preset lama (display name, tersimpan di DB lama sebagai
/// `Settings.ThemePreset`) → id baru. Id yang sudah benar dilewatkan.
pub fn legacy_preset_id(raw: &str) -> String {
    match unquote(raw).as_str() {
        "BMachine Dark" | "bmachine-dark" => "bmachine-dark".to_string(),
        "BMachine Light" | "bmachine-light" => "bmachine-light".to_string(),
        "Midnight OLED" | "midnight-oled" => "midnight-oled".to_string(),
        "Catppuccin Mocha" | "catppuccin-mocha" => "catppuccin-mocha".to_string(),
        "Emerald Studio" | "emerald-studio" => "emerald-studio".to_string(),
        "Custom" | "custom" => "custom".to_string(),
        other => other.to_string(),
    }
}

/// C# menyimpan sebagian nilai KeyValueStore sebagai JSON string
/// (mis. `"spider"`, `"#FFD9272E"`, `"Comfortable"` — lengkap dengan
/// tanda kutip). Kupas tanda kutip + spasi sebelum dipakai.
pub fn unquote(raw: &str) -> String {
    let s = raw.trim();
    if s.len() >= 2 && s.starts_with('"') && s.ends_with('"') {
        s[1..s.len() - 1].to_string()
    } else {
        s.to_string()
    }
}

/// Normalisasi warna C#/user ke `#RRGGBB` (huruf besar): kupas kutip,
/// buang prefix alpha `#AARRGGBB` (C# `Settings.Color.*` menyimpan
/// `"#FFD9272E"`). `None` bila bukan hex 6 digit.
pub fn normalize_hex(raw: &str) -> Option<String> {
    let s = unquote(raw);
    let body = s.trim().trim_start_matches('#');
    let hex6 = match body.len() {
        6 => body.to_string(),
        8 => body[2..].to_string(),
        _ => return None,
    };
    if !hex6.chars().all(|c| c.is_ascii_hexdigit()) {
        return None;
    }
    Some(format!("#{}", hex6.to_uppercase()))
}

/// Slug untuk id tema user: huruf kecil, spasi → `-`, buang karakter
/// selain huruf/angka/`-`.
pub fn slugify(name: &str) -> String {
    name.trim()
        .to_lowercase()
        .chars()
        .map(|c| {
            if c.is_ascii_alphanumeric() {
                c
            } else if c.is_whitespace() || c == '-' || c == '_' {
                '-'
            } else {
                '\0'
            }
        })
        .filter(|c| *c != '\0')
        .collect::<String>()
        .split('-')
        .filter(|p| !p.is_empty())
        .collect::<Vec<_>>()
        .join("-")
}

pub fn parse_hex_rgb(hex: &str) -> Option<(u8, u8, u8)> {
    let s = hex.trim().trim_start_matches('#');
    if s.len() != 6 {
        return None;
    }
    let r = u8::from_str_radix(&s[0..2], 16).ok()?;
    let g = u8::from_str_radix(&s[2..4], 16).ok()?;
    let b = u8::from_str_radix(&s[4..6], 16).ok()?;
    Some((r, g, b))
}

#[inline]
fn rgb(r: u8, g: u8, b: u8) -> Color {
    Color::from_rgb_u8(r, g, b)
}

#[inline]
fn blend(a: (u8, u8, u8), b: (u8, u8, u8), t: f32) -> (u8, u8, u8) {
    let inv = 1.0 - t;
    (
        ((a.0 as f32 * inv) + (b.0 as f32 * t))
            .round()
            .clamp(0.0, 255.0) as u8,
        ((a.1 as f32 * inv) + (b.1 as f32 * t))
            .round()
            .clamp(0.0, 255.0) as u8,
        ((a.2 as f32 * inv) + (b.2 as f32 * t))
            .round()
            .clamp(0.0, 255.0) as u8,
    )
}

#[inline]
fn shift(c: (u8, u8, u8), delta: i16) -> (u8, u8, u8) {
    (
        (c.0 as i16 + delta).clamp(0, 255) as u8,
        (c.1 as i16 + delta).clamp(0, 255) as u8,
        (c.2 as i16 + delta).clamp(0, 255) as u8,
    )
}

#[inline]
fn is_light_rgb(c: (u8, u8, u8)) -> bool {
    let luma = 0.2126 * c.0 as f32 + 0.7152 * c.1 as f32 + 0.0722 * c.2 as f32;
    luma > 140.0
}

pub fn is_windows_light_mode() -> bool {
    #[cfg(target_os = "windows")]
    {
        if let Ok(out) = std::process::Command::new("reg")
            .args([
                "query",
                r"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "/v",
                "AppsUseLightTheme",
            ])
            .output()
        {
            let text = String::from_utf8_lossy(&out.stdout);
            if text.contains("0x1") {
                return true;
            }
        }
    }
    false
}

/// Applies all Appearance settings on `AppWindow` directly to `Design` global tokens.
/// Returns `Ok(())` or `Err(msg)` if a typed hex string is invalid.
pub fn apply_appearance(ui: &AppWindow) -> Result<(), String> {
    let mode = ui.get_app_color_mode().to_string();
    let effective_light = match mode.as_str() {
        "light" => true,
        "system" => is_windows_light_mode(),
        _ => false,
    };

    let accent_hex = ui.get_app_accent_hex().to_string();
    let accent_rgb = match parse_hex_rgb(&accent_hex) {
        Some(c) => {
            let norm = format!("#{:02X}{:02X}{:02X}", c.0, c.1, c.2);
            ui.set_app_accent_color(rgb(c.0, c.1, c.2));
            ui.set_app_accent_hex(SharedString::from(&norm));
            ui.set_app_accent_hex_good(SharedString::from(&norm));
            c
        }
        None => {
            let good = ui.get_app_accent_hex_good().to_string();
            ui.set_app_accent_hex(SharedString::from(&good));
            return Err("Format hex Accent harus #RRGGBB (contoh: #3B82F6)".to_string());
        }
    };

    let bg_hex = ui.get_app_bg_hex().to_string();
    let bg_rgb = match parse_hex_rgb(&bg_hex) {
        Some(c) => {
            let norm = format!("#{:02X}{:02X}{:02X}", c.0, c.1, c.2);
            ui.set_app_bg_color(rgb(c.0, c.1, c.2));
            ui.set_app_bg_hex(SharedString::from(&norm));
            ui.set_app_bg_hex_good(SharedString::from(&norm));
            c
        }
        None => {
            let good = ui.get_app_bg_hex_good().to_string();
            ui.set_app_bg_hex(SharedString::from(&good));
            return Err("Format hex Background harus #RRGGBB".to_string());
        }
    };

    let fg_hex = ui.get_app_fg_hex().to_string();
    let fg_rgb = match parse_hex_rgb(&fg_hex) {
        Some(c) => {
            let norm = format!("#{:02X}{:02X}{:02X}", c.0, c.1, c.2);
            ui.set_app_fg_color(rgb(c.0, c.1, c.2));
            ui.set_app_fg_hex(SharedString::from(&norm));
            ui.set_app_fg_hex_good(SharedString::from(&norm));
            c
        }
        None => {
            let good = ui.get_app_fg_hex_good().to_string();
            ui.set_app_fg_hex(SharedString::from(&good));
            return Err("Format hex Foreground harus #RRGGBB".to_string());
        }
    };

    // Warna 4 widget (C# `Settings.Color.*`): pola yang sama seperti
    // accent/bg/fg — hex tidak valid → kembalikan ke nilai terakhir
    // yang baik, batalkan penerapan.
    let widget_rgb = [
        ("EDITING", ui.get_app_widget_editing_hex().to_string(), ui.get_app_widget_editing_hex_good().to_string()),
        ("REVISION", ui.get_app_widget_revision_hex().to_string(), ui.get_app_widget_revision_hex_good().to_string()),
        ("LATE", ui.get_app_widget_late_hex().to_string(), ui.get_app_widget_late_hex_good().to_string()),
        ("POINTS", ui.get_app_widget_points_hex().to_string(), ui.get_app_widget_points_hex_good().to_string()),
    ]
    .map(|(label, hex, good)| match parse_hex_rgb(&hex) {
        Some(c) => {
            let norm = format!("#{:02X}{:02X}{:02X}", c.0, c.1, c.2);
            Ok((label, norm, c))
        }
        None => Err((label, good)),
    });
    let mut widget_colors: [(u8, u8, u8); 4] = [(0, 0, 0); 4];
    for (i, item) in widget_rgb.into_iter().enumerate() {
        match item {
            Ok((label, norm, c)) => {
                widget_colors[i] = c;
                match label {
                    "EDITING" => ui.set_app_widget_editing_hex(SharedString::from(&norm)),
                    "REVISION" => ui.set_app_widget_revision_hex(SharedString::from(&norm)),
                    "LATE" => ui.set_app_widget_late_hex(SharedString::from(&norm)),
                    _ => ui.set_app_widget_points_hex(SharedString::from(&norm)),
                }
                match label {
                    "EDITING" => ui.set_app_widget_editing_hex_good(SharedString::from(&norm)),
                    "REVISION" => ui.set_app_widget_revision_hex_good(SharedString::from(&norm)),
                    "LATE" => ui.set_app_widget_late_hex_good(SharedString::from(&norm)),
                    _ => ui.set_app_widget_points_hex_good(SharedString::from(&norm)),
                }
            }
            Err((label, good)) => {
                match label {
                    "EDITING" => ui.set_app_widget_editing_hex(SharedString::from(&good)),
                    "REVISION" => ui.set_app_widget_revision_hex(SharedString::from(&good)),
                    "LATE" => ui.set_app_widget_late_hex(SharedString::from(&good)),
                    _ => ui.set_app_widget_points_hex(SharedString::from(&good)),
                }
                return Err(format!(
                    "Format hex warna widget {} harus #RRGGBB",
                    label
                ));
            }
        }
    }

    // If user chose light mode (or system light) while bg is still dark, derive light base;
    // otherwise honor exact custom bg/fg!
    let (eff_bg, eff_fg) = if effective_light && !is_light_rgb(bg_rgb) {
        ((245, 245, 247), (17, 17, 20))
    } else if !effective_light && is_light_rgb(bg_rgb) && mode == "system" {
        ((18, 18, 18), (255, 255, 255))
    } else {
        (bg_rgb, fg_rgb)
    };

    let light_surface = is_light_rgb(eff_bg);
    let d = Design::get(ui);

    d.set_app_bg(rgb(eff_bg.0, eff_bg.1, eff_bg.2));
    d.set_accent(rgb(accent_rgb.0, accent_rgb.1, accent_rgb.2));
    d.set_text_primary(rgb(eff_fg.0, eff_fg.1, eff_fg.2));

    if light_surface {
        let card = if eff_bg.0 >= 250 {
            (255, 255, 255)
        } else {
            (255, 255, 255)
        };
        let elev = (255, 255, 255);
        let sep = shift(eff_bg, -24);
        let hover = shift(eff_bg, -12);
        let input = (240, 242, 245);
        let subtle = blend(card, accent_rgb, 0.14);

        d.set_card_bg(rgb(card.0, card.1, card.2));
        d.set_surface_elevated(rgb(elev.0, elev.1, elev.2));
        d.set_separator(rgb(sep.0, sep.1, sep.2));
        d.set_bg_hover(rgb(hover.0, hover.1, hover.2));
        d.set_bg_input(rgb(input.0, input.1, input.2));
        d.set_border_default(rgb(sep.0, sep.1, sep.2));
        d.set_border_hover(rgb(190, 195, 205));
        d.set_border_focus(rgb(accent_rgb.0, accent_rgb.1, accent_rgb.2));
        d.set_ghost_hover(rgb(232, 234, 238));
        d.set_ghost_pressed(rgb(218, 221, 226));
        d.set_toggle_off_bg(rgb(203, 213, 225));
        d.set_accent_subtle(rgb(subtle.0, subtle.1, subtle.2));

        d.set_text_secondary(rgb(92, 98, 108));
        d.set_text_muted(rgb(130, 137, 148));

        d.set_nav_pill_bg(rgb(255, 255, 255));
        d.set_nav_pill_border(rgb(sep.0, sep.1, sep.2));
        d.set_nav_selected_bg(rgb(24, 24, 27));
        d.set_nav_selected_fg(rgb(255, 255, 255));
        d.set_nav_unselected_fg(rgb(100, 116, 139));

        d.set_btn_primary_bg(rgb(24, 24, 27));
        d.set_btn_primary_bg_hover(rgb(39, 39, 42));
        d.set_btn_primary_bg_pressed(rgb(63, 63, 70));
        d.set_btn_primary_fg(rgb(255, 255, 255));
        d.set_btn_secondary_bg(rgb(255, 255, 255));
        d.set_btn_secondary_bg_hover(rgb(244, 244, 245));
        d.set_btn_secondary_bg_pressed(rgb(228, 228, 231));
        d.set_btn_secondary_fg(rgb(24, 24, 27));
        d.set_btn_secondary_border(rgb(212, 212, 216));

        d.set_done_row_bg(rgb(220, 252, 231));
        d.set_done_row_border(rgb(34, 197, 94));
        d.set_done_text(rgb(21, 128, 61));
        d.set_done_text_alt(rgb(22, 101, 52));
        d.set_row_border_idle(rgb(203, 213, 225));
        d.set_row_text_dim(rgb(100, 116, 139));
        d.set_row_text_alt(rgb(71, 85, 105));
    } else {
        let card = shift(eff_bg, 8);
        let elev = shift(eff_bg, 14);
        let sep = shift(eff_bg, 20);
        let hover = shift(eff_bg, 16);
        let input = shift(eff_bg, -6);
        let subtle = blend(card, accent_rgb, 0.18);

        d.set_card_bg(rgb(card.0, card.1, card.2));
        d.set_surface_elevated(rgb(elev.0, elev.1, elev.2));
        d.set_separator(rgb(sep.0, sep.1, sep.2));
        d.set_bg_hover(rgb(hover.0, hover.1, hover.2));
        d.set_bg_input(rgb(input.0, input.1, input.2));
        d.set_border_default(rgb(sep.0, sep.1, sep.2));
        d.set_border_hover(rgb(63, 63, 70));
        d.set_border_focus(rgb(75, 75, 82));
        d.set_ghost_hover(rgb(40, 40, 45));
        d.set_ghost_pressed(rgb(49, 49, 54));
        d.set_toggle_off_bg(rgb(42, 42, 42));
        d.set_accent_subtle(rgb(subtle.0, subtle.1, subtle.2));

        d.set_text_secondary(rgb(153, 153, 153));
        d.set_text_muted(rgb(113, 113, 122));

        d.set_nav_pill_bg(rgb(card.0, card.1, card.2));
        d.set_nav_pill_border(rgb(sep.0, sep.1, sep.2));
        d.set_nav_selected_bg(rgb(244, 244, 245));
        d.set_nav_selected_fg(rgb(24, 24, 27));
        d.set_nav_unselected_fg(rgb(161, 161, 170));

        d.set_btn_primary_bg(rgb(237, 237, 237));
        d.set_btn_primary_bg_hover(rgb(255, 255, 255));
        d.set_btn_primary_bg_pressed(rgb(212, 212, 212));
        d.set_btn_primary_fg(rgb(17, 17, 17));
        d.set_btn_secondary_bg(rgb(28, 28, 31));
        d.set_btn_secondary_bg_hover(rgb(39, 39, 43));
        d.set_btn_secondary_bg_pressed(rgb(34, 34, 38));
        d.set_btn_secondary_fg(rgb(228, 228, 231));
        d.set_btn_secondary_border(rgb(63, 63, 70));

        d.set_done_row_bg(rgb(23, 63, 45));
        d.set_done_row_border(rgb(63, 175, 120));
        d.set_done_text(rgb(69, 201, 138));
        d.set_done_text_alt(rgb(157, 232, 193));
        d.set_row_border_idle(rgb(70, 80, 92));
        d.set_row_text_dim(rgb(123, 135, 148));
        d.set_row_text_alt(rgb(170, 180, 191));
    }

    // Font family
    let font = ui.get_app_font_choice().to_string();
    d.set_font_family(SharedString::from(if font.is_empty() {
        "Inter"
    } else {
        &font
    }));

    // Corner radius
    match ui.get_app_corner_style().as_str() {
        "sharp" => {
            d.set_radius_xs(2.0);
            d.set_radius_sm(3.0);
            d.set_radius_md(4.0);
            d.set_radius_lg(5.0);
            d.set_radius_xl(6.0);
        }
        "rounded" => {
            d.set_radius_xs(6.0);
            d.set_radius_sm(9.0);
            d.set_radius_md(12.0);
            d.set_radius_lg(16.0);
            d.set_radius_xl(20.0);
        }
        _ => {
            d.set_radius_xs(4.0);
            d.set_radius_sm(6.0);
            d.set_radius_md(8.0);
            d.set_radius_lg(10.0);
            d.set_radius_xl(12.0);
        }
    }

    // Nav pill style
    match ui.get_app_nav_style().as_str() {
        "rounded" => d.set_nav_pill_radius(10.0),
        "flat" => d.set_nav_pill_radius(4.0),
        _ => d.set_nav_pill_radius(999.0),
    }

    // Warna 4 widget — SATU palet dibaca semua permukaan: token
    // `cat-*` dipakai StatWidget dashboard, list Trello, spreadsheet,
    // explorer, modul, detail — jadi ganti preset mengubah warna
    // widget di mana-mana, bukan hanya aksen.
    d.set_cat_editing(rgb(widget_colors[0].0, widget_colors[0].1, widget_colors[0].2));
    d.set_cat_revision(rgb(widget_colors[1].0, widget_colors[1].1, widget_colors[1].2));
    d.set_cat_late(rgb(widget_colors[2].0, widget_colors[2].1, widget_colors[2].2));
    d.set_cat_points(rgb(widget_colors[3].0, widget_colors[3].1, widget_colors[3].2));

    // Kepadatan (C# `Settings.UiDensity` / UiDensityService: Compact /
    // Normal / Comfortable — ukuran tombol & ikon). Port menerapkannya
    // lewat token font + spacing (dipakai ~100 tempat di UI) dan
    // `density-scale`. Nilai Normal = token bawaan.
    let (cap, btn, body, h3, h2, h1, xs, sm, md, lg, xl, dscale) =
        match ui.get_app_ui_density().as_str() {
            "compact" => (10.0, 11.0, 12.0, 13.0, 17.0, 22.0, 3.0, 6.0, 10.0, 14.0, 18.0, 0.9),
            "comfortable" => (12.0, 13.5, 14.0, 15.0, 19.0, 26.0, 5.0, 10.0, 15.0, 20.0, 25.0, 1.15),
            _ => (11.0, 12.0, 13.0, 14.0, 18.0, 24.0, 4.0, 8.0, 12.0, 16.0, 20.0, 1.0),
        };
    d.set_font_caption(cap);
    d.set_font_button(btn);
    d.set_font_body(body);
    d.set_font_h3(h3);
    d.set_font_h2(h2);
    d.set_font_h1(h1);
    d.set_space_xs(xs);
    d.set_space_sm(sm);
    d.set_space_md(md);
    d.set_space_lg(lg);
    d.set_space_xl(xl);
    d.set_density_scale(dscale);

    Ok(())
}

/// Salin token Design yang dipakai Dynamic Bar dari window utama ke
/// window bar. Slint memberi tiap window OS instance Design
/// SENDIRI (terbukti 27 Nov 2026: SpreadsheetWindow tetap warna
/// BMachine Dark setelah ganti tema Spider — dan bar yang sama).
/// Jadi token harus disalin eksplisit saat bar dibuka + setiap kali
/// tema berubah; mengandalkan "global bersama" tidak berlaku untuk
/// window OS terpisah. Hanya token yang dibaca DynamicBarWindow
/// yang disalin (bukan seluruh Design).
pub fn sync_design_to_bar(ui: &AppWindow, bar: &DynamicBarWindow) {
    let src = Design::get(ui);
    let dst = Design::get(bar);
    dst.set_surface_elevated(src.get_surface_elevated());
    dst.set_separator(src.get_separator());
    dst.set_text_primary(src.get_text_primary());
    dst.set_text_secondary(src.get_text_secondary());
    dst.set_text_muted(src.get_text_muted());
    dst.set_accent(src.get_accent());
    dst.set_bg_hover(src.get_bg_hover());
    dst.set_status_danger(src.get_status_danger());
    dst.set_cat_editing(src.get_cat_editing());
    dst.set_cat_revision(src.get_cat_revision());
    dst.set_cat_late(src.get_cat_late());
    dst.set_cat_points(src.get_cat_points());
    dst.set_radius_sm(src.get_radius_sm());
    dst.set_radius_md(src.get_radius_md());
    dst.set_font_family(src.get_font_family());
}

/// Sama seperti `sync_design_to_bar`, untuk window radial (OS window
/// 320×320): salin SEMUA token yang dipakai `ui/radial.slint`
/// (aksen, permukaan, teks, overlay) supaya segmen radial mengikuti
/// preset tema aktif. Dipanggil saat radial dibuka + tiap handler tema.
pub fn sync_design_to_radial(ui: &AppWindow, radial: &RadialMenuWindow) {
    let src = Design::get(ui);
    let dst = Design::get(radial);
    dst.set_app_bg(src.get_app_bg());
    dst.set_card_bg(src.get_card_bg());
    dst.set_surface_elevated(src.get_surface_elevated());
    dst.set_separator(src.get_separator());
    dst.set_text_primary(src.get_text_primary());
    dst.set_text_secondary(src.get_text_secondary());
    dst.set_text_muted(src.get_text_muted());
    dst.set_accent(src.get_accent());
    dst.set_accent_subtle(src.get_accent_subtle());
    dst.set_bg_hover(src.get_bg_hover());
    dst.set_overlay_solid(src.get_overlay_solid());
    dst.set_status_danger(src.get_status_danger());
    dst.set_radius_sm(src.get_radius_sm());
    dst.set_radius_md(src.get_radius_md());
    dst.set_font_family(src.get_font_family());
}

/// Terapkan satu preset bawaan: set SEMUA properti tampilan dari
/// palet (mode + aksen + bg + fg + 4 warna widget), lalu turunkan
/// token Design. Penyimpanan ke DB dilakukan pemanggil (seperti
/// `on_apply_appearance`). `Err` bila id tidak dikenal.
pub fn apply_preset(ui: &AppWindow, id: &str) -> Result<(), String> {
    let p = palette_for(id)
        .ok_or_else(|| format!("Tema tidak dikenal: {}", id))?;
    ui.set_app_color_mode(SharedString::from(p.mode.clone()));
    ui.set_app_theme_preset(SharedString::from(p.id.clone()));
    ui.set_widget_style(SharedString::from(p.widget_style.clone()));
    set_hex_pair(ui, "accent", &p.accent);
    set_hex_pair(ui, "bg", &p.bg);
    set_hex_pair(ui, "fg", &p.fg);
    set_hex_pair(ui, "editing", &p.editing);
    set_hex_pair(ui, "revision", &p.revision);
    set_hex_pair(ui, "late", &p.late);
    set_hex_pair(ui, "points", &p.points);
    apply_appearance(ui)
}

fn set_hex_pair(ui: &AppWindow, which: &str, hex: &str) {
    let norm = normalize_hex(hex).unwrap_or_else(|| hex.to_string());
    match which {
        "accent" => {
            ui.set_app_accent_hex(SharedString::from(&norm));
            ui.set_app_accent_hex_good(SharedString::from(&norm));
        }
        "bg" => {
            ui.set_app_bg_hex(SharedString::from(&norm));
            ui.set_app_bg_hex_good(SharedString::from(&norm));
        }
        "fg" => {
            ui.set_app_fg_hex(SharedString::from(&norm));
            ui.set_app_fg_hex_good(SharedString::from(&norm));
        }
        "editing" => {
            ui.set_app_widget_editing_hex(SharedString::from(&norm));
            ui.set_app_widget_editing_hex_good(SharedString::from(&norm));
        }
        "revision" => {
            ui.set_app_widget_revision_hex(SharedString::from(&norm));
            ui.set_app_widget_revision_hex_good(SharedString::from(&norm));
        }
        "late" => {
            ui.set_app_widget_late_hex(SharedString::from(&norm));
            ui.set_app_widget_late_hex_good(SharedString::from(&norm));
        }
        _ => {
            ui.set_app_widget_points_hex(SharedString::from(&norm));
            ui.set_app_widget_points_hex_good(SharedString::from(&norm));
        }
    }
}

/// Terapkan satu tema user (hasil `load_user_themes`): sama seperti
/// preset, tapi id `user:<slug>` dan nama tampilannya nama user.
pub fn apply_user_theme(ui: &AppWindow, theme: &ThemePalette) -> Result<(), String> {
    ui.set_app_color_mode(SharedString::from(theme.mode.clone()));
    ui.set_app_theme_preset(SharedString::from(theme.id.clone()));
    ui.set_widget_style(SharedString::from(
        valid_widget_style(&theme.widget_style),
    ));
    set_hex_pair(ui, "accent", &theme.accent);
    set_hex_pair(ui, "bg", &theme.bg);
    set_hex_pair(ui, "fg", &theme.fg);
    set_hex_pair(ui, "editing", &theme.editing);
    set_hex_pair(ui, "revision", &theme.revision);
    set_hex_pair(ui, "late", &theme.late);
    set_hex_pair(ui, "points", &theme.points);
    apply_appearance(ui)
}

pub const USER_THEMES_KEY: &str = "Settings.Theme.UserThemes";

/// Muat tema user dari SQLite (satu key JSON array). Rusak / kosong →
/// daftar kosong (jangan crash startup).
pub fn load_user_themes(db: &DatabaseService) -> Vec<ThemePalette> {
    let Some(raw) = db.get(USER_THEMES_KEY) else {
        return Vec::new();
    };
    serde_json::from_str::<Vec<ThemePalette>>(&raw).unwrap_or_default()
}

pub fn save_user_themes(db: &mut DatabaseService, themes: &[ThemePalette]) {
    let json = serde_json::to_string(themes).unwrap_or_else(|_| "[]".to_string());
    let _ = db.set(USER_THEMES_KEY, &json, "Json");
}

/// Dorong daftar tema user ke array paralel Slint (picker `for`).
/// Warna tidak valid → pakai aksen tema itu sebagai ganti.
pub fn refresh_user_themes(ui: &AppWindow, themes: &[ThemePalette]) {
    let ids: Vec<SharedString> = themes.iter().map(|t| SharedString::from(t.id.clone())).collect();
    let names: Vec<SharedString> =
        themes.iter().map(|t| SharedString::from(t.name.clone())).collect();
    let color_of = |hex: &str, fallback: (u8, u8, u8)| -> Color {
        parse_hex_rgb(hex)
            .map(|(r, g, b)| Color::from_rgb_u8(r, g, b))
            .unwrap_or_else(|| Color::from_rgb_u8(fallback.0, fallback.1, fallback.2))
    };
    let accents: Vec<Color> = themes
        .iter()
        .map(|t| color_of(&t.accent, (59, 130, 246)))
        .collect();
    let revisions: Vec<Color> = themes
        .iter()
        .map(|t| color_of(&t.revision, (249, 115, 22)))
        .collect();
    let bgs: Vec<Color> =
        themes.iter().map(|t| color_of(&t.bg, (18, 18, 18))).collect();
    let fgs: Vec<Color> =
        themes.iter().map(|t| color_of(&t.fg, (255, 255, 255))).collect();
    ui.set_user_theme_ids(std::rc::Rc::new(VecModel::from(ids)).into());
    ui.set_user_theme_names(std::rc::Rc::new(VecModel::from(names)).into());
    ui.set_user_theme_accents(std::rc::Rc::new(VecModel::from(accents)).into());
    ui.set_user_theme_revisions(std::rc::Rc::new(VecModel::from(revisions)).into());
    ui.set_user_theme_bgs(std::rc::Rc::new(VecModel::from(bgs)).into());
    ui.set_user_theme_fgs(std::rc::Rc::new(VecModel::from(fgs)).into());
}

/// Snapshot tampilan saat ini sebagai tema user baru (nama dari user).
/// Nama kosong → Err. Nama sama (slug sama) → timpa tema lama.
pub fn snapshot_current_as_user_theme(ui: &AppWindow, name: &str) -> Result<ThemePalette, String> {
    let name = name.trim();
    if name.is_empty() {
        return Err("Nama tema tidak boleh kosong".to_string());
    }
    let slug = slugify(name);
    if slug.is_empty() {
        return Err("Nama tema harus mengandung huruf atau angka".to_string());
    }
    Ok(ThemePalette {
        id: format!("user:{}", slug),
        name: name.to_string(),
        mode: ui.get_app_color_mode().to_string(),
        accent: ui.get_app_accent_hex_good().to_string(),
        bg: ui.get_app_bg_hex_good().to_string(),
        fg: ui.get_app_fg_hex_good().to_string(),
        editing: ui.get_app_widget_editing_hex_good().to_string(),
        revision: ui.get_app_widget_revision_hex_good().to_string(),
        late: ui.get_app_widget_late_hex_good().to_string(),
        points: ui.get_app_widget_points_hex_good().to_string(),
        widget_style: valid_widget_style(&ui.get_widget_style().to_string()),
    })
}

pub fn load_theme_from_db(ui: &AppWindow, db: &DatabaseService) {
    if let Some(v) = db.get("Settings.Theme") {
        ui.set_app_color_mode(SharedString::from(unquote(&v)));
    }
    // Preset: kunci C# `Appearance.Palette` didahulukan (DB asli abeng
    // menyimpan `"spider"` di sana), fallback kunci port lama.
    // Nilai C# dikutip JSON — kupas dulu, lalu petakan nama lama.
    let preset_raw = db
        .get("Appearance.Palette")
        .or_else(|| db.get("Settings.ThemePreset"));
    // Terapkan warna preset ke prop — tanpa ini apply_appearance di
    // akhir memakai hex default biru meski preset sudah dimuat
    // (ditemukan saat verifikasi radial Spider: aksen tetap #3B82F6
    // padahal Appearance.Palette = "spider"). Override eksplisit di
    // bawah (Settings.Accent, Appearance.Background.*,
    // Settings.Color.*) tetap menang karena ditulis setelah preset.
    if let Some(v) = preset_raw.as_ref() {
        let id = legacy_preset_id(v);
        ui.set_app_theme_preset(SharedString::from(id.clone()));
        if let Some(p) = palette_for(&id) {
            set_hex_pair(ui, "accent", &p.accent);
            set_hex_pair(ui, "bg", &p.bg);
            set_hex_pair(ui, "fg", &p.fg);
            set_hex_pair(ui, "editing", &p.editing);
            set_hex_pair(ui, "revision", &p.revision);
            set_hex_pair(ui, "late", &p.late);
            set_hex_pair(ui, "points", &p.points);
            ui.set_widget_style(SharedString::from(p.widget_style.clone()));
        }
    }
    if let Some(v) = db.get("Settings.Accent") {
        if let Some(norm) = normalize_hex(&v) {
            ui.set_app_accent_hex(SharedString::from(&norm));
            ui.set_app_accent_hex_good(SharedString::from(&norm));
        }
    }
    let bg_key = if ui.get_app_color_mode().as_str() == "light" {
        "Appearance.Background.Light"
    } else {
        "Appearance.Background.Dark"
    };
    if let Some(v) = db.get(bg_key) {
        if let Some(norm) = normalize_hex(&v) {
            ui.set_app_bg_hex(SharedString::from(&norm));
            ui.set_app_bg_hex_good(SharedString::from(&norm));
        }
    }
    if let Some(v) = db.get("Settings.Foreground") {
        if let Some(norm) = normalize_hex(&v) {
            ui.set_app_fg_hex(SharedString::from(&norm));
            ui.set_app_fg_hex_good(SharedString::from(&norm));
        }
    }
    // Warna widget (C# `Settings.Color.*`, nilai dikutip + prefix
    // alpha `#FF...` — normalisasi di sini).
    for (key, which) in [
        ("Settings.Color.Editing", "editing"),
        ("Settings.Color.Revision", "revision"),
        ("Settings.Color.Late", "late"),
        ("Settings.Color.Points", "points"),
    ] {
        if let Some(v) = db.get(key) {
            if let Some(norm) = normalize_hex(&v) {
                match which {
                    "editing" => {
                        ui.set_app_widget_editing_hex(SharedString::from(&norm));
                        ui.set_app_widget_editing_hex_good(SharedString::from(&norm));
                    }
                    "revision" => {
                        ui.set_app_widget_revision_hex(SharedString::from(&norm));
                        ui.set_app_widget_revision_hex_good(SharedString::from(&norm));
                    }
                    "late" => {
                        ui.set_app_widget_late_hex(SharedString::from(&norm));
                        ui.set_app_widget_late_hex_good(SharedString::from(&norm));
                    }
                    _ => {
                        ui.set_app_widget_points_hex(SharedString::from(&norm));
                        ui.set_app_widget_points_hex_good(SharedString::from(&norm));
                    }
                }
            }
        }
    }
    if let Some(v) = db.get("Settings.Font") {
        ui.set_app_font_choice(SharedString::from(unquote(&v)));
    }
    if let Some(v) = db.get("Settings.WidgetBg") {
        ui.set_widget_bg(SharedString::from(unquote(&v)));
    }
    // Gaya latar widget (port; C# tidak punya padanan) — nilai tak
    // dikenal → "solid".
    if let Some(v) = db.get("Settings.WidgetStyle") {
        ui.set_widget_style(SharedString::from(valid_widget_style(&v)));
    }
    if let Some(v) = db.get("Settings.CornerRadius") {
        ui.set_app_corner_style(SharedString::from(unquote(&v)));
    }
    if let Some(v) = db.get("Settings.NavStyle") {
        ui.set_app_nav_style(SharedString::from(unquote(&v)));
    }
    if let Some(v) = db.get("Settings.Chrome") {
        ui.set_app_chrome(SharedString::from(valid_chrome_style(&v)));
    }
    if let Some(v) = db.get("Settings.Chrome.ButtonStyle") {
        ui.set_app_chrome_button_style(SharedString::from(valid_chrome_button_style(&v)));
    }
    if let Some(v) = db.get("Settings.Chrome.ButtonPosition") {
        ui.set_app_chrome_button_position(SharedString::from(valid_chrome_button_position(
            &v,
        )));
    }
    // Kepadatan (C# `Settings.UiDensity`: "Compact"/"Normal"/
    // "Comfortable", dikutip JSON) → "compact"/"normal"/"comfortable".
    if let Some(v) = db.get("Settings.UiDensity") {
        let d = match unquote(&v).to_lowercase().as_str() {
            "compact" => "compact",
            "comfortable" => "comfortable",
            _ => "normal",
        };
        ui.set_app_ui_density(SharedString::from(d));
    }
    // Tema user (picker "Tema saya").
    let user_themes = load_user_themes(db);
    refresh_user_themes(ui, &user_themes);

    let _ = apply_appearance(ui);
}

pub fn save_theme_to_db(ui: &AppWindow, db: &mut DatabaseService) {
    let mode = ui.get_app_color_mode().to_string();
    let _ = db.set("Settings.Theme", &mode, "String");
    // Preset ditulis ke KEDUA kunci supaya DB port dan DB C# saling
    // terbaca (C# membaca `Appearance.Palette`).
    let preset = ui.get_app_theme_preset().to_string();
    let _ = db.set("Settings.ThemePreset", &preset, "String");
    let _ = db.set("Appearance.Palette", &preset, "String");
    let _ = db.set(
        "Settings.Accent",
        ui.get_app_accent_hex_good().as_str(),
        "String",
    );
    let bg_key = if mode == "light" {
        "Appearance.Background.Light"
    } else {
        "Appearance.Background.Dark"
    };
    let _ = db.set(bg_key, ui.get_app_bg_hex_good().as_str(), "String");
    let _ = db.set(
        "Settings.Foreground",
        ui.get_app_fg_hex_good().as_str(),
        "String",
    );
    let _ = db.set(
        "Settings.Color.Editing",
        ui.get_app_widget_editing_hex_good().as_str(),
        "String",
    );
    let _ = db.set(
        "Settings.Color.Revision",
        ui.get_app_widget_revision_hex_good().as_str(),
        "String",
    );
    let _ = db.set(
        "Settings.Color.Late",
        ui.get_app_widget_late_hex_good().as_str(),
        "String",
    );
    let _ = db.set(
        "Settings.Color.Points",
        ui.get_app_widget_points_hex_good().as_str(),
        "String",
    );
    let _ = db.set("Settings.Font", ui.get_app_font_choice().as_str(), "String");
    let _ = db.set("Settings.WidgetBg", ui.get_widget_bg().as_str(), "String");
    let _ = db.set(
        "Settings.WidgetStyle",
        ui.get_widget_style().as_str(),
        "String",
    );
    let _ = db.set(
        "Settings.CornerRadius",
        ui.get_app_corner_style().as_str(),
        "String",
    );
    let _ = db.set(
        "Settings.NavStyle",
        ui.get_app_nav_style().as_str(),
        "String",
    );
    let _ = db.set(
        "Settings.Chrome",
        valid_chrome_style(ui.get_app_chrome().as_str()).as_str(),
        "String",
    );
    let _ = db.set(
        "Settings.Chrome.ButtonStyle",
        valid_chrome_button_style(ui.get_app_chrome_button_style().as_str()).as_str(),
        "String",
    );
    let _ = db.set(
        "Settings.Chrome.ButtonPosition",
        valid_chrome_button_position(ui.get_app_chrome_button_position().as_str()).as_str(),
        "String",
    );
    // Kepadatan — nilai kapital seperti C# (`UiDensityService`).
    let density = match ui.get_app_ui_density().as_str() {
        "compact" => "Compact",
        "comfortable" => "Comfortable",
        _ => "Normal",
    };
    let _ = db.set("Settings.UiDensity", density, "String");
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn unquote_strips_json_quotes() {
        assert_eq!(unquote("\"spider\""), "spider");
        assert_eq!(unquote("\"#FFD9272E\""), "#FFD9272E");
        assert_eq!(unquote("dark"), "dark");
        assert_eq!(unquote("  \"Comfortable\"  "), "Comfortable");
        assert_eq!(unquote(""), "");
    }

    #[test]
    fn normalize_hex_handles_csharp_argb() {
        assert_eq!(normalize_hex("\"#FFD9272E\"").as_deref(), Some("#D9272E"));
        assert_eq!(normalize_hex("#8b5cf6").as_deref(), Some("#8B5CF6"));
        assert_eq!(normalize_hex("#3b82f6").as_deref(), Some("#3B82F6"));
        assert_eq!(normalize_hex("not-a-color"), None);
        assert_eq!(normalize_hex("#12345"), None);
        assert_eq!(normalize_hex(""), None);
    }

    #[test]
    fn builtin_presets_are_eight_valid_palettes() {
        let presets = builtin_presets();
        assert_eq!(presets.len(), 8);
        let mut ids: Vec<&str> = presets.iter().map(|p| p.id.as_str()).collect();
        ids.sort_unstable();
        ids.dedup();
        assert_eq!(ids.len(), 8, "id preset harus unik");
        for p in &presets {
            for hex in [
                &p.accent, &p.bg, &p.fg, &p.editing, &p.revision, &p.late, &p.points,
            ] {
                assert!(
                    parse_hex_rgb(hex).is_some(),
                    "preset {} punya hex tidak valid: {}",
                    p.id,
                    hex
                );
            }
            assert!(p.mode == "dark" || p.mode == "light", "mode {}", p.id);
        }
        // Default tetap BMachine Dark, palet C# Spider/Gwen/Armor ada.
        assert_eq!(presets[0].id, "bmachine-dark");
        assert!(palette_for("spider").is_some());
        assert!(palette_for("gwen").is_some());
        assert!(palette_for("armor").is_some());
        assert!(palette_for("tidak-ada").is_none());
    }

    #[test]
    fn valid_chrome_style_accepts_known_and_defaults_unknown() {
        assert_eq!(valid_chrome_style("custom"), "custom");
        assert_eq!(valid_chrome_style("headless"), "headless");
        assert_eq!(valid_chrome_style("native"), "native");
        assert_eq!(valid_chrome_style("wrapped"), "wrapped");
        assert_eq!(valid_chrome_style("WRAPPED"), "wrapped");
        assert_eq!(valid_chrome_style("\"native\""), "native");
        assert_eq!(valid_chrome_style("aneh"), "custom");
        assert_eq!(valid_chrome_style(""), "custom");
    }

    #[test]
    fn valid_chrome_button_style_and_position_default_unknown() {
        assert_eq!(valid_chrome_button_style("dots"), "dots");
        assert_eq!(valid_chrome_button_style("icons"), "icons");
        assert_eq!(valid_chrome_button_style("DOTS"), "dots");
        assert_eq!(valid_chrome_button_style("aneh"), "icons");
        assert_eq!(valid_chrome_button_position("left"), "left");
        assert_eq!(valid_chrome_button_position("right"), "right");
        assert_eq!(valid_chrome_button_position("LEFT"), "left");
        assert_eq!(valid_chrome_button_position("tengah"), "right");
    }

    #[test]
    fn valid_toggle_style_accepts_known_and_defaults_unknown() {
        assert_eq!(valid_toggle_style("classic"), "classic");
        assert_eq!(valid_toggle_style("pill"), "pill");
        assert_eq!(valid_toggle_style("outline"), "outline");
        assert_eq!(valid_toggle_style("PILL"), "pill");
        assert_eq!(valid_toggle_style("\"outline\""), "outline");
        assert_eq!(valid_toggle_style("aneh"), "classic");
        assert_eq!(valid_toggle_style(""), "classic");
    }

    #[test]
    fn legacy_preset_names_map_to_ids() {
        assert_eq!(legacy_preset_id("BMachine Dark"), "bmachine-dark");
        assert_eq!(legacy_preset_id("\"BMachine Light\""), "bmachine-light");
        assert_eq!(legacy_preset_id("Midnight OLED"), "midnight-oled");
        assert_eq!(legacy_preset_id("Catppuccin Mocha"), "catppuccin-mocha");
        assert_eq!(legacy_preset_id("Emerald Studio"), "emerald-studio");
        assert_eq!(legacy_preset_id("\"Custom\""), "custom");
        assert_eq!(legacy_preset_id("spider"), "spider");
        assert_eq!(legacy_preset_id("user:tema-saya"), "user:tema-saya");
    }

    #[test]
    fn slugify_names() {
        assert_eq!(slugify("Tema Saya 1"), "tema-saya-1");
        assert_eq!(slugify("  Merah -- Muda  "), "merah-muda");
        assert_eq!(slugify("!!!"), "");
        assert_eq!(slugify("TokYO_Night"), "tokyo-night");
    }

    #[test]
    fn user_theme_json_roundtrip() {
        let themes = vec![ThemePalette::new(
            "user:tema-saya",
            "Tema Saya",
            "dark",
            "#D9272E",
            "#171717",
            "#FFFFFF",
            "#D9272E",
            "#1557A5",
            "#EF4444",
            "#F97316",
        )];
        let json = serde_json::to_string(&themes).unwrap();
        let back: Vec<ThemePalette> = serde_json::from_str(&json).unwrap();
        assert_eq!(back, themes);
        // JSON rusak → daftar kosong (startup tidak boleh crash).
        let broken: Vec<ThemePalette> =
            serde_json::from_str("{bukan json").unwrap_or_default();
        assert!(broken.is_empty());
    }

    #[test]
    fn valid_widget_style_falls_back_to_solid() {
        assert_eq!(valid_widget_style("gradient"), "gradient");
        assert_eq!(valid_widget_style("glass"), "glass");
        assert_eq!(valid_widget_style("outline"), "outline");
        assert_eq!(valid_widget_style("solid"), "solid");
        assert_eq!(valid_widget_style("tidak-ada"), "solid");
        assert_eq!(valid_widget_style(""), "solid");
        assert_eq!(valid_widget_style("\"glass\""), "glass");
    }

    #[test]
    fn preset_widget_styles_are_valid_and_mapped() {
        for p in builtin_presets() {
            assert!(
                ["solid", "gradient", "glass", "outline"].contains(&p.widget_style.as_str()),
                "preset {} gaya tidak valid: {}",
                p.id,
                p.widget_style
            );
        }
        // Pemetaan opinionated (§22): default solid; OLED outline;
        // Mocha + Spider gradient; Emerald + Gwen glass.
        assert_eq!(palette_for("bmachine-dark").unwrap().widget_style, "solid");
        assert_eq!(palette_for("bmachine-light").unwrap().widget_style, "solid");
        assert_eq!(palette_for("armor").unwrap().widget_style, "solid");
        assert_eq!(palette_for("midnight-oled").unwrap().widget_style, "outline");
        assert_eq!(
            palette_for("catppuccin-mocha").unwrap().widget_style,
            "gradient"
        );
        assert_eq!(palette_for("spider").unwrap().widget_style, "gradient");
        assert_eq!(palette_for("emerald-studio").unwrap().widget_style, "glass");
        assert_eq!(palette_for("gwen").unwrap().widget_style, "glass");
    }

    #[test]
    fn old_user_theme_json_without_widget_style_still_parses() {
        // Tema user yang disimpan sebelum field widget_style ada
        // (JSON tanpa field itu) harus tetap terbaca, gaya = "solid".
        let old_json = r##"[{"id":"user:tema-lama","name":"Tema Lama","mode":"dark","accent":"#D9272E","bg":"#171717","fg":"#FFFFFF","editing":"#D9272E","revision":"#1557A5","late":"#EF4444","points":"#F97316"}]"##;
        let back: Vec<ThemePalette> = serde_json::from_str(old_json).unwrap();
        assert_eq!(back.len(), 1);
        assert_eq!(back[0].widget_style, "solid");
        assert_eq!(back[0].name, "Tema Lama");
    }
}
