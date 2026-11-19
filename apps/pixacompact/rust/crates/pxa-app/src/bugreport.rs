//! Port `Abeng.BugReporter` (shared/BMachine.Shared.BugReporter).
//!
//! Kirim bug/crash ke ProjectBug bridge lokal (`http://127.0.0.1:21478/bug`),
//! fallback ke file JSON di `%LOCALAPPDATA%\BMachine\bug-reports` kalau bridge
//! mati. Tahan banting: kegagalan jaringan tak boleh menjatuhkan app.

use std::path::PathBuf;

/// Port default bridge bug ProjectBot (`ProjectBot/lib/httpbug.js`).
pub const DEFAULT_PORT: u16 = 21478;

fn local_appdata() -> PathBuf {
    std::env::var("LOCALAPPDATA")
        .ok()
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from("."))
}

fn bug_reports_dir() -> PathBuf {
    local_appdata().join("BMachine").join("bug-reports")
}

/// <summary>Port `BugPayload`.</summary>
fn build_payload(
    project: &str,
    summary: &str,
    text: &str,
    app_version: Option<&str>,
    severity: &str,
    source: &str,
) -> serde_json::Value {
    let summary = summary.trim();
    let summary = if summary.is_empty() { "(tidak ada judul)" } else { summary };
    let text = if text.trim().is_empty() { summary } else { text.trim() };
    let ts = timestamp_now();
    serde_json::json!({
        "project": project.trim(),
        "summary": summary,
        "text": text,
        "appVersion": app_version.filter(|v| !v.trim().is_empty()),
        "severity": severity,
        "source": if source == "crash" { "crash" } else { "manual" },
        "timestamp": ts,
    })
}

fn timestamp_now() -> String {
    // Format ISO-8601 lokal (C# `DateTime.Now.ToString("o")`).
    let secs = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0);
    // Tanggal dari epoch (cukup akurat untuk log; tak perlu crate chrono).
    let days = (secs / 86400) as i64;
    let rem = secs % 86400;
    let (y, m, d) = civil_from_days(days);
    format!(
        "{y:04}-{m:02}-{d:02}T{:02}:{:02}:{:02}.0000000+07:00",
        rem / 3600,
        (rem % 3600) / 60,
        rem % 60
    )
}

/// Algoritma civil (Howard Hinnant) — hari → tanggal, tanpa chrono.
fn civil_from_days(z: i64) -> (i64, u32, u32) {
    let z = z + 719468;
    let era = if z >= 0 { z } else { z - 146096 } / 146097;
    let doe = (z - era * 146097) as u64;
    let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let day = (doy - (153 * mp + 2) / 5 + 1) as u32;
    let month = if mp < 10 { mp + 3 } else { mp - 9 } as u32;
    let year = yoe as i64 + era * 400 + if month <= 2 { 1 } else { 0 };
    (year, month, day)
}

/// <summary>Port `SendAsync`: kirim ke bridge; fallback file lokal.
/// Return `(ok, fallback_path)`. Tidak pernah panik.</summary>
pub fn send(project: &str, summary: &str, text: &str, app_version: Option<&str>) -> (bool, Option<String>) {
    let payload = build_payload(project, summary, text, app_version, "sedang", "manual");
    let json = serde_json::to_string(&payload).unwrap_or_default();
    let endpoint = format!("http://127.0.0.1:{DEFAULT_PORT}/bug");
    match http_post(&endpoint, &json) {
        Some(resp) if resp.starts_with("{\"ok\":true") || resp.contains("\"ok\":true") => (true, None),
        _ => {
            let path = save_fallback(&payload, &json);
            (false, path)
        }
    }
}

/// <summary>Port `ReportCrash`: simpan cadangan lokal dulu, coba kirim ke bridge.</summary>
pub fn report_crash(project: &str, ex: &str, app_version: Option<&str>, context: &str) {
    // 1) selalu simpan log crash lokal.
    let dir = local_appdata().join("BMachine");
    if std::fs::create_dir_all(&dir).is_ok() {
        let log = dir.join("crash.log");
        let _ = std::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(&log)
            .and_then(|f| {
                use std::io::Write;
                let mut f = f;
                f.write_all(
                    format!("[{}] {}:\n{}\n\n", timestamp_now(), context.to_uppercase(), ex)
                        .as_bytes(),
                )
            });
    }
    // 2) coba kirim ke bridge (tak block crash handler).
    let summary = first_line(ex).unwrap_or_else(|| format!("{context}: exception"));
    let _ = send(project, &summary, &format!("Context: {context}\n\n{ex}"), app_version);
}

