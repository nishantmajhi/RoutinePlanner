using RoutinePlanner.Web.Models;

namespace RoutinePlanner.Web.Services.Scheduling;

/// <summary>Port of utils/schedule/teacherManager.js.</summary>
public static class TeacherManager
{
    public static int GetTeacherCurrentWorkload(Teacher teacher, RoutineGrid routine)
    {
        var workload = 0;
        foreach (var (_, slots) in routine)
        {
            foreach (var slot in slots)
            {
                foreach (var assignment in slot.Values)
                {
                    if (assignment is not null && assignment.Teacher.Id == teacher.Id) workload++;
                }
            }
        }
        return workload;
    }

    public static bool IsTeacherAvailableAtSlot(
        Teacher teacher, string day, TimeSlot timeSlot, RoutineGrid routine, int slotIndex)
    {
        if (!teacher.Availability.TryGetValue(day, out var windows) || windows is null || windows.Count == 0)
        {
            return false;
        }

        var slotStart = TimeUtils.TimeToMinutes(timeSlot.Start);
        var slotEnd = TimeUtils.TimeToMinutes(timeSlot.End);

        var withinAvailableHours = false;
        foreach (var window in windows)
        {
            var availStart = TimeUtils.TimeToMinutes(window.Start);
            var availEnd = TimeUtils.TimeToMinutes(window.End);
            if (slotStart >= availStart && slotEnd <= availEnd)
            {
                withinAvailableHours = true;
                break;
            }
        }
        if (!withinAvailableHours) return false;

        // Already teaching some other section in this same period. Compared by row id, not by
        // name: while this compared names, a teacher was reported busy because a different person
        // who happens to share their name was teaching in the slot.
        foreach (var assignment in routine[day][slotIndex].Values)
        {
            if (assignment is not null && assignment.Teacher.Id == teacher.Id) return false;
        }

        return true;
    }

    /// <summary>
    /// Finds an available teacher for subject+section+slot, preferring lower workload,
    /// picking randomly among the top 3 candidates for variety — exactly like
    /// findAvailableTeacherWithSectionConstraint in the JS version.
    /// </summary>
    public static Teacher? FindAvailableTeacherWithSectionConstraint(
        Dictionary<long, Teacher> teachers,
        Dictionary<long, Dictionary<string, HashSet<string>>> teacherSectionSubjects,
        RoutineGrid routine,
        string subject, string section, string day, TimeSlot timeSlot, int slotIndex,
        GenerationLogger? logger, Random rng)
    {
        // Workload is a full walk of the grid, so it is measured once per candidate and the pairs
        // are sorted on the measurement. Passing GetTeacherCurrentWorkload straight to Sort's
        // comparison recomputed it for both sides of every comparison — O(n log n) grid walks per
        // candidate list, at every node of the backtracking search.
        var candidates = new List<(Teacher Teacher, int Workload)>();

        foreach (var teacher in teachers.Values)
        {
            if (!teacher.Subjects.Contains(subject)) continue;
            if (TeacherLocking.TeacherAlreadyTeachesInSection(teacherSectionSubjects, teacher.Id, section)) continue;
            if (!IsTeacherAvailableAtSlot(teacher, day, timeSlot, routine, slotIndex)) continue;
            candidates.Add((teacher, GetTeacherCurrentWorkload(teacher, routine)));
        }

        if (candidates.Count == 0) return null;

        candidates.Sort((a, b) => a.Workload.CompareTo(b.Workload));

        var poolSize = Math.Min(3, candidates.Count);
        var randomIndex = rng.Next(poolSize);
        return candidates[randomIndex].Teacher;
    }
}
