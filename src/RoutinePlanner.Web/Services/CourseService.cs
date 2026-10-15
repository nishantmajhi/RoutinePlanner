namespace RoutinePlanner.Web.Services;

/// <summary>Port of routes/getAllCoursesData.js. courses.json is served as raw JSON, untouched.</summary>
public class CourseService
{
    private readonly string _coursesJsonPath;

    public CourseService(IConfiguration config, IWebHostEnvironment env)
    {
        var relativePath = config["RoutinePlanner:CoursesJsonPath"] ?? "App_Data/courses.json";
        _coursesJsonPath = Path.Combine(env.ContentRootPath, relativePath);
    }

    public async Task<string> ReadRawJsonAsync(CancellationToken cancellationToken) =>
        await File.ReadAllTextAsync(_coursesJsonPath, cancellationToken);
}
