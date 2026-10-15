using RoutinePlanner.Web.Models;
using RoutinePlanner.Web.Services.Scheduling;

namespace RoutinePlanner.Web.Services;

/// <summary>
/// Checks a teacher's subjects and availability before they are written to the database.
///
/// Neither POST /teacher/add nor PUT /teacher/update used to look at either field, so any JSON of
/// the right shape was stored verbatim. An availability window of "9" rather than "09:00" was
/// accepted, and then every later attempt to generate a routine — for any section, whether or not
/// it involved this teacher — failed with an unattributed internal server error, because the time
/// was parsed deep inside the solver. Rejecting it here means the request that introduced the bad
/// value is the request that fails, and it fails saying which field and why.
/// </summary>
public static class TeacherPayloadValidator
{
    private static readonly string[] ValidDays =
        { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

    public static string? ValidateSubjects(List<string>? subjects)
    {
        if (subjects is null) return null;

        if (subjects.Any(string.IsNullOrWhiteSpace))
            return "subjects_array contains an empty subject code";

        var duplicate = subjects.GroupBy(s => s, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            return $"subjects_array lists {duplicate.Key} more than once";

        return null;
    }

    public static string? ValidateAvailability(Dictionary<string, List<AvailabilityWindow>>? availability)
    {
        if (availability is null) return null;

        foreach (var (day, windows) in availability)
        {
            if (!ValidDays.Contains(day))
                return $"availability_json has an entry for \"{day}\", which is not a day of the week";

            if (windows is null) continue;

            foreach (var window in windows)
            {
                if (!TimeUtils.TryTimeToMinutes(window.Start, out var start))
                    return $"{day} availability starts at \"{window.Start}\", which is not a time in HH:MM form";

                if (!TimeUtils.TryTimeToMinutes(window.End, out var end))
                    return $"{day} availability ends at \"{window.End}\", which is not a time in HH:MM form";

                if (end <= start)
                    return $"{day} availability runs from \"{window.Start}\" to \"{window.End}\", which ends no later than it starts";
            }
        }

        return null;
    }
}
