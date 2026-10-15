using System.Text.Json;
using RoutinePlanner.Web.Models;
using RoutinePlanner.Web.Services;
using RoutinePlanner.Web.Tests.TestHelpers;
using Xunit;

namespace RoutinePlanner.Web.Tests;

/// <summary>
/// The routine page is the static shell with the timetable handed to its script as a JSON literal.
/// Composing it used Regex.Replace(html, "&lt;/html&gt;", script + "&lt;/html&gt;", 1), which looks
/// like a string substitution but is not: Regex.Replace interprets $ sequences in the replacement,
/// and the replacement is where the serialized user data goes. Section names come off the /generate
/// body and teacher names out of the database, so both reach it.
/// </summary>
public class RoutinePageRendererTests
{
    private const string Template = """
        <!DOCTYPE html>
        <html lang="en">
          <head>
            <title>Routine - No. {{ID}}</title>
            <script src="/routine/script.js"></script>
          </head>
          <body><main></main></body>
        </html>
        """;

    private static FormattedRoutine RoutineWithSection(string section) => new()
    {
        ["Monday"] = new List<FormattedSlot>
        {
            new()
            {
                TimeSlot = new TimeSlot { Start = "09:00", End = "10:00" },
                Classes = new Dictionary<string, SlotAssignment?>
                {
                    [section] = new SlotAssignment { Subject = "CSC538", Teacher = "Someone" },
                },
            },
        },
    };

    private static FormattedRoutine RoutineWithTeacher(string teacher) => new()
    {
        ["Monday"] = new List<FormattedSlot>
        {
            new()
            {
                TimeSlot = new TimeSlot { Start = "09:00", End = "10:00" },
                Classes = new Dictionary<string, SlotAssignment?>
                {
                    ["1A"] = new SlotAssignment { Subject = "CSC538", Teacher = teacher },
                },
            },
        },
    };

    /// <summary>
    /// "$_" is .NET's substitution for the entire input string. A section of that name used to
    /// embed the whole HTML document inside the script literal — and because the real template
    /// contains a &lt;/script&gt; tag, that closed the script early and spilled the rest into the
    /// page as markup.
    /// </summary>
    [Fact]
    public void A_section_named_dollar_underscore_does_not_embed_the_document_in_itself()
    {
        var html = RoutinePageRenderer.Render(Template, 7, RoutineWithSection("$_"));

        Assert.Contains("\"$_\":", html);
        Assert.DoesNotContain("<!DOCTYPE html>\\u003C", html);

        // Exactly one DOCTYPE and one closing </html>: the document was not inlined into itself.
        Assert.Equal(1, CountOccurrences(html, "<!DOCTYPE html>"));
        Assert.Equal(1, CountOccurrences(html, "</html>"));

        // And the script block is still a single well-formed block.
        Assert.Equal(2, CountOccurrences(html, "</script>")); // the template's own, plus ours
    }

    /// <summary>"$$" is the escape for a literal "$", so a name containing it silently lost a
    /// character on its way to the browser.</summary>
    [Fact]
    public void A_teacher_whose_name_contains_two_dollar_signs_reaches_the_page_intact()
    {
        var html = RoutinePageRenderer.Render(Template, 1, RoutineWithTeacher("A$$B"));

        Assert.Contains("\"teacher\":\"A$$B\"", html);
        Assert.DoesNotContain("\"teacher\":\"A$B\"", html);
    }

    [Theory]
    [InlineData("$&")]
    [InlineData("$`")]
    [InlineData("$'")]
    [InlineData("$+")]
    [InlineData("$1")]
    [InlineData("${name}")]
    public void Every_other_dollar_substitution_form_also_survives(string section)
    {
        var html = RoutinePageRenderer.Render(Template, 1, RoutineWithSection(section));

        // Whatever JSON escaping does to the name, what lands in the page must round-trip back to
        // the name we put in.
        var embedded = ExtractRoutineLiteral(html);
        var parsed = JsonSerializer.Deserialize<FormattedRoutine>(embedded)!;
        Assert.True(parsed["Monday"][0].Classes.ContainsKey(section),
            $"section \"{section}\" did not survive: keys were {string.Join(", ", parsed["Monday"][0].Classes.Keys)}");
    }

    /// <summary>
    /// Not a defect, and worth a test so nobody "fixes" it: System.Text.Json's default encoder
    /// escapes '&lt;', '&gt;' and '&amp;', which is what stops a name containing a closing script
    /// tag from ending the block. The renderer relies on that rather than escaping again.
    /// </summary>
    [Fact]
    public void A_name_containing_a_closing_script_tag_cannot_end_the_script_block()
    {
        var html = RoutinePageRenderer.Render(Template, 1, RoutineWithTeacher("</script><img src=x>"));

        Assert.DoesNotContain("</script><img", html);
        Assert.Contains("\\u003C/script\\u003E", html);
        Assert.Equal(2, CountOccurrences(html, "</script>"));

        var parsed = JsonSerializer.Deserialize<FormattedRoutine>(ExtractRoutineLiteral(html))!;
        Assert.Equal("</script><img src=x>", parsed["Monday"][0].Classes["1A"]!.Teacher);
    }

    [Fact]
    public void The_id_placeholder_is_filled_in_and_the_script_goes_before_the_closing_tag()
    {
        var html = RoutinePageRenderer.Render(Template, 42, RoutineWithSection("1A"));

        Assert.Contains("Routine - No. 42", html);
        Assert.DoesNotContain("{{ID}}", html);
        Assert.EndsWith("</html>", html.TrimEnd());
        Assert.True(html.IndexOf("const routine =", StringComparison.Ordinal) < html.LastIndexOf("</html>", StringComparison.Ordinal));
    }

    /// <summary>A template with no closing tag used to silently lose the routine altogether:
    /// Regex.Replace found nothing to replace and returned the page unchanged, so the script that
    /// renders the timetable had no data and the page stayed blank.</summary>
    [Fact]
    public void A_template_without_a_closing_html_tag_still_gets_the_routine()
    {
        var html = RoutinePageRenderer.Render("<main></main>", 3, RoutineWithSection("1A"));

        Assert.Contains("const routine = {", html);
    }

    /// <summary>The routine page the app really ships, rendered with a real routine.</summary>
    [Fact]
    public void The_shipped_template_renders_with_the_script_inside_the_document()
    {
        var template = File.ReadAllText(Path.Combine(SourceTree.WebProjectDirectory, "wwwroot", "routine", "index.html"));

        var html = RoutinePageRenderer.Render(template, 12, RoutineWithSection("1A"));

        Assert.Contains("Routine - No. 12", html);
        Assert.Contains("const routine = {", html);
        Assert.True(html.IndexOf("const routine =", StringComparison.Ordinal) < html.LastIndexOf("</html>", StringComparison.Ordinal),
            "the routine literal must be inside the document, not after it");
    }

    private static string ExtractRoutineLiteral(string html)
    {
        const string prefix = "const routine = ";
        var start = html.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        var end = html.IndexOf(";</script>", start, StringComparison.Ordinal);
        return html[start..end];
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}
