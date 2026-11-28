// Explorer — filesystem nyata (bukan mock).
//
// Menggantikan dataset contoh yang dulu digenerate `tools/gen_explorer_data.py`
// (dikosongkan 27 Nov 2026 karena aturan default-clean). Semua urutan dihitung
// di Rust dan didorong ke Slint sebagai array indeks — Slint tidak bisa
// sort/filter array sendiri (alasan yang sama seperti generator dulu).
//
// Aturan urutan (EXPLORER_SPEC.md §2.5, dipertahankan dari generator):
//   - folder dulu, lalu shortcut (.lnk / symlink), lalu file — di semua sort;
//   - natural sort (angka: "2" sebelum "10"); Descending membalik tiap
//     kelompok terpisah (bukan membalik seluruh daftar);
//   - Group Date: Today -> Yesterday -> Earlier this week -> Last week ->
//     Earlier this month -> Last month -> A long time ago;
//   - Group Type: Folders -> Shortcut -> "XXX Files" per ekstensi (alfabetis);
//   - Split: kiri = grup ekstensi terbanyak (folder = grup "Folder"),
//     kanan = sisanya; bila 1 grup -> belah dua (ceil kiri).
use std::collections::HashMap;
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering as AtomicOrdering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use chrono::{Datelike, Local, TimeZone};

use crate::batch::natural_cmp;

/// Urutan kanonis jenis item di semua sort: folder, shortcut, file.
fn kind_rank(kind: &str) -> u8 {
    match kind {
        "folder" => 0,
        "link" => 1,
        _ => 2,
    }
}

/// Satu entri filesystem untuk Explorer.
#[derive(Clone, Debug)]
pub struct FsItem {
    /// Nama tampil (dengan ekstensi).
    pub name: String,
    /// "folder" | "link" | "file".
    pub kind: String,
    /// Ekstensi lowercase dengan titik (".jpg"), "" untuk folder/link-ke-folder.
    pub ext: String,
    /// Nama tanpa ekstensi (folder: nama penuh).
    pub base: String,
    /// Ukuran byte (0 untuk folder).
    pub size: u64,
    /// Tampil ukuran ("" untuk folder — JANGAN meniru bug C# "0 items", §8.2).
    pub size_display: String,
    /// Waktu modifikasi.
    pub mtime: SystemTime,
    /// Tampil tanggal ("26 Nov 2026").
    pub date_display: String,
    /// Hari sejak tanggal file (0 = hari ini); dipakai sort Date + bucket.
    pub days_ago: i64,
    /// Bucket tanggal (Group Date).
    pub bucket: String,
    /// Path penuh.
    pub path: PathBuf,
    /// true untuk folder dan shortcut-ke-folder (bisa dinavigasi).
    pub is_dir: bool,
}

/// Format ukuran ala C# `BytesToString`: "0 B", "1,5 KB", "12,4 MB", ...
pub fn format_size(bytes: u64) -> String {
    const UNITS: [&str; 6] = ["B", "KB", "MB", "GB", "TB", "PB"];
    if bytes == 0 {
        return "0 B".to_string();
    }
    let mut v = bytes as f64;
    let mut u = 0usize;
    while v >= 1024.0 && u + 1 < UNITS.len() {
        v /= 1024.0;
        u += 1;
    }
    if u == 0 {
        format!("{} B", bytes)
    } else {
        // Koma desimal ala Indonesia; buang ",0" (mis. "2 KB" bukan "2,0 KB").
        let s = format!("{:.1}", v).replace('.', ",");
        let s = s.strip_suffix(",0").unwrap_or(&s);
        format!("{} {}", s, UNITS[u])
    }
}

/// Format tanggal "26 Nov 2026" (bulan Inggris, seperti mock lama).
pub fn format_date(mtime: SystemTime) -> String {
    let secs = mtime
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs() as i64)
        .unwrap_or(0);
    match Local.timestamp_opt(secs, 0).single() {
        Some(dt) => dt.format("%d %b %Y").to_string(),
        None => String::new(),
    }
}

/// Bucket tanggal untuk Group Date (label Inggris, sama seperti mock lama).
pub fn date_bucket(mtime: SystemTime) -> (String, i64) {
    let today = Local::now().date_naive();
    let secs = mtime
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs() as i64)
        .unwrap_or(0);
    let fdate = match Local.timestamp_opt(secs, 0).single() {
        Some(dt) => dt.date_naive(),
        None => today,
    };
    let days = (today - fdate).num_days();
    if days <= 0 {
        return ("Today".to_string(), days.max(0));
    }
    // Awal minggu = Minggu (C# DayOfWeek.Sunday).
    let dow = today.weekday().num_days_from_sunday() as i64;
    let week_start = today - chrono::Duration::days(dow);
    let bucket = if fdate >= week_start {
        "Earlier this week"
    } else if fdate >= week_start - chrono::Duration::days(7) {
        "Last week"
    } else if fdate.year() == today.year() && fdate.month() == today.month() {
        "Earlier this month"
    } else {
        let prev_month = if today.month() == 1 {
            (today.year() - 1, 12)
        } else {
            (today.year(), today.month() - 1)
        };
        if fdate.year() == prev_month.0 && fdate.month() == prev_month.1 {
            "Last month"
        } else {
            "A long time ago"
        }
    };
    (bucket.to_string(), days)
}

