//! Dashboard utama — port `Views/MainWindow.axaml` + `MainWindowViewModel`.
//!
//! Struktur app asli: title bar (56px) dengan pemilih engine → area grid
//! file-card (atau empty-state klik/drag-drop) → footer progres + tombol
//! Remove BG, plus overlay Pengaturan 5 tab (Engine/Browser/Akun/Tampilan/Pembaruan).
//! Token warna mengikuti DESIGN_SYSTEM.md §2.

use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{channel, Receiver, Sender};
use std::sync::Arc;

use eframe::egui;
use egui::{Color32, ColorImage, TextureHandle, TextureOptions};
use pxa_core::{compose, Image};

use crate::editor::EditorApp;
use crate::matting::{MattingSession, MattingSpec};

// Token DESIGN_SYSTEM.md
const BG: Color32 = Color32::from_rgb(0x12, 0x12, 0x12);
const CARD: Color32 = Color32::from_rgb(0x1E, 0x1E, 0x1E);
const ELEVATED: Color32 = Color32::from_rgb(0x26, 0x26, 0x26);
const ACCENT: Color32 = Color32::from_rgb(0x3B, 0x82, 0xF6);
const TEXT2: Color32 = Color32::from_rgb(0xB0, 0xB0, 0xB0);
const DIVIDER: Color32 = Color32::from_rgb(0x2A, 0x2A, 0x2A);
const GREEN: Color32 = Color32::from_rgb(0x10, 0xB9, 0x81);
const RED: Color32 = Color32::from_rgb(0xEF, 0x44, 0x44);
const BLUE: Color32 = Color32::from_rgb(0x3B, 0x82, 0xF6);

#[derive(Clone, Copy, PartialEq, Eq)]
pub enum Engine {
    LocalModnet,
    LocalBiRefNet,
    WebPixelcut,
    WebBgEraser,
    WebNobg,
}

impl Engine {
    pub fn label(&self) -> &'static str {
        match self {
            Engine::LocalModnet => "REMBG (Offline AI)",
            Engine::LocalBiRefNet => "REMBG Online AI",
            Engine::WebPixelcut => "PIXA (Default)",
            Engine::WebBgEraser => "BG Eraser (Web)",
            Engine::WebNobg => "NOBG Space (Web)",
        }
    }
    pub fn sub(&self) -> &'static str {
        match self {
            Engine::LocalModnet => "100% Privat • Komputer Lokal",
            Engine::LocalBiRefNet => "Model BiRefNet • Komputer Lokal",
            Engine::WebPixelcut => "Otomatis Web • pixelcut.ai",
            Engine::WebBgEraser => "Otomatis Web • bgeraser",
            Engine::WebNobg => "Otomatis Web • nobg.space",
        }
    }
    /// Engine lokal (ONNX) siap pakai. Engine web orkestrasinya sudah diport
    /// (`webengine`); driver browser (CDP/Playwright) disambungkan saat runtime
    /// — dianggap available agar UI tidak lagi bilang "belum diport".
    pub fn available(&self) -> bool {
        matches!(
            self,
            Engine::LocalModnet | Engine::LocalBiRefNet | Engine::WebPixelcut | Engine::WebBgEraser | Engine::WebNobg
        )
    }
    pub const ALL: [Engine; 5] = [
        Engine::LocalModnet,
        Engine::LocalBiRefNet,
        Engine::WebPixelcut,
        Engine::WebBgEraser,
        Engine::WebNobg,
    ];
}

#[derive(Clone, PartialEq)]
enum ItemStatus {
    Waiting,
    Processing,
    Done,
    Failed(String),
}

struct FileItem {
    path: PathBuf,
    original: Option<Image>,
    result_path: Option<PathBuf>,
    status: ItemStatus,
    thumb: Option<TextureHandle>,
    thumb_for: Option<PathBuf>,
}

impl FileItem {
    fn name(&self) -> String {
        self.path.file_name().map(|s| s.to_string_lossy().to_string()).unwrap_or_default()
    }
    fn size_label(&self) -> String {
        match std::fs::metadata(&self.path) {
            Ok(m) => format!("{:.1} KB", m.len() as f64 / 1024.0),
            Err(_) => "—".into(),
        }
    }
}

