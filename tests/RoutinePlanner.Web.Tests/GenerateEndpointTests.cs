using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RoutinePlanner.Web.Tests.TestHelpers;
using Xunit;

namespace RoutinePlanner.Web.Tests;

/// <summary>
/// POST /generate driven over HTTP against the real pipeline. These assert on the status code and
/// the error text a client actually receives, because that is where several of this project's
/// defects showed: a request the server could reject by arithmetic in microseconds was instead
/// answered with a blanket 500 after an exhaustive search, or with an unhandled exception.
///
/// wwwroot/preferences/generateRoutine.js renders response.error verbatim for any non-2xx reply,
/// so the message is part of the interface.
/// </summary>
public class GenerateEndpointTests : IAsyncLifetime
{
    private RoutinePlannerTestApp _app = null!;
    private HttpClient _client = null!;

    private static readonly string[] WeekDays = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday" };
    private static readonly string[] Subjects = { "CSC538", "CSC539", "CSC540" };

    public Task InitializeAsync()
    {
        _app = new RoutinePlannerTestApp();
        _client = _app.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _app.Dispose();
        return Task.CompletedTask;
    }

    private async Task<(HttpStatusCode Status, string Error)> PostGenerate(string bodyJson)
    {
        using var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync("/generate", content);

        var payload = await response.Content.ReadAsStringAsync();
        string error;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            error = doc.RootElement.TryGetProperty("error", out var el) ? el.GetString() ?? "" : "";
        }
        catch (JsonException)
        {
            error = payload;
        }

