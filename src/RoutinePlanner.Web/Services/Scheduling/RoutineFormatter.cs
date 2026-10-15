using RoutinePlanner.Web.Models;

namespace RoutinePlanner.Web.Services.Scheduling;

/// <summary>Port of utils/schedule/routineFormatter.js.</summary>
public static class RoutineFormatter
{
    /// <summary>
    /// Projects the solver's grid down to what is saved and sent to the browser: the teacher's
    /// display name, not the teacher. This is the only place the identity becomes a label again.
    /// </summary>
    public static FormattedRoutine FormatRoutineWithTimeSlots(RoutineGrid routine, List<TimeSlot> timeSlots)
    {
        var formatted = new FormattedRoutine();

        foreach (var (day, slots) in routine)
        {
            var formattedSlots = new List<FormattedSlot>();

            for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
            {
                var slot = slots[slotIndex];
                var timeSlot = timeSlots[slotIndex];

                var formattedSlot = new FormattedSlot
                {
                    TimeSlot = new TimeSlot { Start = timeSlot.Start, End = timeSlot.End },
                };

                foreach (var (section, assignment) in slot)
                {
                    formattedSlot.Classes[section] = assignment is null
                        ? null
                        : new SlotAssignment { Subject = assignment.Subject, Teacher = assignment.Teacher.Name };
                }

                formattedSlots.Add(formattedSlot);
            }

            formatted[day] = formattedSlots;
        }

        return formatted;
    }
}
