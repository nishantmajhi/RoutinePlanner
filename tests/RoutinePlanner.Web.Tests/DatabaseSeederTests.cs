using System.Text.Json;
using RoutinePlanner.Web.Data;
using RoutinePlanner.Web.Models;
using RoutinePlanner.Web.Services;
using RoutinePlanner.Web.Tests.TestHelpers;
using Xunit;

namespace RoutinePlanner.Web.Tests;

/// <summary>
/// The seed path that replaced the committed campus.sqlite3. The important property is the refusal:
/// the database it seeds is the same one the running app writes to, so it must not add to a table
/// that already has rows unless told twice. This archive has a seeder that overwrote real client
/// records twice by accident.
/// </summary>
public class DatabaseSeederTests : IDisposable
{
    private readonly TempAppDirectory _app = new();
    private readonly StringWriter _output = new();

    public void Dispose()
    {
        _app.Dispose();
        _output.Dispose();
    }

    private TeacherRepository Repository() => new(_app.BuildConfiguration(), _app.Environment);

    private int Seed(bool force = false) => DatabaseSeeder.Run(
        Repository(), _app.Environment, _app.BuildConfiguration(), force, _output);

    private void WriteSeedFile(string json)
    {
        var path = Path.Combine(_app.RootPath, DatabaseSeeder.DefaultSeedPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private const string TwoTeachers = """
    {
      "teachers": [
        {
          "salutation": "Dr.", "name": "One Example", "subjects_array": ["CSC538"],
          "availability_json": { "Monday": [{ "start": "10:30", "end": "13:30" }] }
        },
        {
          "salutation": "Mr.", "name": "Two Example", "subjects_array": ["CSC539","CSC540"],
          "availability_json": { "Monday": [{ "start": "14:00", "end": "17:00" }] }
        }
      ]
    }
    """;

    [Fact]
    public void An_empty_database_is_filled_from_the_seed_file()
    {
        WriteSeedFile(TwoTeachers);

        Assert.Equal(0, Seed());

        var teachers = Repository().GetAllAsDto();
        Assert.Equal(2, teachers.Count);

        var one = teachers.Values.Single(t => t.FullName == "One Example");
        Assert.Equal("Dr.", one.Salutation);
        Assert.Equal(new[] { "CSC538" }, one.SubjectsArray);
        Assert.Equal("10:30", one.AvailabilityJson!["Monday"][0].Start);
    }

    /// <summary>The refusal. This is the property that matters most about this whole file.</summary>
    [Fact]
    public void A_database_that_already_has_rows_is_left_alone()
    {
        WriteSeedFile(TwoTeachers);
        Repository().Add(new AddTeacherRequest { Name = "Somebody Real" });

        Assert.Equal(2, Seed());

        var teachers = Repository().GetAllAsDto();
        Assert.Single(teachers);
        Assert.Equal("Somebody Real", teachers.Values.Single().FullName);
        Assert.Contains("Refusing", _output.ToString());
        Assert.Contains("1 row", _output.ToString());
    }

    [Fact]
    public void Forcing_it_adds_to_a_database_that_already_has_rows()
    {
        WriteSeedFile(TwoTeachers);
        Repository().Add(new AddTeacherRequest { Name = "Somebody Real" });

        Assert.Equal(0, Seed(force: true));

        Assert.Equal(3, Repository().GetAllAsDto().Count);
    }

    [Fact]
    public void A_missing_seed_file_is_reported_rather_than_throwing()
    {
        Assert.Equal(1, Seed());
        Assert.Contains("No seed file", _output.ToString());
    }

    [Fact]
    public void A_seed_file_that_is_not_valid_json_is_reported()
    {
        WriteSeedFile("{ not json");

        Assert.Equal(1, Seed());
        Assert.Contains("not valid JSON", _output.ToString());
        Assert.Empty(Repository().GetAllAsDto());
    }

    [Theory]
    [InlineData("""{ "teachers": [] }""")]
    [InlineData("""{ }""")]
    public void A_seed_file_with_no_teachers_is_reported(string json)
    {
        WriteSeedFile(json);

        Assert.Equal(1, Seed());
        Assert.Contains("no teachers", _output.ToString());
    }

    /// <summary>
    /// The seed goes through the same checks as POST /teacher/add, so it cannot introduce the sort of
    /// malformed availability window that used to make every later generation fail — and it writes
    /// nothing at all if any entry is bad, rather than half-seeding and stopping.
    /// </summary>
    [Fact]
    public void A_seed_file_with_an_unusable_availability_window_writes_nothing()
    {
        WriteSeedFile("""
        {
          "teachers": [
            { "name": "Fine Example", "subjects_array": ["CSC538"],
              "availability_json": { "Monday": [{ "start": "09:00", "end": "17:00" }] } },
            { "name": "Broken Example", "subjects_array": ["CSC539"],
              "availability_json": { "Monday": [{ "start": "9", "end": "17:00" }] } }
          ]
        }
        """);

        Assert.Equal(1, Seed());
        Assert.Contains("Broken Example", _output.ToString());
        Assert.Contains("\"9\"", _output.ToString());
        Assert.Empty(Repository().GetAllAsDto());
    }

    [Fact]
    public void A_seed_file_with_a_nameless_teacher_writes_nothing()
    {
        WriteSeedFile("""{ "teachers": [ { "subjects_array": ["CSC538"] } ] }""");

        Assert.Equal(1, Seed());
        Assert.Contains("no name", _output.ToString());
        Assert.Empty(Repository().GetAllAsDto());
    }

    /// <summary>
    /// The seed file the repository actually ships. It must parse, pass the same validation, and
    /// contain nobody real — so the names are checked against the ten academics who used to be in
    /// the committed campus.sqlite3.
    /// </summary>
    [Fact]
    public void The_shipped_seed_file_is_valid_and_loads()
    {
        var shipped = Path.Combine(SourceTree.WebProjectDirectory, "App_Data", "seed", "teachers.seed.json");
        Assert.True(File.Exists(shipped), $"the shipped seed file is missing from {shipped}");

        File.Copy(shipped, EnsureSeedPath(), overwrite: true);

        Assert.Equal(0, Seed());

        var teachers = Repository().GetAllAsDto();
        Assert.True(teachers.Count >= 8, $"only {teachers.Count} demo teachers; a timetable needs a few");

        // Every one of them covers at least one real subject code, or the seeded app cannot generate.
        var courses = File.ReadAllText(Path.Combine(SourceTree.WebProjectDirectory, "App_Data", "courses.json"));
        foreach (var teacher in teachers.Values)
        {
            Assert.NotEmpty(teacher.SubjectsArray!);
            Assert.All(teacher.SubjectsArray!, code => Assert.Contains($"\"{code}\"", courses));
        }

        // None of the real people whose details used to be committed.
        string[] realNames =
        {
            "Jagdish Bhatta", "Subarna Shakya", "Sarbin Sayami", "Nawaraj Paudel", "Bikash Balami",
            "Dhundi Raj Pathak", "Bal Krishna Subedi", "Arjun Singh Saud", "Dhiraj Kedar Pandey",
            "Binod Kumar Adhikari",
        };

        var seededNames = teachers.Values.Select(t => t.FullName).ToList();
        foreach (var realName in realNames)
        {
            Assert.DoesNotContain(realName, seededNames);
        }
    }

    /// <summary>A seeded database really does produce a timetable, which is the point of shipping one.</summary>
    [Fact]
    public async Task A_seeded_database_can_generate_a_routine()
    {
        File.Copy(
            Path.Combine(SourceTree.WebProjectDirectory, "App_Data", "seed", "teachers.seed.json"),
            EnsureSeedPath(), overwrite: true);

        Assert.Equal(0, Seed());

        var config = _app.BuildConfiguration();
        var planner = new RoutinePlanner.Web.Services.Scheduling.RoutinePlannerService(
            Repository(), _app.Environment, config,
            Microsoft.Extensions.Options.Options.Create(
                new RoutinePlanner.Web.Services.Scheduling.RoutinePlannerOptions
                {
                    ParallelAttempts = 1,
                    GenerationTimeoutSeconds = 20,
                    Seed = 4242,
                }));

        // Three subjects, twice each, over four days, inside the 10:30-13:30 window the demo
        // teachers all share.
        var routine = await planner.GenerateAsync(JsonSerializer.Deserialize<GenerateRequest>("""
        { "days": ["Sunday","Monday","Tuesday","Wednesday"],
          "start_time": "10:30", "break_start": "12:00", "break_end": "12:30", "end_time": "13:30",
          "periods": { "1A": ["CSC538","CSC539","CSC540"] },
          "period_duration": 30, "periods_per_subject": 2 }
        """)!, CancellationToken.None);

        Assert.NotNull(routine);
    }

    private string EnsureSeedPath()
    {
        var path = Path.Combine(_app.RootPath, DatabaseSeeder.DefaultSeedPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
}
