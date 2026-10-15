using RoutinePlanner.Web.Models;
using RoutinePlanner.Web.Services.Scheduling;
using Xunit;

namespace RoutinePlanner.Web.Tests.TestHelpers;

/// <summary>
/// Everything a finished timetable has to be true of. The solver's own completeness check
/// (ScheduleConstraints.ValidateRoutine) only looks at period counts, so these are the rules it
/// assumes hold by construction — which is exactly the set worth checking independently.
///
/// Teacher names must be distinct for these to be decidable from a FormattedRoutine, because a
/// FormattedRoutine records the teacher's name and nothing else.
/// </summary>
public static class GeneratedRoutineAssertions
{
    public static void AssertValid(
        FormattedRoutine routine,
        IReadOnlyCollection<Teacher> teachers,
        Dictionary<string, List<string>> periods,
        List<string> days,
        int periodsPerSubject)
    {
        var byName = teachers.ToDictionary(t => t.Name, t => t, StringComparer.Ordinal);
        Assert.Equal(teachers.Count, byName.Count); // the caller must give every teacher a distinct name

        Assert.Equal(days.Count, routine.Count);
        foreach (var day in days) Assert.True(routine.ContainsKey(day), $"day {day} missing from the routine");

        var sectionSubjectPeriods = new Dictionary<(string, string), int>();
        var sectionSubjectTeacher = new Dictionary<(string, string), string>();

        foreach (var (day, slots) in routine)
        {
            foreach (var slot in slots)
            {
                var teachersInThisSlot = new List<string>();

                foreach (var (section, assignment) in slot.Classes)
                {
                    Assert.True(periods.ContainsKey(section), $"routine contains a section nobody asked for: {section}");
                    if (assignment is null) continue;

                    Assert.True(periods[section].Contains(assignment.Subject),
                        $"{day} {slot.TimeSlot.Start} section {section}: subject {assignment.Subject} was not requested for it");

                    var teacher = Assert.Contains(assignment.Teacher, byName);

                    // Teaches the subject at all.
                    Assert.True(teacher.Subjects.Contains(assignment.Subject),
                        $"{day} {slot.TimeSlot.Start} section {section}: {teacher.Name} does not teach {assignment.Subject}");

                    // Is available for the whole of that period.
                    AssertAvailable(teacher, day, slot.TimeSlot);

                    teachersInThisSlot.Add(assignment.Teacher);

                    var key = (section, assignment.Subject);
                    sectionSubjectPeriods[key] = sectionSubjectPeriods.GetValueOrDefault(key) + 1;

                    // One teacher per section+subject for the whole week.
                    if (sectionSubjectTeacher.TryGetValue(key, out var alreadyTeaching))
                    {
                        Assert.Equal(alreadyTeaching, assignment.Teacher);
                    }
                    else
                    {
                        sectionSubjectTeacher[key] = assignment.Teacher;
                    }
                }

                // Nobody is in two places in the same period.
                Assert.Equal(teachersInThisSlot.Count, teachersInThisSlot.Distinct(StringComparer.Ordinal).Count());
            }

            // A subject appears at most once per section per day.
            foreach (var section in periods.Keys)
            {
                var subjectsToday = slots
                    .Select(s => s.Classes.GetValueOrDefault(section))
                    .Where(a => a is not null)
                    .Select(a => a!.Subject)
                    .ToList();

                Assert.Equal(subjectsToday.Count, subjectsToday.Distinct(StringComparer.Ordinal).Count());
            }
        }

        // Every requested subject got exactly the number of periods asked for.
        foreach (var (section, subjects) in periods)
        {
            foreach (var subject in subjects)
            {
                Assert.Equal(periodsPerSubject, sectionSubjectPeriods.GetValueOrDefault((section, subject)));
            }
        }

        // One teacher takes at most one subject in any one section.
        foreach (var group in sectionSubjectTeacher.GroupBy(kv => kv.Key.Item1))
        {
            var names = group.Select(kv => kv.Value).ToList();
            Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        }
    }

    private static void AssertAvailable(Teacher teacher, string day, TimeSlot slot)
    {
        Assert.True(teacher.Availability.TryGetValue(day, out var windows) && windows is { Count: > 0 },
            $"{teacher.Name} is scheduled on {day} but has no availability that day");

        var slotStart = TimeUtils.TimeToMinutes(slot.Start);
        var slotEnd = TimeUtils.TimeToMinutes(slot.End);

        var covered = windows!.Any(w =>
            slotStart >= TimeUtils.TimeToMinutes(w.Start) && slotEnd <= TimeUtils.TimeToMinutes(w.End));

        Assert.True(covered,
            $"{teacher.Name} is scheduled {day} {slot.Start}-{slot.End}, outside every window they gave: " +
            string.Join(", ", windows!.Select(w => $"{w.Start}-{w.End}")));
    }
}
