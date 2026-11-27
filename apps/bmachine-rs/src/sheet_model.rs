//! Model kolom + logika spreadsheet (fill down, hapus, kalender tanggal).
//! Port perilaku C# (Avalonia DataGrid di `SpreadsheetView.axaml.cs` +
//! `SpreadsheetViewModel.cs`) — tapi HANYA perilaku yang benar.
//!
//! Yang sengaja TIDAK ditiru dari C# (catatan abeng 27 Nov 23:07 —
//! "spreadsheet C# masih belum sempurna / aneh"):
//! - C# mengaktifkan edit lewat toggle `DataGrid.IsReadOnly` bolak-balik
//!   (false saat mulai edit, dan `BeginningEdit` membatalkan edit klik
//!   tunggal) + flag `_openDropdownOnNextEdit` + post ke dispatcher —
//!   rapuh. Port memakai state `editing` eksplisit di Slint.
//! - C# menebak jenis kolom dari header (`EDITOR`/`STATUS`/`SELEKSI`/
//!   `HKS`/`NR` => dropdown; `TGL`/`DATE`/`TIME`/`DEADLINE` => tanggal)
//!   dan dari validasi Google Sheets. Port memakai jenis kolom eksplisit
//!   di `SHEET_COLUMNS` (deterministik; nanti dipetakan dari metadata
//!   sheet saat Fase 6 tersambung ke Google Sheets).
//! - C# tidak punya pengunci sel sama sekali (`IsReadOnly = false` di
//!   grid; hanya kolom gutter nomor baris yang read-only) — padahal
//!   sheet kantor punya sel yang dilindungi. Port menambahkan pengunci
//!   per kolom (`locked`), sumbernya nanti metadata sheet.
//! - C# `FillDownRows` diam-diam tidak berbuat apa-apa bila < 2 baris
//!   terpilih, dan menulis status debug ke status bar. Port memberi
//!   toast yang jelas bila tidak ada yang bisa diisi.
//! - C# `FixHeaderContentCentering` (geser margin header +16px) adalah
//!   workaround theme Avalonia — tidak relevan di Slint.
//! - C# `StringToDateTimeConverter` masih ragu format tanggal ("6
//!   Februari 2026" vs ISO). Port konsisten memakai `yyyy-MM-dd`
//!   (format yang sama dipakai C# saat menyimpan ke Sheets).

use chrono::{Datelike, Local, NaiveDate};

/// Jenis isi satu kolom sheet.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SheetColKind {
    /// Teks bebas.
    Text,
    /// Angka (editor tetap teks; C# juga tidak membatasi ketikan).
    Number,
    /// Pilihan dari daftar (ComboBox di C#).
    Dropdown,
    /// Tanggal `yyyy-MM-dd` (kalender + tombol TODAY di C#).
    Date,
}

impl SheetColKind {
    pub fn as_str(&self) -> &'static str {
        match self {
            SheetColKind::Text => "text",
            SheetColKind::Number => "number",
            SheetColKind::Dropdown => "dropdown",
            SheetColKind::Date => "date",
        }
    }
}

/// Definisi satu kolom sheet (metadata — di C# datang dari header +
/// metadata Google Sheets; di port ini tetap sampai Fase 6).
#[derive(Debug, Clone, Copy)]
pub struct SheetColumn {
    /// Huruf kolom (A..G).
    pub letter: &'static str,
    /// Nama header tampil.
    pub header: &'static str,
    pub kind: SheetColKind,
    /// Bila true, sel di kolom ini tidak bisa diedit (sel terkunci —
    /// perlindungan sheet kantor).
    pub locked: bool,
    /// Pilihan dropdown (hanya dipakai bila `kind == Dropdown`).
    pub items: &'static [&'static str],
}

/// Jumlah kolom sheet di port (A–G).
pub const SHEET_COLUMN_COUNT: usize = 7;

