// Fingerprint + anchor-guard + draft store untuk Spreadsheet —
// port dari C# (SpreadsheetRowFingerprint.cs, SpreadsheetRowAnchorGuard.cs,
// SpreadsheetDraft.cs). Dipakai alur Simpan/Muat Ulang spreadsheet.
//
// Aturan C# yang ditiru persis:
// - Sidik jari = gabungan SELURUH nilai kolom baris (dipisah \u{1f});
//   kolom kunci tunggal tidak cukup karena satu NO FAKTUR bisa menempati
//   beberapa baris (70% baris), jadi pergeseran 1 baris tidak terlihat.
// - Guard: sebelum menulis, baca ulang range sheet lalu bandingkan sidik
//   jari tiap baris yang berubah dengan sidik jari saat dimuat. Baris
//   yang seluruhnya kosong dilewati (tidak bisa jadi jangkar).
// - Pesan guard DINAMIS (Describe): menyebut NO FAKTUR baris yang
//   bergeser + kolom yang berubah (nama + nilai lama vs baru) — bukan
//   nilai hardcode, dan bukan pengenal baris yang sama di kedua sisi.
// - Draft dipersist ke SQLite key "Spreadsheet.Draft" (JSON array, skema
//   C# + `RowKey`). Saat reload, draft hanya DITERAPKAN bila nilai
//   asli sel di sheet masih sama; draft yang tidak bisa diterapkan tetap
//   tersimpan sebagai sisa dan dicoba lagi di reload berikutnya (BUKAN
//   dibuang — koreksi premis DESIGN_SYSTEM.md §19.9 butir 5).
//
// ANTI-MENCLOK (abeng 28 Nov 00:25, mengikat): baris dikenali dari
// kunci tetap (NO FAKTUR, kolom kunci — `key_column`), BUKAN nomor
// baris. Draft + jangkar menyimpan `row_key`; penerapan draft mencari
// baris berdasarkan kunci, jadi sisipan baris di atas tidak bisa
// menggeser edit ke order lain. Draft lama tanpa kunci (RowKey kosong)
// TIDAK PERNAH diterapkan berdasarkan posisi — tetap jadi sisa.

use serde::{Deserialize, Serialize};

/// Kunci DB untuk draft (sama dengan C# SpreadsheetDraftStore.StorageKey).
pub const DRAFT_KEY: &str = "Spreadsheet.Draft";
/// Kunci DB untuk snapshot sheet terakhir yang dimuat ("sheet truth"
/// lokal — sumber baca-ulang guard saat tidak ada koneksi Sheets live).
pub const SNAPSHOT_KEY: &str = "Spreadsheet.SheetSnapshot";

const SEPARATOR: char = '\u{1f}';

/// Sidik jari satu baris dari nilai-nilai selnya.
pub fn fingerprint_from_values(values: &[String], column_count: usize) -> String {
    if column_count == 0 {
        return String::new();
    }
    let mut parts: Vec<&str> = Vec::with_capacity(column_count);
    for i in 0..column_count {
        parts.push(values.get(i).map(|s| s.as_str()).unwrap_or(""));
    }
    parts.join(&SEPARATOR.to_string())
}

/// True bila semua kolom kosong — baris seperti ini tidak bisa dipakai
/// sebagai jangkar.
pub fn fingerprint_is_empty(fingerprint: &str) -> bool {
    fingerprint.is_empty()
        || fingerprint
            .chars()
            .filter(|c| *c != SEPARATOR)
            .all(|c| c.is_whitespace())
}

/// Nilai pertama yang tidak kosong, dipakai untuk pesan yang bisa dibaca
/// manusia.
pub fn fingerprint_display(fingerprint: &str) -> String {
    if fingerprint.is_empty() {
        return String::new();
    }
    for part in fingerprint.split(SEPARATOR) {
        if !part.trim().is_empty() {
            return part.trim().to_string();
        }
    }
    String::new()
}

/// Jangkar identitas satu baris: kunci tetap baris (NO FAKTUR — nilai
/// kolom kunci saat dimuat; kosong bila baris belum punya kunci),
/// nomor baris sheet (1-based, hanya untuk pesan fallback), sidik jari
/// seluruh baris saat dimuat, dan nilai tampilan (untuk pesan).
#[derive(Debug, Clone, PartialEq)]
pub struct RowAnchor {
    pub sheet_row_number: i32,
    pub row_key: String,
    pub fingerprint: String,
    pub display: String,
}

#[derive(Debug, Clone, PartialEq)]
pub struct AnchorMismatch {
    pub sheet_row_number: i32,
    /// Kunci baris (NO FAKTUR) bila diketahui; kosong = jangkar lama
    /// tanpa kunci.
    pub row_key: String,
    pub expected_display: String,
    pub actual_display: String,
    /// Kolom yang berubah: (indeks kolom, nilai saat dimuat, nilai
    /// di sheet sekarang). Kosong bila baris tidak ditemukan lagi.
    pub changed: Vec<(usize, String, String)>,
}

