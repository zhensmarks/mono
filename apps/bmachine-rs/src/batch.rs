//! Model BATCH (Batch Master) — logika murni, tanpa Slint.
//!
//! Sesuai `BATCH_DASHBOARD.md` (abeng) + `LANJUTKAN.md` §10 (studi C#
//! 28 Nov 2026): pasangan folder SOURCE → OUTPUT, prediksi output dari
//! folder bulan, klik nama = salin path, multi-select manual (Shift
//! range + Ctrl toggle + anchor), Replace (Auto) multi-pasangan, dan
//! Folder Reguler ala master_creator.
//!
//! Tree dirender di Slint sebagai DAFTAR DATAR (flat rows): Rust yang
//! meratakan tree terlihat (hormat expand) ke `FlatRow`, Slint hanya
//! menampilkan baris + indentasi dari `depth`. Ini pola yang sama
//! dengan spreadsheet (parallel arrays), bukan nested component.

use std::cmp::Ordering;
use std::collections::HashSet;
use std::path::{Path, PathBuf};

// ---------------------------------------------------------------------------
// Natural sort ("2" sebelum "10") — seperti C# (natural sort di tree)
// ---------------------------------------------------------------------------

/// Bandingkan dua nama file secara natural: potongan angka dibandingkan
/// sebagai bilangan, bukan sebagai teks.
pub fn natural_cmp(a: &str, b: &str) -> Ordering {
    let ac: Vec<char> = a.chars().collect();
    let bc: Vec<char> = b.chars().collect();
    let mut i = 0usize;
    let mut j = 0usize;
    while i < ac.len() && j < bc.len() {
        let ca = ac[i];
        let cb = bc[j];
        if ca.is_ascii_digit() && cb.is_ascii_digit() {
            let mut x = 0usize;
            let mut y = 0usize;
            while i < ac.len() && ac[i].is_ascii_digit() {
                x = x.saturating_mul(10).saturating_add((ac[i] as u8 - b'0') as usize);
                i += 1;
            }
            while j < bc.len() && bc[j].is_ascii_digit() {
                y = y.saturating_mul(10).saturating_add((bc[j] as u8 - b'0') as usize);
                j += 1;
            }
            match x.cmp(&y) {
                Ordering::Equal => continue,
                o => return o,
            }
        }
        // Huruf dibandingkan tanpa peduli besar-kecil (seperti Explorer).
        match ca.to_ascii_lowercase().cmp(&cb.to_ascii_lowercase()) {
            Ordering::Equal => {
                i += 1;
                j += 1;
            }
            o => return o,
        }
    }
    (ac.len() - i).cmp(&(bc.len() - j))
}

/// Nama yang disembunyikan dari tree BATCH (C# BatchNodeItem):
/// shortcut `.lnk` dan `desktop.ini`.
pub fn is_hidden_name(name: &str) -> bool {
    name.eq_ignore_ascii_case("desktop.ini") || name.to_ascii_lowercase().ends_with(".lnk")
}

// ---------------------------------------------------------------------------
// Prediksi OUTPUT path ("Smart Path Prediction", BATCH_DASHBOARD §4.3)
// ---------------------------------------------------------------------------

/// Apakah nama folder cocok pola bulan `DD BULAN YYYY`,
/// mis. "02 AGUSTUS 2025" / "11 November 2026".
/// Dipakai tanpa crate regex (pola sederhana: 2 digit + kata + 4 digit).
pub fn is_month_folder(name: &str) -> bool {
    let parts: Vec<&str> = name.split_whitespace().collect();
    if parts.len() != 3 {
        return false;
    }
    let day_ok = parts[0].len() == 2 && parts[0].bytes().all(|b| b.is_ascii_digit());
    let year_ok = parts[2].len() == 4 && parts[2].bytes().all(|b| b.is_ascii_digit());
    let month_ok = !parts[1].is_empty() && parts[1].chars().all(|c| c.is_alphanumeric());
    day_ok && year_ok && month_ok
}

/// Prediksi output path untuk satu folder SOURCE.
///
/// - Cari folder bulan di atas path (maks 5 level ke atas, regex
///   `^\d{2}\s+\w+\s+\d{4}$`); `relative` = path relatif dari folder
///   bulan itu.
/// - Kasus khusus `PILIHAN`: output diarahkan ke parent folder
///   (folder proyeknya), bukan ke folder `PILIHAN` itu sendiri.
/// - Kalau tidak ketemu folder bulan: fallback = nama folder saja.
/// - `OutputPath = LocalOutput + relative`.
pub fn predict_output_path(local_output: &Path, source_path: &Path) -> PathBuf {
    // Efektif: PILIHAN → parent-nya.
    let effective = match source_path.file_name().and_then(|n| n.to_str()) {
        Some(n) if n.eq_ignore_ascii_case("pilihan") => {
            source_path.parent().map(|p| p.to_path_buf()).unwrap_or_else(|| source_path.to_path_buf())
        }
        _ => source_path.to_path_buf(),
    };
    // Cari folder bulan ke atas (maks 5 level).
    let mut cur: Option<&Path> = Some(effective.as_path());
    for _ in 0..5 {
        let Some(p) = cur else { break };
        if let Some(name) = p.file_name().and_then(|n| n.to_str()) {
            if is_month_folder(name) {
                if let Ok(rel) = effective.strip_prefix(p) {
                    if rel.as_os_str().is_empty() {
                        return local_output.to_path_buf();
                    }
                    return local_output.join(rel);
                }
            }
        }
        cur = p.parent();
    }
    // Fallback: nama folder saja.
    match effective.file_name() {
        Some(n) => local_output.join(n),
        None => local_output.to_path_buf(),
    }
}

// ---------------------------------------------------------------------------
// Tree node + root
// ---------------------------------------------------------------------------

#[derive(Debug, Clone)]
pub struct BatchNode {
    pub name: String,
    pub path: PathBuf,
    pub is_dir: bool,
    pub children: Vec<BatchNode>,
    pub children_loaded: bool,
    pub expanded: bool,
}

impl BatchNode {
    pub fn new_file(path: &Path) -> Self {
        BatchNode {
            name: path.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default(),
            path: path.to_path_buf(),
            is_dir: false,
            children: Vec::new(),
            children_loaded: true,
            expanded: false,
        }
    }

    pub fn new_dir(path: &Path) -> Self {
        BatchNode {
            name: path.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default(),
            path: path.to_path_buf(),
            is_dir: true,
            children: Vec::new(),
            children_loaded: false,
            expanded: false,
        }
    }

    /// Apakah folder ini punya anak (cek murah tanpa memuat semua).
    pub fn has_kids(&self) -> bool {
        if !self.is_dir {
            return false;
        }
        if self.children_loaded {
            return !self.children.is_empty();
        }
        match std::fs::read_dir(&self.path) {
            Ok(mut rd) => rd.any(|e| match e {
                Ok(en) => !is_hidden_name(&en.file_name().to_string_lossy()),
                Err(_) => false,
            }),
            Err(_) => false,
        }
    }

    /// Muat anak (lazy load, seperti C# BatchNodeItem.LoadChildren).
    pub fn load_children(&mut self) {
        if !self.is_dir || self.children_loaded {
            return;
        }
        self.children = scan_children(&self.path);
        self.children_loaded = true;
    }
}

/// Baca isi satu folder: natural sort, sembunyikan `.lnk`/`desktop.ini`.
/// Anak folder belum dimuat (lazy) — hanya 1 level.
pub fn scan_children(dir: &Path) -> Vec<BatchNode> {
    let mut entries: Vec<(String, PathBuf, bool)> = Vec::new();
    if let Ok(rd) = std::fs::read_dir(dir) {
        for e in rd.flatten() {
            let name = e.file_name().to_string_lossy().into_owned();
            if is_hidden_name(&name) {
                continue;
            }
            let is_dir = e.file_type().map(|t| t.is_dir()).unwrap_or(false);
            entries.push((name, e.path(), is_dir));
        }
    }
    entries.sort_by(|a, b| natural_cmp(&a.0, &b.0));
    entries
        .into_iter()
        .map(|(name, path, is_dir)| {
            let mut n = if is_dir { BatchNode::new_dir(&path) } else { BatchNode::new_file(&path) };
            n.name = name;
            n
        })
        .collect()
}

#[derive(Debug, Clone)]
pub struct BatchRoot {
    pub source_path: PathBuf,
    pub output_path: PathBuf,
    /// Tree SOURCE — node akar = folder sumber itu sendiri (tidak
    /// ditampilkan sebagai baris bila hanya ada 1 root, §4.4).
    pub source_root: BatchNode,
    /// Tree OUTPUT — `None` = folder output belum ada di disk
    /// ("(pending)", chip oranye).
    pub output_root: Option<BatchNode>,
}

impl BatchRoot {
    pub fn new(source_path: &Path, local_output: &Path) -> Self {
        let output_path = predict_output_path(local_output, source_path);
        let mut source_root = BatchNode::new_dir(source_path);
        source_root.load_children();
        let output_root = if output_path.is_dir() {
            let mut o = BatchNode::new_dir(&output_path);
            o.load_children();
            Some(o)
        } else {
            None
        };
        BatchRoot { source_path: source_path.to_path_buf(), output_path, source_root, output_root }
    }

    /// Refresh OUTPUT: cek ulang apakah folder output sudah ada di disk
    /// (dipanggil setelah script selesai — C# BatchViewModel:399).
    /// Mengembalikan `true` bila status pending berubah.
    pub fn refresh_output(&mut self) -> bool {
        let was_pending = self.output_root.is_none();
        let exists = self.output_path.is_dir();
        match (was_pending, exists) {
            (true, true) => {
                let mut o = BatchNode::new_dir(&self.output_path);
                o.load_children();
                self.output_root = Some(o);
                true
            }
            (false, false) => {
                self.output_root = None;
                true
            }
            (false, true) => {
                if let Some(o) = self.output_root.as_mut() {
                    o.load_children();
                }
                false
            }
            (true, false) => false,
        }
    }
}

// ---------------------------------------------------------------------------
// Flat rows (yang dikirim ke Slint sebagai parallel arrays)
// ---------------------------------------------------------------------------

#[derive(Debug, Clone, PartialEq)]
pub struct FlatRow {
    pub id: String,
    pub name: String,
    pub path: String,
    pub depth: i32,
    pub is_dir: bool,
    pub expanded: bool,
    pub has_kids: bool,
    pub selected: bool,
    pub is_root: bool,
    /// Jumlah file (rekursif) di folder ini untuk ekstensi yang dipilih
    /// user; -1 = bukan folder / jangan tampilkan angkanya.
    pub file_count: i32,
}

fn flatten_node(node: &BatchNode, depth: i32, selected: &HashSet<String>, out: &mut Vec<FlatRow>, is_root: bool) {
    let id = node.path.to_string_lossy().into_owned();
    out.push(FlatRow {
        id: id.clone(),
        name: node.name.clone(),
        path: id.clone(),
        depth,
        is_dir: node.is_dir,
        expanded: node.expanded,
        has_kids: node.has_kids(),
        selected: selected.contains(&id),
        is_root,
        // Diisi `fill_file_counts` oleh src_rows/out_rows (hitung
        // rekursif per folder yang terlihat); -1 = belum diisi.
        file_count: -1,
    });
    if node.is_dir && node.expanded {
        for c in &node.children {
            flatten_node(c, depth + 1, selected, out, false);
        }
    }
}

