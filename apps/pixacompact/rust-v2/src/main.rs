mod i18n;

use std::path::{Path, PathBuf};

use eframe::egui;
use i18n::{Key, Locale};

const IMAGE_EXTENSIONS: &[&str] = &["png", "jpg", "jpeg", "webp", "bmp", "tif", "tiff"];

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
            set_theme(&cc.egui_ctx, true);
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
}

impl Default for PixaApp {
    fn default() -> Self {
        Self {
            page: Page::Queue,
            engine: Engine::Local,
            locale: Locale::Id,
            dark: true,
            items: Vec::new(),
            selected: None,
            output_folder: None,
            notice: None,
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

    fn render_topbar(&mut self, ctx: &egui::Context) {
        egui::TopBottomPanel::top("topbar")
            .frame(egui::Frame::default().inner_margin(egui::Margin::symmetric(20, 12)))
            .show(ctx, |ui| {
                ui.horizontal(|ui| {
                    ui.heading(self.t(Key::AppTitle));
                    ui.separator();
                    ui.label(egui::RichText::new(self.t(Key::Work)).weak());
                    ui.with_layout(
                        egui::Layout::right_to_left(egui::Align::Center),
                        |ui| {
                            if ui
                                .add(egui::Button::new(self.t(Key::AddImages)).fill(
                                    egui::Color32::from_rgb(54, 117, 230),
                                ))
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
                        },
                    );
                });
            });
    }

    fn render_navigation(&mut self, ctx: &egui::Context) {
        let queue_label = self.t(Key::Work);
        let editor_label = self.t(Key::Editor);
        let settings_label = self.t(Key::Settings);
        let section_label = self.t(Key::Queue);

        egui::SidePanel::left("navigation")
            .resizable(false)
            .exact_width(190.0)
            .frame(egui::Frame::default().inner_margin(egui::Margin::same(16)))
            .show(ctx, |ui| {
                ui.add_space(8.0);
                ui.label(egui::RichText::new(section_label).strong());
                ui.add_space(8.0);
                nav_button(ui, &mut self.page, Page::Queue, queue_label);
                nav_button(ui, &mut self.page, Page::Editor, editor_label);
                nav_button(ui, &mut self.page, Page::Settings, settings_label);
                ui.with_layout(egui::Layout::bottom_up(egui::Align::Min), |ui| {
                    ui.separator();
                    ui.label(egui::RichText::new("PixaCompact · Rust").small().weak());
                });
            });
    }

    fn render_details(&mut self, ctx: &egui::Context) {
        egui::SidePanel::right("job_details")
            .default_width(286.0)
            .min_width(250.0)
            .show(ctx, |ui| {
                ui.add_space(8.0);
                ui.heading(self.t(Key::Engine));
                ui.add_space(8.0);
                let local_label = self.t(Key::LocalEngine);
                ui.selectable_value(&mut self.engine, Engine::Local, local_label);
                let web_label = self.t(Key::WebEngine);
                ui.selectable_value(&mut self.engine, Engine::Web, web_label);
                ui.add_space(6.0);
                let privacy = match self.engine {
                    Engine::Local => self.t(Key::LocalPrivacy),
                    Engine::Web => self.t(Key::WebPrivacy),
                };
                ui.label(egui::RichText::new(privacy).small().weak());

                ui.add_space(24.0);
                ui.heading(self.t(Key::Destination));
                ui.add_space(8.0);
                let output_label = self
                    .output_folder
                    .as_ref()
                    .map(|path| path.display().to_string())
                    .unwrap_or_else(|| "Output".to_owned());
                ui.label(egui::RichText::new(output_label).small());
                if ui.button(self.t(Key::OutputFolder)).clicked() {
                    self.choose_output_folder();
                }

                ui.add_space(24.0);
                ui.separator();
                ui.add_space(8.0);
                ui.label(egui::RichText::new(self.t(Key::ReadyCount)).weak());
                ui.label(
                    egui::RichText::new(self.items.len().to_string())
                        .size(24.0)
                        .strong(),
                );
                ui.add_space(10.0);
                ui.add_enabled(false, egui::Button::new(self.t(Key::StartProcessing)))
                    .on_hover_text(self.t(Key::ProcessingUnavailableBody));
                ui.add_space(8.0);
                ui.label(
                    egui::RichText::new(self.t(Key::ProcessingUnavailableTitle))
                        .small()
                        .weak(),
                );
            });
    }

    fn render_queue(&mut self, ui: &mut egui::Ui) {
        ui.horizontal(|ui| {
            ui.heading(self.t(Key::Queue));
            ui.label(egui::RichText::new(self.items.len().to_string()).weak());
            ui.with_layout(
                egui::Layout::right_to_left(egui::Align::Center),
                |ui| {
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
                },
            );
        });
        ui.add_space(12.0);

        if let Some(notice) = self.notice.take() {
            ui.label(egui::RichText::new(notice).color(egui::Color32::from_rgb(91, 190, 135)));
            ui.add_space(6.0);
        }

        if self.items.is_empty() {
            egui::Frame::group(ui.style())
                .inner_margin(egui::Margin::symmetric(24, 32))
                .show(ui, |ui| {
                    ui.vertical_centered(|ui| {
                        ui.add_space(20.0);
                        ui.heading(self.t(Key::QueueEmptyTitle));
                        ui.add_space(8.0);
                        ui.label(self.t(Key::QueueEmptyBody));
                        ui.add_space(6.0);
                        ui.label(egui::RichText::new(self.t(Key::DropHint)).small().weak());
                        ui.add_space(16.0);
                        if ui.button(self.t(Key::AddImages)).clicked() {
                            self.import_files();
                        }
                    });
                });
            return;
        }

        ui.horizontal(|ui| {
            ui.label(egui::RichText::new(self.t(Key::Input)).small().weak());
            ui.with_layout(
                egui::Layout::right_to_left(egui::Align::Center),
                |ui| {
                    ui.label(egui::RichText::new(self.t(Key::Status)).small().weak());
                },
            );
        });
        ui.separator();

        let mut remove_path = None;
        let paths = self.items.clone();
        egui::ScrollArea::vertical().show(ui, |ui| {
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
                    let parent = path.parent().unwrap_or(Path::new(""));
                    ui.label(egui::RichText::new(parent.display().to_string()).small().weak());
                    ui.with_layout(
                        egui::Layout::right_to_left(egui::Align::Center),
                        |ui| {
                            ui.label(egui::RichText::new(self.t(Key::Ready)).small().weak());
                            if ui
                                .small_button("×")
                                .on_hover_text(self.t(Key::RemoveItem))
                                .clicked()
                            {
                                remove_path = Some(path.clone());
                            }
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
                Page::Queue => self.render_queue(ui),
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
                    ui.horizontal(|ui| {
                        ui.label(self.t(Key::Language));
                        if ui.selectable_label(self.locale == Locale::Id, "ID").clicked() {
                            self.locale = Locale::Id;
                        }
                        if ui.selectable_label(self.locale == Locale::En, "EN").clicked() {
                            self.locale = Locale::En;
                        }
                    });
                    ui.horizontal(|ui| {
                        ui.label(self.t(Key::Theme));
                        if ui.selectable_label(self.dark, self.t(Key::Dark)).clicked() {
                            self.dark = true;
                            set_theme(ctx, true);
                        }
                        if ui.selectable_label(!self.dark, self.t(Key::Light)).clicked() {
                            self.dark = false;
                            set_theme(ctx, false);
                        }
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

fn set_theme(ctx: &egui::Context, dark: bool) {
    let mut visuals = if dark {
        egui::Visuals::dark()
    } else {
        egui::Visuals::light()
    };
    visuals.selection.bg_fill = egui::Color32::from_rgb(54, 117, 230);
    if dark {
        visuals.panel_fill = egui::Color32::from_rgb(20, 22, 27);
        visuals.window_fill = egui::Color32::from_rgb(27, 30, 36);
        visuals.extreme_bg_color = egui::Color32::from_rgb(15, 17, 21);
    } else {
        visuals.panel_fill = egui::Color32::from_rgb(247, 248, 250);
        visuals.window_fill = egui::Color32::from_rgb(255, 255, 255);
        visuals.extreme_bg_color = egui::Color32::from_rgb(239, 241, 245);
    }
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
