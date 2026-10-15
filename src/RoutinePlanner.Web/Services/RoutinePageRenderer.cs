using System.Text.Json;
using RoutinePlanner.Web.Models;

namespace RoutinePlanner.Web.Services;

/// <summary>
/// Composes the routine page: the static shell from wwwroot/routine/index.html with the routine
/// itself handed to the page's script as a JSON literal.
///
/// Pulled out of RoutineController so it can be tested without a filesystem or an HTTP context,
/// and because the composition is where the interesting mistake was. It used
/// Regex.Replace(html, "</html>", script + "</html>", count: 1), which reads as a plain string
/// substitution but is not one: Regex.Replace processes $ sequences in the *replacement*, and the
/// replacement here contains serialized user data. Section names come straight off the /generate
/// request body and teacher names out of the database, so a section named "$_" — .NET's
/// "the entire input string" — embedded the whole HTML document inside the script literal. The real
/// template contains a &lt;/script&gt; tag, so that terminated the script early and spilled markup
/// into the page; a name containing "$$" silently arrived in the browser as a single "$".
/// </summary>
public static class RoutinePageRenderer
{
    private const string IdPlaceholder = "{{ID}}";
    private const string ClosingHtmlTag = "</html>";

    public static string Render(string htmlTemplate, long id, FormattedRoutine routine)
    {
        var html = htmlTemplate.Replace(IdPlaceholder, id.ToString());

        // Default JsonSerializerOptions use JavaScriptEncoder.Default, which escapes '<', '>' and
        // '&' as <, > and &. That is what stops a teacher or section name containing
        // "</script>" from closing the block, and it is why this needs no escaping of its own.
        var routineJson = JsonSerializer.Serialize(routine);
        var routineScript = $"<script>const routine = {routineJson};</script>\n";

        // Plain index-and-insert. Equivalent to the single Regex.Replace it replaces for every
        // input except the ones the regex engine would have reinterpreted.
        var insertAt = html.IndexOf(ClosingHtmlTag, StringComparison.OrdinalIgnoreCase);

        return insertAt < 0
            ? html + routineScript
            : html[..insertAt] + routineScript + html[insertAt..];
    }
}