/// Baca isi satu folder dari disk, urutan kanonis (name-asc).
/// File tersembunyi (nama diawali '.') disembunyikan kecuali show_hidden.
/// Atribut hidden Windows tidak bisa dibaca via std::fs — dicatat sebagai
/// keterbatasan (lihat PERUBAHAN.md).
pub fn read_folder(path: &Path, show_hidden: bool) -> Vec<FsItem> {
    let mut out: Vec<FsItem> = Vec::new();
    let Ok(rd) = fs::read_dir(path) else {
        return out;
    };
    for entry in rd.flatten() {
        let name = entry.file_name().to_string_lossy().into_owned();
        if name.is_empty() {
            continue;
        }
        if !show_hidden && name.starts_with('.') {
            continue;
        }
        let p = entry.path();
        let ft = match entry.file_type() {
            Ok(t) => t,
            Err(_) => continue,
        };
        let md = entry.metadata().ok();
        let mtime = md
            .as_ref()
            .and_then(|m| m.modified().ok())
            .unwrap_or(UNIX_EPOCH);
        let size = md.as_ref().map(|m| m.len()).unwrap_or(0);
        let (kind, is_dir, ext, base, size, size_display) = if ft.is_symlink() {
            // Symlink: target folder -> navigasi, target file -> buka file.
            let target_dir = fs::metadata(&p).map(|m| m.is_dir()).unwrap_or(false);
            (
                "link".to_string(),
                target_dir,
                if target_dir {
                    String::new()
                } else {
                    ext_of(&name)
                },
                if target_dir {
                    name.clone()
                } else {
                    base_of(&name)
                },
                0,
                String::new(),
            )
        } else if ft.is_dir() {
            (
                "folder".to_string(),
                true,
                String::new(),
                name.clone(),
                0,
                String::new(),
            )
        } else {
            let ext = ext_of(&name);
            // .lnk Windows = shortcut (C# Shortcut) meski di Linux ini file biasa.
            if ext == ".lnk" {
                (
                    "link".to_string(),
                    false,
                    ext,
                    base_of(&name),
                    0,
                    String::new(),
                )
            } else {
                (
                    "file".to_string(),
                    false,
                    ext.clone(),
                    base_of(&name),
                    size,
                    format_size(size),
                )
            }
        };
        let (bucket, days_ago) = date_bucket(mtime);
        out.push(FsItem {
            name,
            kind,
            ext,
            base,
            size,
            size_display,
            mtime,
            date_display: format_date(mtime),
            days_ago,
            bucket,
            path: p,
            is_dir,
        });
    }
    out.sort_by(|a, b| {
        kind_rank(&a.kind)
            .cmp(&kind_rank(&b.kind))
            .then_with(|| natural_cmp(&a.name, &b.name))
    });
    out
}

/// Ekstensi lowercase dengan titik (".jpg"), "" bila tidak ada.
pub fn ext_of(name: &str) -> String {
    let p = Path::new(name);
    match p.extension() {
        Some(e) => format!(".{}", e.to_string_lossy().to_lowercase()),
        None => String::new(),
    }
}

/// Nama tanpa ekstensi terakhir.
pub fn base_of(name: &str) -> String {
    match name.rfind('.') {
        Some(i) if i > 0 => name[..i].to_string(),
        _ => name.to_string(),
    }
}

/// Semua urutan tampilan untuk satu folder (indeks ke items urutan kanonis).
#[derive(Default, Debug)]
pub struct DisplayOrders {
    pub name_asc: Vec<i32>,
    pub name_desc: Vec<i32>,
    pub date_asc: Vec<i32>,
    pub date_desc: Vec<i32>,
    pub type_asc: Vec<i32>,
    pub type_desc: Vec<i32>,
    /// Group Date: urutan + header ("Today" dst, "" bila bukan awal bucket).
    pub grp_date: Vec<i32>,
    pub grp_date_hdr: Vec<String>,
    /// Group Type: urutan + header ("Folders"/"Shortcut"/"JPG Files").
    pub grp_type: Vec<i32>,
    pub grp_type_hdr: Vec<String>,
    /// Split: kiri + kanan.
    pub split_l: Vec<i32>,
    pub split_r: Vec<i32>,
}

/// Balik tiap kelompok jenis terpisah (bukan balik seluruh daftar) —
///
/// C# `rev_within` di generator lama.
fn rev_within(order: &[i32], items: &[FsItem]) -> Vec<i32> {
    let mut out: Vec<i32> = Vec::with_capacity(order.len());
    for rank in 0..=2u8 {
        let grp: Vec<i32> = order
            .iter()
            .copied()
            .filter(|&i| kind_rank(&items[i as usize].kind) == rank)
            .collect();
        out.extend(grp.into_iter().rev());
    }
    out
}

const DATE_BUCKETS: [&str; 7] = [
    "Today",
    "Yesterday",
    "Earlier this week",
    "Last week",
    "Earlier this month",
    "Last month",
    "A long time ago",
];

