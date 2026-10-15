using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoutinePlanner.Web.Models;

/// <summary>
/// Body for POST /generate. Property names match the frontend's snake_case JSON exactly
/// (see wwwroot/preferences/generateRoutine.js) via JsonPropertyName, so nothing in the
/// frontend has to change.
/// </summary>
public class GenerateRequest
{
    [JsonPropertyName("days")]
    public List<string>? Days { get; set; }

    [JsonPropertyName("start_time")]
    public string? StartTime { get; set; }

    [JsonPropertyName("break_start")]
    public string? BreakStart { get; set; }

    [JsonPropertyName("break_end")]
    public string? BreakEnd { get; set; }

    [JsonPropertyName("end_time")]
    public string? EndTime { get; set; }

    /// <summary>section -> list of subject codes, e.g. { "1A": ["CSC101","MTH102"] }</summary>
    [JsonPropertyName("periods")]
    public Dictionary<string, List<string>>? Periods { get; set; }

    [JsonPropertyName("period_duration")]
    public JsonElement? PeriodDuration { get; set; }

    [JsonPropertyName("periods_per_subject")]
    public JsonElement? PeriodsPerSubject { get; set; }
}

public class TimeSlot
{
    [JsonPropertyName("start")]
    public string Start { get; set; } = "";

    [JsonPropertyName("end")]
    public string End { get; set; } = "";
}

/// <summary>What a single section is assigned in a single slot: subject + teacher name.</summary>
public class SlotAssignment
{
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = "";

    [JsonPropertyName("teacher")]
    public string Teacher { get; set; } = "";
}

/// <summary>
/// A filled cell of the in-progress grid the solver backtracks over. It holds the whole
/// <see cref="Models.Teacher"/> rather than the teacher's name, because the name is a label and not
/// an identity: nothing stops two rows of the teachers table carrying the same name, and while the
/// grid recorded only the name the solver could not tell two such people apart. It checked one
/// person's availability while attributing the period to the other, counted one teacher's workload
/// against the other, and treated a teacher as busy in a slot because their namesake was teaching
/// in it. <see cref="Scheduling.RoutineFormatter"/> projects this down to a
/// <see cref="SlotAssignment"/>, so the JSON that reaches the browser is unchanged.
/// </summary>
public class GridAssignment
{
    public string Subject { get; set; } = "";
    public Teacher Teacher { get; set; } = new();
}

public class FormattedSlot
{
    [JsonPropertyName("timeSlot")]
    public TimeSlot TimeSlot { get; set; } = new();

    [JsonPropertyName("classes")]
    public Dictionary<string, SlotAssignment?> Classes { get; set; } = new();
}

public class SectionSubjectAssignment
{
    public string Section { get; set; } = "";
    public string Subject { get; set; } = "";
}

/// <summary>
/// day -> list of slots (indexed like timeSlots) -> section -> assignment (nullable).
/// Mirrors the plain nested-object `routine` structure used throughout the JS scheduling code
/// (routine[day][slotIndex][section]). This is the solver's working grid only; what the frontend
/// receives is the <see cref="FormattedRoutine"/> that RoutineFormatter builds from it.
/// </summary>
public class RoutineGrid : Dictionary<string, List<Dictionary<string, GridAssignment?>>> { }

/// <summary>Final shape written to routine.sqlite3 and returned by GET /routine[/{id}]: day -> ordered slots.</summary>
public class FormattedRoutine : Dictionary<string, List<FormattedSlot>> { }
