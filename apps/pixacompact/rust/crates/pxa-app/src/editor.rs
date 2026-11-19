//! PixaCompact — port Rust dari app Avalonia (`apps/pixacompact`).
//!
//! Sudah ada: galeri + editor kanvas, mask cutout, brush erase/restore, seleksi
//! (rect/elips/lasso/magic wand) + combine mode, operasi mask
//! (invert/grow/shrink/feather/defringe), undo/redo, ekspor PNG.
//!
//! ponytail: belum ada background-removal otomatis (C# memakai otomasi
//! pixelcut.ai lewat Playwright). Tambah jalur ONNX rembg/BiRefNet kalau mesin
//! itu dibutuhkan; engine mask di bawah tidak perlu berubah.

use std::path::PathBuf;

use eframe::egui;
use egui::{Color32, ColorImage, Pos2, Rect, Sense, Stroke, TextureHandle, TextureOptions, Vec2};
use pxa_core::{
    brush::{self, BrushStamp},
    compose, image_buf::Image, mask as mask_ops, raster::Region, selection, CombineMode,
    SelectionState,
};

const MAX_UNDO: usize = 24;

#[derive(Clone, Copy, PartialEq, Eq)]
enum Tool {
    Brush,
    Restore,
    Rect,
    Ellipse,
    Lasso,
    Wand,
    Pan,
}

impl Tool {
    fn label(&self) -> &'static str {
        match self {
            Tool::Brush => "Brush (hapus)",
            Tool::Restore => "Brush (pulihkan)",
            Tool::Rect => "Seleksi kotak",
            Tool::Ellipse => "Seleksi elips",
            Tool::Lasso => "Seleksi laso",
            Tool::Wand => "Magic wand",
            Tool::Pan => "Geser kanvas",
        }
    }
}

struct Doc {
    path: PathBuf,
    original: Image,
    mask: Vec<u8>,
    selection: SelectionState,
    undo: Vec<Vec<u8>>,
    redo: Vec<Vec<u8>>,
    texture: Option<TextureHandle>,
    dirty: bool,
    contour_version: u64,
    contours: Vec<Vec<[f64; 2]>>,
}

impl Doc {
    fn new(path: PathBuf, original: Image) -> Self {
        let mask = vec![255u8; original.width * original.height];
        let selection = SelectionState::new(original.width, original.height);
        Self {
            path,
            original,
            mask,
            selection,
            undo: Vec::new(),
            redo: Vec::new(),
            texture: None,
            dirty: true,
            contour_version: u64::MAX,
            contours: Vec::new(),
        }
    }

    fn push_undo(&mut self) {
        self.undo.push(self.mask.clone());
        if self.undo.len() > MAX_UNDO {
            self.undo.remove(0);
        }
        self.redo.clear();
    }

    fn undo(&mut self) {
        if let Some(prev) = self.undo.pop() {
            self.redo.push(self.mask.clone());
            self.mask = prev;
            self.dirty = true;
        }
    }

    fn redo(&mut self) {
        if let Some(next) = self.redo.pop() {
            self.undo.push(self.mask.clone());
            self.mask = next;
            self.dirty = true;
        }
    }

    fn refresh_contours(&mut self) {
        if self.contour_version == self.selection.version {
            return;
        }
        self.contours = selection::trace(
            &self.selection.coverage,
            self.selection.width,
            self.selection.height,
            selection::DEFAULT_THRESHOLD,
        );
        self.contour_version = self.selection.version;
    }

    /// Terapkan coverage seleksi ke mask (0 = buang area, 255 = isi/pulihkan).
    fn apply_selection(&mut self, target: u8) {
        if !self.selection.has_selection() {
            return;
        }
        self.push_undo();
        for i in 0..self.mask.len() {
            let c = self.selection.coverage[i] as f64 / 255.0;
            if c <= 0.0 {
                continue;
            }
            let cur = self.mask[i] as f64;
            self.mask[i] = (cur + (target as f64 - cur) * c + 0.5) as u8;
        }
        self.dirty = true;
    }
}

pub struct EditorApp {
    docs: Vec<Doc>,
    current: usize,
    tool: Tool,
    brush_radius: f32,
    brush_hardness: f32,
    brush_opacity: f32,
    combine: CombineMode,
    feather_px: i32,
    tolerance: u8,
    zoom: f32,
    offset: Vec2,
    fit_pending: bool,
    canvas_min: Pos2,
    drag_start: Option<Pos2>,
    drag_current: Option<Pos2>,
    lasso_points: Vec<[f64; 2]>,
    last_stamp: Option<Pos2>,
    status: String,
    bg_color: [u8; 3],
    flatten_bg: bool,
}

