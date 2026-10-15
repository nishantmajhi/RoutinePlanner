using RoutinePlanner.Web.Data;
using RoutinePlanner.Web.Models;
using RoutinePlanner.Web.Tests.TestHelpers;
using Xunit;

namespace RoutinePlanner.Web.Tests;

/// <summary>
/// TeacherRepository.Update builds its SET clause from the dictionary's keys. The values are
/// parameterised, but a column name cannot be, so it is interpolated — which means the keys are the
/// one part of that statement that has to be trusted, and nothing checked them. The controller
/// happens to pass four literals, so this was never reachable; it is checked now so that it stays
/// unreachable if a caller ever forwards a field name from a request body.
/// </summary>
public class TeacherRepositoryUpdateTests : IDisposable
{
    private readonly TempAppDirectory _app = new();

    public void Dispose() => _app.Dispose();

    private TeacherRepository Repository() => new(_app.BuildConfiguration(), _app.Environment);

    private long AddSomeone() => Repository().Add(new AddTeacherRequest
    {
        Salutation = "Dr.",
        Name = "A. Example",
        SubjectsArray = new List<string> { "CSC538" },
        AvailabilityJson = new Dictionary<string, List<AvailabilityWindow>>
        {
            ["Monday"] = new() { new AvailabilityWindow { Start = "09:00", End = "17:00" } },
        },
    });

    [Theory]
    [InlineData("salutation")]
    [InlineData("name")]
    [InlineData("subjects_array")]
    [InlineData("availability_json")]
    public void Each_of_the_four_real_columns_can_be_written(string column)
    {
        var id = AddSomeone();

        Assert.True(Repository().Update(id, new Dictionary<string, object?> { [column] = "x" }));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("Name")]                                // case matters; the column is lowercase
    [InlineData("name = 'x', salutation")]
    [InlineData("name'")]
    [InlineData("name = 'pwned' --")]
    [InlineData("nonexistent_column")]
    [InlineData("")]
    public void Anything_else_is_refused_before_a_statement_is_built(string column)
    {
        var id = AddSomeone();

        var ex = Assert.Throws<ArgumentException>(
            () => Repository().Update(id, new Dictionary<string, object?> { [column] = "x" }));

        Assert.Contains(column, ex.Message);
    }

    /// <summary>A refused key must not have written the acceptable ones alongside it either.</summary>
    [Fact]
    public void A_refused_column_leaves_the_row_untouched()
    {
        var id = AddSomeone();

        Assert.Throws<ArgumentException>(() => Repository().Update(id, new Dictionary<string, object?>
        {
            ["name"] = "Changed",
            ["id"] = 99,
        }));

        Assert.Equal("A. Example", Repository().GetById(id)!.FullName);
    }

    [Fact]
    public void An_empty_update_changes_nothing_and_reports_so()
    {
        var id = AddSomeone();

        Assert.False(Repository().Update(id, new Dictionary<string, object?>()));
        Assert.Equal("A. Example", Repository().GetById(id)!.FullName);
    }

    [Fact]
    public void Updating_an_unknown_id_reports_that_nothing_changed()
    {
        AddSomeone();

        Assert.False(Repository().Update(9999, new Dictionary<string, object?> { ["name"] = "Nobody" }));
    }

    /// <summary>Values are parameterised, so a value that looks like SQL is stored as text.</summary>
    [Fact]
    public void A_value_that_looks_like_sql_is_stored_verbatim()
    {
        var id = AddSomeone();
        const string awkward = "Robert'); DROP TABLE teachers; --";

        Assert.True(Repository().Update(id, new Dictionary<string, object?> { ["name"] = awkward }));

        Assert.Equal(awkward, Repository().GetById(id)!.FullName);
        Assert.Single(Repository().GetAllAsDto()); // the table is still there
    }

    [Fact]
    public void A_teacher_whose_columns_are_cleared_loads_with_empty_defaults_rather_than_nulls()
    {
        var id = AddSomeone();

        Repository().Update(id, new Dictionary<string, object?>
        {
            ["salutation"] = null,
            ["subjects_array"] = null,
            ["availability_json"] = null,
        });

        var loaded = Repository().LoadTeachersForEngine()[id];

        Assert.Null(loaded.Salutation);
        Assert.Empty(loaded.Subjects);
        Assert.Empty(loaded.Availability);
        Assert.False(loaded.WorksOn("Monday"));
    }
}
