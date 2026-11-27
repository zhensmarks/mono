//! Kontrol volume OS dari panel mengembang Dynamic Bar (notch
//! hidup) — menggantikan shortcut radial volume yang dibatalkan
//! 27 Nov 2026 (abeng 20:41: kontrol volume pindah ke notch).
//!
//! Windows: Core Audio (`IAudioEndpointVolume`) lewat PowerShell
//! + `Add-Type` (C# inline) — bawaan Windows, offline, tanpa
//! dependensi baru. Linux: `pactl` (PulseAudio) lalu `wpctl`
//! (PipeWire); bila tidak ada keduanya → tidak tersedia
//! (slider nonaktif, bukan error). Semua pemanggilan subprocess
//! ber-timeout supaya UI tidak pernah macet gara-gara audio.
//!
//! `set_volume` di-debounce (150 ms): drag slider mengirim
//! puluhan event — hanya nilai terakhir yang diterapkan ke OS.

use std::sync::{mpsc, Mutex};
use std::time::Duration;

/// State volume OS: level 0–100 + status bisu.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct VolumeState {
    pub level: u8,
    pub muted: bool,
}

/// Baca volume + mute saat ini. `None` bila OS tidak
/// menyediakan kontrol volume (mis. Linux tanpa PulseAudio /
/// PipeWire, atau perintah gagal/timeout).
pub fn get_volume() -> Option<VolumeState> {
    #[cfg(windows)]
    {
        get_volume_windows()
    }
    #[cfg(not(windows))]
    {
        get_volume_linux()
    }
}

/// Terapkan level volume (0–100). Di-debounce: bila dipanggil
/// beruntun (drag slider), hanya nilai terakhir tiap 150 ms
/// yang dikirim ke OS. Tidak pernah memblokir pemanggil.
pub fn set_volume(level: u8) {
    let level = level.clamp(0, 100);
    let mut slot = VOLUME_TX.lock().unwrap_or_else(|e| e.into_inner());
    if slot.is_none() {
        let (tx, rx) = mpsc::channel::<u8>();
        *slot = Some(tx);
        std::thread::spawn(move || {
            let mut latest: Option<u8> = None;
            loop {
                match rx.recv_timeout(Duration::from_millis(150)) {
                    Ok(v) => latest = Some(v),
                    Err(mpsc::RecvTimeoutError::Timeout) => {
                        if let Some(v) = latest.take() {
                            apply_volume(v);
                        }
                    }
                    Err(mpsc::RecvTimeoutError::Disconnected) => {
                        if let Some(v) = latest.take() {
                            apply_volume(v);
                        }
                        return;
                    }
                }
            }
        });
    }
    if let Some(tx) = slot.as_ref() {
        let _ = tx.send(level);
    }
}

/// Terapkan status bisu. Sekali jalan, tanpa debounce (toggle
/// diskrit — tidak ada hujan event).
pub fn set_muted(muted: bool) {
    std::thread::spawn(move || {
        apply_mute(muted);
    });
}

static VOLUME_TX: Mutex<Option<mpsc::Sender<u8>>> = Mutex::new(None);

/// Panggil subprocess dengan timeout (detik). `None` bila
/// perintah tidak ada, gagal, atau kehabisan waktu.
fn run_timeout(cmd: &str, args: &[&str], secs: u64) -> Option<std::process::Output> {
    let (tx, rx) = mpsc::channel();
    let mut command = std::process::Command::new(cmd);
    command.args(args);
    command.stdout(std::process::Stdio::piped());
    command.stderr(std::process::Stdio::null());
    let child = command.spawn().ok()?;
    let tx2 = tx.clone();
    std::thread::spawn(move || {
        let _ = tx2.send(child.wait_with_output());
    });
    drop(tx);
    match rx.recv_timeout(Duration::from_secs(secs)) {
        Ok(Ok(out)) => Some(out),
        _ => None,
    }
}

// ================= Windows (Core Audio) =================

/// Definisi C# inline untuk `IAudioEndpointVolume` (endpoint
/// audio default). Satu definisi dipakai get/set/mute.
#[cfg_attr(not(windows), allow(dead_code))]
const PS_AUDIO_TYPE: &str = r#"Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolume { int _1(); int _2(); int _3(); int _4(); int SetMasterVolumeLevelScalar(float fLevel, Guid pguidEventContext); int _6(); int GetMasterVolumeLevelScalar(out float pfLevel); int _8(); int _9(); int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, Guid pguidEventContext); int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute); }
[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice { int Activate(ref Guid id, int clsCtx, int activationParams, out IAudioEndpointVolume aev); }
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator { int _1(); int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device); }
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorComObject { }
public class BMAudio {
  static IAudioEndpointVolume Vol() {
    var enumerator = new MMDeviceEnumeratorComObject() as IMMDeviceEnumerator;
    IMMDevice dev = null;
    Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out dev));
    IAudioEndpointVolume epv = null;
    var epvid = typeof(IAudioEndpointVolume).GUID;
    Marshal.ThrowExceptionForHR(dev.Activate(ref epvid, 23, 0, out epv));
    return epv;
  }
  public static float GetVolume() { float v = 0; Marshal.ThrowExceptionForHR(Vol().GetMasterVolumeLevelScalar(out v)); return v; }
  public static bool GetMute() { bool m = false; Marshal.ThrowExceptionForHR(Vol().GetMute(out m)); return m; }
  public static void SetVolume(float v) { Marshal.ThrowExceptionForHR(Vol().SetMasterVolumeLevelScalar(v, Guid.Empty)); }
  public static void SetMute(bool m) { Marshal.ThrowExceptionForHR(Vol().SetMute(m, Guid.Empty)); }
}
'@; "#;

