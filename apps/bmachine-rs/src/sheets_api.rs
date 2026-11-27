//! Google Sheets API live client (Fase 6) — service-account JWT (RS256)
//! + spreadsheets.values get/update/append, + model sheet live
//! (deteksi header, kolom dinamis, kunci NO FAKTUR).
//!
//! Alur C# yang ditiru (SpreadsheetViewModel / PointLeaderboardViewModel):
//! - Kredensial: file JSON service account (Google.CredsPath) —
//!   client_email + private_key (PKCS#8 PEM).
//! - Token: JWT RS256 (iss=client_email, scope spreadsheets,
//!   aud=https://oauth2.googleapis.com/token, exp 1 jam) ditukar ke
//!   access token (grant-type jwt-bearer). Token di-cache sampai
//!   kedaluwarsa − 60 detik.
//! - Baca: GET /v4/spreadsheets/{id}/values/{tab}!{range}
//!   (FORMATTED_VALUE — API mengembalikan tampilan sel, bukan serial
//!   mentah: ORDER TIME jadi tanggal, QTY "72.0" jadi "72").
//! - Tulis: values.batchUpdate (satu request untuk semua sel draft)
//!   + values.append untuk baris baru.
//! - Header: baris pertama (maks 10 baris pertama) dengan >= 2 sel
//!   tak-kosong — untuk ALL DATA REGULER itu baris 3 (baris 1 judul,
//!   baris 2 kosong); JANGAN hardcode baris 3.
//! - Kunci baris: kolom header mengandung "FAKTUR" (NO FAKTUR, unik
//!   per order); bila tidak ada, None (guard jatuh ke posisi baris).
//! - Jenis kolom ditebak dari kata kunci header seperti C#
//!   (EDITOR/STATUS/SELEKSI/HKS/NR => dropdown; TGL/DATE/TIME/
//!   DEADLINE => tanggal; QTY/POIN/JUMLAH => angka; sisanya teks).
//!   Opsi dropdown = nilai-nilai berbeda yang terlihat di kolom itu
//!   (C# membaca metadata validasi Sheets; port tidak mengambil
//!   metadata — nilai berbeda memberi hasil yang sama untuk sheet
//!   kantor). ORDER TIME dikunci (teks) — C# menanganinya khusus,
//!   port tidak menimpa stempel waktu order.
//!
//! Base URL bisa di-override (SheetsClient::with_bases) supaya bisa
//! diuji melawan mock server lokal tanpa jaringan Google.

use base64::{engine::general_purpose::URL_SAFE_NO_PAD, Engine as _};
use ring::{
    rand::SystemRandom,
    signature::{RsaKeyPair, RSA_PKCS1_SHA256},
};
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::fs;
use std::path::Path;
use std::time::{SystemTime, UNIX_EPOCH};

use crate::sheet_model::SheetColKind;

pub const SHEETS_SCOPE: &str = "https://www.googleapis.com/auth/spreadsheets";
pub const DEFAULT_TOKEN_URL: &str = "https://oauth2.googleapis.com/token";
pub const DEFAULT_API_BASE: &str = "https://sheets.googleapis.com";

/// Satu pembaruan sel (dipakai values.batchUpdate).
#[derive(Debug, Clone, Serialize, Deserialize)]
#[allow(dead_code)]
pub struct SheetCellUpdate {
    pub sheet_name: String,
    pub row: i32,
    pub col: String,
    pub value: String,
}

/// Kredensial service account (isi file JSON Google.CredsPath).
#[derive(Debug, Clone)]
pub struct ServiceAccount {
    pub client_email: String,
    pub private_key_pem: String,
}

impl ServiceAccount {
    pub fn from_file(creds_path: &str) -> Result<Self, String> {
        let content = fs::read_to_string(Path::new(creds_path))
            .map_err(|e| format!("Gagal membaca file credentials: {}", e))?;
        Self::from_json(&content)
    }

    pub fn from_json(content: &str) -> Result<Self, String> {
        let json: serde_json::Value = serde_json::from_str(content)
            .map_err(|_| "Format file credentials.json tidak valid".to_string())?;
        let client_email = json
            .get("client_email")
            .and_then(|v| v.as_str())
            .unwrap_or("")
            .to_string();
        let private_key_pem = json
            .get("private_key")
            .and_then(|v| v.as_str())
            .unwrap_or("")
            .to_string();
        if client_email.is_empty() || private_key_pem.is_empty() {
            return Err(
                "credentials.json tidak berisi client_email / private_key service account"
                    .to_string(),
            );
        }
        Ok(Self {
            client_email,
            private_key_pem,
        })
    }
}

pub fn base64url(data: &[u8]) -> String {
    URL_SAFE_NO_PAD.encode(data)
}

