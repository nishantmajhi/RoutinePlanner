using System.Text.Json;
using Microsoft.Extensions.Options;
using RoutinePlanner.Web.Data;
using RoutinePlanner.Web.Models;
using RoutinePlanner.Web.Services;
using RoutinePlanner.Web.Services.Scheduling;
using RoutinePlanner.Web.Tests.TestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace RoutinePlanner.Web.Tests;

/// <summary>
/// The solver's own completeness check only counts periods. Everything else it relies on — that a
/// teacher teaches the subject they are given, is available when they are scheduled, is in one place
/// at a time, keeps one subject per section, and that no subject appears twice in a section's day —
/// it assumes holds by construction. So these generate many different timetables and check all of it
/// independently, which is how a defect in the construction shows up rather than being asserted as
/// correct.
///
/// Teachers are given distinct names here, because a FormattedRoutine records a teacher's name and
/// nothing else. TeacherIdentityTests covers what happens when two of them share one.
/// </summary>
public class GeneratedRoutineInvariantTests : IDisposable
{
    private readonly TempAppDirectory _app = new();
    private readonly ITestOutputHelper _out;

    public GeneratedRoutineInvariantTests(ITestOutputHelper output) => _out = output;

    public void Dispose() => _app.Dispose();

    private static readonly string[] AllDays = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" };

    private RoutinePlannerService Planner(int? seed) => new(
        new TeacherRepository(_app.BuildConfiguration(), _app.Environment),
        _app.Environment,
        _app.BuildConfiguration(),
        Options.Create(new RoutinePlannerOptions { ParallelAttempts = 1, GenerationTimeoutSeconds = 20, Seed = seed }));

    /// <summary>
    /// Fifty randomly-shaped schedules. Each one is checked for feasibility first, so a run that
    /// comes back empty is a real "no timetable exists" rather than a bad case; the invariants are
    /// then asserted against whatever was produced.
    /// </summary>
    [Fact]
    public async Task Fifty_randomly_shaped_timetables_all_satisfy_every_constraint()
    {
        var rng = new Random(20260926);
        var generated = 0;
        var refused = 0;
        var unsatisfiable = 0;

        for (var iteration = 0; iteration < 50; iteration++)
        {
            using var app = new TempAppDirectory();
            var config = app.BuildConfiguration();
            var repository = new TeacherRepository(config, app.Environment);

            var dayCount = rng.Next(1, 6);
            var days = AllDays.Take(dayCount).ToList();

            var subjectCount = rng.Next(1, 5);
            var subjects = Enumerable.Range(1, subjectCount).Select(i => $"SUB{i}").ToList();

            var sectionCount = rng.Next(1, 4);
            var periods = new Dictionary<string, List<string>>();
            for (var s = 0; s < sectionCount; s++) periods[$"S{s}"] = subjects.ToList();

            var periodsPerSubject = rng.Next(1, dayCount + 1);
            var periodDuration = new[] { 30, 45, 60 }[rng.Next(3)];

            // Generously many teachers, each covering every subject and the whole day, so that when
            // no timetable comes back it is the grid's shape that made it impossible rather than a
            // shortage of people.
            var teacherCount = sectionCount * subjectCount + rng.Next(1, 4);
            var availability = new Dictionary<string, List<AvailabilityWindow>>();
            foreach (var day in days) availability[day] = new() { new AvailabilityWindow { Start = "07:00", End = "19:00" } };

            for (var t = 0; t < teacherCount; t++)
            {
                repository.Add(new AddTeacherRequest
                {
                    Name = $"Teacher {t}",
                    SubjectsArray = subjects.ToList(),
                    AvailabilityJson = availability,
                });
            }

            var requestJson = JsonSerializer.Serialize(new
            {
                days,
                start_time = "08:00",
                break_start = "12:00",
                break_end = "12:30",
                end_time = "17:00",
                periods,
                period_duration = periodDuration,
                periods_per_subject = periodsPerSubject,
            });

            var request = JsonSerializer.Deserialize<GenerateRequest>(requestJson)!;

            // Only ask for timetables the validator considers possible.
            if (RoutineRequestValidator.ValidateRoutineRequest(request) is not null ||
                RoutineRequestValidator.ValidateTimeSequence(request.StartTime!, request.BreakStart!, request.BreakEnd!, request.EndTime!) is not null ||
                RoutineRequestValidator.ValidateScheduleFeasibility(request) is not null)
            {
                refused++;
                continue;
            }

            var planner = new RoutinePlannerService(
                new TeacherRepository(config, app.Environment), app.Environment, config,
                Options.Create(new RoutinePlannerOptions { ParallelAttempts = 1, GenerationTimeoutSeconds = 20, Seed = rng.Next() }));

            var routine = await planner.GenerateAsync(request, CancellationToken.None);

            if (routine is null)
            {
                unsatisfiable++;
                continue;
            }

            var teachers = new TeacherRepository(config, app.Environment).LoadTeachersForEngine().Values.ToList();

            try
            {
                GeneratedRoutineAssertions.AssertValid(routine, teachers, periods, days, periodsPerSubject);
            }
            catch (Exception)
            {
                _out.WriteLine($"iteration {iteration} failed: {dayCount} days, {sectionCount} sections, " +
                               $"{subjectCount} subjects, {periodsPerSubject} periods each, {periodDuration}-minute periods");
                _out.WriteLine(JsonSerializer.Serialize(routine, new JsonSerializerOptions { WriteIndented = true }));
                throw;
            }

            generated++;
        }

        _out.WriteLine($"{generated} timetables checked, {unsatisfiable} had no solution, {refused} refused by the validator");
        Assert.True(generated >= 25, $"only {generated} of 50 shapes produced a timetable to check");
    }

