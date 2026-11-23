mod i18n;
mod pixels;

use std::path::{Path, PathBuf};

use eframe::egui;
use i18n::{Key, Locale};
use pixels::PixelBuffer;

const IMAGE_EXTENSIONS: &[&str] = &["png", "jpg", "jpeg", "webp", "bmp", "tif", "tiff"];
const PREVIEW_MAX_WIDTH: usize = 1800;
const PREVIEW_MAX_HEIGHT: usize = 1200;
const ACCENT: egui::Color32 = egui::Color32::from_rgb(224, 96, 75);

fn main() -> eframe::Result<()> {
    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size([1360.0, 860.0])
            .with_min_inner_size([920.0, 640.0])
            .with_title("PixaCompact"),
        ..Default::default()
    };

    eframe::run_native(
        "PixaCompact",
        options,
        Box::new(|cc| {
            set_theme(&cc.egui_ctx, false);
            Ok(Box::new(PixaApp::default()))
        }),
    )
}

#[derive(Clone, Copy, PartialEq, Eq)]
enum Page {
    Queue,
    Editor,
    Settings,
}

#[derive(Clone, Copy, PartialEq, Eq)]
enum Engine {
    Local,
    Web,
}

struct PixaApp {
    page: Page,
    engine: Engine,
    locale: Locale,
    dark: bool,
    items: Vec<PathBuf>,
    selected: Option<PathBuf>,
    output_folder: Option<PathBuf>,
    notice: Option<String>,
    preview_source: Option<PathBuf>,
    preview_texture: Option<egui::TextureHandle>,
    preview_error: bool,
}

impl Default for PixaApp {
    fn default() -> Self {
        Self {
            page: Page::Queue,
            engine: Engine::Local,
            locale: Locale::Id,
            dark: false,
            items: Vec::new(),
            selected: None,
            output_folder: None,
            notice: None,
            preview_source: None,
            preview_texture: None,
            preview_error: false,
        }
    }
}

