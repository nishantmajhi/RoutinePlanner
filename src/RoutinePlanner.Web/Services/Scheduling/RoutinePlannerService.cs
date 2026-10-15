using Microsoft.Extensions.Options;
using RoutinePlanner.Web.Data;
using RoutinePlanner.Web.Models;

namespace RoutinePlanner.Web.Services.Scheduling;

public class RoutinePlannerOptions
{
    /// <summary>0 = auto (Environment.ProcessorCount).</summary>
    public int ParallelAttempts { get; set; } = 0;

    /// <summary>
    /// How long the whole search may run before it gives up, in seconds. 0 disables the limit.
    ///
    /// The search is exponential and /generate needs no authentication, so without a ceiling one
    /// request can hold every core at full load for as long as the client keeps the connection
    /// open. The validator now rejects the requests that are infeasible by arithmetic, but
    /// infeasibility that follows from who teaches what and when is not decidable up front, and
    /// that is the case this bounds.
    /// </summary>
    public int GenerationTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Fixes the random seed the attempts are derived from, so a given request produces the same
    /// timetable every time. Left null the base is drawn at random and written to the generation
    /// log, so a run that produced a good timetable can be reproduced by setting it here.
    /// </summary>
    public int? Seed { get; set; }
}

/// <summary>
/// Thrown when a teacher row cannot be used by the engine — an availability window whose times are
/// not HH:MM, say. It names the teacher, because the alternative is what this replaced: a
/// FormatException escaping mid-search and being reported as an unattributed internal error on
/// every generation until somebody guessed which row was at fault.
/// </summary>
public class InvalidTeacherDataException : Exception
{
    public InvalidTeacherDataException(string message) : base(message) { }
}

/// <summary>
/// C# port of controller/RoutinePlanner.js.
///
/// The core algorithm is still a recursive backtracking search — recursion is the right tool
/// here (it maps 1:1 onto "try an assignment, recurse, undo on failure") and running on the
/// CLR JIT rather than an interpreted VM already removes most of the interpretation overhead
/// the user was worried about.
///
/// On top of that, the JS version already injects randomness at two points (shuffled
/// assignment-group order, and a random pick among the top-3 least-loaded teachers per slot),
/// so no two runs explore the search space the same way. That makes the search
/// embarrassingly parallel: this port launches several independent attempts — each with its
/// own RNG, its own routine grid, its own bookkeeping dictionaries, so nothing is shared and no
/// locking is needed — and races them with Task.WhenAny, keeping the first valid schedule any
/// thread finds and cancelling the rest. Wall-clock time drops roughly with the number of
/// cores instead of depending on which random shuffle a single-threaded run happened to get.
/// </summary>
public class RoutinePlannerService
{
    private readonly TeacherRepository _teacherRepository;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly RoutinePlannerOptions _options;

    public RoutinePlannerService(
        TeacherRepository teacherRepository,
        IWebHostEnvironment env,
        IConfiguration config,
        IOptions<RoutinePlannerOptions> options)
    {
        _teacherRepository = teacherRepository;
        _env = env;
        _config = config;
        _options = options.Value;
    }

    public async Task<FormattedRoutine?> GenerateAsync(GenerateRequest request, CancellationToken cancellationToken)
    {
        var days = request.Days!;
        var periods = request.Periods!;

        if (!RoutineRequestValidator.TryReadPositiveInt(request.PeriodDuration, out var periodDuration))
            throw new ArgumentException("Period duration must be a positive number", nameof(request));

        if (!RoutineRequestValidator.TryReadPositiveInt(request.PeriodsPerSubject, out var periodsPerSubject))
            throw new ArgumentException("Periods per subject must be a positive number", nameof(request));

        // Read-only for the duration of generation, so it's safe to share across attempt threads.
        var teachers = _teacherRepository.LoadTeachersForEngine();
        ValidateTeacherData(teachers);

        var timeSlots = SlotGenerator.GenerateTimeSlots(
            request.StartTime!, request.BreakStart!, request.BreakEnd!, request.EndTime!, periodDuration);

        var sections = periods.Keys.ToList();

        var attemptCount = _options.ParallelAttempts > 0 ? _options.ParallelAttempts : Math.Max(1, Environment.ProcessorCount);

        using var attemptsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.GenerationTimeoutSeconds > 0)
        {
            attemptsCts.CancelAfter(TimeSpan.FromSeconds(_options.GenerationTimeoutSeconds));
        }