impl EditorApp {
    pub fn new() -> Self {
        let mut app = Self {
            docs: Vec::new(),
            current: 0,
            tool: Tool::Brush,
            brush_radius: 40.0,
            brush_hardness: 0.6,
            brush_opacity: 0.9,
            combine: CombineMode::Replace,
            feather_px: 0,
            tolerance: 12,
            zoom: 1.0,
            offset: Vec2::ZERO,
            fit_pending: true,
            canvas_min: Pos2::ZERO,
            drag_start: None,
            drag_current: None,
            lasso_points: Vec::new(),
            last_stamp: None,
            status: "Buka gambar untuk mulai.".to_owned(),
            bg_color: [255, 255, 255],
            flatten_bg: false,
        };
        // `pixa <file>...` — buka langsung dari command line.
        for arg in std::env::args().skip(1) {
            let path = PathBuf::from(&arg);
            match Image::load(&path) {
                Ok(img) => {
                    app.docs.push(Doc::new(path, img.to_opaque_rgb()));
                    app.current = app.docs.len() - 1;
                    app.status = format!("Dimuat: {arg}");
                }
                Err(e) => app.status = format!("Gagal buka {arg}: {e}"),
            }
        }
        app
    }

    fn doc(&self) -> Option<&Doc> {
        self.docs.get(self.current)
    }

    fn doc_mut(&mut self) -> Option<&mut Doc> {
        self.docs.get_mut(self.current)
    }

    fn open_dialog(&mut self) {
        let Some(paths) = rfd::FileDialog::new()
            .add_filter("Gambar", &["png", "jpg", "jpeg", "webp", "bmp", "tif", "tiff"])
            .pick_files()
        else {
            return;
        };
        let mut ok = 0;
        for p in paths {
            match Image::load(&p) {
                Ok(img) => {
                    self.docs.push(Doc::new(p, img.to_opaque_rgb()));
                    ok += 1;
                }
                Err(e) => self.status = format!("Gagal buka {}: {e}", p.display()),
            }
        }
        if ok > 0 {
            self.current = self.docs.len() - 1;
            self.fit_pending = true;
            self.status = format!("{ok} gambar dimuat.");
        }
    }

    fn save_dialog(&mut self, copy: bool) {
        let Some(doc) = self.doc() else { return };
        let out = if self.flatten_bg {
            compose::compose_on_background(&doc.original, Some(&doc.mask), self.bg_color)
        } else {
            compose::compose(&doc.original, Some(&doc.mask))
        };
        let stem = doc
            .path
            .file_stem()
            .map(|s| s.to_string_lossy().to_string())
            .unwrap_or_else(|| "hasil".into());
        let default_name = if copy { format!("{stem}-edit.png") } else { format!("{stem}.png") };
        let path = rfd::FileDialog::new()
            .set_file_name(default_name)
            .add_filter("PNG", &["png"])
            .save_file();
        let Some(path) = path else { return };
        match out.save_png(&path) {
            Ok(()) => {
                self.status = format!("Disimpan: {}", path.display());
                if let Some(d) = self.doc_mut() {
                    if !copy {
                        d.path = path;
                    }
                }
            }
            Err(e) => self.status = format!("Gagal simpan: {e}"),
        }
    }

    fn fit_view(&mut self, canvas: Rect, doc_w: usize, doc_h: usize) {
        let z = (canvas.width() / doc_w as f32).min(canvas.height() / doc_h as f32).min(1.0);
        self.zoom = z.max(0.02);
        let size = Vec2::new(doc_w as f32 * self.zoom, doc_h as f32 * self.zoom);
        self.offset = (canvas.size() - size) * 0.5;
        self.fit_pending = false;
    }

    fn img_to_screen(&self, p: [f64; 2]) -> Pos2 {
        self.canvas_min + self.offset + Vec2::new(p[0] as f32 * self.zoom, p[1] as f32 * self.zoom)
    }

    fn screen_to_img(&self, p: Pos2) -> [f64; 2] {
        let d = (p - self.canvas_min - self.offset) / self.zoom;
        [d.x as f64, d.y as f64]
    }