impl PixaApp {
    fn t(&self, key: Key) -> &'static str {
        i18n::text(self.locale, key)
    }

    fn import_files(&mut self) {
        if let Some(paths) = rfd::FileDialog::new()
            .add_filter("Images", IMAGE_EXTENSIONS)
            .pick_files()
        {
            self.add_paths(paths);
        }
    }

    fn import_folder(&mut self) {
        if let Some(folder) = rfd::FileDialog::new().pick_folder() {
            self.add_paths(vec![folder]);
        }
    }

    fn add_paths(&mut self, paths: Vec<PathBuf>) {
        let mut found = Vec::new();
        for path in paths {
            collect_images(&path, &mut found);
        }
        found.sort();
        found.dedup();

        let before = self.items.len();
        for path in found {
            if !self.items.contains(&path) {
                self.items.push(path);
            }
        }
        self.items.sort();
        if self.selected.is_none() {
            self.selected = self.items.first().cloned();
        }
        if self.items.len() > before {
            self.notice = Some(self.t(Key::ImportNotice).to_owned());
        }
    }

    fn remove_item(&mut self, path: &Path) {
        self.items.retain(|item| item != path);
        if self.selected.as_deref() == Some(path) {
            self.selected = self.items.first().cloned();
        }
    }

    fn choose_output_folder(&mut self) {
        if let Some(folder) = rfd::FileDialog::new().pick_folder() {
            self.output_folder = Some(folder);
        }
    }

    fn refresh_preview(&mut self, ctx: &egui::Context) {
        let Some(path) = self.selected.clone() else {
            self.preview_source = None;
            self.preview_texture = None;
            self.preview_error = false;
            return;
        };

        if self.preview_source.as_ref() == Some(&path) {
            return;
        }

        self.preview_source = Some(path.clone());
        self.preview_texture = None;
        self.preview_error = false;

        match PixelBuffer::open(&path) {
            Ok(image) => {
                let scale = (PREVIEW_MAX_WIDTH as f32 / image.width() as f32)
                    .min(PREVIEW_MAX_HEIGHT as f32 / image.height() as f32)
                    .min(1.0);
                let preview = if scale < 1.0 {
                    let width = ((image.width() as f32 * scale).round() as usize).max(1);
                    let height = ((image.height() as f32 * scale).round() as usize).max(1);
                    image.resize_bilinear(width, height).unwrap_or(image)
                } else {
                    image
                };
                self.preview_texture = Some(ctx.load_texture(
                    "pixacompact-selected-photo",
                    preview.to_color_image(),
                    egui::TextureOptions::LINEAR,
                ));
            }
            Err(_) => self.preview_error = true,
        }
    }

    fn render_topbar(&mut self, ctx: &egui::Context) {
        egui::TopBottomPanel::top("topbar")
            .frame(
                egui::Frame::default()
                    .fill(if self.dark {
                        egui::Color32::from_rgb(34, 36, 39)
                    } else {
                        egui::Color32::from_rgb(255, 252, 247)
                    })
                    .inner_margin(egui::Margin::symmetric(20, 12)),
            )
            .show(ctx, |ui| {
                ui.horizontal(|ui| {
                    ui.colored_label(ACCENT, egui::RichText::new("●").size(17.0));
                    ui.heading(egui::RichText::new(self.t(Key::AppTitle)).strong());
                    ui.separator();
                    ui.label(egui::RichText::new(self.t(Key::Studio)).weak());
                    ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                        if ui
                            .add(
                                egui::Button::new(self.t(Key::AddImages))
                                    .fill(ACCENT)
                                    .stroke(egui::Stroke::NONE),
                            )
                            .clicked()
                        {
                            self.import_files();
                        }
                        if ui.button("EN / ID").clicked() {
                            self.locale = match self.locale {
                                Locale::Id => Locale::En,
                                Locale::En => Locale::Id,
                            };
                        }
                        let theme_label = if self.dark {
                            self.t(Key::Light)
                        } else {
                            self.t(Key::Dark)
                        };
                        if ui.button(theme_label).clicked() {
                            self.dark = !self.dark;
                            set_theme(ctx, self.dark);
                        }
                    });
                });
            });
    }

    fn render_navigation(&mut self, ctx: &egui::Context) {
        let workspace_label = self.t(Key::Work);
        let editor_label = self.t(Key::Editor);
        let settings_label = self.t(Key::Settings);
        let studio_label = self.t(Key::Studio);

        egui::SidePanel::left("navigation")
            .resizable(false)
            .exact_width(166.0)
            .frame(egui::Frame::default().inner_margin(egui::Margin::same(14)))
            .show(ctx, |ui| {
                ui.add_space(5.0);
                ui.label(egui::RichText::new(studio_label).small().weak());
                ui.add_space(10.0);
                nav_button(ui, &mut self.page, Page::Queue, workspace_label);
                nav_button(ui, &mut self.page, Page::Editor, editor_label);
                nav_button(ui, &mut self.page, Page::Settings, settings_label);
                ui.with_layout(egui::Layout::bottom_up(egui::Align::Min), |ui| {
                    ui.separator();
                    ui.label(egui::RichText::new(self.t(Key::AppTitle)).small().weak());
                });
            });
    }

    fn render_details(&mut self, ctx: &egui::Context) {
        egui::SidePanel::right("job_details")
            .default_width(272.0)
            .min_width(232.0)
            .show(ctx, |ui| {
                ui.add_space(8.0);
                ui.heading(self.t(Key::Engine));
                ui.add_space(8.0);
                let local_label = self.t(Key::LocalEngine);
                ui.selectable_value(&mut self.engine, Engine::Local, local_label);
                let web_label = self.t(Key::WebEngine);
                ui.selectable_value(&mut self.engine, Engine::Web, web_label);
                ui.add_space(8.0);
                let privacy = match self.engine {
                    Engine::Local => self.t(Key::LocalPrivacy),
                    Engine::Web => self.t(Key::WebPrivacy),
                };
                egui::Frame::group(ui.style())
                    .inner_margin(egui::Margin::same(10))
                    .show(ui, |ui| {
                        ui.label(egui::RichText::new(privacy).small());
                    });

                ui.add_space(22.0);
                ui.heading(self.t(Key::Destination));
                ui.add_space(8.0);
                let output_label = self
                    .output_folder
                    .as_ref()
                    .map(|path| path.display().to_string())
                    .unwrap_or_else(|| self.t(Key::OutputNotSelected).to_owned());
                ui.label(egui::RichText::new(output_label).small());
                if ui.button(self.t(Key::OutputFolder)).clicked() {
                    self.choose_output_folder();
                }

                ui.add_space(22.0);
                ui.separator();
                ui.add_space(10.0);
                ui.label(egui::RichText::new(self.t(Key::ReadyCount)).weak());
                ui.label(
                    egui::RichText::new(self.items.len().to_string())
                        .size(28.0)
                        .strong(),
                );
                ui.add_space(10.0);
                ui.add_enabled(false, egui::Button::new(self.t(Key::StartProcessing)).fill(ACCENT))
                    .on_hover_text(self.t(Key::ProcessingUnavailableBody));
                ui.add_space(8.0);
                ui.label(
                    egui::RichText::new(self.t(Key::ProcessingUnavailableTitle))
                        .small()
                        .weak(),
                );
            });
    }

    fn render_preview(&mut self, ui: &mut egui::Ui) {
        egui::Frame::group(ui.style())
            .inner_margin(egui::Margin::same(16))
            .show(ui, |ui| {
                ui.horizontal(|ui| {
                    ui.heading(self.t(Key::Preview));
                    if let Some(path) = self.selected.as_ref() {
                        let name = path
                            .file_name()
                            .unwrap_or(path.as_os_str())
                            .to_string_lossy();
                        ui.label(egui::RichText::new(name).small().weak());
                    }
                });
                ui.add_space(12.0);

                let canvas_height = ui.available_height().clamp(250.0, 480.0);
                let canvas_size = egui::vec2(ui.available_width(), canvas_height);

                if let Some(texture) = self.preview_texture.as_ref() {
                    let (rect, _) = ui.allocate_exact_size(canvas_size, egui::Sense::hover());
                    paint_checkerboard(ui.painter(), rect, self.dark);
                    let source_size = texture.size_vec2();
                    let bounds = rect.shrink(28.0).size();
                    let scale = (bounds.x / source_size.x)
                        .min(bounds.y / source_size.y)
                        .min(1.0);
                    let image_size = source_size * scale;
                    let image_rect = egui::Rect::from_center_size(rect.center(), image_size);
                    ui.painter().image(
                        texture.id(),
                        image_rect,
                        egui::Rect::from_min_max(egui::Pos2::ZERO, egui::pos2(1.0, 1.0)),
                        egui::Color32::WHITE,
                    );
                } else {
                    ui.allocate_ui_with_layout(
                        canvas_size,
                        egui::Layout::centered_and_justified(egui::Direction::TopDown),
                        |ui| {
                            ui.vertical_centered(|ui| {
                                if self.preview_error {
                                    ui.heading(self.t(Key::PreviewLoadError));
                                    ui.add_space(6.0);
                                    ui.label(self.selected.as_ref().map(|path| path.display().to_string()).unwrap_or_default());
                                } else if self.selected.is_some() {
                                    ui.label(self.t(Key::LoadingPreview));
                                } else {
                                    ui.heading(self.t(Key::QueueEmptyTitle));
                                    ui.add_space(8.0);
                                    ui.label(self.t(Key::QueueEmptyBody));
                                    ui.add_space(10.0);
                                    ui.label(egui::RichText::new(self.t(Key::DropHint)).small().weak());
                                    ui.add_space(14.0);
                                    ui.horizontal(|ui| {
                                        if ui.button(self.t(Key::AddImages)).clicked() {
                                            self.import_files();
                                        }
                                        if ui.button(self.t(Key::AddFolder)).clicked() {
                                            self.import_folder();
                                        }
                                    });
                                }
                            });
                        },
                    );
                }
            });
    }

    fn render_queue(&mut self, ui: &mut egui::Ui) {
        ui.horizontal(|ui| {
            ui.heading(self.t(Key::Queue));
            ui.label(egui::RichText::new(self.items.len().to_string()).weak());
            ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                if ui.button(self.t(Key::AddFolder)).clicked() {
                    self.import_folder();
                }
                if ui.button(self.t(Key::AddImages)).clicked() {
                    self.import_files();
                }
                if let Some(selected) = self.selected.clone() {
                    if ui
                        .button(self.t(Key::RemoveSelected))
                        .on_hover_text(self.t(Key::RemoveItem))
                        .clicked()
                    {
                        self.remove_item(&selected);
                    }
                }
            });
        });
        ui.add_space(8.0);

        if let Some(notice) = self.notice.take() {
            ui.label(egui::RichText::new(notice).color(egui::Color32::from_rgb(71, 142, 105)));
            ui.add_space(4.0);
        }

        if self.items.is_empty() {
            return;
        }

        let mut remove_path = None;
        let paths = self.items.clone();
        egui::ScrollArea::vertical()
            .auto_shrink([false, false])
            .show(ui, |ui| {
                for path in paths {
                    ui.horizontal(|ui| {
                        let name = path
                            .file_name()
                            .unwrap_or(path.as_os_str())
                            .to_string_lossy();
                        let selected = self.selected.as_ref() == Some(&path);
                        if ui
                            .selectable_label(selected, egui::RichText::new(name).strong())
                            .clicked()
                        {
                            self.selected = Some(path.clone());
                        }
                        ui.with_layout(
                            egui::Layout::right_to_left(egui::Align::Center),
                            |ui| {
                                if ui
                                    .small_button("×")
                                    .on_hover_text(self.t(Key::RemoveItem))
                                    .clicked()
                                {
                                    remove_path = Some(path.clone());
                                }
                                ui.label(egui::RichText::new(self.t(Key::Ready)).small().weak());
                            },
                        );
                    });
                    ui.separator();
                }
            });
        if let Some(path) = remove_path {
            self.remove_item(&path);
        }
    }

    fn render_page(&mut self, ctx: &egui::Context) {
        if self.page == Page::Queue {
            self.render_details(ctx);
        }

        egui::CentralPanel::default().show(ctx, |ui| {
            ui.add_space(8.0);
            match self.page {
                Page::Queue => {
                    self.render_preview(ui);
                    ui.add_space(14.0);
                    self.render_queue(ui);
                }
                Page::Editor => {
                    ui.horizontal(|ui| {
                        if ui.button(self.t(Key::BackToQueue)).clicked() {
                            self.page = Page::Queue;
                        }
                        ui.heading(self.t(Key::Editor));
                    });
                    ui.add_space(18.0);
                    ui.label(self.t(Key::EditorNext));
                }
                Page::Settings => {
                    ui.heading(self.t(Key::Settings));
                    ui.add_space(18.0);
                    ui.label(self.t(Key::SettingsNext));
                    ui.add_space(18.0);
                    egui::Frame::group(ui.style())
                        .inner_margin(egui::Margin::same(14))
                        .show(ui, |ui| {
                            ui.horizontal(|ui| {
                                ui.label(self.t(Key::Language));
                                if ui.selectable_label(self.locale == Locale::Id, "ID").clicked() {
                                    self.locale = Locale::Id;
                                }
                                if ui.selectable_label(self.locale == Locale::En, "EN").clicked() {
                                    self.locale = Locale::En;
                                }
                            });
                            ui.add_space(8.0);
                            ui.horizontal(|ui| {
                                ui.label(self.t(Key::Theme));
                                if ui.selectable_label(!self.dark, self.t(Key::Light)).clicked() {
                                    self.dark = false;
                                    set_theme(ctx, false);
                                }
                                if ui.selectable_label(self.dark, self.t(Key::Dark)).clicked() {
                                    self.dark = true;
                                    set_theme(ctx, true);
                                }
                            });
                        });
                }
            }
        });
    }
}

