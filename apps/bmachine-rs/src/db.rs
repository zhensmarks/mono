use rusqlite::{Connection, Result, params};
use std::fs;
use std::path::{Path, PathBuf};

pub struct DatabaseService {
    conn: Connection,
    #[allow(dead_code)]
    pub database_path: PathBuf,
}

impl DatabaseService {
    pub fn init() -> Result<Self> {
        let app_data = std::env::var("APPDATA").unwrap_or_else(|_| ".".to_string());
        let dir = Path::new(&app_data).join("BMachine");
        let _ = fs::create_dir_all(&dir);
        let db_path = dir.join("BMachine.db");

        let conn = Connection::open(&db_path)?;

        // WAL mode & timeout 15s persis DatabaseService.cs
        let _ = conn.execute_batch("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=15000;");

        create_tables(&conn)?;

        Ok(Self {
            conn,
            database_path: db_path,
        })
    }

    pub fn get(&self, key: &str) -> Option<String> {
        let mut stmt = self
            .conn
            .prepare("SELECT Value FROM KeyValueStore WHERE Key = ?1")
            .ok()?;
        let val: Result<String> = stmt.query_row(params![key], |row| row.get(0));
        val.ok()
    }

    pub fn set(&mut self, key: &str, value: &str, val_type: &str) -> Result<()> {
        self.conn.execute(
            "INSERT INTO KeyValueStore (Key, Value, Type, UpdatedAt)
             VALUES (?1, ?2, ?3, CURRENT_TIMESTAMP)
             ON CONFLICT(Key) DO UPDATE SET Value = ?2, Type = ?3, UpdatedAt = CURRENT_TIMESTAMP",
            params![key, value, val_type],
        )?;
        Ok(())
    }

    /// Catat satu aktivitas (mis. laporan bug dari ReportBugDialog).
    pub fn insert_activity(
        &mut self,
        user_id: &str,
        act_type: &str,
        title: &str,
        description: &str,
    ) -> Result<()> {
        self.conn.execute(
            "INSERT INTO Activities (UserId, Type, Title, Description) VALUES (?1, ?2, ?3, ?4)",
            params![user_id, act_type, title, description],
        )?;
        Ok(())
    }

    /// Salin konsisten DB aktif ke file tujuan (VACUUM INTO — aman
    /// meski koneksi sedang terbuka; gagal bila file tujuan sudah ada).
    /// Folder induk dibuat dulu bila belum ada.
    pub fn backup_to(&self, dest: &Path) -> Result<()> {
        if let Some(parent) = dest.parent() {
            if !parent.as_os_str().is_empty() {
                let _ = fs::create_dir_all(parent);
            }
        }
        self.conn.execute(
            "VACUUM INTO ?1",
            params![dest.to_string_lossy().to_string()],
        )?;
        Ok(())
    }

    /// Validasi file import: harus file SQLite yang punya tabel
    /// KeyValueStore (database BMachine). Pesan error berbahasa
    /// Indonesia yang jelas — bukan pesan mentah rusqlite.
    pub fn validate_bmachine_db(path: &Path) -> std::result::Result<(), String> {
        let mut f = fs::File::open(path)
            .map_err(|e| format!("File bukan database BMachine yang valid (tidak bisa dibuka: {})", e))?;
        use std::io::Read;
        let mut header = [0u8; 16];
        f.read_exact(&mut header).map_err(|_| {
            "File bukan database BMachine yang valid (bukan file SQLite)".to_string()
        })?;
        if &header != b"SQLite format 3\0" {
            return Err(
                "File bukan database BMachine yang valid (bukan file SQLite)".to_string(),
            );
        }
        let src = Connection::open_with_flags(
            path,
            rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY,
        )
        .map_err(|e| format!("File bukan database BMachine yang valid (tidak bisa dibuka: {})", e))?;
        let has_table: bool = src
            .query_row(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='KeyValueStore'",
                [],
                |row| row.get::<_, i64>(0),
            )
            .map(|n| n > 0)
            .unwrap_or(false);
        if !has_table {
            return Err(
                "File bukan database BMachine yang valid (tabel KeyValueStore tidak ditemukan)"
                    .to_string(),
            );
        }
        Ok(())
    }

