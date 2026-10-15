using System.Globalization;

namespace RoutinePlanner.Web.Services.Scheduling;

/// <summary>Port of utils/schedule/timeUtils.js.</summary>
public static class TimeUtils
{
    /// <summary>
    /// Parses "HH:MM" into minutes since midnight. Throws FormatException naming the offending
    /// value rather than letting a malformed string fail as an IndexOutOfRangeException from
    /// parts[1] or an unqualified "input string was not in a correct format" from int.Parse —
    /// availability windows come out of the database unvalidated, and an availability of "9"
    /// instead of "09:00" used to surface as a bare 500 with nothing to say which teacher it was.
    /// </summary>
    public static int TimeToMinutes(string timeStr)
    {
        if (!TryTimeToMinutes(timeStr, out var minutes))
        {
            throw new FormatException(
                $"\"{timeStr}\" is not a time of day in HH:MM form (00:00 to 23:59).");
        }
        return minutes;
    }

    public static bool TryTimeToMinutes(string? timeStr, out int minutes)
    {
        minutes = 0;
        if (timeStr is null) return false;

        var parts = timeStr.Split(':');
        if (parts.Length != 2) return false;

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)) return false;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var mins)) return false;

        if (hours is < 0 or > 23) return false;
        if (mins is < 0 or > 59) return false;

        minutes = hours * 60 + mins;
        return true;
    }

    public static string MinutesToTime(int minutes)
    {
        var hours = minutes / 60;
        var mins = minutes % 60;
        return $"{hours:D2}:{mins:D2}";
    }
}