    fn ensure_texture(&mut self, ctx: &egui::Context) {
        let Some(doc) = self.docs.get_mut(self.current) else { return };
        if !doc.dirty && doc.texture.is_some() {
            return;
        }
        let img = compose::compose(&doc.original, Some(&doc.mask));
        let color = ColorImage::from_rgba_unmultiplied([img.width, img.height], &img.rgba);
        let opts = if self.zoom >= 1.0 { TextureOptions::NEAREST } else { TextureOptions::LINEAR };
        match doc.texture.as_mut() {
            Some(t) => t.set(color, opts),
            None => doc.texture = Some(ctx.load_texture("composite", color, opts)),
        }
        doc.dirty = false;
    }

    fn paint_canvas(&mut self, ui: &mut egui::Ui) {
        let canvas = ui.available_rect_before_wrap();
        self.canvas_min = canvas.min;
        let response = ui.interact(canvas, ui.id().with("canvas"), Sense::click_and_drag());
        if self.docs.is_empty() {
            ui.painter().text(
                canvas.center(),
                egui::Align2::CENTER_CENTER,
                "Buka gambar (PNG/JPG/WebP) untuk mulai",
                egui::FontId::proportional(16.0),
                Color32::from_gray(150),
            );
            return;
        }
        if self.fit_pending {
            let (w, h) = (self.docs[self.current].original.width, self.docs[self.current].original.height);
            self.fit_view(canvas, w, h);
        }

        let painter = ui.painter_at(canvas);
        checkerboard(&painter, canvas);

        let (doc_w, doc_h) = (self.docs[self.current].original.width, self.docs[self.current].original.height);
        let size = Vec2::new(doc_w as f32 * self.zoom, doc_h as f32 * self.zoom);
        let img_rect = Rect::from_min_size(canvas.min + self.offset, size);
        if let Some(tex) = self.docs[self.current].texture.as_ref() {
            painter.image(
                tex.id(),
                img_rect,
                Rect::from_min_max(Pos2::ZERO, Pos2::new(1.0, 1.0)),
                Color32::WHITE,
            );
        }
        painter.rect_stroke(
            img_rect,
            0.0,
            Stroke::new(1.0_f32, Color32::from_gray(70)),
            egui::StrokeKind::Outside,
        );

        self.docs[self.current].refresh_contours();
        let contours = self.docs[self.current].contours.clone();
        for lp in &contours {
            if lp.len() < 2 {
                continue;
            }
            let mut pts: Vec<Pos2> = lp.iter().map(|p| self.img_to_screen(*p)).collect();
            pts.push(pts[0]);
            painter.add(egui::Shape::line(pts, Stroke::new(1.5_f32, Color32::from_rgb(90, 190, 255))));
        }

        if let (Some(a), Some(b)) = (self.drag_start, self.drag_current) {
            match self.tool {
                Tool::Rect => {
                    painter.rect_stroke(
                        Rect::from_two_pos(a, b),
                        0.0,
                        Stroke::new(1.0_f32, Color32::LIGHT_BLUE),
                        egui::StrokeKind::Inside,
                    );
                }
                Tool::Ellipse => {
                    let r = Rect::from_two_pos(a, b);
                    painter.circle_stroke(
                        r.center(),
                        r.width().min(r.height()) * 0.5,
                        Stroke::new(1.0_f32, Color32::LIGHT_BLUE),
                    );
                }
                Tool::Lasso => {
                    let pts: Vec<Pos2> = self.lasso_points.iter().map(|p| self.img_to_screen(*p)).collect();
                    if pts.len() > 1 {
                        painter.add(egui::Shape::line(pts, Stroke::new(1.0_f32, Color32::LIGHT_BLUE)));
                    }
                }
                _ => {}
            }
        }

        if matches!(self.tool, Tool::Brush | Tool::Restore) {
            if let Some(pos) = response.hover_pos() {
                painter.circle_stroke(pos, self.brush_radius * self.zoom, Stroke::new(1.0_f32, Color32::WHITE));
            }
        }

        if response.hovered() {
            let scroll = ui.input(|i| i.raw_scroll_delta.y);
            if scroll.abs() > 0.1 {
                let anchor = response.hover_pos().unwrap_or(canvas.center());
                let before = self.screen_to_img(anchor);
                self.zoom = (self.zoom * (scroll * 0.0025).exp()).clamp(0.02, 16.0);
                let after = self.img_to_screen(before);
                self.offset += anchor - after;
            }
        }

        if self.tool == Tool::Pan && response.dragged() {
            self.offset += response.drag_delta();
            return;
        }

        if response.drag_started() {
            self.drag_start = response.interact_pointer_pos();
            self.drag_current = self.drag_start;
            self.lasso_points.clear();
            self.last_stamp = None;
        }
        if response.dragged() || response.clicked() {
            if let Some(pos) = response.interact_pointer_pos() {
                let dragging = response.dragged();
                self.handle_pointer(pos, dragging);
            }
        }
        if response.drag_stopped() {
            self.finish_drag();
        }
    }

