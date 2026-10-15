using RoutinePlanner.Web.Services.Scheduling;
using Xunit;

namespace RoutinePlanner.Web.Tests;

/// <summary>
/// Availability windows are stored as free text in a JSON column and were never checked, so this
/// parser was the first thing to see a malformed one — deep inside the solver, on a worker thread.
/// It split on ':' and indexed parts[1] unconditionally, so "9" was an IndexOutOfRangeException and
/// "aa:bb" a bare "input string was not in a correct format", neither of which said what the value
/// was or which teacher it belonged to.
/// </summary>
public class TimeUtilsTests
{
    [Theory]
    [InlineData("00:00", 0)]
    [InlineData("08:00", 480)]
    [InlineData("8:00", 480)]
    [InlineData("09:45", 585)]
    [InlineData("13:30", 810)]
    [InlineData("23:59", 1439)]
    public void A_time_of_day_parses_to_minutes_since_midnight(string time, int expected)
    {
        Assert.Equal(expected, TimeUtils.TimeToMinutes(time));
        Assert.True(TimeUtils.TryTimeToMinutes(time, out var viaTry));
        Assert.Equal(expected, viaTry);
    }

    [Theory]
    [InlineData("9")]            // no minutes at all — used to be IndexOutOfRangeException
    [InlineData("")]
    [InlineData(":")]
    [InlineData("09:")]
    [InlineData(":30")]
    [InlineData("aa:bb")]
    [InlineData("09:00:00")]
    [InlineData("24:00")]        // not a time of day
    [InlineData("09:60")]
    [InlineData("-1:00")]
    [InlineData(" 9:00")]
    [InlineData("09 00")]
    public void Anything_that_is_not_a_time_of_day_is_refused(string time)
    {
        Assert.False(TimeUtils.TryTimeToMinutes(time, out _));

        var ex = Assert.Throws<FormatException>(() => TimeUtils.TimeToMinutes(time));
        Assert.Contains($"\"{time}\"", ex.Message);
    }

    [Fact]
    public void A_null_time_is_refused_without_throwing_from_TryTimeToMinutes()
    {
        Assert.False(TimeUtils.TryTimeToMinutes(null, out _));
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(480, "08:00")]
    [InlineData(585, "09:45")]
    [InlineData(1439, "23:59")]
    public void Minutes_render_back_as_a_zero_padded_time(int minutes, string expected)
    {
        Assert.Equal(expected, TimeUtils.MinutesToTime(minutes));
    }

    [Fact]
    public void Every_minute_of_the_day_round_trips()
    {
        for (var minutes = 0; minutes < 24 * 60; minutes++)
        {
            Assert.Equal(minutes, TimeUtils.TimeToMinutes(TimeUtils.MinutesToTime(minutes)));
        }
    }
}
