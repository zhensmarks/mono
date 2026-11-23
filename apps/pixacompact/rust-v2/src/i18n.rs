#[derive(Clone, Copy, PartialEq, Eq)]
pub enum Locale {
    Id,
    En,
}

#[derive(Clone, Copy)]
pub enum Key {
    AppTitle,
    Work,
    Editor,
    Settings,
    AddImages,
    AddFolder,
    RemoveSelected,
    StartProcessing,
    Engine,
    LocalEngine,
    WebEngine,
    Queue,
    QueueEmptyTitle,
    QueueEmptyBody,
    DropHint,
    Status,
    Ready,
    ReadyCount,
    OutputFolder,
    ProcessingUnavailableTitle,
    ProcessingUnavailableBody,
    LocalPrivacy,
    WebPrivacy,
    EditorNext,
    SettingsNext,
    RemoveItem,
    Theme,
    Light,
    Dark,
    Language,
    ImportNotice,
    Input,
    Destination,
    BackToQueue,
}

pub fn text(locale: Locale, key: Key) -> &'static str {
    let (id, en) = match key {
        Key::AppTitle => ("PixaCompact", "PixaCompact"),
        Key::Work => ("Antrean", "Queue"),
        Key::Editor => ("Editor", "Editor"),
        Key::Settings => ("Pengaturan", "Settings"),
        Key::AddImages => ("Tambah gambar", "Add images"),
        Key::AddFolder => ("Tambah folder", "Add folder"),
        Key::RemoveSelected => ("Hapus pilihan", "Remove selected"),
        Key::StartProcessing => ("Mulai proses", "Start processing"),
        Key::Engine => ("Mesin", "Engine"),
        Key::LocalEngine => ("MODNet lokal", "Local MODNet"),
        Key::WebEngine => ("Engine web", "Web engine"),
        Key::Queue => ("Daftar gambar", "Image queue"),
        Key::QueueEmptyTitle => ("Belum ada gambar", "No images yet"),
        Key::QueueEmptyBody => (
            "Tambahkan gambar atau seret file ke jendela ini untuk memulai.",
            "Add images or drop files into this window to get started.",
        ),
        Key::DropHint => ("PNG, JPG, WEBP, BMP, TIFF", "PNG, JPG, WEBP, BMP, TIFF"),
        Key::Status => ("Status", "Status"),
        Key::Ready => ("Siap", "Ready"),
        Key::ReadyCount => ("Siap diproses", "Ready to process"),
        Key::OutputFolder => ("Folder hasil", "Output folder"),
        Key::ProcessingUnavailableTitle => (
            "Mesin pemrosesan belum tersedia di tahap ini",
            "Processing is not available in this milestone",
        ),
        Key::ProcessingUnavailableBody => (
            "Tombol proses baru diaktifkan setelah engine Rust dipindahkan dan diuji.",
            "Processing will be enabled after the Rust engine is ported and tested.",
        ),
        Key::LocalPrivacy => (
            "Pemrosesan lokal menjaga gambar tetap di perangkat.",
            "Local processing keeps images on this device.",
        ),
        Key::WebPrivacy => (
            "Engine web mengirim gambar ke layanan pihak ketiga saat proses dimulai.",
            "Web engines send images to a third-party service when processing starts.",
        ),
        Key::EditorNext => (
            "Editor gambar sedang disiapkan. Pilih gambar dari antrean setelah tahap ini.",
            "The image editor is being prepared. Open an image from the queue in a later milestone.",
        ),
        Key::SettingsNext => (
            "Pengaturan engine, akun, proxy, dan pembaruan akan ditambahkan berikutnya.",
            "Engine, account, proxy, and update settings are planned for a later milestone.",
        ),
        Key::RemoveItem => ("Hapus gambar", "Remove image"),
        Key::Theme => ("Tema", "Theme"),
        Key::Light => ("Terang", "Light"),
        Key::Dark => ("Gelap", "Dark"),
        Key::Language => ("Bahasa", "Language"),
        Key::ImportNotice => ("Gambar ditambahkan ke antrean.", "Images added to the queue."),
        Key::Input => ("Sumber", "Source"),
        Key::Destination => ("Tujuan", "Destination"),
        Key::BackToQueue => ("Kembali ke antrean", "Back to queue"),
    };
    match locale {
        Locale::Id => id,
        Locale::En => en,
    }
}