    fn handle_pointer(&mut self, pos: Pos2, dragging: bool) {
        let ip = self.screen_to_img(pos);
        let (w, h) = match self.doc() {
            Some(d) => (d.original.width, d.original.height),
            None => return,
        };
        match self.tool {
            Tool::Pan => {}
            Tool::Brush | Tool::Restore => {
                if !dragging && self.last_stamp.is_some() {
                    return;
                }
                let restore = self.tool == Tool::Restore;
                let stamp = BrushStamp {
                    x: ip[0],
                    y: ip[1],
                    radius: self.brush_radius as f64,
                    hardness: self.brush_hardness as f64,
                    opacity: self.brush_opacity as f64,
                    restore,
                };
                // Hitung stamp interpolasi SEBELUM meminjam doc mutable.
                let mut stamps = Vec::new();
                match self.last_stamp {
                    Some(prev) => {
                        let prev_img = self.screen_to_img(prev);
                        let from = BrushStamp { x: prev_img[0], y: prev_img[1], ..stamp };
                        brush::interpolate(&mut stamps, &from, &stamp, (self.brush_radius as f64 * 0.25).max(1.0));
                    }
                    None => stamps.push(stamp),
                }
                let Some(doc) = self.docs.get_mut(self.current) else { return };
                if self.last_stamp.is_none() {
                    doc.push_undo();
                }
                for s in &stamps {
                    brush::stamp(&mut doc.mask, w, h, s);
                }
                doc.dirty = true;
                self.last_stamp = if dragging { Some(pos) } else { None };
            }
            Tool::Rect | Tool::Ellipse => {
                if self.drag_start.is_none() {
                    self.drag_start = Some(pos);
                }
                self.drag_current = Some(pos);
            }
            Tool::Lasso => {
                if self.drag_start.is_none() {
                    self.drag_start = Some(pos);
                    self.lasso_points.clear();
                }
                self.drag_current = Some(pos);
                let far = match self.lasso_points.last() {
                    Some(last) => ((last[0] - ip[0]).powi(2) + (last[1] - ip[1]).powi(2)).sqrt() > 2.0,
                    None => true,
                };
                if far {
                    self.lasso_points.push(ip);
                }
            }
            Tool::Wand => {
                if ip[0] < 0.0 || ip[1] < 0.0 {
                    return;
                }
                let Some(doc) = self.docs.get_mut(self.current) else { return };
                if ip[0] as usize >= doc.original.width || ip[1] as usize >= doc.original.height {
                    return;
                }
                let cov = compose::magic_wand(&doc.original, ip[0] as usize, ip[1] as usize, self.tolerance);
                doc.selection.combine_raw(&cov, self.combine);
            }
        }
    }

    fn finish_drag(&mut self) {
        let Some(start) = self.drag_start.take() else { return };
        let end = self.drag_current.take().unwrap_or(start);
        let a = self.screen_to_img(start);
        let b = self.screen_to_img(end);
        let Some(doc) = self.docs.get_mut(self.current) else { return };
        match self.tool {
            Tool::Rect | Tool::Ellipse => {
                let (x0, x1) = (a[0].min(b[0]), a[0].max(b[0]));
                let (y0, y1) = (a[1].min(b[1]), a[1].max(b[1]));
                if (x1 - x0) < 1.0 || (y1 - y0) < 1.0 {
                    return;
                }
                let region = match self.tool {
                    Tool::Rect => Region::rect(x0, y0, x1 - x0, y1 - y0),
                    _ => Region::ellipse((x0 + x1) * 0.5, (y0 + y1) * 0.5, (x1 - x0) * 0.5, (y1 - y0) * 0.5),
                };
                doc.selection.combine_region(&region, self.combine, true, self.feather_px as f64);
            }
            Tool::Lasso => {
                if self.lasso_points.len() >= 3 {
                    let region = Region::lasso(&self.lasso_points);
                    doc.selection.combine_region(&region, self.combine, true, self.feather_px as f64);
                }
                self.lasso_points.clear();
            }
            _ => {}
        }
    }
}

