using RoutinePlanner.Web.Data;
using RoutinePlanner.Web.Models;
using RoutinePlanner.Web.Tests.TestHelpers;
using Xunit;

namespace RoutinePlanner.Web.Tests;

/// <summary>
/// Guards the exact mechanism behind "not generating from the same sqlite db file": both
/// repositories build their connection string as Path.Combine(ContentRootPath, configuredRelativePath).
/// If that logic ever diverges between the two repositories — or stops honoring the config value —
/// the app would silently start reading/writing two different files without any error or exception.
/// </summary>
public class PathResolutionTests : IDisposable
{
    private readonly TempAppDirectory _app = new();

    [Fact]
    public void RoutineRepository_Writes_To_The_Configured_Path_Under_ContentRoot()
    {
        var repo = new RoutineRepository(_app.BuildConfiguration(), _app.Environment);
        repo.Save(new FormattedRoutine());

        var expectedPath = Path.Combine(_app.RootPath, "App_Data", "routine.sqlite3");
        Assert.True(File.Exists(expectedPath), $"Expected sqlite file at {expectedPath}");
    }

    [Fact]
    public void TeacherRepository_Writes_To_The_Configured_Path_Under_ContentRoot()
    {
        var repo = new TeacherRepository(_app.BuildConfiguration(), _app.Environment);
        repo.Add(new AddTeacherRequest { Name = "Path Check" });

        var expectedPath = Path.Combine(_app.RootPath, "App_Data", "campus.sqlite3");
        Assert.True(File.Exists(expectedPath), $"Expected sqlite file at {expectedPath}");
    }

    [Fact]
    public void Repositories_Honor_A_Custom_Configured_Relative_Path_Not_Just_The_Default()
    {
        var overrides = new Dictionary<string, string?>
        {
            ["RoutinePlanner:RoutineDbPath"] = "custom-folder/mine.sqlite3",
        };
        var repo = new RoutineRepository(_app.BuildConfiguration(overrides), _app.Environment);
        repo.Save(new FormattedRoutine());

        var expectedPath = Path.Combine(_app.RootPath, "custom-folder", "mine.sqlite3");
        Assert.True(File.Exists(expectedPath));

        var defaultPath = Path.Combine(_app.RootPath, "App_Data", "routine.sqlite3");
        Assert.False(File.Exists(defaultPath), "Should not have also written to the default path.");
    }

    [Fact]
    public void TeacherRepository_And_RoutineRepository_Agree_On_The_Same_ContentRoot()
    {
        // Both repositories are constructed from the exact same IWebHostEnvironment/IConfiguration
        // that DI would hand them in the real app — so a teacher added through one and a routine
        // saved through the other must land under the identical app folder, not diverge.
        var config = _app.BuildConfiguration();
        new TeacherRepository(config, _app.Environment).Add(new AddTeacherRequest { Name = "X" });
        new RoutineRepository(config, _app.Environment).Save(new FormattedRoutine());

        Assert.True(File.Exists(Path.Combine(_app.RootPath, "App_Data", "campus.sqlite3")));
        Assert.True(File.Exists(Path.Combine(_app.RootPath, "App_Data", "routine.sqlite3")));
    }

    public void Dispose() => _app.Dispose();
}
