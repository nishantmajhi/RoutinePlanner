using System.Text.Json;
using RoutinePlanner.Web.Models;
using RoutinePlanner.Web.Services;
using Xunit;

namespace RoutinePlanner.Web.Tests;

/// <summary>
/// Guards ValidateScheduleFeasibility's core assumption: sections are scheduled independently
/// within one shared day/slot grid (they run in parallel, taught by different teachers in the
/// same period — see ScheduleConstraints.ValidateRoutine, which tracks period counts per
/// section). Feasibility must therefore depend on the busiest single section's own subject
/// count, never on the union of subjects across every section combined.
/// </summary>
public class RoutineRequestValidatorTests
{
    private static GenerateRequest BuildRequest(
        Dictionary<string, List<string>> periods,
        int periodsPerSubject,
        List<string>? days = null)
    {
        return new GenerateRequest
        {
            Days = days ?? new List<string> { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" },
            StartTime = "08:00",
            BreakStart = "10:00",
            BreakEnd = "10:30",
            EndTime = "12:30",
            Periods = periods,
            // 08:00-10:00 (120 min) + 10:30-12:30 (120 min) = 240 min available / 60-min
            // periods = 4 periods/day, matching the "4 periods/day x 6 days" scenario below.
            PeriodDuration = JsonSerializer.SerializeToElement(60),
            PeriodsPerSubject = JsonSerializer.SerializeToElement(periodsPerSubject),
        };
    }

    [Fact]
    public void Feasible_When_No_Single_Section_Exceeds_Available_Slots_Even_If_The_Union_Of_All_Subjects_Would()
    {
        // 4 periods/day x 6 days = 24 slots available.
        // Two sections, 4 *different* subjects each (8 distinct subjects in total).
        // Per-section requirement: 4 subjects x 4 periods/subject = 16 <= 24 -> feasible.
        // Union-based (buggy) requirement: 8 distinct subjects x 4 periods/subject = 32 > 24
        // -> would have wrongly reported this as infeasible.
        var periods = new Dictionary<string, List<string>>
        {
            ["1A"] = new() { "MTH101", "ENG101", "SCI101", "HIS101" }, // 4 subjects
            ["1B"] = new() { "GEO101", "ART101", "PED101", "CSC101" }, // 4 different subjects
        };
        var request = BuildRequest(periods, periodsPerSubject: 4);

        var result = RoutineRequestValidator.ValidateScheduleFeasibility(request);

        Assert.Null(result);
    }

    [Fact]
    public void Infeasible_When_The_Busiest_Section_Alone_Exceeds_Available_Slots()
    {
        // 4 periods/day x 6 days = 24 slots available.
        // One section needs 5 subjects x 6 periods/subject = 30 > 24 -> genuinely infeasible.
        var periods = new Dictionary<string, List<string>>
        {
            ["1A"] = new() { "MTH101", "ENG101", "SCI101", "HIS101", "GEO101" },
        };
        var request = BuildRequest(periods, periodsPerSubject: 6);

        var result = RoutineRequestValidator.ValidateScheduleFeasibility(request);

        Assert.NotNull(result);
        Assert.Contains("Need 30 periods but only 24 slots available", result);
    }
}
