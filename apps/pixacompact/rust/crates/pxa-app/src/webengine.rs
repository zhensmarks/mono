//! Port lapisan orkestrasi `Services/PixelcutService.cs` + `PixaAccountRotator.cs`.
//! Ini "otak" engine web: pemilihan engine, rotasi akun round-robin, kuota,
//! pemecahan sub-batch, parse daftar proxy. Driver browser (CDP/Playwright)
//! di lapisan bawah dipanggil via `WebEngine::process` (lihat catatan akhir file).

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
/// Engine web yang tersedia di app asli.
pub enum WebEngine {
    /// PIXA / pixelcut.ai — default; otomasi batch-edit.
    Pixa,
    /// REMBG lokal (CLI rembg.exe + model u2netp).
    Rembg,
    /// NOBG Space — web.
    NobgSpace,
    /// REMBG Online — web.
    RembgOnline,
    /// BG Eraser — web.
    BgEraser,
}

impl WebEngine {
    pub fn from_str(s: &str) -> Option<Self> {
        match s.to_ascii_uppercase().as_str() {
            "PIXA" | "PIXELCUT" => Some(Self::Pixa),
            "REMBG" => Some(Self::Rembg),
            "NOBG_SPACE" | "NOBG" => Some(Self::NobgSpace),
            "REMBG_ONLINE" => Some(Self::RembgOnline),
            "BG_ERASER" | "BG_ERASER" => Some(Self::BgEraser),
            _ => None,
        }
    }
}

/// Akun pixelcut untuk rotasi round-robin (port `Models/PixaAccount`).
#[derive(Clone)]
pub struct PixaAccount {
    pub name: String,
    /// Suffix folder profil browser (tiap akun sesi sendiri).
    pub profile_suffix: String,
    /// Limit gambar per sesi (akun gratis pixelcut ~5).
    pub max_images_per_session: usize,
    /// Berapa gambar sudah dipakai sesi ini (wrap otomatis oleh rotator).
    pub images_used_this_session: usize,
}

impl PixaAccount {
    pub fn new(name: impl Into<String>, profile_suffix: impl Into<String>, limit: usize) -> Self {
        Self { name: name.into(), profile_suffix: profile_suffix.into(), max_images_per_session: limit.max(1), images_used_this_session: 0 }
    }
}

/// Rotasi round-robin antar akun (port `PixaAccountRotator`).
pub struct AccountRotator {
    pub accounts: Vec<PixaAccount>,
    idx: usize,
}

impl AccountRotator {
    pub fn new(accounts: Vec<PixaAccount>) -> Self {
        Self { accounts, idx: 0 }
    }

    /// Akun aktif atau berikutnya (null kalau semua limit).
    /// Proaktif: akun yang kuotanya habis di-wrap lalu pindah giliran.
    pub fn current_or_next(&mut self) -> Option<usize> {
        let n = self.accounts.len();
        if n == 0 {
            return None;
        }
        // 1) cari akun yang masih punya sisa kuota (tanpa wrap).
        for step in 0..n {
            let i = (self.idx + step) % n;
            let acc = &mut self.accounts[i];
            if acc.images_used_this_session < acc.max_images_per_session {
                self.idx = i;
                return Some(i);
            }
        }
        // 2) semua habis → wrap ronde baru (counter di-mod cap) dan mulai lagi.
        for step in 0..n {
            let i = (self.idx + step) % n;
            let acc = &mut self.accounts[i];
            let cap = acc.max_images_per_session.max(1);
            if cap == 0 {
                continue;
            }
            acc.images_used_this_session %= cap;
            if acc.images_used_this_session < cap {
                self.idx = i;
                return Some(i);
            }
        }
        None
    }

    /// Mark akun kena limit (deteksi halaman / error) → giliran berikutnya.
    pub fn mark_limited(&mut self, i: usize, _reason: &str) {
        if let Some(a) = self.accounts.get_mut(i) {
            a.images_used_this_session = a.max_images_per_session;
        }
        self.next();
    }

    pub fn next(&mut self) {
        if !self.accounts.is_empty() {
            self.idx = (self.idx + 1) % self.accounts.len();
        }
    }

    /// Sisa kuota akun pada ronde berjalan (proaktif, wrap bila sudah habis).
    pub fn remaining_quota(&mut self, i: usize) -> usize {
        let cap = self.accounts[i].max_images_per_session.max(1);
        let mut used = self.accounts[i].images_used_this_session;
        if used >= cap {
            used %= cap;
            self.accounts[i].images_used_this_session = used;
        }
        cap - used
    }
}

