using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace RoutinePlanner.Web.Tests;

/// <summary>
/// Reads RoutinePlanner.Web.csproj directly and asserts on its structure. These tests exist
/// specifically to lock in the fix for the "routine not generating from the same sqlite db file"
/// regression: an earlier version of this project shipped campus.sqlite3/routine.sqlite3 as
/// plain CopyToOutputDirectory="PreserveNewest" Content items, which silently overwrote live,
/// already-mutated databases with the pristine seed copy on every rebuild (see
/// PreserveNewestCopySemanticsTests for why). If someone "fixes" a future missing-database-file
/// complaint by reverting to that pattern, these tests catch it before it ships again.
/// </summary>
public class DatabaseBuildConfigurationTests
{
    private static string GetCsprojPath([CallerFilePath] string thisFile = "")
    {
        // .../tests/RoutinePlanner.Web.Tests/DatabaseBuildConfigurationTests.cs
        //   -> .../src/RoutinePlanner.Web/RoutinePlanner.Web.csproj
        var testProjectDir = Path.GetDirectoryName(thisFile)!;         // .../tests/RoutinePlanner.Web.Tests
        var testsDir = Path.GetDirectoryName(testProjectDir)!;         // .../tests
        var repoRoot = Path.GetDirectoryName(testsDir)!;                // repo root
        return Path.Combine(repoRoot, "src", "RoutinePlanner.Web", "RoutinePlanner.Web.csproj");
    }

    private static XDocument LoadCsproj() => XDocument.Load(GetCsprojPath());

    [Fact]
    public void Sqlite_Database_Files_Are_Not_Blindly_Copied_With_PreserveNewest()
    {
        var doc = LoadCsproj();

        var offendingItems = doc.Descendants()
            .Where(e => e.Name.LocalName is "Content" or "None")
            .Where(e => (string?)e.Attribute("Include") is { } include && include.Contains(".sqlite3"))
            .Where(e => (string?)e.Attribute("CopyToOutputDirectory") == "PreserveNewest"
                     || (string?)e.Attribute("CopyToPublishDirectory") == "PreserveNewest")
            .ToList();

        Assert.True(offendingItems.Count == 0,
            "A *.sqlite3 item is set to CopyToOutputDirectory/CopyToPublishDirectory=PreserveNewest. " +
            "This unconditionally overwrites live databases (teacher edits, generated routines) with " +
            "the pristine seed copy on every rebuild, based on file timestamp rather than content. " +
            "Use the seed-if-missing targets instead (SeedDatabaseFilesOnBuild / SeedDatabaseFilesOnPublish).");
    }

    [Fact]
    public void Sqlite_Database_Files_Are_Seeded_Only_When_Missing_On_Build_And_Publish()
    {
        var doc = LoadCsproj();
        var targets = doc.Descendants(doc.Root!.Name.Namespace + "Target").ToList();

        AssertHasSeedIfMissingTarget(targets, "Build");
        AssertHasSeedIfMissingTarget(targets, "Publish");
    }

    private static void AssertHasSeedIfMissingTarget(List<XElement> targets, string afterTarget)
    {
        var match = targets.FirstOrDefault(t =>
            ((string?)t.Attribute("AfterTargets"))?
                .Split(';', StringSplitOptions.TrimEntries)
                .Contains(afterTarget) == true);

        Assert.True(match is not null, $"Expected a Target with AfterTargets=\"{afterTarget}\" that seeds the sqlite files.");

        var copy = match!.Descendants(match.Name.Namespace + "Copy").FirstOrDefault();
        Assert.True(copy is not null, $"Target after {afterTarget} should contain a <Copy> step.");

        var condition = (string?)copy!.Attribute("Condition") ?? "";
        Assert.Contains("!Exists(", condition);
    }

    [Fact]
    public void Static_Reference_Content_Still_Gets_Refreshed_Normally()
    {
        // courses.json / courses.pdf / view are read-only reference/static assets — safe to keep
        // copying fresh on every build, unlike the mutable sqlite files above.
        var doc = LoadCsproj();

        foreach (var include in new[] { "wwwroot\\**", "App_Data\\courses.json", "App_Data\\courses.pdf" })
        {
            var item = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Content" && (string?)e.Attribute("Include") == include);

            Assert.True(item is not null, $"Expected a Content Include for \"{include}\".");
            Assert.Equal("PreserveNewest", (string?)item!.Attribute("CopyToOutputDirectory"));
        }
    }
}