/// Kolom-kolom yang berbeda antara dua baris (per indeks kolom).
fn changed_columns(expected: &[String], actual: &[String], column_count: usize) -> Vec<(usize, String, String)> {
    let mut out = Vec::new();
    for i in 0..column_count {
        let e = expected.get(i).map(|s| s.as_str()).unwrap_or("");
        let a = actual.get(i).map(|s| s.as_str()).unwrap_or("");
        if e != a {
            out.push((i, e.to_string(), a.to_string()));
        }
    }
    out
}

/// Bandingkan setiap jangkar dengan nilai sheet yang baru dibaca.
/// `sheet_values` adalah hasil bacaan mulai `first_sheet_row`
/// (indeks 0 = baris `first_sheet_row`). Baris yang seluruhnya kosong
/// dilewati karena tidak bisa dipakai sebagai jangkar.
pub fn find_mismatches(
    anchors: &[RowAnchor],
    sheet_values: Option<&[Vec<String>]>,
    first_sheet_row: i32,
    column_count: usize,
) -> Vec<AnchorMismatch> {
    let mut mismatches = Vec::new();
    if first_sheet_row < 1 {
        return mismatches;
    }
    for anchor in anchors {
        if fingerprint_is_empty(&anchor.fingerprint) {
            continue;
        }
        let index = anchor.sheet_row_number - first_sheet_row;
        let row: Option<&Vec<String>> =
            if index >= 0 {
                sheet_values.and_then(|v| v.get(index as usize))
            } else {
                None
            };
        let actual_fp = match row {
            Some(r) => fingerprint_from_values(r, column_count),
            None => fingerprint_from_values(&[], column_count),
        };
        if actual_fp == anchor.fingerprint {
            continue;
        }
        let actual_display = match row {
            Some(r) => r
                .iter()
                .find(|c| !c.trim().is_empty())
                .map(|c| c.trim().to_string())
                .unwrap_or_default(),
            None => String::new(),
        };
        // Nilai baris saat dimuat, direkonstruksi dari sidik jari
        // (sidik jari menyimpan seluruh nilai kolom, dipisah \u{1f}).
        let expected_row: Vec<String> = anchor
            .fingerprint
            .split(SEPARATOR)
            .map(|s| s.to_string())
            .collect();
        let changed = match row {
            Some(r) => changed_columns(&expected_row, r, column_count),
            None => Vec::new(),
        };
        mismatches.push(AnchorMismatch {
            sheet_row_number: anchor.sheet_row_number,
            row_key: anchor.row_key.clone(),
            expected_display: anchor.display.clone(),
            actual_display,
            changed,
        });
    }
    mismatches
}

/// Varian kunci-tetap dari `find_mismatches` (ANTI-MENCLOK): baris
/// dicari berdasarkan `row_key` (NO FAKTUR) di kolom `key_col`, bukan
/// nomor baris — sisipan/hapusan baris di atas tidak menggeser
/// perbandingan. Jangkar tanpa kunci dicek berdasarkan posisi seperti
/// biasa. Baris yang kuncinya tidak ditemukan lagi = mismatch
/// (baris dihapus/dipindah di luar). `sheet_values` mulai dari
/// `first_sheet_row` (sama seperti `find_mismatches`).
pub fn find_mismatches_by_key(
    anchors: &[RowAnchor],
    sheet_values: &[Vec<String>],
    key_col: usize,
    column_count: usize,
    first_sheet_row: i32,
) -> Vec<AnchorMismatch> {
    let mut mismatches = Vec::new();
    for anchor in anchors {
        if fingerprint_is_empty(&anchor.fingerprint) {
            continue;
        }
        let expected_row: Vec<String> = anchor
            .fingerprint
            .split(SEPARATOR)
            .map(|s| s.to_string())
            .collect();
        if anchor.row_key.is_empty() {
            // Jangkar lama tanpa kunci: fallback posisi.
            let idx = anchor.sheet_row_number - first_sheet_row;
            let row = if idx >= 0 {
                sheet_values.get(idx as usize)
            } else {
                None
            };
            let actual_fp = row
                .map(|r| fingerprint_from_values(r, column_count))
                .unwrap_or_else(|| fingerprint_from_values(&[], column_count));
            if actual_fp == anchor.fingerprint {
                continue;
            }
            mismatches.push(AnchorMismatch {
                sheet_row_number: anchor.sheet_row_number,
                row_key: String::new(),
                expected_display: anchor.display.clone(),
                actual_display: row
                    .and_then(|r| r.iter().find(|c| !c.trim().is_empty()))
                    .map(|c| c.trim().to_string())
                    .unwrap_or_default(),
                changed: row
                    .map(|r| changed_columns(&expected_row, r, column_count))
                    .unwrap_or_default(),
            });
            continue;
        }
        let found = sheet_values
            .iter()
            .find(|r| r.get(key_col).map(|s| s.as_str()) == Some(anchor.row_key.as_str()));
        let Some(row) = found else {
            mismatches.push(AnchorMismatch {
                sheet_row_number: anchor.sheet_row_number,
                row_key: anchor.row_key.clone(),
                expected_display: anchor.display.clone(),
                actual_display: String::new(),
                changed: Vec::new(),
            });
            continue;
        };
        if fingerprint_from_values(row, column_count) == anchor.fingerprint {
            continue;
        }
        mismatches.push(AnchorMismatch {
            sheet_row_number: anchor.sheet_row_number,
            row_key: anchor.row_key.clone(),
            expected_display: anchor.display.clone(),
            actual_display: row
                .iter()
                .find(|c| !c.trim().is_empty())
                .map(|c| c.trim().to_string())
                .unwrap_or_default(),
            changed: changed_columns(&expected_row, row, column_count),
        });
    }
    mismatches
}

