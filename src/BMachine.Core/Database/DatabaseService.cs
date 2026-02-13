using BMachine.SDK;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace BMachine.Core.Database;

public class DatabaseService : IDatabase, IActivityService
{
    private readonly string _connectionString;
    
    public DatabaseService(string databasePath = "BMachine.db")
    {
        // Use Platform Service to get persistent app data path
        var platform = BMachine.Core.Platform.PlatformServiceFactory.Get();
        var appData = platform.GetAppDataDirectory();
        
        var fullPath = Path.Combine(appData, databasePath);
        DatabasePath = fullPath;
        
        _connectionString = $"Data Source={fullPath}";
        InitializeDatabase();
    }

    public string DatabasePath { get; private set; }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // Clear Cache on Startup (Session-based)
        // We only clear entries that are marked as 'Cache' type or have specific keys if we want to be selective.
        // But user asked for "cache expiry saat aplikasi close". Since persistence is needed for "offline fallback", 
        // actually we should NOT clear it immediately on startup if we want to show last valid data until we fetch new one?
        // Wait, User said: "cache expiry muungkin dibuat saat jam 12 malam saja mungkin, atau saat aplikasi di close saja ya?"
        // Me: "Saya akan set agar Persistent (tetap ada meski di-close) tapi di-refresh otomatis saat App dibuka."
        // So I will NOT clear it here. I will let the Service layer overwrite it.
        // But I will add the Table Create command just to be sure it matches.
        
        var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS KeyValueStore (
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
            );
        ";
        command.ExecuteNonQuery();
    }

    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM KeyValueStore WHERE Key = $key";
        command.Parameters.AddWithValue("$key", key);

        var result = await command.ExecuteScalarAsync();
        if (result is string json)
        {
            try 
            {
                return JsonSerializer.Deserialize<T>(json);
            }
            catch 
            {
                return null;
            }
        }
        return null;
    }

    public async Task SetAsync<T>(string key, T value) where T : class
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var json = JsonSerializer.Serialize(value);
        var type = typeof(T).FullName ?? "Unknown";

        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO KeyValueStore (Key, Value, Type, UpdatedAt) 
            VALUES ($key, $value, $type, CURRENT_TIMESTAMP)
            ON CONFLICT(Key) DO UPDATE SET 
                Value = excluded.Value, 
                Type = excluded.Type, 
                UpdatedAt = excluded.UpdatedAt;";
        
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", json);
        command.Parameters.AddWithValue("$type", type);

        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(string key)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM KeyValueStore WHERE Key = $key";
        command.Parameters.AddWithValue("$key", key);
        
        await command.ExecuteNonQueryAsync();
    }

    // This is a naive implementation of Query for InMemory filtering
    // Ideally we map predicates to SQL but that's complex for generic key-value
    public async Task<IEnumerable<T>> QueryAsync<T>(Func<T, bool> predicate) where T : class
    {
         using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM KeyValueStore WHERE Type = $type";
        command.Parameters.AddWithValue("$type", typeof(T).FullName ?? "Unknown");

        var results = new List<T>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var json = reader.GetString(0);
            try
            {
                var item = JsonSerializer.Deserialize<T>(json);
                if (item != null && predicate(item))
                {
                    results.Add(item);
                }
             }
             catch { /* Ignore serialization errors */ }
        }
        
        return results;
    }
    
    // IActivityService Implementation
    public async Task LogAsync(string type, string title, string description)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Activities (Type, Title, Description, CreatedAt)
            VALUES ($type, $title, $description, CURRENT_TIMESTAMP)";
            
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$description", description);
        
        await command.ExecuteNonQueryAsync();
    }
    
    public async Task<int> GetCountAsync(string type)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Activities WHERE Type = $type";
        command.Parameters.AddWithValue("$type", type);
        
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task<IEnumerable<ActivityLog>> GetRecentAsync(int count = 10)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Type, Title, Description, CreatedAt FROM Activities ORDER BY Id DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", count);
        
        var results = new List<ActivityLog>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new ActivityLog
            {
                Id = reader.GetInt32(0),
                Type = reader.GetString(1),
                Title = reader.GetString(2),
                Description = reader.GetString(3),
                CreatedAt = DateTime.Parse(reader.GetString(4))
            });
        }
        return results;
    }
    public async Task ClearAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Activities";
        await command.ExecuteNonQueryAsync();
    }
}
