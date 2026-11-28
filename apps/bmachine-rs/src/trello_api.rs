use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
#[allow(dead_code)]
pub struct TrelloBoardItem {
    pub id: String,
    pub name: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[allow(dead_code)]
pub struct TrelloListItem {
    pub id: String,
    pub name: String,
    pub id_board: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[allow(dead_code)]
pub struct TrelloLabel {
    pub name: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[allow(dead_code)]
pub struct TrelloBadges {
    pub attachments: Option<i64>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[allow(dead_code)]
pub struct TrelloCardItem {
    pub id: String,
    pub name: String,
    pub desc: Option<String>,
    pub due: Option<String>,
    #[serde(rename = "shortUrl")]
    pub short_url: Option<String>,
    pub labels: Option<Vec<TrelloLabel>>,
    pub badges: Option<TrelloBadges>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TrelloMember {
    #[serde(rename = "fullName")]
    pub full_name: Option<String>,
    pub username: String,
}

/// Base URL bawaan: API Trello asli. Trello kantor diganti lewat
/// Settings (key DB `Trello.BaseUrl`) — cukup ganti base URL + API
/// Key + Token, tanpa rombak dan tanpa rebuild (lihat
/// ~/workspace/your_files/spesifikasi-api-trello-kantor-untuk-bmachine.md).
pub const DEFAULT_BASE_URL: &str = "https://api.trello.com/1";

/// Interval auto-refresh Trello bawaan (detik) — sama dengan C#
/// (`Settings.Interval.Editing/Revision/Late`, default 60).
pub const DEFAULT_REFRESH_INTERVAL_SECS: u64 = 60;
/// Interval auto-refresh Trello minimum (detik) — di bawah ini
/// dijepit ke 30 supaya API tidak dihujani request.
pub const MIN_REFRESH_INTERVAL_SECS: u64 = 30;
/// Interval auto-refresh Trello maksimum (detik) — 1 jam.
pub const MAX_REFRESH_INTERVAL_SECS: u64 = 3600;

/// Parse interval auto-refresh Trello (detik) dari string Settings
/// (key DB `Trello.RefreshInterval`, satu nilai untuk ketiga list).
/// Tidak valid / kosong -> default 60; di bawah 30 -> 30; di atas
/// 3600 -> 3600.
pub fn parse_refresh_interval_seconds(raw: &str) -> u64 {
    let secs: u64 = raw.trim().parse().unwrap_or(DEFAULT_REFRESH_INTERVAL_SECS);
    secs.clamp(
        MIN_REFRESH_INTERVAL_SECS,
        MAX_REFRESH_INTERVAL_SECS,
    )
}

/// Jumlah list Trello yang di-sync port (EDITING / REVISION / LATE).
/// QC / ACC ada key-nya di DB tapi tidak di-fetch port (lihat
/// DESIGN_SYSTEM.md §15).
pub const TRELLO_SYNC_LIST_COUNT: usize = 3;

/// Status sinkronisasi Trello per list — dipakai timer auto-refresh
/// (1 detik, ala C# DashboardViewModel: tick 1 detik, cek elapsed
/// per list) + refresh manual, supaya tidak ada dua fetch berjalan
/// untuk list yang sama (`if (!IsRefreshing)` di C#).
///
/// `last_sync` di-set saat fetch DIMULAI (ala C#: `_lastEditingSync
/// = DateTime.Now` sebelum `RefreshCommand` jalan) — jadi refresh
/// manual ikut me-reset timer auto-refresh list itu.
pub struct TrelloSyncState {
    /// true = fetch list ini sedang berjalan (thread).
    in_flight: [std::sync::atomic::AtomicBool; TRELLO_SYNC_LIST_COUNT],
    /// Kapan fetch list ini terakhir dimulai.
    last_sync: [std::sync::Mutex<std::time::Instant>; TRELLO_SYNC_LIST_COUNT],
}

impl TrelloSyncState {
    pub fn new() -> Self {
        let now = std::time::Instant::now();
        Self {
            in_flight: [
                std::sync::atomic::AtomicBool::new(false),
                std::sync::atomic::AtomicBool::new(false),
                std::sync::atomic::AtomicBool::new(false),
            ],
            last_sync: [
                std::sync::Mutex::new(now),
                std::sync::Mutex::new(now),
                std::sync::Mutex::new(now),
            ],
        }
    }

    /// true bila fetch list `idx` sedang berjalan.
    pub fn is_running(&self, idx: usize) -> bool {
        self.in_flight[idx].load(std::sync::atomic::Ordering::SeqCst)
    }