/// Kolom sheet port — nilai opsi dropdown diambil dari data contoh lama
/// (sebelum default-clean 27 Nov): Jenjang SD/SMP/SMA (+TK/PAUD dari
/// kartu Trello), Status SELESAI/PROSES/REVISI/ANTRIAN.
pub const SHEET_COLUMNS: [SheetColumn; SHEET_COLUMN_COUNT] = [
    SheetColumn {
        letter: "A",
        header: "KODE",
        kind: SheetColKind::Text,
        // Kode = ID baris (dipakai "Copy Row ID" di C#) — tidak bisa diedit.
        locked: true,
        items: &[],
    },
    SheetColumn {
        letter: "B",
        header: "SEKOLAH",
        kind: SheetColKind::Text,
        locked: false,
        items: &[],
    },
    SheetColumn {
        letter: "C",
        header: "JENJANG",
        kind: SheetColKind::Dropdown,
        locked: false,
        items: &["TK", "PAUD", "SD", "SMP", "SMA", "SMK"],
    },
    SheetColumn {
        letter: "D",
        header: "STATUS",
        kind: SheetColKind::Dropdown,
        locked: false,
        items: &["SELESAI", "PROSES", "REVISI", "ANTRIAN"],
    },
    SheetColumn {
        letter: "E",
        header: "POIN",
        kind: SheetColKind::Number,
        locked: false,
        items: &[],
    },
    SheetColumn {
        letter: "F",
        header: "CATATAN",
        kind: SheetColKind::Text,
        locked: false,
        items: &[],
    },
    SheetColumn {
        letter: "G",
        header: "TANGGAL",
        kind: SheetColKind::Date,
        locked: false,
        items: &[],
    },
];

/// Isi ke bawah (C# Ctrl+D, "Fill Down - Row based"): salin nilai baris
/// paling atas dari seleksi ke baris-baris terpilih lainnya, HANYA di
/// kolom fokus. Mengembalikan daftar (baris 0-based, kolom, nilai baru)
/// yang benar-benar berubah — sel terkunci dan sel yang nilainya sudah
/// sama dilewati (tidak jadi draft).
pub fn fill_down(
    grid: &[Vec<String>],
    focus_col: usize,
    selected_rows: &[bool],
) -> Vec<(usize, usize, String)> {
    if focus_col >= SHEET_COLUMN_COUNT || SHEET_COLUMNS[focus_col].locked {
        return Vec::new();
    }
    let Some(top) = selected_rows.iter().position(|s| *s) else {
        return Vec::new();
    };
    let Some(source_row) = grid.get(top) else {
        return Vec::new();
    };
    let Some(source) = source_row.get(focus_col) else {
        return Vec::new();
    };
    let source = source.clone();
    let mut out = Vec::new();
    for (r, selected) in selected_rows.iter().enumerate() {
        if !selected || r == top {
            continue;
        }
        let cur = grid
            .get(r)
            .and_then(|row| row.get(focus_col))
            .map(|s| s.as_str())
            .unwrap_or("");
        if cur != source {
            out.push((r, focus_col, source.clone()));
        }
    }
    out
}

/// Hapus isi sel terpilih di kolom fokus (C# Delete/Backspace: mengosongkan
/// sel-sel baris terpilih di kolom fokus). Mengembalikan daftar
/// (baris, kolom, "") yang benar-benar berubah; sel terkunci dilewati.
pub fn clear_cells(
    grid: &[Vec<String>],
    focus_col: usize,
    selected_rows: &[bool],
) -> Vec<(usize, usize, String)> {
    if focus_col >= SHEET_COLUMN_COUNT || SHEET_COLUMNS[focus_col].locked {
        return Vec::new();
    }
    let mut out = Vec::new();
    for (r, selected) in selected_rows.iter().enumerate() {
        if !selected {
            continue;
        }
        let cur = grid
            .get(r)
            .and_then(|row| row.get(focus_col))
            .map(|s| s.as_str())
            .unwrap_or("");
        if !cur.is_empty() {
            out.push((r, focus_col, String::new()));
        }
    }
    out
}

/// Jumlahkan kolom Poin (kolom E, indeks 4) — hanya nilai yang bisa
/// diparse sebagai bilangan yang dihitung; sisanya dilewati.
pub fn sum_points(grid: &[Vec<String>]) -> i64 {
    grid.iter()
        .filter_map(|row| row.get(4))
        .filter_map(|s| s.trim().parse::<i64>().ok())
        .sum()
}

/// Nama bulan Bahasa Indonesia (kalender memakai Senin sebagai hari pertama,
/// seperti Google Sheets locale id).
pub const MONTH_NAMES_ID: [&str; 12] = [
    "Januari",
    "Februari",
    "Maret",
    "April",
    "Mei",
    "Juni",
    "Juli",
    "Agustus",
    "September",
    "Oktober",
    "November",
    "Desember",
];