/// Pesan siap tampil untuk pengguna, menyebut baris pertama yang bergeser.
/// Sama dengan C# SpreadsheetRowAnchorGuard.Describe() — tapi menyebut
/// NO FAKTUR + kolom yang berubah (nama kolom dari `headers`, nilai lama
/// vs baru), bukan pengenal baris yang sama di kedua sisi.
pub fn describe_mismatches(mismatches: &[AnchorMismatch], headers: &[String]) -> String {
    let first = &mismatches[0];
    let more = if mismatches.len() > 1 {
        format!(" (dan {} baris lain)", mismatches.len() - 1)
    } else {
        String::new()
    };
    let who = if !first.row_key.is_empty() {
        format!("NO FAKTUR {}", first.row_key)
    } else {
        format!("baris {}", first.sheet_row_number)
    };
    let what = if first.changed.is_empty() {
        // Baris hilang dari sheet (kunci tidak ditemukan lagi).
        if first.actual_display.is_empty() {
            format!("{} tidak ditemukan lagi di sheet", who)
        } else {
            format!(
                "{} di sheet berisi '{}', sedangkan yang dimuat '{}'",
                who, first.actual_display, first.expected_display
            )
        }
    } else {
        // Sebut kolom yang berubah: nama + nilai lama vs baru.
        let mut parts: Vec<String> = Vec::new();
        for (col, expected, actual) in first.changed.iter().take(2) {
            let name = headers
                .get(*col)
                .map(|h| h.as_str())
                .filter(|h| !h.is_empty())
                .unwrap_or("kolom");
            parts.push(format!(
                "kolom {} berisi '{}', sedangkan yang dimuat '{}'",
                name, actual, expected
            ));
        }
        if first.changed.len() > 2 {
            parts.push(format!("dan {} kolom lain", first.changed.len() - 2));
        }
        format!("{} sudah berubah di sheet: {}", who, parts.join("; "))
    };
    format!(
        "Data di sheet sudah berubah sejak terakhir dimuat{}: {}. Simpan dibatalkan agar data tidak salah baris \u{2014} klik Muat Ulang; edit Anda tetap tersimpan sebagai draft dan dipulihkan setelah muat ulang.",
        more, what
    )
}

/// Satu perubahan yang belum dikirim ke sheet (skema JSON C# +
/// `RowKey` — kunci tetap baris, ANTI-MENCLOK). Draft lama tanpa
/// `RowKey` tetap bisa dibaca (`#[serde(default)]` -> kunci kosong)
/// tapi TIDAK PERNAH diterapkan berdasarkan posisi.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DraftEntry {
    #[serde(rename = "SheetId")]
    pub sheet_id: String,
    #[serde(rename = "SheetName")]
    pub sheet_name: String,
    #[serde(rename = "SheetRowNumber")]
    pub sheet_row_number: i32,
    #[serde(rename = "RowKey", default)]
    pub row_key: String,
    #[serde(rename = "ColumnIndex")]
    pub column_index: i32,
    #[serde(rename = "OriginalValue")]
    pub original_value: String,
    #[serde(rename = "Value")]
    pub value: String,
}

pub fn serialize_drafts(entries: &[DraftEntry]) -> String {
    if entries.is_empty() {
        return String::new();
    }
    serde_json::to_string(entries).unwrap_or_default()
}

/// Data rusak / format lama: jangan sampai menghalangi load spreadsheet.
pub fn deserialize_drafts(json: &str) -> Vec<DraftEntry> {
    if json.trim().is_empty() {
        return Vec::new();
    }
    serde_json::from_str(json).unwrap_or_default()
}

