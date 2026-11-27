//! Eksekusi script Python BATCH — spawn via batch_wrapper.py, stream
//! log live, stop = bunuh seluruh tree proses (ala C# KillProcess).
//!
//! Rust HANYA menjalankan script (keputusan abeng 28 Nov 2026):
//! `python Scripts/batch_wrapper.py --target <nama> --pilihan <src>
//! --master <m1> --master2 <m2> --output <base> --okebase <oke>
//! [--master3 <m3>]`, env `BMACHINE_USER_NAME`. Satu root = satu
//! proses wrapper, berurutan, seperti C# `ExecuteMaster`.
//!
//! Stop: bunuh wrapper + semua descendant-nya satu-satu per-PID
//! (C# `Process.Kill(true)`). JANGAN group-kill (`kill -9 -<pgid>`):
//! sandbox di sini membunuh sesi pemanggil bila ada group-kill
//! (terbukti 28 Nov 2026, dua kali + eksperimen terisolasi).
//! Wrapper tetap di-spawn setsid (isolasi sinyal terminal);
//! pembunuhan tetap per-PID lewat /proc.
//! Python: `python3` di Linux (tidak ada `python` polos),
//! `python` di Windows (seperti C#).

use std::io::{BufRead, BufReader};
use std::process::{Child, Command, Stdio};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{channel, Receiver, Sender};
use std::sync::{Arc, Mutex};

/// Pesan dari thread eksekusi ke UI (dibaca timer 200ms — pola sama
/// seperti `SheetsInboxMsg`).
#[derive(Debug)]
pub enum BatchExecMsg {
    Line(String),
    RootDone { root: String, code: i32 },
    AllDone,
    Stopped,
    Error(String),
}

/// State satu run BATCH yang sedang jalan (dipegang Arc di main.rs).
pub struct BatchRunHandle {
    pub running: AtomicBool,
    pub stop_requested: AtomicBool,
    /// PID wrapper yang sedang jalan (= PGID di Linux via setsid).
    pub current_pid: Mutex<Option<u32>>,
    pub rx: Mutex<Option<Receiver<BatchExecMsg>>>,
    /// Nama file script run saat ini (mis. "buat_master.py") — untuk
    /// menentukan `has-manifest` saat sukses.
    pub script_file: Mutex<String>,
    /// Ada root yang exit != 0 / error selama run ini.
    pub failed: AtomicBool,
}

impl BatchRunHandle {
    pub fn new() -> Self {
        BatchRunHandle {
            running: AtomicBool::new(false),
            stop_requested: AtomicBool::new(false),
            current_pid: Mutex::new(None),
            rx: Mutex::new(None),
            script_file: Mutex::new(String::new()),
            failed: AtomicBool::new(false),
        }
    }

    pub fn is_running(&self) -> bool {
        self.running.load(Ordering::SeqCst)
    }

    /// Minta berhenti + bunuh tree proses yang sedang jalan (bila ada).
    /// Aman dipanggil berkali-kali / saat tidak ada proses.
    pub fn stop(&self) {
        self.stop_requested.store(true, Ordering::SeqCst);
        if let Some(pid) = *self.current_pid.lock().unwrap() {
            kill_process_tree(pid);
        }
    }
}

/// Bunuh tree proses per-PID (bukan per process group).
///
/// JANGAN pakai `kill -9 -<pgid>`: terbukti 28 Nov 2026 — dua kali
/// Stop saat run aktif membunuh BUKAN hanya wrapper, melainkan juga
/// sesi exec pemanggil + aplikasi + Xvfb; eksperimen terisolasi
/// (`setsid sleep 60`, lalu `kill -9 -<pgid>`) membunuh sesi exec
/// yang sama sekali tidak terkait aplikasinya. Sandbox/runtime di
/// sini bereaksi terhadap group-kill dengan membunuh sesi pemanggil.
/// Karena itu: kumpulkan descendant lewat /proc, bunuh satu-satu
/// dengan PID positif (`libc::kill`), anak dulu baru induk.
/// Windows: `taskkill /T /F` (tetap — tree kill resmi di sana).
pub fn kill_process_tree(pid: u32) {
    #[cfg(target_os = "windows")]
    {
        let _ = Command::new("taskkill")
            .args(["/PID", &pid.to_string(), "/T", "/F"])
            .output();
    }
    #[cfg(not(target_os = "windows"))]
    {
        let victims = descendants(pid);
        for v in victims {
            unsafe {
                libc::kill(v as i32, libc::SIGKILL);
            }
        }
        unsafe {
            libc::kill(pid as i32, libc::SIGKILL);
        }
    }
}