fn pem_to_der(pem: &str) -> Result<Vec<u8>, String> {
    let body: String = pem
        .lines()
        .filter(|l| !l.trim_start().starts_with("-----"))
        .collect();
    base64::engine::general_purpose::STANDARD
        .decode(body.trim())
        .map_err(|e| format!("private_key PEM tidak bisa di-decode: {}", e))
}

pub fn now_unix() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0)
}

/// JWT service account (RS256) — header.payload.signature, base64url.
pub fn build_service_account_jwt(
    creds: &ServiceAccount,
    scope: &str,
    now: u64,
) -> Result<String, String> {
    let header = base64url(br#"{"alg":"RS256","typ":"JWT"}"#);
    let claims = serde_json::json!({
        "iss": creds.client_email,
        "scope": scope,
        "aud": DEFAULT_TOKEN_URL,
        "exp": now + 3600,
        "iat": now,
    });
    let payload = base64url(claims.to_string().as_bytes());
    let signing_input = format!("{}.{}", header, payload);
    let der = pem_to_der(&creds.private_key_pem)?;
    let key = RsaKeyPair::from_pkcs8(&der)
        .map_err(|_| "private_key bukan kunci RSA PKCS#8 yang valid".to_string())?;
    let mut sig = vec![0u8; key.public().modulus_len()];
    key.sign(
        &RSA_PKCS1_SHA256,
        &SystemRandom::new(),
        signing_input.as_bytes(),
        &mut sig,
    )
    .map_err(|_| "Gagal menandatangani JWT".to_string())?;
    Ok(format!("{}.{}", signing_input, base64url(&sig)))
}

/// Encode bagian range URL (nama tab boleh berisi spasi, dst.).
/// Karakter aman path dibiarkan; sisanya %-encode.
pub fn encode_range(range: &str) -> String {
    let mut out = String::new();
    for b in range.bytes() {
        let c = b as char;
        if c.is_ascii_alphanumeric() || "-_.!~*'():".contains(c) {
            out.push(c);
        } else {
            out.push_str(&format!("%{:02X}", b));
        }
    }
    out
}

fn cell_to_string(v: &serde_json::Value) -> String {
    match v {
        serde_json::Value::String(s) => s.clone(),
        serde_json::Value::Number(n) => {
            if let Some(i) = n.as_i64() {
                i.to_string()
            } else if let Some(f) = n.as_f64() {
                if f == f.trunc() {
                    format!("{}", f as i64)
                } else {
                    n.to_string()
                }
            } else {
                n.to_string()
            }
        }
        serde_json::Value::Bool(b) => {
            if *b {
                "TRUE".to_string()
            } else {
                "FALSE".to_string()
            }
        }
        _ => String::new(),
    }
}

#[derive(Debug, Clone, Deserialize)]
struct TokenResponse {
    access_token: String,
    #[allow(dead_code)]
    expires_in: Option<u64>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ValuesResponse {
    #[allow(dead_code)]
    pub range: Option<String>,
    #[serde(default)]
    pub values: Vec<Vec<serde_json::Value>>,
}

impl ValuesResponse {
    pub fn as_strings(&self) -> Vec<Vec<String>> {
        self.values
            .iter()
            .map(|row| row.iter().map(cell_to_string).collect())
            .collect()
    }
}

/// Client Sheets API dengan token cache (token hidup 1 jam; dipakai
/// ulang sampai kedaluwarsa − 60 detik).
pub struct SheetsClient {
    http: reqwest::blocking::Client,
    creds: ServiceAccount,
    token: Option<(String, u64)>,
    api_base: String,
    token_url: String,
}

impl SheetsClient {
    pub fn new(creds: ServiceAccount) -> Self {
        Self {
            http: reqwest::blocking::Client::builder()
                .timeout(std::time::Duration::from_secs(20))
                .build()
                .unwrap_or_else(|_| reqwest::blocking::Client::new()),
            creds,
            token: None,
            api_base: DEFAULT_API_BASE.to_string(),
            token_url: DEFAULT_TOKEN_URL.to_string(),
        }
    }

    /// Override base URL (mock server lokal saat verifikasi).
    pub fn with_bases(mut self, api_base: &str, token_url: &str) -> Self {
        self.api_base = api_base.trim_end_matches('/').to_string();
        self.token_url = token_url.to_string();
        self
    }

    pub fn access_token(&mut self) -> Result<String, String> {
        let now = now_unix();
        if let Some((tok, exp)) = &self.token {
            if now + 60 < *exp {
                return Ok(tok.clone());
            }
        }
        let jwt = build_service_account_jwt(&self.creds, SHEETS_SCOPE, now)?;
        let res = self
            .http
            .post(&self.token_url)
            .form(&[
                (
                    "grant_type",
                    "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ),
                ("assertion", jwt.as_str()),
            ])
            .send()
            .map_err(|e| format!("Token request gagal: {}", e))?;
        if !res.status().is_success() {
            return Err(format!("Token request ditolak: {}", res.status()));
        }
        let body: TokenResponse = res
            .json()
            .map_err(|e| format!("Token response tidak bisa dibaca: {}", e))?;
        let exp = now + 3600;
        self.token = Some((body.access_token.clone(), exp));
        Ok(body.access_token)
    }

    /// GET spreadsheets.values — `range` mis. "ALL DATA REGULER!A3:Z".
    pub fn get_values(
        &mut self,
        sheet_id: &str,
        range: &str,
    ) -> Result<Vec<Vec<String>>, String> {
        let token = self.access_token()?;
        let url = format!(
            "{}/v4/spreadsheets/{}/values/{}",
            self.api_base,
            sheet_id,
            encode_range(range)
        );
        let res = self
            .http
            .get(&url)
            .bearer_auth(&token)
            .send()
            .map_err(|e| format!("GET values gagal: {}", e))?;
        if !res.status().is_success() {
            return Err(format!("Sheets API error: {}", res.status()));
        }
        let body: ValuesResponse = res
            .json()
            .map_err(|e| format!("Values response tidak bisa dibaca: {}", e))?;
        Ok(body.as_strings())
    }

    /// Satu sel (POINTS — C# membaca `{SheetName}!{Col}{Row}`).
    pub fn get_cell(&mut self, sheet_id: &str, range: &str) -> Result<String, String> {
        let values = self.get_values(sheet_id, range)?;
        Ok(values
            .first()
            .and_then(|r| r.first())
            .cloned()
            .unwrap_or_default())
    }

    /// Tulis banyak sel sekaligus (values.batchUpdate).
    /// `updates`: (range A1, nilai).
    pub fn update_cells(
        &mut self,
        sheet_id: &str,
        updates: &[(String, String)],
    ) -> Result<(), String> {
        if updates.is_empty() {
            return Ok(());
        }
        let token = self.access_token()?;
        let url = format!(
            "{}/v4/spreadsheets/{}/values:batchUpdate",
            self.api_base, sheet_id
        );
        let data: Vec<serde_json::Value> = updates
            .iter()
            .map(|(range, value)| {
                serde_json::json!({
                    "range": range,
                    "majorDimension": "ROWS",
                    "values": [[value]],
                })
            })
            .collect();
        let res = self
            .http
            .post(&url)
            .bearer_auth(&token)
            .json(&serde_json::json!({
                "valueInputOption": "USER_ENTERED",
                "data": data,
            }))
            .send()
            .map_err(|e| format!("batchUpdate gagal: {}", e))?;
        if !res.status().is_success() {
            return Err(format!("Sheets batchUpdate error: {}", res.status()));
        }
        Ok(())
    }

    /// Tambah baris baru di akhir tab (values.append).
    pub fn append_row(
        &mut self,
        sheet_id: &str,
        range: &str,
        row: &[String],
    ) -> Result<(), String> {
        let token = self.access_token()?;
        let url = format!(
            "{}/v4/spreadsheets/{}/values/{}:append?valueInputOption=USER_ENTERED&insertDataOption=INSERT_ROWS",
            self.api_base,
            sheet_id,
            encode_range(range)
        );
        let res = self
            .http
            .post(&url)
            .bearer_auth(&token)
            .json(&serde_json::json!({
                "range": range,
                "majorDimension": "ROWS",
                "values": [row],
            }))
            .send()
            .map_err(|e| format!("append gagal: {}", e))?;
        if !res.status().is_success() {
            return Err(format!("Sheets append error: {}", res.status()));
        }
        Ok(())
    }
}

pub struct GoogleSheetsService;

#[allow(dead_code)]
impl GoogleSheetsService {
    /// Validasi keberadaan file credentials.json
    pub fn validate_credentials(creds_path: &str) -> Result<String, String> {
        let account = ServiceAccount::from_file(creds_path)?;
        Ok(account.client_email)
    }

    /// Format URL endpoint batch update atau fetch range Google Sheets API
    pub fn get_range_url(sheet_id: &str, sheet_name: &str, range: &str) -> String {
        format!(
            "https://sheets.googleapis.com/v4/spreadsheets/{}/values/{}!{}",
            sheet_id, sheet_name, range
        )
    }
}

// ================= Model sheet live (kolom dinamis) =================

/// Satu kolom sheet live (dibangun dari header + isi).
#[derive(Debug, Clone)]
pub struct LiveColumn {
    /// Huruf kolom (A..Z, AA..).
    pub letter: String,
    /// Nama header tampil.
    pub header: String,
    pub kind: SheetColKind,
    /// Bila true, sel di kolom ini tidak bisa diedit.
    pub locked: bool,
    /// Pilihan dropdown (hanya bila kind == Dropdown).
    pub items: Vec<String>,
    /// Lebar tampil (px).
    pub width_px: f32,
}

/// Sheet yang sudah dibaca dari API: header + baris data + kolom.
#[derive(Debug, Clone)]
pub struct LiveSheet {
    /// Header tampil (satu per kolom).
    pub headers: Vec<String>,
    /// Nomor baris header dalam values yang diambil (0-based).
    pub header_row: usize,
    /// Baris data saja (tanpa header; tiap baris sepanjang headers).
    pub rows: Vec<Vec<String>>,
    /// Indeks kolom kunci (NO FAKTUR) bila ada.
    pub key_col: Option<usize>,
    pub columns: Vec<LiveColumn>,
}

impl LiveSheet {
    pub fn empty() -> Self {
        Self {
            headers: Vec::new(),
            header_row: 0,
            rows: Vec::new(),
            key_col: None,
            columns: Vec::new(),
        }
    }

    /// Nilai kunci satu baris data (NO FAKTUR) — "" bila tidak ada.
    pub fn key_of(&self, row: &[String]) -> String {
        match self.key_col {
            Some(c) => row.get(c).cloned().unwrap_or_default(),
            None => String::new(),
        }
    }
}

/// Huruf kolom dari indeks 0-based (0=A, 25=Z, 26=AA).
pub fn column_letter(idx: usize) -> String {
    let mut n = idx + 1;
    let mut out = String::new();
    while n > 0 {
        let rem = (n - 1) % 26;
        out.insert(0, (b'A' + rem as u8) as char);
        n = (n - 1) / 26;
    }
    out
}

/// Ubah kolom live (`LiveSheet`) menjadi definisi kolom grid
/// (`sheet_model::DynColumn`) — dipakai grid Slint + fill-down /
/// clear-cells / total. Lebar: dari `column_widths` (DB
/// `Configs.Spreadsheet.ColumnWidths`, per indeks kolom) bila ada,
/// kalau tidak heuristik panjang isi (70–260px, sama seperti
/// `LiveSheet::from_values`).
pub fn to_dyn_columns(
    live: &LiveSheet,
    column_widths: Option<&[f32]>,
) -> Vec<crate::sheet_model::DynColumn> {
    use crate::sheet_model::SheetColKind as ModelKind;
    live.columns
        .iter()
        .enumerate()
        .map(|(i, c)| {
            let kind = match c.kind {
                SheetColKind::Text => ModelKind::Text,
                SheetColKind::Number => ModelKind::Number,
                SheetColKind::Dropdown => ModelKind::Dropdown,
                SheetColKind::Date => ModelKind::Date,
            };
            let width_px = column_widths
                .and_then(|w| w.get(i).copied())
                .unwrap_or(c.width_px);
            crate::sheet_model::DynColumn {
                letter: c.letter.clone(),
                header: c.header.clone(),
                kind,
                locked: c.locked,
                items: c.items.clone(),
                width_px,
            }
        })
        .collect()
}

/// Baris header = baris pertama (maks 10 baris pertama) dengan >= 2
/// sel tak-kosong dan minimal satu sel berisi huruf. Untuk ALL DATA
/// REGULER: baris 1 (judul, 1 sel) + baris 2 (kosong) dilewati,
/// baris 3 terdeteksi — tanpa hardcode.
pub fn detect_header_row(values: &[Vec<String>]) -> Option<usize> {
    for (i, row) in values.iter().take(10).enumerate() {
        let non_empty = row.iter().filter(|c| !c.trim().is_empty()).count();
        if non_empty < 2 {
            continue;
        }
        let has_letter = row
            .iter()
            .any(|c| c.chars().any(|ch| ch.is_alphabetic()));
        if has_letter {
            return Some(i);
        }
    }
    None
}

/// Jenis kolom dari kata kunci header (seperti C#).
pub fn detect_column_kind(header: &str) -> SheetColKind {
    let h = header.to_uppercase();
    if h.contains("TGL") || h.contains("DATE") || h.contains("TIME") || h.contains("DEADLINE") {
        return SheetColKind::Date;
    }
    if h.contains("EDITOR")
        || h.contains("STATUS")
        || h.contains("SELEKSI")
        || h.contains("HKS")
        || h.contains("NR")
    {
        return SheetColKind::Dropdown;
    }
    if h.contains("QTY") || h.contains("POIN") || h.contains("JUMLAH") {
        return SheetColKind::Number;
    }
    SheetColKind::Text
}

/// Lebar kolom: dari Configs.Spreadsheet.ColumnWidths bila ada, kalau
/// tidak dari panjang isi (header + 50 baris pertama), dijepit 70..260.
fn column_width_px(
    idx: usize,
    header: &str,
    rows: &[Vec<String>],
    widths: &HashMap<usize, f32>,
) -> f32 {
    if let Some(w) = widths.get(&idx) {
        if *w >= 30.0 {
            return w.clamp(40.0, 400.0);
        }
    }
    let mut max_len = header.chars().count();
    for row in rows.iter().take(50) {
        if let Some(cell) = row.get(idx) {
            max_len = max_len.max(cell.chars().count().min(60));
        }
    }
    (56.0 + 7.5 * max_len as f32).clamp(70.0, 260.0)
}

/// Bangun model live dari values API (range penuh, mis. A1:Z).
/// `widths`: Configs.Spreadsheet.ColumnWidths (indeks -> px).
pub fn parse_live_sheet(
    values: Vec<Vec<String>>,
    widths: &HashMap<usize, f32>,
) -> LiveSheet {
    let Some(header_idx) = detect_header_row(&values) else {
        return LiveSheet::empty();
    };
    let mut header_row = values[header_idx].clone();
    // Pangkas sel kosong di ekor baris header — ekspor xlsx mengisi
    // baris sampai lebar dimensi sheet (mis. 26 sel) padahal kolom
    // aslinya 20 (A–T); tanpa ini kolom "Column 21.." ikut terbentuk.
    // API Sheets asli memangkas ekor kosong yang sama.
    while header_row
        .last()
        .map(|h| h.trim().is_empty())
        .unwrap_or(false)
    {
        header_row.pop();
    }
    let col_count = header_row.len().max(1);
    let mut headers: Vec<String> = header_row
        .iter()
        .enumerate()
        .map(|(i, h)| {
            let t = h.trim();
            if t.is_empty() {
                format!("Column {}", i + 1)
            } else {
                t.to_string()
            }
        })
        .collect();
    // Header duplikat (ALL DATA REGULER punya dua "TGL DONE") — beri
    // penanda supaya nama kolom tetap unik di UI.
    let mut seen: HashMap<String, usize> = HashMap::new();
    for h in headers.iter_mut() {
        let n = seen.entry(h.clone()).or_insert(0);
        *n += 1;
        if *n > 1 {
            *h = format!("{} ({})", h, n);
        }
    }
    let mut rows: Vec<Vec<String>> = values
        .iter()
        .skip(header_idx + 1)
        .map(|row| {
            let mut r: Vec<String> = row.clone();
            r.resize(col_count, String::new());
            r
        })
        .collect();
    // Pangkas baris kosong di ekor (sheet sering punya ribuan baris
    // kosong di bawah data).
    while rows
        .last()
        .map(|r| r.iter().all(|c| c.trim().is_empty()))
        .unwrap_or(false)
    {
        rows.pop();
    }
    let key_col = headers
        .iter()
        .position(|h| h.to_uppercase().contains("FAKTUR"));
    let columns: Vec<LiveColumn> = headers
        .iter()
        .enumerate()
        .map(|(i, header)| {
            let kind = detect_column_kind(header);
            let is_key = Some(i) == key_col;
            let is_order_time = header.to_uppercase().contains("ORDER TIME");
            let items = if kind == SheetColKind::Dropdown {
                let mut seen: Vec<String> = Vec::new();
                for row in rows.iter() {
                    if let Some(v) = row.get(i) {
                        let t = v.trim();
                        if !t.is_empty() && !seen.iter().any(|s| s == t) {
                            seen.push(t.to_string());
                            if seen.len() >= 30 {
                                break;
                            }
                        }
                    }
                }
                seen
            } else {
                Vec::new()
            };
            LiveColumn {
                letter: column_letter(i),
                header: header.clone(),
                kind,
                locked: is_key || is_order_time,
                items,
                width_px: column_width_px(i, header, &rows, widths),
            }
        })
        .collect();
    LiveSheet {
        headers,
        header_row: header_idx,
        rows,
        key_col,
        columns,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use ring::signature::{UnparsedPublicKey, RSA_PKCS1_2048_8192_SHA256};

    // Kunci RSA uji — dibuat khusus untuk test ini (openssl genpkey),
    // BUKAN kredensial siapa pun. Dipakai membuktikan JWT RS256
    // terbentuk + tanda tangannya valid.
    const TEST_PRIVATE_KEY_PEM: &str = "-----BEGIN PRIVATE KEY-----
MIIEvgIBADANBgkqhkiG9w0BAQEFAASCBKgwggSkAgEAAoIBAQC1sMaE58Ab5u/4
Kk4gjMtdp8KRd4+mIY3V7dJQCY/drltGR8QDe7INs7SnxjrbFPlzk+aWCaR+ffSM
QnM9kLpNfVlXivwjF1Ro4zCqp+CEFIdMthxbrvO03Ir4P6pf45GUgaDx2K7cP10G
BwunLcG9fWDaglmU0jORY2AxHJy7IzsGmSpgG6do6WUhMGaL8AgkzEk2UwtQxLGv
/h4fzSuvVmScNQq0KoCsBQ/PiTtQ/B0Mquyu4K8OmI0FyFVcRatJI9iF3vHQDfth
XYqrKfMojM6wwp0XAO7/QH5z5JAKbtDkS0bUYqlKULILFsAmxa6E0Zw+c6e3Z5/i
bVVAZsb5AgMBAAECggEAAoPzW6exuzEpUAWV61FyXKuT6rD9u3kUyPTFR/e57Cxx
IGSh9sJhprA+52ERM4ieI4GABxe/FUPn0iv2rdAxJB2A0nt0HjBBKj/q+TyLGTOy
OSz+8NitOxXKlBadjzz2qQMFGBQTPQK2O6fL+4E2AMpEQFeJp0vPDEuUlgQlZK8u
2YEKFbqfChj2mrZtRwq6ccH49Fv8uc7c30C1QuHTlLKFf3wnn6am9EcC9VuqSnrY
8tkEHmK8f76wuGcj9SSEM5FL6ndZ6uDGwoWocGFHg1Cs7XABAJA7sDv/ihhgz/X7
tIMMOV9RmiITHnGpN/UT8cLp+3gO2Ro/kitD794UeQKBgQDtcQkMjn5vE3aX8RVo
VZijzjLDpcYSrVRgjIuFEU8X7kZJXgE2Re5pCzbEy3hCHVqrfpQKEcawjxHFwQeQ
GE6gS1tbbyW82opSMDSToJhStVV6Ac7TtJEcC7AbSA/8rBRCoggjauKB6TnPrnqx
vq99CwOtheTsohxnNzv+XgB+DwKBgQDD5DgD1S1GDNecj+6a2912QiroaPkCSgVJ
NK7U/LFMCRM/u2KKkDPVWAWe/a7ixvcwXxM6DzkoHrJ5RqxauQQEwqyboJ4DPJt2
mpCF+/oNAJyliIF822kracb/1GN2Hi0hZioaE6tUTQ3g5hOX0zfj7l7W6aO1TQmS
1mg72t7ydwKBgQDC15X4Ytnnpk4UIvZRNqRaDvPjTp56ZxetXseCu5Bu781nmSdf
dQVN17DqzpjxkAr+XZF3rdygt9Z6s4rUOM4UhaIDJvMNxTvqc7pBRnCYtkrdIqyp
VywYmpK54GmWC1NY1oPdJHGY71zMSr5OK07wSmCLPSt87w5luJKXimXXCwKBgQC+
YfmG0BKWODj88m4Fl5XfL2OgSRVpgiMtCMS6P3TY3yEUGbwlywx1VnzwDsF/qbvo
CR8YmF8GfP8cGvG7ooEjV0iGYE3x+19kn8fCUCivpl6YCtk+Tcgi50T+sp6P17go
87ALc/5ZiiHo5/M25ZM2NR97Y8s1k0+wpCzdr1Rq9QKBgCnraUWTA1a2AtW4/0TH
9z2KuaqKnxxr0zh3LPY9k70Wblz9+An+++vish8zdVaH+g4mW8KKUmbGalUDcCsC
ZZDFFFod4u1pVyVauFAHy69yDwCG6GgsD8npRzS2pEjeqn1jgnZbubuK/W0saYs6
QUdHNUacLZwJw8fCCvjWHs4R
-----END PRIVATE KEY-----";
    // Kunci publik uji dalam format RSAPublicKey mentah (DER SEQUENCE n,e)
    // — BUKAN SubjectPublicKeyInfo: ring 0.17 UnparsedPublicKey::verify
    // untuk RSA mengharapkan format ini (lihat docs ring::signature).
    const TEST_PUBLIC_KEY_RSA_DER_B64: &str = "MIIBCgKCAQEAtbDGhOfAG+bv+CpOIIzLXafCkXePpiGN1e3SUAmP3a5bRkfEA3uyDbO0p8Y62xT5c5Pmlgmkfn30jEJzPZC6TX1ZV4r8IxdUaOMwqqfghBSHTLYcW67ztNyK+D+qX+ORlIGg8diu3D9dBgcLpy3BvX1g2oJZlNIzkWNgMRycuyM7BpkqYBunaOllITBmi/AIJMxJNlMLUMSxr/4eH80rr1ZknDUKtCqArAUPz4k7UPwdDKrsruCvDpiNBchVXEWrSSPYhd7x0A37YV2KqynzKIzOsMKdFwDu/0B+c+SQCm7Q5EtG1GKpSlCyCxbAJsWuhNGcPnOnt2ef4m1VQGbG+QIDAQAB";

    #[test]
    fn jwt_rs256_builds_and_verifies() {
        let creds = ServiceAccount {
            client_email: "test@project.iam.gserviceaccount.com".to_string(),
            private_key_pem: TEST_PRIVATE_KEY_PEM.to_string(),
        };
        let now = 1794000000u64;
        let jwt = build_service_account_jwt(&creds, SHEETS_SCOPE, now).expect("JWT harus jadi");
        let parts: Vec<&str> = jwt.split('.').collect();
        assert_eq!(parts.len(), 3);
        let header = String::from_utf8(
            URL_SAFE_NO_PAD.decode(parts[0]).expect("header base64url"),
        )
        .expect("header utf8");
        assert!(header.contains("\"RS256\""));
        let payload = String::from_utf8(
            URL_SAFE_NO_PAD.decode(parts[1]).expect("payload base64url"),
        )
        .expect("payload utf8");
        assert!(payload.contains("test@project.iam.gserviceaccount.com"));
        assert!(payload.contains(SHEETS_SCOPE));
        assert!(payload.contains("https://oauth2.googleapis.com/token"));
        assert!(payload.contains("\"exp\":1794003600"));
        assert!(payload.contains("\"iat\":1794000000"));
        // Tanda tangan valid menurut kunci publik pasangan kunci uji.
        let spki = base64::engine::general_purpose::STANDARD
            .decode(TEST_PUBLIC_KEY_RSA_DER_B64)
            .expect("spki base64");
        let sig = URL_SAFE_NO_PAD.decode(parts[2]).expect("sig base64url");
        let signing_input = format!("{}.{}", parts[0], parts[1]);
        let pubkey = UnparsedPublicKey::new(&RSA_PKCS1_2048_8192_SHA256, spki);
        pubkey
            .verify(signing_input.as_bytes(), &sig)
            .expect("tanda tangan RS256 harus valid");
    }

    #[test]
    fn service_account_json_parsing() {
        let json = serde_json::json!({
            "type": "service_account",
            "client_email": "a@b.iam.gserviceaccount.com",
            "private_key": "-----BEGIN PRIVATE KEY-----\nxxx\n-----END PRIVATE KEY-----\n",
        })
        .to_string();
        let sa = ServiceAccount::from_json(&json).expect("parse");
        assert_eq!(sa.client_email, "a@b.iam.gserviceaccount.com");
        assert!(ServiceAccount::from_json("{}").is_err());
        assert!(ServiceAccount::from_json("bukan json").is_err());
    }

    #[test]
    fn encode_range_keeps_safe_chars() {
        assert_eq!(
            encode_range("ALL DATA REGULER!A3:Z"),
            "ALL%20DATA%20REGULER!A3:Z"
        );
        assert_eq!(encode_range("EDITOR HARIAN!J14"), "EDITOR%20HARIAN!J14");
    }

    #[test]
    fn column_letter_sequence() {
        assert_eq!(column_letter(0), "A");
        assert_eq!(column_letter(25), "Z");
        assert_eq!(column_letter(26), "AA");
        assert_eq!(column_letter(27), "AB");
        assert_eq!(column_letter(51), "AZ");
        assert_eq!(column_letter(52), "BA");
    }

    #[test]
    fn header_detection_skips_title_and_empty_rows() {
        // ALL DATA REGULER: baris 1 judul (1 sel), baris 2 kosong,
        // baris 3 header — tanpa hardcode baris 3.
        let values = vec![
            vec!["PT DELAPAN MATA AIR".to_string(), "".into(), "".into()],
            vec!["".to_string(), "".into(), "".into()],
            vec![
                "NO FAKTUR".to_string(),
                "ORDER TIME".into(),
                "PIC".into(),
            ],
            vec!["6CCC26070100003004".into(), "01/07/2026".into(), "nunuy".into()],
        ];
        assert_eq!(detect_header_row(&values), Some(2));
        // EDITOR HARIAN: header langsung baris 1.
        let values2 = vec![vec![
            "NAMA SEKOLAH".into(),
            "MARKETING".into(),
            "ITEM".into(),
            "QTY".into(),
        ]];
        assert_eq!(detect_header_row(&values2), Some(0));
        // Tidak ada baris header: None.
        let values3 = vec![vec!["satu".into()], vec!["dua".into()]];
        assert_eq!(detect_header_row(&values3), None);
    }

    #[test]
    fn column_kind_keywords_like_csharp() {
        assert_eq!(detect_column_kind("TGL DONE"), SheetColKind::Date);
        assert_eq!(detect_column_kind("ORDER TIME"), SheetColKind::Date);
        assert_eq!(detect_column_kind("EDITOR"), SheetColKind::Dropdown);
        assert_eq!(detect_column_kind("STATUS"), SheetColKind::Dropdown);
        // "SELEKTOR" TIDAK mengandung "SELEKSI" — C# pun
        // (Contains("SELEKSI")) memperlakukannya sebagai teks.
        assert_eq!(detect_column_kind("SELEKTOR"), SheetColKind::Text);
        assert_eq!(detect_column_kind("QTY"), SheetColKind::Number);
        assert_eq!(detect_column_kind("QTY POIN"), SheetColKind::Number);
        assert_eq!(detect_column_kind("CUSTOMER NAME"), SheetColKind::Text);
        assert_eq!(detect_column_kind("NO FAKTUR"), SheetColKind::Text);
    }

    #[test]
    fn parse_live_sheet_all_data_reguler() {
        let values = vec![
            vec!["PT DELAPAN MATA AIR".into(), "".into(), "".into(), "".into()],
            vec!["".into(), "".into(), "".into(), "".into()],
            vec![
                "NO FAKTUR".into(),
                "ORDER TIME".into(),
                "PIC".into(),
                "QTY".into(),
            ],
            vec!["6CCC26070100003004".into(), "01/07/2026 13:06".into(), "nunuy".into(), "72".into()],
            vec!["6CCC26072200003005".into(), "22/07/2026 17:08".into(), "nunuy".into(), "47".into()],
        ];
        let sheet = parse_live_sheet(values, &HashMap::new());
        assert_eq!(sheet.header_row, 2);
        assert_eq!(
            sheet.headers,
            vec!["NO FAKTUR", "ORDER TIME", "PIC", "QTY"]
        );
        assert_eq!(sheet.rows.len(), 2);
        assert_eq!(sheet.key_col, Some(0));
        assert_eq!(sheet.key_of(&sheet.rows[0]), "6CCC26070100003004");
        // NO FAKTUR terkunci, ORDER TIME terkunci, PIC teks, QTY angka.
        assert!(sheet.columns[0].locked);
        assert!(sheet.columns[1].locked);
        assert_eq!(sheet.columns[1].kind, SheetColKind::Date);
        assert!(!sheet.columns[2].locked);
        assert_eq!(sheet.columns[3].kind, SheetColKind::Number);
        assert_eq!(sheet.columns[0].letter, "A");
        assert_eq!(sheet.columns[3].letter, "D");
        // Dropdown: nilai berbeda dari kolom.
        let values2 = vec![
            vec!["EDITOR".into(), "STATUS".into()],
            vec!["CAESAR".into(), "DONE".into()],
            vec!["NISSA".into(), "DONE".into()],
            vec!["CAESAR".into(), "PROSES".into()],
        ];
        let sheet2 = parse_live_sheet(values2, &HashMap::new());
        assert_eq!(sheet2.columns[0].kind, SheetColKind::Dropdown);
        assert_eq!(sheet2.columns[0].items, vec!["CAESAR", "NISSA"]);
        assert_eq!(sheet2.columns[1].items, vec!["DONE", "PROSES"]);
    }

    #[test]
    fn parse_live_sheet_trims_trailing_empty_header_cells() {
        // Ekspor xlsx mengisi baris header sampai lebar dimensi sheet
        // (26 sel) padahal kolom aslinya 20 — ekor kosong harus
        // dipangkas supaya grid menampilkan 20 kolom, bukan 26.
        let values = vec![
            vec!["PT DELAPAN MATA AIR".into(), "".into(), "".into(), "".into(), "".into()],
            vec!["".into(), "".into(), "".into(), "".into(), "".into()],
            vec![
                "NO FAKTUR".into(),
                "ORDER TIME".into(),
                "PIC".into(),
                "".into(),
                "".into(),
            ],
            vec![
                "6CCC26070100003004".into(),
                "01/07/2026 13:06".into(),
                "nunuy".into(),
                "".into(),
                "".into(),
            ],
        ];
        let sheet = parse_live_sheet(values, &HashMap::new());
        assert_eq!(sheet.headers, vec!["NO FAKTUR", "ORDER TIME", "PIC"]);
        assert_eq!(sheet.columns.len(), 3);
        assert_eq!(sheet.rows[0].len(), 3);
    }

    #[test]
    fn cell_to_string_numbers() {
        let v: ValuesResponse = serde_json::from_str(
            r#"{"range":"A1:B2","values":[["72.0",72],["TRUE",true],["x",null]]}"#,
        )
        .expect("parse");
        let rows = v.as_strings();
        assert_eq!(rows[0][0], "72.0");
        assert_eq!(rows[0][1], "72");
        assert_eq!(rows[1][0], "TRUE");
        assert_eq!(rows[1][1], "TRUE");
        assert_eq!(rows[2][0], "x");
        assert_eq!(rows[2][1], "");
    }
}
