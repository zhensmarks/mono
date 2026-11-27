//! Text-to-speech untuk maskot Dynamic Bar (notch hidup).
//!
//! Windows: suara bawaan SAPI (`System.Speech`) lewat PowerShell
//! — offline, gratis, tanpa dependensi crate baru (PowerShell +
//! .NET Framework sudah ada di Windows 10/11). OS lain: stub
//! no-op supaya build Linux tetap lolos; voice HANYA bisa dites
//! di PC Windows abeng.
//!
//! Konfigurasi hidup di satu static global (diisi Rust saat
//! startup dari SQLite, diubah handler Settings) supaya thread
//! sync Trello pun bisa memicu ucapan tanpa plumbing parameter.

use std::sync::Mutex;
use std::time::{Duration, Instant};

/// Satu suara Windows yang terpasang.
#[derive(Debug, Clone, PartialEq)]
pub struct VoiceInfo {
    pub name: String,
    pub culture: String,
}

/// Konfigurasi voice maskot (satu-satunya sumber kebenaran di
/// sisi Rust; Settings mengikat ke properti AppWindow, handler
/// Rust menulis balik ke sini + SQLite).
#[derive(Debug, Clone)]
pub struct VoiceConfig {
    pub enabled: bool,
    /// Nama suara SAPI; kosong = otomatis (utamakan id-ID).
    pub voice_name: String,
    /// Volume 0–100.
    pub volume: i32,
}

impl Default for VoiceConfig {
    fn default() -> Self {
        Self {
            enabled: true,
            voice_name: String::new(),
            volume: 80,
        }
    }
}

static VOICE_CONFIG: Mutex<VoiceConfig> = Mutex::new(VoiceConfig {
    enabled: true,
    voice_name: String::new(),
    volume: 80,
});

/// Penanda ucap terakhir — throttle supaya satu hujan notifikasi
/// tidak menumpuk antrean suara (PowerShell `Speak` itu sinkron
/// per proses; tiap ucapan spawn proses sendiri).
static LAST_SPEAK: Mutex<Option<Instant>> = Mutex::new(None);

/// Ucapan terakhir minimal 1,5 detik yang lalu (kecuali `force`).
fn throttle_ok(force: bool) -> bool {
    if force {
        return true;
    }
    let mut last = match LAST_SPEAK.lock() {
        Ok(g) => g,
        Err(_) => return true,
    };
    match *last {
        Some(t) if t.elapsed() < Duration::from_millis(1500) => false,
        _ => {
            *last = Some(Instant::now());
            true
        }
    }
}

pub fn set_config(cfg: VoiceConfig) {
    if let Ok(mut g) = VOICE_CONFIG.lock() {
        *g = cfg;
    }
}

pub fn get_config() -> VoiceConfig {
    VOICE_CONFIG.lock().map(|g| g.clone()).unwrap_or_default()
}

/// Escape teks untuk literal string PowerShell bertanda kutip
/// tunggal: `'` digandakan. (Windows saja; di Linux tidak
/// terpakai — warning dead_code di Linux diharapkan.)
#[cfg_attr(not(windows), allow(dead_code))]
pub fn ps_quote(s: &str) -> String {
    format!("'{}'", s.replace('\'', "''"))
}

/// Rakit skrip PowerShell untuk satu ucapan. `voice` kosong =
/// suara default sistem (tanpa `SelectVoice`). (Windows saja.)
#[cfg_attr(not(windows), allow(dead_code))]
pub fn build_speak_script(text: &str, voice: Option<&str>, volume: u8) -> String {
    let mut script = String::from(
        "Add-Type -AssemblyName System.Speech; $s = New-Object System.Speech.Synthesis.SpeechSynthesizer; ",
    );
    script.push_str(&format!("$s.Volume = {}; ", volume.clamp(0, 100)));
    if let Some(v) = voice {
        if !v.is_empty() {
            script.push_str(&format!("$s.SelectVoice({}); ", ps_quote(v)));
        }
    }
    script.push_str(&format!("$s.Speak({}); $s.Dispose()", ps_quote(text)));
    script
}

/// Pilih suara default dari daftar: utamakan kultur id-ID,
/// lalu suara pertama, lalu None (default sistem). (Windows
/// saja — dipakai resolver "Otomatis" di bawah.)
#[cfg_attr(not(windows), allow(dead_code))]
pub fn pick_default_voice(voices: &[VoiceInfo]) -> Option<String> {
    voices
        .iter()
        .find(|v| v.culture.to_lowercase().starts_with("id"))
        .map(|v| v.name.clone())
        .or_else(|| voices.first().map(|v| v.name.clone()))
}

/// Ucapkan `text` bila voice aktif (baca konfigurasi global).
/// Fire-and-forget di thread sendiri — tidak pernah memblokir
/// UI atau timer sinkronisasi. Di OS non-Windows: no-op.
pub fn speak_event(text: &str) {
    let cfg = get_config();
    if !cfg.enabled {
        return;
    }
    speak_with(&cfg, text, false);
}

/// Ucapan tes dari Settings — mengabaikan toggle aktif (preview)
/// tapi tetap memakai suara + volume yang dipilih.
/// (Windows saja — handler Rust memanggilnya di balik cfg.)
#[cfg_attr(not(windows), allow(dead_code))]
pub fn speak_test(text: &str) {
    let cfg = get_config();
    speak_with(&cfg, text, true);
}