/// Skrip baca: mencetak dua baris, `VOL=0.50` lalu `MUTE=False`.
#[cfg_attr(not(windows), allow(dead_code))]
pub fn build_get_script() -> String {
    format!(
        "{} $v = [BMAudio]::GetVolume(); $m = [BMAudio]::GetMute(); \"VOL=$v\"; \"MUTE=$m\"",
        PS_AUDIO_TYPE
    )
}

/// Skrip tulis level (0.0–1.0).
#[cfg_attr(not(windows), allow(dead_code))]
pub fn build_set_script(level_01: f32) -> String {
    format!(
        "{} [BMAudio]::SetVolume({:.3})",
        PS_AUDIO_TYPE,
        level_01.clamp(0.0, 1.0)
    )
}

/// Skrip tulis mute (`$true` / `$false`).
#[cfg_attr(not(windows), allow(dead_code))]
pub fn build_mute_script(muted: bool) -> String {
    format!(
        "{} [BMAudio]::SetMute(${})",
        PS_AUDIO_TYPE,
        if muted { "true" } else { "false" }
    )
}

/// Parse output skrip baca: `VOL=0.50` + `MUTE=False`.
#[cfg_attr(not(windows), allow(dead_code))]
pub fn parse_get_output(out: &str) -> Option<VolumeState> {
    let mut level: Option<u8> = None;
    let mut muted: Option<bool> = None;
    for line in out.lines() {
        let line = line.trim();
        if let Some(v) = line.strip_prefix("VOL=") {
            level = v.trim().parse::<f32>().ok().map(|f| (f.clamp(0.0, 1.0) * 100.0).round() as u8);
        } else if let Some(m) = line.strip_prefix("MUTE=") {
            muted = Some(m.trim().eq_ignore_ascii_case("true"));
        }
    }
    match (level, muted) {
        (Some(level), Some(muted)) => Some(VolumeState { level, muted }),
        _ => None,
    }
}

#[cfg_attr(not(windows), allow(dead_code))]
fn run_powershell(script: &str, secs: u64) -> Option<std::process::Output> {
    run_timeout(
        "powershell.exe",
        &[
            "-NoProfile",
            "-NonInteractive",
            "-WindowStyle",
            "Hidden",
            "-Command",
            script,
        ],
        secs,
    )
}

#[cfg(windows)]
fn get_volume_windows() -> Option<VolumeState> {
    let out = run_powershell(&build_get_script(), 5)?;
    if !out.status.success() {
        return None;
    }
    parse_get_output(&String::from_utf8_lossy(&out.stdout))
}

#[cfg(windows)]
fn apply_volume(level: u8) {
    let _ = run_powershell(&build_set_script(level as f32 / 100.0), 5);
}

#[cfg(windows)]
fn apply_mute(muted: bool) {
    let _ = run_powershell(&build_mute_script(muted), 5);
}

// ================= Linux (pactl / wpctl) =================

/// Parse `pactl get-sink-volume`: ambil persen pertama.
/// Contoh: `Volume: front-left: 32768 /  50% / -18.06 dB, ...`
pub fn parse_pactl_volume(out: &str) -> Option<u8> {
    for token in out.split(|c: char| c == ' ' || c == '\t' || c == ',') {
        if let Some(num) = token.strip_suffix('%') {
            if let Ok(v) = num.parse::<u8>() {
                return Some(v.min(100));
            }
        }
    }
    None
}

/// Parse `pactl get-sink-mute`: `Mute: no` / `Mute: yes`.
pub fn parse_pactl_mute(out: &str) -> Option<bool> {
    let lower = out.to_lowercase();
    if lower.contains("yes") {
        Some(true)
    } else if lower.contains("no") {
        Some(false)
    } else {
        None
    }
}

/// Parse `wpctl get-volume`: `Volume: 0.50` atau
/// `Volume: 0.50 [MUTED]`.
pub fn parse_wpctl_volume(out: &str) -> Option<VolumeState> {
    let line = out.trim();
    let rest = line.strip_prefix("Volume:")?.trim();
    let (num, muted) = match rest.split_once(' ') {
        Some((n, flag)) => (n, flag.contains("MUTED")),
        None => (rest, false),
    };
    let level = (num.parse::<f32>().ok()?.clamp(0.0, 1.5) * 100.0).round() as u8;
    Some(VolumeState {
        level: level.min(100),
        muted,
    })
}