/// Potong sub-batch sesuai sisa kuota tiap akun, round-robin.
/// Return: daftar (index_akun, start_item, take) yang siap diproses per akun.
/// Mirip logika `RunSubBatchRotatingAsync` — dipecah jadi segmen per akun.
/// Kalau semua akun habis di tengah chunk, kuota di-wrap (ronde baru) seperti
/// `RemainingQuota` di C#, sehingga chunk tetap selesai.
pub fn plan_rotated_segments(chunk_len: usize, accounts: &[PixaAccount]) -> Vec<(usize, usize, usize)> {
    let mut plan = Vec::new();
    let n = accounts.len();
    if n == 0 {
        return plan;
    }
    let mut used: Vec<usize> = accounts.iter().map(|a| a.images_used_this_session).collect();
    let mut idx = 0usize;
    let mut guard = 0;
    let max_guard = chunk_len.saturating_mul(n + 1).saturating_add(8);
    let total_cap: usize = accounts.iter().map(|a| a.max_images_per_session.max(1)).sum();
    let wraps_needed = if total_cap == 0 { 0 } else { (chunk_len.saturating_sub(1)) / total_cap + 1 };
    loop {
        if idx >= chunk_len {
            break;
        }
        guard += 1;
        if guard > max_guard.saturating_mul(2).max(max_guard) {
            break;
        }
        let mut ai = 0usize;
        while ai < n && used[ai] >= accounts[ai].max_images_per_session.max(1) {
            ai += 1;
        }
        if ai == n {
            // semua habis → wrap satu ronde (mod cap). Kalau total_cap==0, berhenti.
            if total_cap == 0 {
                break;
            }
            for a in 0..n {
                let cap = accounts[a].max_images_per_session.max(1);
                if cap > 0 {
                    used[a] %= cap;
                }
            }
            continue;
        }
        let cap = accounts[ai].max_images_per_session.max(1);
        let take = if cap == 0 {
            0
        } else {
            (cap - used[ai]).min(chunk_len - idx).max(1)
        };
        if take == 0 {
            // guard: akun ini tak bisa ambil apa-apa
            used[ai] = cap;
            continue;
        }
        plan.push((ai, idx, take));
        idx += take;
        used[ai] += take;
        let _ = wraps_needed;
    }
    plan
}

/// Parse daftar proxy (port `ParseProxyList`): satu per baris, duplikat
/// diabaikan case-insensitive.
pub fn parse_proxy_list(raw: &str) -> Vec<String> {
    let mut out = Vec::new();
    for line in raw.replace('\r', "").lines() {
        let l = line.trim();
        if l.is_empty() {
            continue;
        }
        let dup = out.iter().any(|p: &String| p.eq_ignore_ascii_case(l));
        if !dup {
            out.push(l.to_string());
        }
    }
    out
}

/// Normalisasi proxy ke format Playwright: `ip:port` → `http://ip:port`.
pub fn normalize_proxy(p: &str) -> String {
    let l = p.trim();
    if l.starts_with("http://") || l.starts_with("https://") || l.starts_with("socks5://") {
        l.to_string()
    } else {
        format!("http://{l}")
    }
}

/// Timeout dinamis (port `ComputeTimeoutMs`): 180s + 8s/gambar, cap 30 menit.
pub fn compute_timeout_ms(image_count: usize) -> u64 {
    let ms = 180_000u64 + 8_000u64 * image_count.max(0) as u64;
    ms.min(1_800_000)
}

/// Kata kunci limit akun (port daftar `DetectAccountLimitAsync`).
pub const LIMIT_KEYWORDS: &[&str] = &[
    "out of credits", "no credits left", "reached your limit", "reached your free", "daily limit",
    "limit reached", "you've reached", "quota exceeded", "upgrade to continue", "upgrade your plan",
    "free plan limit", "sign up to continue", "limit exceeded", "try again later", "too many requests",
];

pub fn detect_limit(body_text: &str) -> Option<&'static str> {
    let t = body_text.to_ascii_lowercase();
    for kw in LIMIT_KEYWORDS {
        if t.contains(*kw) {
            return Some(*kw);
        }
    }
    None
}

