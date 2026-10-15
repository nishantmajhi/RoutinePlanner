using Xunit;

namespace RoutinePlanner.Web.Tests;

/// <summary>
/// Reproduces, in pure C#, the exact MSBuild "CopyToOutputDirectory/CopyToPublishDirectory
/// =PreserveNewest" rule that caused the regression: copy source -> destination whenever the
/// source file's LastWriteTimeUtc is newer than the destination's (or the destination doesn't
/// exist yet). This isn't testing MSBuild itself — it's a small, always-runnable reproduction of
/// *why* the old csproj configuration was unsafe for a live, mutable database file, using the
/// same comparison MSBuild performs, plus a demonstration that the "seed only if missing"
/// approach used in RoutinePlanner.Web.csproj avoids it.
/// </summary>
public class PreserveNewestCopySemanticsTests
{
    private static void PreserveNewestCopy(string source, string destination)
    {
        if (!File.Exists(destination) || File.GetLastWriteTimeUtc(source) > File.GetLastWriteTimeUtc(destination))
        {
            File.Copy(source, destination, overwrite: true);
        }
    }

    private static void SeedIfMissingCopy(string source, string destination)
    {
        if (!File.Exists(destination))
        {
            File.Copy(source, destination);
        }
    }

    [Fact]
    public void PreserveNewestCopy_Overwrites_A_Mutated_Destination_When_Source_Timestamp_Moves_Forward()
    {
        // This demonstrates the exact failure mode reported: a live/mutated "output" database
        // gets silently clobbered by the pristine "source" copy after the source file's
        // timestamp is bumped forward — which is exactly what `git clone`/unzip does to every
        // tracked file, and exactly what plain `CopyToOutputDirectory="PreserveNewest"` did to
        // campus.sqlite3 / routine.sqlite3 on every rebuild.
        var dir = Directory.CreateTempSubdirectory("PreserveNewestDemo_");
        try
        {
            var source = Path.Combine(dir.FullName, "seed.sqlite3");
            var destination = Path.Combine(dir.FullName, "live.sqlite3");

            File.WriteAllText(source, "seed-data");
            PreserveNewestCopy(source, destination); // first build: seeds the output copy
            File.WriteAllText(destination, "live-data-the-app-generated-at-runtime");

            // Simulate `git clone`/unzip resetting the seed file's mtime to "now", after the
            // destination was last written.
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddSeconds(5));

            PreserveNewestCopy(source, destination); // a plain rebuild

            Assert.Equal("seed-data", File.ReadAllText(destination));
            // ^ the live data is gone. This is the bug. The fix never calls a PreserveNewest-style
            // copy for these files at all — see SeedIfMissingCopy below, and the
            // SeedDatabaseFilesOnBuild/SeedDatabaseFilesOnPublish targets in the csproj.
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void SeedIfMissingCopy_Never_Touches_An_Existing_Destination()
    {
        var dir = Directory.CreateTempSubdirectory("SeedIfMissingDemo_");
        try
        {
            var source = Path.Combine(dir.FullName, "seed.sqlite3");
            var destination = Path.Combine(dir.FullName, "live.sqlite3");

            File.WriteAllText(source, "seed-data");
            SeedIfMissingCopy(source, destination); // first build: seeds the output copy
            File.WriteAllText(destination, "live-data-the-app-generated-at-runtime");
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddSeconds(5));

            SeedIfMissingCopy(source, destination); // a plain rebuild, matching the csproj targets

            Assert.Equal("live-data-the-app-generated-at-runtime", File.ReadAllText(destination));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
