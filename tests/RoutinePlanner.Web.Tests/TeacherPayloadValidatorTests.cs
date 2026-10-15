using RoutinePlanner.Web.Models;
using RoutinePlanner.Web.Services;
using Xunit;

namespace RoutinePlanner.Web.Tests;

/// <summary>
/// What POST /teacher/add and PUT /teacher/update now check before writing. Neither looked at
/// subjects or availability at all, so a window of "9" instead of "09:00" was stored happily and
/// then broke every subsequent generation — for every section, whether or not that teacher was a
/// candidate — with a message that named neither the teacher nor the field.
/// </summary>
public class TeacherPayloadValidatorTests
{
    private static Dictionary<string, List<AvailabilityWindow>> Availability(string day, string start, string end) =>
        new() { [day] = new List<AvailabilityWindow> { new() { Start = start, End = end } } };

    [Fact]
    public void A_well_formed_availability_is_accepted()
    {
        Assert.Null(TeacherPayloadValidator.ValidateAvailability(Availability("Monday", "10:30", "13:30")));
    }

    [Fact]
    public void Several_windows_in_one_day_are_accepted()
    {
        var availability = new Dictionary<string, List<AvailabilityWindow>>
        {
            ["Sunday"] = new()
            {
                new AvailabilityWindow { Start = "10:30", End = "13:30" },
                new AvailabilityWindow { Start = "14:00", End = "17:00" },
            },
        };

        Assert.Null(TeacherPayloadValidator.ValidateAvailability(availability));
    }

    [Fact]
    public void An_absent_availability_is_accepted_because_the_field_is_optional()
    {
        Assert.Null(TeacherPayloadValidator.ValidateAvailability(null));
        Assert.Null(TeacherPayloadValidator.ValidateAvailability(new Dictionary<string, List<AvailabilityWindow>>()));
    }

    [Theory]
    [InlineData("9")]
    [InlineData("")]
    [InlineData("nine")]
    [InlineData("24:00")]
    [InlineData("10:70")]
    public void A_start_that_is_not_a_time_is_refused_and_quoted_back(string start)
    {
        var error = TeacherPayloadValidator.ValidateAvailability(Availability("Monday", start, "17:00"));

        Assert.NotNull(error);
        Assert.Contains("Monday", error!);
        Assert.Contains($"\"{start}\"", error);
    }

    [Fact]
    public void An_end_that_is_not_a_time_is_refused_and_quoted_back()
    {
        var error = TeacherPayloadValidator.ValidateAvailability(Availability("Friday", "09:00", "5pm"));

        Assert.NotNull(error);
        Assert.Contains("Friday", error!);
        Assert.Contains("\"5pm\"", error);
    }

    /// <summary>
    /// A window that ends when or before it starts can never contain a period, so a teacher with one
    /// silently becomes unavailable that day rather than being told their entry is wrong.
    /// </summary>
    [Theory]
    [InlineData("13:00", "13:00")]
    [InlineData("17:00", "09:00")]
    public void A_window_that_ends_no_later_than_it_starts_is_refused(string start, string end)
    {
        var error = TeacherPayloadValidator.ValidateAvailability(Availability("Tuesday", start, end));

        Assert.NotNull(error);
        Assert.Contains("ends no later than it starts", error!);
    }

    [Theory]
    [InlineData("Mon")]
    [InlineData("monday")]
    [InlineData("Someday")]
    public void An_entry_that_is_not_a_day_of_the_week_is_refused(string day)
    {
        var error = TeacherPayloadValidator.ValidateAvailability(Availability(day, "09:00", "17:00"));

        Assert.NotNull(error);
        Assert.Contains($"\"{day}\"", error!);
    }

    [Fact]
    public void A_well_formed_subject_list_is_accepted()
    {
        Assert.Null(TeacherPayloadValidator.ValidateSubjects(new List<string> { "CSC538", "CSC539" }));
        Assert.Null(TeacherPayloadValidator.ValidateSubjects(new List<string>()));
        Assert.Null(TeacherPayloadValidator.ValidateSubjects(null));
    }

    [Fact]
    public void An_empty_subject_code_is_refused()
    {
        Assert.NotNull(TeacherPayloadValidator.ValidateSubjects(new List<string> { "CSC538", "   " }));
        Assert.NotNull(TeacherPayloadValidator.ValidateSubjects(new List<string> { "" }));
    }

    /// <summary>
    /// A duplicated code does no harm to the teacher row itself, but the engine tests subject
    /// membership with Contains, so the duplicate is dead weight that misrepresents what the teacher
    /// covers. Rejecting it keeps the stored list meaning what it looks like it means.
    /// </summary>
    [Fact]
    public void A_duplicated_subject_code_is_refused_and_named()
    {
        var error = TeacherPayloadValidator.ValidateSubjects(new List<string> { "CSC538", "CSC539", "CSC538" });

        Assert.NotNull(error);
        Assert.Contains("CSC538", error!);
    }
}
