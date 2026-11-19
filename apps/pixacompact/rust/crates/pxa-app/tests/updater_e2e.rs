//! E2E cek update LAN terhadap manifest asli (folder via env).
#[test]
fn check_real_manifest() {
    let folder = std::env::var("PX_UPDATER_E2E_FOLDER").unwrap_or_default();
    if folder.is_empty() {
        eprintln!("SKIP: set PX_UPDATER_E2E_FOLDER");
        return;
    }
    let (info, status) = pxa_app::updater::check(&folder, false);
    eprintln!("status: {status}");
    assert!(info.is_update_available, "status={status}");
    assert_eq!(info.latest_version, "9.9.9");
    assert!(info.notes.iter().any(|n| n.contains("Perbaikan")));
    assert!(info.size_bytes > 0);
}