        var seedBase = _options.Seed ?? Random.Shared.Next();

        var tasks = new List<Task<AttemptResult?>>();
        for (var i = 0; i < attemptCount; i++)
        {
            var seed = unchecked(seedBase + i * 7919); // distinct seed per attempt, still deterministic given the base
            tasks.Add(Task.Run(() => RunAttempt(
                seed, days, periods, sections, timeSlots, periodsPerSubject, teachers, attemptsCts.Token), attemptsCts.Token));
        }

        AttemptResult? winner = null;
        var remaining = new List<Task<AttemptResult?>>(tasks);
        while (remaining.Count > 0)
        {
            var finished = await Task.WhenAny(remaining);
            remaining.Remove(finished);

            AttemptResult? result;
            try
            {
                result = await finished;
            }
            catch (OperationCanceledException)
            {
                continue;
            }

            if (result is not null)
            {
                winner = result;
                attemptsCts.Cancel(); // stop the other attempts, we already have a valid schedule
                break;
            }
        }

        // Let cancelled/losing attempts unwind without throwing into the caller.
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { }
        catch (AggregateException) { }

        // The caller asked us to stop, as opposed to us running out of search space.
        cancellationToken.ThrowIfCancellationRequested();

        if (winner is null) return null;

        var logsDir = Path.Combine(_env.ContentRootPath, _config["RoutinePlanner:LogsPath"] ?? "logs");
        winner.Logger.AddLog(
            $"\nRoutine generation completed successfully in {winner.BacktrackingSteps} steps (winning attempt of {attemptCount} run in parallel)");
        winner.Logger.AddLog(
            $"Random seed base: {seedBase} (set RoutinePlanner:Seed to this value to reproduce this run)");
        winner.Logger.SaveLogToFile(logsDir, $"routine_{winner.Logger.Timestamp}.log");

