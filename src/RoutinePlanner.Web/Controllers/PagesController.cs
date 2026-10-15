using Microsoft.AspNetCore.Mvc;

namespace RoutinePlanner.Web.Controllers;

/// <summary>
/// Serves the (untouched) frontend HTML shells at their original clean URLs.
/// Port of routes/addTeacher.js, routes/editTeachers.js, routes/preferences.js.
/// </summary>
[ApiController]
public class PagesController : ControllerBase
{
    private readonly string _viewRoot;

    public PagesController(IConfiguration config, IWebHostEnvironment env)
    {
        var relativePath = config["RoutinePlanner:ViewRootPath"] ?? "wwwroot";
        _viewRoot = Path.Combine(env.ContentRootPath, relativePath);
    }

    [HttpGet("/addTeacher")]
    public IActionResult AddTeacher() => PhysicalFile(Path.Combine(_viewRoot, "add", "index.html"), "text/html");

    [HttpGet("/editTeachers")]
    public IActionResult EditTeachers() => PhysicalFile(Path.Combine(_viewRoot, "edit", "index.html"), "text/html");

    [HttpGet("/preferences")]
    public IActionResult Preferences() => PhysicalFile(Path.Combine(_viewRoot, "preferences", "index.html"), "text/html");
}
