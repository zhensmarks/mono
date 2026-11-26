use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};

pub struct ScriptExecutor;

#[allow(dead_code)]
impl ScriptExecutor {
    /// Menjalankan script master Python persis pola BatchViewModel.cs & batch_wrapper.py
    pub fn run_python_batch(
        target_script: &str,
        source_path: &str,
        master1: &str,
        master2: &str,
        master3: Option<&str>,
        output_base: &str,
        oke_base: &str,
        user_name: &str,
    ) -> Result<i32, String> {
        let base_dir = std::env::current_dir().unwrap_or_else(|_| PathBuf::from("."));
        let wrapper = base_dir.join("Scripts").join("batch_wrapper.py");

        let mut cmd = Command::new("python");
        cmd.arg(&wrapper)
            .arg("--target").arg(target_script)
            .arg("--pilihan").arg(source_path)
            .arg("--master").arg(master1)
            .arg("--master2").arg(master2)
            .arg("--output").arg(output_base)
            .arg("--okebase").arg(oke_base)
            .env("BMACHINE_USER_NAME", user_name)
            .stdout(Stdio::piped())
            .stderr(Stdio::piped());

        if let Some(m3) = master3 {
            if !m3.is_empty() {
                cmd.arg("--master3").arg(m3);
            }
        }

        match cmd.spawn() {
            Ok(mut child) => {
                match child.wait() {
                    Ok(status) => Ok(status.code().unwrap_or(-1)),
                    Err(e) => Err(format!("Gagal menunggu proses python: {}", e)),
                }
            }
            Err(e) => Err(format!("Gagal meluncurkan python: {}", e)),
        }
    }

    /// Meluncurkan file Photoshop atau skrip JSX ke Photoshop
    pub fn open_in_photoshop(ps_path: Option<&str>, target_file: &str) -> Result<(), String> {
        if let Some(path) = ps_path {
            if Path::new(path).exists() {
                Command::new(path)
                    .arg(target_file)
                    .spawn()
                    .map_err(|e| format!("Gagal membuka via executable Photoshop: {}", e))?;
                return Ok(());
            }
        }

        // Fallback default Windows association (rundll32 / cmd start)
        Command::new("cmd")
            .args(&["/c", "start", "", target_file])
            .spawn()
            .map_err(|e| format!("Gagal membuka default shell: {}", e))?;
        Ok(())
    }
}