/// Terapkan draft ke baris yang baru dimuat (mutasi `rows` di tempat).
/// Baris dicari berdasarkan `row_key` (kunci tetap, mis. NO FAKTUR)
/// di kolom `key_column` — BUKAN nomor baris, jadi sisipan baris di
/// atas tidak menggeser draft ke order lain (ANTI-MENCLOK). Draft
/// hanya diterapkan bila sel masih berisi nilai asli yang sama; draft
/// yang kuncinya kosong (format lama) atau tidak ditemukan, atau
/// nilainya sudah berubah, dikembalikan sebagai sisa (tidak menimpa
/// data orang lain). Mengembalikan (diterapkan, sisa).
pub fn apply_drafts(
    drafts: &[DraftEntry],
    sheet_id: &str,
    rows: &mut [Vec<String>],
    column_count: usize,
    first_sheet_row: i32,
    key_column: Option<usize>,
) -> (Vec<DraftEntry>, Vec<DraftEntry>) {
    let mut applied: Vec<DraftEntry> = Vec::new();
    let mut remaining = Vec::new();
    for draft in drafts {
        if draft.sheet_id != sheet_id {
            continue;
        }
        if draft.column_index < 0 || draft.column_index as usize >= column_count {
            remaining.push(draft.clone());
            continue;
        }
        // Tanpa kunci tetap, draft TIDAK diterapkan berdasarkan posisi
        // (celah menclok: nilai "DONE"/"1" berulang di banyak baris).
        let Some(key_col) = key_column else {
            remaining.push(draft.clone());
            continue;
        };
        if draft.row_key.is_empty() {
            remaining.push(draft.clone());
            continue;
        }
        let Some(row) = rows
            .iter_mut()
            .find(|r| r.get(key_col).map(|s| s.as_str()) == Some(draft.row_key.as_str()))
        else {
            remaining.push(draft.clone());
            continue;
        };
        if (draft.column_index as usize) >= row.len() {
            remaining.push(draft.clone());
            continue;
        }
        let cell_original = row[draft.column_index as usize].clone();
        if cell_original != draft.original_value {
            remaining.push(draft.clone());
            continue;
        }
        row[draft.column_index as usize] = draft.value.clone();
        applied.push(draft.clone());
    }
    let _ = first_sheet_row; // nomor baris hanya untuk pesan/kompat.
    (applied, remaining)
}

/// Status baris sheet terakhir yang dimuat (snapshot lokal).
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SheetSnapshot {
    #[serde(rename = "SheetId")]
    pub sheet_id: String,
    #[serde(rename = "SheetName")]
    pub sheet_name: String,
    #[serde(rename = "Rows")]
    pub rows: Vec<Vec<String>>,
    /// Header kolom snapshot (28 Nov 2026 — supaya setelah sesi live,
    /// restart offline tetap menampilkan data terakhir dengan header
    /// yang benar, bukan 7 kolom lokal). Kosong = snapshot lama.
    #[serde(default)]
    pub headers: Vec<String>,
    /// Indeks kolom kunci (NO FAKTUR) snapshot.
    #[serde(default)]
    pub key_column: Option<usize>,
    /// Nomor baris sheet (1-based) baris data pertama snapshot.
    #[serde(default = "default_first_sheet_row")]
    pub first_sheet_row: i32,
}

fn default_first_sheet_row() -> i32 {
    1
}

/// State spreadsheet yang dipegang Rust (satu sheet logis, dipakai
/// bersama oleh tampilan embedded + window).
pub struct SpreadsheetState {
    pub sheet_id: String,
    pub sheet_name: String,
    pub first_sheet_row: i32,
    pub column_count: usize,
    /// Indeks kolom kunci tetap baris (NO FAKTUR — untuk sheet kantor
    /// kolom A = 0). None = model tanpa kunci (draft tidak bisa
    /// diterapkan — lihat `apply_drafts`).
    pub key_column: Option<usize>,
    /// Nama header per kolom (untuk pesan guard yang menyebut nama kolom).
    pub headers: Vec<String>,
    /// Definisi kolom yang sedang tampil (lokal 7 kolom atau dinamis
    /// dari header sheet live). Sumber kunci / jenis / lebar grid.
    pub columns: Vec<crate::sheet_model::DynColumn>,
    /// True bila grid berasal dari Sheets API live (bukan snapshot
    /// lokal). Menentukan pesan status + jalur simpan.
    pub live: bool,
    /// Paging: Slint hanya merender satu jendela baris (Slint tidak
    /// memvirtualisasi seperti DataGrid C# — 16.308 baris × 20 kolom
    /// tidak bisa jadi elemen). Rust memegang seluruh `loaded`,
    /// jendela = `loaded[page_start..page_start+page_size]`.
    pub page_start: usize,
    pub page_size: usize,
    /// Baris seperti saat dimuat (nilai asli; sumber jangkar).
    pub loaded: Vec<Vec<String>>,
    pub anchors: Vec<RowAnchor>,
    pub drafts: Vec<DraftEntry>,
    /// Simpan live sedang dalam flight (thread fetch/push) — dipakai
    /// mencegah Simpan ganda + Muat Ulang bersamaan (28 Nov 2026).
    /// Edit sel TIDAK diblokir selama flight; draft yang masuk selama
    /// flight dipertahankan di tick SavePushed (lihat main.rs).
    pub saving_live: bool,
}

/// Jumlah baris per halaman jendela grid.
pub const SHEET_PAGE_SIZE: usize = 100;