impl eframe::App for PixaApp {
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        let dropped = ctx.input(|input| {
            input
                .raw
                .dropped_files
                .iter()
                .filter_map(|file| file.path.clone())
                .collect::<Vec<_>>()
        });
        if !dropped.is_empty() {
            self.add_paths(dropped);
        }

        self.refresh_preview(ctx);
        self.render_topbar(ctx);
        self.render_navigation(ctx);
        self.render_page(ctx);
    }
}

fn nav_button(ui: &mut egui::Ui, current: &mut Page, page: Page, label: &str) {
    let response = ui.selectable_label(*current == page, label);
    if response.clicked() {
        *current = page;
    }
}

fn paint_checkerboard(painter: &egui::Painter, rect: egui::Rect, dark: bool) {
    let first = if dark {
        egui::Color32::from_rgb(46, 48, 51)
    } else {
        egui::Color32::from_rgb(248, 244, 237)
    };
    let second = if dark {
        egui::Color32::from_rgb(55, 57, 60)
    } else {
        egui::Color32::from_rgb(239, 233, 224)
    };
    let cell = 18.0;
    let mut row = 0;
    let mut y = rect.top();
    while y < rect.bottom() {
        let mut column = 0;
        let mut x = rect.left();
        while x < rect.right() {
            let cell_rect = egui::Rect::from_min_max(
                egui::pos2(x, y),
                egui::pos2((x + cell).min(rect.right()), (y + cell).min(rect.bottom())),
            );
            painter.rect_filled(
                cell_rect,
                0.0,
                if (row + column) % 2 == 0 { first } else { second },
            );
            x += cell;
            column += 1;
        }
        y += cell;
        row += 1;
    }
}