#[cfg(not(windows))]
fn get_volume_linux() -> Option<VolumeState> {
    // PulseAudio dulu, lalu PipeWire.
    if let Some(out) = run_timeout("pactl", &["get-sink-volume", "@DEFAULT_SINK@"], 3) {
        if out.status.success() {
            let vol_out = String::from_utf8_lossy(&out.stdout);
            let level = parse_pactl_volume(&vol_out)?;
            let muted = run_timeout("pactl", &["get-sink-mute", "@DEFAULT_SINK@"], 3)
                .filter(|o| o.status.success())
                .and_then(|o| parse_pactl_mute(&String::from_utf8_lossy(&o.stdout)))
                .unwrap_or(false);
            return Some(VolumeState { level, muted });
        }
    }
    if let Some(out) = run_timeout("wpctl", &["get-volume", "@DEFAULT_AUDIO_SINK@"], 3) {
        if out.status.success() {
            return parse_wpctl_volume(&String::from_utf8_lossy(&out.stdout));
        }
    }
    None
}

#[cfg(not(windows))]
fn apply_volume(level: u8) {
    let pct = format!("{}%", level);
    if run_timeout(
        "pactl",
        &["set-sink-volume", "@DEFAULT_SINK@", &pct],
        3,
    )
    .map(|o| o.status.success())
    .unwrap_or(false)
    {
        return;
    }
    let val = format!("{:.2}", level as f32 / 100.0);
    let _ = run_timeout("wpctl", &["set-volume", "@DEFAULT_AUDIO_SINK@", &val], 3);
}

#[cfg(not(windows))]
fn apply_mute(muted: bool) {
    let flag = if muted { "1" } else { "0" };
    if run_timeout("pactl", &["set-sink-mute", "@DEFAULT_SINK@", flag], 3)
        .map(|o| o.status.success())
        .unwrap_or(false)
    {
        return;
    }
    let _ = run_timeout("wpctl", &["set-mute", "@DEFAULT_AUDIO_SINK@", flag], 3);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn pactl_volume_parses_first_percent() {
        assert_eq!(
            parse_pactl_volume("Volume: front-left: 32768 /  50% / -18.06 dB,   front-right: 32768 /  50% / -18.06 dB"),
            Some(50)
        );
        assert_eq!(parse_pactl_volume("Volume: front-left: 0 / 0%"), Some(0));
        assert_eq!(parse_pactl_volume("Volume: front-left: 65536 / 100%"), Some(100));
        assert_eq!(parse_pactl_volume("tidak ada persen"), None);
    }

    #[test]
    fn pactl_mute_parses_yes_no() {
        assert_eq!(parse_pactl_mute("Mute: no"), Some(false));
        assert_eq!(parse_pactl_mute("Mute: yes"), Some(true));
        assert_eq!(parse_pactl_mute("Mute: Yes"), Some(true));
        assert_eq!(parse_pactl_mute("???"), None);
    }

    #[test]
    fn wpctl_volume_parses_level_and_mute() {
        assert_eq!(
            parse_wpctl_volume("Volume: 0.50"),
            Some(VolumeState {
                level: 50,
                muted: false
            })
        );
        assert_eq!(
            parse_wpctl_volume("Volume: 0.75 [MUTED]"),
            Some(VolumeState {
                level: 75,
                muted: true
            })
        );
        assert_eq!(parse_wpctl_volume("Volume: 1.00"), Some(VolumeState { level: 100, muted: false }));
        assert_eq!(parse_wpctl_volume("bogus"), None);
    }

    #[test]
    fn powershell_scripts_contain_audio_calls() {
        let get = build_get_script();
        assert!(get.contains("BMAudio]::GetVolume"));
        assert!(get.contains("BMAudio]::GetMute"));
        assert!(get.contains("IAudioEndpointVolume"));
        let set = build_set_script(0.5);
        assert!(set.contains("SetVolume(0.500)"));
        assert!(build_mute_script(true).contains("SetMute($true)"));
        assert!(build_mute_script(false).contains("SetMute($false)"));
    }

    #[test]
    fn get_output_parses_vol_and_mute() {
        assert_eq!(
            parse_get_output("VOL=0.5\r\nMUTE=False\r\n"),
            Some(VolumeState {
                level: 50,
                muted: false
            })
        );
        assert_eq!(
            parse_get_output("VOL=1\r\nMUTE=True\r\n"),
            Some(VolumeState {
                level: 100,
                muted: true
            })
        );
        assert_eq!(parse_get_output("VOL=0.5"), None);
        assert_eq!(parse_get_output(""), None);
    }

    #[test]
    fn get_volume_is_none_without_audio_server() {
        // VM ini tidak punya pactl/wpctl — get_volume harus
        // mengembalikan None, bukan panic/timeout lama.
        // (Di mesin abeng yang ada audio server-nya, tes ini
        // bisa gagal — itu wajar, bukan bug.)
        if std::process::Command::new("pactl").arg("--version").output().is_err()
            && std::process::Command::new("wpctl").arg("--version").output().is_err()
        {
            assert_eq!(get_volume(), None);
        }
    }
}