/// Hitung semua urutan dari items (urutan kanonis = name-asc).
pub fn compute_orders(items: &[FsItem]) -> DisplayOrders {
    let n = items.len();
    let canon: Vec<i32> = (0..n as i32).collect();
    let mut o = DisplayOrders::default();

    o.name_asc = canon.clone();
    o.name_desc = rev_within(&o.name_asc, items);

    // Date-asc: dalam tiap kelompok jenis, tanggal naik (lama dulu),
    // seri -> nama natural. Date-desc = balik tiap kelompok.
    let mut date_asc: Vec<i32> = Vec::with_capacity(n);
    for rank in 0..=2u8 {
        let mut grp: Vec<i32> = canon
            .iter()
            .copied()
            .filter(|&i| kind_rank(&items[i as usize].kind) == rank)
            .collect();
        grp.sort_by(|&a, &b| {
            let (ia, ib) = (items[a as usize].clone(), items[b as usize].clone());
            // days_ago kecil = baru; asc = lama dulu -> days_ago turun.
            ib.days_ago
                .cmp(&ia.days_ago)
                .then_with(|| natural_cmp(&ia.name, &ib.name))
        });
        date_asc.extend(grp);
    }
    o.date_desc = rev_within(&date_asc, items);
    o.date_asc = date_asc;

    // Type-asc: dalam tiap kelompok jenis, ekstensi lalu nama natural.
    let mut type_asc: Vec<i32> = Vec::with_capacity(n);
    for rank in 0..=2u8 {
        let mut grp: Vec<i32> = canon
            .iter()
            .copied()
            .filter(|&i| kind_rank(&items[i as usize].kind) == rank)
            .collect();
        grp.sort_by(|&a, &b| {
            let (ia, ib) = (&items[a as usize], &items[b as usize]);
            ia.ext
                .cmp(&ib.ext)
                .then_with(|| natural_cmp(&ia.name, &ib.name))
        });
        type_asc.extend(grp);
    }
    o.type_desc = rev_within(&type_asc, items);
    o.type_asc = type_asc;

    // Group Date: bucket kanonis, di dalam bucket (jenis, nama natural).
    for b in DATE_BUCKETS {
        let mut grp: Vec<i32> = canon
            .iter()
            .copied()
            .filter(|&i| items[i as usize].bucket == b)
            .collect();
        grp.sort_by(|&a, &b| {
            let (ia, ib) = (&items[a as usize], &items[b as usize]);
            kind_rank(&ia.kind)
                .cmp(&kind_rank(&ib.kind))
                .then_with(|| natural_cmp(&ia.name, &ib.name))
        });
        for (j, i) in grp.iter().enumerate() {
            o.grp_date.push(*i);
            o.grp_date_hdr
                .push(if j == 0 { b.to_string() } else { String::new() });
        }
    }

    // Group Type: Folders -> Shortcut -> "XXX Files" alfabetis.
    let mut groups: Vec<(String, Vec<i32>)> = Vec::new();
    let folders: Vec<i32> = canon
        .iter()
        .copied()
        .filter(|&i| items[i as usize].kind == "folder")
        .collect();
    if !folders.is_empty() {
        groups.push(("Folders".to_string(), folders));
    }
    let links: Vec<i32> = canon
        .iter()
        .copied()
        .filter(|&i| items[i as usize].kind == "link")
        .collect();
    if !links.is_empty() {
        groups.push(("Shortcut".to_string(), links));
    }
    let mut exts: Vec<String> = items
        .iter()
        .filter(|it| it.kind == "file")
        .map(|it| it.ext.to_uppercase())
        .filter(|e| !e.is_empty())
        .collect();
    exts.sort();
    exts.dedup();
    for e in exts {
        let grp: Vec<i32> = canon
            .iter()
            .copied()
            .filter(|&i| {
                let it = &items[i as usize];
                it.kind == "file" && it.ext.to_uppercase() == e
            })
            .collect();
        if !grp.is_empty() {
            let label = format!("{} Files", e.trim_start_matches('.'));
            groups.push((label, grp));
        }
    }
    for (label, grp) in groups {
        for (j, i) in grp.iter().enumerate() {
            o.grp_type.push(*i);
            o.grp_type_hdr
                .push(if j == 0 { label.clone() } else { String::new() });
        }
    }

    // Split: kiri = grup ekstensi terbanyak (folder = "Folder"), kanan =
    // sisanya; seri -> grup yang item pertamanya paling dulu di kanonis.
    // Bila 1 grup -> belah dua (ceil kiri).
    let mut split_groups: HashMap<String, Vec<i32>> = HashMap::new();
    for &i in &canon {
        let g = if items[i as usize].kind == "folder" {
            "Folder".to_string()
        } else {
            items[i as usize].ext.to_lowercase()
        };
        split_groups.entry(g).or_default().push(i);
    }
    if split_groups.len() >= 2 {
        let mut best: Option<&Vec<i32>> = None;
        let mut best_key = (0usize, usize::MAX);
        for g in split_groups.values() {
            let first_pos = canon.iter().position(|&x| x == g[0]).unwrap_or(usize::MAX);
            let key = (g.len(), usize::MAX - first_pos);
            if best.is_none() || key > best_key {
                best_key = key;
                best = Some(g);
            }
        }
        let left = best.cloned().unwrap_or_default();
        let left_set: std::collections::HashSet<i32> = left.iter().copied().collect();
        let right: Vec<i32> = canon
            .iter()
            .copied()
            .filter(|i| !left_set.contains(i))
            .collect();
        o.split_l = left;
        o.split_r = right;
    } else {
        let half = (n + 1) / 2;
        o.split_l = canon[..half].to_vec();
        o.split_r = canon[half..].to_vec();
    }

    o
}

/// Satu hasil search (rekursif).
#[derive(Clone, Debug)]
pub struct SearchHit {
    pub name: String,
    pub kind: String,
    pub ext: String,
    pub base: String,
    pub size_display: String,
    pub date_display: String,
    /// Nama folder induk (kolom Location, seperti C#).
    pub loc: String,
    pub path: PathBuf,
    pub is_dir: bool,
}