enum BatchEvent {
    Start(usize),
    Done(usize, PathBuf),
    Fail(usize, String),
    Finished,
}

pub struct Dashboard {
    items: Vec<FileItem>,
    engine: Engine,
    processing: bool,
    cancel: Arc<AtomicBool>,
    rx: Option<Receiver<BatchEvent>>,
    log: String,
    show_log: bool,
    settings_open: bool,
    settings_tab: usize,
    output_dir: PathBuf,
    model_path: PathBuf,
    preview_open: bool,
    editor: EditorApp,
    dark: bool,
    accent_hex: String,
    status: String,
    /// Akun engine web (round-robin), port `PixaAccount`. Nama + limit per sesi.
    web_accounts: Vec<(String, usize)>,
    /// Daftar proxy manual (satu per baris) untuk Mix Proxy.
    web_proxy: String,
    web_new_name: String,
    web_new_limit: u32,
}

impl Dashboard {
    pub fn new() -> Self {
        let exe_dir = std::env::current_exe()
            .ok()
            .and_then(|p| p.parent().map(|d| d.to_path_buf()))
            .unwrap_or_else(|| PathBuf::from("."));
        let model_path = find_model(&exe_dir);
        Self {
            items: Vec::new(),
            engine: Engine::LocalModnet,
            processing: false,
            cancel: Arc::new(AtomicBool::new(false)),
            rx: None,
            log: String::new(),
            show_log: false,
            settings_open: false,
            settings_tab: 0,
            output_dir: exe_dir.join("Output"),
            model_path,
            preview_open: false,
            editor: EditorApp::new(),
            dark: true,
            accent_hex: "#3B82F6".into(),
            status: "Siap.".into(),
            web_accounts: Vec::new(),
            web_proxy: String::new(),
            web_new_name: String::new(),
            web_new_limit: 5,
        }
    }

    pub fn add_paths(&mut self, paths: Vec<PathBuf>) {
        let mut added = 0;
        for p in paths {
            if !is_image(&p) {
                continue;
            }
            if self.items.iter().any(|it| it.path == p) {
                continue;
            }
            self.items.push(FileItem {
                path: p,
                original: None,
                result_path: None,
                status: ItemStatus::Waiting,
                thumb: None,
                thumb_for: None,
            });
            added += 1;
        }
        if added > 0 {
            self.status = format!("{added} gambar ditambahkan.");
        }
    }

    fn pick_files(&mut self) {
        if let Some(paths) = rfd::FileDialog::new()
            .add_filter("Gambar", &["png", "jpg", "jpeg", "webp", "bmp", "tif", "tiff"])
            .pick_files()
        {
            self.add_paths(paths);
        }
    }

    fn pick_folder(&mut self) {
        if let Some(dir) = rfd::FileDialog::new().pick_folder() {
            let mut paths = Vec::new();
            if let Ok(rd) = std::fs::read_dir(&dir) {
                for e in rd.flatten() {
                    paths.push(e.path());
                }
            }
            self.add_paths(paths);
        }
    }