impl SpreadsheetState {
    pub fn new(
        sheet_id: String,
        sheet_name: String,
        first_sheet_row: i32,
        column_count: usize,
        loaded: Vec<Vec<String>>,
        key_column: Option<usize>,
    ) -> Self {
        let anchors = build_anchors(&loaded, first_sheet_row, column_count, key_column);
        Self {
            sheet_id,
            sheet_name,
            first_sheet_row,
            column_count,
            key_column,
            headers: Vec::new(),
            columns: crate::sheet_model::local_columns(),
            live: false,
            page_start: 0,
            page_size: SHEET_PAGE_SIZE,
            loaded,
            anchors,
            drafts: Vec::new(),
            saving_live: false,
        }
    }

    /// Nama header untuk pesan guard (sejumlah `column_count`; bila
    /// kosong, pesan memakai "kolom").
    pub fn set_headers(&mut self, headers: Vec<String>) {
        self.headers = headers;
    }

    /// Beralih ke kolom live dari header sheet (dipanggil setelah fetch
    /// Sheets API berhasil): kolom dinamis, kunci, header, baris awal
    /// data (header_row + 1, 1-based), mode live. Halaman kembali ke awal.
    pub fn set_live_columns(
        &mut self,
        columns: Vec<crate::sheet_model::DynColumn>,
        key_column: Option<usize>,
        headers: Vec<String>,
        first_sheet_row: i32,
    ) {
        self.column_count = columns.len();
        self.columns = columns;
        self.key_column = key_column;
        self.headers = headers;
        self.first_sheet_row = first_sheet_row;
        self.live = true;
        self.page_start = 0;
    }

    /// Kembali ke model lokal 7 kolom (offline / tanpa kredensial).
    pub fn set_local_columns(&mut self) {
        let cols = crate::sheet_model::local_columns();
        self.column_count = cols.len();
        self.columns = cols;
        self.key_column = Some(0);
        self.headers = crate::sheet_model::SHEET_COLUMNS
            .iter()
            .map(|c| c.header.to_string())
            .collect();
        self.live = false;
        self.page_start = 0;
    }

    /// Jendela baris yang dirender Slint dari `rows` penuh
    /// (loaded / display): `rows[page_start..page_start+page_size]`.
    pub fn window_rows(&self, rows: &[Vec<String>]) -> Vec<Vec<String>> {
        let start = self.page_start.min(rows.len());
        let end = (start + self.page_size).min(rows.len());
        rows[start..end].to_vec()
    }

    /// Nomor baris sheet (1-based) baris pertama jendela — tampil di
    /// gutter (live: header_row + view_start).
    pub fn row_num_base(&self) -> i32 {
        self.first_sheet_row + self.page_start as i32
    }

    /// Pindah halaman (+1 / -1); mengembalikan true bila berubah.
    /// `total` = jumlah baris penuh.
    pub fn turn_page(&mut self, delta: i32, total: usize) -> bool {
        if total == 0 {
            return false;
        }
        let last_start = total.saturating_sub(1) / self.page_size * self.page_size;
        let cur = self.page_start as i32;
        let next = (cur + delta * self.page_size as i32).clamp(0, last_start as i32);
        if next as usize == self.page_start {
            return false;
        }
        self.page_start = next as usize;
        true
    }

    /// Kunci tetap baris ke-`row0` (nilai kolom kunci saat dimuat).
    fn row_key_at(&self, row0: usize) -> String {
        match self.key_column {
            Some(k) => self
                .loaded
                .get(row0)
                .and_then(|r| r.get(k))
                .cloned()
                .unwrap_or_default(),
            None => String::new(),
        }
    }

    /// Catat satu edit sel sebagai draft (satu entri per sel; edit ulang
    /// sel yang sama menimpa entri lama). Draft membawa `row_key` baris
    /// (ANTI-MENCLOK). Mengembalikan true bila ada perubahan draft.
    pub fn record_edit(&mut self, row0: usize, col: usize, original: &str, value: &str) -> bool {
        let sheet_row = self.first_sheet_row + row0 as i32;
        let row_key = self.row_key_at(row0);
        if let Some(e) = self.drafts.iter_mut().find(|d| {
            d.column_index == col as i32
                && (if !row_key.is_empty() {
                    d.row_key == row_key
                } else {
                    d.row_key.is_empty() && d.sheet_row_number == sheet_row
                })
        }) {
            if e.value == value {
                return false;
            }
            e.value = value.to_string();
            return true;
        }
        if original == value {
            return false;
        }
        self.drafts.push(DraftEntry {
            sheet_id: self.sheet_id.clone(),
            sheet_name: self.sheet_name.clone(),
            sheet_row_number: sheet_row,
            row_key,
            column_index: col as i32,
            original_value: original.to_string(),
            value: value.to_string(),
        });
        true
    }