/// Search rekursif dari root (C#: cap 5000, `*`/`?` literal, nama saja).
/// Mengembalikan (hasil, truncated). Urutan: folder -> link -> file,
/// nama-asc di dalam grup.
pub fn search_recursive(root: &Path, query: &str, show_hidden: bool) -> (Vec<SearchHit>, bool) {
    const CAP: usize = 5000;
    let q = query.to_lowercase();
    let mut hits: Vec<SearchHit> = Vec::new();
    let mut truncated = false;
    let mut stack: Vec<PathBuf> = vec![root.to_path_buf()];
    while let Some(dir) = stack.pop() {
        let Ok(rd) = fs::read_dir(&dir) else {
            continue;
        };
        let mut entries: Vec<_> = rd.flatten().collect();
        entries.sort_by(|a, b| {
            natural_cmp(
                &a.file_name().to_string_lossy(),
                &b.file_name().to_string_lossy(),
            )
        });
        for entry in entries {
            let name = entry.file_name().to_string_lossy().into_owned();
            if !show_hidden && name.starts_with('.') {
                continue;
            }
            let p = entry.path();
            let ft = match entry.file_type() {
                Ok(t) => t,
                Err(_) => continue,
            };
            if ft.is_dir() || (ft.is_symlink() && fs::metadata(&p).map(|m| m.is_dir()).unwrap_or(false)) {
                stack.push(p.clone());
            }
            if name.to_lowercase().contains(&q) {
                if hits.len() >= CAP {
                    truncated = true;
                    break;
                }
                let (kind, is_dir, ext, base, size_display, date_display) = if ft.is_symlink() {
                    let target_dir = fs::metadata(&p).map(|m| m.is_dir()).unwrap_or(false);
                    (
                        "link".to_string(),
                        target_dir,
                        if target_dir {
                            String::new()
                        } else {
                            ext_of(&name)
                        },
                        if target_dir {
                            name.clone()
                        } else {
                            base_of(&name)
                        },
                        String::new(),
                        String::new(),
                    )
                } else if ft.is_dir() {
                    ("folder".to_string(), true, String::new(), name.clone(), String::new(), String::new())
                } else {
                    let md = entry.metadata().ok();
                    let (size, mtime) = md
                        .as_ref()
                        .map(|m| (m.len(), m.modified().unwrap_or(UNIX_EPOCH)))
                        .unwrap_or((0, UNIX_EPOCH));
                    (
                        "file".to_string(),
                        false,
                        ext_of(&name),
                        base_of(&name),
                        format_size(size),
                        format_date(mtime),
                    )
                };
                let loc = dir
                    .file_name()
                    .map(|s| s.to_string_lossy().into_owned())
                    .unwrap_or_default();
                hits.push(SearchHit {
                    name,
                    kind,
                    ext,
                    base,
                    size_display,
                    date_display,
                    loc,
                    path: p,
                    is_dir,
                });
            }
        }
        if truncated {
            break;
        }
    }
    hits.sort_by(|a, b| {
        kind_rank(&a.kind)
            .cmp(&kind_rank(&b.kind))
            .then_with(|| natural_cmp(&a.name, &b.name))
    });
    (hits, truncated)
}

/// Nama salinan ala C# `FileNameDeduplicator.BuildCopyPath`: nama asli dulu
/// bila bebas di folder tujuan; baru bila tabrakan, "x - Copy",
/// "x - Copy (2)", ... (28 Nov 2026: port sebelumnya selalu menambah
/// " - Copy" walau nama bebas — paste ke folder lain jadi
/// "FolderB - Copy - Copy", menyimpang dari C#).
pub fn dedup_name(dir: &Path, base: &str, ext: &str) -> String {
    let orig = format!("{}{}", base, ext);
    if !dir.join(&orig).exists() {
        return orig;
    }
    let mut cand = format!("{} - Copy{}", base, ext);
    if !dir.join(&cand).exists() {
        return cand;
    }
    let mut n = 2;
    loop {
        cand = format!("{} - Copy ({}){}", base, n, ext);
        if !dir.join(&cand).exists() {
            return cand;
        }
        n += 1;
    }
}

/// Ukuran total (byte) sebuah path: file = ukurannya, folder = jumlah
/// rekursif. Dipakai total progress copy/move.
pub fn dir_size(path: &Path) -> u64 {
    let Ok(md) = fs::metadata(path) else {
        return 0;
    };
    if !md.is_dir() {
        return md.len();
    }
    let mut total = 0u64;
    let mut stack = vec![path.to_path_buf()];
    while let Some(d) = stack.pop() {
        if let Ok(rd) = fs::read_dir(&d) {
            for e in rd.flatten() {
                let p = e.path();
                match e.file_type() {
                    Ok(t) if t.is_dir() => stack.push(p),
                    Ok(_) => {
                        total += e.metadata().map(|m| m.len()).unwrap_or(0);
                    }
                    Err(_) => {}
                }
            }
        }
    }
    total
}

/// Progress satu tugas file. `done` adalah nilai yang dibaca UI —
/// diterbitkan maks ~8x/detik (125ms, ala C# UpdateCopyProgress) supaya
/// update progress-nya sendiri tidak bikin lag (logika TeraCopy);
/// hitungan byte akurat disimpan di `inner_done` dan selalu ditambah
/// tiap buffer 64KB, jadi total akhir tetap tepat.
pub struct Progress {
    pub done: u64,
    pub total: u64,
    inner_done: u64,
    last_push: Instant,
}

impl Progress {
    pub fn new(total: u64) -> Self {
        Progress { done: 0, total, inner_done: 0, last_push: Instant::now() }
    }

    /// Tambah byte tersalin; terbitkan ke `done` bila sudah ≥125ms sejak
    /// terbit terakhir, atau bila sudah selesai.
    pub fn add(&mut self, n: u64) {
        self.inner_done += n;
        if self.last_push.elapsed() >= Duration::from_millis(125)
            || self.inner_done >= self.total
        {
            self.done = self.inner_done;
            self.last_push = Instant::now();
        }
    }

    /// Paksa terbit 100% saat tugas selesai (pengaman bila add terakhir
    /// tertahan throttle).
    pub fn finish(&mut self) {
        self.done = self.total;
    }
}

