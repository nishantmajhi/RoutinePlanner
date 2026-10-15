using RoutinePlanner.Web.Models;

namespace RoutinePlanner.Web.Services.Scheduling;

/// <summary>Port of utils/schedule/slotGenerator.js.</summary>
public static class SlotGenerator
{
    public static List<TimeSlot> GenerateTimeSlots(
        string startTime, string breakStart, string breakEnd, string endTime, int periodDuration)
    {
        // A non-positive duration never advances currentTime, so the loop below would append slots
        // of zero length until the process ran out of memory. The request validator rejects it
        // first, but this is a public entry point and the failure is unrecoverable rather than loud.
        if (periodDuration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(periodDuration), periodDuration, "Period duration must be a positive number of minutes.");
        }

        var slots = new List<TimeSlot>();
        var currentTime = TimeUtils.TimeToMinutes(startTime);
        var breakStartMin = TimeUtils.TimeToMinutes(breakStart);
        var breakEndMin = TimeUtils.TimeToMinutes(breakEnd);
        var endTimeMin = TimeUtils.TimeToMinutes(endTime);

        while (currentTime < endTimeMin)
        {
            if (currentTime >= breakStartMin && currentTime < breakEndMin)
            {
                currentTime = breakEndMin;
                continue;
            }

            var slotEnd = currentTime + periodDuration;
            if (slotEnd > breakStartMin && currentTime < breakStartMin)
            {
                currentTime = breakEndMin;
                continue;
            }

            if (slotEnd > endTimeMin)
            {
                break;
            }

            slots.Add(new TimeSlot { Start = TimeUtils.MinutesToTime(currentTime), End = TimeUtils.MinutesToTime(slotEnd) });
            currentTime = slotEnd;
        }

        return slots;
    }

    public static RoutineGrid InitializeRoutine(List<string> days, List<TimeSlot> timeSlots, List<string> sections)
    {
        var routine = new RoutineGrid();

        foreach (var day in days)
        {
            var daySlots = new List<Dictionary<string, GridAssignment?>>();
            for (var i = 0; i < timeSlots.Count; i++)
            {
                var slot = new Dictionary<string, GridAssignment?>();
                foreach (var section in sections)
                {
                    slot[section] = null;
                }
                daySlots.Add(slot);
            }
            routine[day] = daySlots;
        }

        return routine;
    }
}
