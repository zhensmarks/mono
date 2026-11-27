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
    });
    if node.is_dir && node.expanded {
        for c in &node.children {
            flatten_node(c, depth + 1, selected, out, false);
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
        for c in &roots[0].source_root.children {
            flatten_node(c, 0, selected, &mut out, false);
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

#[derive(Debug, Clone, PartialEq)]
pub struct RegulerRow {
    pub bulan: String,
    pub marketing: String,
    pub sekolah: String,
    pub path: String,
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
    let Ok(rd) = std::fs::read_dir(root) else {
        return rows;
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
    for (bulan, mpath) in months {
        let Ok(sd) = std::fs::read_dir(&mpath) else {
            continue;
        };
        let mut schools: Vec<(String, PathBuf)> = sd
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
        schools.sort_by(|a, b| natural_cmp(&a.0, &b.0));
        for (sekolah, spath) in schools {
            let marketing = marketing_from_note(&spath);
            rows.push(RegulerRow {
                bulan: bulan.clone(),
                marketing,
                sekolah,
                path: spath.to_string_lossy().into_owned(),
            });
        }
    }
    rows
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
    /// Folder Reguler tersimpan (DB `Batch.FolderReguler`, JSON array).
    pub reguler_dirs: Vec<String>,
    pub reguler_active: String,
}

impl BatchState {
    /// AddFolders() C#: MENGGANTI seluruh koleksi (bukan append).
    pub fn add_folder(&mut self, source_path: &Path) {
        let local = PathBuf::from(&self.local_output);
        self.roots = vec![BatchRoot::new(source_path, &local)];
        self.selection.clear();
        self.has_manifest = false;
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
        flatten_source(&self.roots, &sel)
    }

    pub fn out_rows(&self) -> Vec<FlatRow> {
        let sel: HashSet<String> = self.selection.ids.iter().cloned().collect();
        flatten_output(&self.roots, &sel)
    }

    /// Nama folder aktif untuk header (1 root → nama folder sumbernya;
    /// >1 → nama root terpilih; kosong → "").
    pub fn header_source_name(&self) -> String {
        if self.roots.len() == 1 {
            self.roots[0]
                .source_path
                .file_name()
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
        assert!(st.src_rows().is_empty());
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
}
