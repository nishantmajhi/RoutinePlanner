using RoutinePlanner.Web.Models;

namespace RoutinePlanner.Web.Services.Scheduling;

/// <summary>Port of utils/schedule/scheduleConstraints.js.</summary>
public static class ScheduleConstraints
{
    public static bool IsSubjectAlreadyScheduledToday(RoutineGrid routine, string day, string section, string subject)
    {
        foreach (var slot in routine[day])
        {
            if (slot.TryGetValue(section, out var assignment) && assignment is not null && assignment.Subject == subject)
            {
                return true;
            }
        }
        return false;
    }

    public static bool ValidateRoutine(RoutineGrid routine, Dictionary<string, List<string>> periods, int periodsPerSubject)
    {
        var periodCounts = new Dictionary<string, Dictionary<string, int>>();
        var dailySubjectCounts = new Dictionary<string, Dictionary<string, Dictionary<string, int>>>();

        foreach (var (section, subjects) in periods)
        {
            periodCounts[section] = new Dictionary<string, int>();
            dailySubjectCounts[section] = new Dictionary<string, Dictionary<string, int>>();
            foreach (var subject in subjects)
            {
                periodCounts[section][subject] = 0;
            }
        }

        foreach (var (day, slots) in routine)
        {
            foreach (var section in periods.Keys)
            {
                dailySubjectCounts[section][day] = new Dictionary<string, int>();
            }

            foreach (var slot in slots)
            {
                foreach (var (section, assignment) in slot)
                {
                    if (assignment is null) continue;

                    if (periodCounts.TryGetValue(section, out var subjectCounts) &&
                        subjectCounts.ContainsKey(assignment.Subject))
                    {
                        subjectCounts[assignment.Subject]++;

                        var dayCounts = dailySubjectCounts[section][day];
                        dayCounts.TryGetValue(assignment.Subject, out var current);
                        dayCounts[assignment.Subject] = current + 1;
                    }
                }
            }
        }

        foreach (var (section, subjects) in periods)
        {
            foreach (var subject in subjects)
            {
                if (periodCounts[section][subject] != periodsPerSubject)
                {
                    return false;
                }
            }
        }

        foreach (var (_, dayData) in dailySubjectCounts)
        {
            foreach (var (_, subjectCounts) in dayData)
            {
                foreach (var (_, count) in subjectCounts)
                {
                    if (count > 1) return false;
                }
            }
        }

        return true;
    }
}