/// Isi `file_count` tiap baris folder dari hitungan rekursif di disk
/// (dipakai `src_rows`/`out_rows`): hanya folder yang terlihat yang
/// dihitung — sekali per push — supaya angka selalu segar setelah file
/// berubah (master jalan, refresh, tambah folder) tanpa cache yang
/// bisa basi. Baris file tetap -1 (tidak tampil).
pub fn fill_file_counts(rows: &mut [FlatRow], ext: &str) {
    for r in rows.iter_mut() {
        if r.is_dir {
            r.file_count = count_files_recursive(std::path::Path::new(&r.path)).get(ext) as i32;
        }
    }
}
/// Ratakan tree SOURCE yang terlihat (hormat expand).
/// - 1 root → baris root disembunyikan, isinya jadi baris teratas
///   (nama tampil di header, BATCH_DASHBOARD §4.4).
/// - >1 root → tiap folder tampil sebagai baris root.
pub fn flatten_source(roots: &[BatchRoot], selected: &HashSet<String>) -> Vec<FlatRow> {
    let mut out = Vec::new();
    if roots.len() == 1 {
        if roots[0].source_root.children.is_empty() {
            // Folder kosong: tampilkan root-nya sendiri sebagai satu
            // baris — kalau tidak, src-rows kosong dan drop zone muncul
            // lagi seolah folder tidak terpilih (temuan verifikasi Dex
            // 28 Nov 2026: hanya toast yang membuktikan pilihannya).
            flatten_node(&roots[0].source_root, 0, selected, &mut out, true);
        } else {
            for c in &roots[0].source_root.children {
                flatten_node(c, 0, selected, &mut out, false);
            }
        }
    } else {
        for r in roots {
            flatten_node(&r.source_root, 0, selected, &mut out, true);
        }
    }
    out
}

/// Ratakan tree OUTPUT (hanya root yang output-nya sudah ada).
pub fn flatten_output(roots: &[BatchRoot], selected: &HashSet<String>) -> Vec<FlatRow> {
    let mut out = Vec::new();
    for r in roots {
        if let Some(o) = &r.output_root {
            if roots.len() == 1 {
                for c in &o.children {
                    flatten_node(c, 0, selected, &mut out, false);
                }
            } else {
                flatten_node(o, 0, selected, &mut out, true);
            }
        }
    }
    out
}

// ---------------------------------------------------------------------------
// Selection model — manual, seperti C# (LANJUTKAN §10)
// ---------------------------------------------------------------------------

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum BatchPanel {
    Source,
    Output,
}

#[derive(Debug, Clone, Default)]
pub struct BatchSelection {
    pub panel: Option<BatchPanel>,
    pub ids: Vec<String>,
    pub anchor: Option<String>,
    /// Snapshot sebelum klik kanan (C#: OnBatchTreePointerPressed
    /// menyimpan seleksi; RestoreBatchTreeSelection mengembalikannya
    /// setelah TreeView native meruntuhkannya).
    saved: Option<(Option<BatchPanel>, Vec<String>, Option<String>)>,
}

/// Deteksi klik ganda untuk tree BATCH: klik kedua pada (panel, id)
/// yang sama dalam 450ms = double-click. Dipakai di Rust (main.rs)
/// karena Slint `TouchArea.double-clicked` tidak bisa diandalkan di
/// sini: tiap klik tunggal mengganti model `src-rows`/`out-rows`
/// wholesale, sehingga elemen baris dibuat ulang dan deteksi
/// double-click winit kehilangan jejak klik pertama.
pub fn is_double_click(
    last: &mut Option<(String, String, std::time::Instant)>,
    panel: &str,
    id: &str,
    now: std::time::Instant,
) -> bool {
    let dbl = matches!(&*last, Some((p, i, t))
        if p == panel && i == id
            && now.duration_since(*t) < std::time::Duration::from_millis(450));
    *last = if dbl {
        None
    } else {
        Some((panel.to_string(), id.to_string(), now))
    };
    dbl
}

impl BatchSelection {
    /// Klik kiri pada satu baris (visible = baris yang sedang tampil
    /// di panel itu, dalam urutan tampil).
    /// - Tanpa modifier: seleksi = [id] + anchor = id. Pemanggil
    ///   (Rust, bukan Slint) juga set MASTER target + salin path.
    /// - Ctrl: toggle id; anchor = id.
    /// - Shift: range anchor..id (anchor = item terakhir diklik).
    pub fn click(&mut self, panel: BatchPanel, visible: &[FlatRow], id: &str, shift: bool, ctrl: bool) {        if self.panel != Some(panel) {
            self.panel = Some(panel);
            self.ids.clear();
            self.anchor = None;
        }
        if ctrl {
            if let Some(pos) = self.ids.iter().position(|x| x == id) {
                self.ids.remove(pos);
            } else {
                self.ids.push(id.to_string());
            }
            self.anchor = Some(id.to_string());
            return;
        }
        if shift {
            if let Some(anchor) = self.anchor.clone() {
                let ai = visible.iter().position(|r| r.id == anchor);
                let bi = visible.iter().position(|r| r.id == id);
                if let (Some(a), Some(b)) = (ai, bi) {
                    let (lo, hi) = if a <= b { (a, b) } else { (b, a) };
                    self.ids = visible[lo..=hi].iter().map(|r| r.id.clone()).collect();
                    return;
                }
            }
            self.ids = vec![id.to_string()];
            self.anchor = Some(id.to_string());
            return;
        }
        self.ids = vec![id.to_string()];
        self.anchor = Some(id.to_string());
    }

    /// Simpan seleksi sebelum klik kanan (tunnel).
    pub fn save(&mut self) {
        self.saved = Some((self.panel, self.ids.clone(), self.anchor.clone()));
    }

    /// Pulihkan seleksi setelah klik kanan (dispatcher Input).
    pub fn restore(&mut self) {
        if let Some((p, ids, a)) = self.saved.take() {
            self.panel = p;
            self.ids = ids;
            self.anchor = a;
        }
    }

    /// Snapshot ulang saat pointer dilepas — seleksi final yang dipakai
    /// command context menu (C#: OnBatchTreePointerReleased).
    pub fn snapshot(&mut self) {
        self.saved = Some((self.panel, self.ids.clone(), self.anchor.clone()));
    }

    pub fn clear(&mut self) {
        self.panel = None;
        self.ids.clear();
        self.anchor = None;
        self.saved = None;
    }

    pub fn contains(&self, id: &str) -> bool {
        self.ids.iter().any(|x| x == id)
    }
}

// ---------------------------------------------------------------------------
// Replace (Auto) — resolve pasangan (output, source)
// ---------------------------------------------------------------------------

/// Cari node berdasarkan id (full path) di semua root SOURCE/OUTPUT.
pub fn find_node<'a>(roots: &'a [BatchRoot], panel: BatchPanel, id: &str) -> Option<&'a BatchNode> {
    fn walk<'b>(n: &'b BatchNode, id: &str) -> Option<&'b BatchNode> {
        if n.path.to_string_lossy() == id {
            return Some(n);
        }
        for c in &n.children {
            if let Some(f) = walk(c, id) {
                return Some(f);
            }
        }
        None
    }
    for r in roots {
        let tree = match panel {
            BatchPanel::Source => &r.source_root,
            BatchPanel::Output => match &r.output_root {
                Some(o) => o,
                None => continue,
            },
        };
        if let Some(f) = walk(tree, id) {
            return Some(f);
        }
    }
    None
}

/// Versi mutable dari `find_node`.
pub fn find_node_mut<'a>(roots: &'a mut [BatchRoot], panel: BatchPanel, id: &str) -> Option<&'a mut BatchNode> {
    fn walk<'b>(n: &'b mut BatchNode, id: &str) -> Option<&'b mut BatchNode> {
        if n.path.to_string_lossy() == id {
            return Some(n);
        }
        for c in &mut n.children {
            if let Some(f) = walk(c, id) {
                return Some(f);
            }
        }
        None
    }
    for r in roots {
        let tree = match panel {
            BatchPanel::Source => &mut r.source_root,
            BatchPanel::Output => match &mut r.output_root {
                Some(o) => o,
                None => continue,
            },
        };
        if let Some(f) = walk(tree, id) {
            return Some(f);
        }
    }
    None
}

/// Toggle expand satu node direktori (lazy load anak saat pertama
/// dibuka — C# BatchNodeItem.LoadChildren). Mengembalikan `true`
/// bila node ditemukan.
pub fn toggle_expand(roots: &mut [BatchRoot], panel: BatchPanel, id: &str) -> bool {
    match find_node_mut(roots, panel, id) {
        Some(n) if n.is_dir => {
            n.load_children();
            n.expanded = !n.expanded;
            true
        }
        _ => false,
    }
}

/// Muat ulang anak-anak satu node direktori dari disk (setelah
/// buat/hapus subfolder di dalamnya).
/// Gabung hasil scan segar dengan subtree lama: node yang path-nya
/// masih ada memakai kembali anak + status expand yang sudah dimuat,
/// dan level yang sudah dimuat di-scan ulang secara rekursif (jadi
/// Refresh tidak menutup folder yang sedang dibuka, tidak membuang
/// seleksi pada cucu, dan tetap menampilkan perubahan di disk).
/// Tanpa ini, `rescan_all` membuang cucu yang sudah dimuat → seleksi
/// pada cucu tidak bisa di-resolve (Replace diam-diam kehilangan
/// pasangan; ketemu saat verifikasi GUI 28 Nov 2026).
fn merge_children(old: Vec<BatchNode>, dir: &Path) -> Vec<BatchNode> {
    let fresh = scan_children(dir);
    let mut old_map: std::collections::HashMap<PathBuf, BatchNode> =
        old.into_iter().map(|n| (n.path.clone(), n)).collect();
    fresh
        .into_iter()
        .map(|mut f| {
            if let Some(o) = old_map.remove(&f.path) {
                if o.children_loaded {
                    f.children = merge_children(o.children, &f.path);
                    f.children_loaded = true;
                }
                f.expanded = o.expanded;
                f
            } else {
                f
            }
        })
        .collect()
}

pub fn rescan_node(roots: &mut [BatchRoot], panel: BatchPanel, id: &str) {
    if let Some(n) = find_node_mut(roots, panel, id) {
        if n.is_dir {
            let old = std::mem::take(&mut n.children);
            n.children = merge_children(old, &n.path);
            n.children_loaded = true;
        }
    }
}

/// Hapus satu path dari tree (setelah dihapus dari disk): bila path
/// adalah root → buang root-nya; bila anak → muat ulang induknya.
pub fn remove_from_tree(roots: &mut Vec<BatchRoot>, panel: BatchPanel, path: &Path) {
    let key = path.to_string_lossy().into_owned();
    match panel {
        BatchPanel::Source => {
            if let Some(i) = roots.iter().position(|r| r.source_path.to_string_lossy() == key) {
                roots.remove(i);
                return;
            }
        }
        BatchPanel::Output => {
            if let Some(i) = roots.iter().position(|r| r.output_path.to_string_lossy() == key) {
                roots[i].output_root = None;
                return;
            }
        }
    }
    if let Some(parent) = path.parent() {
        let pid = parent.to_string_lossy().into_owned();
        rescan_node(roots, panel, &pid);
    }
}

/// Muat ulang SEMUA root dari disk (Refresh): baca ulang anak + cek
/// output pending.
pub fn rescan_all(roots: &mut [BatchRoot]) {
    for r in roots {
        let old = std::mem::take(&mut r.source_root.children);
        r.source_root.children = merge_children(old, &r.source_path);
        r.source_root.children_loaded = true;
        r.refresh_output();
        if let Some(o) = r.output_root.as_mut() {
            let old_o = std::mem::take(&mut o.children);
            o.children = merge_children(old_o, &o.path);
            o.children_loaded = true;
        }
    }
}

