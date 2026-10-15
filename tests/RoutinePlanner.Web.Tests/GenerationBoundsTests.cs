using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RoutinePlanner.Web.Data;
using RoutinePlanner.Web.Models;
using RoutinePlanner.Web.Services.Scheduling;
using RoutinePlanner.Web.Tests.TestHelpers;
using Xunit;

namespace RoutinePlanner.Web.Tests;

/// <summary>
/// What bounds the search, and what makes a run reproducible.
///
/// The validator can now reject everything infeasible by arithmetic, but infeasibility that follows
/// from who teaches what and when cannot be decided up front — and /generate needs no
/// authentication, so an unbounded search is one request holding every core for as long as the
/// client keeps the connection open. Hence a wall-clock ceiling.
/// </summary>
public class GenerationBoundsTests : IDisposable
{
    private readonly TempAppDirectory _app = new();

    public void Dispose() => _app.Dispose();

    private RoutinePlannerService Planner(int attempts = 1, int timeoutSeconds = 30, int? seed = null) => new(
        new TeacherRepository(_app.BuildConfiguration(), _app.Environment),
        _app.Environment,
        _app.BuildConfiguration(),
        Options.Create(new RoutinePlannerOptions
        {
            ParallelAttempts = attempts,
            GenerationTimeoutSeconds = timeoutSeconds,
            Seed = seed,
        }));

    private void SeedTeachers(int count, string[] subjects, string[] days, string start = "08:00", string end = "18:00")
    {
        var repository = new TeacherRepository(_app.BuildConfiguration(), _app.Environment);
        var availability = new Dictionary<string, List<AvailabilityWindow>>();
        foreach (var day in days) availability[day] = new() { new AvailabilityWindow { Start = start, End = end } };

        for (var i = 0; i < count; i++)
        {
            repository.Add(new AddTeacherRequest
            {
                Name = $"Teacher {i}",
                SubjectsArray = subjects.ToList(),
                AvailabilityJson = availability,
            });
        }
    }

    private static GenerateRequest Request(string json) => JsonSerializer.Deserialize<GenerateRequest>(json)!;

