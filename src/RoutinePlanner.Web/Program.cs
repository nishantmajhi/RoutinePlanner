using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;
using RoutinePlanner.Web.Data;
using RoutinePlanner.Web.Services;
using RoutinePlanner.Web.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

// ---- Services (DI) -------------------------------------------------------
builder.Services.AddControllers();

builder.Services.AddSingleton<TeacherRepository>();
builder.Services.AddSingleton<RoutineRepository>();
builder.Services.AddSingleton<CourseService>();
builder.Services.AddSingleton<RoutinePlannerService>();

builder.Services.Configure<RoutinePlannerOptions>(builder.Configuration.GetSection("RoutinePlanner"));

// ---- Rate limiting: equivalent of express-rate-limit on POST /generate ---
// (5 requests / minute per client, reject immediately — no queueing — same as the JS middleware.)
var generateLimits = builder.Configuration.GetSection("RoutinePlanner:GenerateRateLimit");
var permitLimit = generateLimits.GetValue("PermitLimit", 5);
var windowMinutes = generateLimits.GetValue("WindowMinutes", 1);

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("generate", limiterOptions =>
    {
        limiterOptions.PermitLimit = permitLimit;
        limiterOptions.Window = TimeSpan.FromMinutes(windowMinutes);
        limiterOptions.QueueLimit = 0;
    });

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Too many requests. Please try again later." }, cancellationToken);
    };
});

// ---- Hosting port: Render (and most PaaS hosts) assign a dynamic port via $PORT and expect
// the app to bind to it — appsettings.json's "Urls" is only a local-dev fallback (port 3645).
var renderPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(renderPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{renderPort}");
}

var app = builder.Build();

// ---- `dotnet run -- --seed` fills an empty teachers table from App_Data/seed/teachers.seed.json
// and exits. It replaces the campus.sqlite3 that used to be committed; see DatabaseSeeder.
if (args.Contains("--seed"))
{
    return DatabaseSeeder.Run(
        app.Services.GetRequiredService<TeacherRepository>(),
        app.Environment,
        app.Configuration,
        force: args.Contains("--force"),
        output: Console.Out);
}

// ---- Static frontend, served untouched from wwwroot/ (same as express.static(view)) ----
var viewRoot = Path.Combine(app.Environment.ContentRootPath, app.Configuration["RoutinePlanner:ViewRootPath"] ?? "wwwroot");
var viewFileProvider = new PhysicalFileProvider(viewRoot);

app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = viewFileProvider });
app.UseStaticFiles(new StaticFileOptions { FileProvider = viewFileProvider });

app.UseRateLimiter();

app.MapControllers();

app.Run();

return 0;

// Exposes the implicitly-generated Program type to the test project so its WebApplicationFactory
// can boot this exact pipeline in-process — same DI registrations, same rate limiter, same
// controllers. Top-level statements make Program internal by default.
public partial class Program { }