/// Resolve SATU item ke pasangan (output, source) — C#
/// `TryResolveReplaceFolders`:
/// - Root → path yang diklik, relatif "" → (OutputPath, SourcePath).
/// - Node direktori → root yang cocok dengan PREFIX TERPANJANG di
///   antara SourceFolders (menang bila folder bersarang); sisi yang
///   diklik dipakai apa adanya, sisi lain = root + relatif.
pub fn resolve_replace_pair(roots: &[BatchRoot], panel: BatchPanel, id: &str) -> Option<(PathBuf, PathBuf)> {
    let node = find_node(roots, panel, id)?;
    if !node.is_dir {
        return None;
    }
    let clicked = node.path.clone();
    // Cari root dengan prefix terpanjang yang mengandung node ini.
    let mut best: Option<(&BatchRoot, PathBuf)> = None;
    for r in roots {
        let base = match panel {
            BatchPanel::Source => &r.source_path,
            BatchPanel::Output => &r.output_path,
        };
        if let Ok(rel) = clicked.strip_prefix(base) {
            let len = base.as_os_str().len();
            if best.as_ref().map(|(_, b)| b.as_os_str().len()).unwrap_or(0) < len {
                best = Some((r, rel.to_path_buf()));
            }
        }
    }
    let (root, rel) = best?;
    match panel {
        BatchPanel::Source => Some((root.output_path.join(&rel), clicked)),
        BatchPanel::Output => Some((clicked, root.source_path.join(&rel))),
    }
}

/// Resolve SEMUA item terpilih ke pasangan (output, source).
/// Target klik kanan hanya fallback bila 1 item terpilih (pemanggil
/// yang menentukan: bila seleksi > 1, pakai seleksi; bila 1, pakai
/// target klik kanan bila ada, kalau tidak pakai seleksi itu).
pub fn resolve_replace_pairs(
    roots: &[BatchRoot],
    panel: BatchPanel,
    selected_ids: &[String],
) -> Vec<(PathBuf, PathBuf)> {
    let mut out = Vec::new();
    let mut seen = HashSet::new();
    for id in selected_ids {
        if let Some(p) = resolve_replace_pair(roots, panel, id) {
            let key = format!("{}|{}", p.0.display(), p.1.display());
            if seen.insert(key) {
                out.push(p);
            }
        }
    }
    out
}

// ---------------------------------------------------------------------------
// Folder Reguler (ala master_creator) — daftar direktori kerja tersimpan
// ---------------------------------------------------------------------------

#[derive(Debug, Clone, PartialEq, serde::Serialize, serde::Deserialize)]
pub struct RegulerRow {
    pub bulan: String,
    pub marketing: String,
    pub sekolah: String,
    pub path: String,
    /// Jumlah file di dalam folder sekolah (rekursif), per keranjang
    /// ekstensi — dihitung saat scan (penuh / inkremental per bulan)
    /// dan disimpan di cache, supaya daftar tampil instan tanpa
    /// menghitung ulang tiap baris (28 Nov 2026).
    /// `#[serde(default)]` = cache lama (tanpa field ini) tetap bisa
    /// dibaca; hitungannya 0 sampai scan ulang berikutnya.
    #[serde(default)]
    pub counts: FileCounts,
}

// ---------------------------------------------------------------------------
// Hitungan file per folder (28 Nov 2026 — angka di kanan nama folder)
// ---------------------------------------------------------------------------

/// Jumlah file rekursif di dalam satu folder, dikelompokkan per
/// keranjang ekstensi. `jpg` mencakup `.jpg` + `.jpeg` (case-insensitive);
/// `all` = semua file. Dipakai daftar Folder Reguler (per sekolah) dan
/// treeview (per folder) — ekstensi yang ditampilkan dipilih user
/// (`Batch.FileCountExtension`), keranjang lain tetap tersimpan supaya
/// ganti ekstensi tidak butuh scan ulang.
#[derive(Debug, Clone, Copy, Default, PartialEq, serde::Serialize, serde::Deserialize)]
pub struct FileCounts {
    pub all: u32,
    pub jpg: u32,
    pub png: u32,
    pub psd: u32,
}

impl FileCounts {
    /// Ambil keranjang sesuai pilihan ekstensi (`jpg` | `png` | `psd` |
    /// `all`; nilai lain = `jpg`).
    pub fn get(&self, ext: &str) -> u32 {
        match ext {
            "png" => self.png,
            "psd" => self.psd,
            "all" => self.all,
            _ => self.jpg,
        }
    }
}

/// Pilihan ekstensi yang sah untuk hitungan file. Nilai di luar daftar
/// (mis. dari DB lama/rusak) → `"jpg"` (default).
pub fn valid_file_count_ext(ext: &str) -> &'static str {
    match ext {
        "png" => "png",
        "psd" => "psd",
        "all" => "all",
        _ => "jpg",
    }
}

/// Label yang ditampilkan di samping angka (daftar Folder Reguler):
/// ekstensi itu sendiri, kecuali `all` → `"file"`.
pub fn file_count_ext_label(ext: &str) -> &'static str {
    match valid_file_count_ext(ext) {
        "all" => "file",
        other => other,
    }
}

/// Hitung file rekursif di bawah `dir`, dikelompokkan per keranjang.
/// - Hanya file (bukan direktori); nama tersembunyi (`.lnk`,
///   `desktop.ini`, dotfile) dilewati — sama seperti yang ditampilkan
///   tree (is_hidden_name).
/// - Symlink TIDAK diikuti (file_type entri, bukan target) — tidak
///   ada risiko putaran.
/// - Folder yang tidak bisa dibaca → 0 (bukan error): daftar tetap
///   tampil, angkanya saja 0.
pub fn count_files_recursive(dir: &Path) -> FileCounts {
    let mut counts = FileCounts::default();
    let mut stack: Vec<PathBuf> = vec![dir.to_path_buf()];
    while let Some(d) = stack.pop() {
        let Ok(rd) = std::fs::read_dir(&d) else {
            continue;
        };
        for e in rd.flatten() {
            let name = e.file_name().to_string_lossy().into_owned();
            if is_hidden_name(&name) {
                continue;
            }
            let ft = match e.file_type() {
                Ok(t) => t,
                Err(_) => continue,
            };
            if ft.is_dir() {
                stack.push(e.path());
            } else if ft.is_file() {
                counts.all += 1;
                match e.path().extension().and_then(|x| x.to_str()).unwrap_or("").to_ascii_lowercase().as_str() {
                    "jpg" | "jpeg" => counts.jpg += 1,
                    "png" => counts.png += 1,
                    "psd" => counts.psd += 1,
                    _ => {}
                }
            }
        }
    }
    counts
}

/// Baca MARKETING dari NOTE.txt di folder sekolah, bila ada baris
/// `MARKETING: <nama>` (master_creator memakai NOTE.txt untuk
/// automation). Kalau tidak ada → "".
pub fn marketing_from_note(folder: &Path) -> String {
    let note = folder.join("NOTE.txt");
    let Ok(text) = std::fs::read_to_string(&note) else {
        return String::new();
    };
    for line in text.lines() {
        let t = line.trim();
        if let Some(rest) = t.strip_prefix("MARKETING:").or_else(|| t.strip_prefix("Marketing:")) {
            return rest.trim().to_string();
        }
    }
    String::new()
}

/// Scan folder root untuk tabel Folder Reguler: tiap folder bulan
/// (pola `DD BULAN YYYY`) → tiap subfolder = satu baris sekolah.
/// Jalan di thread latar (pemanggil) supaya UI tidak lag.
pub fn scan_reguler(root: &Path) -> Vec<RegulerRow> {
    let mut rows = Vec::new();
    for (bulan, mpath) in month_folders_sorted(root) {
        rows.extend(scan_reguler_month(root, &bulan, &mpath));
    }
    rows
}

/// Folder bulan di bawah root, urut natural (nama + path).
fn month_folders_sorted(root: &Path) -> Vec<(String, PathBuf)> {
    let Ok(rd) = std::fs::read_dir(root) else {
        return Vec::new();
    };
    let mut months: Vec<(String, PathBuf)> = rd
        .flatten()
        .filter_map(|e| {
            let name = e.file_name().to_string_lossy().into_owned();
            let is_dir = e.file_type().map(|t| t.is_dir()).unwrap_or(false);
            if is_dir && is_month_folder(&name) {
                Some((name, e.path()))
            } else {
                None
            }
        })
        .collect();
    months.sort_by(|a, b| natural_cmp(&a.0, &b.0));
    months
}

/// Pindai SATU folder bulan → baris-baris sekolah di bulan itu.
/// Dipakai scan penuh (scan_reguler) dan refresh inkremental
/// (hanya bulan yang mtime-nya berubah yang dipindai ulang).
pub fn scan_reguler_month(_root: &Path, bulan: &str, mpath: &Path) -> Vec<RegulerRow> {
    let mut rows = Vec::new();
    let Ok(sd) = std::fs::read_dir(mpath) else {
        return rows;
    };
    let mut entries: Vec<(String, PathBuf)> = sd
        .flatten()
        .filter_map(|e| {
            let name = e.file_name().to_string_lossy().into_owned();
            let is_dir = e.file_type().map(|t| t.is_dir()).unwrap_or(false);
            if is_dir && !is_hidden_name(&name) {
                Some((name, e.path()))
            } else {
                None
            }
        })
        .collect();
    entries.sort_by(|a, b| natural_cmp(&a.0, &b.0));
    for (name, path) in entries {
        // Dua bentuk layout bulan: langsung sekolah (bulan →
        // sekolah, berisi file foto) atau lewat kategori bernomor
        // (bulan → "1. PAS FOTO" → sekolah). Kategori dikenali
        // karena isinya HANYA subfolder sekolah (tanpa file);
        // sekolah dikenali karena berisi file (foto/catatan).
        let child_dirs = subdirs_sorted(&path);
        let has_files = std::fs::read_dir(&path)
            .map(|rd| {
                rd.flatten().any(|e| {
                    e.file_type().map(|t| t.is_file()).unwrap_or(false)
                })
            })
            .unwrap_or(false);
        // Sekolah dikenali juga dari anak PILIHAN-nya: kategori (mis.
        // "1. PAS FOTO") berisi sekolah, bukan PILIHAN langsung — jadi
        // folder yang punya anak bernama PILIHAN adalah sekolah, walau
        // tidak punya file langsung (tanpa NOTE.txt). Tanpa ini,
        // sekolah semacam itu salah dibaca sebagai kategori dan
        // namanya jadi "PILIHAN" (temuan verifikasi 28 Nov 2026:
        // SDN-B yang hanya berisi folder PILIHAN).
        let has_pilihan_child = child_dirs
            .iter()
            .any(|(n, _)| n.eq_ignore_ascii_case("pilihan"));
        if !child_dirs.is_empty() && !has_files && !has_pilihan_child {
            for (sekolah, spath) in child_dirs {
                let marketing = marketing_from_note(&spath);
                rows.push(RegulerRow {
                    bulan: bulan.to_string(),
                    marketing,
                    sekolah,
                    path: spath.to_string_lossy().into_owned(),
                    // Hitungan file rekursif (angka di kanan nama):
                    // dihitung di sini — di thread scan latar — lalu
                    // disimpan di cache, supaya UI tinggal membaca.
                    counts: count_files_recursive(&spath),
                });
            }
        } else {
            let marketing = marketing_from_note(&path);
            rows.push(RegulerRow {
                bulan: bulan.to_string(),
                marketing,
                sekolah: name,
                path: path.to_string_lossy().into_owned(),
                counts: count_files_recursive(&path),
            });
        }
    }
    rows
}

// ---------------------------------------------------------------------------
// Folder Reguler — CACHE + REFRESH INKREMENTAL (28 Nov 2026, temuan
// master_creator: tiap buka dialog, _RegularScanWorker jalan ulang
// penuh dari nol + _fill_table membangun ulang ribuan baris; hasil
// scan TIDAK pernah disimpan; root UNC jadi tiap listing = satu
// round-trip SMB. Port menyimpan hasil scan terakhir di DB dan
// hanya memindai ulang bulan yang mtime-nya berubah — tanpa
// FileSystemWatcher lewat SMB (event sering tidak sampai),
// bandingkan mtime seperti watcher Explorer (polling 2 detik).
// ---------------------------------------------------------------------------