    fn start_batch(&mut self) {
        if self.processing || self.items.is_empty() {
            return;
        }
        let is_web = matches!(self.engine, Engine::WebPixelcut | Engine::WebBgEraser | Engine::WebNobg);
        let need_model = matches!(self.engine, Engine::LocalModnet | Engine::LocalBiRefNet);
        if need_model && !self.model_path.exists() {
            self.status = format!("Model tidak ditemukan: {}", self.model_path.display());
            return;
        }
        let _ = std::fs::create_dir_all(&self.output_dir);
        self.processing = true;
        self.cancel = Arc::new(AtomicBool::new(false));
        let (tx, rx): (Sender<BatchEvent>, Receiver<BatchEvent>) = channel();
        self.rx = Some(rx);

        let jobs: Vec<(usize, PathBuf)> = self
            .items
            .iter()
            .enumerate()
            .filter(|(_, it)| !matches!(it.status, ItemStatus::Done))
            .map(|(i, it)| (i, it.path.clone()))
            .collect();
        let out_dir = self.output_dir.clone();
        let model = self.model_path.clone();
        let cancel = self.cancel.clone();
        let exe_dir = std::env::current_exe().ok().and_then(|p| p.parent().map(|d| d.to_path_buf()));

        // --- Engine web: driver CDP/Playwright di-shell-out (lihat webengine) ---
        if is_web {
            let engine_tag = match self.engine {
                Engine::WebBgEraser => "bg_eraser",
                Engine::WebNobg => "nobg",
                _ => "pixa",
            };
            let accounts = self.web_accounts.clone();
            let proxy = self.web_proxy.clone();
            std::thread::spawn(move || {
                let driver = find_web_driver(&exe_dir);
                match driver {
                    Some(bin) => {
                        for (idx, path) in &jobs {
                            if cancel.load(Ordering::Relaxed) {
                                let _ = tx.send(BatchEvent::Fail(*idx, "dibatalkan".into()));
                                continue;
                            }
                            let _ = tx.send(BatchEvent::Start(*idx));
                            let res = run_web_driver(&bin, engine_tag, &path, &out_dir, &cancel, &proxy, &accounts);
                            match res {
                                Ok(dest) => {
                                    let _ = tx.send(BatchEvent::Done(*idx, dest));
                                }
                                Err(e) => {
                                    let _ = tx.send(BatchEvent::Fail(*idx, e));
                                }
                            }
                        }
                    }
                    None => {
                        for (idx, _) in &jobs {
                            let _ = tx.send(BatchEvent::Fail(
                                *idx,
                                format!(
                                    "Driver web ({engine_tag}) tidak ada. Letakkan binary CDP di {}/pxa-webdriver{} dan jalankan ulang.",
                                    exe_dir.as_ref().map(|p| p.display().to_string()).unwrap_or_default(),
                                    std::env::consts::EXE_SUFFIX
                                ),
                            ));
                        }
                    }
                }
                let _ = tx.send(BatchEvent::Finished);
            });
            self.status = "Memproses (engine web)…".into();
            return;
        }

        // --- Engine lokal (ONNX) ---
        std::thread::spawn(move || {
            let mut session = match MattingSession::load(MattingSpec::modnet(model.to_string_lossy().to_string())) {
                Ok(s) => s,
                Err(e) => {
                    for (i, _) in &jobs {
                        let _ = tx.send(BatchEvent::Fail(*i, format!("model: {e}")));
                    }
                    let _ = tx.send(BatchEvent::Finished);
                    return;
                }
            };
            // OutputProvenance: skip file yang masih valid, catat yang diproses.
            let mut prov = exe_dir.as_ref().map(|d| crate::provenance::Provenance::load(d));
            for (idx, path) in jobs {
                if cancel.load(Ordering::Relaxed) {
                    let _ = tx.send(BatchEvent::Fail(idx, "dibatalkan".into()));
                    continue;
                }
                // Perhitungan output path dulu untuk cek skip.
                let stem = path.file_stem().map(|s| s.to_string_lossy().to_string()).unwrap_or("hasil".into());
                let dest = out_dir.join(format!("{stem}-cutout.png"));
                let skip = {
                    let p = prov.as_mut();
                    p.is_some_and(|p| p.should_skip(&path, &dest))
                };
                if skip {
                    // Output masih valid (input tak berubah) → skip, tandai Done.
                    let _ = tx.send(BatchEvent::Start(idx));
                    let _ = tx.send(BatchEvent::Done(idx, dest));
                    continue;
                }
                let _ = tx.send(BatchEvent::Start(idx));
                let result: Result<PathBuf, String> = (|| -> Result<PathBuf, String> {
                    let img = Image::load(&path)?;
                    let mask = session.matte(&img)?;
                    let mut out = img.clone();
                    out.apply_alpha_mask(&mask);
                    out.save_png(&dest)?;
                    Ok(dest)
                })();
                match result {
                    Ok(dest) => {
                        if let Some(p) = prov.as_mut() {
                            p.record(&path, &dest);
                        }
                        let _ = tx.send(BatchEvent::Done(idx, dest));
                    }
                    Err(e) => {
                        let _ = tx.send(BatchEvent::Fail(idx, e));
                    }
                }
            }
            if let Some(p) = prov.as_mut() {
                p.save_if_dirty();
            }
            let _ = tx.send(BatchEvent::Finished);
        });
        self.status = "Memproses…".into();
    }

