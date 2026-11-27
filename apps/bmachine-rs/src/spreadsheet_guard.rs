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
// - Pesan guard DINAMIS (Describe): menyebut baris pertama yang bergeser
//   + isi aktual vs isi saat dimuat — bukan nilai hardcode.
// - Draft dipersist ke SQLite key "Spreadsheet.Draft" (JSON array, skema
//   sama dengan C#: SheetId/SheetName/SheetRowNumber/ColumnIndex/
//   OriginalValue/Value). Saat reload, draft hanya DITERAPKAN bila nilai
//   asli sel di sheet masih sama; draft yang tidak bisa diterapkan tetap
//   tersimpan sebagai sisa dan dicoba lagi di reload berikutnya (BUKAN
//   dibuang — koreksi premis DESIGN_SYSTEM.md §19.9 butir 5).

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

/// Jangkar identitas satu baris: nomor baris sheet (1-based), sidik jari
/// seluruh baris saat dimuat, dan nilai tampilan (untuk pesan).
#[derive(Debug, Clone, PartialEq)]
pub struct RowAnchor {
    pub sheet_row_number: i32,
    pub fingerprint: String,
    pub display: String,
}

#[derive(Debug, Clone, PartialEq)]
pub struct AnchorMismatch {
    pub sheet_row_number: i32,
    pub expected_display: String,
    pub actual_display: String,
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
        mismatches.push(AnchorMismatch {
            sheet_row_number: anchor.sheet_row_number,
            expected_display: anchor.display.clone(),
            actual_display,
        });
    }
    mismatches
}

/// Pesan siap tampil untuk pengguna, menyebut baris pertama yang bergeser.
/// Sama dengan C# SpreadsheetRowAnchorGuard.Describe().
pub fn describe_mismatches(mismatches: &[AnchorMismatch]) -> String {
    let first = &mismatches[0];
    let more = if mismatches.len() > 1 {
        format!(" (dan {} baris lain)", mismatches.len() - 1)
    } else {
        String::new()
    };
    format!(
        "Data di sheet sudah berubah sejak terakhir dimuat{}: baris {} di sheet berisi '{}', sedangkan yang dimuat '{}'. Simpan dibatalkan agar data tidak salah baris \u{2014} klik Muat Ulang; edit Anda tetap tersimpan sebagai draft dan dipulihkan setelah muat ulang.",
        more, first.sheet_row_number, first.actual_display, first.expected_display
    )
}

/// Satu perubahan yang belum dikirim ke sheet (skema JSON sama dengan C#).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DraftEntry {
    #[serde(rename = "SheetId")]
    pub sheet_id: String,
    #[serde(rename = "SheetName")]
    pub sheet_name: String,
    #[serde(rename = "SheetRowNumber")]
    pub sheet_row_number: i32,
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
/// Draft hanya diterapkan bila sel masih berisi nilai asli yang sama;
/// draft yang barisnya tidak ada / nilainya sudah berubah dikembalikan
/// sebagai sisa (tidak menimpa data orang lain). Mengembalikan
/// (jumlah_diterapkan, sisa).
pub fn apply_drafts(
    drafts: &[DraftEntry],
    sheet_id: &str,
    rows: &mut [Vec<String>],
    column_count: usize,
    first_sheet_row: i32,
) -> (usize, Vec<DraftEntry>) {
    let mut applied = 0usize;
    let mut remaining = Vec::new();
    for draft in drafts {
        if draft.sheet_id != sheet_id {
            continue;
        }
        let row_idx = draft.sheet_row_number - first_sheet_row;
        if row_idx < 0
            || draft.column_index < 0
            || draft.column_index as usize >= column_count
        {
            remaining.push(draft.clone());
            continue;
        }
        let Some(row) = rows.get_mut(row_idx as usize) else {
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
        applied += 1;
    }
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
}

/// State spreadsheet yang dipegang Rust (satu sheet logis, dipakai
/// bersama oleh tampilan embedded + window).
pub struct SpreadsheetState {
    pub sheet_id: String,
    pub sheet_name: String,
    pub first_sheet_row: i32,
    pub column_count: usize,
    /// Baris seperti saat dimuat (nilai asli; sumber jangkar).
    pub loaded: Vec<Vec<String>>,
    pub anchors: Vec<RowAnchor>,
    pub drafts: Vec<DraftEntry>,
}

impl SpreadsheetState {
    pub fn new(
        sheet_id: String,
        sheet_name: String,
        first_sheet_row: i32,
        column_count: usize,
        loaded: Vec<Vec<String>>,
    ) -> Self {
        let anchors = build_anchors(&loaded, first_sheet_row, column_count);
        Self {
            sheet_id,
            sheet_name,
            first_sheet_row,
            column_count,
            loaded,
            anchors,
            drafts: Vec::new(),
        }
    }

    /// Catat satu edit sel sebagai draft (satu entri per sel; edit ulang
    /// sel yang sama menimpa entri lama). Mengembalikan true bila ada
    /// perubahan draft.
    pub fn record_edit(&mut self, row0: usize, col: usize, original: &str, value: &str) -> bool {
        let sheet_row = self.first_sheet_row + row0 as i32;
        if let Some(e) = self
            .drafts
            .iter_mut()
            .find(|d| d.sheet_row_number == sheet_row && d.column_index == col as i32)
        {
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
            column_index: col as i32,
            original_value: original.to_string(),
            value: value.to_string(),
        });
        true
    }

    /// Guard sebelum simpan (C# CheckRowsStillAlignedAsync): bandingkan
    /// jangkar baris-baris yang berubah dengan nilai sheet yang baru
    /// dibaca. None = aman; Some(pesan) = simpan dibatalkan.
    pub fn check_save(&self, current_sheet: &[Vec<String>]) -> Option<String> {
        let changed_rows: Vec<&RowAnchor> = self
            .anchors
            .iter()
            .filter(|a| {
                self.drafts
                    .iter()
                    .any(|d| d.sheet_row_number == a.sheet_row_number)
            })
            .collect();
        if changed_rows.is_empty() {
            return None;
        }
        let owned: Vec<RowAnchor> = changed_rows.into_iter().cloned().collect();
        let mismatches = find_mismatches(
            &owned,
            Some(current_sheet),
            self.first_sheet_row,
            self.column_count,
        );
        if mismatches.is_empty() {
            None
        } else {
            Some(describe_mismatches(&mismatches))
        }
    }

    /// Setelah simpan berhasil: nilai baru menjadi nilai asli, jangkar
    /// dibangun ulang, draft dibersihkan.
    pub fn commit_save(&mut self, new_values: Vec<Vec<String>>) {
        self.loaded = new_values;
        self.anchors = build_anchors(&self.loaded, self.first_sheet_row, self.column_count);
        self.drafts.clear();
    }

    /// Muat ulang dari snapshot baru: bangun ulang jangkar, lalu pulihkan
    /// draft (hanya yang nilai aslinya masih cocok). Mengembalikan
    /// (baris_tampil, diterapkan, sisa).
    pub fn reload(
        &mut self,
        fresh_rows: Vec<Vec<String>>,
    ) -> (Vec<Vec<String>>, usize, Vec<DraftEntry>) {
        self.loaded = fresh_rows;
        self.anchors = build_anchors(&self.loaded, self.first_sheet_row, self.column_count);
        let mut display = self.loaded.clone();
        let (applied, remaining) = apply_drafts(
            &self.drafts.clone(),
            &self.sheet_id,
            &mut display,
            self.column_count,
            self.first_sheet_row,
        );
        self.drafts = remaining.clone();
        (display, applied, remaining)
    }
}