    /// Salin semua baris KeyValueStore dari file DB lain (dibuka
    /// read-only) ke DB aktif, dalam SATU transaksi (semua-atau-
    /// tidak-sama-sekali). File divalidasi dulu. Mengembalikan jumlah
    /// baris disalin. Bila ada baris yang gagal dibaca/ditulis,
    /// transaksi di-rollback dan DB aktif tetap utuh.
    pub fn import_key_values(&mut self, path: &Path) -> std::result::Result<usize, String> {
        Self::validate_bmachine_db(path)?;
        let src = Connection::open_with_flags(
            path,
            rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY,
        )
        .map_err(|e| format!("Gagal membuka file import: {}", e))?;
        let mut stmt = src
            .prepare("SELECT Key, Value, Type FROM KeyValueStore")
            .map_err(|e| format!("Gagal membaca file import: {}", e))?;
        let rows: Vec<(String, String, String)> = stmt
            .query_map([], |row| Ok((row.get(0)?, row.get(1)?, row.get(2)?)))
            .map_err(|e| format!("Gagal membaca file import: {}", e))?
            .collect::<Result<Vec<_>, _>>()
            .map_err(|e| format!("Gagal membaca file import: {}", e))?;
        let count = rows.len();
        let tx = self
            .conn
            .transaction()
            .map_err(|e| format!("Gagal memulai import: {}", e))?;
        for (k, v, t) in &rows {
            tx.execute(
                "INSERT INTO KeyValueStore (Key, Value, Type, UpdatedAt)
                 VALUES (?1, ?2, ?3, CURRENT_TIMESTAMP)
                 ON CONFLICT(Key) DO UPDATE SET Value = ?2, Type = ?3, UpdatedAt = CURRENT_TIMESTAMP",
                params![k, v, t],
            )
            .map_err(|e| format!("Gagal mengimpor pengaturan: {}", e))?;
        }
        tx.commit()
            .map_err(|e| format!("Gagal menyelesaikan import: {}", e))?;
        Ok(count)
    }

    /// Import database lengkap: validasi → backup DB aktif dulu
    /// (file .bak bertimestamp di samping DB aktif) → import dalam
    /// satu transaksi. Bila import gagal, DB aktif tetap utuh dan
    /// file .bak bisa dipakai memulihkan manual.
    pub fn import_database(&mut self, path: &Path) -> std::result::Result<ImportReport, String> {
        Self::validate_bmachine_db(path)?;
        let dir = self
            .database_path
            .parent()
            .map(|p| p.to_path_buf())
            .unwrap_or_else(|| PathBuf::from("."));
        let stamp = chrono::Local::now().format("%Y%m%d-%H%M%S");
        let bak = unique_path(
            &dir,
            format!("BMachine-sebelum-import-{}.bak", stamp),
        );
        self.backup_to(&bak)
            .map_err(|e| format!("Gagal mencadangkan database aktif sebelum import: {}", e))?;
        let n = self.import_key_values(path)?;
        Ok(ImportReport {
            imported: n,
            backup_path: bak,
        })
    }
}

/// Hasil import_database: jumlah pengaturan dipulihkan + lokasi file
/// cadangan DB aktif sebelum import.
#[derive(Debug)]
pub struct ImportReport {
    pub imported: usize,
    pub backup_path: PathBuf,
}