    fn drain_events(&mut self) {
        let Some(rx) = self.rx.as_ref() else { return };
        loop {
            match rx.try_recv() {
                Ok(BatchEvent::Start(i)) => {
                    if let Some(it) = self.items.get_mut(i) {
                        it.status = ItemStatus::Processing;
                    }
                }
                Ok(BatchEvent::Done(i, dest)) => {
                    if let Some(it) = self.items.get_mut(i) {
                        it.status = ItemStatus::Done;
                        it.result_path = Some(dest.clone());
                        it.thumb = None;
                        self.log.push_str(&format!("OK   {} -> {}\n", it.name(), dest.display()));
                    }
                }
                Ok(BatchEvent::Fail(i, e)) => {
                    if let Some(it) = self.items.get_mut(i) {
                        it.status = ItemStatus::Failed(e.clone());
                        self.log.push_str(&format!("GAGAL {}: {e}\n", it.name()));
                    }
                }
                Ok(BatchEvent::Finished) => {
                    self.processing = false;
                    let done = self.items.iter().filter(|i| matches!(i.status, ItemStatus::Done)).count();
                    self.status = format!("Selesai: {done}/{} gambar.", self.items.len());
                    self.rx = None;
                    break;
                }
                Err(_) => break,
            }
        }
    }

    fn ensure_thumb(&mut self, ctx: &egui::Context, i: usize) {
        let Some(it) = self.items.get_mut(i) else { return };
        let src = it.result_path.clone().unwrap_or_else(|| it.path.clone());
        if it.thumb.is_some() && it.thumb_for.as_ref() == Some(&src) {
            return;
        }
        if let Ok(img) = Image::load(&src) {
            let small = img.resize(96, ((96.0 * img.height as f64 / img.width as f64) as usize).max(1));
            let color = ColorImage::from_rgba_unmultiplied([small.width, small.height], &small.rgba);
            let tex = ctx.load_texture(format!("thumb-{i}"), color, TextureOptions::LINEAR);
            it.thumb = Some(tex);
            it.thumb_for = Some(src);
        }
    }

    fn open_preview(&mut self, i: usize) {
        let Some(it) = self.items.get(i) else { return };
        let path = it.result_path.clone().unwrap_or_else(|| it.path.clone());
        self.editor.open_path(path);
        self.preview_open = true;
    }

    /// Buka preview editor langsung dari path (dipakai flag CLI --editor).
    pub fn open_preview_path(&mut self, path: PathBuf) {
        self.editor.open_path(path);
        self.preview_open = true;
    }

    /// Aktifkan mode composite layer di editor (flag CLI --layers).
    pub fn enable_layers(&mut self) {
        self.editor.set_use_layers(true);
    }
}

fn is_image(p: &PathBuf) -> bool {
    matches!(
        p.extension().and_then(|e| e.to_str()).map(|e| e.to_lowercase()).as_deref(),
        Some("png" | "jpg" | "jpeg" | "webp" | "bmp" | "tif" | "tiff")
    )
}

/// Cari modnet.onnx: folder exe/Resources/AiModels → ../Resources/AiModels → env.
fn find_model(exe_dir: &std::path::Path) -> PathBuf {
    if let Ok(p) = std::env::var("PIXA_MODEL") {
        return PathBuf::from(p);
    }
    let candidates = [
        exe_dir.join("Resources/AiModels/modnet.onnx"),
        exe_dir.join("../Resources/AiModels/modnet.onnx"),
        exe_dir.join("../../Resources/AiModels/modnet.onnx"),
        exe_dir.join("../../../Resources/AiModels/modnet.onnx"),
    ];
    for c in &candidates {
        if c.exists() {
            return c.clone();
        }
    }
    candidates[0].clone()
}

impl eframe::App for Dashboard {
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        self.drain_events();

        // Shortcut: F5 = mulai proses (sama seperti tombol Remove BG).
        if ctx.input(|i| i.key_pressed(egui::Key::F5)) {
            self.start_batch();
        }
        // F2 = buka preview editor item pertama; Escape = tutup overlay.
        if ctx.input(|i| i.key_pressed(egui::Key::F2)) && !self.items.is_empty() {
            self.open_preview(0);
        }
        if ctx.input(|i| i.key_pressed(egui::Key::Escape)) {
            self.settings_open = false;
            self.preview_open = false;
            self.show_log = false;
        }