    /// true bila sudah `interval` sejak fetch list `idx` terakhir
    /// dimulai (dipakai timer 1 detik untuk memutuskan list mana yang
    /// jatuh tempo).
    pub fn is_due(&self, idx: usize, interval: std::time::Duration) -> bool {
        match self.last_sync[idx].lock() {
            Ok(t) => t.elapsed() >= interval,
            Err(_) => true,
        }
    }

    /// Coba klaim list `idx` untuk fetch: false bila fetch list itu
    /// masih berjalan (tick di-skip, ala C# `if (!IsRefreshing)`).
    /// Berhasil -> `last_sync` di-set ke sekarang.
    pub fn try_begin(&self, idx: usize) -> bool {
        if self.in_flight[idx]
            .compare_exchange(
                false,
                true,
                std::sync::atomic::Ordering::SeqCst,
                std::sync::atomic::Ordering::SeqCst,
            )
            .is_err()
        {
            return false;
        }
        if let Ok(mut t) = self.last_sync[idx].lock() {
            *t = std::time::Instant::now();
        }
        true
    }

    /// Lepas klaim list `idx` (dipanggil thread fetch saat selesai —
    /// sukses maupun gagal).
    pub fn finish(&self, idx: usize) {
        self.in_flight[idx].store(false, std::sync::atomic::Ordering::SeqCst);
    }
}

impl Default for TrelloSyncState {
    fn default() -> Self {
        Self::new()
    }
}

/// Normalisasi base URL dari Settings: trim, kosong -> default,
/// buang garis miring di akhir (supaya `endpoint()` tidak dobel `/`).
pub fn normalize_base_url(raw: &str) -> String {
    let t = raw.trim().trim_end_matches('/');
    if t.is_empty() {
        DEFAULT_BASE_URL.to_string()
    } else {
        t.to_string()
    }
}

/// Gabung base URL + path endpoint, misal
/// `endpoint("http://127.0.0.1:8080", "/lists/x/cards")`.
fn endpoint(base_url: &str, path: &str) -> String {
    format!("{}{}", normalize_base_url(base_url), path)
}

/// Origin web UI board dari base URL API — dipakai untuk membangun
/// link kartu saat API tidak mengembalikan `shortUrl` (misal kartu
/// manual yang ditempel hanya ID-nya).
/// Trello asli: API di `api.trello.com`, web di `trello.com`.
/// Trello kantor diasumsikan melayani web UI di origin yang sama
/// dengan API-nya.
pub fn web_origin(base_url: &str) -> String {
    let base = normalize_base_url(base_url);
    if base == DEFAULT_BASE_URL {
        return "https://trello.com".to_string();
    }
    // Ambil scheme://host[:port] — buang semua path sesudahnya.
    let after_scheme = match base.find("://") {
        Some(i) => &base[i + 3..],
        None => base.as_str(),
    };
    let host = after_scheme.split('/').next().unwrap_or("");
    let scheme = if base.starts_with("http://") {
        "http://"
    } else {
        "https://"
    };
    format!("{}{}", scheme, host)
}

/// Link web satu kartu, misal `https://trello.com/c/<id>`.
pub fn web_url_for_card(base_url: &str, card_id: &str) -> String {
    format!("{}/c/{}", web_origin(base_url), card_id.trim())
}

pub struct TrelloApiService;

impl TrelloApiService {
    #[allow(dead_code)]
    pub fn fetch_boards(
        base_url: &str,
        api_key: &str,
        token: &str,
    ) -> Result<Vec<TrelloBoardItem>, String> {
        let client = reqwest::blocking::Client::builder()
            .timeout(std::time::Duration::from_secs(10))
            .build()
            .map_err(|e| e.to_string())?;

        let url = format!(
            "{}?key={}&token={}&fields=name,id",
            endpoint(base_url, "/members/me/boards"),
            api_key, token
        );

        let res = client
            .get(&url)
            .send()
            .map_err(|e| format!("HTTP request gagal: {}", e))?;
        if !res.status().is_success() {
            return Err(format!("Trello API error: {}", res.status()));
        }

        let boards: Vec<TrelloBoardItem> =
            res.json().map_err(|e| format!("JSON parse error: {}", e))?;
        Ok(boards)
    }

    #[allow(dead_code)]
    pub fn fetch_lists(
        base_url: &str,
        api_key: &str,
        token: &str,
        board_id: &str,
    ) -> Result<Vec<TrelloListItem>, String> {
        let client = reqwest::blocking::Client::builder()
            .timeout(std::time::Duration::from_secs(10))
            .build()
            .map_err(|e| e.to_string())?;

        let url = format!(
            "{}?key={}&token={}&fields=name,id,idBoard",
            endpoint(base_url, &format!("/boards/{}/lists", board_id)),
            api_key, token
        );

        let res = client
            .get(&url)
            .send()
            .map_err(|e| format!("HTTP request gagal: {}", e))?;
        if !res.status().is_success() {
            return Err(format!("Trello API error: {}", res.status()));
        }

        let lists: Vec<TrelloListItem> =
            res.json().map_err(|e| format!("JSON parse error: {}", e))?;
        Ok(lists)
    }