/// Satu tugas file di Task Monitor (copy/move/delete).
pub struct FileTask {
    /// "copy" | "move" | "delete".
    pub kind: String,
    /// Nama tampil ("Salin 3 item" / nama file).
    pub name: String,
    /// "run" | "merging" | "cancelling" | "done" | "failed" | "cancelled"
    /// (dibungkus Arc agar thread pekerja bisa mengubahnya).
    pub status: Arc<Mutex<String>>,
    pub error: Arc<Mutex<String>>,
    pub progress: Arc<Mutex<Progress>>,
    pub cancel: Arc<AtomicBool>,
    /// Jeda TeraCopy (baru, tidak ada di C# — disetujui abeng 28 Nov 2026
    /// 08:05): pekerja berhenti sementara antar chunk 64KB sampai flag
    /// ini dicabut; file tidak corrupt karena tulis berhenti di batas chunk.
    pub paused: Arc<AtomicBool>,
    /// Status sebelum dijeda ("run"/"merging") — dipulihkan saat lanjut.
    pub paused_from: Arc<Mutex<String>>,
    /// Kapan pekerja mulai jalan (di-set setelah antrean serial memberi
    /// giliran) — dasar kecepatan + ETA nyata (C# UpdateCopyProgress).
    pub started: Arc<Mutex<Option<Instant>>>,
    /// Sudah diberitahukan ke user (toast + reload) oleh task-poll.
    pub notified: bool,
    /// Info retry: sumber + tujuan + apakah cut.
    pub srcs: Vec<PathBuf>,
    pub dst: PathBuf,
    pub is_cut: bool,
    pub total: u64,
}

impl FileTask {
    pub fn new(kind: &str, name: String, srcs: Vec<PathBuf>, dst: PathBuf, is_cut: bool, total: u64) -> Self {
        FileTask {
            kind: kind.to_string(),
            name,
            status: Arc::new(Mutex::new("run".to_string())),
            error: Arc::new(Mutex::new(String::new())),
            progress: Arc::new(Mutex::new(Progress::new(total))),
            cancel: Arc::new(AtomicBool::new(false)),
            paused: Arc::new(AtomicBool::new(false)),
            paused_from: Arc::new(Mutex::new(String::new())),
            started: Arc::new(Mutex::new(None)),
            notified: false,
            srcs,
            dst,
            is_cut,
            total,
        }
    }

    pub fn status(&self) -> String {
        self.status.lock().map(|s| s.clone()).unwrap_or_default()
    }

    pub fn error(&self) -> String {
        self.error.lock().map(|s| s.clone()).unwrap_or_default()
    }

    /// Kecepatan nyata MB/s ala C# (byte tersalin / detik sejak mulai).
    /// Sebelum 1 detik berlalu (atau belum mulai) = "Menghitung…".
    /// Saat dijeda = "Dijeda".
    pub fn speed_str(&self) -> String {
        if self.paused.load(AtomicOrdering::Relaxed) {
            return "Dijeda".to_string();
        }
        let (done, _) = self.progress.lock().map(|p| (p.done, p.total)).unwrap_or((0, 0));
        let start = self.started.lock().ok().and_then(|s| *s);
        let Some(start) = start else {
            return "Menghitung…".to_string();
        };
        let el = start.elapsed().as_secs_f64();
        if el < 1.0 || done == 0 {
            return "Menghitung…".to_string();
        }
        let bps = done as f64 / el;
        format!("{}/s", format_size(bps as u64))
    }

    /// ETA nyata hh:mm:ss ala C# (sisa byte / kecepatan). Sebelum
    /// kecepatan bisa dihitung = "Menghitung…". Saat dijeda = "--:--:--".
    pub fn eta_str(&self) -> String {
        if self.paused.load(AtomicOrdering::Relaxed) {
            return "--:--:--".to_string();
        }
        let (done, total) = self.progress.lock().map(|p| (p.done, p.total)).unwrap_or((0, 0));
        if total == 0 || done >= total {
            return "00:00:00".to_string();
        }
        let start = self.started.lock().ok().and_then(|s| *s);
        let Some(start) = start else {
            return "Menghitung…".to_string();
        };
        let el = start.elapsed().as_secs_f64();
        if el < 1.0 || done == 0 {
            return "Menghitung…".to_string();
        }
        let bps = done as f64 / el;
        if bps <= 0.0 {
            return "Menghitung…".to_string();
        }
        let secs = ((total - done) as f64 / bps).round() as u64;
        format!("{:02}:{:02}:{:02}", secs / 3600, (secs % 3600) / 60, secs % 60)
    }

    /// Baris "dari … → ke …" ala C# (SourceFolderDisplay /
    /// DestinationDisplay, dipangkas 50 karakter dari belakang).
    /// Delete tidak punya tujuan — hanya "dari …".
    pub fn route_str(&self) -> String {
        fn trim50(s: &str) -> String {
            let chars: Vec<char> = s.chars().collect();
            if chars.len() > 50 {
                format!("...{}", chars[chars.len() - 47..].iter().collect::<String>())
            } else {
                s.to_string()
            }
        }
        let src = self.srcs.first();
        match self.kind.as_str() {
            "delete" => match src {
                Some(p) => format!("dari {}", trim50(&p.to_string_lossy())),
                None => String::new(),
            },
            _ => {
                let from = match src {
                    Some(p) => p
                        .parent()
                        .map(|d| d.to_string_lossy().into_owned())
                        .unwrap_or_else(|| p.to_string_lossy().into_owned()),
                    None => String::new(),
                };
                format!(
                    "dari {} → ke {}",
                    trim50(&from),
                    trim50(&self.dst.to_string_lossy())
                )
            }
        }
    }