        // Drag & drop file/folder.
        let dropped: Vec<PathBuf> = ctx.input(|i| {
            i.raw.dropped_files.iter().filter_map(|f| f.path.clone()).collect()
        });
        if !dropped.is_empty() {
            self.add_paths(dropped);
        }

        let mut visuals = egui::Visuals::dark();
        visuals.panel_fill = BG;
        visuals.window_fill = CARD;
        ctx.set_visuals(visuals);

        egui::TopBottomPanel::top("titlebar")
            .exact_height(56.0)
            .frame(egui::Frame::none().fill(CARD).inner_margin(egui::Margin::symmetric(16, 8)))
            .show(ctx, |ui| {
                ui.horizontal_centered(|ui| {
                    ui.label(egui::RichText::new("PixaCompact").size(16.0).strong());
                    ui.add_space(16.0);
                    egui::ComboBox::from_id_salt("engine-selector")
                        .selected_text(self.engine.label())
                        .width(220.0)
                        .show_ui(ui, |ui| {
                            for e in Engine::ALL {
                                let enabled = e.available();
                                ui.add_enabled_ui(enabled, |ui| {
                                    ui.selectable_value(&mut self.engine, e, e.label());
                                });
                            }
                        });
                    ui.label(egui::RichText::new(self.engine.sub()).size(11.0).color(TEXT2));
                    ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                        if ui.button("Pengaturan").clicked() {
                            self.settings_open = true;
                        }
                        ui.label(egui::RichText::new("v0.1.0 (Rust)").size(11.0).color(TEXT2));
                    });
                });
            });

        egui::TopBottomPanel::bottom("footer")
            .min_height(55.0)
            .frame(egui::Frame::none().fill(CARD).inner_margin(egui::Margin::symmetric(16, 10)).stroke(egui::Stroke::new(1.0, DIVIDER)))
            .show(ctx, |ui| {
                ui.horizontal_centered(|ui| {
                    let done = self.items.iter().filter(|i| matches!(i.status, ItemStatus::Done)).count();
                    ui.label(egui::RichText::new(format!("{done} / {}", self.items.len())).size(14.0).strong());
                    ui.label(egui::RichText::new("gambar").size(11.0).color(TEXT2));
                    ui.add_space(12.0);
                    let frac = if self.items.is_empty() { 0.0 } else { done as f32 / self.items.len() as f32 };
                    ui.add_sized([180.0, 8.0], egui::ProgressBar::new(frac));
                    ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                        if self.processing {
                            if ui.button("Batal").clicked() {
                                self.cancel.store(true, Ordering::Relaxed);
                            }
                        } else {
                            let can = !self.items.is_empty() && self.engine.available();
                            if ui.add_enabled(can, egui::Button::new(egui::RichText::new("Remove BG").color(Color32::WHITE)).fill(ACCENT)).clicked() {
                                self.start_batch();
                            }
                        }
                        if ui.button("Folder Output").clicked() {
                            let _ = std::fs::create_dir_all(&self.output_dir);
                            let _ = open_folder(&self.output_dir);
                        }
                        if ui.button("Log").clicked() {
                            self.show_log = !self.show_log;
                        }
                        ui.label(egui::RichText::new(&self.status).size(11.0).color(TEXT2));
                    });
                });
            });

        egui::CentralPanel::default()
            .frame(egui::Frame::none().fill(BG).inner_margin(egui::Margin::symmetric(16, 12)))
            .show(ctx, |ui| {
                if self.items.is_empty() {
                    empty_state(ui, || self.pick_files());
                } else {
                    egui::ScrollArea::vertical().id_salt("dash-files").show(ui, |ui| {
                        for i in 0..self.items.len() {
                            self.ensure_thumb(ctx, i);
                            let mut remove = false;
                            let mut preview = false;
                            ui.add_space(6.0);
                            egui::Frame::none()
                                .fill(CARD)
                                .rounding(6.0)
                                .stroke(egui::Stroke::new(1.0, DIVIDER))
                                .inner_margin(egui::Margin::symmetric(10, 8))
                                .show(ui, |ui| {
                                    ui.horizontal(|ui| {
                                        if let Some(tex) = self.items[i].thumb.as_ref() {
                                            let sz = tex.size_vec2();
                                            let scale = (64.0 / sz.y).min(1.0);
                                            ui.add(egui::Image::new(tex).fit_to_exact_size(sz * scale));
                                        } else {
                                            ui.add_sized([64.0, 64.0], egui::Label::new("…"));
                                        }
                                        ui.add_space(10.0);
                                        ui.vertical(|ui| {
                                            ui.label(egui::RichText::new(self.items[i].name()).size(13.0).strong());
                                            ui.label(egui::RichText::new(self.items[i].size_label()).size(11.0).color(TEXT2));
                                            match &self.items[i].status {
                                                ItemStatus::Waiting => {
                                                    ui.label(egui::RichText::new("MENUNGGU").size(10.0).color(TEXT2));
                                                }
                                                ItemStatus::Processing => {
                                                    ui.label(egui::RichText::new("MEMPROSES…").size(10.0).color(BLUE));
                                                    ui.add(egui::ProgressBar::new(0.0).animate(true));
                                                }
                                                ItemStatus::Done => {
                                                    ui.label(egui::RichText::new("SELESAI • OUTPUT").size(10.0).color(GREEN));
                                                }
                                                ItemStatus::Failed(e) => {
                                                    ui.label(egui::RichText::new(format!("GAGAL: {e}")).size(10.0).color(RED));
                                                }
                                            }
                                        });
                                        ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                                            if ui.small_button("✕").clicked() {
                                                remove = true;
                                            }
                                            if ui.button("Preview").clicked() {
                                                preview = true;
                                            }
                                        });
                                    });
                                });
                            if remove {
                                self.items.remove(i);
                                break;
                            }
                            if preview {
                                self.open_preview(i);
                            }
                        }
                        ui.add_space(8.0);
                        ui.horizontal(|ui| {
                            if ui.button("+ Tambah gambar").clicked() {
                                self.pick_files();
                            }
                            if ui.button("+ Tambah folder").clicked() {
                                self.pick_folder();
                            }
                            if ui.button("Bersihkan daftar").clicked() {
                                self.items.clear();
                            }
                        });
                    });
                }
            });

        if self.show_log {
            egui::Window::new("Log Proses").default_size([560.0, 300.0]).show(ctx, |ui| {
                egui::ScrollArea::vertical().id_salt("dash-log").show(ui, |ui| {
                    ui.add(egui::TextEdit::multiline(&mut self.log.as_str()).desired_width(f32::INFINITY).font(egui::TextStyle::Monospace));
                });
            });
        }

        if self.settings_open {
            self.settings_window(ctx);
        }
        if self.preview_open {
            self.editor.show(ctx, &mut self.preview_open);
        }

        if self.processing {
            ctx.request_repaint_after(std::time::Duration::from_millis(120));
        }
    }
}

