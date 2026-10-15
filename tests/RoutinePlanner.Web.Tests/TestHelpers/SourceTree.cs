using System.Runtime.CompilerServices;

namespace RoutinePlanner.Web.Tests.TestHelpers;

/// <summary>
/// Locates the repository on disk from this file's own compile-time path, so tests can read the
/// real csproj, the real wwwroot and the real seed data without depending on the working directory
/// the test runner happens to have chosen.
/// </summary>
public static class SourceTree
{
    /// <summary>Repository root — the folder holding src/, tests/ and README.md.</summary>
    public static string Root { get; } = ResolveRoot();

    public static string WebProjectDirectory => Path.Combine(Root, "src", "RoutinePlanner.Web");

    public static string WebProjectFile => Path.Combine(WebProjectDirectory, "RoutinePlanner.Web.csproj");

    private static string ResolveRoot([CallerFilePath] string thisFile = "")
    {
        // .../tests/RoutinePlanner.Web.Tests/TestHelpers/SourceTree.cs
        var helpersDir = Path.GetDirectoryName(thisFile)!;          // .../TestHelpers
        var testProjectDir = Path.GetDirectoryName(helpersDir)!;    // .../RoutinePlanner.Web.Tests
        var testsDir = Path.GetDirectoryName(testProjectDir)!;      // .../tests
        return Path.GetDirectoryName(testsDir)!;                    // repository root
    }
}
