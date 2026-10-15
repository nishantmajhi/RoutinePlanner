using RoutinePlanner.Web.Data;
using RoutinePlanner.Web.Models;
using RoutinePlanner.Web.Tests.TestHelpers;
using Xunit;

namespace RoutinePlanner.Web.Tests;

/// <summary>
/// Regression coverage for the "routine isn't generating from the same sqlite db file" class of
/// bug: every test here writes through one TeacherRepository *instance* and reads back through a
/// brand-new one, constructed from scratch with the same IWebHostEnvironment/IConfiguration —
/// exactly what happens across two separate HTTP requests in the real app (each request gets its
/// own repository resolved from DI). If path resolution ever drifted between instances, or a
/// write ever landed somewhere a fresh read couldn't see, these are the tests that catch it.
/// </summary>
public class TeacherRepositoryTests : IDisposable
{
    private readonly TempAppDirectory _app = new();

    private TeacherRepository NewRepository() => new(_app.BuildConfiguration(), _app.Environment);

    [Fact]
    public void Add_Then_GetById_OnANewRepositoryInstance_SeesTheSameTeacher()
    {
        var id = NewRepository().Add(new AddTeacherRequest
        {
            Salutation = "Mr.",
            Name = "Alan Turing",
            SubjectsArray = new List<string> { "CSC101" },
            AvailabilityJson = new Dictionary<string, List<AvailabilityWindow>>
            {
                ["Monday"] = new() { new AvailabilityWindow { Start = "08:00", End = "12:00" } },
            },
        });

        // Simulate a completely separate request: fresh repository, same content root.
        var teacher = NewRepository().GetById(id);

        Assert.NotNull(teacher);
        Assert.Equal("Alan Turing", teacher!.FullName);
        Assert.Equal(new List<string> { "CSC101" }, teacher.SubjectsArray);
    }

    [Fact]
    public void Add_Then_LoadTeachersForEngine_OnANewRepositoryInstance_IncludesTheTeacher()
    {
        NewRepository().Add(new AddTeacherRequest
        {
            Name = "Ada Lovelace",
            SubjectsArray = new List<string> { "MTH101" },
            AvailabilityJson = new Dictionary<string, List<AvailabilityWindow>>
            {
                ["Monday"] = new() { new AvailabilityWindow { Start = "08:00", End = "12:00" } },
            },
        });

        // This is exactly the call RoutinePlannerService.GenerateAsync makes at the start of
        // every generation run — it must see teachers added in a prior, separate request.
        var engineView = NewRepository().LoadTeachersForEngine();

        Assert.Contains(engineView.Values, t => t.Name == "Ada Lovelace" && t.Subjects.Contains("MTH101"));
    }

    [Fact]
    public void Update_Persists_And_Is_Visible_From_A_New_Repository_Instance()
    {
        var id = NewRepository().Add(new AddTeacherRequest { Name = "Original Name" });

        var updated = NewRepository().Update(id, new Dictionary<string, object?> { ["name"] = "Updated Name" });
        Assert.True(updated);

        var teacher = NewRepository().GetById(id);
        Assert.Equal("Updated Name", teacher!.FullName);
    }

    [Fact]
    public void Remove_Persists_And_Is_Visible_From_A_New_Repository_Instance()
    {
        var id = NewRepository().Add(new AddTeacherRequest { Name = "Temporary" });

        var removed = NewRepository().Remove(id);
        Assert.True(removed);

        var teacher = NewRepository().GetById(id);
        Assert.Null(teacher);
    }

    [Fact]
    public void GetById_ForAnUnknownId_ReturnsNull_RatherThanThrowing()
    {
        Assert.Null(NewRepository().GetById(999_999));
    }

    public void Dispose() => _app.Dispose();
}