/// Detik epoch sekarang (untuk `scanned_at`).
pub fn now_unix() -> u64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0)
}

/// Fingerprint satu folder bulan: nama + mtime (nanodetik epoch).
/// Nanodetik — bukan detik — supaya perubahan dalam detik yang sama
/// tetap terdeteksi (scan lalu tambah sekolah seketika).
/// mtime 0 = metadata tak terbaca → selalu dianggap berubah (aman).
#[derive(Debug, Clone, PartialEq, serde::Serialize, serde::Deserialize)]
pub struct MonthFp {
    pub name: String,
    pub mtime: u64,
}

/// Cache hasil scan Folder Reguler (DB `Batch.FolderReguler.Cache`,
/// JSON): baris terakhir + fingerprint mtime tiap folder bulan +
/// waktu scan. Daftar tampil instan dari cache; worker latar
/// membandingkan fingerprint dan hanya memindai ulang bulan berubah.
#[derive(Debug, Clone, PartialEq, serde::Serialize, serde::Deserialize)]
pub struct RegulerCache {
    pub dir: String,
    pub scanned_at: u64,
    pub months: Vec<MonthFp>,
    pub rows: Vec<RegulerRow>,
    /// Versi hitungan file per baris (28 Nov 2026): 0 = cache lama
    /// tanpa `counts` (sebelum angka file) — refresh inkremental
    /// memindai ulang semua bulan sekali untuk mengisinya, lalu
    /// menyimpan 1. `#[serde(default)]` = cache lama tetap terbaca.
    #[serde(default)]
    pub counts_v: u32,
}

/// Fingerprint murah satu root: daftar + mtime folder bulan saja
/// (segelintir round-trip — tanpa membaca isi tiap bulan).
pub fn reguler_month_fingerprints(root: &Path) -> Vec<MonthFp> {
    month_folders_sorted(root)
        .iter()
        .map(|(name, path)| MonthFp {
            name: name.clone(),
            mtime: std::fs::metadata(path)
                .and_then(|m| m.modified())
                .ok()
                .and_then(|t| t.duration_since(std::time::UNIX_EPOCH).ok())
                .map(|d| d.as_nanos() as u64)
                .unwrap_or(0),
        })
        .collect()
}

/// Bandingkan fingerprint cache dengan fingerprint sekarang:
/// (bulan berubah/baru → pindai ulang, bulan hilang → buang barisnya).
/// Bulan yang mtime-nya sama persis dilewati.
pub fn reguler_cache_diff(
    old: &RegulerCache,
    new_fps: &[MonthFp],
) -> (Vec<String>, Vec<String>) {
    let changed: Vec<String> = new_fps
        .iter()
        .filter(|fp| {
            old.months
                .iter()
                .find(|m| m.name == fp.name)
                .map(|m| m.mtime != fp.mtime)
                .unwrap_or(true)
        })
        .map(|fp| fp.name.clone())
        .collect();
    let removed: Vec<String> = old
        .months
        .iter()
        .filter(|m| !new_fps.iter().any(|fp| fp.name == m.name))
        .map(|m| m.name.clone())
        .collect();
    (changed, removed)
}

/// Gabungkan baris cache dengan hasil pindai ulang bulan-bulan yang
/// berubah: buang baris bulan berubah/hilang, lalu tambah baris baru;
/// urut lagi (bulan natural, lalu sekolah natural — sama seperti
/// scan penuh).
pub fn reguler_merge_rows(
    cached: &[RegulerRow],
    changed: &[String],
    removed: &[String],
    fresh: Vec<RegulerRow>,
) -> Vec<RegulerRow> {
    let mut rows: Vec<RegulerRow> = cached
        .iter()
        .filter(|r| !changed.contains(&r.bulan) && !removed.contains(&r.bulan))
        .cloned()
        .collect();
    rows.extend(fresh);
    rows.sort_by(|a, b| {
        natural_cmp(&a.bulan, &b.bulan).then_with(|| natural_cmp(&a.sekolah, &b.sekolah))
    });
    rows
}

/// Resolusi PILIHAN (temuan master_creator `_resolve_pilihan_dir`,
/// open_folder_reguler_popup — dibaca PERSIS dari bytecode,
/// carve.py → mc_extract/, Python 3.13 + mc_dis_local.py,
/// 28 Nov 2026 14:55; hal terbuka sebelumnya kini TERTUTUP).
/// Urutan kandidat, berhenti di yang pertama ketemu:
/// 1. pp = normpath(path.strip()); kosong / bukan dir → None.
/// 2. base = basename(pp); base.strip().upper() == "PILIHAN" →
///    kembalikan pp apa adanya (path itu sendiri sudah PILIHAN).
/// 3. cand = join(pp, "PILIHAN"); isdir(cand) → kembalikan cand.
/// 4. Scan anak-anak pp satu-satu; nama .strip().upper() ==
///    "PILIHAN" DAN is_dir → kembalikan join(pp, nama) — jadi
///    "Pilihan"/"pilihan" huruf apa pun diterima di langkah ini,
///    tapi langkah 3 yang persis huruf besar dicek dulu.
/// 5. Tidak ada → None (missing; folder tidak ditambahkan).
/// Semua kegagalan (basename/listdir) ditangkap → None/lanjut.
/// (Rust PathBuf dari read_dir join sudah ternormalisasi —
/// setara normpath; tidak ada trailing-slash.)
pub fn resolve_pilihan_dir(school: &Path) -> Option<PathBuf> {
    // 1. Bukan dir → missing.
    if !school.is_dir() {
        return None;
    }
    // 2. Folder itu sendiri sudah PILIHAN.
    if let Some(base) = school.file_name().and_then(|n| n.to_str()) {
        if base.trim().to_ascii_uppercase() == "PILIHAN" {
            return Some(school.to_path_buf());
        }
    }
    // 3. Subfolder persis "PILIHAN" (huruf besar).
    let cand = school.join("PILIHAN");
    if cand.is_dir() {
        return Some(cand);
    }
    // 4. Scan anak-anak: varian huruf apa pun.
    if let Ok(rd) = std::fs::read_dir(school) {
        for e in rd.flatten() {
            let name = e.file_name().to_string_lossy().into_owned();
            if name.trim().to_ascii_uppercase() == "PILIHAN"
                && e.file_type().map(|t| t.is_dir()).unwrap_or(false)
            {
                return Some(e.path());
            }
        }
    }
    // 5. Tidak ada.
    None
}

/// Subdirektori (bukan file tersembunyi) satu folder, urut natural.
fn subdirs_sorted(path: &Path) -> Vec<(String, PathBuf)> {
    let Ok(rd) = std::fs::read_dir(path) else {
        return Vec::new();
    };
    let mut v: Vec<(String, PathBuf)> = rd
        .flatten()
        .filter_map(|e| {
            let name = e.file_name().to_string_lossy().into_owned();
            let is_dir = e.file_type().map(|t| t.is_dir()).unwrap_or(false);
            if is_dir && !is_hidden_name(&name) {
                Some((name, e.path()))
            } else {
                None
            }
        })
        .collect();
    v.sort_by(|a, b| natural_cmp(&a.0, &b.0));
    v
}

// ---------------------------------------------------------------------------
// Folder Reguler — satu direktori kerja tersimpan (ala master_creator:
// _dir_manager_*: dropdown + tombol hapus, status per-direktori dengan
// warna, work lock, ingat konteks output per direktori).
// Disimpan di DB `Batch.FolderReguler` sebagai JSON array.
// ---------------------------------------------------------------------------

#[derive(Debug, Clone, PartialEq, serde::Serialize, serde::Deserialize)]
pub struct RegulerDir {
    pub path: String,
    /// Kunci manual (pin): tidak bisa dihapus dari daftar selama terkunci.
    #[serde(default)]
    pub locked: bool,
    /// Konteks output terakhir yang dipakai bersama direktori ini
    /// (nilai `Paths.LocalOutput` saat run terakhir) — dipulihkan saat
    /// direktori dipilih lagi (master_creator: _remember_output_context
    /// / _dir_manager_restore_output_context).
    #[serde(default)]
    pub last_output: String,
}

impl RegulerDir {
    pub fn new(path: &str) -> Self {
        RegulerDir { path: path.to_string(), locked: false, last_output: String::new() }
    }

    /// Nama tampil di dropdown (nama folder terakhir).
    pub fn display_name(&self) -> String {
        std::path::Path::new(&self.path)
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_else(|| self.path.clone())
    }
}

// ---------------------------------------------------------------------------
// Working set — sekolah/folder yang sudah dibuka sebagai SOURCE (daftar
// Folder Reguler, Pilih Folder, kirim dari Explorer). Tampil di dropdown
// direktori di atas treeview (ala "Direktori :" master_creator):
// ganti dropdown = ganti sekolah di treeview. Terbaru dulu, maks 20.
// Disimpan di DB `Batch.WorkingSet` sebagai JSON array.
// ---------------------------------------------------------------------------

#[derive(Debug, Clone, PartialEq, serde::Serialize, serde::Deserialize)]
pub struct WorkingSetEntry {
    pub name: String,
    pub path: String,
}

pub const WORKING_SET_MAX: usize = 20;

/// Masukkan (nama, path) ke depan working set: dedup berdasarkan path
/// (tak peka huruf besar — Windows), potong ke maks 20.
pub fn working_set_add(set: &mut Vec<WorkingSetEntry>, name: &str, path: &str) {
    let lower = path.to_ascii_lowercase();
    set.retain(|e| e.path.to_ascii_lowercase() != lower);
    set.insert(
        0,
        WorkingSetEntry { name: name.to_string(), path: path.to_string() },
    );
    set.truncate(WORKING_SET_MAX);
}

/// Status satu direktori reguler (teks kunci untuk .slint — warna
/// dipetakan di sana; prioritas: hilang > dipakai > terkunci > kosong).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RegulerStatus {
    Ready,
    Empty,
    Missing,
    Locked,
    InUse,
}

impl RegulerStatus {
    pub fn key(&self) -> &'static str {
        match self {
            RegulerStatus::Ready => "ready",
            RegulerStatus::Empty => "empty",
            RegulerStatus::Missing => "missing",
            RegulerStatus::Locked => "locked",
            RegulerStatus::InUse => "inuse",
        }
    }

    pub fn label(&self) -> &'static str {
        match self {
            RegulerStatus::Ready => "Siap",
            RegulerStatus::Empty => "Kosong",
            RegulerStatus::Missing => "Tidak ada",
            RegulerStatus::Locked => "Terkunci",
            RegulerStatus::InUse => "Dipakai",
        }
    }
}

/// Apakah direktori punya minimal satu subfolder bulan
/// (pola `DD BULAN YYYY`)?
pub fn reguler_has_months(path: &Path) -> bool {
    let Ok(rd) = std::fs::read_dir(path) else {
        return false;
    };
    rd.flatten().any(|e| {
        let name = e.file_name().to_string_lossy().into_owned();
        e.file_type().map(|t| t.is_dir()).unwrap_or(false) && is_month_folder(&name)
    })
}

pub fn reguler_status(dir: &RegulerDir, in_use: bool) -> RegulerStatus {
    let p = std::path::Path::new(&dir.path);
    if !p.is_dir() {
        return RegulerStatus::Missing;
    }
    if in_use {
        return RegulerStatus::InUse;
    }
    if dir.locked {
        return RegulerStatus::Locked;
    }
    if reguler_has_months(p) {
        RegulerStatus::Ready
    } else {
        RegulerStatus::Empty
    }
}

/// Bulan-bulan unik dari hasil scan (urut natural), untuk filter bulan.
pub fn reguler_months(rows: &[RegulerRow]) -> Vec<String> {
    let mut months: Vec<String> = rows.iter().map(|r| r.bulan.clone()).collect();
    months.sort_by(|a, b| natural_cmp(a, b));
    months.dedup();
    months
}

