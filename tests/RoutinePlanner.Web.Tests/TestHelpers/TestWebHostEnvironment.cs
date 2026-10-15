using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace RoutinePlanner.Web.Tests.TestHelpers;

/// <summary>
/// Minimal stand-in for the real hosting environment. The one thing that matters for these
/// tests is ContentRootPath — every repository/service under test resolves its database and
/// view paths as Path.Combine(ContentRootPath, configuredRelativePath), so pointing this at a
/// throwaway temp directory gives each test its own isolated, disposable "app folder" without
/// spinning up a real ASP.NET Core host.
/// </summary>
public class TestWebHostEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "RoutinePlanner.Web.Tests";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = "";
    public string EnvironmentName { get; set; } = "Development";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = "";
}
