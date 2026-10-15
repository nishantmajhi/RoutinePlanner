using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using RoutinePlanner.Web.Models;

namespace RoutinePlanner.Web.Data;

/// <summary>
/// Talks directly to App_Data/campus.sqlite3 — the exact same file the Express app used.
/// Mirrors routes/getAllTeachersData.js and routes/teacherCRUD.js query-for-query.
/// </summary>
public class TeacherRepository
{
    private readonly string _connectionString;

    public TeacherRepository(IConfiguration config, IWebHostEnvironment env)
    {
        var relativePath = config["RoutinePlanner:CampusDbPath"] ?? "App_Data/campus.sqlite3";
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
            create.CommandText = @"CREATE TABLE IF NOT EXISTS teachers (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                salutation TEXT,
                name TEXT,
                subjects_array TEXT,
                availability_json TEXT
            )";
            create.ExecuteNonQuery();
        }

        return conn;
    }

    /// <summary>Loads every teacher row, keyed by id — for GET /teachers (dictionary shape).</summary>
    public Dictionary<long, TeacherDto> GetAllAsDto()
    {
        var result = new Dictionary<long, TeacherDto>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, salutation, name, subjects_array, availability_json FROM teachers";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            result[id] = new TeacherDto
            {
                TeacherID = id,
                Salutation = reader.IsDBNull(1) ? null : reader.GetString(1),
                FullName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                SubjectsArray = reader.IsDBNull(3) ? null : JsonSerializer.Deserialize<List<string>>(reader.GetString(3)),
                AvailabilityJson = reader.IsDBNull(4)
                    ? null
                    : JsonSerializer.Deserialize<Dictionary<string, List<AvailabilityWindow>>>(reader.GetString(4)),
            };
        }
        return result;
    }

    /// <summary>Loads every teacher row into the engine's working shape — mirrors model/Loader.js.</summary>
    public Dictionary<long, Teacher> LoadTeachersForEngine()
    {
        var result = new Dictionary<long, Teacher>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, salutation, name, subjects_array, availability_json FROM teachers";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            result[id] = new Teacher
            {
                Id = id,
                Salutation = reader.IsDBNull(1) ? null : reader.GetString(1),
                Name = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Subjects = reader.IsDBNull(3)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? new List<string>(),
                Availability = reader.IsDBNull(4)
                    ? new Dictionary<string, List<AvailabilityWindow>>()
                    : JsonSerializer.Deserialize<Dictionary<string, List<AvailabilityWindow>>>(reader.GetString(4))
                      ?? new Dictionary<string, List<AvailabilityWindow>>(),
            };
        }
        return result;
    }

    public TeacherDto? GetById(long teacherId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, salutation, name, subjects_array, availability_json FROM teachers WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", teacherId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new TeacherDto
        {
            TeacherID = reader.GetInt64(0),
            Salutation = reader.IsDBNull(1) ? null : reader.GetString(1),
            FullName = reader.IsDBNull(2) ? "" : reader.GetString(2),
            SubjectsArray = reader.IsDBNull(3) ? null : JsonSerializer.Deserialize<List<string>>(reader.GetString(3)),
            AvailabilityJson = reader.IsDBNull(4)
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, List<AvailabilityWindow>>>(reader.GetString(4)),
        };
    }

    public long Add(AddTeacherRequest req)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO teachers (salutation, name, subjects_array, availability_json)
                             VALUES ($salutation, $name, $subjects, $availability);
                             SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$salutation", (object?)req.Salutation ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", req.Name ?? "");
        cmd.Parameters.AddWithValue("$subjects", req.SubjectsArray is null ? DBNull.Value : JsonSerializer.Serialize(req.SubjectsArray));
        cmd.Parameters.AddWithValue("$availability", req.AvailabilityJson is null ? DBNull.Value : JsonSerializer.Serialize(req.AvailabilityJson));

        var result = cmd.ExecuteScalar();
        return Convert.ToInt64(result);
    }

    /// <summary>The only columns <see cref="Update"/> will write. Values are parameterised, but
    /// column names are interpolated into the SET clause and cannot be — so they are checked
    /// against this list instead of being trusted. Today's only caller passes a fixed set of four
    /// literals; this is so that a caller which one day forwards a client-supplied field name
    /// cannot turn the clause into arbitrary SQL.</summary>
    private static readonly HashSet<string> UpdatableColumns = new(StringComparer.Ordinal)
    {
        "salutation", "name", "subjects_array", "availability_json",
    };

    /// <summary>
    /// Partial update. `updates` maps column name -> new value (string or null),
    /// pre-serialized to JSON where needed by the caller — mirrors the dynamic
    /// SET clause built in routes/teacherCRUD.js PUT /teacher/update.
    /// </summary>
    public bool Update(long teacherId, IReadOnlyDictionary<string, object?> updates)
    {
        if (updates.Count == 0) return false;

        var unknown = updates.Keys.FirstOrDefault(column => !UpdatableColumns.Contains(column));
        if (unknown is not null)
        {
            throw new ArgumentException($"\"{unknown}\" is not an updatable teacher column.", nameof(updates));
        }

        using var conn = Open();
        using var cmd = conn.CreateCommand();

        var setClauses = new List<string>();
        var i = 0;
        foreach (var (column, value) in updates)
        {
            var paramName = $"$p{i++}";
            setClauses.Add($"{column} = {paramName}");
            cmd.Parameters.AddWithValue(paramName, value ?? DBNull.Value);
        }

        cmd.CommandText = $"UPDATE teachers SET {string.Join(", ", setClauses)} WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", teacherId);

        var affected = cmd.ExecuteNonQuery();
        return affected > 0;
    }

    public bool Remove(long teacherId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM teachers WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", teacherId);
        return cmd.ExecuteNonQuery() > 0;
    }
}