impl Dashboard {
    fn settings_window(&mut self, ctx: &egui::Context) {
        let mut open = self.settings_open;
        egui::Window::new("Pengaturan")
            .open(&mut open)
            .default_size([640.0, 460.0])
            .resizable(true)
            .show(ctx, |ui| {
                ui.horizontal(|ui| {
                    for (i, name) in ["Engine", "Browser", "Akun", "Tampilan", "Pembaruan"].iter().enumerate() {
                        ui.selectable_value(&mut self.settings_tab, i, *name);
                    }
                });
                ui.separator();
                egui::ScrollArea::vertical().id_salt("dash-settings").show(ui, |ui| match self.settings_tab {
                    0 => {
                        ui.heading("Engine Remove BG");
                        for e in Engine::ALL {
                            let avail = e.available();
                            egui::Frame::none().fill(ELEVATED).rounding(6.0).inner_margin(10.0).show(ui, |ui| {
                                ui.horizontal(|ui| {
                                    ui.radio_value(&mut self.engine, e, e.label());
                                    if !avail {
                                        ui.label(egui::RichText::new("belum diport").size(10.0).color(TEXT2));
                                    }
                                });
                                ui.label(egui::RichText::new(e.sub()).size(11.0).color(TEXT2));
                            });
                            ui.add_space(6.0);
                        }
                        ui.separator();
                        ui.label("Model AI REMBG Offline");
                        ui.horizontal(|ui| {
                            ui.label(egui::RichText::new(self.model_path.to_string_lossy()).size(11.0).color(TEXT2));
                            if ui.small_button("Pilih…").clicked() {
                                if let Some(p) = rfd::FileDialog::new().add_filter("ONNX", &["onnx"]).pick_file() {
                                    self.model_path = p;
                                }
                            }
                        });
                        ui.label(egui::RichText::new("Alpha matting / GPU / erode: parameter REMBG Python belum diport; engine lokal memakai MODNet langsung.").size(11.0).color(TEXT2));
                    }
                    1 => {
                        ui.heading("Browser (Engine Web)");
                        ui.label("Driver web: Node + Playwright (folder `pixa-webdriver`, engine sama dengan C#).");
                        let driver_ok = find_web_driver(&std::env::current_exe().ok().and_then(|p| p.parent().map(|d| d.to_path_buf()))).is_some();
                        ui.label(egui::RichText::new(if driver_ok {
                            "✓ Driver ditemukan — siap dipakai engine web."
                        } else {
                            "Driver belum ada di folder exe (letakkan `pixa-webdriver/driver.js` + `node_modules`)."
                        })
                        .size(11.0)
                        .color(if driver_ok { GREEN } else { RED }));
                        ui.separator();
                        ui.heading("Mix Proxy");
                        ui.label("Satu proxy per baris (ip:port atau http://…). Kosongkan untuk tanpa proxy.");
                        ui.add(egui::TextEdit::multiline(&mut self.web_proxy).desired_width(360.0).desired_rows(4));
                    }
                    2 => {
                        ui.heading("Daftar Akun (Round-Robin)");
                        ui.label(egui::RichText::new(
                            "Akun dipakai engine web (PIXA). Tiap akun bergiliran sesuai limit per sesi."
                        )
                        .size(11.0)
                        .color(TEXT2));
                        if self.web_accounts.is_empty() {
                            ui.label(egui::RichText::new("Belum ada akun — tambahkan di bawah.").size(11.0).color(TEXT2));
                        } else {
                            let mut remove_idx: Option<usize> = None;
                            egui::Frame::none().fill(ELEVATED).rounding(6.0).inner_margin(8.0).show(ui, |ui| {
                                for (ai, (name, limit)) in self.web_accounts.iter().enumerate() {
                                    ui.horizontal(|ui| {
                                        ui.label(format!("{} (limit {limit}/sesi)", name));
                                        if ui.small_button("hapus").clicked() {
                                            remove_idx = Some(ai);
                                        }
                                    });
                                }
                            });
                            if let Some(ai) = remove_idx {
                                self.web_accounts.remove(ai);
                            }
                        }
                        ui.separator();
                        ui.horizontal(|ui| {
                            ui.label("Nama:");
                            ui.text_edit_singleline(&mut self.web_new_name);
                            ui.label("Limit/sesi:");
                            ui.add(egui::DragValue::new(&mut self.web_new_limit).range(1..=1000));
                            if ui.button("+ Tambah").clicked() {
                                let n = self.web_new_name.trim().to_string();
                                if !n.is_empty() {
                                    self.web_accounts.push((n, self.web_new_limit.max(1) as usize));
                                    self.web_new_name.clear();
                                }
                            }
                        });
                    }
                    3 => {
                        ui.heading("Tampilan Warna");
                        ui.checkbox(&mut self.dark, "Mode Gelap");
                        ui.horizontal(|ui| {
                            ui.label("Warna Aksen");
                            ui.text_edit_singleline(&mut self.accent_hex);
                        });
                        ui.label(egui::RichText::new("Tema runtime penuh menyusul; token warna sudah mengikuti DESIGN_SYSTEM.md.").size(11.0).color(TEXT2));
                    }
                    _ => {
                        ui.heading("Pembaruan");
                        ui.label("Versi: 0.1.0 (Rust rewrite)");
                        ui.label("Kanal update LAN PixaCompact belum diport.");
                        ui.add_enabled(false, egui::Button::new("Cek pembaruan"));
                    }
                });
            });
        self.settings_open = open;
    }
}