/// Semua descendant (anak, cucu, ...) dari `root`, urutan anak-dulu
/// (BFS dibalik: parent selalu muncul setelah keturunannya).
/// Dibaca dari /proc (Linux). Bila /proc tak bisa dibaca, kembalikan
/// vec kosong — pemanggil tetap membunuh `root` sendiri.
#[cfg(not(target_os = "windows"))]
fn descendants(root: u32) -> Vec<u32> {
    use std::collections::HashMap;
    let mut children: HashMap<u32, Vec<u32>> = HashMap::new();
    let Ok(rd) = std::fs::read_dir("/proc") else {
        return Vec::new();
    };
    for e in rd.flatten() {
        let name = e.file_name();
        let Ok(pid) = name.to_string_lossy().parse::<u32>() else {
            continue;
        };
        let Ok(stat) = std::fs::read_to_string(format!("/proc/{}/stat", pid)) else {
            continue;
        };
        // Format: `pid (comm) state ppid ...` — comm bisa berisi
        // spasi/kurung, jadi cari ')' TERAKHIR lalu ambil field sesudahnya.
        let Some(close) = stat.rfind(')') else {
            continue;
        };
        let rest = &stat[close + 1..];
        let mut it = rest.split_whitespace();
        let _state = it.next();
        if let Some(ppid) = it.next().and_then(|s| s.parse::<u32>().ok()) {
            children.entry(ppid).or_default().push(pid);
        }
    }
    let mut out = Vec::new();
    let mut stack = vec![root];
    let mut bfs_order = Vec::new();
    while let Some(p) = stack.pop() {
        bfs_order.push(p);
        if let Some(kids) = children.get(&p) {
            for k in kids {
                stack.push(*k);
            }
        }
    }
    // bfs_order[0] = root; sisanya descendant (parent sebelum anak).
    // Dibalik -> anak sebelum parent.
    bfs_order.remove(0);
    bfs_order.reverse();
    out.append(&mut bfs_order);
    out
}

/// Python yang dipakai: `python3` di Linux, `python` di Windows.
pub fn python_cmd() -> &'static str {
    #[cfg(target_os = "windows")]
    {
        "python"
    }
    #[cfg(not(target_os = "windows"))]
    {
        "python3"
    }
}

