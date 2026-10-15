using Microsoft.Extensions.Configuration;

namespace RoutinePlanner.Web.Tests.TestHelpers;

/// <summary>
/// Creates a throwaway "app content root" on disk — the same layout the real app expects
/// (an App_Data/ subfolder holding the sqlite files) — and cleans it up on Dispose. Every test
/// gets its own isolated copy, so tests can never interfere with each other or with the real
/// seed data shipped in the repo.
/// </summary>
public sealed class TempAppDirectory : IDisposable
{
    public string RootPath { get; }
    public string DatabaseDir { get; }
    public TestWebHostEnvironment Environment { get; }

    public TempAppDirectory()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "RoutinePlannerTests_" + Guid.NewGuid().ToString("N"));
        DatabaseDir = Path.Combine(RootPath, "App_Data");
        Directory.CreateDirectory(DatabaseDir);

        Environment = new TestWebHostEnvironment { ContentRootPath = RootPath };
    }

    /// <summary>Builds an IConfiguration matching appsettings.json's RoutinePlanner section defaults,
    /// with any values in <paramref name="overrides"/> applied on top.</summary>
    public IConfiguration BuildConfiguration(IDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["RoutinePlanner:CampusDbPath"] = "App_Data/campus.sqlite3",
            ["RoutinePlanner:RoutineDbPath"] = "App_Data/routine.sqlite3",
            ["RoutinePlanner:CoursesJsonPath"] = "App_Data/courses.json",
            ["RoutinePlanner:LogsPath"] = "logs",
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides) values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
        }
        catch
        {
            // Best effort — a leftover temp folder in %TEMP% isn't worth failing the test run over.
        }
    }
}