fn set_theme(ctx: &egui::Context, dark: bool) {
    let mut visuals = if dark {
        egui::Visuals::dark()
    } else {
        egui::Visuals::light()
    };
    visuals.selection.bg_fill = ACCENT;
    visuals.selection.stroke = egui::Stroke::new(1.0, egui::Color32::from_rgb(189, 71, 55));

    if dark {
        visuals.panel_fill = egui::Color32::from_rgb(28, 30, 32);
        visuals.window_fill = egui::Color32::from_rgb(37, 39, 41);
        visuals.extreme_bg_color = egui::Color32::from_rgb(21, 23, 24);
        visuals.faint_bg_color = egui::Color32::from_rgb(34, 36, 38);
    } else {
        visuals.panel_fill = egui::Color32::from_rgb(248, 244, 237);
        visuals.window_fill = egui::Color32::from_rgb(255, 252, 247);
        visuals.extreme_bg_color = egui::Color32::from_rgb(239, 233, 224);
        visuals.faint_bg_color = egui::Color32::from_rgb(250, 247, 241);
    }

    visuals.widgets.active.bg_fill = ACCENT;
    visuals.widgets.hovered.bg_fill = if dark {
        egui::Color32::from_rgb(68, 51, 47)
    } else {
        egui::Color32::from_rgb(248, 228, 219)
    };
    visuals.widgets.inactive.bg_fill = if dark {
        egui::Color32::from_rgb(42, 44, 46)
    } else {
        egui::Color32::from_rgb(255, 252, 247)
    };
    ctx.set_visuals(visuals);
}

fn collect_images(path: &Path, output: &mut Vec<PathBuf>) {
    if path.is_dir() {
        if let Ok(entries) = std::fs::read_dir(path) {
            for entry in entries.flatten() {
                collect_images(&entry.path(), output);
            }
        }
    } else if is_image(path) {
        output.push(path.to_path_buf());
    }
}

fn is_image(path: &Path) -> bool {
    path.extension()
        .and_then(|extension| extension.to_str())
        .map(|extension| IMAGE_EXTENSIONS.contains(&extension.to_ascii_lowercase().as_str()))
        .unwrap_or(false)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn accepts_common_image_extensions_case_insensitively() {
        assert!(is_image(Path::new("portrait.PNG")));
        assert!(is_image(Path::new("photo.webp")));
        assert!(!is_image(Path::new("notes.txt")));
    }
}
