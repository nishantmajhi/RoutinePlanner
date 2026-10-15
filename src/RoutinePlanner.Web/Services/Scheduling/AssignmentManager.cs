using RoutinePlanner.Web.Models;

namespace RoutinePlanner.Web.Services.Scheduling;

/// <summary>Port of utils/schedule/assignmentManager.js.</summary>
public static class AssignmentManager
{
    /// <summary>Fisher-Yates shuffle, using the caller's own Random instance so parallel
    /// attempts (see RoutinePlannerService) don't share mutable RNG state.</summary>
    public static List<T> ShuffleArray<T>(IReadOnlyList<T> array, Random rng)
    {
        var shuffled = new List<T>(array);
        for (var i = shuffled.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        return shuffled;
    }

    public static List<SectionSubjectAssignment> GetAllAssignments(
        Dictionary<string, List<string>> periods, int periodsPerSubject, GenerationLogger logger, Random rng)
    {
        var assignments = new List<SectionSubjectAssignment>();

        foreach (var (section, subjects) in periods)
        {
            foreach (var subject in subjects)
            {
                for (var i = 0; i < periodsPerSubject; i++)
                {
                    assignments.Add(new SectionSubjectAssignment { Section = section, Subject = subject });
                }
            }
        }

        logger.AddLog($"\nInitial assignments: {assignments.Count}");

        var grouped = new Dictionary<string, List<SectionSubjectAssignment>>();
        foreach (var assignment in assignments)
        {
            var key = $"{assignment.Section}-{assignment.Subject}";
            if (!grouped.TryGetValue(key, out var list))
            {
                list = new List<SectionSubjectAssignment>();
                grouped[key] = list;
            }
            list.Add(assignment);
        }

        var groupKeys = grouped.Keys.ToList();
        var shuffledGroupKeys = ShuffleArray(groupKeys, rng);

        var finalAssignments = new List<SectionSubjectAssignment>();
        foreach (var key in shuffledGroupKeys)
        {
            finalAssignments.AddRange(grouped[key]);
        }

        logger.AddLog($"Final assignment order ready ({finalAssignments.Count} total)");
        return finalAssignments;
    }
}
