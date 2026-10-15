using System.Text.Json;
using Microsoft.Extensions.Options;
using RoutinePlanner.Web.Data;
using RoutinePlanner.Web.Models;
using RoutinePlanner.Web.Services.Scheduling;
using RoutinePlanner.Web.Tests.TestHelpers;
using Xunit;

namespace RoutinePlanner.Web.Tests;

/// <summary>
/// The actual bug report this migration produced: "routine are not generating from the same
/// sqlite db file". This exercises the full real pipeline exactly as the app does it — add a
/// teacher through TeacherRepository, generate a routine through RoutinePlannerService (which
/// reads teachers back out via a *second*, freshly-constructed TeacherRepository, just like a
/// real HTTP request would), save it through RoutineRepository, then reload it through a
/// *third*, freshly-constructed RoutineRepository — all pointed at the same content root. If
/// generation, or the save/reload round trip, ever silently touched a different physical sqlite
/// file, these tests fail.
/// </summary>
public class RoutinePlannerServiceIntegrationTests : IDisposable
{
    private readonly TempAppDirectory _app = new();

    // One day, one 60-minute slot, one section, one subject, one teacher who covers exactly
    // that slot — small enough to be fully deterministic regardless of parallel-attempt count.
    private const string RequestJson = """
    {
        "days": ["Monday"],
        "start_time": "08:00",
        "break_start": "12:00",
        "break_end": "12:00",
        "end_time": "09:00",
        "periods": { "1A": ["MTH101"] },
        "period_duration": 60,
        "periods_per_subject": 1
    }
    """;

    private static AddTeacherRequest GraceHopper() => new()
    {
        Name = "Grace Hopper",
        SubjectsArray = new List<string> { "MTH101" },
        AvailabilityJson = new Dictionary<string, List<AvailabilityWindow>>
        {
            ["Monday"] = new() { new AvailabilityWindow { Start = "08:00", End = "09:00" } },
        },
    };

    [Fact]
    public async Task Generate_Reads_Teachers_From_The_Same_Db_File_They_Were_Just_Added_To()
    {
        var config = _app.BuildConfiguration();

        new TeacherRepository(config, _app.Environment).Add(GraceHopper());

        // A *new* TeacherRepository instance — same as RoutinePlannerService gets from DI on a
        // fresh request — proving generation reads whatever was just written, not a stale copy.
        var planner = new RoutinePlannerService(
            new TeacherRepository(config, _app.Environment),
            _app.Environment,
            config,
            Options.Create(new RoutinePlannerOptions { ParallelAttempts = 1 }));

        var request = JsonSerializer.Deserialize<GenerateRequest>(RequestJson)!;
        var routine = await planner.GenerateAsync(request, CancellationToken.None);

        Assert.NotNull(routine);
        var assignment = routine!["Monday"][0].Classes["1A"];
        Assert.NotNull(assignment);
        Assert.Equal("Grace Hopper", assignment!.Teacher);
        Assert.Equal("MTH101", assignment.Subject);
    }

    [Fact]
    public async Task Generate_Does_Not_Silently_Fall_Back_To_A_Teacher_Seeded_In_A_Different_Db_File()
    {
        // The regression, made explicit: seed the teacher into a *different* content root
        // entirely (simulating the old bug — one component reading bin's copy, another reading
        // out's, or a rebuild resetting the live db back to seed data).
        using var otherApp = new TempAppDirectory();
        new TeacherRepository(otherApp.BuildConfiguration(), otherApp.Environment).Add(GraceHopper());

        // Planner is wired to THIS app's (empty) db, not otherApp's.
        var config = _app.BuildConfiguration();
        var planner = new RoutinePlannerService(
            new TeacherRepository(config, _app.Environment),
            _app.Environment,
            config,
            Options.Create(new RoutinePlannerOptions { ParallelAttempts = 1 }));

        var request = JsonSerializer.Deserialize<GenerateRequest>(RequestJson)!;
        var routine = await planner.GenerateAsync(request, CancellationToken.None);

        // No teacher available for MTH101 in *this* db -> generation correctly fails (returns
        // null) instead of silently reusing the other app's teacher data.
        Assert.Null(routine);
    }

    [Fact]
    public async Task Generated_Routine_Round_Trips_Through_Save_And_Reload_On_The_Same_Db_File()
    {
        var config = _app.BuildConfiguration();
        new TeacherRepository(config, _app.Environment).Add(GraceHopper());

        var planner = new RoutinePlannerService(
            new TeacherRepository(config, _app.Environment),
            _app.Environment,
            config,
            Options.Create(new RoutinePlannerOptions { ParallelAttempts = 1 }));

        var request = JsonSerializer.Deserialize<GenerateRequest>(RequestJson)!;
        var generated = await planner.GenerateAsync(request, CancellationToken.None);
        Assert.NotNull(generated);

        var id = new RoutineRepository(config, _app.Environment).Save(generated!);
        var reloaded = new RoutineRepository(config, _app.Environment).GetById(id);

        Assert.Equal(JsonSerializer.Serialize(generated), JsonSerializer.Serialize(reloaded));
    }

    public void Dispose() => _app.Dispose();
}
