using System.Text.Json;
using RoutinePlanner.Web.Models;
using Xunit;

namespace RoutinePlanner.Web.Tests;

/// <summary>
/// wwwroot/routine/fetchCourses.js reads the embedded routine JSON with lowercase-first keys
/// (s.timeSlot.start, s.timeSlot.end, classData.subject, classData.teacher). RoutineController
/// serializes FormattedRoutine with the plain JsonSerializer default options (no camelCase
/// policy), so every nested property needs its own [JsonPropertyName] — relying on the naming
/// policy alone silently emits PascalCase and breaks the routine page's rendering script.
/// </summary>
public class RoutineModelJsonCasingTests
{
    [Fact]
    public void TimeSlot_Serializes_With_Lowercase_Start_And_End_Keys()
    {
        var timeSlot = new TimeSlot { Start = "09:00", End = "10:00" };

        var json = JsonSerializer.Serialize(timeSlot);

        Assert.Contains("\"start\":\"09:00\"", json);
        Assert.Contains("\"end\":\"10:00\"", json);
        Assert.DoesNotContain("\"Start\"", json);
        Assert.DoesNotContain("\"End\"", json);
    }

    [Fact]
    public void SlotAssignment_Serializes_With_Lowercase_Subject_And_Teacher_Keys()
    {
        var assignment = new SlotAssignment { Subject = "MTH101", Teacher = "Dr. Rahman" };

        var json = JsonSerializer.Serialize(assignment);

        Assert.Contains("\"subject\":\"MTH101\"", json);
        Assert.Contains("\"teacher\":\"Dr. Rahman\"", json);
        Assert.DoesNotContain("\"Subject\"", json);
        Assert.DoesNotContain("\"Teacher\"", json);
    }

    [Fact]
    public void FormattedRoutine_Serializes_With_All_Nested_Keys_Lowercase_For_The_Frontend()
    {
        var routine = new FormattedRoutine
        {
            ["Monday"] = new List<FormattedSlot>
            {
                new()
                {
                    TimeSlot = new TimeSlot { Start = "09:00", End = "10:00" },
                    Classes = new Dictionary<string, SlotAssignment?>
                    {
                        ["1A"] = new SlotAssignment { Subject = "MTH101", Teacher = "Dr. Rahman" },
                    },
                },
            },
        };

        // Same call RoutineController.BuildRoutineHtmlAsync makes when embedding
        // `<script>const routine = ...</script>` into the routine page.
        var json = JsonSerializer.Serialize(routine);

        Assert.Contains("\"timeSlot\":{\"start\":\"09:00\",\"end\":\"10:00\"}", json);
        Assert.Contains("\"subject\":\"MTH101\",\"teacher\":\"Dr. Rahman\"", json);
    }
}
