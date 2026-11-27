use serde::{Deserialize, Serialize};
use std::fs;
use std::path::Path;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[allow(dead_code)]
pub struct SheetCellUpdate {
    pub sheet_name: String,
    pub row: i32,
    pub col: String,
    pub value: String,
}

pub struct GoogleSheetsService;

#[allow(dead_code)]
impl GoogleSheetsService {
    /// Validasi keberadaan file credentials.json
    pub fn validate_credentials(creds_path: &str) -> Result<String, String> {
        let p = Path::new(creds_path);
        if !p.exists() {
            return Err(format!(
                "File credentials tidak ditemukan di: {}",
                creds_path
            ));
        }

        let content = fs::read_to_string(p).map_err(|e| format!("Gagal membaca file: {}", e))?;
        if let Ok(json) = serde_json::from_str::<serde_json::Value>(&content) {
            let client_email = json
                .get("client_email")
                .and_then(|v| v.as_str())
                .unwrap_or("service_account");
            Ok(client_email.to_string())
        } else {
            Err("Format file credentials.json tidak valid".to_string())
        }
    }

    /// Format URL endpoint batch update atau fetch range Google Sheets API
    pub fn get_range_url(sheet_id: &str, sheet_name: &str, range: &str) -> String {
        format!(
            "https://sheets.googleapis.com/v4/spreadsheets/{}/values/{}!{}",
            sheet_id, sheet_name, range
        )
    }
}
