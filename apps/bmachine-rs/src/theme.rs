use crate::db::DatabaseService;
use crate::{AppWindow, Design};
use slint::{Color, Global, SharedString};

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

    Ok(())
}

pub fn load_theme_from_db(ui: &AppWindow, db: &DatabaseService) {
    if let Some(v) = db.get("Settings.Theme") {
        ui.set_app_color_mode(SharedString::from(v));
    }
    if let Some(v) = db.get("Settings.ThemePreset") {
        ui.set_app_theme_preset(SharedString::from(v));
    }
    if let Some(v) = db.get("Settings.Accent") {
        if parse_hex_rgb(&v).is_some() {
            ui.set_app_accent_hex(SharedString::from(&v));
            ui.set_app_accent_hex_good(SharedString::from(&v));
        }
    }
    let bg_key = if ui.get_app_color_mode().as_str() == "light" {
        "Appearance.Background.Light"
    } else {
        "Appearance.Background.Dark"
    };
    if let Some(v) = db.get(bg_key) {
        if parse_hex_rgb(&v).is_some() {
            ui.set_app_bg_hex(SharedString::from(&v));
            ui.set_app_bg_hex_good(SharedString::from(&v));
        }
    }
    if let Some(v) = db.get("Settings.Foreground") {
        if parse_hex_rgb(&v).is_some() {
            ui.set_app_fg_hex(SharedString::from(&v));
            ui.set_app_fg_hex_good(SharedString::from(&v));
        }
    }
    if let Some(v) = db.get("Settings.Font") {
        ui.set_app_font_choice(SharedString::from(v));
    }
    if let Some(v) = db.get("Settings.WidgetBg") {
        ui.set_widget_bg(SharedString::from(v));
    }
    if let Some(v) = db.get("Settings.CornerRadius") {
        ui.set_app_corner_style(SharedString::from(v));
    }
    if let Some(v) = db.get("Settings.NavStyle") {
        ui.set_app_nav_style(SharedString::from(v));
    }
    if let Some(v) = db.get("Settings.Chrome") {
        ui.set_app_chrome(SharedString::from(v));
    }

    let _ = apply_appearance(ui);
}

pub fn save_theme_to_db(ui: &AppWindow, db: &mut DatabaseService) {
    let mode = ui.get_app_color_mode().to_string();
    let _ = db.set("Settings.Theme", &mode, "String");
    let _ = db.set(
        "Settings.ThemePreset",
        ui.get_app_theme_preset().as_str(),
        "String",
    );
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
    let _ = db.set("Settings.Font", ui.get_app_font_choice().as_str(), "String");
    let _ = db.set("Settings.WidgetBg", ui.get_widget_bg().as_str(), "String");
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
    let _ = db.set("Settings.Chrome", ui.get_app_chrome().as_str(), "String");
}