/// Satu pemanggilan wrapper untuk satu root. Dipanggil dari thread
/// worker (berurutan per root). Mengembalikan exit code wrapper
/// (`None` bila proses mati oleh sinyal / gagal spawn — pemanggil
/// membedakan stop vs error lewat `stop_requested`).
#[allow(clippy::too_many_arguments)]
fn run_one_root(
    python: &str,
    wrapper: &std::path::Path,
    target: &str,
    pilihan: &str,
    master: &str,
    master2: &str,
    master3: &str,
    output: &str,
    okebase: &str,
    user_name: &str,
    tx: &Sender<BatchExecMsg>,
    handle: &Arc<BatchRunHandle>,
) -> Option<i32> {
    let mut cmd = Command::new(python);
    cmd.arg(wrapper)
        .arg("--target")
        .arg(target)
        .arg("--pilihan")
        .arg(pilihan)
        .arg("--master")
        .arg(master)
        .arg("--master2")
        .arg(master2)
        .arg("--output")
        .arg(output)
        .arg("--okebase")
        .arg(okebase)
        .env("BMACHINE_USER_NAME", user_name)
        .env("PYTHONIOENCODING", "utf-8")
        .env("PYTHONUNBUFFERED", "1")
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());
    if !master3.is_empty() {
        cmd.arg("--master3").arg(master3);
    }
    #[cfg(not(target_os = "windows"))]
    unsafe {
        // Process group sendiri (isolasi sinyal terminal). Stop
        // TIDAK memakai group-kill — lihat kill_process_tree.
        use std::os::unix::process::CommandExt;
        cmd.pre_exec(|| {
            if libc::setsid() == -1 {
                return Err(std::io::Error::last_os_error());
            }
            Ok(())
        });
    }
    let mut child: Child = match cmd.spawn() {
        Ok(c) => c,
        Err(e) => {
            let _ = tx.send(BatchExecMsg::Error(format!(
                "Gagal meluncurkan python ({}): {}",
                python, e
            )));
            return None;
        }
    };
    *handle.current_pid.lock().unwrap() = Some(child.id());
    let stdout = match child.stdout.take() {
        Some(s) => s,
        None => {
            let _ = tx.send(BatchExecMsg::Error(
                "Tidak bisa membaca stdout wrapper.".to_string(),
            ));
            return None;
        }
    };
    let stderr = match child.stderr.take() {
        Some(s) => s,
        None => {
            let _ = tx.send(BatchExecMsg::Error(
                "Tidak bisa membaca stderr wrapper.".to_string(),
            ));
            return None;
        }
    };
    // Reader stdout + stderr (dua thread, baris -> channel).
    // stderr wrapper berisi diagnostik "[DEBUG...]" — disaring di sini
    // (bukan error); error beneran ("ERROR: ...") diteruskan dengan
    // prefix "[ERROR] " ala C# onError.
    let tx_out = tx.clone();
    let t_out = std::thread::spawn(move || {
        let r = BufReader::new(stdout);
        for line in r.lines() {
            match line {
                Ok(l) => {
                    if tx_out.send(BatchExecMsg::Line(l)).is_err() {
                        break;
                    }
                }
                Err(_) => break,
            }
        }
    });
    let tx_err = tx.clone();
    let t_err = std::thread::spawn(move || {
        let r = BufReader::new(stderr);
        for line in r.lines() {
            match line {
                Ok(l) => {
                    let t = l.trim_start();
                    if t.starts_with("[DEBUG") {
                        continue;
                    }
                    if tx_err
                        .send(BatchExecMsg::Line(format!("[ERROR] {}", l)))
                        .is_err()
                    {
                        break;
                    }
                }
                Err(_) => break,
            }
        }
    });
    let status = child.wait();
    let _ = t_out.join();
    let _ = t_err.join();
    *handle.current_pid.lock().unwrap() = None;
    match status {
        Ok(s) => s.code(),
        Err(_) => None,
    }
}

/// Jalankan script untuk SEMUA root berurutan (C# `ExecuteMaster`).
/// Dipanggil sekali dari thread worker baru; mengirim event ke channel
/// yang dibaca timer UI. `roots`: (source_path, output_path) per root.
#[allow(clippy::too_many_arguments)]
pub fn start_batch_run(
    handle: Arc<BatchRunHandle>,
    wrapper: std::path::PathBuf,
    target: String,
    roots: Vec<(String, String)>,
    master: String,
    master2: String,
    master3: String,
    output_base: String,
    okebase: String,
    user_name: String,
) {
    handle.running.store(true, Ordering::SeqCst);
    handle.stop_requested.store(false, Ordering::SeqCst);
    handle.failed.store(false, Ordering::SeqCst);
    *handle.script_file.lock().unwrap() = target.clone();
    let (tx, rx) = channel::<BatchExecMsg>();
    *handle.rx.lock().unwrap() = Some(rx);
    let python = python_cmd();
    std::thread::spawn(move || {
        let _ = tx.send(BatchExecMsg::Line(format!(
            "[INFO] Memulai Batch Master: {}",
            target
        )));
        for (src, _out) in &roots {
            if handle.stop_requested.load(Ordering::SeqCst) {
                let _ = tx.send(BatchExecMsg::Stopped);
                handle.running.store(false, Ordering::SeqCst);
                return;
            }
            let _ = tx.send(BatchExecMsg::Line(format!(
                "[INFO] Memproses: {}...",
                src
            )));
            let code = run_one_root(
                python,
                &wrapper,
                &target,
                src,
                &master,
                &master2,
                &master3,
                &output_base,
                &okebase,
                &user_name,
                &tx,
                &handle,
            );
            if handle.stop_requested.load(Ordering::SeqCst) {
                let _ = tx.send(BatchExecMsg::Stopped);
                handle.running.store(false, Ordering::SeqCst);
                return;
            }
            match code {
                Some(c) => {
                    if c != 0 {
                        handle.failed.store(true, Ordering::SeqCst);
                    }
                    let _ = tx.send(BatchExecMsg::RootDone {
                        root: src.clone(),
                        code: c,
                    });
                }
                None => {
                    handle.failed.store(true, Ordering::SeqCst);
                    let _ = tx.send(BatchExecMsg::Error(format!(
                        "Gagal menjalankan wrapper untuk {}",
                        src
                    )));
                }
            }
        }
        let _ = tx.send(BatchExecMsg::AllDone);
        handle.running.store(false, Ordering::SeqCst);
    });
}

