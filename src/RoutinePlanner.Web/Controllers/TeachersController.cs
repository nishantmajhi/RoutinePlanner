using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RoutinePlanner.Web.Data;
using RoutinePlanner.Web.Models;
using RoutinePlanner.Web.Services;

namespace RoutinePlanner.Web.Controllers;

/// <summary>Port of routes/getAllTeachersData.js and routes/teacherCRUD.js.</summary>
[ApiController]
public class TeachersController : ControllerBase
{
    private readonly TeacherRepository _repository;
    private readonly ILogger<TeachersController> _logger;

    public TeachersController(TeacherRepository repository, ILogger<TeachersController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>GET /teachers — dictionary keyed by id, matching routes/getAllTeachersData.js.</summary>
    [HttpGet("/teachers")]
    public IActionResult GetAllTeachers()
    {
        try
        {
            var teachers = _repository.GetAllAsDto();
            return Ok(teachers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching teachers");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>POST /teacher/add</summary>
    [HttpPost("/teacher/add")]
    public IActionResult AddTeacher([FromBody] AddTeacherRequest req)
    {
        if (string.IsNullOrEmpty(req.Name))
        {
            return BadRequest(new { error = "name is required" });
        }

        var payloadError = TeacherPayloadValidator.ValidateSubjects(req.SubjectsArray)
                           ?? TeacherPayloadValidator.ValidateAvailability(req.AvailabilityJson);
        if (payloadError is not null)
        {
            return BadRequest(new { error = payloadError });
        }

        try
        {
            var id = _repository.Add(req);
            return Ok(new { success = true, teacherID = id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding teacher");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>GET /teacher/get/{teacherID}</summary>
    [HttpGet("/teacher/get/{teacherID}")]
    public IActionResult GetTeacher(long teacherID)
    {
        try
        {
            var teacher = _repository.GetById(teacherID);
            if (teacher is null)
            {
                return NotFound(new { error = "Teacher not found" });
            }
            return Ok(teacher);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching teacher {TeacherId}", teacherID);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// PUT /teacher/update. Parses the raw JSON body (rather than binding to a POCO) so that
    /// omitted fields can be told apart from fields explicitly sent as null — exactly matching
    /// the `!== undefined` checks in routes/teacherCRUD.js's dynamic SET clause.
    /// </summary>
    [HttpPut("/teacher/update")]
    public async Task<IActionResult> UpdateTeacher()
    {
        JsonDocument body;
        try
        {
            body = await JsonDocument.ParseAsync(Request.Body);
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "Invalid JSON body" });
        }

        using (body)
        {
            var root = body.RootElement;

            // ValueKind is checked first for the same reason as the fields below: TryGetInt64
            // throws rather than returning false when the element is not a number, so a teacherID
            // sent as the string "5" escaped this method as an unhandled exception.
            if (!root.TryGetProperty("teacherID", out var teacherIdEl) ||
                teacherIdEl.ValueKind != JsonValueKind.Number ||
                !teacherIdEl.TryGetInt64(out var teacherId))
            {
                return BadRequest(new { error = "teacherID is required" });
            }

            var updates = new Dictionary<string, object?>();

            // Every read below checks ValueKind before converting. JsonElement's accessors throw
            // InvalidOperationException when the element is of another kind — GetString() on a
            // number, for instance — and these run outside the try block, so a body of the wrong
            // shape used to escape as an unhandled exception rather than a 400.
            if (root.TryGetProperty("salutation", out var salutationEl))
            {
                if (salutationEl.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    return BadRequest(new { error = "salutation must be a string or null" });

                updates["salutation"] = salutationEl.ValueKind == JsonValueKind.Null ? null : salutationEl.GetString();
            }

            if (root.TryGetProperty("name", out var nameEl))
            {
                if (nameEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(nameEl.GetString()))
                    return BadRequest(new { error = "name must be a non-empty string" });

                updates["name"] = nameEl.GetString();
            }

            if (root.TryGetProperty("subjects_array", out var subjectsEl))
            {
                if (subjectsEl.ValueKind == JsonValueKind.Null)
                {
                    updates["subjects_array"] = null;
                }
                else
                {
                    List<string>? subjects;
                    try
                    {
                        subjects = subjectsEl.Deserialize<List<string>>();
                    }
                    catch (JsonException)
                    {
                        return BadRequest(new { error = "subjects_array must be an array of subject codes" });
                    }

                    var subjectsError = TeacherPayloadValidator.ValidateSubjects(subjects);
                    if (subjectsError is not null) return BadRequest(new { error = subjectsError });

                    updates["subjects_array"] = JsonSerializer.Serialize(subjectsEl);
                }
            }

            if (root.TryGetProperty("availability_json", out var availabilityEl))
            {
                if (availabilityEl.ValueKind == JsonValueKind.Null)
                {
                    updates["availability_json"] = null;
                }
                else
                {
                    Dictionary<string, List<AvailabilityWindow>>? availability;
                    try
                    {
                        availability = availabilityEl.Deserialize<Dictionary<string, List<AvailabilityWindow>>>();
                    }
                    catch (JsonException)
                    {
                        return BadRequest(new { error = "availability_json must map day names to lists of start/end windows" });
                    }

                    var availabilityError = TeacherPayloadValidator.ValidateAvailability(availability);
                    if (availabilityError is not null) return BadRequest(new { error = availabilityError });

                    updates["availability_json"] = JsonSerializer.Serialize(availabilityEl);
                }
            }

            if (updates.Count == 0)
            {
                return BadRequest(new { error = "No fields provided to update" });
            }

            try
            {
                var changed = _repository.Update(teacherId, updates);
                if (!changed)
                {
                    return NotFound(new { error = "Teacher not found" });
                }
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating teacher {TeacherId}", teacherId);
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    /// <summary>DELETE /teacher/remove/{teacherID}</summary>
    [HttpDelete("/teacher/remove/{teacherID}")]
    public IActionResult RemoveTeacher(long teacherID)
    {
        try
        {
            var removed = _repository.Remove(teacherID);
            if (!removed)
            {
                return NotFound(new { error = "Teacher not found" });
            }
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing teacher {TeacherId}", teacherID);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
