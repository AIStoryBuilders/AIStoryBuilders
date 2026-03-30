using System.Text.RegularExpressions;

namespace AIStoryBuilders.Services;

/// <summary>
/// Cleans text by stripping invisible Unicode characters, normalising whitespace,
/// and removing control characters before embedding or AI processing.
/// </summary>
public static class TextSanitiser
{
    public const int MaxEmbeddingChars = 1500;

    private static readonly char[] InvisibleChars =
    {
        '\u200B', // zero-width space
        '\u200C', // zero-width non-joiner
        '\u200D', // zero-width joiner
        '\uFEFF', // byte order mark
        '\u00AD', // soft hyphen
    };

    public static (string Cleaned, bool WasTruncated) Sanitise(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (string.Empty, false);

        var text = raw;

        // Step 1 — Remove invisible Unicode characters
        foreach (var c in InvisibleChars)
            text = text.Replace(c.ToString(), string.Empty);

        // Remove control chars except \n and \t
        text = Regex.Replace(text, @"[\x00-\x08\x0B\x0C\x0E-\x1F]", string.Empty);

        // Replace non-breaking space with regular space
        text = text.Replace('\u00A0', ' ');

        // Step 2 — Normalise whitespace
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        return (text, false);
    }
}