    /// Guard sebelum simpan (C# CheckRowsStillAlignedAsync): bandingkan
    /// jangkar baris-baris yang berubah dengan nilai sheet yang baru
    /// dibaca. Bila ada kolom kunci, perbandingan memakai kunci tetap
    /// (ANTI-MENCLOK); bila tidak, memakai nomor baris. None = aman;
    /// Some(pesan) = simpan dibatalkan.
    pub fn check_save(&self, current_sheet: &[Vec<String>]) -> Option<String> {
        let changed_rows: Vec<&RowAnchor> = self
            .anchors
            .iter()
            .filter(|a| {
                self.drafts.iter().any(|d| {
                    if !d.row_key.is_empty() {
                        d.row_key == a.row_key
                    } else {
                        d.sheet_row_number == a.sheet_row_number
                    }
                })
            })
            .collect();
        if changed_rows.is_empty() {
            return None;
        }
        let owned: Vec<RowAnchor> = changed_rows.into_iter().cloned().collect();
        let mismatches = match self.key_column {
            Some(k) => find_mismatches_by_key(
                &owned,
                current_sheet,
                k,
                self.column_count,
                self.first_sheet_row,
            ),
            None => find_mismatches(
                &owned,
                Some(current_sheet),
                self.first_sheet_row,
                self.column_count,
            ),
        };
        if mismatches.is_empty() {
            None
        } else {
            Some(describe_mismatches(&mismatches, &self.headers))
        }
    }

    /// Setelah simpan berhasil: nilai baru menjadi nilai asli, jangkar
    /// dibangun ulang, draft dibersihkan.
    pub fn commit_save(&mut self, new_values: Vec<Vec<String>>) {
        self.loaded = new_values;
        self.anchors = build_anchors(
            &self.loaded,
            self.first_sheet_row,
            self.column_count,
            self.key_column,
        );
        self.drafts.clear();
    }

    /// Muat ulang dari snapshot baru: bangun ulang jangkar, lalu pulihkan
    /// draft (hanya yang nilai aslinya masih cocok). Mengembalikan
    /// (baris_tampil, diterapkan, sisa). Draft yang berhasil diterapkan
    /// TETAP jadi edit yang belum tersimpan (bukan "habis dipakai"):
    /// tampilan dihitung dari loaded + drafts di mana-mana
    /// (sheet_display_rows), jadi kalau diterapkan lalu dibuang,
    /// SpreadsheetWindow yang dibuka belakangan menampilkan nilai asli
    /// dan check_save kehilangan baris yang berubah. Draft yang tidak
    /// bisa diterapkan tetap tersimpan sebagai sisa (tidak menimpa).
    pub fn reload(
        &mut self,
        fresh_rows: Vec<Vec<String>>,
    ) -> (Vec<Vec<String>>, usize, Vec<DraftEntry>) {
        self.loaded = fresh_rows;
        self.anchors = build_anchors(
            &self.loaded,
            self.first_sheet_row,
            self.column_count,
            self.key_column,
        );
        let mut display = self.loaded.clone();
        let (applied, remaining) = apply_drafts(
            &self.drafts.clone(),
            &self.sheet_id,
            &mut display,
            self.column_count,
            self.first_sheet_row,
            self.key_column,
        );
        let applied_n = applied.len();
        let mut live = applied;
        live.extend(remaining.iter().cloned());
        self.drafts = live;
        (display, applied_n, remaining)
    }
}

