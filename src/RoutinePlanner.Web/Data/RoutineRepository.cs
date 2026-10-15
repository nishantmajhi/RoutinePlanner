using System.Text.Json;
using Microsoft.Data.Sqlite;
using RoutinePlanner.Web.Models;

namespace RoutinePlanner.Web.Data;

/// <summary>
/// Talks directly to App_Data/routine.sqlite3 — same file/table the Express app used
/// (`CREATE TABLE IF NOT EXISTS routine (id INTEGER PRIMARY KEY AUTOINCREMENT, json TEXT)`).
/// Mirrors routes/generate.js's saveRoutine() and routes/routine.js's readers.
/// </summary>
public class RoutineRepository
{
    private readonly string _connectionString;

    public RoutineRepository(IConfiguration config, IWebHostEnvironment env)
    {
        var relativePath = config["RoutinePlanner:RoutineDbPath"] ?? "App_Data/routine.sqlite3";
        var fullPath = Path.Combine(env.ContentRootPath, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        _connectionString = $"Data Source={fullPath}";
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using (var create = conn.CreateCommand())
        {
            create.CommandText = "CREATE TABLE IF NOT EXISTS routine (id INTEGER PRIMARY KEY AUTOINCREMENT, json TEXT)";
            create.ExecuteNonQuery();
        }

        return conn;
    }

    public long Save(FormattedRoutine routine)
    {
        using var conn = Open();

        using var insert = conn.CreateCommand();
        insert.CommandText = "INSERT INTO routine (json) VALUES ($json); SELECT last_insert_rowid();";
        insert.Parameters.AddWithValue("$json", JsonSerializer.Serialize(routine));
        var result = insert.ExecuteScalar();
        return Convert.ToInt64(result);
    }

    public long? GetLatestId()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM routine ORDER BY id DESC LIMIT 1";
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? null : Convert.ToInt64(result);
    }

    public FormattedRoutine? GetById(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT json FROM routine WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        var result = cmd.ExecuteScalar() as string;
        return result is null ? null : JsonSerializer.Deserialize<FormattedRoutine>(result);
    }
}