    /// <summary>
    /// A request that passes every arithmetic check and still has no solution, because the teachers
    /// cannot cover it: five subjects needing a teacher each in the same period, and only two
    /// teachers. Before the ceiling, this ran until the process was killed or the client gave up.
    /// </summary>
    [Fact]
    public async Task An_unsatisfiable_search_gives_up_at_the_configured_ceiling()
    {
        var days = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday" };
        var subjects = new[] { "S1", "S2", "S3", "S4", "S5", "S6" };
        SeedTeachers(3, subjects, days);

        // Six subjects, five periods each, across five days: arithmetically fine (6x5 = 30 periods
        // against 6/day x 5 days = 30), but every one of the day's six slots must then be filled for
        // every section, and three teachers cannot cover six subjects under the one-subject-per-
        // teacher-per-section rule.
        var request = Request("""
        { "days": ["Sunday","Monday","Tuesday","Wednesday","Thursday"],
          "start_time": "08:00", "break_start": "12:00", "break_end": "12:40", "end_time": "18:00",
          "periods": { "1A": ["S1","S2","S3","S4","S5","S6"] },
          "period_duration": 40, "periods_per_subject": 5 }
        """);

        var stopwatch = Stopwatch.StartNew();
        var routine = await Planner(attempts: 1, timeoutSeconds: 3).GenerateAsync(request, CancellationToken.None);
        stopwatch.Stop();

        Assert.Null(routine);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30),
            $"the search ran for {stopwatch.Elapsed.TotalSeconds:F1}s despite a 3-second ceiling");
    }

    /// <summary>The ceiling must not cut short a search that was going to succeed.</summary>
    [Fact]
    public async Task A_satisfiable_request_is_unaffected_by_the_ceiling()
    {
        var days = new[] { "Monday", "Tuesday" };
        SeedTeachers(4, new[] { "S1", "S2" }, days);

        var routine = await Planner(attempts: 1, timeoutSeconds: 30).GenerateAsync(Request("""
        { "days": ["Monday","Tuesday"],
          "start_time": "08:00", "break_start": "10:00", "break_end": "10:30", "end_time": "12:30",
          "periods": { "1A": ["S1","S2"] },
          "period_duration": 60, "periods_per_subject": 2 }
        """), CancellationToken.None);

        Assert.NotNull(routine);
    }

    /// <summary>A ceiling of zero means no ceiling, for anyone who wants the old behaviour back.</summary>
    [Fact]
    public async Task A_ceiling_of_zero_disables_it()
    {
        SeedTeachers(2, new[] { "S1" }, new[] { "Monday" });

        var routine = await Planner(attempts: 1, timeoutSeconds: 0).GenerateAsync(Request("""
        { "days": ["Monday"],
          "start_time": "08:00", "break_start": "10:00", "break_end": "10:30", "end_time": "12:30",
          "periods": { "1A": ["S1"] },
          "period_duration": 60, "periods_per_subject": 1 }
        """), CancellationToken.None);

        Assert.NotNull(routine);
    }

    /// <summary>
    /// The review asked whether generation is deterministic. It is not by default and should not be
    /// — the randomness is what lets several attempts explore different parts of the space at once —
    /// but the seed it all derives from is now configurable and is written into the generation log,
    /// so a run can be reproduced.
    /// </summary>
    [Fact]
    public async Task The_same_seed_produces_the_same_timetable()
    {
        var days = new[] { "Sunday", "Monday", "Tuesday", "Wednesday" };
        var subjects = new[] { "S1", "S2", "S3" };
        SeedTeachers(6, subjects, days);

        var requestJson = """
        { "days": ["Sunday","Monday","Tuesday","Wednesday"],
          "start_time": "08:00", "break_start": "11:00", "break_end": "11:30", "end_time": "14:30",
          "periods": { "1A": ["S1","S2","S3"], "1B": ["S1","S2","S3"] },
          "period_duration": 60, "periods_per_subject": 3 }
        """;

        // One attempt, so the winner is not whichever thread happened to finish first.
        var first = await Planner(attempts: 1, seed: 12345).GenerateAsync(Request(requestJson), CancellationToken.None);
        var second = await Planner(attempts: 1, seed: 12345).GenerateAsync(Request(requestJson), CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
    }

    [Fact]
    public async Task A_different_seed_generally_produces_a_different_timetable()
    {
        var days = new[] { "Sunday", "Monday", "Tuesday", "Wednesday" };
        var subjects = new[] { "S1", "S2", "S3" };
        SeedTeachers(6, subjects, days);

        var requestJson = """
        { "days": ["Sunday","Monday","Tuesday","Wednesday"],
          "start_time": "08:00", "break_start": "11:00", "break_end": "11:30", "end_time": "14:30",
          "periods": { "1A": ["S1","S2","S3"], "1B": ["S1","S2","S3"] },
          "period_duration": 60, "periods_per_subject": 3 }
        """;

        var results = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            var routine = await Planner(attempts: 1, seed: seed).GenerateAsync(Request(requestJson), CancellationToken.None);
            Assert.NotNull(routine);
            results.Add(JsonSerializer.Serialize(routine));
        }

        Assert.True(results.Count > 1, "every seed produced an identical timetable, so the seed is not reaching the search");
    }

    /// <summary>The seed that produced a run is recorded, which is what makes reproducing one
    /// possible at all — otherwise there is nothing to put in the setting.</summary>
    [Fact]
    public async Task The_seed_is_written_to_the_generation_log()
    {
        SeedTeachers(2, new[] { "S1" }, new[] { "Monday" });

        await Planner(attempts: 1, seed: 99991).GenerateAsync(Request("""
        { "days": ["Monday"],
          "start_time": "08:00", "break_start": "10:00", "break_end": "10:30", "end_time": "12:30",
          "periods": { "1A": ["S1"] },
          "period_duration": 60, "periods_per_subject": 1 }
        """), CancellationToken.None);

        var logsDir = Path.Combine(_app.RootPath, "logs");
        Assert.True(Directory.Exists(logsDir), "no logs directory was written");

        var log = Directory.EnumerateFiles(logsDir, "routine_*.log").Select(File.ReadAllText).Single();
        Assert.Contains("Random seed base: 99991", log);
    }

    /// <summary>A client that disconnects mid-search is not the same thing as there being no
    /// timetable, and must not be answered as though a search had completed.</summary>
    [Fact]
    public async Task A_cancelled_request_is_reported_as_cancelled_rather_than_as_no_schedule()
    {
        SeedTeachers(2, new[] { "S1" }, new[] { "Monday" });

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Planner(attempts: 1).GenerateAsync(Request("""
            { "days": ["Monday"],
              "start_time": "08:00", "break_start": "10:00", "break_end": "10:30", "end_time": "12:30",
              "periods": { "1A": ["S1"] },
              "period_duration": 60, "periods_per_subject": 1 }
            """), cts.Token));
    }

    /// <summary>Several attempts racing must still produce a valid timetable, not a mixture of two.
    /// Each attempt owns its own grid and bookkeeping, and this is what says so.</summary>
    [Fact]
    public async Task Racing_attempts_in_parallel_produces_one_coherent_timetable()
    {
        var days = new[] { "Sunday", "Monday", "Tuesday", "Wednesday" };
        var subjects = new[] { "S1", "S2", "S3" };
        SeedTeachers(8, subjects, days);

        var periods = new Dictionary<string, List<string>>
        {
            ["1A"] = subjects.ToList(),
            ["1B"] = subjects.ToList(),
        };

        var routine = await Planner(attempts: 8).GenerateAsync(Request("""
        { "days": ["Sunday","Monday","Tuesday","Wednesday"],
          "start_time": "08:00", "break_start": "11:00", "break_end": "11:30", "end_time": "14:30",
          "periods": { "1A": ["S1","S2","S3"], "1B": ["S1","S2","S3"] },
          "period_duration": 60, "periods_per_subject": 3 }
        """), CancellationToken.None);

        Assert.NotNull(routine);

        var teachers = new TeacherRepository(_app.BuildConfiguration(), _app.Environment)
            .LoadTeachersForEngine().Values.ToList();

        GeneratedRoutineAssertions.AssertValid(routine!, teachers, periods, days.ToList(), periodsPerSubject: 3);
    }
}