    /// <summary>
    /// Teachers with genuinely partial availability — mornings only, afternoons only, one day off —
    /// which is the case the availability check exists for. A period must fall entirely inside one
    /// window, and a window that ends mid-period does not count.
    /// </summary>
    [Fact]
    public async Task A_timetable_respects_windows_that_only_cover_part_of_the_day()
    {
        var config = _app.BuildConfiguration();
        var repository = new TeacherRepository(config, _app.Environment);
        var days = new List<string> { "Sunday", "Monday", "Tuesday" };

        // Mornings only: 08:00-11:00 covers the two 08:00 and 09:00 periods (and the 10:00 one).
        repository.Add(Teacher("Morning Only", new[] { "S1", "S2", "S3" }, days, "08:00", "11:00"));
        // Afternoons only: 13:00-17:00.
        repository.Add(Teacher("Afternoon Only", new[] { "S1", "S2", "S3" }, days, "13:00", "17:00"));
        repository.Add(Teacher("Afternoon Too", new[] { "S1", "S2", "S3" }, days, "13:00", "17:00"));
        // Someone with a window that stops halfway through a period, so it covers nothing extra.
        repository.Add(Teacher("Awkward Hours", new[] { "S1", "S2", "S3" }, days, "08:00", "08:30"));

        var periods = new Dictionary<string, List<string>> { ["1A"] = new() { "S1", "S2", "S3" } };

        var routine = await Planner(seed: 555).GenerateAsync(JsonSerializer.Deserialize<GenerateRequest>("""
        { "days": ["Sunday","Monday","Tuesday"],
          "start_time": "08:00", "break_start": "12:00", "break_end": "12:30", "end_time": "17:00",
          "periods": { "1A": ["S1","S2","S3"] },
          "period_duration": 60, "periods_per_subject": 3 }
        """)!, CancellationToken.None);

        Assert.NotNull(routine);

        var teachers = new TeacherRepository(config, _app.Environment).LoadTeachersForEngine().Values.ToList();
        GeneratedRoutineAssertions.AssertValid(routine!, teachers, periods, days, periodsPerSubject: 3);

        // "Awkward Hours" can never take a 60-minute period inside an 08:00-08:30 window.
        var everyTeacherUsed = routine!.Values
            .SelectMany(slots => slots)
            .SelectMany(slot => slot.Classes.Values)
            .Where(a => a is not null)
            .Select(a => a!.Teacher)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Awkward Hours", everyTeacherUsed);
    }

