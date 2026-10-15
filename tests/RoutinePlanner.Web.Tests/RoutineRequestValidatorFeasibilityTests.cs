using System.Text.Json;
using RoutinePlanner.Web.Models;
using RoutinePlanner.Web.Services;
using RoutinePlanner.Web.Services.Scheduling;
using Xunit;

namespace RoutinePlanner.Web.Tests;

/// <summary>
/// The validator at unit level. RoutineRequestValidatorTests already covers the rule that made
/// feasibility depend on the busiest single section rather than the union of every section's
/// subjects; this covers what that check got wrong and what it did not check at all.
/// </summary>
public class RoutineRequestValidatorFeasibilityTests
{
    private static GenerateRequest Request(
        string[] days, string start, string breakStart, string breakEnd, string end,
        Dictionary<string, List<string>> periods, int duration, int perSubject)
    {
        var json = JsonSerializer.Serialize(new
        {
            days,
            start_time = start,
            break_start = breakStart,
            break_end = breakEnd,
            end_time = end,
            periods,
            period_duration = duration,
            periods_per_subject = perSubject,
        });
        return JsonSerializer.Deserialize<GenerateRequest>(json)!;
    }

    private static Dictionary<string, List<string>> OneSection(params string[] subjects) =>
        new() { ["1A"] = subjects.ToList() };

    /// <summary>
    /// The periods-per-day figure now comes from SlotGenerator, so the validator and the solver
    /// cannot disagree. This asserts that correspondence across a spread of shapes, because it was
    /// the divergence — not either number on its own — that was the defect.
    /// </summary>
    [Theory]
    [InlineData("08:00", "10:00", "10:30", "12:30", 60, 4)]
    [InlineData("09:00", "10:10", "11:00", "12:10", 40, 2)]   // used to be counted as 3
    [InlineData("08:00", "09:30", "10:00", "11:45", 60, 2)]
    [InlineData("08:00", "09:00", "09:30", "10:30", 30, 4)]
    [InlineData("08:00", "12:00", "12:40", "18:00", 40, 14)]
    [InlineData("08:00", "08:40", "09:00", "09:40", 60, 0)]
    public void The_reported_periods_per_day_is_the_number_the_solver_will_actually_have(
        string start, string breakStart, string breakEnd, string end, int duration, int expectedPeriods)
    {
        Assert.Equal(expectedPeriods, SlotGenerator.GenerateTimeSlots(start, breakStart, breakEnd, end, duration).Count);

        if (expectedPeriods == 0)
        {
            var noRoom = RoutineRequestValidator.ValidateScheduleFeasibility(
                Request(new[] { "Monday" }, start, breakStart, breakEnd, end, OneSection("A"), duration, 1));
            Assert.Contains("no period of that length fits", noRoom!);
            return;
        }

        // Ask for one period more than the week can hold, and check the message quotes the real
        // per-day figure rather than a recomputed one.
        var days = new[] { "Monday" };
        var subjects = Enumerable.Range(1, expectedPeriods + 1).Select(i => $"S{i}").ToArray();

        var error = RoutineRequestValidator.ValidateScheduleFeasibility(
            Request(days, start, breakStart, breakEnd, end, OneSection(subjects), duration, 1));

        Assert.NotNull(error);
        Assert.Contains($"({expectedPeriods} periods/day", error!);
        Assert.Contains($"only {expectedPeriods} slots available", error);
    }

    [Fact]
    public void A_schedule_that_exactly_fills_the_week_is_feasible()
    {
        // 4 periods/day x 2 days = 8; four subjects at two periods each = 8.
        var error = RoutineRequestValidator.ValidateScheduleFeasibility(Request(
            new[] { "Monday", "Tuesday" }, "08:00", "10:00", "10:30", "12:30",
            OneSection("A", "B", "C", "D"), 60, 2));

        Assert.Null(error);
    }

    /// <summary>
    /// A subject takes at most one period per section per day, so N periods need N distinct days.
    /// Total-slot arithmetic cannot see this, and it is the case that sent the solver into a search
    /// with no solution in it.
    /// </summary>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(5, 6)]
    public void More_periods_per_subject_than_days_is_infeasible(int dayCount, int periodsPerSubject)
    {
        var days = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" }.Take(dayCount).ToArray();

        var error = RoutineRequestValidator.ValidateScheduleFeasibility(Request(
            days, "08:00", "12:00", "12:40", "18:00", OneSection("A"), 40, periodsPerSubject));

        Assert.NotNull(error);
        Assert.Contains($"{periodsPerSubject} different days", error!);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(5, 2)]
    public void As_many_periods_per_subject_as_days_or_fewer_is_feasible(int dayCount, int periodsPerSubject)
    {
        var days = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" }.Take(dayCount).ToArray();

        Assert.Null(RoutineRequestValidator.ValidateScheduleFeasibility(Request(
            days, "08:00", "12:00", "12:40", "18:00", OneSection("A"), 40, periodsPerSubject)));
    }