/// Label kalender, mis. "Februari 2026".
pub fn month_label(year: i32, month: u32) -> String {
    let name = MONTH_NAMES_ID
        .get(month.saturating_sub(1) as usize)
        .copied()
        .unwrap_or("");
    format!("{} {}", name, year)
}

/// Grid kalender satu bulan: 42 sel (6 minggu × 7 hari, Senin pertama).
/// 0 = sel kosong (di luar bulan), selain itu nomor tanggal.
pub fn month_grid(year: i32, month: u32) -> Vec<i32> {
    let mut cells = vec![0i32; 42];
    let Some(first) = NaiveDate::from_ymd_opt(year, month, 1) else {
        return cells;
    };
    let offset = first.weekday().num_days_from_monday() as usize;
    let days = days_in_month(year, month);
    for d in 1..=days {
        if offset + (d as usize) - 1 < 42 {
            cells[offset + (d as usize) - 1] = d;
        }
    }
    cells
}

/// Jumlah hari dalam satu bulan.
pub fn days_in_month(year: i32, month: u32) -> i32 {
    match month {
        1 | 3 | 5 | 7 | 8 | 10 | 12 => 31,
        4 | 6 | 9 | 11 => 30,
        2 => {
            if (year % 4 == 0 && year % 100 != 0) || year % 400 == 0 {
                29
            } else {
                28
            }
        }
        _ => 30,
    }
}

/// Geser bulan sebanyak `delta` (bisa negatif / melewati tahun).
pub fn shift_month(year: i32, month: u32, delta: i32) -> (i32, u32) {
    let total = year * 12 + (month as i32 - 1) + delta;
    let y = total.div_euclid(12);
    let m = (total.rem_euclid(12) + 1) as u32;
    (y, m)
}

/// Tanggal hari ini (waktu lokal).
pub fn today() -> NaiveDate {
    Local::now().date_naive()
}

/// Tanggal hari ini sebagai (tahun, bulan, hari) — untuk state kalender.
pub fn today_ymd() -> (i32, u32, u32) {
    let t = today();
    (t.year(), t.month(), t.day())
}

/// Tanggal hari ini sebagai `yyyy-MM-dd` (format simpan C#).
pub fn today_iso() -> String {
    today().format("%Y-%m-%d").to_string()
}

/// Format `yyyy-MM-dd`.
pub fn iso_date(year: i32, month: u32, day: u32) -> String {
    format!("{:04}-{:02}-{:02}", year, month, day)
}

