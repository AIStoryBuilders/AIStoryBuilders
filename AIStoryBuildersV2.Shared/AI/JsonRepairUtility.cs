#nullable disable
using System.Text.RegularExpressions;
using System.Text;

namespace AIStoryBuildersV2.Shared.AI;
/// <summary>
/// Deterministic JSON repair for common LLM output problems.
/// Replaces the LLM-based CleanJSON approach entirely.
/// </summary>
public static class JsonRepairUtility
{
    /// <summary>
    /// Extract JSON from LLM response text and repair common issues.
    /// </summary>
    public static string ExtractAndRepair(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "{}";

        var json = raw;

        // 1. Strip Markdown fences
        json = StripMarkdownFences(json);

        // 2. Strip leading/trailing non-JSON text
        json = IsolateJsonBlock(json);

        // 3. Fix trailing commas
        json = FixTrailingCommas(json);

        // 4. Fix unescaped newlines inside strings
        json = FixUnescapedNewlines(json);

        return json;
    }

    private static string StripMarkdownFences(string input)
    {
        var match = Regex.Match(input, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value : input;
    }

    private static string IsolateJsonBlock(string input)
    {
        int start = -1;
        char openChar = '{';
        char closeChar = '}';

        int braceStart = input.IndexOf('{');
        int bracketStart = input.IndexOf('[');

        if (braceStart >= 0 && (bracketStart < 0 || braceStart < bracketStart))
        {
            start = braceStart;
            openChar = '{';
            closeChar = '}';
        }
        else if (bracketStart >= 0)
        {
            start = bracketStart;
            openChar = '[';
            closeChar = ']';
        }

        if (start < 0) return input;

        int depth = 0;
        bool inString = false;
        bool escaped = false;
        int end = start;
        for (int i = start; i < input.Length; i++)
        {
            char c = input[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '"') inString = true;
            else if (c == openChar) depth++;
            else if (c == closeChar && --depth == 0) { end = i; break; }
        }

        return input.Substring(start, end - start + 1);
    }

    private static string FixTrailingCommas(string json)
    {
        var result = new StringBuilder(json.Length);
        bool inString = false;
        bool escaped = false;
        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            if (inString)
            {
                result.Append(c);
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                result.Append(c);
            }
            else if (c == ',')
            {
                int next = i + 1;
                while (next < json.Length && char.IsWhiteSpace(json[next])) next++;
                if (next == json.Length || (json[next] != '}' && json[next] != ']'))
                    result.Append(c);
            }
            else
                result.Append(c);
        }
        return result.ToString();
    }

    private static string FixUnescapedNewlines(string json)
    {
        return Regex.Replace(json, @"(?<=:[ ]*""[^""]*)\n(?=[^""]*"")", "\\n");
    }
}
