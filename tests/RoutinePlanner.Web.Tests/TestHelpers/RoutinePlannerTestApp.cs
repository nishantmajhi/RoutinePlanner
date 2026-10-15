using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RoutinePlanner.Web.Data;
using RoutinePlanner.Web.Models;

namespace RoutinePlanner.Web.Tests.TestHelpers;

/// <summary>
/// Boots the real application in-process — the pipeline from Program.cs, with its own DI
/// registrations, rate limiter and controllers — but pointed at a throwaway content root holding
/// its own empty databases and a copy of the frontend. This is what makes it possible to assert on
/// what a *client* sees: that a bad request comes back 400 with a message the routine page will
/// render, rather than a 500 that reads "internal server error" for a mistake the user could fix.
///
/// The content root is a temp directory, so no test can reach the repository's own App_Data.
/// </summary>
public sealed class RoutinePlannerTestApp : WebApplicationFactory<Program>
{
    private readonly string _contentRoot;
    private readonly Dictionary<string, string?> _settings;

    public string ContentRoot => _contentRoot;

    public RoutinePlannerTestApp(IDictionary<string, string?>? settings = null)
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), "RoutinePlannerHostTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_contentRoot, "App_Data"));

        CopyFrontendInto(_contentRoot);

        _settings = new Dictionary<string, string?>
        {
            ["RoutinePlanner:CampusDbPath"] = "App_Data/campus.sqlite3",
            ["RoutinePlanner:RoutineDbPath"] = "App_Data/routine.sqlite3",
            ["RoutinePlanner:CoursesJsonPath"] = "App_Data/courses.json",
            ["RoutinePlanner:ViewRootPath"] = "wwwroot",
            ["RoutinePlanner:LogsPath"] = "logs",
            // One attempt keeps a failing search to a single thread, so a test that deliberately
            // asks for something impossible does not saturate the machine running the suite.
            ["RoutinePlanner:ParallelAttempts"] = "1",
            ["RoutinePlanner:GenerationTimeoutSeconds"] = "10",
        };

        if (settings is not null)
        {
            foreach (var (key, value) in settings) _settings[key] = value;
        }
    }

    /// <summary>Copies the real wwwroot and courses.json out of the source tree, so the routine
    /// page is rendered from the template the app actually ships rather than a stand-in.</summary>
    private static void CopyFrontendInto(string contentRoot)
    {
        var webProjectDir = SourceTree.WebProjectDirectory;

        foreach (var source in Directory.EnumerateFiles(Path.Combine(webProjectDir, "wwwroot"), "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(webProjectDir, source);
            var destination = Path.Combine(contentRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }

        File.Copy(
            Path.Combine(webProjectDir, "App_Data", "courses.json"),
            Path.Combine(contentRoot, "App_Data", "courses.json"),
            overwrite: true);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(_contentRoot);
        builder.UseSetting("RoutinePlanner:__marker", "test");
        foreach (var (key, value) in _settings) builder.UseSetting(key, value);
    }

    /// <summary>Inserts a teacher straight into the booted app's own campus database.</summary>
    public long AddTeacher(AddTeacherRequest teacher)
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<TeacherRepository>().Add(teacher);
    }

    /// <summary>A teacher who covers the whole of every listed day, for every listed subject.</summary>
    public static AddTeacherRequest AlwaysAvailable(string name, IEnumerable<string> subjects, IEnumerable<string> days)
    {
        var availability = new Dictionary<string, List<AvailabilityWindow>>();
        foreach (var day in days)
        {
            availability[day] = new List<AvailabilityWindow> { new() { Start = "00:00", End = "23:59" } };
        }

        return new AddTeacherRequest
        {
            Name = name,
            SubjectsArray = subjects.ToList(),
            AvailabilityJson = availability,
        };
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing) return;
        try
        {
            if (Directory.Exists(_contentRoot)) Directory.Delete(_contentRoot, recursive: true);
        }
        catch
        {
            // Best effort — a leftover temp folder is not worth failing a run over.
        }
    }
}