/// Filter baris scan: teks cari (nama sekolah, tak peka huruf besar) +
/// bulan ("" = semua bulan).
pub fn filter_reguler_rows(rows: &[RegulerRow], query: &str, month: &str) -> Vec<RegulerRow> {
    let q = query.trim().to_ascii_lowercase();
    rows.iter()
        .filter(|r| {
            (month.is_empty() || r.bulan == month)
                && (q.is_empty()
                    || r.sekolah.to_ascii_lowercase().contains(&q)
                    || r.marketing.to_ascii_lowercase().contains(&q))
        })
        .cloned()
        .collect()
}

// ---------------------------------------------------------------------------
// Task Manager — tugas BATCH (desain master_creator, MEMORY 28 Nov 00:56)
// ---------------------------------------------------------------------------

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PhaseState {
    Pending,
    Running,
    Done,
    Failed,
    Waiting,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TaskState {
    Queued,
    Running,
    /// Barrier: menunggu save Photoshop selesai sebelum lanjut
    /// (master_creator: _automation_wait_for_save + prompt lanjut).
    WaitingSave,
    Done,
    Failed,
    Cancelled,
}

#[derive(Debug, Clone)]
pub struct TaskPhase {
    pub label: String,
    pub state: PhaseState,
}

#[derive(Debug, Clone)]
pub struct BatchTask {
    pub id: String,
    pub title: String,
    pub state: TaskState,
    /// 0..100
    pub progress: i32,
    pub phases: Vec<TaskPhase>,
    pub lines: Vec<String>,
    /// Follow-up yang antre setelah tugas ini (mis. duplikat setelah
    /// save) — master_creator: _automation_enqueue_followup.
    pub followups: Vec<String>,
}

impl BatchTask {
    pub fn new(id: &str, title: &str, phase_labels: &[&str]) -> Self {
        BatchTask {
            id: id.to_string(),
            title: title.to_string(),
            state: TaskState::Queued,
            progress: 0,
            phases: phase_labels.iter().map(|l| TaskPhase { label: l.to_string(), state: PhaseState::Pending }).collect(),
            lines: Vec::new(),
            followups: Vec::new(),
        }
    }

    pub fn set_phase(&mut self, idx: usize, state: PhaseState) {
        if let Some(p) = self.phases.get_mut(idx) {
            p.state = state;
        }
        let total = self.phases.len().max(1) as i32;
        let done = self.phases.iter().filter(|p| p.state == PhaseState::Done).count() as i32;
        self.progress = done * 100 / total;
    }

    /// Tandai fase yang labelnya sama dengan path root (cocokkan nama
    /// folder terakhir) — dipakai timer drain saat RootDone tiba.
    pub fn set_phase_by_root(&mut self, root_path: &str, state: PhaseState) {
        let name = std::path::Path::new(root_path)
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_else(|| root_path.to_string());
        if let Some(idx) = self.phases.iter().position(|p| p.label == name) {
            self.set_phase(idx, state);
        } else if let Some(idx) = self.phases.iter().position(|p| p.state == PhaseState::Running) {
            self.set_phase(idx, state);
        }
    }

    /// Tambah baris log ke detail tugas (hanya N terakhir disimpan).
    pub fn append_line(&mut self, line: &str) {
        self.lines.push(line.to_string());
        if self.lines.len() > 40 {
            let excess = self.lines.len() - 40;
            self.lines.drain(..excess);
        }
    }

    /// Kunci teks state untuk .slint (warna dipetakan di sana).
    pub fn state_key(&self) -> &'static str {
        match self.state {
            TaskState::Queued => "queued",
            TaskState::Running => "running",
            TaskState::WaitingSave => "waiting",
            TaskState::Done => "done",
            TaskState::Failed => "failed",
            TaskState::Cancelled => "cancelled",
        }
    }

    pub fn state_label(&self) -> &'static str {
        match self.state {
            TaskState::Queued => "Antre",
            TaskState::Running => "Berjalan",
            TaskState::WaitingSave => "Menunggu save",
            TaskState::Done => "Selesai",
            TaskState::Failed => "Gagal",
            TaskState::Cancelled => "Dibatalkan",
        }
    }
}

// ---------------------------------------------------------------------------
// Manifest copy-edit (28 Nov 2026 — indikator manifest saat sekolah
// dibuka ulang). Cermin PERSIS `_photoshop_utils.manifest_path_for`
// (buat_master.py): path STABIL per folder PILIHAN, supaya DUPLIKAT
// bisa menemukannya lagi kapan saja setelah fase 1 selesai:
//   <oke_base>/_bm_copy_edit/manifest_<sha1(normcase(abspath(pilihan)))[:12]>.json
// `normcase` = di Windows huruf kecil + '/' → '\'; di POSIX identitas
// (sama seperti Python). SHA-1 ditulis tangan di sini (bukan crate
// baru — build offline; vektor uji di bawah dicocokkan ke hashlib).
// ---------------------------------------------------------------------------

/// SHA-1 (FIPS 180-1), mengembalikan hex lowercase 40 karakter.
pub fn sha1_hex(data: &[u8]) -> String {
    let mut h: [u32; 5] = [0x67452301, 0xEFCDAB89, 0x98BADCFE, 0x10325476, 0xC3D2E1F0];
    let bitlen = (data.len() as u64).wrapping_mul(8);
    let mut msg = data.to_vec();
    msg.push(0x80);
    while msg.len() % 64 != 56 {
        msg.push(0);
    }
    msg.extend_from_slice(&bitlen.to_be_bytes());
    for chunk in msg.chunks_exact(64) {
        let mut w = [0u32; 80];
        for i in 0..16 {
            w[i] = u32::from_be_bytes([chunk[i * 4], chunk[i * 4 + 1], chunk[i * 4 + 2], chunk[i * 4 + 3]]);
        }
        for i in 16..80 {
            w[i] = (w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16]).rotate_left(1);
        }
        let (mut a, mut b, mut c, mut d, mut e) = (h[0], h[1], h[2], h[3], h[4]);
        for (i, &wi) in w.iter().enumerate() {
            let (f, k) = match i {
                0..=19 => ((b & c) | ((!b) & d), 0x5A827999u32),
                20..=39 => (b ^ c ^ d, 0x6ED9EBA1),
                40..=59 => ((b & c) | (b & d) | (c & d), 0x8F1BBCDC),
                _ => (b ^ c ^ d, 0xCA62C1D6),
            };
            let tmp = a
                .rotate_left(5)
                .wrapping_add(f)
                .wrapping_add(e)
                .wrapping_add(k)
                .wrapping_add(wi);
            e = d;
            d = c;
            c = b.rotate_left(30);
            b = a;
            a = tmp;
        }
        h[0] = h[0].wrapping_add(a);
        h[1] = h[1].wrapping_add(b);
        h[2] = h[2].wrapping_add(c);
        h[3] = h[3].wrapping_add(d);
        h[4] = h[4].wrapping_add(e);
    }
    h.iter().map(|x| format!("{:08x}", x)).collect()
}

/// Path manifest untuk satu folder PILIHAN (sumber aktif), cermin
/// `manifest_path_for(pilihan_path, output_base)` — output_base =
/// OKE BASE (`Paths.OkeBase`). `None` bila oke_base kosong.
pub fn manifest_path_for(pilihan: &Path, oke_base: &str) -> Option<PathBuf> {
    if oke_base.trim().is_empty() {
        return None;
    }
    // normcase: Windows → huruf kecil + '/' → '\'; POSIX → apa adanya.
    let abs = if pilihan.is_absolute() {
        pilihan.to_path_buf()
    } else {
        std::env::current_dir().unwrap_or_default().join(pilihan)
    };
    let key_src = {
        let s = abs.to_string_lossy().into_owned();
        #[cfg(windows)]
        let s = s.replace('/', "\\").to_ascii_lowercase();
        s
    };
    let key = &sha1_hex(key_src.as_bytes())[..12];
    Some(
        PathBuf::from(oke_base)
            .join("_bm_copy_edit")
            .join(format!("manifest_{}.json", key)),
    )
}

/// Apakah file manifest untuk folder sumber ini ada di disk.
pub fn manifest_exists(pilihan: &Path, oke_base: &str) -> bool {
    manifest_path_for(pilihan, oke_base)
        .map(|p| p.is_file())
        .unwrap_or(false)
}

// ---------------------------------------------------------------------------
// State keseluruhan BATCH (dipegang main.rs)
// ---------------------------------------------------------------------------

#[derive(Debug, Default)]
pub struct BatchState {
    pub roots: Vec<BatchRoot>,
    pub local_output: String,
    pub oke_base: String,
    pub selection: BatchSelection,
    pub has_manifest: bool,
    /// Ekstensi yang dihitung untuk angka file di treeview + daftar
    /// Folder Reguler (`jpg` | `png` | `psd` | `all`; DB
    /// `Batch.FileCountExtension`, default `"jpg"`). "" = belum dimuat
    /// dari DB — `valid_file_count_ext` mengembalikannya ke `"jpg"`.
    pub file_count_ext: String,
    /// Folder Reguler tersimpan (DB `Batch.FolderReguler`, JSON array).
    pub reguler_dirs: Vec<RegulerDir>,
    pub reguler_active: String,
    /// Working set: sekolah/folder yang sudah dibuka sebagai SOURCE
    /// (DB `Batch.WorkingSet`, JSON array). Dropdown di atas treeview.
    pub working_set: Vec<WorkingSetEntry>,
    /// Tugas Task Manager (terbaru dulu, maks 8 — lihat `push_task`).
    pub tasks: Vec<BatchTask>,
    /// ID tugas run yang sedang aktif ("" = tidak ada).
    pub current_task_id: String,
}

impl BatchState {
    /// Tambah tugas baru di depan (maks 8, yang lama dibuang).
    pub fn push_task(&mut self, task: BatchTask) {
        self.current_task_id = task.id.clone();
        self.tasks.insert(0, task);
        self.tasks.truncate(8);
    }

    pub fn active_task_mut(&mut self) -> Option<&mut BatchTask> {
        if self.current_task_id.is_empty() {
            return None;
        }
        let id = self.current_task_id.clone();
        self.tasks.iter_mut().find(|t| t.id == id)
    }

    pub fn task_mut(&mut self, id: &str) -> Option<&mut BatchTask> {
        self.tasks.iter_mut().find(|t| t.id == id)
    }
}

impl BatchState {
    /// AddFolders() C#: MENGGANTI seluruh koleksi (bukan append).
    /// Sumber baru juga masuk working set (dropdown treeview).
    /// Nama working set = nama folder (file_name).
    pub fn add_folder(&mut self, source_path: &Path) {
        let name = source_path
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_else(|| source_path.to_string_lossy().into_owned());
        self.add_folder_named(source_path, &name);
    }

    /// Seperti add_folder, tapi nama working set diberikan eksplisit —
    /// dipakai saat sumbernya hasil resolusi PILIHAN (nama yang tampil
    /// tetap nama sekolah yang dipilih user, bukan "PILIHAN").
    pub fn add_folder_named(&mut self, source_path: &Path, name: &str) {
        let local = PathBuf::from(&self.local_output);
        working_set_add(
            &mut self.working_set,
            name,
            &source_path.to_string_lossy(),
        );
        self.roots = vec![BatchRoot::new(source_path, &local)];
        self.selection.clear();
        // Indikator manifest mencerminkan FILE di disk (28 Nov 2026):
        // dulu selalu false saat sekolah dibuka (ulang) padahal file
        // manifestnya masih ada — kini dibaca dari
        // <oke_base>/_bm_copy_edit/manifest_<hash>.json (cermin
        // manifest_path_for Python). Oke base kosong → false.
        self.has_manifest = manifest_exists(source_path, &self.oke_base.clone());
    }

    pub fn clear(&mut self) {
        self.roots.clear();
        self.selection.clear();
        self.has_manifest = false;
    }