    pub fn verify_connection(
        base_url: &str,
        api_key: &str,
        token: &str,
    ) -> Result<TrelloMember, String> {
        let client = reqwest::blocking::Client::builder()
            .timeout(std::time::Duration::from_secs(10))
            .build()
            .map_err(|e| e.to_string())?;

        let url = format!(
            "{}?key={}&token={}&fields=fullName,username",
            endpoint(base_url, "/members/me"),
            api_key.trim(),
            token.trim()
        );

        let res = client
            .get(&url)
            .send()
            .map_err(|e| format!("HTTP request gagal: {}", e))?;
        if !res.status().is_success() {
            return Err(format!("Trello auth gagal ({})", res.status()));
        }
        res.json::<TrelloMember>()
            .map_err(|e| format!("JSON parse error: {}", e))
    }

    pub fn fetch_cards(
        base_url: &str,
        api_key: &str,
        token: &str,
        list_id: &str,
    ) -> Result<Vec<TrelloCardItem>, String> {
        let client = reqwest::blocking::Client::builder()
            .timeout(std::time::Duration::from_secs(10))
            .build()
            .map_err(|e| e.to_string())?;

        let url = format!(
            "{}?key={}&token={}&fields=name,desc,due,id,shortUrl,labels,badges",
            endpoint(base_url, &format!("/lists/{}/cards", list_id.trim())),
            api_key.trim(),
            token.trim()
        );

        let res = client
            .get(&url)
            .send()
            .map_err(|e| format!("HTTP request gagal: {}", e))?;
        if !res.status().is_success() {
            return Err(format!("Trello API error: {}", res.status()));
        }

        let cards: Vec<TrelloCardItem> =
            res.json().map_err(|e| format!("JSON parse error: {}", e))?;
        Ok(cards)
    }

