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
        )?;

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
}
