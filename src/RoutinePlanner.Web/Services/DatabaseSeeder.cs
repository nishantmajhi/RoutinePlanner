using System.Text.Json;
using System.Text.Json.Serialization;
using RoutinePlanner.Web.Data;
using RoutinePlanner.Web.Models;

namespace RoutinePlanner.Web.Services;

/// <summary>
/// Fills an empty teachers table from App_Data/seed/teachers.seed.json.
///
/// This exists because campus.sqlite3 used to be committed to the repository, and it held real,
/// named academics with their subject allocations and weekly working hours. A demo database belongs
/// in version control; that one did not. It is also a binary file, so no change to it was ever
/// reviewable in a diff — which is the other half of why the seed is JSON.
///
/// It refuses to run against a table that already has rows unless asked twice, because the file the
/// app writes to and the file it seeds are the same one, and someone's real data is likely to be in
/// it. The archive this project lives in has a seeder that overwrote real client records twice.
/// </summary>
public static class DatabaseSeeder
{
    private sealed class SeedFile
    {
        [JsonPropertyName("teachers")]
        public List<AddTeacherRequest> Teachers { get; set; } = new();
    }

    public const string DefaultSeedPath = "App_Data/seed/teachers.seed.json";

    /// <summary>Exit code for the host: 0 on success, non-zero on refusal or error.</summary>
    public static int Run(
        TeacherRepository repository,
        IWebHostEnvironment env,
        IConfiguration config,
        bool force,
        TextWriter output)
    {
        var relativePath = config["RoutinePlanner:SeedPath"] ?? DefaultSeedPath;
        var seedPath = Path.Combine(env.ContentRootPath, relativePath);

        if (!File.Exists(seedPath))
        {
            output.WriteLine($"No seed file at {seedPath}.");
            return 1;
        }

        var existing = repository.GetAllAsDto();
        if (existing.Count > 0 && !force)
        {
            output.WriteLine(
                $"The teachers table already has {existing.Count} row(s). Refusing to add to it, because " +
                "this is the same database the running app writes to and those rows may be real. " +
                "Re-run with --force if you are sure.");
            return 2;
        }

        SeedFile? seed;
        try
        {
            seed = JsonSerializer.Deserialize<SeedFile>(File.ReadAllText(seedPath));
        }
        catch (JsonException ex)
        {
            output.WriteLine($"{relativePath} is not valid JSON: {ex.Message}");
            return 1;
        }

        if (seed is null || seed.Teachers.Count == 0)
        {
            output.WriteLine($"{relativePath} contains no teachers.");
            return 1;
        }

        // Validated with the same rules as POST /teacher/add, so the seed cannot introduce the sort
        // of malformed availability window that used to make every later generation fail.
        for (var i = 0; i < seed.Teachers.Count; i++)
        {
            var teacher = seed.Teachers[i];

            if (string.IsNullOrWhiteSpace(teacher.Name))
            {
                output.WriteLine($"teachers[{i}] has no name.");
                return 1;
            }

            var error = TeacherPayloadValidator.ValidateSubjects(teacher.SubjectsArray)
                        ?? TeacherPayloadValidator.ValidateAvailability(teacher.AvailabilityJson);

            if (error is not null)
            {
                output.WriteLine($"teachers[{i}] (\"{teacher.Name}\"): {error}");
                return 1;
            }
        }

        foreach (var teacher in seed.Teachers)
        {
            repository.Add(teacher);
        }

        output.WriteLine($"Added {seed.Teachers.Count} teachers from {relativePath}.");
        return 0;
    }
}
