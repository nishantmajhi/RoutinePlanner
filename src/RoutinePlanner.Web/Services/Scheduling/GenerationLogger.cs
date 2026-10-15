using System.Text;

namespace RoutinePlanner.Web.Services.Scheduling;

/// <summary>
/// Port of utils/Logger.js. One instance is created per generation *attempt*
/// (see RoutinePlannerService), so it needs no internal locking even though several
/// attempts run concurrently on separate threads — each thread owns its own logger.
/// </summary>
public class GenerationLogger
{
    private readonly List<string> _logBuffer = new();
    public string Timestamp { get; }

    public GenerationLogger()
    {
        // Matches the JS version's UTC+5:45 offset trick (Date.now() + 5.75h) used to timestamp Nepal-local log filenames.
        Timestamp = DateTimeOffset.UtcNow.AddHours(5.75).ToString("yyyy-MM-dd_HH-mm-ss");
    }

    public void AddLog(string message) => _logBuffer.Add(message);

    public void SaveLogToFile(string logsDirectory, string? fileName = null)
    {
        fileName ??= Timestamp;
        Directory.CreateDirectory(logsDirectory);
        var logFile = Path.Combine(logsDirectory, fileName);

        var sb = new StringBuilder();
        sb.AppendJoin('\n', _logBuffer);
        File.WriteAllText(logFile, sb.ToString(), Encoding.UTF8);
    }
}
