using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RoutinePlanner.Web.Models;
using RoutinePlanner.Web.Tests.TestHelpers;
using Xunit;

namespace RoutinePlanner.Web.Tests;

/// <summary>
/// The teacher CRUD endpoints over HTTP. PUT /teacher/update reads the raw JSON body so it can tell
/// an omitted field from an explicit null, and every one of those reads used to call a JsonElement
/// accessor without checking the element's kind first — which throws, outside the method's try
/// block, so a body of the wrong shape left as an unhandled exception rather than a 400.
/// </summary>
public class TeacherEndpointTests : IAsyncLifetime
{
    private RoutinePlannerTestApp _app = null!;
    private HttpClient _client = null!;

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

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private async Task<(HttpStatusCode Status, string Error)> Send(HttpMethod method, string path, string body)
    {
        using var request = new HttpRequestMessage(method, path) { Content = Json(body) };
        using var response = await _client.SendAsync(request);

        var payload = await response.Content.ReadAsStringAsync();
        string error;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            error = doc.RootElement.TryGetProperty("error", out var el) ? el.GetString() ?? "" : "";
        }
        catch (JsonException) { error = payload; }

        return (response.StatusCode, error);
    }

    private const string ValidTeacher = """
    {
        "salutation": "Dr.",
        "name": "A. Example",
        "subjects_array": ["CSC538"],
        "availability_json": { "Monday": [{ "start": "09:00", "end": "17:00" }] }
    }
    """;

    [Fact]
    public async Task A_well_formed_teacher_is_added_and_comes_back_from_the_list()
    {
        var (status, _) = await Send(HttpMethod.Post, "/teacher/add", ValidTeacher);
        Assert.Equal(HttpStatusCode.OK, status);

        using var list = await _client.GetAsync("/teachers");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var teachers = await list.Content.ReadFromJsonAsync<Dictionary<string, TeacherDto>>();
        var only = Assert.Single(teachers!);
        Assert.Equal("A. Example", only.Value.FullName);
        Assert.Equal("Dr.", only.Value.Salutation);
        Assert.Equal(new[] { "CSC538" }, only.Value.SubjectsArray);
    }

    /// <summary>
    /// The request that introduces a bad availability window is the request that fails. Before, it
    /// was accepted and every later /generate — for any section — reported an internal error.
    /// </summary>
    [Fact]
    public async Task Adding_a_teacher_with_an_unusable_availability_window_is_refused()
    {
        var (status, error) = await Send(HttpMethod.Post, "/teacher/add", """
        {
            "name": "A. Example",
            "subjects_array": ["CSC538"],
            "availability_json": { "Monday": [{ "start": "9", "end": "17:00" }] }
        }
        """);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("\"9\"", error);

        // And nothing was stored.
        using var list = await _client.GetAsync("/teachers");
        var teachers = await list.Content.ReadFromJsonAsync<Dictionary<string, TeacherDto>>();
        Assert.Empty(teachers!);
    }

    [Fact]
    public async Task Adding_a_teacher_with_no_name_is_refused()
    {
        var (status, error) = await Send(HttpMethod.Post, "/teacher/add", """{ "subjects_array": ["CSC538"] }""");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("name is required", error);
    }

    [Fact]
    public async Task Updating_a_single_field_leaves_the_others_alone()
    {
        await Send(HttpMethod.Post, "/teacher/add", ValidTeacher);

        var (status, _) = await Send(HttpMethod.Put, "/teacher/update", """
        { "teacherID": 1, "salutation": "Prof." }
        """);
        Assert.Equal(HttpStatusCode.OK, status);

        using var fetched = await _client.GetAsync("/teacher/get/1");
        var teacher = await fetched.Content.ReadFromJsonAsync<TeacherDto>();

        Assert.Equal("Prof.", teacher!.Salutation);
        Assert.Equal("A. Example", teacher.FullName);
        Assert.Equal(new[] { "CSC538" }, teacher.SubjectsArray);
    }

    /// <summary>An explicit null still clears the column — that distinction is why the body is read
    /// as raw JSON rather than bound to a class.</summary>
    [Fact]
    public async Task An_explicit_null_salutation_clears_it_while_an_omitted_one_does_not()
    {
        await Send(HttpMethod.Post, "/teacher/add", ValidTeacher);

        await Send(HttpMethod.Put, "/teacher/update", """{ "teacherID": 1, "salutation": null }""");

        using var fetched = await _client.GetAsync("/teacher/get/1");
        var teacher = await fetched.Content.ReadFromJsonAsync<TeacherDto>();
        Assert.Null(teacher!.Salutation);
    }

    [Theory]
    [InlineData("""{ "teacherID": "1", "name": "X" }""", "teacherID")]
    [InlineData("""{ "teacherID": true, "name": "X" }""", "teacherID")]
    [InlineData("""{ "name": "X" }""", "teacherID")]
    public async Task A_teacherID_that_is_not_a_number_is_a_400_not_an_unhandled_exception(string body, string expected)
    {
        var (status, error) = await Send(HttpMethod.Put, "/teacher/update", body);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(expected, error);
    }

    [Theory]
    [InlineData("""{ "teacherID": 1, "salutation": 42 }""", "salutation")]
    [InlineData("""{ "teacherID": 1, "name": 42 }""", "name")]
    [InlineData("""{ "teacherID": 1, "name": "" }""", "name")]
    [InlineData("""{ "teacherID": 1, "name": null }""", "name")]
    [InlineData("""{ "teacherID": 1, "subjects_array": "CSC538" }""", "subjects_array")]
    [InlineData("""{ "teacherID": 1, "subjects_array": [1,2] }""", "subjects_array")]
    [InlineData("""{ "teacherID": 1, "availability_json": [] }""", "availability_json")]
    [InlineData("""{ "teacherID": 1, "availability_json": { "Monday": "all day" } }""", "availability_json")]
    public async Task A_field_of_the_wrong_json_type_is_a_400_naming_the_field(string body, string expected)
    {
        await Send(HttpMethod.Post, "/teacher/add", ValidTeacher);

        var (status, error) = await Send(HttpMethod.Put, "/teacher/update", body);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(expected, error);
    }

    [Fact]
    public async Task An_update_with_an_unusable_availability_window_is_refused_and_changes_nothing()
    {
        await Send(HttpMethod.Post, "/teacher/add", ValidTeacher);

        var (status, error) = await Send(HttpMethod.Put, "/teacher/update", """
        { "teacherID": 1, "availability_json": { "Monday": [{ "start": "09:00", "end": "08:00" }] } }
        """);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("ends no later than it starts", error);

        using var fetched = await _client.GetAsync("/teacher/get/1");
        var teacher = await fetched.Content.ReadFromJsonAsync<TeacherDto>();
        Assert.Equal("17:00", teacher!.AvailabilityJson!["Monday"][0].End);
    }

    [Fact]
    public async Task An_update_with_no_fields_is_refused()
    {
        var (status, error) = await Send(HttpMethod.Put, "/teacher/update", """{ "teacherID": 1 }""");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("No fields provided", error);
    }

    [Fact]
    public async Task A_malformed_body_is_refused()
    {
        var (status, error) = await Send(HttpMethod.Put, "/teacher/update", "not json at all");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("Invalid JSON", error);
    }

    [Fact]
    public async Task Updating_or_removing_an_unknown_teacher_is_a_404()
    {
        var (updateStatus, _) = await Send(HttpMethod.Put, "/teacher/update", """{ "teacherID": 999, "name": "X" }""");
        Assert.Equal(HttpStatusCode.NotFound, updateStatus);

        using var remove = await _client.DeleteAsync("/teacher/remove/999");
        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);

        using var get = await _client.GetAsync("/teacher/get/999");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task A_teacher_can_be_removed()
    {
        await Send(HttpMethod.Post, "/teacher/add", ValidTeacher);

        using var remove = await _client.DeleteAsync("/teacher/remove/1");
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);

        using var list = await _client.GetAsync("/teachers");
        var teachers = await list.Content.ReadFromJsonAsync<Dictionary<string, TeacherDto>>();
        Assert.Empty(teachers!);
    }

    /// <summary>courses.json is served through the app rather than read off disk by the page.</summary>
    [Fact]
    public async Task The_course_list_is_served_as_json()
    {
        using var response = await _client.GetAsync("/courses");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var courses = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, courses.ValueKind);
        Assert.True(courses.GetArrayLength() > 0);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/addTeacher")]
    [InlineData("/editTeachers")]
    [InlineData("/preferences")]
    public async Task Every_page_is_served(string path)
    {
        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }
}
