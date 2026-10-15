using Microsoft.AspNetCore.Mvc;
using RoutinePlanner.Web.Services;

namespace RoutinePlanner.Web.Controllers;

/// <summary>Port of routes/getAllCoursesData.js.</summary>
[ApiController]
public class CoursesController : ControllerBase
{
    private readonly CourseService _courseService;
    private readonly ILogger<CoursesController> _logger;

    public CoursesController(CourseService courseService, ILogger<CoursesController> logger)
    {
        _courseService = courseService;
        _logger = logger;
    }

    [HttpGet("/courses")]
    public async Task<IActionResult> GetCourses(CancellationToken cancellationToken)
    {
        try
        {
            var raw = await _courseService.ReadRawJsonAsync(cancellationToken);
            return Content(raw, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading courses.json");
            return StatusCode(500, new { error = "Failed to load courses" });
        }
    }
}
