using RoutinePlanner.Web.Models;

namespace RoutinePlanner.Web.Services.Scheduling;

/// <summary>Port of utils/schedule/teacherLocking.js.</summary>
public static class TeacherLocking
{
    /// <summary>
    /// Which teacher a section's subject is pinned to, once the first period of it is placed.
    /// Keyed by the (section, subject) pair rather than by the string $"{section}-{subject}" the
    /// JS version used: concatenating with a separator that is itself legal inside both halves
    /// makes ("1A-B", "C") and ("1A", "B-C") the same key, so a lock taken on one silently applied
    /// to the other and the solver constrained a subject it had never assigned.
    ///
    /// The value is the teacher's row id, not their name. Two rows of the teachers table may
    /// legitimately carry the same name, and resolving a name back to a teacher picked whichever
    /// row came first — so a lock could be taken on one person and then checked against another,
    /// putting a period in a slot where the teacher it names is unavailable.
    /// </summary>
    public static long? GetLockedTeacher(
        Dictionary<(string Section, string Subject), long> sectionSubjectTeachers, string section, string subject)
    {
        return sectionSubjectTeachers.TryGetValue((section, subject), out var teacherId) ? teacherId : null;
    }

    public static void LockTeacherToSectionSubject(
        Dictionary<(string Section, string Subject), long> sectionSubjectTeachers,
        Dictionary<long, Dictionary<string, HashSet<string>>> teacherSectionSubjects,
        GenerationLogger logger,
        string section, string subject, Teacher teacher)
    {
        sectionSubjectTeachers[(section, subject)] = teacher.Id;

        if (!teacherSectionSubjects.TryGetValue(teacher.Id, out var sectionsMap))
        {
            sectionsMap = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            teacherSectionSubjects[teacher.Id] = sectionsMap;
        }
        if (!sectionsMap.TryGetValue(section, out var subjectsSet))
        {
            subjectsSet = new HashSet<string>(StringComparer.Ordinal);
            sectionsMap[section] = subjectsSet;
        }
        subjectsSet.Add(subject);

        logger.AddLog($"Locked teacher {teacher.Name} (id {teacher.Id}) to {section}-{subject}");
    }

    public static void UnlockTeacherFromSectionSubject(
        Dictionary<(string Section, string Subject), long> sectionSubjectTeachers,
        Dictionary<long, Dictionary<string, HashSet<string>>> teacherSectionSubjects,
        GenerationLogger logger,
        string section, string subject)
    {
        if (!sectionSubjectTeachers.TryGetValue((section, subject), out var teacherId)) return;

        if (teacherSectionSubjects.TryGetValue(teacherId, out var sectionsMap) &&
            sectionsMap.TryGetValue(section, out var subjectsSet))
        {
            subjectsSet.Remove(subject);
            if (subjectsSet.Count == 0) sectionsMap.Remove(section);
            if (sectionsMap.Count == 0) teacherSectionSubjects.Remove(teacherId);
        }

        logger.AddLog($"Unlocked teacher id {teacherId} from {section}-{subject}");
        sectionSubjectTeachers.Remove((section, subject));
    }

    /// <summary>
    /// True when this teacher is already pinned to some subject in this section. One teacher takes
    /// at most one subject per section, so a teacher who appears here is not a candidate for any
    /// other subject of that section.
    /// </summary>
    public static bool TeacherAlreadyTeachesInSection(
        Dictionary<long, Dictionary<string, HashSet<string>>> teacherSectionSubjects, long teacherId, string section)
    {
        return teacherSectionSubjects.TryGetValue(teacherId, out var sectionsMap) &&
               sectionsMap.TryGetValue(section, out var subjectsSet) &&
               subjectsSet.Count > 0;
    }

    public static bool IsFirstAssignmentForSectionSubject(
        RoutineGrid routine, List<string> days, List<TimeSlot> timeSlots, string section, string subject)
    {
        foreach (var day in days)
        {
            for (var slotIndex = 0; slotIndex < timeSlots.Count; slotIndex++)
            {
                var assignment = routine[day][slotIndex][section];
                if (assignment is not null && assignment.Subject == subject) return false;
            }
        }
        return true;
    }
}
