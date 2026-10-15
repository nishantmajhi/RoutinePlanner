using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RoutinePlanner.Web.Data;
using RoutinePlanner.Web.Models;
using RoutinePlanner.Web.Services;
using RoutinePlanner.Web.Services.Scheduling;

namespace RoutinePlanner.Web.Controllers;

/// <summary>Port of routes/generate.js and routes/routine.js.</summary>
[ApiController]
public class RoutineController : ControllerBase
{
    private readonly RoutinePlannerService _planner;
    private readonly RoutineRepository _routineRepository;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<RoutineController> _logger;

    public RoutineController(
        RoutinePlannerService planner,
        RoutineRepository routineRepository,
        IWebHostEnvironment env,
        IConfiguration config,
        ILogger<RoutineController> logger)
    {
        _planner = planner;
        _routineRepository = routineRepository;
        _env = env;
        _config = config;
        _logger = logger;
    }

    /// <summary>POST /generate — rate limited to 5 requests/minute per client, like express-rate-limit.</summary>
    [HttpPost("/generate")]
    [EnableRateLimiting("generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateRequest req, CancellationToken cancellationToken)
    {
        try
        {
            var validationError = RoutineRequestValidator.ValidateRoutineRequest(req);
            if (validationError is not null)
            {
                return BadRequest(new { error = validationError });
            }

            var timeError = RoutineRequestValidator.ValidateTimeSequence(req.StartTime!, req.BreakStart!, req.BreakEnd!, req.EndTime!);
            if (timeError is not null)
            {
                return BadRequest(new { error = timeError });
            }

            var feasibilityError = RoutineRequestValidator.ValidateScheduleFeasibility(req);
            if (feasibilityError is not null)
            {
                return BadRequest(new { error = feasibilityError });
            }

            _logger.LogInformation("Generating schedule...");

            FormattedRoutine? routine;
            try
            {
                routine = await _planner.GenerateAsync(req, cancellationToken);
            }
            catch (InvalidTeacherDataException ex)
            {
                // Stored teacher data the engine cannot work with. The client did not send it, but
                // the client is the one who can go and fix it, and the message names the row.
                _logger.LogWarning(ex, "Generation refused: unusable teacher data");
                return BadRequest(new { error = ex.Message });
            }

            if (routine is null)
            {
                return StatusCode(500, new
                {
                    error = "Could not generate a valid schedule with the given constraints. Please try reducing periods per subject or increasing available time slots.",
                });
            }

            _logger.LogInformation("Schedule generated successfully");

            long id;
            try
            {
                id = _routineRepository.Save(routine);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving routine");
                return StatusCode(500, new { error = "Internal database error occured while saving the routine." });
            }

            return Ok(new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in generate route");
            return StatusCode(500, new { error = "Internal server error occurred while generating the schedule." });
        }
    }

    /// <summary>GET /routine — renders the latest routine into wwwroot/routine/index.html.</summary>
    [HttpGet("/routine")]
    public async Task<IActionResult> GetLatestRoutine(CancellationToken cancellationToken)
    {
        try
        {
            var latestId = _routineRepository.GetLatestId();
            if (latestId is null)
            {
                return NotFound(new { error = "No routines found." });
            }

            var routine = _routineRepository.GetById(latestId.Value);
            if (routine is null)
            {
                return NotFound(new { error = "Routine not found." });
            }

            var html = await BuildRoutineHtmlAsync(latestId.Value, routine, cancellationToken);
            return Content(html, "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rendering latest routine");
            return StatusCode(500, new { error = "Internal server error." });
        }
    }

    /// <summary>GET /routine/{id} — renders a specific saved routine.</summary>
    [HttpGet("/routine/{id:long}")]
    public async Task<IActionResult> GetRoutineById(long id, CancellationToken cancellationToken)
    {
        try
        {
            var routine = _routineRepository.GetById(id);
            if (routine is null)
            {
                return NotFound(new { error = "Routine not found." });
            }

            var html = await BuildRoutineHtmlAsync(id, routine, cancellationToken);
            return Content(html, "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rendering routine {RoutineId}", id);
            return StatusCode(500, new { error = "Internal server error." });
        }
    }

    private async Task<string> BuildRoutineHtmlAsync(long id, FormattedRoutine routineData, CancellationToken cancellationToken)
    {
        var viewRoot = Path.Combine(_env.ContentRootPath, _config["RoutinePlanner:ViewRootPath"] ?? "wwwroot");
        var htmlPath = Path.Combine(viewRoot, "routine", "index.html");

        var template = await System.IO.File.ReadAllTextAsync(htmlPath, cancellationToken);

        return RoutinePageRenderer.Render(template, id, routineData);
    }
}