fn build_anchors(
    loaded: &[Vec<String>],
    first_sheet_row: i32,
    column_count: usize,
    key_column: Option<usize>,
) -> Vec<RowAnchor> {
    loaded
        .iter()
        .enumerate()
        .map(|(i, row)| {
            let fp = fingerprint_from_values(row, column_count);
            RowAnchor {
                sheet_row_number: first_sheet_row + i as i32,
                row_key: key_column
                    .and_then(|k| row.get(k))
                    .cloned()
                    .unwrap_or_default(),
                display: fingerprint_display(&fp),
                fingerprint: fp,
            }
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn row(vals: &[&str]) -> Vec<String> {
        vals.iter().map(|s| s.to_string()).collect()
    }

    #[test]
    fn fingerprint_joins_all_columns() {
        let fp = fingerprint_from_values(&row(&["A", "B", "C"]), 3);
        assert_eq!(fp, "A\u{1f}B\u{1f}C");
        // Baris lebih pendek dari kolom: sel kosong di kanan dipangkas.
        let fp2 = fingerprint_from_values(&row(&["A"]), 3);
        assert_eq!(fp2, "A\u{1f}\u{1f}");
        assert_eq!(fingerprint_from_values(&[], 0), "");
    }

    #[test]
    fn fingerprint_empty_detection() {
        assert!(fingerprint_is_empty(""));
        assert!(fingerprint_is_empty("\u{1f}\u{1f}"));
        assert!(fingerprint_is_empty("  \u{1f} "));
        assert!(!fingerprint_is_empty("A\u{1f}"));
        assert_eq!(fingerprint_display("SEKOLAH CONTOH\u{1f}SD\u{1f}"), "SEKOLAH CONTOH");
        assert_eq!(fingerprint_display("\u{1f}\u{1f}"), "");
    }

    #[test]
    fn guard_allows_save_when_row_only_moved_by_insert() {
        // ANTI-MENCLOK: sisipan baris di atas saja (isi baris yang
        // diedit TIDAK berubah) tidak boleh memblokir simpan — draft
        // dicari berdasarkan kunci, bukan nomor baris.
        let loaded = vec![
            row(&["540", "SDN A", "SD"]),
            row(&["541", "SDN B", "SD"]),
            row(&["542", "SDN C", "SD"]),
        ];
        let mut state = SpreadsheetState::new("id".into(), "S".into(), 1, 3, loaded, Some(0));
        state.record_edit(1, 1, "SDN B", "SDN B EDIT");
        let current = vec![
            row(&["540", "SDN A", "SD"]),
            row(&["539", "SDN BARU", "SD"]),
            row(&["541", "SDN B", "SD"]),
            row(&["542", "SDN C", "SD"]),
        ];
        assert!(state.check_save(&current).is_none());
    }

    #[test]
    fn guard_blocks_when_keyed_row_changed_externally() {
        let loaded = vec![
            row(&["540", "SDN A", "SD"]),
            row(&["541", "SDN B", "SD"]),
            row(&["542", "SDN C", "SD"]),
        ];
        let mut state = SpreadsheetState::new("id".into(), "S".into(), 1, 3, loaded, Some(0));
        state.set_headers(vec![
            "NO FAKTUR".to_string(),
            "SEKOLAH".to_string(),
            "JENJANG".to_string(),
        ]);
        state.record_edit(1, 1, "SDN B", "SDN B EDIT");
        // Baris kunci 541 berubah di luar (kolom SEKOLAH diganti orang
        // lain) — walau posisinya sama, simpan harus diblokir.
        let current = vec![
            row(&["540", "SDN A", "SD"]),
            row(&["541", "SDN B DIUBAH", "SD"]),
            row(&["542", "SDN C", "SD"]),
        ];
        // Tanpa draft, tidak ada yang dicek.
        let fresh_state =
            SpreadsheetState::new("id".into(), "S".into(), 1, 3, current.clone(), Some(0));
        assert!(fresh_state.check_save(&current).is_none());
        let msg = state.check_save(&current).expect("harus diblokir");
        // Pesan menyebut NO FAKTUR + kolom yang berubah + nilai lama/baru.
        assert!(msg.contains("541"), "pesan: {msg}");
        assert!(msg.contains("SEKOLAH"), "pesan: {msg}");
        assert!(msg.contains("SDN B DIUBAH"), "pesan: {msg}");
        assert!(msg.contains("SDN B"), "pesan: {msg}");
        assert!(msg.contains("Simpan dibatalkan"), "pesan: {msg}");
        // Bukan cacat lama: pengenal baris yang sama di kedua sisi.
        assert!(
            !msg.contains("berisi '541', sedangkan yang dimuat '541'"),
            "pesan: {msg}"
        );
    }

    #[test]
    fn guard_blocks_when_keyed_row_deleted_externally() {
        let loaded = vec![row(&["540", "SDN A", "SD"]), row(&["541", "SDN B", "SD"])];
        let mut state = SpreadsheetState::new("id".into(), "S".into(), 1, 3, loaded, Some(0));
        state.record_edit(1, 1, "SDN B", "SDN B EDIT");
        let current = vec![row(&["540", "SDN A", "SD"])];
        let msg = state.check_save(&current).expect("harus diblokir");
        assert!(msg.contains("541"), "pesan: {msg}");
        assert!(msg.contains("tidak ditemukan lagi"), "pesan: {msg}");
    }

    #[test]
    fn guard_ignores_empty_rows_and_passes_when_unchanged() {
        let loaded = vec![row(&["", "", ""]), row(&["540", "SDN A", "SD"])];
        let mut state = SpreadsheetState::new("id".into(), "S".into(), 1, 3, loaded, Some(0));
        state.record_edit(1, 1, "SDN A", "SDN A EDIT");
        let current = vec![row(&["", "", ""]), row(&["540", "SDN A", "SD"])];
        assert!(state.check_save(&current).is_none());
        // Baris hilang dari sheet (di luar jangkauan bacaan) = mismatch.
        let short = vec![row(&["", "", ""])];
        assert!(state.check_save(&short).is_some());
    }

    #[test]
    fn draft_roundtrip_and_restore_rules() {
        let mut state = SpreadsheetState::new(
            "id".into(),
            "S".into(),
            1,
            2,
            vec![row(&["A1", "B1"]), row(&["A2", "B2"])],
            Some(0),
        );
        state.record_edit(0, 1, "B1", "B1 EDIT");
        state.record_edit(1, 0, "A2", "A2 EDIT");
        let json = serialize_drafts(&state.drafts);
        // Skema JSON sama dengan C# (PascalCase) + RowKey baru.
        assert!(json.contains("\"SheetRowNumber\""));
        assert!(json.contains("\"OriginalValue\""));
        assert!(json.contains("\"RowKey\":\"A1\""));
        let back = deserialize_drafts(&json);
        assert_eq!(back, state.drafts);
        // JSON rusak -> kosong, tidak crash.
        assert!(deserialize_drafts("{bukan json").is_empty());

        // Reload: sel pertama nilainya berubah di luar -> draft tidak
        // diterapkan tapi tetap tersimpan sebagai sisa.
        let fresh = vec![row(&["A1", "B1 BARU"]), row(&["A2", "B2"])];
        let (display, applied, remaining) = state.reload(fresh);
        assert_eq!(applied, 1);
        assert_eq!(remaining.len(), 1);
        assert_eq!(remaining[0].value, "B1 EDIT");
        assert_eq!(display[0][1], "B1 BARU"); // tidak ditimpa
        assert_eq!(display[1][0], "A2 EDIT"); // diterapkan
        // Draft yang diterapkan TETAP tersimpan (edit belum tersimpan),
        // bukan dibuang — kalau tidak, tampilan lain yang dihitung dari
        // loaded + drafts akan kehilangan nilainya.
        assert_eq!(state.drafts.len(), 2);
        assert!(state.drafts.iter().any(|d| d.value == "A2 EDIT"));
        assert!(state.drafts.iter().any(|d| d.value == "B1 EDIT"));
    }

    #[test]
    fn commit_save_reanchors_and_clears_drafts() {
        let mut state = SpreadsheetState::new(
            "id".into(),
            "S".into(),
            1,
            2,
            vec![row(&["A1", "B1"])],
            Some(0),
        );
        state.record_edit(0, 1, "B1", "B1 EDIT");
        state.commit_save(vec![row(&["A1", "B1 EDIT"])]);
        assert!(state.drafts.is_empty());
        assert!(state.check_save(&[row(&["A1", "B1 EDIT"])]).is_none());
        // Edit baru lalu sheet berubah -> diblokir lagi.
        state.record_edit(0, 0, "A1", "A1 EDIT");
        assert!(state
            .check_save(&[row(&["A1 LAIN", "B1 EDIT"])])
            .is_some());
    }

    #[test]
    fn draft_applies_by_key_after_insert_above() {
        // ANTI-MENCLOK (bukti unit): satu baris disisip di atas data,
        // lalu reload — draft harus mendarat di NO FAKTUR yang sama,
        // bukan di nomor baris yang sama.
        let mut state = SpreadsheetState::new(
            "id".into(),
            "S".into(),
            4,
            3,
            vec![
                row(&["6CCC001", "SDN A", "SD"]),
                row(&["6CCC002", "SDN B", "SD"]),
                row(&["6CCC003", "SDN C", "SD"]),
            ],
            Some(0),
        );
        state.record_edit(1, 1, "SDN B", "SDN B EDIT");
        assert_eq!(state.drafts[0].row_key, "6CCC002");
        // Sisip satu baris di atas data (sheet kini 4 baris).
        let fresh = vec![
            row(&["6CCC000", "SDN BARU", "SD"]),
            row(&["6CCC001", "SDN A", "SD"]),
            row(&["6CCC002", "SDN B", "SD"]),
            row(&["6CCC003", "SDN C", "SD"]),
        ];
        let (display, applied, remaining) = state.reload(fresh);
        assert_eq!(applied, 1);
        assert!(remaining.is_empty());
        // Draft mendarat di baris 6CCC002 (kini indeks 2), bukan di
        // baris baru 6CCC000 (indeks 0) atau 6CCC001 (indeks 1).
        assert_eq!(display[0][1], "SDN BARU");
        assert_eq!(display[1][1], "SDN A");
        assert_eq!(display[2][1], "SDN B EDIT");
        assert_eq!(display[3][1], "SDN C");
    }

    #[test]
    fn draft_without_key_never_applied_by_position() {
        // Draft format lama (JSON C# tanpa RowKey) tetap bisa dibaca,
        // tapi TIDAK PERNAH diterapkan berdasarkan posisi — walau sel
        // di nomor baris itu masih berisi nilai asli yang sama.
        let json = r#"[{"SheetId":"id","SheetName":"S","SheetRowNumber":5,"ColumnIndex":1,"OriginalValue":"DONE","Value":"DONE EDIT"}]"#;
        let drafts = deserialize_drafts(json);
        assert_eq!(drafts.len(), 1);
        assert!(drafts[0].row_key.is_empty());
        let mut rows = vec![
            row(&["6CCC001", "DONE"]),
            row(&["6CCC002", "DONE"]),
            row(&["6CCC003", "DONE"]),
        ];
        // Sisip di atas: nomor baris 5 kini menunjuk order lain yang
        // kebetulan berisi "DONE" juga — draft tetap tidak diterapkan.
        let (applied, remaining) = apply_drafts(&drafts, "id", &mut rows, 2, 4, Some(0));
        assert!(applied.is_empty());
        assert_eq!(remaining.len(), 1);
        assert!(rows.iter().all(|r| r[1] == "DONE"));
    }
}