    pub fn prog_pct(&self) -> i32 {
        let p = self.progress.lock().map(|p| (p.done, p.total)).unwrap_or((0, 0));
        if p.1 == 0 {
            return if self.status() == "done" { 100 } else { 0 };
        }
        ((p.0 as f64 / p.1 as f64) * 100.0).round() as i32
    }

    pub fn size_str(&self) -> String {
        let p = self.progress.lock().map(|p| (p.done, p.total)).unwrap_or((0, 0));
        format!("{} / {}", format_size(p.0), format_size(p.1))
    }
}


/// Tunggu selama tugas dijeda (TeraCopy pause): cek flag tiap 50ms;
/// batal selalu menang supaya tugas yang dijeda tetap bisa dibatalkan.
/// Dipanggil antar chunk 64KB — tulis berhenti di batas chunk, jadi
/// file tidak corrupt dan lanjut tinggal terus dari posisi terakhir.
fn wait_if_paused(paused: &AtomicBool, cancel: &AtomicBool) -> Result<(), String> {
    while paused.load(AtomicOrdering::Relaxed) {
        if cancel.load(AtomicOrdering::Relaxed) {
            return Err("cancelled".to_string());
        }
        std::thread::sleep(Duration::from_millis(50));
    }
    Ok(())
}

/// Salin satu file dengan progress + cancel. Mengembalikan Ok(()) atau pesan error.
fn copy_file_progress(
    src: &Path,
    dst: &Path,
    cancel: &AtomicBool,
    paused: &AtomicBool,
    progress: &Arc<Mutex<Progress>>,
) -> Result<(), String> {
    use std::io::{Read, Write};
    let mut fin = fs::File::open(src).map_err(|e| e.to_string())?;
    let mut fout = fs::File::create(dst).map_err(|e| e.to_string())?;
    let mut buf = [0u8; 65536];
    loop {
        wait_if_paused(paused, cancel)?;
        if cancel.load(AtomicOrdering::Relaxed) {
            return Err("cancelled".to_string());
        }
        let n = fin.read(&mut buf).map_err(|e| e.to_string())?;
        if n == 0 {
            break;
        }
        fout.write_all(&buf[..n]).map_err(|e| e.to_string())?;
        if let Ok(mut p) = progress.lock() {
            p.add(n as u64);
        }
    }
    Ok(())
}

/// Salin rekursif (file/folder) dengan progress + cancel.
pub fn copy_recursive(
    src: &Path,
    dst: &Path,
    cancel: &AtomicBool,
    paused: &AtomicBool,
    progress: &Arc<Mutex<Progress>>,
) -> Result<(), String> {
    wait_if_paused(paused, cancel)?;
    if cancel.load(AtomicOrdering::Relaxed) {
        return Err("cancelled".to_string());
    }
    let md = fs::symlink_metadata(src).map_err(|e| e.to_string())?;
    if md.file_type().is_symlink() {
        // Salin symlink apa adanya (target tetap).
        let target = fs::read_link(src).map_err(|e| e.to_string())?;
        #[cfg(unix)]
        {
            std::os::unix::fs::symlink(&target, dst).map_err(|e| e.to_string())?;
        }
        #[cfg(not(unix))]
        {
            let _ = target;
            return Err("symlink tidak didukung di platform ini".to_string());
        }
        return Ok(());
    }
    if md.is_dir() {
        fs::create_dir_all(dst).map_err(|e| e.to_string())?;
        let rd = fs::read_dir(src).map_err(|e| e.to_string())?;
        for e in rd.flatten() {
            copy_recursive(&e.path(), &dst.join(e.file_name()), cancel, paused, progress)?;
        }
        return Ok(());
    }
    copy_file_progress(src, dst, cancel, paused, progress)
}

/// Pindah satu path: rename cepat bila satu volume, fallback copy+delete.
pub fn move_one(
    src: &Path,
    dst: &Path,
    cancel: &AtomicBool,
    paused: &AtomicBool,
    progress: &Arc<Mutex<Progress>>,
) -> Result<(), String> {
    wait_if_paused(paused, cancel)?;
    if fs::rename(src, dst).is_ok() {
        if let Ok(mut p) = progress.lock() {
            p.add(dir_size(dst));
        }
        return Ok(());
    }
    copy_recursive(src, dst, cancel, paused, progress)?;
    if cancel.load(AtomicOrdering::Relaxed) {
        return Err("cancelled".to_string());
    }
    delete_recursive(src, cancel, paused, progress)?;
    Ok(())
}

/// Hapus rekursif dengan progress (done = byte file yang terhapus).
pub fn delete_recursive(
    path: &Path,
    cancel: &AtomicBool,
    paused: &AtomicBool,
    progress: &Arc<Mutex<Progress>>,
) -> Result<(), String> {
    wait_if_paused(paused, cancel)?;
    if cancel.load(AtomicOrdering::Relaxed) {
        return Err("cancelled".to_string());
    }
    let md = fs::symlink_metadata(path).map_err(|e| e.to_string())?;
    if md.is_dir() && !md.file_type().is_symlink() {
        let rd = fs::read_dir(path).map_err(|e| e.to_string())?;
        for e in rd.flatten() {
            delete_recursive(&e.path(), cancel, paused, progress)?;
        }
        fs::remove_dir(path).map_err(|e| e.to_string())?;
    } else {
        let sz = md.len();
        fs::remove_file(path).map_err(|e| e.to_string())?;
        if let Ok(mut p) = progress.lock() {
            p.add(sz);
        }
    }
    Ok(())
}

/// Satu entri undo (maks 50, C# `UndoStack`).
#[derive(Clone, Debug)]
pub struct UndoEntry {
    /// "new" | "rename" | "copy" | "move" | "recycle".
    pub kind: String,
    /// Nama tampil ("Buat folder X").
    pub name: String,
    /// Pasangan (dari, ke). Undo: new/copy -> hapus `ke`; rename/move ->
    /// pindah `ke` kembali ke `dari`.
    pub pairs: Vec<(PathBuf, PathBuf)>,
}

