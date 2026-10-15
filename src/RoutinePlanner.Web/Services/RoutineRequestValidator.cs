using System.Text.Json;
using System.Text.RegularExpressions;
using RoutinePlanner.Web.Models;
using RoutinePlanner.Web.Services.Scheduling;

namespace RoutinePlanner.Web.Services;

/// <summary>Port of utils/validator.js.</summary>
public static partial class RoutineRequestValidator
{
    private static readonly string[] ValidDays =
        { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

    [GeneratedRegex(@"^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$")]
    private static partial Regex TimeFormatRegex();

    public static bool IsValidTimeFormat(string? timeString) =>
        timeString is not null && TimeFormatRegex().IsMatch(timeString);

    /// <summary>
    /// Reads a JSON number as a positive Int32 without throwing on the wrong JSON type.
    /// JsonElement.TryGetInt32 only reports "this number does not fit in an Int32" by returning
    /// false; given a string, bool, array or object it throws InvalidOperationException instead.
    /// The request body is client-supplied, so a period_duration sent as the string "60" has to be
    /// a 400 naming the field, not an unhandled exception the controller turns into a blanket 500.
    /// </summary>
    public static bool TryReadPositiveInt(JsonElement? element, out int value)
    {
        value = 0;
        return element is { ValueKind: JsonValueKind.Number } number
            && number.TryGetInt32(out value)
            && value > 0;
    }

    public static string? ValidateRoutineRequest(GenerateRequest req)
    {
        if (req.Days is null || req.Days.Count == 0)
            return "Days must be a non-empty array";

        if (!IsValidTimeFormat(req.StartTime))
            return "Start time must be in HH:MM format";

        if (!IsValidTimeFormat(req.BreakStart))
            return "Break start time must be in HH:MM format";

        if (!IsValidTimeFormat(req.BreakEnd))
            return "Break end time must be in HH:MM format";

        if (!IsValidTimeFormat(req.EndTime))
            return "End time must be in HH:MM format";

        if (req.Periods is null || req.Periods.Count == 0)
            return "periods must be a non-empty object";

        if (!TryReadPositiveInt(req.PeriodDuration, out _))
            return "Period duration must be a positive number";

        if (!TryReadPositiveInt(req.PeriodsPerSubject, out _))
            return "Periods per subject must be a positive number";

        foreach (var day in req.Days)
        {
            if (!ValidDays.Contains(day))
                return $"Invalid day: {day}. Must be one of: {string.Join(", ", ValidDays)}";
        }

        // A day listed twice becomes two independent columns of the same name in the grid, so the
        // solver can place a subject in both and still believe its one-period-per-day rule holds.
        if (req.Days.Distinct(StringComparer.Ordinal).Count() != req.Days.Count)
            return "Days must not contain duplicates";

        foreach (var (section, subjects) in req.Periods)
        {
            // A null subject list threw a NullReferenceException out of the feasibility check,
            // which the controller reported as an internal server error. An empty one was accepted
            // and produced a timetable with every slot blank, which was then saved as a routine and
            // rendered as a page of dashes.
            if (subjects is null || subjects.Count == 0)
                return $"Section {section} has no subjects selected";

            if (subjects.Any(string.IsNullOrWhiteSpace))
                return $"Section {section} contains an empty subject code";

            // The solver groups assignments by section and subject, so a code repeated within one
            // section asks for periodsPerSubject periods twice under a single identity. Its own
            // completeness check then counts those periods once and compares the total against
            // periodsPerSubject, so every complete schedule it builds is rejected and the search
            // becomes an exhaustive walk of a space with no solution in it.
            var duplicate = subjects.GroupBy(s => s, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
                return $"Section {section} lists subject {duplicate.Key} more than once";
        }

        return null;
    }

    public static string? ValidateTimeSequence(string startTime, string breakStart, string breakEnd, string endTime)
    {
        var start = ParseTime(startTime);
        var brkStart = ParseTime(breakStart);
        var brkEnd = ParseTime(breakEnd);
        var end = ParseTime(endTime);

        if (start >= brkStart) return "Start time must be before break start time";
        if (brkStart >= brkEnd) return "Break start time must be before break end time";
        if (brkEnd >= end) return "Break end time must be before end time";
        if (brkStart - start < 30) return "There should be at least 30 minutes between start time and break";
        if (end - brkEnd < 30) return "There should be at least 30 minutes between break end and end time";

        return null;
    }

    public static string? ValidateScheduleFeasibility(GenerateRequest req)
    {
        if (!TryReadPositiveInt(req.PeriodDuration, out var periodDuration))
            return "Period duration must be a positive number";

        if (!TryReadPositiveInt(req.PeriodsPerSubject, out var periodsPerSubject))
            return "Periods per subject must be a positive number";

        // Count the day's periods by asking the generator the solver itself will use, instead of
        // dividing the available minutes a second time here. Dividing timeBeforeBreak +
        // timeAfterBreak by the period length treats the two halves of the day as one continuous
        // run, but a period may not straddle the break: SlotGenerator drops any slot that would,
        // so each half loses its own remainder separately. A 09:00 start, 10:10-11:00 break and
        // 12:10 end with 40-minute periods came to floor(140 / 40) = 3 here and 1 + 1 = 2 there,
        // and a request for three periods was accepted as feasible against a grid with room for
        // two. Deriving the number from GenerateTimeSlots makes the two agree by construction.
        var periodsPerDay = SlotGenerator.GenerateTimeSlots(
            req.StartTime!, req.BreakStart!, req.BreakEnd!, req.EndTime!, periodDuration).Count;

        if (periodsPerDay == 0)
            return "Schedule not feasible: no period of that length fits between the start, break and end times";

        var totalPeriodsAvailable = periodsPerDay * req.Days!.Count;

        // Each section is scheduled independently within the same shared day/slot grid
        // (sections run in parallel, taught by different teachers in the same period) — see
        // ScheduleConstraints.ValidateRoutine, which checks period counts per section. So
        // feasibility depends on the busiest single section's own subject count, not the union
        // of subjects across every section combined.
        var maxSubjectsInAnySection = req.Periods!.Values.Max(subjects => subjects!.Count);
        var totalPeriodsRequired = maxSubjectsInAnySection * periodsPerSubject;

        if (totalPeriodsRequired > totalPeriodsAvailable)
        {
            return $"Schedule not feasible: Need {totalPeriodsRequired} periods but only {totalPeriodsAvailable} " +
                   $"slots available ({periodsPerDay} periods/day × {req.Days!.Count} days)";
        }

        // ScheduleConstraints.IsSubjectAlreadyScheduledToday allows a subject at most one period
        // per section per day, so periodsPerSubject periods need periodsPerSubject *distinct* days.
        // A total-slot count cannot see that. Six periods of each subject across five days passed
        // this check and then sent the solver into an exhaustive search of a space with no solution
        // in it: measured at over twenty seconds on a single attempt thread, and generation runs
        // one attempt per core.
        if (periodsPerSubject > req.Days!.Count)
        {
            var dayWord = req.Days!.Count == 1 ? "day is" : "days are";
            return $"Schedule not feasible: {periodsPerSubject} periods per subject need {periodsPerSubject} " +
                   $"different days, but only {req.Days!.Count} {dayWord} selected " +
                   $"(a subject is scheduled at most once per day)";
        }

        return null;
    }

    private static int ParseTime(string timeString) => TimeUtils.TimeToMinutes(timeString);
}