    #[allow(dead_code)]
    pub fn add_comment(
        base_url: &str,
        api_key: &str,
        token: &str,
        card_id: &str,
        text: &str,
    ) -> Result<(), String> {
        let client = reqwest::blocking::Client::builder()
            .timeout(std::time::Duration::from_secs(10))
            .build()
            .map_err(|e| e.to_string())?;
        let url = format!(
            "{}?key={}&token={}",
            endpoint(
                base_url,
                &format!("/cards/{}/actions/comments", card_id.trim())
            ),
            api_key.trim(),
            token.trim()
        );
        let res = client
            .post(&url)
            .form(&[("text", text)])
            .send()
            .map_err(|e| format!("HTTP request gagal: {}", e))?;
        if !res.status().is_success() {
            return Err(format!("Gagal kirim komentar: {}", res.status()));
        }
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn normalize_empty_or_blank_uses_default() {
        assert_eq!(normalize_base_url(""), DEFAULT_BASE_URL);
        assert_eq!(normalize_base_url("   "), DEFAULT_BASE_URL);
    }

    #[test]
    fn normalize_trims_and_strips_trailing_slashes() {
        assert_eq!(
            normalize_base_url("  http://127.0.0.1:8080/  "),
            "http://127.0.0.1:8080"
        );
        assert_eq!(
            normalize_base_url("https://api.trello.com/1/"),
            "https://api.trello.com/1"
        );
    }

    #[test]
    fn endpoint_joins_custom_base_without_double_slash() {
        assert_eq!(
            endpoint("http://127.0.0.1:8080/", "/lists/abc/cards"),
            "http://127.0.0.1:8080/lists/abc/cards"
        );
        assert_eq!(
            endpoint("https://trello.kantor.local/api/v1", "/members/me"),
            "https://trello.kantor.local/api/v1/members/me"
        );
        assert_eq!(
            endpoint("", "/members/me"),
            "https://api.trello.com/1/members/me"
        );
    }

    #[test]
    fn web_origin_trello_is_trello_com() {
        assert_eq!(web_origin("https://api.trello.com/1"), "https://trello.com");
        assert_eq!(web_origin(""), "https://trello.com");
    }

    #[test]
    fn web_origin_custom_base_uses_same_origin() {
        assert_eq!(
            web_origin("https://trello.kantor.local/api/v1"),
            "https://trello.kantor.local"
        );
        assert_eq!(
            web_origin("http://127.0.0.1:8080/1"),
            "http://127.0.0.1:8080"
        );
    }

    #[test]
    fn web_url_for_card_builds_card_link() {
        assert_eq!(
            web_url_for_card("https://api.trello.com/1", "abc123"),
            "https://trello.com/c/abc123"
        );
        assert_eq!(
            web_url_for_card("http://127.0.0.1:8080", "abc123"),
            "http://127.0.0.1:8080/c/abc123"
        );
    }

    #[test]
    fn parse_refresh_interval_defaults_and_clamps() {
        assert_eq!(parse_refresh_interval_seconds("60"), 60);
        assert_eq!(parse_refresh_interval_seconds("30"), 30);
        assert_eq!(parse_refresh_interval_seconds("120"), 120);
        // Di bawah minimum -> dijepit ke 30.
        assert_eq!(parse_refresh_interval_seconds("10"), 30);
        assert_eq!(parse_refresh_interval_seconds("0"), 30);
        // Tidak valid / kosong -> default 60.
        assert_eq!(parse_refresh_interval_seconds(""), 60);
        assert_eq!(parse_refresh_interval_seconds("abc"), 60);
        assert_eq!(parse_refresh_interval_seconds("  "), 60);
        // Spasi di sekitar angka ditoleransi; di atas maksimum -> 3600.
        assert_eq!(parse_refresh_interval_seconds("  90 "), 90);
        assert_eq!(parse_refresh_interval_seconds("99999"), 3600);
    }

    #[test]
    fn sync_state_try_begin_skips_while_running() {
        let st = TrelloSyncState::new();
        // List 0 bebas, list lain tidak terpengaruh.
        assert!(!st.is_running(0));
        assert!(st.try_begin(0));
        assert!(st.is_running(0));
        // Klaim kedua selagi berjalan -> ditolak (skip tick).
        assert!(!st.try_begin(0));
        assert!(st.is_running(1) == false);
        assert!(st.try_begin(1));
        // Selesai -> bisa diklaim lagi.
        st.finish(0);
        assert!(!st.is_running(0));
        assert!(st.try_begin(0));
        st.finish(0);
        st.finish(1);
    }

    #[test]
    fn sync_state_is_due_follows_elapsed() {
        let st = TrelloSyncState::new();
        // Baru dibuat: belum jatuh tempo untuk interval besar...
        assert!(!st.is_due(0, std::time::Duration::from_secs(3600)));
        // ...tapi jatuh tempo untuk interval 0.
        assert!(st.is_due(0, std::time::Duration::from_secs(0)));
        // try_begin me-reset last_sync -> belum jatuh tempo lagi.
        assert!(st.try_begin(0));
        assert!(!st.is_due(0, std::time::Duration::from_secs(3600)));
        st.finish(0);
    }

    #[test]
    fn add_comment_posts_to_custom_base_url() {
        // Bukti end-to-end (tanpa jaringan luar): server TCP lokal
        // mencatat request mentah, lalu add_comment harus POST ke
        // base URL custom itu — bukan ke api.trello.com.
        use std::io::{Read, Write};
        use std::net::TcpListener;

        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let port = listener.local_addr().unwrap().port();
        let base = format!("http://127.0.0.1:{}/api", port);

        let handle = std::thread::spawn(move || {
            let (mut stream, _) = listener.accept().unwrap();
            stream
                .set_read_timeout(Some(std::time::Duration::from_secs(5)))
                .unwrap();
            let mut buf = Vec::new();
            let mut chunk = [0u8; 1024];
            loop {
                let n = stream.read(&mut chunk).unwrap();
                if n == 0 {
                    break;
                }
                buf.extend_from_slice(&chunk[..n]);
                if let Some(pos) = buf.windows(4).position(|w| w == b"\r\n\r\n") {
                    let header = String::from_utf8_lossy(&buf[..pos]).to_string();
                    let len: usize = header
                        .lines()
                        .find_map(|l| {
                            l.to_ascii_lowercase()
                                .strip_prefix("content-length:")
                                .and_then(|v| v.trim().parse().ok())
                        })
                        .unwrap_or(0);
                    let total = pos + 4 + len;
                    while buf.len() < total {
                        let n = stream.read(&mut chunk).unwrap();
                        if n == 0 {
                            break;
                        }
                        buf.extend_from_slice(&chunk[..n]);
                    }
                    break;
                }
            }
            let req = String::from_utf8_lossy(&buf).to_string();
            let resp =
                b"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\n\r\n{}";
            let _ = stream.write_all(resp);
            req
        });

        let r = TrelloApiService::add_comment(&base, "kunci", "token", "card-9", "halo mock");
        assert!(r.is_ok(), "add_comment gagal: {:?}", r.err());
        let req = handle.join().unwrap();
        assert!(
            req.starts_with("POST /api/cards/card-9/actions/comments?key=kunci&token=token "),
            "request line salah: {}",
            req.lines().next().unwrap_or("")
        );
        assert!(
            req.contains("text=halo+mock"),
            "body form salah: {}",
            req
        );
    }
}