/// State Explorer yang hidup di main.rs (dibagi Arc<Mutex<...>>).
#[derive(Default)]
pub struct ExplorerState {
    /// Path root yang di-resolve ("" bila belum disetting).
    pub root_path: PathBuf,
    pub root_missing: bool,
    /// Pesan untuk empty state bila root ada tapi hilang di disk.
    pub root_error: String,
    pub show_hidden: bool,
    /// Quick Access: (nama tampil, path penuh) — maks 8.
    pub qa: Vec<(String, String)>,
    /// Gerbang serial tugas file (satu tugas jalan dalam satu waktu).
    pub task_gate: Arc<Mutex<()>>,
    /// Kapan terakhir operasi KITA mengubah folder (watcher polling
    /// diam 3 detik sesudahnya supaya tidak reload sendiri).
    pub last_own_change: Option<Instant>,
    /// Folder yang sedang tampil + item kanonisnya.
    pub cur_path: PathBuf,
    pub items: Vec<FsItem>,
    /// mtime folder saat terakhir dibaca (polling watcher).
    pub cur_mtime: Option<SystemTime>,
    /// Clipboard: path + mode ("copy"/"cut") + folder sumber.
    pub clipboard: Vec<PathBuf>,
    pub clipboard_mode: String,
    pub clipboard_src: PathBuf,
    pub undo: Vec<UndoEntry>,
    /// Tugas file (maks 3 tampil; antrian serial).
    pub tasks: Vec<FileTask>,
    /// Generasi search (hasil lama dibuang).
    pub search_gen: u64,
    /// Generasi terakhir yang hasilnya sudah didorong ke UI.
    pub search_pushed_gen: u64,
    /// Apakah search sedang berjalan.
    pub searching: bool,
}

impl ExplorerState {
    /// Dorong entri undo (maks 50, yang tertua dibuang).
    pub fn push_undo(&mut self, entry: UndoEntry) {
        self.undo.push(entry);
        if self.undo.len() > 50 {
            self.undo.remove(0);
        }
    }

    /// Cek apakah `child` berada di dalam (atau sama dengan) `parent`.
    pub fn is_inside(parent: &Path, child: &Path) -> bool {
        child.starts_with(parent)
    }
}

/// Normalisasi path kunci per-folder (C#: tanpa trailing slash, lowercase).
/// Kunci DB: `Configs.Explorer.FolderSettings.<base64(path-lowercase)>`.
pub fn folder_settings_key(path: &Path) -> String {
    use base64::Engine;
    let s = path.to_string_lossy().to_lowercase();
    let s = s.trim_end_matches(['/', '\\']).to_string();
    base64::engine::general_purpose::STANDARD.encode(s.as_bytes())
}

