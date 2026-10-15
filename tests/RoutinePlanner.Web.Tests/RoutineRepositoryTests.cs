using System.Text.Json;
using RoutinePlanner.Web.Data;
using RoutinePlanner.Web.Models;
using RoutinePlanner.Web.Tests.TestHelpers;
using Xunit;

namespace RoutinePlanner.Web.Tests;

/// <summary>
/// Mirrors TeacherRepositoryTests, but for the routine.sqlite3 side: POST /generate saves
/// through one repository instance, GET /routine reads through a different one built on a
/// separate request. These tests prove that round trip always lands on the same physical file.
/// </summary>
public class RoutineRepositoryTests : IDisposable
{
    private readonly TempAppDirectory _app = new();

    private RoutineRepository NewRepository() => new(_app.BuildConfiguration(), _app.Environment);

    private static FormattedRoutine SampleRoutine(string teacherName = "Ada Lovelace") => new()
    {
        ["Monday"] = new List<FormattedSlot>
        {
            new FormattedSlot
            {
                TimeSlot = new TimeSlot { Start = "08:00", End = "09:00" },
                Classes = new Dictionary<string, SlotAssignment?>
                {
                    ["1A"] = new SlotAssignment { Subject = "MTH101", Teacher = teacherName },
                },
            },
        },
    };

    [Fact]
    public void Save_Then_GetById_OnANewRepositoryInstance_ReturnsTheSameRoutine()
    {
        var routine = SampleRoutine();
        var id = NewRepository().Save(routine);

        var reloaded = NewRepository().GetById(id);

        Assert.NotNull(reloaded);
        Assert.Equal(JsonSerializer.Serialize(routine), JsonSerializer.Serialize(reloaded));
    }

    [Fact]
    public void GetLatestId_OnANewRepositoryInstance_ReturnsTheMostRecentlySavedRoutine()
    {
        NewRepository().Save(SampleRoutine("First Teacher"));
        var secondId = NewRepository().Save(SampleRoutine("Second Teacher"));

        var latestId = NewRepository().GetLatestId();

        Assert.Equal(secondId, latestId);
    }

    [Fact]
    public void GetLatestId_ReturnsNull_WhenNoRoutineHasEverBeenSaved()
    {
        Assert.Null(NewRepository().GetLatestId());
    }

    [Fact]
    public void GetById_ForAnUnknownId_ReturnsNull_RatherThanThrowing()
    {
        Assert.Null(NewRepository().GetById(999_999));
    }

    public void Dispose() => _app.Dispose();
}