// ---------------------------------------------------------------------------
// Daftar script master (tombol footer) — scan Scripts/Master/*.py
// ---------------------------------------------------------------------------

/// Satu entri script master (tombol footer): nama tampil + nama file.
#[derive(Debug, Clone)]
pub struct BatchScriptEntry {
    pub display: String,
    pub filename: String,
}

/// Scan `Scripts/Master/*.py` ala C# `LoadScriptsAsync`: lewati `_*.py`
/// (helper) dan `buat_master_duplikat.py` (tombol sendiri di panel
/// OUTPUT); nama + urutan dari `scripts.json` bila ada (alias Name /
/// Order), kalau tidak nama file tanpa ekstensi, order 9999.
pub fn scan_master_scripts(scripts_dir: &std::path::Path) -> Vec<BatchScriptEntry> {
    let mut aliases: std::collections::HashMap<String, (String, i64)> =
        std::collections::HashMap::new();
    if let Ok(text) = std::fs::read_to_string(scripts_dir.join("scripts.json")) {
        if let Ok(json) = serde_json::from_str::<serde_json::Value>(&text) {
            if let Some(obj) = json.as_object() {
                for (k, v) in obj {
                    let name = v
                        .get("Name")
                        .and_then(|n| n.as_str())
                        .unwrap_or("")
                        .to_string();
                    let order = v.get("Order").and_then(|n| n.as_i64()).unwrap_or(9999);
                    aliases.insert(k.clone(), (name, order));
                }
            }
        }
    }
    let master_dir = scripts_dir.join("Master");
    let mut entries: Vec<(BatchScriptEntry, i64)> = Vec::new();
    if let Ok(rd) = std::fs::read_dir(&master_dir) {
        for e in rd.flatten() {
            let fname = e.file_name().to_string_lossy().into_owned();
            if !fname.ends_with(".py") || fname.starts_with('_') {
                continue;
            }
            if fname.eq_ignore_ascii_case("buat_master_duplikat.py") {
                continue;
            }
            if !e.file_type().map(|t| t.is_file()).unwrap_or(false) {
                continue;
            }
            let (display, order) = match aliases.get(&fname) {
                Some((n, o)) if !n.is_empty() => (n.clone(), *o),
                _ => (fname.trim_end_matches(".py").to_string(), 9999),
            };
            entries.push((BatchScriptEntry { display, filename: fname }, order));
        }
    }
    entries.sort_by(|a, b| a.1.cmp(&b.1).then_with(|| a.0.display.cmp(&b.0.display)));
    entries.into_iter().map(|(e, _)| e).collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn write_file(path: &std::path::Path, content: &str) {
        if let Some(p) = path.parent() {
            std::fs::create_dir_all(p).unwrap();
        }
        std::fs::write(path, content).unwrap();
    }

    fn tmpdir(name: &str) -> std::path::PathBuf {
        let d = std::env::temp_dir().join(format!(
            "bmachine-batch-exec-test-{}-{}",
            name,
            std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .unwrap()
                .as_nanos()
        ));
        std::fs::create_dir_all(&d).unwrap();
        d
    }

    #[test]
    fn scan_master_scripts_skips_helpers_and_duplikat() {
        let d = tmpdir("scan");
        let m = d.join("Master");
        write_file(&m.join("buat_master.py"), "# stub\n");
        write_file(&m.join("manasik.py"), "# stub\n");
        write_file(&m.join("buat_master_duplikat.py"), "# stub\n");
        write_file(&m.join("_photoshop_utils.py"), "# helper\n");
        write_file(&m.join("tambahan_flat.py"), "# stub\n");
        write_file(
            &d.join("scripts.json"),
            r#"{"manasik.py": {"Name": "MANASIK", "Order": 1}, "buat_master.py": {"Name": "BUAT MASTER", "Order": 8}}"#,
        );
        let list = scan_master_scripts(&d);
        let names: Vec<&str> = list.iter().map(|e| e.display.as_str()).collect();
        assert_eq!(names, vec!["MANASIK", "BUAT MASTER", "tambahan_flat"]);
        assert_eq!(list[0].filename, "manasik.py");
        assert_eq!(list[2].filename, "tambahan_flat.py");
    }

    #[test]
    fn scan_master_scripts_no_json_falls_back_to_filename() {
        let d = tmpdir("scan2");
        let m = d.join("Master");
        write_file(&m.join("wisuda.py"), "# stub\n");
        write_file(&m.join("pasfoto.py"), "# stub\n");
        let list = scan_master_scripts(&d);
        let names: Vec<&str> = list.iter().map(|e| e.display.as_str()).collect();
        assert_eq!(names, vec!["pasfoto", "wisuda"]);
    }

    #[test]
    fn kill_process_tree_dead_pid_is_harmless() {
        // PID yang hampir pasti sudah tidak ada — tidak boleh panic.
        kill_process_tree(999_999);
    }

    #[cfg(not(target_os = "windows"))]
    fn proc_is_dead(pid: u32) -> bool {
        match std::fs::read_to_string(format!("/proc/{}/stat", pid)) {
            Err(_) => true, // sudah tidak ada
            Ok(stat) => {
                // state 'Z' = zombie = sudah mati (tinggal nunggu reaping).
                let Some(close) = stat.rfind(')') else {
                    return false;
                };
                stat[close + 1..]
                    .split_whitespace()
                    .next()
                    .map(|s| s == "Z")
                    .unwrap_or(false)
            }
        }
    }

    #[test]
    #[cfg(not(target_os = "windows"))]
    fn descendants_finds_spawned_child() {
        let mut child = Command::new("sleep")
            .arg("60")
            .spawn()
            .expect("sleep tersedia di Linux");
        let pid = child.id();
        let found = descendants(std::process::id());
        assert!(
            found.contains(&pid),
            "descendants harus memuat sleep {:?}",
            found
        );
        kill_process_tree(pid);
        let mut dead = false;
        for _ in 0..20 {
            if proc_is_dead(pid) {
                dead = true;
                break;
            }
            std::thread::sleep(std::time::Duration::from_millis(100));
        }
        assert!(dead, "sleep harus mati setelah kill_process_tree");
        let _ = child.wait();
    }

    #[test]
    #[cfg(not(target_os = "windows"))]
    fn kill_process_tree_kills_grandchild_too() {
        // sh -c 'sleep 60 & wait' -> sh (anak) + sleep (cucu).
        let mut child = Command::new("sh")
            .args(["-c", "sleep 60 & wait"])
            .spawn()
            .expect("sh tersedia");
        let sh_pid = child.id();
        std::thread::sleep(std::time::Duration::from_millis(300));
        let grandkids = descendants(sh_pid);
        assert_eq!(grandkids.len(), 1, "sh harus punya 1 anak sleep");
        let sleep_pid = grandkids[0];
        kill_process_tree(sh_pid);
        let mut dead = false;
        for _ in 0..20 {
            if proc_is_dead(sh_pid) && proc_is_dead(sleep_pid) {
                dead = true;
                break;
            }
            std::thread::sleep(std::time::Duration::from_millis(100));
        }
        assert!(
            dead,
            "sh dan sleep cucu harus mati setelah kill_process_tree"
        );
        let _ = child.wait();
    }
}