    pub fn is_empty(&self) -> bool {
        self.roots.is_empty()
    }

    pub fn src_rows(&self) -> Vec<FlatRow> {
        let sel: HashSet<String> = self.selection.ids.iter().cloned().collect();
        let mut rows = flatten_source(&self.roots, &sel);
        fill_file_counts(&mut rows, &self.file_count_ext);
        rows
    }

    pub fn out_rows(&self) -> Vec<FlatRow> {
        let sel: HashSet<String> = self.selection.ids.iter().cloned().collect();
        let mut rows = flatten_output(&self.roots, &sel);
        fill_file_counts(&mut rows, &self.file_count_ext);
        rows
    }

    /// Nama folder aktif untuk header (1 root → nama folder sumbernya;
    /// >1 → nama root terpilih; kosong → "").
    pub fn header_source_name(&self) -> String {
        if self.roots.len() == 1 {
            let p = &self.roots[0].source_path;
            // Sumber hasil resolusi PILIHAN: header menampilkan nama
            // sekolah (parent), bukan "PILIHAN" — user memilih sekolah,
            // isi tree-nya memang dari folder PILIHAN.
            if p.file_name().map(|n| n.eq_ignore_ascii_case("pilihan")).unwrap_or(false) {
                return p
                    .parent()
                    .and_then(|par| par.file_name())
                    .map(|n| n.to_string_lossy().into_owned())
                    .unwrap_or_default();
            }
            p.file_name()
                .map(|n| n.to_string_lossy().into_owned())
                .unwrap_or_default()
        } else {
            String::new()
        }
    }

    /// Apakah ada output yang pending (folder output belum ada di disk).
    pub fn any_output_pending(&self) -> bool {
        self.roots.iter().any(|r| r.output_root.is_none())
    }
}