/// Apakah PNG punya alpha channel (color type 4/6 atau tRNS) — port `PngHasAlpha`.
pub fn png_has_alpha(bytes: &[u8]) -> bool {
    if bytes.len() < 33 || &bytes[0..4] != [0x89, 0x50, 0x4E, 0x47] {
        return false;
    }
    let mut offset = 8;
    let mut color_type: Option<u8> = None;
    while offset + 8 <= bytes.len() {
        let length = u32::from_be_bytes([bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3]]) as usize;
        if offset + 12 + length > bytes.len() {
            break;
        }
        let tag = &bytes[offset + 4..offset + 8];
        let data = &bytes[offset + 8..offset + 8 + length];
        if tag == b"IHDR" {
            if data.len() >= 9 {
                color_type = Some(data[8]);
            }
        }
        if tag == b"tRNS" {
            return true;
        }
        if tag == b"IEND" {
            break;
        }
        offset += 12 + length;
    }
    matches!(color_type, Some(4) | Some(6))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn engine_from_str() {
        assert_eq!(WebEngine::from_str("PIXA"), Some(WebEngine::Pixa));
        assert_eq!(WebEngine::from_str("rembg"), Some(WebEngine::Rembg));
        assert_eq!(WebEngine::from_str("bg_eraser"), Some(WebEngine::BgEraser));
        assert_eq!(WebEngine::from_str("nope"), None);
    }

    #[test]
    fn proxy_list_dedupes_and_trims() {
        let raw = "1.2.3.4:8080\n 5.6.7.8:1080 \n1.2.3.4:8080\n";
        let v = parse_proxy_list(raw);
        assert_eq!(v.len(), 2);
    }

    #[test]
    fn normalize_proxy_maps_bare_ip_port() {
        assert_eq!(normalize_proxy("1.2.3.4:80"), "http://1.2.3.4:80");
        assert_eq!(normalize_proxy("socks5://x:1"), "socks5://x:1");
    }

    #[test]
    fn timeout_scales_and_caps() {
        assert_eq!(compute_timeout_ms(0), 180_000);
        // 180000 + 8000*10 = 260000 (port ComputeTimeoutMs C#: 180s + 8s/gambar).
        assert_eq!(compute_timeout_ms(10), 260_000);
        // 180000 + 8000*1000 = 8.18M → cap 1.8M.
        assert_eq!(compute_timeout_ms(1000), 1_800_000);
        assert_eq!(compute_timeout_ms(10_000), 1_800_000);
    }

    #[test]
    fn limit_detection() {
        assert!(detect_limit("You've reached your limit").is_some());
        assert!(detect_limit("ok, 3 images left").is_none());
    }

    #[test]
    fn rotator_wraps_and_advances() {
        let mut r = AccountRotator::new(vec![
            PixaAccount::new("a", "a", 2),
            PixaAccount::new("b", "b", 1),
        ]);
        let i0 = r.current_or_next().expect("ada akun");
        assert!(i0 < 2);
        // pakai 2 gambar akun i0 → habis
        let acc = &mut r.accounts[i0];
        acc.images_used_this_session = 2;
        let i1 = r.current_or_next().expect("pindah akun");
        assert_ne!(i1, i0);
    }

    #[test]
    fn plan_segments_respects_quota() {
        let accounts = vec![PixaAccount::new("a", "a", 2), PixaAccount::new("b", "b", 2)];
        let plan = plan_rotated_segments(8, &accounts);
        // total item yang direncanakan harus == 8
        let total: usize = plan.iter().map(|(_, _, t)| *t).sum();
        assert_eq!(total, 8, "plan harus mencakup semua item");
        // tiap segmen ≤ kuota akun
        for (ai, _start, take) in &plan {
            assert!(*take <= accounts[*ai].max_images_per_session);
        }
    }

    #[test]
    fn png_alpha_detect() {
        // Buat PNG RGBA minimal: signature + IHDR(color type 6).
        let mut b: Vec<u8> = vec![0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        // IHDR: length=13, tag, data(13) dengan colorType=6 di byte terakhir data
        b.extend_from_slice(&[0, 0, 0, 13, b'I', b'H', b'D', b'R']);
        let mut ihdr_data = vec![0u8; 13];
        ihdr_data[8] = 6;
        b.extend_from_slice(&ihdr_data);
        b.extend_from_slice(&[0u8; 4]); // crc
        b.extend_from_slice(&[0, 0, 0, 0, b'I', b'E', b'N', b'D', 0, 0, 0, 0]);
        assert!(png_has_alpha(&b));
        // PNG RGB (color type 2) tanpa tRNS → tak ada alpha.
        // colorType di absolute index 24 (8 sig + 4 len + 4 tag + data[8]).
        let mut c = b.clone();
        c[24] = 2;
        assert!(!png_has_alpha(&c));
    }
}