using System.Text.Json;
using Microsoft.Extensions.Options;
using RoutinePlanner.Web.Data;
using RoutinePlanner.Web.Models;
using RoutinePlanner.Web.Services.Scheduling;
using RoutinePlanner.Web.Tests.TestHelpers;
using Xunit;

namespace RoutinePlanner.Web.Tests;

/// <summary>
/// Nothing stops two rows of the teachers table carrying the same name — POST /teacher/add has
/// never required the name to be unique, and two people at one institution sharing a name is
/// ordinary. The grid used to record a teacher by name, so the solver could not tell two such rows
/// apart: it resolved a lock to whichever row came first, counted one person's periods against the
/// other, and considered a teacher busy because their namesake was teaching.
/// </summary>
public class TeacherIdentityTests : IDisposable
{
    private readonly TempAppDirectory _app = new();

    public void Dispose() => _app.Dispose();

    // One 08:00-09:00 period per day: 60 minutes before the break, 30 after, so only the first
    // half of the day fits a 60-minute period.
    private const string TwoDayRequest = """
    {
        "days": ["Monday","Tuesday"],
        "start_time": "08:00", "break_start": "09:00", "break_end": "09:30", "end_time": "10:00",
        "periods": { "1A": ["CSC538"] },
        "period_duration": 60, "periods_per_subject": 2
    }
    """;

    private RoutinePlannerService Planner(TeacherRepository repository) => new(
        repository,
        _app.Environment,
        _app.BuildConfiguration(),
        Options.Create(new RoutinePlannerOptions { ParallelAttempts = 1, GenerationTimeoutSeconds = 20 }));

    private static AddTeacherRequest Teacher(string name, string[] subjects, params (string Day, string Start, string End)[] windows)
    {
        var availability = new Dictionary<string, List<AvailabilityWindow>>();
        foreach (var (day, start, end) in windows)
        {
            if (!availability.TryGetValue(day, out var list))
            {
                list = new List<AvailabilityWindow>();
                availability[day] = list;
            }
            list.Add(new AvailabilityWindow { Start = start, End = end });
        }

        return new AddTeacherRequest { Name = name, SubjectsArray = subjects.ToList(), AvailabilityJson = availability };
    }

    /// <summary>
    /// The headline case. Row 1 and row 2 are both called "R. Sharma" and are different people:
    /// row 1 teaches CSC539 and is free all week, row 2 teaches CSC538 and is free on Monday only.
    /// CSC538 needs two periods, so the second must fall on Tuesday.
    ///
    /// Only row 2 can take CSC538, so the lock goes to row 2 — who is not available on Tuesday, and
    /// there is therefore no valid timetable. Resolving that lock by name returned row 1 instead:
    /// row 1 *is* free on Tuesday, so the search accepted the slot and produced a timetable in which
    /// "R. Sharma" teaches CSC538 on Tuesday — a subject row 1 does not teach, at a time row 2 is
    /// not available.
    /// </summary>
    [Fact]
    public async Task A_lock_is_not_resolved_to_a_different_person_who_happens_to_share_the_name()
    {
        var config = _app.BuildConfiguration();
        var repository = new TeacherRepository(config, _app.Environment);

        repository.Add(Teacher("R. Sharma", new[] { "CSC539" },
            ("Monday", "08:00", "18:00"), ("Tuesday", "08:00", "18:00")));

        repository.Add(Teacher("R. Sharma", new[] { "CSC538" },
            ("Monday", "08:00", "18:00")));

        var request = JsonSerializer.Deserialize<GenerateRequest>(TwoDayRequest)!;
        var routine = await Planner(repository).GenerateAsync(request, CancellationToken.None);

        Assert.Null(routine);
    }