// ---------------------------------------------------------------------------
// Unit tests
// ---------------------------------------------------------------------------

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;

    fn tmpdir(name: &str) -> PathBuf {
        let p = std::env::temp_dir().join(format!("bmachine-batch-test-{}", name));
        let _ = fs::remove_dir_all(&p);
        fs::create_dir_all(&p).unwrap();
        p
    }

    #[test]
    fn natural_sort_orders_numbers_numerically() {
        let mut v = vec!["10RP", "2", "1. PAS FOTO", "02 POSE", "1"];
        v.sort_by(|a, b| natural_cmp(a, b));
        assert_eq!(v, vec!["1", "1. PAS FOTO", "2", "02 POSE", "10RP"]);
    }

    #[test]
    fn month_folder_detection() {
        assert!(is_month_folder("02 AGUSTUS 2025"));
        assert!(is_month_folder("11 November 2026"));
        assert!(!is_month_folder("27_SDN RANCAIYUH I"));
        assert!(!is_month_folder("AGUSTUS 2025"));
        assert!(!is_month_folder("02 AGUSTUS"));
    }

    #[test]
    fn predict_output_with_month_folder() {
        let local = Path::new("/tmp/OUT");
        let src = Path::new("/data/02 AGUSTUS 2025/27_SDN RANCAIYUH I");
        assert_eq!(
            predict_output_path(local, src),
            PathBuf::from("/tmp/OUT/27_SDN RANCAIYUH I")
        );
        // Subfolder di bawah sekolah ikut relatif.
        let src2 = Path::new("/data/02 AGUSTUS 2025/27_SDN RANCAIYUH I/PILIHAN");
        assert_eq!(
            predict_output_path(local, src2),
            PathBuf::from("/tmp/OUT/27_SDN RANCAIYUH I")
        );
    }

    #[test]
    fn predict_output_pilihan_maps_to_parent() {
        let local = Path::new("/tmp/OUT");
        let src = Path::new("/data/11 NOVEMBER 2026/28_SDN BANTAR KAMBING 03/PILIHAN");
        assert_eq!(
            predict_output_path(local, src),
            PathBuf::from("/tmp/OUT/28_SDN BANTAR KAMBING 03")
        );
    }

    #[test]
    fn predict_output_fallback_is_folder_name() {
        let local = Path::new("/tmp/OUT");
        let src = Path::new("/data/random/27_SDN RANCAIYUH I");
        assert_eq!(
            predict_output_path(local, src),
            PathBuf::from("/tmp/OUT/27_SDN RANCAIYUH I")
        );
    }

    #[test]
    fn scan_hides_lnk_and_desktop_ini() {
        let d = tmpdir("scan");
        fs::create_dir(d.join("1. PAS FOTO")).unwrap();
        fs::write(d.join("desktop.ini"), b"x").unwrap();
        fs::write(d.join("jalan pintas.lnk"), b"x").unwrap();
        fs::write(d.join("foto_001.jpg"), b"x").unwrap();
        let kids = scan_children(&d);
        let names: Vec<&str> = kids.iter().map(|k| k.name.as_str()).collect();
        assert_eq!(names, vec!["1. PAS FOTO", "foto_001.jpg"]);
    }

    #[test]
    fn selection_plain_ctrl_shift() {
        let rows: Vec<FlatRow> = ["a", "b", "c", "d", "e"]
            .iter()
            .map(|n| FlatRow {
                id: n.to_string(),
                name: n.to_string(),
                path: n.to_string(),
                depth: 0,
                is_dir: true,
                expanded: false,
                has_kids: false,
                selected: false,
                is_root: false,
                file_count: -1,
            })
            .collect();
        let mut s = BatchSelection::default();
        s.click(BatchPanel::Source, &rows, "b", false, false);
        assert_eq!(s.ids, vec!["b"]);
        // Ctrl toggle tambah.
        s.click(BatchPanel::Source, &rows, "d", false, true);
        assert_eq!(s.ids, vec!["b", "d"]);
        // Ctrl toggle lepas.
        s.click(BatchPanel::Source, &rows, "b", false, true);
        assert_eq!(s.ids, vec!["d"]);
        // Shift range dari anchor (b, item terakhir diklik) ke e → b,c,d,e.
        s.click(BatchPanel::Source, &rows, "e", true, false);
        assert_eq!(s.ids, vec!["b", "c", "d", "e"]);
        // Save/restore klik kanan.
        s.save();
        s.ids = vec!["e".to_string()];
        s.restore();
        assert_eq!(s.ids, vec!["b", "c", "d", "e"]);
    }

    #[test]
    fn replace_pairs_longest_prefix_wins() {
        let d = tmpdir("replace");
        // Root luar + root bersarang di dalamnya.
        let outer = d.join("27_SDN RANCAIYUH I");
        let inner = outer.join("1. PAS FOTO");
        fs::create_dir_all(&inner).unwrap();
        fs::create_dir(inner.join("SUB")).unwrap();
        let local = d.join("OUT");
        let mut st = BatchState::default();
        st.local_output = local.to_string_lossy().into_owned();
        // Dua root: luar (source=outer) + bersarang (source=inner).
        st.roots = vec![
            BatchRoot::new(&outer, &local),
            BatchRoot::new(&inner, &local),
        ];
        // Expand root luar supaya node SUB terlihat oleh find_node
        // (find_node jalan di children yang sudah dimuat).
        st.roots[0].source_root.load_children();
        st.roots[1].source_root.load_children();
        let sub_id = inner.join("SUB").to_string_lossy().into_owned();
        let pairs = resolve_replace_pairs(&st.roots, BatchPanel::Source, &[sub_id]);
        assert_eq!(pairs.len(), 1);
        // Prefix terpanjang = inner → output = OUT/1. PAS FOTO/SUB.
        assert_eq!(pairs[0].0, local.join("1. PAS FOTO").join("SUB"));
        assert_eq!(pairs[0].1, inner.join("SUB"));
    }

    #[test]
    fn flatten_single_root_hides_root_row() {
        let d = tmpdir("flat");
        fs::create_dir(d.join("1. PAS FOTO")).unwrap();
        fs::create_dir(d.join("2. WISUDA")).unwrap();
        let local = d.join("OUT");
        let mut st = BatchState::default();
        st.local_output = local.to_string_lossy().into_owned();
        st.add_folder(&d);
        let rows = st.src_rows();
        let names: Vec<&str> = rows.iter().map(|r| r.name.as_str()).collect();
        assert_eq!(names, vec!["1. PAS FOTO", "2. WISUDA"]);
        assert!(rows.iter().all(|r| !r.is_root && r.depth == 0));
    }

    #[test]
    fn output_pending_until_folder_exists() {
        let d = tmpdir("pending");
        fs::create_dir(d.join("1. PAS FOTO")).unwrap();
        let local = d.join("OUT");
        let mut st = BatchState::default();
        st.local_output = local.to_string_lossy().into_owned();
        st.add_folder(&d);
        assert!(st.any_output_pending());
        assert!(st.out_rows().is_empty());
        // Script membuat folder output → refresh → tidak pending.
        fs::create_dir_all(local.join(d.file_name().unwrap())).unwrap();
        let changed = st.roots[0].refresh_output();
        assert!(changed);
        assert!(!st.any_output_pending());
    }

    #[test]
    fn double_click_detection() {
        use std::time::{Duration, Instant};
        let mut last = None;
        let t0 = Instant::now();
        assert!(!is_double_click(&mut last, "SOURCE", "a", t0));
        assert!(is_double_click(&mut last, "SOURCE", "a", t0 + Duration::from_millis(100)));
        // Setelah double-click, klik berikutnya = single baru.
        assert!(!is_double_click(&mut last, "SOURCE", "a", t0 + Duration::from_millis(200)));
        // Lewat 450ms dari klik terakhir = bukan double-click.
        assert!(!is_double_click(&mut last, "SOURCE", "a", t0 + Duration::from_millis(700)));
        assert!(is_double_click(&mut last, "SOURCE", "a", t0 + Duration::from_millis(800)));
        // Id/panel berbeda = bukan double-click.
        assert!(!is_double_click(&mut last, "SOURCE", "b", t0 + Duration::from_millis(900)));
        assert!(!is_double_click(&mut last, "OUTPUT", "b", t0 + Duration::from_millis(950)));
    }

    #[test]
    fn toggle_expand_lazy_loads_children() {
        let d = tmpdir("toggle");
        let sub = d.join("1. PAS FOTO");
        fs::create_dir(&sub).unwrap();
        fs::write(sub.join("foto_001.jpg"), b"x").unwrap();
        let local = d.join("OUT");
        let mut st = BatchState::default();
        st.local_output = local.to_string_lossy().into_owned();
        st.add_folder(&d);
        let id = sub.to_string_lossy().into_owned();
        // Belum expand → anak tidak tampil.
        assert!(!st.src_rows().iter().any(|r| r.name == "foto_001.jpg"));
        assert!(toggle_expand(&mut st.roots, BatchPanel::Source, &id));
        let rows = st.src_rows();
        assert!(rows.iter().any(|r| r.name == "foto_001.jpg" && r.depth == 1));
        assert!(toggle_expand(&mut st.roots, BatchPanel::Source, &id));
        assert!(!st.src_rows().iter().any(|r| r.name == "foto_001.jpg"));
    }

    #[test]
    fn remove_from_tree_reloads_parent() {
        let d = tmpdir("remove");
        let sub = d.join("1. PAS FOTO");
        fs::create_dir(&sub).unwrap();
        let local = d.join("OUT");
        let mut st = BatchState::default();
        st.local_output = local.to_string_lossy().into_owned();
        st.add_folder(&d);
        assert_eq!(st.src_rows().len(), 1);
        fs::remove_dir(&sub).unwrap();
        remove_from_tree(&mut st.roots, BatchPanel::Source, &sub);
        // Root yang jadi kosong tetap tampil sebagai satu baris
        // (fix folder kosong 28 Nov 2026) — bukan tree kosong.
        let rows = st.src_rows();
        assert_eq!(rows.len(), 1);
        assert_eq!(
            rows[0].name,
            d.file_name().unwrap().to_string_lossy().into_owned()
        );
    }

    #[test]
    fn rescan_all_keeps_loaded_grandchildren_and_selection_resolvable() {
        let d = tmpdir("rescan-keep");
        let month = d.join("07 Juli 2026");
        let sub = month.join("1. PAS FOTO");
        let school = sub.join("SDN X");
        fs::create_dir_all(&school).unwrap();
        let local = d.join("OUT");
        let out_school = local.join("07 Juli 2026").join("1. PAS FOTO").join("SDN X");
        fs::create_dir_all(&out_school).unwrap();
        let mut st = BatchState::default();
        st.local_output = local.to_string_lossy().into_owned();
        st.add_folder(&month);
        // Expand + seleksi cucu, lalu Refresh: pasangan cucu harus
        // tetap bisa di-resolve (regresi: rescan dulu membuang cucu).
        let sub_id = sub.to_string_lossy().into_owned();
        assert!(toggle_expand(&mut st.roots, BatchPanel::Source, &sub_id));
        let school_id = school.to_string_lossy().into_owned();
        let visible = st.src_rows();
        st.selection.click(BatchPanel::Source, &visible, &sub_id, false, false);
        st.selection.click(BatchPanel::Source, &visible, &school_id, false, true);
        assert_eq!(st.selection.ids.len(), 2);
        rescan_all(&mut st.roots);
        assert!(st.src_rows().iter().any(|r| r.id == school_id));
        let pairs = resolve_replace_pairs(&st.roots, BatchPanel::Source, &st.selection.ids);
        assert_eq!(pairs.len(), 2, "pasangan cucu hilang setelah rescan: {:?}", pairs);
    }

    #[test]
    fn reguler_scan_months_and_marketing() {
        let d = tmpdir("reguler");
        let m = d.join("02 AGUSTUS 2025");
        let s = m.join("27_SDN RANCAIYUH I");
        fs::create_dir_all(&s).unwrap();
        fs::write(s.join("NOTE.txt"), "MARKETING: AMEL\nlain-lain\n").unwrap();
        fs::create_dir(m.join("28_SDN BANTAR KAMBING 03")).unwrap();
        fs::create_dir(d.join("bukan bulan")).unwrap();
        let rows = scan_reguler(&d);
        assert_eq!(rows.len(), 2);
        assert_eq!(rows[0].bulan, "02 AGUSTUS 2025");
        assert_eq!(rows[0].sekolah, "27_SDN RANCAIYUH I");
        assert_eq!(rows[0].marketing, "AMEL");
        assert_eq!(rows[1].marketing, "");
    }

    #[test]
    fn empty_source_folder_still_shows_its_root_row() {
        // Temuan verifikasi Dex 28 Nov 2026: folder kosong → src-rows
        // kosong → drop zone muncul lagi. Root-nya sendiri harus tampil.
        let d = tmpdir("empty-src");
        let local = d.join("OUT");
        let mut st = BatchState::default();
        st.local_output = local.to_string_lossy().into_owned();
        st.add_folder(&d);
        let rows = st.src_rows();
        assert_eq!(rows.len(), 1);
        assert_eq!(rows[0].name, "bmachine-batch-test-empty-src");
        assert!(rows[0].is_dir);
    }

    #[test]
    fn nonempty_source_folder_keeps_old_flattening() {
        let d = tmpdir("nonempty-src");
        fs::create_dir(d.join("1. PAS FOTO")).unwrap();
        let local = d.join("OUT");
        let mut st = BatchState::default();
        st.local_output = local.to_string_lossy().into_owned();
        st.add_folder(&d);
        let rows = st.src_rows();
        assert_eq!(rows.len(), 1);
        assert_eq!(rows[0].name, "1. PAS FOTO");
        assert!(!rows[0].is_root);
    }

    #[test]
    fn reguler_status_and_filter() {
        let d = tmpdir("reguler-status");
        let missing = RegulerDir::new("/tmp/tidak-ada-pasti-xyz-123");
        assert_eq!(reguler_status(&missing, false), RegulerStatus::Missing);
        let empty = RegulerDir::new(d.to_str().unwrap());
        assert_eq!(reguler_status(&empty, false), RegulerStatus::Empty);
        let m = d.join("07 Juli 2026");
        fs::create_dir_all(m.join("27_SDN RANCAIYUH I")).unwrap();
        assert_eq!(reguler_status(&empty, false), RegulerStatus::Ready);
        assert_eq!(reguler_status(&empty, true), RegulerStatus::InUse);
        let mut locked = RegulerDir::new(d.to_str().unwrap());
        locked.locked = true;
        assert_eq!(reguler_status(&locked, false), RegulerStatus::Locked);
        // JSON lama (array string) tetap bisa dibaca → migrasi.
        let old: Vec<RegulerDir> =
            serde_json::from_str::<Vec<String>>("[\"/tmp/a\", \"/tmp/b\"]")
                .unwrap()
                .into_iter()
                .map(|p| RegulerDir::new(&p))
                .collect();
        assert_eq!(old.len(), 2);
        assert!(!old[0].locked);
        // Filter: cari sekolah + bulan.
        let rows = scan_reguler(&d);
        assert_eq!(rows.len(), 1);
        assert_eq!(filter_reguler_rows(&rows, "rancaiyuh", "").len(), 1);
        assert_eq!(filter_reguler_rows(&rows, "bantar", "").len(), 0);
        assert_eq!(filter_reguler_rows(&rows, "", "07 Juli 2026").len(), 1);
        assert_eq!(filter_reguler_rows(&rows, "", "02 AGUSTUS 2025").len(), 0);
        assert_eq!(reguler_months(&rows), vec!["07 Juli 2026".to_string()]);
    }

    #[test]
    fn scan_reguler_nested_category_and_flat() {
        let d = tmpdir("reguler-scan-nested");
        // Layout bersarang (asli DMA): bulan → kategori → sekolah.
        let school_a = d
            .join("07 Juli 2026")
            .join("1. PAS FOTO")
            .join("SDN Kedung Waringin 3");
        fs::create_dir_all(&school_a).unwrap();
        fs::write(school_a.join("foto_001.jpg"), b"x").unwrap();
        fs::write(school_a.join("NOTE.txt"), "MARKETING: Nunuy\n").unwrap();
        let school_b = d
            .join("07 Juli 2026")
            .join("2. PROFESI")
            .join("SDN Bantar Kambing 03");
        fs::create_dir_all(&school_b).unwrap();
        fs::write(school_b.join("profesi_001.jpg"), b"x").unwrap();
        // Layout datar: bulan → sekolah langsung.
        let school_c = d.join("08 Agustus 2026").join("SDN Langsung");
        fs::create_dir_all(&school_c).unwrap();
        fs::write(school_c.join("foto.jpg"), b"x").unwrap();
        let rows = scan_reguler(&d);
        assert_eq!(rows.len(), 3);
        let names: Vec<&str> = rows.iter().map(|r| r.sekolah.as_str()).collect();
        assert!(names.contains(&"SDN Kedung Waringin 3"));
        assert!(names.contains(&"SDN Bantar Kambing 03"));
        assert!(names.contains(&"SDN Langsung"));
        // Kategori ("1. PAS FOTO") TIDAK boleh muncul sebagai sekolah.
        assert!(!names.iter().any(|n| n.contains("PAS FOTO")));
        let a = rows.iter().find(|r| r.sekolah == "SDN Kedung Waringin 3").unwrap();
        assert_eq!(a.bulan, "07 Juli 2026");
        assert_eq!(a.marketing, "Nunuy");
        let b = rows.iter().find(|r| r.sekolah == "SDN Bantar Kambing 03").unwrap();
        assert_eq!(b.marketing, "");
        assert_eq!(reguler_months(&rows).len(), 2);
    }

    #[test]
    fn reguler_cache_diff_and_merge_only_touched_month() {
        let root = tmpdir("cache-diff");
        let m1 = root.join("02 AGUSTUS 2026");
        let m2 = root.join("03 SEPTEMBER 2026");
        fs::create_dir_all(m1.join("SDN-A")).unwrap();
        fs::create_dir_all(m2.join("SDN-B")).unwrap();
        fs::write(m1.join("SDN-A").join("foto.jpg"), b"x").unwrap();
        fs::write(m2.join("SDN-B").join("foto.jpg"), b"x").unwrap();
        let rows = scan_reguler(&root);
        assert_eq!(rows.len(), 2);
        let fps = reguler_month_fingerprints(&root);
        assert_eq!(fps.len(), 2);
        let cache = RegulerCache {
            dir: root.to_string_lossy().into_owned(),
            scanned_at: now_unix(),
            months: fps.clone(),
            rows: rows.clone(),
            counts_v: 1,
        };
        // Tanpa perubahan -> tidak ada bulan yang dipindai ulang.
        let (changed, removed) = reguler_cache_diff(&cache, &fps);
        assert!(changed.is_empty() && removed.is_empty());
        // Tambah sekolah di bulan 2 saja -> hanya bulan 2 berubah.
        fs::create_dir_all(m2.join("SDN-C")).unwrap();
        fs::write(m2.join("SDN-C").join("foto.jpg"), b"x").unwrap();
        // Sentuh mtime folder bulan 2 (buat dir baru mengubah mtime
        // parent di Linux; pastikan dengan set mtime bila perlu).
        let fps2 = reguler_month_fingerprints(&root);
        let (changed2, removed2) = reguler_cache_diff(&cache, &fps2);
        assert_eq!(changed2, vec!["03 SEPTEMBER 2026".to_string()]);
        assert!(removed2.is_empty());
        // Merge: baris bulan 1 dipertahankan, bulan 2 diganti hasil baru.
        let fresh = scan_reguler_month(&root, "03 SEPTEMBER 2026", &m2);
        assert_eq!(fresh.len(), 2);
        let merged = reguler_merge_rows(&rows, &changed2, &removed2, fresh);
        assert_eq!(merged.len(), 3);
        assert_eq!(merged[0].sekolah, "SDN-A");
        assert_eq!(merged[1].sekolah, "SDN-B");
        assert_eq!(merged[2].sekolah, "SDN-C");
        // Hapus bulan 1 -> terdeteksi sebagai removed. Cache di sini
        // = keadaan setelah merge (fps2 + merged), bukan cache awal.
        let cache2 = RegulerCache {
            dir: root.to_string_lossy().into_owned(),
            scanned_at: now_unix(),
            months: fps2.clone(),
            rows: merged.clone(),
            counts_v: 1,
        };
        fs::remove_dir_all(&m1).unwrap();
        let fps3 = reguler_month_fingerprints(&root);
        let (changed3, removed3) = reguler_cache_diff(&cache2, &fps3);
        assert_eq!(removed3, vec!["02 AGUSTUS 2026".to_string()]);
        assert!(changed3.is_empty());
        let merged3 = reguler_merge_rows(&merged, &changed3, &removed3, Vec::new());
        assert_eq!(merged3.len(), 2);
        assert_eq!(merged3[0].sekolah, "SDN-B");
        assert_eq!(merged3[1].sekolah, "SDN-C");
    }

    #[test]
    fn resolve_pilihan_dir_follows_bytecode_order() {
        let root = tmpdir("pilihan");
        // 1. Bukan dir -> None.
        let notdir = root.join("bukan-dir.txt");
        fs::write(&notdir, b"x").unwrap();
        assert!(resolve_pilihan_dir(&notdir).is_none());
        assert!(resolve_pilihan_dir(&root.join("tidak-ada")).is_none());
        // 2. Folder itu sendiri sudah PILIHAN -> kembalikan apa adanya.
        let p = root.join("PILIHAN");
        fs::create_dir_all(&p).unwrap();
        assert_eq!(resolve_pilihan_dir(&p).unwrap(), p);
        // 3. Subfolder persis "PILIHAN".
        let school = root.join("SDN-A");
        fs::create_dir_all(&school).unwrap();
        assert!(resolve_pilihan_dir(&school).is_none());
        let cand = school.join("PILIHAN");
        fs::create_dir_all(&cand).unwrap();
        assert_eq!(resolve_pilihan_dir(&school).unwrap(), cand);
        // 4. Varian huruf ("Pilihan") diterima bila yang persis tak ada.
        let school2 = root.join("SDN-B");
        let v = school2.join("Pilihan");
        fs::create_dir_all(&v).unwrap();
        assert_eq!(resolve_pilihan_dir(&school2).unwrap(), v);
        // Tanpa PILIHAN tapi ada subfolder lain -> tetap None.
        let school3 = root.join("SDN-C");
        fs::create_dir_all(school3.join("LAIN")).unwrap();
        assert!(resolve_pilihan_dir(&school3).is_none());
    }

    #[test]
    fn header_source_name_shows_school_for_pilihan_source() {
        let mut st = BatchState::default();
        st.local_output = "/tmp/out".to_string();
        st.add_folder(Path::new("/data/28 NOVEMBER 2026/SDN-A/PILIHAN"));
        assert_eq!(st.header_source_name(), "SDN-A");
        st.add_folder(Path::new("/data/28 NOVEMBER 2026/SDN-B"));
        assert_eq!(st.header_source_name(), "SDN-B");
    }

    #[test]
    fn working_set_add_prepends_dedups_and_caps() {
        let mut set = Vec::new();
        working_set_add(&mut set, "SDN-A", "/x/SDN-A");
        working_set_add(&mut set, "SDN-B", "/x/SDN-B");
        assert_eq!(set.len(), 2);
        assert_eq!(set[0].name, "SDN-B");
        // Buka lagi SDN-A (path sama, huruf beda) -> naik ke depan, tetap 2.
        working_set_add(&mut set, "SDN-A", "/X/sdn-a");
        assert_eq!(set.len(), 2);
        assert_eq!(set[0].name, "SDN-A");
        assert_eq!(set[0].path, "/X/sdn-a");
        for i in 0..25 {
            working_set_add(&mut set, &format!("S{}", i), &format!("/x/S{}", i));
        }
        assert_eq!(set.len(), WORKING_SET_MAX);
        assert_eq!(set[0].name, "S24");
    }

    #[test]
    fn task_phase_progress_and_lines() {
        let mut t = BatchTask::new("t1", "buat_master.py — 07 Juli 2026", &["07 Juli 2026", "08 Juli 2026"]);
        assert_eq!(t.progress, 0);
        t.set_phase_by_root("/x/07 Juli 2026", PhaseState::Done);
        assert_eq!(t.progress, 50);
        assert_eq!(t.state_key(), "queued");
        for i in 0..45 {
            t.append_line(&format!("baris {}", i));
        }
        assert_eq!(t.lines.len(), 40);
        assert_eq!(t.lines[0], "baris 5");
        t.followups.push("BUAT MASTER (duplikat)".to_string());
        assert_eq!(t.followups.len(), 1);
    }

    #[test]
    fn sha1_hex_matches_known_vectors() {
        // Vektor FIPS 180-1 (dicocokkan ke Python hashlib.sha1).
        assert_eq!(sha1_hex(b""), "da39a3ee5e6b4b0d3255bfef95601890afd80709");
        assert_eq!(sha1_hex(b"abc"), "a9993e364706816aba3e25717850c26c9cd0d89d");
        assert_eq!(
            sha1_hex(b"/tmp/batchfix-fixture/reguler/28 November 2026/SDN-A/PILIHAN"),
            "b781a54fa4a9feb9c52f06c8d7414ae1a3277067"
        );
    }

    #[test]
    fn manifest_path_for_matches_python() {
        // Hash dihitung Python hashlib untuk path persis ini.
        let p = manifest_path_for(
            Path::new("/tmp/bmachine-batch-test-manifest/SDN-A/PILIHAN"),
            "/tmp/bmachine-batch-test-manifest/oke",
        )
        .unwrap();
        assert_eq!(
            p,
            PathBuf::from("/tmp/bmachine-batch-test-manifest/oke/_bm_copy_edit/manifest_e93cd222a464.json")
        );
        assert!(manifest_path_for(Path::new("/x/PILIHAN"), "").is_none());
        assert!(manifest_path_for(Path::new("/x/PILIHAN"), "   ").is_none());
    }

    #[test]
    fn manifest_exists_reads_file_on_disk() {
        let d = tmpdir("manifest");
        let pilihan = d.join("SDN-A").join("PILIHAN");
        fs::create_dir_all(&pilihan).unwrap();
        let oke = d.join("oke");
        assert!(!manifest_exists(&pilihan, oke.to_str().unwrap()));
        // Buat file manifest di path yang dihitung (seperti yang
        // ditulis buat_master.py setelah Fase 1 sukses).
        let mp = manifest_path_for(&pilihan, oke.to_str().unwrap()).unwrap();
        fs::create_dir_all(mp.parent().unwrap()).unwrap();
        fs::write(&mp, "{}").unwrap();
        assert!(manifest_exists(&pilihan, oke.to_str().unwrap()));
    }

    #[test]
    fn add_folder_named_restores_manifest_indicator() {
        let d = tmpdir("manifest-restore");
        let pilihan = d.join("SDN-A").join("PILIHAN");
        fs::create_dir_all(&pilihan).unwrap();
        let oke = d.join("oke");
        let mut st = BatchState::default();
        st.local_output = d.join("out").to_string_lossy().into_owned();
        st.oke_base = oke.to_string_lossy().into_owned();
        // Tanpa file manifest -> false (perilaku lama tetap benar).
        st.add_folder_named(&pilihan, "SDN-A");
        assert!(!st.has_manifest);
        // File manifest ada -> buka (ulang) sekolah = indikator true.
        let mp = manifest_path_for(&pilihan, st.oke_base.as_str()).unwrap();
        fs::create_dir_all(mp.parent().unwrap()).unwrap();
        fs::write(&mp, "{}").unwrap();
        st.add_folder_named(&pilihan, "SDN-A");
        assert!(st.has_manifest);
    }

    #[test]
    fn count_files_recursive_buckets() {
        let d = tmpdir("filecount");
        let school = d.join("SDN-A");
        let pilihan = school.join("PILIHAN");
        fs::create_dir_all(pilihan.join("1. PAS FOTO")).unwrap();
        fs::write(pilihan.join("foto1.jpg"), b"x").unwrap();
        fs::write(pilihan.join("foto2.JPG"), b"x").unwrap();
        fs::write(pilihan.join("foto3.jpeg"), b"x").unwrap();
        fs::write(pilihan.join("1. PAS FOTO").join("a.png"), b"x").unwrap();
        fs::write(pilihan.join("1. PAS FOTO").join("b.PSD"), b"x").unwrap();
        fs::write(pilihan.join("catatan.txt"), b"x").unwrap();
        fs::write(pilihan.join("NOTE.txt"), b"x").unwrap();
        // Tersembunyi tidak dihitung (desktop.ini / .lnk — sama
        // seperti yang disembunyikan tree; dotfile TIDAK termasuk
        // hidden di sini, jadi tetap dihitung bila ada).
        fs::write(pilihan.join("desktop.ini"), b"x").unwrap();
        let c = count_files_recursive(&school);
        assert_eq!(c.jpg, 3);
        assert_eq!(c.png, 1);
        assert_eq!(c.psd, 1);
        assert_eq!(c.all, 7);
        assert_eq!(c.get("jpg"), 3);
        assert_eq!(c.get("png"), 1);
        assert_eq!(c.get("all"), 7);
        assert_eq!(c.get("tidak-ada"), 3);
        assert_eq!(count_files_recursive(&d.join("tidak-ada")), FileCounts::default());
    }

    #[test]
    fn scan_reguler_month_counts_files() {
        let d = tmpdir("filecount-scan");
        let school = d.join("28 November 2026").join("SDN-A");
        let pilihan = school.join("PILIHAN");
        fs::create_dir_all(&pilihan).unwrap();
        fs::write(pilihan.join("f1.jpg"), b"x").unwrap();
        fs::write(pilihan.join("f2.jpg"), b"x").unwrap();
        fs::write(pilihan.join("f3.png"), b"x").unwrap();
        let rows = scan_reguler(&d);
        assert_eq!(rows.len(), 1);
        assert_eq!(rows[0].counts.jpg, 2);
        assert_eq!(rows[0].counts.png, 1);
        assert_eq!(rows[0].counts.all, 3);
        // Cache lama (tanpa field counts) tetap bisa dibaca.
        let old: RegulerCache = serde_json::from_str(
            r#"{"dir":"/x","scanned_at":0,"months":[],"rows":[{"bulan":"28 November 2026","marketing":"","sekolah":"SDN-A","path":"/x/SDN-A"}]}"#,
        )
        .unwrap();
        assert_eq!(old.rows[0].counts, FileCounts::default());
    }

    #[test]
    fn scan_reguler_school_with_only_pilihan_child() {
        // Sekolah yang isinya HANYA folder PILIHAN (tanpa NOTE.txt /
        // file langsung) tetap dibaca sebagai sekolah "SDN-B", bukan
        // sebagai kategori yang turun satu level jadi "PILIHAN"
        // (temuan verifikasi GUI 28 Nov 2026).
        let d = tmpdir("filecount-pilihan-only");
        let pilihan = d
            .join("28 November 2026")
            .join("SDN-B")
            .join("PILIHAN");
        fs::create_dir_all(&pilihan).unwrap();
        fs::write(pilihan.join("b1.jpg"), b"x").unwrap();
        let rows = scan_reguler(&d);
        assert_eq!(rows.len(), 1);
        assert_eq!(rows[0].sekolah, "SDN-B");
        assert_eq!(rows[0].counts.jpg, 1);
    }

    #[test]
    fn file_count_ext_validation_and_labels() {
        assert_eq!(valid_file_count_ext("jpg"), "jpg");
        assert_eq!(valid_file_count_ext("png"), "png");
        assert_eq!(valid_file_count_ext("psd"), "psd");
        assert_eq!(valid_file_count_ext("all"), "all");
        assert_eq!(valid_file_count_ext(""), "jpg");
        assert_eq!(valid_file_count_ext("exe"), "jpg");
        assert_eq!(file_count_ext_label("jpg"), "jpg");
        assert_eq!(file_count_ext_label("all"), "file");
    }

    #[test]
    fn src_rows_fill_file_counts() {
        let d = tmpdir("filecount-rows");
        let pilihan = d.join("SDN-A").join("PILIHAN");
        fs::create_dir_all(pilihan.join("1. PAS FOTO")).unwrap();
        fs::write(pilihan.join("a.jpg"), b"x").unwrap();
        fs::write(pilihan.join("1. PAS FOTO").join("b.jpg"), b"x").unwrap();
        fs::write(pilihan.join("1. PAS FOTO").join("c.png"), b"x").unwrap();
        let mut st = BatchState::default();
        st.local_output = d.join("out").to_string_lossy().into_owned();
        st.file_count_ext = "jpg".to_string();
        st.add_folder_named(&pilihan, "SDN-A");
        let rows = st.src_rows();
        // Baris folder "1. PAS FOTO": 1 jpg rekursif; baris file: -1.
        let folder = rows.iter().find(|r| r.name == "1. PAS FOTO").unwrap();
        assert_eq!(folder.file_count, 1);
        // Ganti ekstensi -> angka ikut tanpa scan ulang.
        st.file_count_ext = "png".to_string();
        let rows = st.src_rows();
        let folder = rows.iter().find(|r| r.name == "1. PAS FOTO").unwrap();
        assert_eq!(folder.file_count, 1);
        st.file_count_ext = "all".to_string();
        let rows = st.src_rows();
        let folder = rows.iter().find(|r| r.name == "1. PAS FOTO").unwrap();
        assert_eq!(folder.file_count, 2);
    }
}