    /// <summary>
    /// Sections run in parallel in the same period, taught by different people. With exactly as many
    /// teachers as sections and only one period in the day, every teacher must be used at once —
    /// which is only possible if nothing double-books them and nothing treats them as busy wrongly.
    /// </summary>
    [Fact]
    public async Task Sections_run_in_parallel_and_no_teacher_is_in_two_of_them_at_once()
    {
        var config = _app.BuildConfiguration();
        var repository = new TeacherRepository(config, _app.Environment);
        var days = new List<string> { "Monday" };

        for (var i = 0; i < 3; i++)
        {
            repository.Add(Teacher($"Teacher {i}", new[] { "S1" }, days, "08:00", "18:00"));
        }

        var periods = new Dictionary<string, List<string>>
        {
            ["1A"] = new() { "S1" },
            ["1B"] = new() { "S1" },
            ["1C"] = new() { "S1" },
        };

        var routine = await Planner(seed: 777).GenerateAsync(JsonSerializer.Deserialize<GenerateRequest>("""
        { "days": ["Monday"],
          "start_time": "08:00", "break_start": "09:00", "break_end": "09:30", "end_time": "10:00",
          "periods": { "1A": ["S1"], "1B": ["S1"], "1C": ["S1"] },
          "period_duration": 60, "periods_per_subject": 1 }
        """)!, CancellationToken.None);

        Assert.NotNull(routine);

        var slot = routine!["Monday"][0];
        var names = slot.Classes.Values.Where(a => a is not null).Select(a => a!.Teacher).ToList();

        Assert.Equal(3, names.Count);
        Assert.Equal(3, names.Distinct(StringComparer.Ordinal).Count());

        var teachers = new TeacherRepository(config, _app.Environment).LoadTeachersForEngine().Values.ToList();
        GeneratedRoutineAssertions.AssertValid(routine, teachers, periods, days, periodsPerSubject: 1);
    }

    /// <summary>
    /// One teacher per section per subject for the whole week — the point of the locking. With two
    /// subjects in a section and two teachers who both cover both, each must settle on one subject.
    /// </summary>
    [Fact]
    public async Task Each_subject_of_a_section_keeps_one_teacher_all_week()
    {
        var config = _app.BuildConfiguration();
        var repository = new TeacherRepository(config, _app.Environment);
        var days = new List<string> { "Sunday", "Monday", "Tuesday" };

        repository.Add(Teacher("Alice", new[] { "S1", "S2" }, days, "08:00", "18:00"));
        repository.Add(Teacher("Bob", new[] { "S1", "S2" }, days, "08:00", "18:00"));

        var periods = new Dictionary<string, List<string>> { ["1A"] = new() { "S1", "S2" } };

        var routine = await Planner(seed: 31337).GenerateAsync(JsonSerializer.Deserialize<GenerateRequest>("""
        { "days": ["Sunday","Monday","Tuesday"],
          "start_time": "08:00", "break_start": "10:00", "break_end": "10:30", "end_time": "12:30",
          "periods": { "1A": ["S1","S2"] },
          "period_duration": 60, "periods_per_subject": 3 }
        """)!, CancellationToken.None);

        Assert.NotNull(routine);

        var teachersOf = new Dictionary<string, HashSet<string>>();
        foreach (var (_, slots) in routine!)
        {
            foreach (var slot in slots)
            {
                var assignment = slot.Classes["1A"];
                if (assignment is null) continue;

                if (!teachersOf.TryGetValue(assignment.Subject, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    teachersOf[assignment.Subject] = set;
                }
                set.Add(assignment.Teacher);
            }
        }

        Assert.Equal(2, teachersOf.Count);
        Assert.All(teachersOf, entry => Assert.Single(entry.Value));

        // And the two subjects went to different people, since one teacher takes one subject per section.
        Assert.Equal(2, teachersOf.Values.SelectMany(v => v).Distinct(StringComparer.Ordinal).Count());

        var teachers = new TeacherRepository(config, _app.Environment).LoadTeachersForEngine().Values.ToList();
        GeneratedRoutineAssertions.AssertValid(routine, teachers, periods, days, periodsPerSubject: 3);
    }

    /// <summary>
    /// The checker above is only worth anything if it fails when it should. Each case here breaks one
    /// of the rules in a timetable that is otherwise correct, and each must be caught — otherwise the
    /// fifty passing shapes above would be evidence of nothing.
    /// </summary>
    [Theory]
    [InlineData("teacher does not teach the subject")]
    [InlineData("teacher is outside their availability")]
    [InlineData("teacher is in two sections at once")]
    [InlineData("subject appears twice in one day")]
    [InlineData("subject has too few periods")]
    [InlineData("subject changes teacher mid-week")]
    [InlineData("one teacher takes two subjects in a section")]
    public void The_invariant_checker_rejects_a_timetable_that_breaks_a_rule(string breakage)
    {
        var days = new List<string> { "Monday", "Tuesday" };
        var periods = new Dictionary<string, List<string>>
        {
            ["1A"] = new() { "S1" },
            ["1B"] = new() { "S1" },
        };

        var teachers = new List<Teacher>
        {
            NamedTeacher(1, "Alice", new[] { "S1" }, days),
            NamedTeacher(2, "Bob", new[] { "S1" }, days),
            NamedTeacher(3, "Carol", new[] { "S2" }, days),                      // teaches S2, not S1
            NamedTeacher(4, "Dave", new[] { "S1" }, days, "13:00", "17:00"),     // afternoons only
        };

        // A correct baseline: one 08:00 period a day, 1A with Alice and 1B with Bob, both days.
        FormattedRoutine Baseline() => new()
        {
            ["Monday"] = new() { Slot("08:00", "09:00", ("1A", "S1", "Alice"), ("1B", "S1", "Bob")) },
            ["Tuesday"] = new() { Slot("08:00", "09:00", ("1A", "S1", "Alice"), ("1B", "S1", "Bob")) },
        };

        // Sanity: the baseline itself passes.
        GeneratedRoutineAssertions.AssertValid(Baseline(), teachers, periods, days, periodsPerSubject: 2);

        var broken = Baseline();
        switch (breakage)
        {
            case "teacher does not teach the subject":
                broken["Monday"][0].Classes["1A"] = new SlotAssignment { Subject = "S1", Teacher = "Carol" };
                break;
            case "teacher is outside their availability":
                broken["Monday"][0].Classes["1A"] = new SlotAssignment { Subject = "S1", Teacher = "Dave" };
                break;
            case "teacher is in two sections at once":
                broken["Monday"][0].Classes["1B"] = new SlotAssignment { Subject = "S1", Teacher = "Alice" };
                break;
            case "subject appears twice in one day":
                broken["Monday"][0] = Slot("08:00", "09:00", ("1A", "S1", "Alice"), ("1B", "S1", "Bob"));
                broken["Monday"].Add(Slot("09:00", "10:00", ("1A", "S1", "Alice"), ("1B", "S1", "Bob")));
                broken["Tuesday"].Add(Slot("09:00", "10:00"));
                break;
            case "subject has too few periods":
                broken["Tuesday"][0].Classes["1A"] = null;
                break;
            case "subject changes teacher mid-week":
                broken["Tuesday"][0].Classes["1A"] = new SlotAssignment { Subject = "S1", Teacher = "Bob" };
                broken["Tuesday"][0].Classes["1B"] = new SlotAssignment { Subject = "S1", Teacher = "Alice" };
                break;
            case "one teacher takes two subjects in a section":
                periods["1A"] = new List<string> { "S1", "S2" };
                broken["Monday"][0].Classes["1A"] = new SlotAssignment { Subject = "S1", Teacher = "Alice" };
                broken["Tuesday"][0].Classes["1A"] = new SlotAssignment { Subject = "S2", Teacher = "Alice" };
                teachers[0] = NamedTeacher(1, "Alice", new[] { "S1", "S2" }, days);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(breakage), breakage, "unknown breakage");
        }

        Assert.ThrowsAny<Exception>(() =>
            GeneratedRoutineAssertions.AssertValid(broken, teachers, periods, days, periodsPerSubject: 2));
    }

