//! PixaCompact (Rust) — entry point.
//! `pixa [gambar...]` membuka dashboard; file arg langsung masuk daftar.

use std::path::PathBuf;

use eframe::egui;
use pxa_app::dashboard::Dashboard;

fn main() -> eframe::Result<()> {
    // Port `Program.cs`: tangkap crash tak-terangani → `BugReporter.ReportCrash`.
    std::panic::set_hook(Box::new(|info| {
        let ex = info
            .payload()
            .downcast_ref::<&str>()
            .map(|s| s.to_string())
            .or_else(|| info.payload().downcast_ref::<String>().map(|s| s.clone()))
            .unwrap_or_else(|| "panic tanpa payload".into());
        pxa_app::bugreport::report_crash(
            "PixelcutCompact",
            &ex,
            Some(std::env::var("PXA_VERSION").as_deref().unwrap_or("0.1.0")),
            "unhandled",
        );
    }));

    let raw: Vec<String> = std::env::args().skip(1).collect();
    // `pixa --editor <file>` langsung buka preview editor (tanpa klik).
    let mut editor_file: Option<PathBuf> = None;
    let mut files: Vec<PathBuf> = Vec::new();
    let mut use_layers = false;
    let mut float_tools = false;
    let mut it = raw.into_iter();
    while let Some(a) = it.next() {
        match a.as_str() {
            "--editor" => {
                editor_file = it.next().map(PathBuf::from);
            }
            "--layers" => use_layers = true,
            "--float-tools" => float_tools = true,
            s if s.starts_with("--") => {} // flag tak dikenal: abaikan
            _ => files.push(PathBuf::from(a)),
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
                if use_layers {
                    app.enable_layers();
                }
                if float_tools {
                    app.enable_floating_tools();
                }
            }
            Ok(Box::new(app))
        }),
    )
}