#nullable disable
using AIStoryBuildersV2.Shared.Models;

namespace AIStoryBuildersV2.Shared.Graph;
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
    private const string OmissionMarker = "- ... earlier timeline details omitted";
    private const string EventsHeader = "Events (chronological):";

    public string GenerateSummary(TimelineContextDto context)
    {
        if (context == null || string.IsNullOrWhiteSpace(context.TimelineName))
            return "";

        var dateRange = FormatDateRange(context.StartDate, context.EndDate);
        var baseLines = new List<string> { $"Timeline: {context.TimelineName}{dateRange}" };
        if (!string.IsNullOrWhiteSpace(context.TimelineDescription))
            baseLines.Add(context.TimelineDescription);
        baseLines.Add("");

        if (context.Characters.Count > 0)
        {
            baseLines.Add("Characters active in this timeline:");
            foreach (var c in context.Characters)
            {
                var attrs = string.Join("; ", c.Attributes.Where(a => !string.IsNullOrWhiteSpace(a)));
                var rolePart = string.IsNullOrWhiteSpace(c.Role) ? "" : $" ({c.Role})";
                var attrPart = string.IsNullOrWhiteSpace(attrs) ? "" : $": {attrs}";
                baseLines.Add($"- {c.Name}{rolePart}{attrPart}");
            }
            baseLines.Add("");
        }

        if (context.Locations.Count > 0)
        {
            baseLines.Add("Locations in this timeline:");
            foreach (var loc in context.Locations)
            {
                var desc = string.IsNullOrWhiteSpace(loc.Description)
                    ? "" : $": {loc.Description}";
                baseLines.Add($"- {loc.Name}{desc}");
            }
            baseLines.Add("");
        }

        var eventLines = context.Events.Select(e =>
        {
            var chars = string.Join(", ", e.Characters);
            var loc = string.IsNullOrWhiteSpace(e.Location) ? "" : $" at {e.Location}";
            return $"- {e.Chapter}, P{e.ParagraphIndex}: {chars}{loc}";
        }).ToList();
        var allLines = baseLines.Concat(eventLines.Count > 0
            ? new[] { EventsHeader }.Concat(eventLines)
            : Enumerable.Empty<string>()).ToList();
        if (WordCount(string.Join("\n", allLines)) <= MaxWords)
            return string.Join("\n", allLines).TrimEnd();

        var output = new List<string>();
        var words = 0;
        var omitted = false;
        var contentLimit = MaxWords - WordCount(OmissionMarker);
        foreach (var line in baseLines)
            AppendWithinBudget(output, line, ref words, contentLimit, ref omitted);

        // Reserve the header first, then keep the most recent events that fit, newest backwards with no gaps.
        var keptEvents = new List<string>();
        var eventBudget = contentLimit - WordCount(EventsHeader);
        foreach (var line in Enumerable.Reverse(eventLines))
        {
            if (words + WordCount(line) > eventBudget) break;
            keptEvents.Add(line);
            words += WordCount(line);
        }

        if (keptEvents.Count > 0)
        {
            output.Add(EventsHeader);
            output.AddRange(keptEvents.AsEnumerable().Reverse());
            words += WordCount(EventsHeader);
        }
        if (keptEvents.Count < eventLines.Count)
            omitted = true;

        if (omitted) output.Add(OmissionMarker);
        return string.Join("\n", output).TrimEnd();
    }

    private static void AppendWithinBudget(List<string> lines, string line, ref int words, int contentLimit, ref bool omitted)
    {
        if (string.IsNullOrEmpty(line))
        {
            lines.Add(line);
            return;
        }

        var lineWords = WordCount(line);
        var available = contentLimit - words;
        if (lineWords <= available)
        {
            lines.Add(line);
            words += lineWords;
            return;
        }

        if (available > 0)
        {
            var wordsToKeep = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Take(available);
            lines.Add(string.Join(" ", wordsToKeep));
            words += available;
        }
        omitted = true;
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