/// Cari path model MODNet lokal (env PIXA_MODEL dulu, lalu folder Resources/AiModels
/// relatif ke exe). Dipakai tombol Refine Hair.
fn find_model_path() -> Option<std::path::PathBuf> {
    if let Ok(p) = std::env::var("PIXA_MODEL") {
        return Some(std::path::PathBuf::from(p));
    }
    let exe = std::env::current_exe().ok()?;
    let base = exe.parent()?.to_path_buf();
    for rel in [
        "Resources/AiModels/modnet.onnx",
        "../../../Resources/AiModels/modnet.onnx",
        "../../../../Resources/AiModels/modnet.onnx",
    ] {
        let p = base.join(rel);
        if p.exists() {
            return Some(p);
        }
    }
    None
}

/// ponytail: dipertahankan untuk API publik; tak dipakai di refine_hair.
#[allow(dead_code)]
fn ui_ctx() -> egui::Context {
    egui::Context::default()
}

impl EditorApp {
    /// Buka satu gambar langsung ke editor (dipakai dashboard).
    pub fn open_path(&mut self, path: std::path::PathBuf) {
        match Image::load(&path) {
            Ok(img) => {
                self.docs.push(Doc::new(path.clone(), img.to_opaque_rgb()));
                self.current = self.docs.len() - 1;
                self.fit_pending = true;
                self.status = format!("Dimuat: {}", path.display());
            }
            Err(e) => self.status = format!("Gagal buka {}: {e}", path.display()),
        }
    }

    /// Jalankan Refine Hair lokal: inferensi ONNX pada mask sekarang,
    /// lalu tulis alpha baru HANYA di band unknown (area solid tak bergeser).
    fn refine_hair(&mut self) {
        let doc = match self.doc() {
            Some(d) => d,
            None => return,
        };
        let model = find_model_path();
        let Some(model) = model else {
            self.status = "Refine Hair: model MODNet tidak ditemukan.".into();
            return;
        };
        let original = doc.original.clone();
        let mask = doc.mask.clone();
        self.status = "Refine Hair: memproses…".into();
        let result = (|| {
            let session = crate::matting::MattingSession::load(crate::matting::MattingSpec::modnet(model.to_string_lossy().to_string()))?;
            let mut rh = crate::refine_hair::RefineHair::new(session);
            rh.refine(&original, &mask, None, 12, 8)
        })();
        match result {
            Ok(r) => {
                if r.no_change {
                    self.status = "Refine Hair: tidak ada tepi untuk di-refine.".into();
                } else {
                    if let Some(d) = self.doc_mut() {
                        d.mask = r.mask;
                    }
                    // ensure_texture dipanggil tiap frame di `show` → texture auto-refresh.
                    self.status = "Refine Hair: band tepi diperhalus.".into();
                }
            }
            Err(e) => self.status = format!("Refine Hair gagal: {e}"),
        }
    }

    /// Jendela preview + panel editor (ala PreviewWindow app asli).
    pub fn show(&mut self, ctx: &egui::Context, open: &mut bool) {
        egui::Window::new("Preview — PixaCompact")
            .id(egui::Id::new("pxa-preview-window"))
            .open(open)
            .default_size([1150.0, 780.0])
            .min_size([860.0, 540.0])
            .resizable(true)
            .show(ctx, |ui| {
                ui.push_id("editor-root", |ui| self.ui(ui));
            });
    }