/// Parse `yyyy-MM-dd` (juga menerima `yyyy-M-d`); None bila tidak valid.
pub fn parse_iso_date(s: &str) -> Option<(i32, u32, u32)> {
    let s = s.trim();
    let mut it = s.split('-');
    let y: i32 = it.next()?.parse().ok()?;
    let m: u32 = it.next()?.parse().ok()?;
    let d: u32 = it.next()?.parse().ok()?;
    if it.next().is_some() || !(1..=12).contains(&m) {
        return None;
    }
    NaiveDate::from_ymd_opt(y, m, d).map(|_| (y, m, d))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn row(vals: &[&str]) -> Vec<String> {
        vals.iter().map(|s| s.to_string()).collect()
    }

    #[test]
    fn columns_seven_with_kinds() {
        assert_eq!(SHEET_COLUMNS.len(), SHEET_COLUMN_COUNT);
        assert_eq!(SHEET_COLUMNS[0].letter, "A");
        assert!(SHEET_COLUMNS[0].locked);
        assert_eq!(SHEET_COLUMNS[2].kind, SheetColKind::Dropdown);
        assert_eq!(SHEET_COLUMNS[2].items, ["TK", "PAUD", "SD", "SMP", "SMA", "SMK"]);
        assert_eq!(SHEET_COLUMNS[3].items, ["SELESAI", "PROSES", "REVISI", "ANTRIAN"]);
        assert_eq!(SHEET_COLUMNS[6].kind, SheetColKind::Date);
        assert_eq!(SheetColKind::Date.as_str(), "date");
        assert!(!SHEET_COLUMNS[1..].iter().any(|c| c.locked));
    }

    #[test]
    fn fill_down_copies_top_row_in_focus_column_only() {
        let grid = vec![
            row(&["1", "SDN A", "SD", "SELESAI", "10", "", ""]),
            row(&["2", "SDN B", "SD", "PROSES", "20", "", ""]),
            row(&["3", "SDN C", "SD", "REVISI", "30", "", ""]),
        ];
        // Seleksi baris 0..2, kolom fokus B (1): B1..B3 = "SDN A".
        let edits = fill_down(&grid, 1, &[true, true, true]);
        assert_eq!(
            edits,
            vec![
                (1, 1, "SDN A".to_string()),
                (2, 1, "SDN A".to_string()),
            ]
        );
        // Satu baris terpilih saja: tidak ada yang diisi.
        assert!(fill_down(&grid, 1, &[true, false, false]).is_empty());
        // Tidak ada seleksi: kosong.
        assert!(fill_down(&grid, 1, &[false, false, false]).is_empty());
        // Baris tengah sebagai sumber (seleksi 1..2): sumber = baris 1.
        let edits = fill_down(&grid, 1, &[false, true, true]);
        assert_eq!(edits, vec![(2, 1, "SDN B".to_string())]);
    }

    #[test]
    fn fill_down_skips_locked_and_unchanged() {
        let grid = vec![
            row(&["1", "SDN A", "SD", "SELESAI", "10", "", ""]),
            row(&["1", "SDN A", "SD", "SELESAI", "10", "", ""]),
        ];
        // Kolom A terkunci: tidak ada edit walau seleksi ada.
        assert!(fill_down(&grid, 0, &[true, true]).is_empty());
        // Nilai sudah sama: tidak ada edit.
        assert!(fill_down(&grid, 1, &[true, true]).is_empty());
    }

    #[test]
    fn clear_cells_only_focus_column_and_skips_empty_locked() {
        let grid = vec![
            row(&["1", "SDN A", "SD", "SELESAI", "10", "", ""]),
            row(&["2", "", "SD", "PROSES", "20", "", ""]),
        ];
        let edits = clear_cells(&grid, 1, &[true, true]);
        assert_eq!(edits, vec![(0, 1, String::new())]);
        // Kolom terkunci: kosong.
        assert!(clear_cells(&grid, 0, &[true, true]).is_empty());
    }

    #[test]
    fn sum_points_parses_only_numbers() {
        let grid = vec![
            row(&["1", "A", "SD", "SELESAI", "1280", "", ""]),
            row(&["2", "B", "SD", "PROSES", "950", "", ""]),
            row(&["3", "C", "SD", "REVISI", "bukan-angka", "", ""]),
            row(&["4", "D", "SD", "SELESAI", "", "", ""]),
        ];
        assert_eq!(sum_points(&grid), 2230);
        assert_eq!(sum_points(&[]), 0);
    }

    #[test]
    fn month_grid_february_2026_monday_first() {
        // 1 Feb 2026 = Minggu -> offset 6 (Senin pertama), 28 hari.
        let cells = month_grid(2026, 2);
        assert_eq!(cells.len(), 42);
        assert_eq!(&cells[0..6], &[0, 0, 0, 0, 0, 0]);
        assert_eq!(cells[6], 1);
        assert_eq!(cells[33], 28);
        assert_eq!(&cells[34..], &[0, 0, 0, 0, 0, 0, 0, 0]);
        assert_eq!(month_label(2026, 2), "Februari 2026");
        assert_eq!(days_in_month(2026, 2), 28);
        assert_eq!(days_in_month(2024, 2), 29);
        assert_eq!(days_in_month(2026, 11), 30);
    }

    #[test]
    fn month_shift_crosses_year() {
        assert_eq!(shift_month(2026, 1, -1), (2025, 12));
        assert_eq!(shift_month(2026, 12, 1), (2027, 1));
        assert_eq!(shift_month(2026, 6, 0), (2026, 6));
        assert_eq!(shift_month(2026, 6, -7), (2025, 11));
    }

    #[test]
    fn iso_roundtrip() {
        assert_eq!(iso_date(2026, 2, 5), "2026-02-05");
        assert_eq!(parse_iso_date("2026-02-05"), Some((2026, 2, 5)));
        assert_eq!(parse_iso_date("2026-2-5"), Some((2026, 2, 5)));
        assert_eq!(parse_iso_date("2026-13-01"), None);
        assert_eq!(parse_iso_date("2026-02-30"), None);
        assert_eq!(parse_iso_date("bukan tanggal"), None);
        assert_eq!(parse_iso_date(""), None);
        // Hari ini selalu valid + format ISO.
        let t = today_iso();
        assert_eq!(t.len(), 10);
        assert!(parse_iso_date(&t).is_some());
    }
}