/// Resolve root Explorer: `Configs.Explorer.DefaultPath` -> `Paths.LocalOutput`
/// -> `Configs.Master.LocalOutput` -> "" (belum disetting).
pub fn resolve_root(db_get: impl Fn(&str) -> String) -> PathBuf {
    for key in [
        "Configs.Explorer.DefaultPath",
        "Paths.LocalOutput",
        "Configs.Master.LocalOutput",
    ] {
        let v = db_get(key);
        if !v.trim().is_empty() {
            return PathBuf::from(v.trim());
        }
    }
    PathBuf::new()
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;
    use std::time::Duration;

    /// Folder sementara std-only (tanpa crate tempfile): dibuat unik per
    /// test, dihapus saat guard di-drop.
    struct TestDir(PathBuf);
    impl TestDir {
        fn new(tag: &str) -> Self {
            static CTR: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);
            let n = CTR.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
            let p = std::env::temp_dir().join(format!(
                "bmachine-explorer-test-{}-{}-{}",
                tag,
                std::process::id(),
                n
            ));
            fs::create_dir_all(&p).unwrap();
            TestDir(p)
        }
        fn path(&self) -> &Path {
            &self.0
        }
    }
    impl Drop for TestDir {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }

    fn fixture() -> (TestDir, Vec<FsItem>) {
        let d = TestDir::new("fixture");
        let root = d.path();
        fs::create_dir(root.join("Sekolah 2")).unwrap();
        fs::create_dir(root.join("Sekolah 10")).unwrap();
        fs::create_dir(root.join("sekolah 1")).unwrap();
        fs::write(root.join("b.jpg"), vec![0u8; 2048]).unwrap();
        fs::write(root.join("a.txt"), b"halo").unwrap();
        fs::write(root.join("A10.txt"), b"x").unwrap();
        fs::write(root.join("A2.txt"), b"xx").unwrap();
        #[cfg(unix)]
        std::os::unix::fs::symlink(root.join("a.txt"), root.join("link.txt")).unwrap();
        let items = read_folder(root, false);
        (d, items)
    }

    #[test]
    fn natural_sort_and_kind_order() {
        let (_d, items) = fixture();
        let names: Vec<&str> = items.iter().map(|i| i.name.as_str()).collect();
        // Folder dulu (natural), lalu link, lalu file (natural).
        assert_eq!(names[0], "sekolah 1");
        assert_eq!(names[1], "Sekolah 2");
        assert_eq!(names[2], "Sekolah 10");
        assert!(names.contains(&"link.txt"));
        assert_eq!(*names.last().unwrap(), "b.jpg");
        // A2 sebelum A10 (natural).
        let pos2 = names.iter().position(|n| *n == "A2.txt").unwrap();
        let pos10 = names.iter().position(|n| *n == "A10.txt").unwrap();
        assert!(pos2 < pos10);
    }

    #[test]
    fn orders_match_generator_rules() {
        let (_d, items) = fixture();
        let o = compute_orders(&items);
        // name-desc: balik tiap kelompok jenis, folder tetap di depan.
        let desc_names: Vec<&str> = o
            .name_desc
            .iter()
            .map(|&i| items[i as usize].name.as_str())
            .collect();
        assert_eq!(desc_names[0], "Sekolah 10");
        assert_eq!(desc_names[1], "Sekolah 2");
        assert_eq!(desc_names[2], "sekolah 1");
        // type-asc: .jpg sebelum .txt di dalam kelompok file.
        let type_names: Vec<&str> = o
            .type_asc
            .iter()
            .map(|&i| items[i as usize].name.as_str())
            .collect();
        let jpg = type_names.iter().position(|n| *n == "b.jpg").unwrap();
        let txt = type_names.iter().position(|n| *n == "a.txt").unwrap();
        assert!(jpg < txt);
        // Group Type headers: "Folders", "Shortcut", "JPG Files", "TXT Files".
        assert!(o.grp_type_hdr.contains(&"Folders".to_string()));
        assert!(o.grp_type_hdr.contains(&"JPG Files".to_string()));
        assert!(o.grp_type_hdr.contains(&"TXT Files".to_string()));
        // Group Date: semua file baru -> bucket "Today" di depan.
        assert_eq!(o.grp_date_hdr[0], "Today");
        // Split: grup terbanyak (.txt = 4) di kiri.
        assert_eq!(o.split_l.len(), 4);
        assert!(o.split_r.iter().all(|&i| items[i as usize].ext != ".txt"));
    }

    #[test]
    fn format_size_cases() {
        assert_eq!(format_size(0), "0 B");
        assert_eq!(format_size(512), "512 B");
        assert_eq!(format_size(1024), "1 KB");
        assert_eq!(format_size(1536), "1,5 KB");
        assert_eq!(format_size(2048), "2 KB");
        assert_eq!(format_size(5 * 1024 * 1024), "5 MB");
    }

    #[test]
    fn dedup_name_sequence() {
        let d = TestDir::new("t");
        // Aturan C# FileNameDeduplicator: nama asli dulu bila bebas.
        assert_eq!(dedup_name(d.path(), "y", ".txt"), "y.txt");
        assert_eq!(dedup_name(d.path(), "z", ""), "z");
        // Bila nama asli + " - Copy" sudah ada, lanjut ke " - Copy (2)".
        fs::write(d.path().join("x.txt"), b"").unwrap();
        fs::write(d.path().join("x - Copy.txt"), b"").unwrap();
        assert_eq!(dedup_name(d.path(), "x", ".txt"), "x - Copy (2).txt");
        // Bila hanya " - Copy" yang ada (nama asli bebas), nama asli dipakai.
        fs::write(d.path().join("w - Copy.txt"), b"").unwrap();
        assert_eq!(dedup_name(d.path(), "w", ".txt"), "w.txt");
    }

    #[test]
    fn search_recursive_finds_and_caps() {
        let d = TestDir::new("t");
        let root = d.path();
        fs::create_dir(root.join("sub")).unwrap();
        fs::write(root.join("sub").join("foto-sekolah.jpg"), b"").unwrap();
        fs::write(root.join("catatan.txt"), b"").unwrap();
        let (hits, trunc) = search_recursive(root, "sekolah", false);
        assert_eq!(hits.len(), 1);
        assert_eq!(hits[0].loc, "sub");
        assert!(!trunc);
        let (hits2, _) = search_recursive(root, "txt", false);
        assert_eq!(hits2.len(), 1);
        assert_eq!(hits2[0].kind, "file");
    }

    #[test]
    fn copy_and_delete_roundtrip() {
        let d = TestDir::new("t");
        let root = d.path();
        fs::create_dir(root.join("src")).unwrap();
        let mut f = fs::File::create(root.join("src").join("a.txt")).unwrap();
        f.write_all(b"hello").unwrap();
        drop(f);
        let cancel = Arc::new(AtomicBool::new(false));
        let paused = AtomicBool::new(false);
        let prog = Arc::new(Mutex::new(Progress::new(5)));
        copy_recursive(&root.join("src"), &root.join("dst"), &cancel, &paused, &prog).unwrap();
        assert_eq!(fs::read(root.join("dst").join("a.txt")).unwrap(), b"hello");
        delete_recursive(&root.join("dst"), &cancel, &paused, &prog).unwrap();
        assert!(!root.join("dst").exists());
    }

    #[test]
    fn date_bucket_today() {
        let (b, days) = date_bucket(SystemTime::now());
        assert_eq!(b, "Today");
        assert_eq!(days, 0);
        let old = SystemTime::now() - Duration::from_secs(86400 * 400);
        let (b2, _) = date_bucket(old);
        assert_eq!(b2, "A long time ago");
    }

    #[test]
    fn folder_settings_key_is_base64_lowercase() {
        let k = folder_settings_key(Path::new("C:\\Data\\Sekolah\\"));
        // Base64 standar dari "c:\data\sekolah" (tanpa trailing slash).
        use base64::Engine;
        let expect = base64::engine::general_purpose::STANDARD
            .encode("c:\\data\\sekolah".as_bytes());
        assert_eq!(k, expect);
    }

    #[test]
    fn hidden_files_filtered() {
        let d = TestDir::new("t");
        fs::write(d.path().join(".hidden"), b"").unwrap();
        fs::write(d.path().join("visible.txt"), b"").unwrap();
        assert_eq!(read_folder(d.path(), false).len(), 1);
        assert_eq!(read_folder(d.path(), true).len(), 2);
    }
}
