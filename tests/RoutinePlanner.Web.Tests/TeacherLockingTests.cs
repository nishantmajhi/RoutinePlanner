using RoutinePlanner.Web.Models;
using RoutinePlanner.Web.Services.Scheduling;
using Xunit;

namespace RoutinePlanner.Web.Tests;

/// <summary>
/// The bookkeeping that keeps one teacher on a section's subject all week, and keeps one teacher to
/// one subject per section. Both dictionaries used to be keyed by strings built with string
/// interpolation, which is where the two defects were: $"{section}-{subject}" collides whenever a
/// '-' appears in either half, and the teacher was identified by name.
/// </summary>
public class TeacherLockingTests
{
    private static Teacher Person(long id, string name) => new() { Id = id, Name = name };

    private static (Dictionary<(string, string), long> Locks, Dictionary<long, Dictionary<string, HashSet<string>>> Sections) Fresh()
        => (new Dictionary<(string, string), long>(), new Dictionary<long, Dictionary<string, HashSet<string>>>());

    [Fact]
    public void A_locked_section_subject_reports_the_teacher_it_was_locked_to()
    {
        var (locks, sections) = Fresh();
        TeacherLocking.LockTeacherToSectionSubject(locks, sections, new GenerationLogger(), "1A", "CSC538", Person(7, "Alice"));

        Assert.Equal(7, TeacherLocking.GetLockedTeacher(locks, "1A", "CSC538"));
        Assert.Null(TeacherLocking.GetLockedTeacher(locks, "1A", "CSC539"));
        Assert.Null(TeacherLocking.GetLockedTeacher(locks, "1B", "CSC538"));
    }

    /// <summary>
    /// Section "1A-B" teaching "C" and section "1A" teaching "B-C" both produced the key "1A-B-C",
    /// so a lock taken on one was read back for the other. The solver then constrained a subject it
    /// had never placed to a teacher it had never chosen for it.
    /// </summary>
    [Fact]
    public void A_hyphen_in_a_section_or_subject_name_does_not_collide_with_another_pair()
    {
        var (locks, sections) = Fresh();
        TeacherLocking.LockTeacherToSectionSubject(locks, sections, new GenerationLogger(), "1A-B", "C", Person(1, "Alice"));

        Assert.Equal(1, TeacherLocking.GetLockedTeacher(locks, "1A-B", "C"));
        Assert.Null(TeacherLocking.GetLockedTeacher(locks, "1A", "B-C"));
        Assert.Null(TeacherLocking.GetLockedTeacher(locks, "1", "A-B-C"));
    }

    [Fact]
    public void Two_colliding_pairs_can_be_locked_to_different_teachers_at_once()
    {
        var (locks, sections) = Fresh();
        var logger = new GenerationLogger();

        TeacherLocking.LockTeacherToSectionSubject(locks, sections, logger, "1A-B", "C", Person(1, "Alice"));
        TeacherLocking.LockTeacherToSectionSubject(locks, sections, logger, "1A", "B-C", Person(2, "Bob"));

        Assert.Equal(1, TeacherLocking.GetLockedTeacher(locks, "1A-B", "C"));
        Assert.Equal(2, TeacherLocking.GetLockedTeacher(locks, "1A", "B-C"));
    }

    /// <summary>Unlocking must leave the bookkeeping exactly as it was before the lock, or a
    /// backtracking search accumulates constraints that no longer correspond to the grid.</summary>
    [Fact]
    public void Unlocking_restores_the_state_the_lock_was_taken_from()
    {
        var (locks, sections) = Fresh();
        var logger = new GenerationLogger();

        TeacherLocking.LockTeacherToSectionSubject(locks, sections, logger, "1A", "CSC538", Person(7, "Alice"));
        TeacherLocking.UnlockTeacherFromSectionSubject(locks, sections, logger, "1A", "CSC538");

        Assert.Empty(locks);
        Assert.Empty(sections);
        Assert.False(TeacherLocking.TeacherAlreadyTeachesInSection(sections, 7, "1A"));
    }

    [Fact]
    public void Unlocking_something_that_was_never_locked_does_nothing()
    {
        var (locks, sections) = Fresh();
        TeacherLocking.UnlockTeacherFromSectionSubject(locks, sections, new GenerationLogger(), "1A", "CSC538");

        Assert.Empty(locks);
        Assert.Empty(sections);
    }

    [Fact]
    public void Unlocking_one_of_a_teachers_sections_leaves_the_others_alone()
    {
        var (locks, sections) = Fresh();
        var logger = new GenerationLogger();
        var alice = Person(7, "Alice");

        TeacherLocking.LockTeacherToSectionSubject(locks, sections, logger, "1A", "CSC538", alice);
        TeacherLocking.LockTeacherToSectionSubject(locks, sections, logger, "1B", "CSC539", alice);

        TeacherLocking.UnlockTeacherFromSectionSubject(locks, sections, logger, "1A", "CSC538");

        Assert.False(TeacherLocking.TeacherAlreadyTeachesInSection(sections, 7, "1A"));
        Assert.True(TeacherLocking.TeacherAlreadyTeachesInSection(sections, 7, "1B"));
        Assert.Equal(7, TeacherLocking.GetLockedTeacher(locks, "1B", "CSC539"));
    }

    /// <summary>Two people with one name are two entries here, not one.</summary>
    [Fact]
    public void Teachers_who_share_a_name_are_tracked_separately()
    {
        var (locks, sections) = Fresh();
        TeacherLocking.LockTeacherToSectionSubject(
            locks, sections, new GenerationLogger(), "1A", "CSC538", Person(1, "R. Sharma"));

        Assert.True(TeacherLocking.TeacherAlreadyTeachesInSection(sections, 1, "1A"));
        Assert.False(TeacherLocking.TeacherAlreadyTeachesInSection(sections, 2, "1A"));
    }

    [Fact]
    public void A_section_subject_already_in_the_grid_is_not_a_first_assignment()
    {
        var days = new List<string> { "Monday", "Tuesday" };
        var timeSlots = new List<TimeSlot>
        {
            new() { Start = "08:00", End = "09:00" },
            new() { Start = "09:00", End = "10:00" },
        };
        var grid = SlotGenerator.InitializeRoutine(days, timeSlots, new List<string> { "1A" });

        Assert.True(TeacherLocking.IsFirstAssignmentForSectionSubject(grid, days, timeSlots, "1A", "CSC538"));

        grid["Tuesday"][1]["1A"] = new GridAssignment { Subject = "CSC538", Teacher = Person(1, "Alice") };

        Assert.False(TeacherLocking.IsFirstAssignmentForSectionSubject(grid, days, timeSlots, "1A", "CSC538"));
        Assert.True(TeacherLocking.IsFirstAssignmentForSectionSubject(grid, days, timeSlots, "1A", "CSC539"));
    }
}