    /// <summary>
    /// The same shape with the second period genuinely coverable: row 2 is free on Tuesday too, so
    /// there is a timetable, and it is the one row 2 can actually teach.
    /// </summary>
    [Fact]
    public async Task The_same_arrangement_still_succeeds_when_the_locked_teacher_really_is_free()
    {
        var config = _app.BuildConfiguration();
        var repository = new TeacherRepository(config, _app.Environment);

        repository.Add(Teacher("R. Sharma", new[] { "CSC539" },
            ("Monday", "08:00", "18:00"), ("Tuesday", "08:00", "18:00")));

        repository.Add(Teacher("R. Sharma", new[] { "CSC538" },
            ("Monday", "08:00", "18:00"), ("Tuesday", "08:00", "18:00")));

        var request = JsonSerializer.Deserialize<GenerateRequest>(TwoDayRequest)!;
        var routine = await Planner(repository).GenerateAsync(request, CancellationToken.None);

        Assert.NotNull(routine);
        Assert.Equal("R. Sharma", routine!["Monday"][0].Classes["1A"]!.Teacher);
        Assert.Equal("R. Sharma", routine["Tuesday"][0].Classes["1A"]!.Teacher);
    }

    /// <summary>
    /// Two same-named people can teach two different sections in the same period. While the slot was
    /// keyed by name, assigning one made the other look busy, so a timetable that needs both at once
    /// was reported impossible.
    /// </summary>
    [Fact]
    public async Task Two_teachers_who_share_a_name_can_both_teach_in_the_same_period()
    {
        var config = _app.BuildConfiguration();
        var repository = new TeacherRepository(config, _app.Environment);

        repository.Add(Teacher("R. Sharma", new[] { "CSC538" }, ("Monday", "08:00", "18:00")));
        repository.Add(Teacher("R. Sharma", new[] { "CSC538" }, ("Monday", "08:00", "18:00")));

        // Two sections, one subject each, one period each, one slot in the day: the only possible
        // timetable puts both people in the 08:00 period.
        var request = JsonSerializer.Deserialize<GenerateRequest>("""
        {
            "days": ["Monday"],
            "start_time": "08:00", "break_start": "09:00", "break_end": "09:30", "end_time": "10:00",
            "periods": { "1A": ["CSC538"], "1B": ["CSC538"] },
            "period_duration": 60, "periods_per_subject": 1
        }
        """)!;

        var routine = await Planner(repository).GenerateAsync(request, CancellationToken.None);

        Assert.NotNull(routine);
        Assert.NotNull(routine!["Monday"][0].Classes["1A"]);
        Assert.NotNull(routine["Monday"][0].Classes["1B"]);
    }

    /// <summary>
    /// Workload is the tie-break for choosing among equally available teachers. Counting it by name
    /// charged one person's periods to their namesake, so the least-loaded teacher could be passed
    /// over for someone already teaching more.
    /// </summary>
    [Fact]
    public void Workload_is_counted_per_row_not_per_name()
    {
        var busy = new Teacher { Id = 1, Name = "R. Sharma" };
        var idle = new Teacher { Id = 2, Name = "R. Sharma" };

        var grid = new RoutineGrid
        {
            ["Monday"] = new()
            {
                new Dictionary<string, GridAssignment?>
                {
                    ["1A"] = new GridAssignment { Subject = "CSC538", Teacher = busy },
                },
            },
        };

        Assert.Equal(1, TeacherManager.GetTeacherCurrentWorkload(busy, grid));
        Assert.Equal(0, TeacherManager.GetTeacherCurrentWorkload(idle, grid));
    }

    /// <summary>A teacher is only busy in a period if it is really them teaching in it.</summary>
    [Fact]
    public void Availability_in_a_slot_is_decided_per_row_not_per_name()
    {
        var teaching = new Teacher
        {
            Id = 1, Name = "R. Sharma", Subjects = { "CSC538" },
            Availability = { ["Monday"] = new() { new AvailabilityWindow { Start = "08:00", End = "18:00" } } },
        };
        var namesake = new Teacher
        {
            Id = 2, Name = "R. Sharma", Subjects = { "CSC538" },
            Availability = { ["Monday"] = new() { new AvailabilityWindow { Start = "08:00", End = "18:00" } } },
        };

        var slot = new TimeSlot { Start = "08:00", End = "09:00" };
        var grid = new RoutineGrid
        {
            ["Monday"] = new()
            {
                new Dictionary<string, GridAssignment?>
                {
                    ["1A"] = new GridAssignment { Subject = "CSC538", Teacher = teaching },
                    ["1B"] = null,
                },
            },
        };

        Assert.False(TeacherManager.IsTeacherAvailableAtSlot(teaching, "Monday", slot, grid, 0));
        Assert.True(TeacherManager.IsTeacherAvailableAtSlot(namesake, "Monday", slot, grid, 0));
    }
}