/// Suara "Otomatis" yang sudah di-resolve sekali (cache):
/// None = belum di-resolve; Some(None) = tidak ada voice yang
/// cocok (pakai default sistem); Some(Some(nama)) = voice id-ID
/// / pertama yang ditemukan.
static AUTO_VOICE: Mutex<Option<Option<String>>> = Mutex::new(None);

/// Resolve suara "Otomatis": utamakan id-ID, lalu suara
/// pertama, lalu None. Di-resolve sekali lalu di-cache —
/// enumerasi suara butuh ~0,5 detik (PowerShell) jadi tidak
/// boleh jalan tiap ucapan. (Windows saja.)
#[cfg_attr(not(windows), allow(dead_code))]
fn auto_voice() -> Option<String> {
    if let Ok(g) = AUTO_VOICE.lock() {
        if let Some(cached) = &*g {
            return cached.clone();
        }
    }
    let resolved = pick_default_voice(&list_voices());
    if let Ok(mut g) = AUTO_VOICE.lock() {
        *g = Some(resolved.clone());
    }
    resolved
}

fn speak_with(cfg: &VoiceConfig, text: &str, force: bool) {
    if text.trim().is_empty() {
        return;
    }
    if !throttle_ok(force) {
        return;
    }
    #[cfg(windows)]
    {
        // Resolve suara + rakit skrip di thread sendiri supaya
        // enumerasi suara pertama (~0,5 detik) tidak memblokir
        // timer / thread sync Trello yang memanggil.
        let cfg = cfg.clone();
        let text = text.to_string();
        std::thread::spawn(move || {
            let voice = if cfg.voice_name.is_empty() {
                auto_voice()
            } else {
                Some(cfg.voice_name.clone())
            };
            let script = build_speak_script(&text, voice.as_deref(), cfg.volume as u8);
            let _ = std::process::Command::new("powershell.exe")
                .args([
                    "-NoProfile",
                    "-NonInteractive",
                    "-WindowStyle",
                    "Hidden",
                    "-Command",
                    &script,
                ])
                .stdout(std::process::Stdio::null())
                .stderr(std::process::Stdio::null())
                .status();
        });
    }
    #[cfg(not(windows))]
    {
        let _ = (cfg, text, force);
    }
}

/// Daftar suara SAPI yang terpasang (Windows saja; di OS lain
/// daftar kosong). Dipanggil sekali saat startup — PowerShell
/// butuh ~0,5 detik, jadi dibatasi timeout 6 detik supaya
/// startup tidak pernah macet gara-gara ini.
pub fn list_voices() -> Vec<VoiceInfo> {
    #[cfg(windows)]
    {
        list_voices_windows()
    }
    #[cfg(not(windows))]
    {
        Vec::new()
    }
}

#[cfg(windows)]
fn list_voices_windows() -> Vec<VoiceInfo> {
    use std::sync::mpsc;
    let script = "Add-Type -AssemblyName System.Speech; (New-Object System.Speech.Synthesis.SpeechSynthesizer).GetInstalledVoices() | ForEach-Object { $_.VoiceInfo.Name + '|' + $_.VoiceInfo.Culture.Name }";
    let (tx, rx) = mpsc::channel();
    std::thread::spawn(move || {
        let out = std::process::Command::new("powershell.exe")
            .args([
                "-NoProfile",
                "-NonInteractive",
                "-WindowStyle",
                "Hidden",
                "-Command",
                script,
            ])
            .output();
        let _ = tx.send(out);
    });
    let out = match rx.recv_timeout(Duration::from_secs(6)) {
        Ok(Ok(o)) => o,
        _ => return Vec::new(),
    };
    if !out.status.success() {
        return Vec::new();
    }
    String::from_utf8_lossy(&out.stdout)
        .lines()
        .filter_map(|line| {
            let (name, culture) = line.split_once('|')?;
            let name = name.trim().to_string();
            let culture = culture.trim().to_string();
            if name.is_empty() {
                None
            } else {
                Some(VoiceInfo { name, culture })
            }
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn ps_quote_doubles_single_quotes() {
        assert_eq!(ps_quote("it's"), "'it''s'");
        assert_eq!(ps_quote("biasa"), "'biasa'");
    }

    #[test]
    fn speak_script_with_and_without_voice() {
        let with = build_speak_script("Halo", Some("Microsoft Andika"), 80);
        assert!(with.contains("SelectVoice('Microsoft Andika')"));
        assert!(with.contains("$s.Volume = 80"));
        assert!(with.contains("$s.Speak('Halo')"));
        let auto = build_speak_script("Halo", None, 50);
        assert!(!auto.contains("SelectVoice"));
        assert!(auto.contains("$s.Volume = 50"));
        // Volume di-clamp 0–100.
        assert!(build_speak_script("x", None, 250).contains("$s.Volume = 100"));
    }

    #[test]
    fn pick_default_prefers_indonesian() {
        let voices = vec![
            VoiceInfo {
                name: "Microsoft David".into(),
                culture: "en-US".into(),
            },
            VoiceInfo {
                name: "Microsoft Andika".into(),
                culture: "id-ID".into(),
            },
        ];
        assert_eq!(
            pick_default_voice(&voices).as_deref(),
            Some("Microsoft Andika")
        );
        assert_eq!(
            pick_default_voice(&voices[..1]).as_deref(),
            Some("Microsoft David")
        );
        assert_eq!(pick_default_voice(&[]), None);
    }
}
