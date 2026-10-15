using System.Text.Json.Serialization;

namespace RoutinePlanner.Web.Models;

/// <summary>A single available time window on a given day.</summary>
public class AvailabilityWindow
{
    [JsonPropertyName("start")]
    public string Start { get; set; } = "";

    [JsonPropertyName("end")]
    public string End { get; set; } = "";
}

/// <summary>
/// Internal representation used by the scheduling engine.
/// Mirrors model/Loader.js's shape: { id, salutation, name, subjects, availability }.
/// </summary>
public class Teacher
{
    public long Id { get; set; }
    public string? Salutation { get; set; }
    public string Name { get; set; } = "";
    public List<string> Subjects { get; set; } = new();

    /// <summary>Day name (e.g. "Monday") -> list of available windows.</summary>
    public Dictionary<string, List<AvailabilityWindow>> Availability { get; set; } = new();

    public bool WorksOn(string day) =>
        Availability.TryGetValue(day, out var windows) && windows.Count > 0;
}

/// <summary>
/// Row shape returned by GET /teachers and GET /teacher/get/{id},
/// matching routes/getAllTeachersData.js and routes/teacherCRUD.js exactly.
/// </summary>
public class TeacherDto
{
    [JsonPropertyName("teacherID")]
    public long TeacherID { get; set; }

    [JsonPropertyName("salutation")]
    public string? Salutation { get; set; }

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("subjects_array")]
    public List<string>? SubjectsArray { get; set; }

    [JsonPropertyName("availability_json")]
    public Dictionary<string, List<AvailabilityWindow>>? AvailabilityJson { get; set; }
}

/// <summary>
/// Body for POST /teacher/add. Field names match wwwroot/add/script.js's payload exactly
/// (salutation, name, subjects_array, availability_json) so the frontend needs no changes.
/// </summary>
public class AddTeacherRequest
{
    [JsonPropertyName("salutation")]
    public string? Salutation { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("subjects_array")]
    public List<string>? SubjectsArray { get; set; }

    [JsonPropertyName("availability_json")]
    public Dictionary<string, List<AvailabilityWindow>>? AvailabilityJson { get; set; }
}

/// <summary>
/// Partial update for a teacher. Note: PUT /teacher/update is parsed from the raw JSON body
/// (see TeachersController) rather than bound to this class directly, because we need to
/// distinguish "field omitted" from "field explicitly null" exactly like the `!== undefined`
/// checks in routes/teacherCRUD.js. This class documents the shape only.
/// </summary>
public class UpdateTeacherRequest
{
    public long? TeacherID { get; set; }
    public string? Salutation { get; set; }
    public string? Name { get; set; }
    public List<string>? SubjectsArray { get; set; }
    public Dictionary<string, List<AvailabilityWindow>>? AvailabilityJson { get; set; }
}