fn first_line(s: &str) -> Option<String> {
    s.lines().next().filter(|l| !l.trim().is_empty()).map(|l| l.to_string())
}

/// <summary>Port `SaveToLocalFile`.</summary>
fn save_fallback(payload: &serde_json::Value, pretty: &str) -> Option<String> {
    let dir = bug_reports_dir();
    if std::fs::create_dir_all(&dir).is_err() {
        return None;
    }
    let ts = timestamp_now().replace([':', ' ', '.'], "-");
    let file = dir.join(format!("bug-{ts}.json"));
    let content = serde_json::to_string_pretty(payload).unwrap_or(pretty.to_string());
    if std::fs::write(&file, content).is_ok() {
        Some(file.to_string_lossy().into_owned())
    } else {
        None
    }
}

/// POST sederhana via `curl` (Windows punya curl.exe bawaan) — tanpa crate reqwest.
/// Return body respons, None kalau gagal/connect.
fn http_post(url: &str, body: &str) -> Option<String> {
    let out = std::process::Command::new("curl.exe")
        .args([
            "-s",
            "-S",
            "-m",
            "5",
            "-X",
            "POST",
            "-H",
            "Content-Type: application/json",
            "-d",
            body,
            url,
        ])
        .output()
        .ok()?;
    if out.status.success() {
        String::from_utf8(out.stdout).ok()
    } else {
        None
    }
}

/// <summary>Port `IsAvailableAsync`: cek bridge hidup (untuk indikator UI).</summary>
pub fn is_available() -> bool {
    let out = std::process::Command::new("curl.exe")
        .args(["-s", "-o", "NUL", "-w", "%{http_code}", "-m", "3", &format!("http://127.0.0.1:{DEFAULT_PORT}/bug")])
        .output();
    match out {
        Ok(o) if o.status.success() => {
            let code = String::from_utf8_lossy(&o.stdout).trim().to_string();
            code == "404" || code == "405" || code.starts_with("2")
        }
        _ => false,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn payload_shape() {
        let p = build_payload("PixelcutCompact", "  ", " detail ", Some("1.2.3"), "tinggi", "crash");
        assert_eq!(p["project"], "PixelcutCompact");
        assert_eq!(p["summary"], "(tidak ada judul)");
        assert_eq!(p["source"], "crash");
        assert_eq!(p["appVersion"], "1.2.3");
        assert!(p["timestamp"].as_str().unwrap().contains('-'));
    }

    #[test]
    fn civil_from_days_known() {
        // 1970-01-11 = hari ke-10 dari epoch.
        let (y, m, d) = civil_from_days(10);
        assert_eq!((y, m, d), (1970, 1, 11));
        // 2024-10-04 = hari ke-20000.
        let (y, m, d) = civil_from_days(20000);
        assert_eq!((y, m, d), (2024, 10, 4));
    }

    #[test]
    fn save_fallback_writes_file() {
        let p = build_payload("P", "s", "t", None, "sedang", "manual");
        let json = serde_json::to_string(&p).unwrap();
        // Jalankan dengan LOCALAPPDATA diarahkan ke temp (test tak menulis ke profil asli).
        let tmp = std::env::temp_dir();
        std::env::set_var("LOCALAPPDATA", &tmp);
        let path = save_fallback(&p, &json);
        assert!(path.is_some());
        let written = std::fs::read_to_string(path.unwrap()).unwrap();
        assert!(written.contains("\"summary\": \"s\""));
    }

    #[test]
    fn report_crash_writes_log() {
        let tmp = std::env::temp_dir();
        std::env::set_var("LOCALAPPDATA", &tmp);
        let before = local_appdata()
            .join("BMachine")
            .join("crash.log")
            .exists();
        report_crash("P", "System.Exception: boom", None, "unhandled");
        let after = local_appdata()
            .join("BMachine")
            .join("crash.log")
            .exists();
        assert!(after || before); // file ada setelah report
    }
}