        return RoutineFormatter.FormatRoutineWithTimeSlots(winner.Routine, timeSlots);
    }

    /// <summary>
    /// Checks every stored availability window before the search starts. Times are parsed deep
    /// inside the recursion, on a worker thread, once per candidate per slot; a malformed one used
    /// to surface as an IndexOutOfRangeException from a string split, with nothing in the response
    /// or the log to say which of the teachers was responsible.
    /// </summary>
    private static void ValidateTeacherData(Dictionary<long, Teacher> teachers)
    {
        foreach (var (id, teacher) in teachers)
        {
            var who = string.IsNullOrWhiteSpace(teacher.Name) ? $"Teacher id {id}" : $"{teacher.Name} (id {id})";

            foreach (var (day, windows) in teacher.Availability)
            {
                if (windows is null) continue;

                foreach (var window in windows)
                {
                    if (!TimeUtils.TryTimeToMinutes(window.Start, out var start))
                        throw new InvalidTeacherDataException(
                            $"{who} has a {day} availability window starting at \"{window.Start}\", which is not a time in HH:MM form. Correct it on the Edit Teachers page.");

                    if (!TimeUtils.TryTimeToMinutes(window.End, out var end))
                        throw new InvalidTeacherDataException(
                            $"{who} has a {day} availability window ending at \"{window.End}\", which is not a time in HH:MM form. Correct it on the Edit Teachers page.");

                    if (end <= start)
                        throw new InvalidTeacherDataException(
                            $"{who} has a {day} availability window from \"{window.Start}\" to \"{window.End}\", which ends no later than it starts. Correct it on the Edit Teachers page.");
                }
            }
        }
    }

    private static AttemptResult? RunAttempt(
        int seed,
        List<string> days,
        Dictionary<string, List<string>> periods,
        List<string> sections,
        List<TimeSlot> timeSlots,
        int periodsPerSubject,
        Dictionary<long, Teacher> teachers,
        CancellationToken cancellationToken)
    {
        var rng = new Random(seed);
        var logger = new GenerationLogger();
        var routine = SlotGenerator.InitializeRoutine(days, timeSlots, sections);
        var assignments = AssignmentManager.GetAllAssignments(periods, periodsPerSubject, logger, rng);

        var sectionSubjectTeachers = new Dictionary<(string Section, string Subject), long>();
        var teacherSectionSubjects = new Dictionary<long, Dictionary<string, HashSet<string>>>();
        var steps = 0;

        var found = Backtrack(
            assignments, 0, days, timeSlots, teachers, routine,
            sectionSubjectTeachers, teacherSectionSubjects, periods, periodsPerSubject,
            logger, rng, cancellationToken, ref steps);

        if (cancellationToken.IsCancellationRequested) return null;

        return found ? new AttemptResult(routine, logger, steps) : null;
    }

    private static bool Backtrack(
        List<SectionSubjectAssignment> assignments, int index,
        List<string> days, List<TimeSlot> timeSlots,
        Dictionary<long, Teacher> teachers, RoutineGrid routine,
        Dictionary<(string Section, string Subject), long> sectionSubjectTeachers,
        Dictionary<long, Dictionary<string, HashSet<string>>> teacherSectionSubjects,
        Dictionary<string, List<string>> periods, int periodsPerSubject,
        GenerationLogger logger, Random rng, CancellationToken cancellationToken, ref int steps)
    {
        if (cancellationToken.IsCancellationRequested) return false;

        steps++;

        if (index >= assignments.Count)
        {
            return ScheduleConstraints.ValidateRoutine(routine, periods, periodsPerSubject);
        }

        var assignment = assignments[index];
        var section = assignment.Section;
        var subject = assignment.Subject;

        var lockedTeacherId = TeacherLocking.GetLockedTeacher(sectionSubjectTeachers, section, subject);
        var isFirstAssignment = TeacherLocking.IsFirstAssignmentForSectionSubject(routine, days, timeSlots, section, subject);

        foreach (var day in days)
        {
            for (var slotIndex = 0; slotIndex < timeSlots.Count; slotIndex++)
            {
                if (cancellationToken.IsCancellationRequested) return false;

                var timeSlot = timeSlots[slotIndex];

                if (routine[day][slotIndex][section] is not null) continue;
                if (ScheduleConstraints.IsSubjectAlreadyScheduledToday(routine, day, section, subject)) continue;

                Teacher? teacher = null;

                if (lockedTeacherId is not null)
                {
                    // Looked up by row id, which is the key of this dictionary. Searching by name
                    // was both a linear scan of every teacher at every node and wrong when two
                    // rows shared a name: it returned whichever came first, so availability was
                    // checked against a different person from the one the lock was taken on.
                    if (teachers.TryGetValue(lockedTeacherId.Value, out var teacherObj) &&
                        TeacherManager.IsTeacherAvailableAtSlot(teacherObj, day, timeSlot, routine, slotIndex))
                    {
                        teacher = teacherObj;
                    }
                    else
                    {
                        continue;
                    }
                }
                else
                {
                    teacher = TeacherManager.FindAvailableTeacherWithSectionConstraint(
                        teachers, teacherSectionSubjects, routine, subject, section, day, timeSlot, slotIndex, logger, rng);
                }

                if (teacher is null) continue;

                SlotManager.AssignTeacherToSlot(routine, day, slotIndex, section, subject, teacher);

                var shouldLock = isFirstAssignment && lockedTeacherId is null;
                if (shouldLock)
                {
                    TeacherLocking.LockTeacherToSectionSubject(
                        sectionSubjectTeachers, teacherSectionSubjects, logger, section, subject, teacher);
                }

                if (Backtrack(
                        assignments, index + 1, days, timeSlots, teachers, routine,
                        sectionSubjectTeachers, teacherSectionSubjects, periods, periodsPerSubject,
                        logger, rng, cancellationToken, ref steps))
                {
                    return true;
                }

                SlotManager.RemoveTeacherFromSlot(routine, day, slotIndex, section);

                if (shouldLock)
                {
                    TeacherLocking.UnlockTeacherFromSectionSubject(sectionSubjectTeachers, teacherSectionSubjects, logger, section, subject);
                }
            }
        }

        return false;
    }

    private sealed record AttemptResult(RoutineGrid Routine, GenerationLogger Logger, int BacktrackingSteps);
}