    fn ui(&mut self, ui: &mut egui::Ui) {
        self.ensure_texture(ui.ctx());

        ui.horizontal_wrapped(|ui| {
            ui.heading("PixaCompact");
            ui.separator();
            if ui.button("Buka Gambar...").clicked() {
                self.open_dialog();
            }
            let has_doc = self.doc().is_some();
            if ui.add_enabled(has_doc, egui::Button::new("Simpan PNG")).clicked() {
                self.save_dialog(false);
            }
            if ui.add_enabled(has_doc, egui::Button::new("Simpan Salinan...")).clicked() {
                self.save_dialog(true);
            }
            ui.separator();
            if ui.add_enabled(has_doc, egui::Button::new("Undo")).clicked() {
                if let Some(d) = self.doc_mut() {
                    d.undo();
                }
            }
            if ui.add_enabled(has_doc, egui::Button::new("Redo")).clicked() {
                if let Some(d) = self.doc_mut() {
                    d.redo();
                }
            }
            if ui.add_enabled(has_doc, egui::Button::new("Refine Hair")).clicked() {
                self.refine_hair();
            }
            ui.separator();
            ui.label(format!("Zoom {:.0}%", self.zoom * 100.0));
            if ui.button("Fit").clicked() {
                self.fit_pending = true;
            }
        });
        ui.separator();

        let avail = ui.available_size();
        let col_h = (avail.y - 34.0).max(200.0);
        ui.horizontal(|ui| {
            // Kolom kiri: daftar gambar.
            ui.allocate_ui_with_layout(
                egui::vec2(150.0, col_h),
                egui::Layout::top_down(egui::Align::LEFT),
                |ui| {
                    ui.heading("Gambar");
                    egui::ScrollArea::vertical().id_salt("editor-files").show(ui, |ui| {
                        for i in 0..self.docs.len() {
                            let name = self.docs[i]
                                .path
                                .file_name()
                                .map(|s| s.to_string_lossy().to_string())
                                .unwrap_or_else(|| format!("gambar {i}"));
                            if ui.selectable_label(self.current == i, name).clicked() {
                                self.current = i;
                                self.fit_pending = true;
                            }
                        }
                    });
                },
            );
            ui.separator();
            // Kolom tengah: kanvas.
            let center_w = (ui.available_width() - 272.0).max(280.0);
            ui.allocate_ui_with_layout(
                egui::vec2(center_w, col_h),
                egui::Layout::top_down(egui::Align::LEFT),
                |ui| {
                    self.paint_canvas(ui);
                },
            );
            ui.separator();
            // Kolom kanan: tool & properti.
            ui.allocate_ui_with_layout(
                egui::vec2(260.0, col_h),
                egui::Layout::top_down(egui::Align::LEFT),
                |ui| {
                    egui::ScrollArea::vertical().id_salt("editor-tools").show(ui, |ui| {
                        ui.heading("Tool");
                        for t in [Tool::Brush, Tool::Restore, Tool::Rect, Tool::Ellipse, Tool::Lasso, Tool::Wand, Tool::Pan] {
                            ui.radio_value(&mut self.tool, t, t.label());
                        }
                        ui.separator();
                        ui.label("Brush");
                        ui.add(egui::Slider::new(&mut self.brush_radius, 1.0..=300.0).text("radius"));
                        ui.add(egui::Slider::new(&mut self.brush_hardness, 0.0..=1.0).text("hardness"));
                        ui.add(egui::Slider::new(&mut self.brush_opacity, 0.05..=1.0).text("opacity"));
                        ui.separator();
                        ui.label("Seleksi");
                        let mode_label = match self.combine {
                            CombineMode::Replace => "Replace",
                            CombineMode::Add => "Add",
                            CombineMode::Subtract => "Subtract",
                            CombineMode::Intersect => "Intersect",
                        };
                        egui::ComboBox::from_label("mode gabung").selected_text(mode_label).show_ui(ui, |ui| {
                            ui.selectable_value(&mut self.combine, CombineMode::Replace, "Replace");
                            ui.selectable_value(&mut self.combine, CombineMode::Add, "Add");
                            ui.selectable_value(&mut self.combine, CombineMode::Subtract, "Subtract");
                            ui.selectable_value(&mut self.combine, CombineMode::Intersect, "Intersect");
                        });
                        ui.add(egui::Slider::new(&mut self.feather_px, 0..=40).text("feather px"));
                        ui.add(egui::Slider::new(&mut self.tolerance, 0..=96).text("wand tolerance"));
                        let has_doc = self.doc().is_some();
                        ui.horizontal(|ui| {
                            if ui.add_enabled(has_doc, egui::Button::new("Hapus")).clicked() {
                                if let Some(d) = self.doc_mut() { d.apply_selection(0); }
                            }
                            if ui.add_enabled(has_doc, egui::Button::new("Pulihkan")).clicked() {
                                if let Some(d) = self.doc_mut() { d.apply_selection(255); }
                            }
                            if ui.add_enabled(has_doc, egui::Button::new("Isi")).clicked() {
                                if let Some(d) = self.doc_mut() { d.apply_selection(255); }
                            }
                        });
                        ui.horizontal(|ui| {
                            if ui.button("Semua").clicked() {
                                if let Some(d) = self.doc_mut() { d.selection.select_all(); }
                            }
                            if ui.button("Balik").clicked() {
                                if let Some(d) = self.doc_mut() { d.selection.invert(); }
                            }
                            if ui.button("Kosongkan").clicked() {
                                if let Some(d) = self.doc_mut() { d.selection.clear(); }
                            }
                        });
                        ui.horizontal(|ui| {
                            if ui.button("Tumbuh 2").clicked() {
                                if let Some(d) = self.doc_mut() { d.selection.grow(2); }
                            }
                            if ui.button("Kerut 2").clicked() {
                                if let Some(d) = self.doc_mut() { d.selection.shrink(2); }
                            }
                            if ui.button("Feather 4").clicked() {
                                if let Some(d) = self.doc_mut() { d.selection.feather(4); }
                            }
                        });
                        ui.separator();
                        ui.label("Mask");
                        ui.horizontal(|ui| {
                            if ui.button("Balik mask").clicked() {
                                if let Some(d) = self.doc_mut() {
                                    d.push_undo(); mask_ops::invert(&mut d.mask); d.dirty = true;
                                }
                            }
                            if ui.button("Isi penuh").clicked() {
                                if let Some(d) = self.doc_mut() {
                                    d.push_undo(); mask_ops::fill(&mut d.mask, 255); d.dirty = true;
                                }
                            }
                            if ui.button("Kosong").clicked() {
                                if let Some(d) = self.doc_mut() {
                                    d.push_undo(); mask_ops::fill(&mut d.mask, 0); d.dirty = true;
                                }
                            }
                        });
                        ui.horizontal(|ui| {
                            if ui.button("Grow 3").clicked() {
                                if let Some(d) = self.doc_mut() {
                                    d.push_undo();
                                    mask_ops::shift_edge(&mut d.mask, d.original.width, d.original.height, 3, true);
                                    d.dirty = true;
                                }
                            }
                            if ui.button("Shrink 3").clicked() {
                                if let Some(d) = self.doc_mut() {
                                    d.push_undo();
                                    mask_ops::shift_edge(&mut d.mask, d.original.width, d.original.height, 3, false);
                                    d.dirty = true;
                                }
                            }
                            if ui.button("Feather 4").clicked() {
                                if let Some(d) = self.doc_mut() {
                                    d.push_undo();
                                    mask_ops::feather(&mut d.mask, d.original.width, d.original.height, 4);
                                    d.dirty = true;
                                }
                            }
                        });
                        if ui.button("Defringe 2").clicked() {
                            if let Some(d) = self.doc_mut() {
                                d.push_undo();
                                let mask = d.mask.clone();
                                mask_ops::defringe(&mut d.original, &mask, 2);
                                d.dirty = true;
                            }
                        }
                        ui.separator();
                        ui.label("Ekspor");
                        ui.checkbox(&mut self.flatten_bg, "Ratakan ke warna background");
                        ui.horizontal(|ui| {
                            let mut rgb = self.bg_color;
                            if ui.color_edit_button_srgb(&mut rgb).changed() {
                                self.bg_color = rgb;
                            }
                            ui.label("warna");
                        });
                    });
                },
            );
        });

        ui.separator();
        ui.horizontal(|ui| {
            ui.label(&self.status);
            if let Some(d) = self.doc() {
                ui.separator();
                ui.label(format!("{}x{}", d.original.width, d.original.height));
                ui.separator();
                ui.label(format!("Terpilih: {} px", d.selection.count_selected()));
            }
        });
    }
}

fn checkerboard(painter: &egui::Painter, rect: Rect) {
    const CELL: f32 = 12.0;
    painter.rect_filled(rect, 0.0, Color32::from_gray(58));
    let cols = (rect.width() / CELL).ceil() as i32;
    let rows = (rect.height() / CELL).ceil() as i32;
    for y in 0..rows {
        for x in 0..cols {
            if (x + y) % 2 == 0 {
                continue;
            }
            let r = Rect::from_min_size(
                rect.min + Vec2::new(x as f32 * CELL, y as f32 * CELL),
                Vec2::splat(CELL),
            )
            .intersect(rect);
            painter.rect_filled(r, 0.0, Color32::from_gray(78));
        }
    }
}