fn build_anchors(
    loaded: &[Vec<String>],
    first_sheet_row: i32,
    column_count: usize,
) -> Vec<RowAnchor> {
    loaded
        .iter()
        .enumerate()
        .map(|(i, row)| {
            let fp = fingerprint_from_values(row, column_count);
            RowAnchor {
                sheet_row_number: first_sheet_row + i as i32,
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
    fn guard_detects_shifted_row() {
        let loaded = vec![
            row(&["540", "SDN A", "SD"]),
            row(&["541", "SDN B", "SD"]),
            row(&["542", "SDN C", "SD"]),
        ];
        let state = SpreadsheetState::new("id".into(), "S".into(), 1, 3, loaded);
        // Sheet berubah di luar: baris disisip di atas — baris 2 kini
        // berisi baris lain (skenario utama guard C#: sisip/hapus/cut).
        let current = vec![
            row(&["540", "SDN A", "SD"]),
            row(&["539", "SDN BARU", "SD"]),
            row(&["541", "SDN B", "SD"]),
        ];
        // Tanpa draft, tidak ada yang dicek.
        assert!(state.check_save(&current).is_none());
        let mut state = state;
        state.record_edit(1, 1, "SDN B", "SDN B EDIT");
        let msg = state.check_save(&current).expect("harus diblokir");
        // Display = sel pertama yang tidak kosong (sama seperti C#).
        assert!(msg.contains("baris 2"));
        assert!(msg.contains("539"));
        assert!(msg.contains("541"));
        assert!(msg.contains("Simpan dibatalkan"));
    }

    #[test]
    fn guard_ignores_empty_rows_and_passes_when_unchanged() {
        let loaded = vec![row(&["", "", ""]), row(&["540", "SDN A", "SD"])];
        let mut state = SpreadsheetState::new("id".into(), "S".into(), 1, 3, loaded);
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
        );
        state.record_edit(0, 1, "B1", "B1 EDIT");
        state.record_edit(1, 0, "A2", "A2 EDIT");
        let json = serialize_drafts(&state.drafts);
        // Skema JSON sama dengan C# (PascalCase).
        assert!(json.contains("\"SheetRowNumber\""));
        assert!(json.contains("\"OriginalValue\""));
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
        assert_eq!(state.drafts.len(), 1);
    }

    #[test]
    fn commit_save_reanchors_and_clears_drafts() {
        let mut state = SpreadsheetState::new(
            "id".into(),
            "S".into(),
            1,
            2,
            vec![row(&["A1", "B1"])],
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
}
