using RoutinePlanner.Web.Services.Scheduling;
using Xunit;

namespace RoutinePlanner.Web.Tests;

/// <summary>
/// The day's periods. This is the one piece of arithmetic the feasibility check and the solver both
/// depend on, and they used to compute it separately and disagree — so these pin down what it
/// produces, and RoutineRequestValidatorFeasibilityTests asserts the validator now asks this.
/// </summary>
public class SlotGeneratorTests
{
    private static string[] Slots(string start, string breakStart, string breakEnd, string end, int duration) =>
        SlotGenerator.GenerateTimeSlots(start, breakStart, breakEnd, end, duration)
            .Select(s => $"{s.Start}-{s.End}")
            .ToArray();

    [Fact]
    public void Periods_fill_the_morning_and_the_afternoon_separately()
    {
        Assert.Equal(
            new[] { "08:00-09:00", "09:00-10:00", "10:30-11:30", "11:30-12:30" },
            Slots("08:00", "10:00", "10:30", "12:30", 60));
    }

    /// <summary>
    /// A period may not straddle the break: the one that would is dropped and the next starts when
    /// the break ends. So each half of the day loses its own remainder, and the total is *not*
    /// (minutes before + minutes after) / duration. Dividing that way gave 3 where the day has 2.
    /// </summary>
    [Fact]
    public void A_period_that_would_straddle_the_break_is_dropped_rather_than_shortened()
    {
        var slots = Slots("09:00", "10:10", "11:00", "12:10", 40);

        Assert.Equal(new[] { "09:00-09:40", "11:00-11:40" }, slots);

        // The sum the old feasibility check made, for contrast: 70 + 70 minutes over 40 = 3.
        Assert.NotEqual(3, slots.Length);
        Assert.Equal(2, slots.Length);
    }

    [Fact]
    public void A_period_that_would_run_past_the_end_of_the_day_is_dropped()
    {
        Assert.Equal(new[] { "08:00-09:00", "10:00-11:00" }, Slots("08:00", "09:30", "10:00", "11:45", 60));
    }

    [Fact]
    public void A_day_with_no_room_for_a_single_period_produces_none()
    {
        Assert.Empty(Slots("08:00", "08:40", "09:00", "09:40", 60));
    }

    [Fact]
    public void Periods_shorter_than_the_gaps_divide_them_exactly()
    {
        Assert.Equal(
            new[] { "08:00-08:30", "08:30-09:00", "09:30-10:00", "10:00-10:30" },
            Slots("08:00", "09:00", "09:30", "10:30", 30));
    }

    /// <summary>
    /// A non-positive duration never advanced the clock, so the loop appended zero-length periods
    /// until the process ran out of memory. It ran for as long as it was left alone.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void A_non_positive_period_duration_is_refused_rather_than_looping(int duration)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => SlotGenerator.GenerateTimeSlots("08:00", "10:00", "10:30", "12:30", duration));

        Assert.Equal("periodDuration", ex.ParamName);
    }

    [Fact]
    public void The_grid_is_initialised_with_a_null_cell_for_every_day_period_and_section()
    {
        var timeSlots = SlotGenerator.GenerateTimeSlots("08:00", "10:00", "10:30", "12:30", 60);
        var days = new List<string> { "Monday", "Tuesday" };
        var sections = new List<string> { "1A", "1B", "1C" };

        var grid = SlotGenerator.InitializeRoutine(days, timeSlots, sections);

        Assert.Equal(2, grid.Count);
        foreach (var day in days)
        {
            Assert.Equal(timeSlots.Count, grid[day].Count);
            foreach (var slot in grid[day])
            {
                Assert.Equal(sections.Count, slot.Count);
                Assert.All(sections, section => Assert.Null(slot[section]));
            }
        }
    }
}