    private static FormattedSlot Slot(string start, string end, params (string Section, string Subject, string Teacher)[] classes)
    {
        var slot = new FormattedSlot { TimeSlot = new TimeSlot { Start = start, End = end } };

        foreach (var section in new[] { "1A", "1B" })
        {
            slot.Classes[section] = null;
        }
        foreach (var (section, subject, teacher) in classes)
        {
            slot.Classes[section] = new SlotAssignment { Subject = subject, Teacher = teacher };
        }

        return slot;
    }

    private static Teacher NamedTeacher(long id, string name, string[] subjects, IEnumerable<string> days,
        string start = "08:00", string end = "18:00")
    {
        var availability = new Dictionary<string, List<AvailabilityWindow>>();
        foreach (var day in days) availability[day] = new() { new AvailabilityWindow { Start = start, End = end } };

        return new Teacher { Id = id, Name = name, Subjects = subjects.ToList(), Availability = availability };
    }

    private static AddTeacherRequest Teacher(string name, string[] subjects, IEnumerable<string> days, string start, string end)
    {
        var availability = new Dictionary<string, List<AvailabilityWindow>>();
        foreach (var day in days) availability[day] = new() { new AvailabilityWindow { Start = start, End = end } };

        return new AddTeacherRequest { Name = name, SubjectsArray = subjects.ToList(), AvailabilityJson = availability };
    }
}