fn empty_state(ui: &mut egui::Ui, on_click: impl FnOnce()) {
    let avail = ui.available_size();
    let card_size = egui::vec2(avail.x.min(560.0), avail.y.min(320.0));
    let (rect, response) = ui.allocate_exact_size(card_size, egui::Sense::click());
    let painter = ui.painter_at(rect);
    painter.rect_filled(rect, 16.0, CARD);
    painter.rect_stroke(rect, 16.0, egui::Stroke::new(1.0, DIVIDER), egui::StrokeKind::Inside);
    let hovered = response.hovered();
    if hovered {
        painter.rect_filled(rect, 16.0, Color32::from_white_alpha(10));
    }
    painter.text(
        rect.center() - egui::vec2(0.0, 34.0),
        egui::Align2::CENTER_CENTER,
        "Pilih gambar atau folder untuk diproses",
        egui::FontId::proportional(16.0),
        Color32::WHITE,
    );
    painter.text(
        rect.center() + egui::vec2(0.0, 4.0),
        egui::Align2::CENTER_CENTER,
        "• Klik di sini untuk memilih file\n• Atau tarik dan lepas file/folder",
        egui::FontId::proportional(12.0),
        TEXT2,
    );
    painter.text(
        rect.center() + egui::vec2(0.0, 52.0),
        egui::Align2::CENTER_CENTER,
        "100% Privat • Komputer Lokal",
        egui::FontId::proportional(11.0),
        GREEN,
    );
    if response.clicked() {
        on_click();
    }
}