        return (response.StatusCode, error);
    }

    /// <summary>
    /// The feasibility check used to divide the day's two halves as one continuous run, so it
    /// believed three 40-minute periods fit between 09:00 and 12:10 with a 10:10-11:00 break. Only
    /// two do, because a period may not straddle the break. The request was accepted and answered
    /// with a 500 after the solver had searched a space with no solution in it.
    /// </summary>
    [Fact]
    public async Task A_day_whose_periods_do_not_fit_around_the_break_is_refused_with_the_real_slot_count()
    {
        var (status, error) = await PostGenerate("""
        { "days": ["Monday"], "start_time": "09:00", "break_start": "10:10",
          "break_end": "11:00", "end_time": "12:10",
          "periods": { "1A": ["CSC538","CSC539","CSC540"] },
          "period_duration": 40, "periods_per_subject": 1 }
        """);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("not feasible", error);
        Assert.Contains("2 periods/day", error);
    }

    /// <summary>
    /// A subject is scheduled at most once per section per day, so N periods of it need N distinct
    /// days. Counting total slots cannot see that: six periods across five days passed the check
    /// and then searched for over twenty seconds on one thread before being cut off.
    /// </summary>
    [Fact]
    public async Task More_periods_per_subject_than_there_are_days_is_refused_immediately()
    {
        var (status, error) = await PostGenerate("""
        { "days": ["Monday","Tuesday"], "start_time": "08:00", "break_start": "10:00",
          "break_end": "10:30", "end_time": "12:30",
          "periods": { "1A": ["CSC538"] },
          "period_duration": 60, "periods_per_subject": 3 }
        """);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("3 different days", error);
        Assert.Contains("only 2 days", error);
    }

    /// <summary>
    /// JsonElement.TryGetInt32 throws on a non-number rather than returning false, and the throw
    /// happened inside the controller's catch-all — so a mistyped field was reported as an internal
    /// server error instead of the 400 that names it.
    /// </summary>
    [Theory]
    [InlineData("\"60\"", "Period duration")]
    [InlineData("true", "Period duration")]
    [InlineData("[]", "Period duration")]
    [InlineData("{}", "Period duration")]
    public async Task A_period_duration_of_the_wrong_json_type_is_a_400_naming_the_field(string value, string expected)
    {
        var (status, error) = await PostGenerate($$"""
        { "days": ["Monday"], "start_time": "08:00", "break_start": "10:00",
          "break_end": "10:30", "end_time": "12:30", "periods": { "1A": ["CSC538"] },
          "period_duration": {{value}}, "periods_per_subject": 1 }
        """);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(expected, error);
    }

    [Theory]
    [InlineData("\"2\"")]
    [InlineData("false")]
    public async Task A_periods_per_subject_of_the_wrong_json_type_is_a_400_naming_the_field(string value)
    {
        var (status, error) = await PostGenerate($$"""
        { "days": ["Monday"], "start_time": "08:00", "break_start": "10:00",
          "break_end": "10:30", "end_time": "12:30", "periods": { "1A": ["CSC538"] },
          "period_duration": 60, "periods_per_subject": {{value}} }
        """);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("Periods per subject", error);
    }

    /// <summary>A null subject list threw a NullReferenceException out of the feasibility check.</summary>
    [Fact]
    public async Task A_section_with_a_null_subject_list_is_a_400_not_an_internal_error()
    {
        var (status, error) = await PostGenerate("""
        { "days": ["Monday"], "start_time": "08:00", "break_start": "10:00",
          "break_end": "10:30", "end_time": "12:30", "periods": { "1A": null },
          "period_duration": 60, "periods_per_subject": 1 }
        """);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("1A", error);
        Assert.Contains("no subjects", error);
    }

    /// <summary>
    /// An empty subject list was accepted, and the solver "succeeded" instantly with every slot
    /// blank. That empty timetable was saved as a routine and served as a page of dashes.
    /// </summary>
    [Fact]
    public async Task A_section_with_an_empty_subject_list_is_refused_rather_than_saved_as_a_blank_timetable()
    {
        var (status, error) = await PostGenerate("""
        { "days": ["Monday"], "start_time": "08:00", "break_start": "10:00",
          "break_end": "10:30", "end_time": "12:30", "periods": { "1A": [] },
          "period_duration": 60, "periods_per_subject": 1 }
        """);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("no subjects", error);

        // And nothing was written: GET /routine still has no routine to show.
        using var routineResponse = await _client.GetAsync("/routine");
        Assert.Equal(HttpStatusCode.NotFound, routineResponse.StatusCode);
    }

    /// <summary>
    /// The solver groups assignments by section and subject, so a code repeated in one section asks
    /// for its periods twice under one identity, and the completeness check then rejects every
    /// complete schedule the search builds.
    /// </summary>
    [Fact]
    public async Task A_subject_listed_twice_in_one_section_is_refused()
    {
        var (status, error) = await PostGenerate("""
        { "days": ["Monday","Tuesday"], "start_time": "08:00", "break_start": "10:00",
          "break_end": "10:30", "end_time": "12:30",
          "periods": { "1A": ["CSC538","CSC538"] },
          "period_duration": 60, "periods_per_subject": 1 }
        """);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("CSC538", error);
        Assert.Contains("more than once", error);
    }

    [Fact]
    public async Task A_day_listed_twice_is_refused()
    {
        var (status, error) = await PostGenerate("""
        { "days": ["Monday","Monday"], "start_time": "08:00", "break_start": "10:00",
          "break_end": "10:30", "end_time": "12:30", "periods": { "1A": ["CSC538"] },
          "period_duration": 60, "periods_per_subject": 1 }
        """);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("duplicates", error);
    }

    /// <summary>
    /// A stored availability window of "9" instead of "09:00" was parsed deep inside the search and
    /// surfaced as an unattributed internal server error — on every generation, for every section,
    /// whether or not that teacher was a candidate. It is now refused up front, by name.
    /// </summary>
    [Fact]
    public async Task An_unusable_stored_availability_window_is_reported_against_the_teacher_it_belongs_to()
    {
        _app.AddTeacher(new RoutinePlanner.Web.Models.AddTeacherRequest
        {
            Name = "Broken Row",
            SubjectsArray = new List<string> { "CSC538" },
            AvailabilityJson = new Dictionary<string, List<RoutinePlanner.Web.Models.AvailabilityWindow>>
            {
                ["Monday"] = new() { new RoutinePlanner.Web.Models.AvailabilityWindow { Start = "9", End = "17:00" } },
            },
        });

        var (status, error) = await PostGenerate("""
        { "days": ["Monday"], "start_time": "08:00", "break_start": "10:00",
          "break_end": "10:30", "end_time": "12:30", "periods": { "1A": ["CSC538"] },
          "period_duration": 60, "periods_per_subject": 1 }
        """);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("Broken Row", error);
        Assert.Contains("\"9\"", error);
    }

    /// <summary>The whole happy path over HTTP: teachers in the database, a request in, an id back,
    /// and the saved routine rendered as a page with the timetable embedded in it.</summary>
    [Fact]
    public async Task A_feasible_request_returns_an_id_whose_routine_page_contains_the_timetable()
    {
        for (var i = 0; i < 4; i++)
        {
            _app.AddTeacher(RoutinePlannerTestApp.AlwaysAvailable($"Teacher {i}", Subjects, WeekDays));
        }

        var body = JsonSerializer.Serialize(new
        {
            days = WeekDays,
            start_time = "08:00",
            break_start = "10:00",
            break_end = "10:30",
            end_time = "12:30",
            periods = new Dictionary<string, string[]> { ["1A"] = Subjects },
            period_duration = 60,
            periods_per_subject = 2,
        });

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync("/generate", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt64();
        Assert.True(id > 0);

        using var page = await _client.GetAsync($"/routine/{id}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("const routine = {", html);
        Assert.Contains($"Routine - No. {id}", html);
        Assert.DoesNotContain("{{ID}}", html);

        // The embedded literal is the shape the page's script reads.
        Assert.Contains("\"timeSlot\":{\"start\":", html);
        Assert.Contains("\"1A\":", html);
    }
}