/// Nama file unik di dalam `dir`: bila sudah ada, tambah -2, -3, ...
/// (VACUUM INTO gagal bila file tujuan sudah ada).
pub fn unique_path(dir: &Path, file_name: String) -> PathBuf {
    let candidate = dir.join(&file_name);
    if !candidate.exists() {
        return candidate;
    }
    let (stem, ext) = match file_name.rfind('.') {
        Some(i) if i > 0 => (file_name[..i].to_string(), file_name[i..].to_string()),
        _ => (file_name.clone(), String::new()),
    };
    let mut n = 2;
    loop {
        let candidate = dir.join(format!("{}-{}{}", stem, n, ext));
        if !candidate.exists() {
            return candidate;
        }
        n += 1;
    }
}

fn create_tables(conn: &Connection) -> Result<()> {
    conn.execute_batch(
        "CREATE TABLE IF NOT EXISTS KeyValueStore (
            Key TEXT PRIMARY KEY,
            Value TEXT NOT NULL,
            Type TEXT NOT NULL,
            UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
        );
        CREATE TABLE IF NOT EXISTS Activities (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            UserId TEXT,
            Type TEXT,
            Title TEXT,
            Description TEXT,
            CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP
        );",
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    fn test_dir(name: &str) -> PathBuf {
        let dir = std::env::temp_dir()
            .join(format!("bmachine-db-test-{}-{}", name, std::process::id()));
        let _ = fs::create_dir_all(&dir);
        dir
    }

    fn open_test_db(path: &Path) -> DatabaseService {
        let _ = fs::remove_file(path);
        let conn = Connection::open(path).unwrap();
        create_tables(&conn).unwrap();
        DatabaseService {
            conn,
            database_path: path.to_path_buf(),
        }
    }

    fn open_existing_db(path: &Path) -> DatabaseService {
        let conn = Connection::open(path).unwrap();
        create_tables(&conn).unwrap();
        DatabaseService {
            conn,
            database_path: path.to_path_buf(),
        }
    }

    /// Lokasi database ASLI abeng (hanya untuk tes lokal; file asli
    /// TIDAK PERNAH ditulis — tes selalu bekerja pada salinan di temp).
    fn real_db_source() -> Option<PathBuf> {
        let home = std::env::var("HOME").ok()?;
        let p = PathBuf::from(home)
            .join("workspace/user/files/BMachine.db");
        if p.exists() {
            Some(p)
        } else {
            None
        }
    }

    #[test]
    fn backup_and_import_roundtrip() {
        let dir = test_dir("roundtrip");
        let mut src = open_test_db(&dir.join("src.db"));
        src.set("Test.Key", "hello", "String").unwrap();
        src.insert_activity("abeng", "BugReport", "Judul tes", "Detail tes")
            .unwrap();
        let backup_path = dir.join("backup.db");
        let _ = fs::remove_file(&backup_path);
        src.backup_to(&backup_path).unwrap();
        assert!(backup_path.exists());

        let mut dst = open_test_db(&dir.join("dst.db"));
        let n = dst.import_key_values(&backup_path).unwrap();
        assert_eq!(n, 1);
        assert_eq!(dst.get("Test.Key").as_deref(), Some("hello"));
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn validate_rejects_non_database() {
        let dir = test_dir("validate");
        // File teks berekstensi .db
        let txt = dir.join("bukan.db");
        fs::write(&txt, "ini bukan database, hanya teks biasa").unwrap();
        let err = DatabaseService::validate_bmachine_db(&txt).unwrap_err();
        assert!(
            err.contains("bukan database BMachine yang valid"),
            "pesan tak jelas: {}",
            err
        );
        // File kosong
        let empty = dir.join("kosong.db");
        fs::write(&empty, b"").unwrap();
        assert!(DatabaseService::validate_bmachine_db(&empty).is_err());
        // SQLite valid tapi tanpa tabel KeyValueStore
        let nodb = dir.join("tanpa-kvs.db");
        let conn = Connection::open(&nodb).unwrap();
        conn.execute_batch("CREATE TABLE Activities (Id INTEGER PRIMARY KEY);")
            .unwrap();
        drop(conn);
        let err = DatabaseService::validate_bmachine_db(&nodb).unwrap_err();
        assert!(
            err.contains("KeyValueStore"),
            "pesan tak jelas: {}",
            err
        );
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn unique_path_appends_counter() {
        let dir = test_dir("unique");
        let first = unique_path(&dir, "BMachine-backup-20261127-120000.db".to_string());
        assert_eq!(first.file_name().unwrap(), "BMachine-backup-20261127-120000.db");
        fs::write(&first, b"x").unwrap();
        let second = unique_path(&dir, "BMachine-backup-20261127-120000.db".to_string());
        assert_eq!(
            second.file_name().unwrap(),
            "BMachine-backup-20261127-120000-2.db"
        );
        fs::write(&second, b"x").unwrap();
        let third = unique_path(&dir, "BMachine-backup-20261127-120000.db".to_string());
        assert_eq!(
            third.file_name().unwrap(),
            "BMachine-backup-20261127-120000-3.db"
        );
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn import_is_all_or_nothing_and_backs_up_first() {
        let dir = test_dir("atomic");
        // DB aktif berisi 1 pengaturan yang harus selamat.
        let mut live = open_test_db(&dir.join("live.db"));
        live.set("Tetap.Ada", "jangan-hilang", "String").unwrap();

        // File import VALID (SQLite + tabel KeyValueStore) tapi berisi
        // 1 baris baik + 1 baris rusak (Key NULL) → import harus gagal
        // total, tanpa satu baris pun tertulis ke DB aktif.
        let bad = dir.join("rusak.db");
        let conn = Connection::open(&bad).unwrap();
        create_tables(&conn).unwrap();
        conn.execute(
            "INSERT INTO KeyValueStore (Key, Value, Type) VALUES ('Baik', 'v', 'String')",
            [],
        )
        .unwrap();
        conn.execute(
            "INSERT INTO KeyValueStore (Key, Value, Type) VALUES (NULL, 'v', 'String')",
            [],
        )
        .unwrap();
        drop(conn);

        let err = live.import_database(&bad).unwrap_err();
        assert!(!err.is_empty(), "import rusak harus mengembalikan error");
        // DB aktif utuh: pengaturan lama ada, baris baik TIDAK ikut masuk.
        assert_eq!(live.get("Tetap.Ada").as_deref(), Some("jangan-hilang"));
        assert_eq!(live.get("Baik"), None);
        // Backup .bak dibuat SEBELUM import gagal — berisi keadaan awal.
        let baks: Vec<_> = fs::read_dir(&dir)
            .unwrap()
            .filter_map(|e| e.ok())
            .map(|e| e.file_name().to_string_lossy().to_string())
            .filter(|n| n.starts_with("BMachine-sebelum-import-") && n.ends_with(".bak"))
            .collect();
        assert_eq!(baks.len(), 1, "harus ada tepat 1 file .bak: {:?}", baks);
        let bak_conn = Connection::open(dir.join(&baks[0])).unwrap();
        let n: i64 = bak_conn
            .query_row("SELECT COUNT(*) FROM KeyValueStore", [], |r| r.get(0))
            .unwrap();
        assert_eq!(n, 1, "backup harus berisi 1 baris awal");
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn import_rejects_non_database_and_keeps_live_db() {
        let dir = test_dir("reject");
        let mut live = open_test_db(&dir.join("live.db"));
        live.set("Tetap.Ada", "jangan-hilang", "String").unwrap();
        let txt = dir.join("palsu.db");
        fs::write(&txt, "bukan sqlite sama sekali").unwrap();
        let err = live.import_database(&txt).unwrap_err();
        assert!(
            err.contains("bukan database BMachine yang valid"),
            "pesan tak jelas: {}",
            err
        );
        // Validasi gagal sebelum backup/import → DB aktif utuh, tanpa .bak.
        assert_eq!(live.get("Tetap.Ada").as_deref(), Some("jangan-hilang"));
        let baks = fs::read_dir(&dir)
            .unwrap()
            .filter(|e| {
                e.as_ref()
                    .map(|e| e.file_name().to_string_lossy().contains(".bak"))
                    .unwrap_or(false)
            })
            .count();
        assert_eq!(baks, 0, "file tidak valid tidak boleh memicu backup");
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn transaction_rolls_back_on_drop() {
        // Bukti mekanisme: transaksi yang di-drop tanpa commit tidak
        // meninggalkan jejak — inilah yang dipakai import_key_values.
        let dir = test_dir("tx");
        let mut db = open_test_db(&dir.join("live.db"));
        {
            let tx = db.conn.transaction().unwrap();
            tx.execute(
                "INSERT INTO KeyValueStore (Key, Value, Type) VALUES ('Sementara', 'v', 'String')",
                [],
            )
            .unwrap();
            // sengaja tidak commit — drop = rollback
        }
        let n: i64 = db
            .conn
            .query_row(
                "SELECT COUNT(*) FROM KeyValueStore WHERE Key='Sementara'",
                [],
                |r| r.get(0),
            )
            .unwrap();
        assert_eq!(n, 0, "baris dalam transaksi yang dibatalkan harus hilang");
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn real_database_backup_import_roundtrip() {
        // Uji terhadap database ASLI abeng (salinan — file asli tidak
        // pernah dibuka untuk tulis). Skip bila file tidak ada di mesin
        // ini (mis. CI / PC lain).
        let Some(src_path) = real_db_source() else {
            eprintln!("SKIP: database asli tidak ada di ~/workspace/user/files/BMachine.db");
            return;
        };
        let orig_len = fs::metadata(&src_path).unwrap().len();
        let dir = test_dir("realdb");
        let copy = dir.join("BMachine-copy.db");
        fs::copy(&src_path, &copy).unwrap();

        // Export (VACUUM INTO) dari salinan DB asli.
        let live = open_existing_db(&copy);
        let backup = dir.join("export.db");
        live.backup_to(&backup).unwrap();
        assert!(backup.exists());

        // File hasil export valid + isinya sama dengan aslinya.
        let bak_conn = Connection::open(&backup).unwrap();
        let kvs: i64 = bak_conn
            .query_row("SELECT COUNT(*) FROM KeyValueStore", [], |r| r.get(0))
            .unwrap();
        let act: i64 = bak_conn
            .query_row("SELECT COUNT(*) FROM Activities", [], |r| r.get(0))
            .unwrap();
        assert_eq!(kvs, 166, "KeyValueStore hasil export harus 166 baris");
        assert_eq!(act, 7674, "Activities hasil export harus 7674 baris");
        let sample: String = bak_conn
            .query_row(
                "SELECT Value FROM KeyValueStore WHERE Key='Appearance.Background.Dark'",
                [],
                |r| r.get(0),
            )
            .unwrap();
        let orig_val: String = Connection::open(&copy)
            .unwrap()
            .query_row(
                "SELECT Value FROM KeyValueStore WHERE Key='Appearance.Background.Dark'",
                [],
                |r| r.get(0),
            )
            .unwrap();
        assert_eq!(sample, orig_val, "nilai contoh harus sama dengan aslinya");
        drop(bak_conn);

        // Import dari file export ke DB kosong → 166 baris, nilai cocok.
        let mut dst = open_test_db(&dir.join("dst.db"));
        let n = dst.import_key_values(&backup).unwrap();
        assert_eq!(n, 166);
        assert_eq!(dst.get("Appearance.Background.Dark").as_deref(), Some(orig_val.as_str()));

        // File asli tidak berubah (hanya dibaca + disalin).
        assert_eq!(fs::metadata(&src_path).unwrap().len(), orig_len);
        let _ = fs::remove_dir_all(&dir);
    }
}