    [Theory]
    [InlineData("\"60\"")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("[60]")]
    [InlineData("{}")]
    [InlineData("0")]
    [InlineData("-30")]
    [InlineData("1.5")]
    [InlineData("99999999999999")]
    public void A_period_duration_that_is_not_a_positive_whole_number_is_reported(string value)
    {
        var req = JsonSerializer.Deserialize<GenerateRequest>($$"""
        { "days": ["Monday"], "start_time": "08:00", "break_start": "10:00",
          "break_end": "10:30", "end_time": "12:30", "periods": { "1A": ["A"] },
          "period_duration": {{value}}, "periods_per_subject": 1 }
        """)!;

        Assert.Equal("Period duration must be a positive number", RoutineRequestValidator.ValidateRoutineRequest(req));

        // And the feasibility check, which also reads it, reports rather than throwing.
        Assert.NotNull(RoutineRequestValidator.ValidateScheduleFeasibility(req));
    }

    [Theory]
    [InlineData("""{ "1A": null }""", "no subjects")]
    [InlineData("""{ "1A": [] }""", "no subjects")]
    [InlineData("""{ "1A": ["A"], "1B": [] }""", "1B")]
    [InlineData("""{ "1A": ["A", ""] }""", "empty subject code")]
    [InlineData("""{ "1A": ["A", "   "] }""", "empty subject code")]
    [InlineData("""{ "1A": ["A", "A"] }""", "more than once")]
    [InlineData("""{ "1A": ["A", "B", "A"] }""", "more than once")]
    public void A_section_whose_subject_list_cannot_be_scheduled_is_reported(string periods, string expected)
    {
        var req = JsonSerializer.Deserialize<GenerateRequest>($$"""
        { "days": ["Monday","Tuesday"], "start_time": "08:00", "break_start": "10:00",
          "break_end": "10:30", "end_time": "12:30", "periods": {{periods}},
          "period_duration": 60, "periods_per_subject": 1 }
        """)!;

        var error = RoutineRequestValidator.ValidateRoutineRequest(req);

        Assert.NotNull(error);
        Assert.Contains(expected, error!);
    }

    /// <summary>The same subject in two different sections is ordinary and must stay allowed —
    /// two sections both taking Advanced Operating Systems is the normal case.</summary>
    [Fact]
    public void The_same_subject_in_two_different_sections_is_fine()
    {
        var req = JsonSerializer.Deserialize<GenerateRequest>("""
        { "days": ["Monday","Tuesday"], "start_time": "08:00", "break_start": "10:00",
          "break_end": "10:30", "end_time": "12:30",
          "periods": { "1A": ["CSC538","CSC539"], "1B": ["CSC538","CSC539"] },
          "period_duration": 60, "periods_per_subject": 2 }
        """)!;

        Assert.Null(RoutineRequestValidator.ValidateRoutineRequest(req));
        Assert.Null(RoutineRequestValidator.ValidateScheduleFeasibility(req));
    }

    [Fact]
    public void A_day_listed_twice_is_reported()
    {
        var req = JsonSerializer.Deserialize<GenerateRequest>("""
        { "days": ["Monday","Tuesday","Monday"], "start_time": "08:00", "break_start": "10:00",
          "break_end": "10:30", "end_time": "12:30", "periods": { "1A": ["A"] },
          "period_duration": 60, "periods_per_subject": 1 }
        """)!;

        Assert.Equal("Days must not contain duplicates", RoutineRequestValidator.ValidateRoutineRequest(req));
    }

    [Theory]
    [InlineData("09:00", true)]
    [InlineData("9:00", true)]
    [InlineData("23:59", true)]
    [InlineData("00:00", true)]
    [InlineData("24:00", false)]
    [InlineData("09:60", false)]
    [InlineData("9", false)]
    [InlineData("", false)]
    public void The_time_format_check_is_unchanged(string time, bool expected)
    {
        Assert.Equal(expected, RoutineRequestValidator.IsValidTimeFormat(time));
    }

    [Theory]
    [InlineData("10:00", "09:00", "12:00", "17:00", "Start time must be before break start time")]
    [InlineData("08:00", "12:00", "11:00", "17:00", "Break start time must be before break end time")]
    [InlineData("08:00", "12:00", "17:00", "16:00", "Break end time must be before end time")]
    [InlineData("08:00", "08:20", "12:00", "17:00", "at least 30 minutes between start time and break")]
    [InlineData("08:00", "12:00", "12:30", "12:50", "at least 30 minutes between break end and end time")]
    public void The_time_sequence_rules_are_unchanged(string start, string bs, string be, string end, string expected)
    {
        Assert.Contains(expected, RoutineRequestValidator.ValidateTimeSequence(start, bs, be, end));
    }

    [Fact]
    public void A_sound_time_sequence_is_accepted()
    {
        Assert.Null(RoutineRequestValidator.ValidateTimeSequence("08:00", "12:00", "12:40", "18:00"));
    }
}
