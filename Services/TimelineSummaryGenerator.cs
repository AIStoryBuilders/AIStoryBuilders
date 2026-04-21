using System.Text;
using AIStoryBuilders.Models;

namespace AIStoryBuilders.Services;

public interface ITimelineSummaryGenerator
{
    string GenerateSummary(TimelineContextDto context);
}

/// <summary>
/// Converts a <see cref="TimelineContextDto"/> into a deterministic, word-capped
/// prose summary suitable for injection into an LLM prompt.
/// </summary>
public class TimelineSummaryGenerator : ITimelineSummaryGenerator
{
    private const int MaxWords = 800;

    public string GenerateSummary(TimelineContextDto context)
    {
        if (context == null || string.IsNullOrWhiteSpace(context.TimelineName))
            return "";

        var sb = new StringBuilder();

        // Header
        var dateRange = FormatDateRange(context.StartDate, context.EndDate);
        sb.AppendLine($"Timeline: {context.TimelineName}{dateRange}");
        if (!string.IsNullOrWhiteSpace(context.TimelineDescription))
            sb.AppendLine(context.TimelineDescription);
        sb.AppendLine();

        // Characters
        if (context.Characters.Count > 0)
        {
            sb.AppendLine("Characters active in this timeline:");
            foreach (var c in context.Characters)
            {
                var attrs = string.Join("; ", c.Attributes.Where(a => !string.IsNullOrWhiteSpace(a)));
                var rolePart = string.IsNullOrWhiteSpace(c.Role) ? "" : $" ({c.Role})";
                var attrPart = string.IsNullOrWhiteSpace(attrs) ? "" : $": {attrs}";
                sb.AppendLine($"- {c.Name}{rolePart}{attrPart}");
            }
            sb.AppendLine();
        }

        // Locations
        if (context.Locations.Count > 0)
        {
            sb.AppendLine("Locations in this timeline:");
            foreach (var loc in context.Locations)
            {
                var desc = string.IsNullOrWhiteSpace(loc.Description)
                    ? "" : $": {loc.Description}";
                sb.AppendLine($"- {loc.Name}{desc}");
            }
            sb.AppendLine();
        }

        // Events (chronological; truncate oldest first if over budget)
        if (context.Events.Count > 0)
        {
            sb.AppendLine("Events (chronological):");
            var eventLines = context.Events.Select(e =>
            {
                var chars = string.Join(", ", e.Characters);
                var loc = string.IsNullOrWhiteSpace(e.Location) ? "" : $" at {e.Location}";
                return $"- {e.Chapter}, P{e.ParagraphIndex}: {chars}{loc}";
            }).ToList();

            var currentWords = WordCount(sb.ToString());
            var kept = new List<string>();
            foreach (var line in Enumerable.Reverse(eventLines))
            {
                if (currentWords + WordCount(line) > MaxWords && kept.Count > 0)
                {
                    var omitted = eventLines.Count - kept.Count;
                    kept.Insert(0, $"- ... and {omitted} earlier events");
                    break;
                }
                kept.Insert(0, line);
                currentWords += WordCount(line);
            }
            foreach (var line in kept)
                sb.AppendLine(line);
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatDateRange(DateTime? start, DateTime? end)
    {
        if (start == null && end == null) return "";
        var s = start?.ToString("yyyy-MM-dd") ?? "?";
        var e = end?.ToString("yyyy-MM-dd") ?? "?";
        return $" ({s} to {e})";
    }

    private static int WordCount(string text)
        => text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
}
