//! PixaCompact (Rust) — entry point.
//! `pixa [gambar...]` membuka dashboard; file arg langsung masuk daftar.

use std::path::PathBuf;

use eframe::egui;
use pxa_app::dashboard::Dashboard;

fn main() -> eframe::Result<()> {
    let raw: Vec<String> = std::env::args().skip(1).collect();
    // `pixa --editor <file>` langsung buka preview editor (tanpa klik).
    let mut editor_file: Option<PathBuf> = None;
    let mut files: Vec<PathBuf> = Vec::new();
    let mut it = raw.into_iter();
    while let Some(a) = it.next() {
        if a == "--editor" {
            editor_file = it.next().map(PathBuf::from);
        } else {
            files.push(PathBuf::from(a));
        }
    }
    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size([1280.0, 860.0])
            .with_min_inner_size([980.0, 640.0])
            .with_title("Pixa Compact"),
        ..Default::default()
    };
    eframe::run_native(
        "Pixa Compact",
        options,
        Box::new(move |_cc| {
            let mut app = Dashboard::new();
            if !files.is_empty() {
                app.add_paths(files.clone());
            }
            if let Some(p) = editor_file.clone() {
                app.open_preview_path(p);
            }
            Ok(Box::new(app))
        }),
    )
}