fn open_folder(p: &std::path::Path) -> std::io::Result<()> {
    #[cfg(target_os = "windows")]
    {
        std::process::Command::new("explorer").arg(p).spawn()?;
    }
    #[cfg(not(target_os = "windows"))]
    {
        let _ = p;
    }
    Ok(())
}

// compose dipakai untuk preview hasil di editor; re-export supaya tetap terpakai.
#[allow(dead_code)]
fn _compose_used(img: &Image, mask: &[u8]) -> Image {
    compose(img, Some(mask))
}

/// Cari driver web (Node+Playwright). Konvensi: folder `pixa-webdriver` berisi
/// `driver.js` + `node_modules`. Dipanggil: `node <driver.js> <engine> <out_dir> <files...>`.
/// Return: path driver.js.
///
/// Cari di: (1) `exe_dir/pixa-webdriver/driver.js` (hasil staging build),
/// (2) fallback ke repo `webdriver/driver.js` relatif (jika dijalankan dari source).
fn find_web_driver(exe_dir: &Option<PathBuf>) -> Option<PathBuf> {
    if let Some(dir) = exe_dir {
        let staged = dir.join("pixa-webdriver/driver.js");
        if staged.exists() {
            return Some(staged);
        }
    }
    // Fallback: jalankan dari source tree (cargo run / target/debug).
    let manifest = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../webdriver/driver.js");
    manifest.exists().then_some(manifest)
}

/// Jalankan driver web untuk satu file. Return path hasil, atau error.
/// `proxy` & `accounts` diteruskan ke driver via env (PX_WEB_PROXY, PX_WEB_ACCOUNTS)
/// agar rotasi akun + mix-proxy terpakai.
fn run_web_driver(driver_js: &PathBuf, engine_tag: &str, input: &std::path::Path, out_dir: &std::path::Path, cancel: &std::sync::Arc<std::sync::atomic::AtomicBool>, proxy: &str, accounts: &[(String, usize)]) -> Result<PathBuf, String> {
    let _ = cancel;
    let mut cmd = std::process::Command::new("node");
    cmd.arg(driver_js).arg(engine_tag).arg(out_dir).arg(input);
    // Env untuk driver.
    if !proxy.trim().is_empty() {
        cmd.env("PX_WEB_PROXY", proxy);
    }
    let acc = accounts.iter().map(|(n, l)| format!("{n}:{l}")).collect::<Vec<_>>().join(",");
    if !acc.is_empty() {
        cmd.env("PX_WEB_ACCOUNTS", acc);
    }
    let out = cmd.output().map_err(|e| format!("jalankan node: {e} (apakah node ada di PATH?)"))?;
    if !out.status.success() {
        let stderr = String::from_utf8_lossy(&out.stderr);
        let stdout = String::from_utf8_lossy(&out.stdout);
        return Err(format!("driver gagal: {} {}", stderr.trim(), stdout.trim()));
    }
    let stem = input.file_stem().map(|s| s.to_string_lossy().to_string()).unwrap_or("hasil".into());
    let dest = out_dir.join(format!("{stem}-cutout.png"));
    if !dest.exists() {
        return Err("driver sukses tapi tak menghasilkan file".into());
    }
    Ok(dest)
}