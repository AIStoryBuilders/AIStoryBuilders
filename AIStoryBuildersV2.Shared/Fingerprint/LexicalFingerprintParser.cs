using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIStoryBuildersV2.Shared.Fingerprint;

/// <summary>
/// Validates a lexical fingerprint file with the tool's own importer rules (plan §6.1, LF5), read from its client-side
/// validator on 2026-11-14. It accepts a bare fingerprint object or the <c>{ "title", "fingerprint" }</c> wrapper.
/// </summary>
public static class LexicalFingerprintParser
{
    public const string TooLarge = "The file is larger than 1 MB.";
    public const string Unreadable = "Could not read the fingerprint file.";
    public const string Empty = "The fingerprint file is empty.";
    public const string NotJson = "The fingerprint file is not valid JSON.";
    public const string NotObject = "The fingerprint file must be a JSON object.";
    public const string SchemaPrefix = "This file does not match a lexical fingerprint. ";

    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    sealed class SchemaException(string message) : Exception(message);

    /// <summary>Never throws. Applies §6.1, including the size and BOM pre-checks.</summary>
    public static FingerprintParseResult Parse(ReadOnlySpan<byte> utf8, string? fileName)
    {
        string? name = null;
        try
        {
            if (utf8.Length > LexicalFingerprintDefaults.MaxFileBytes) return Fail(TooLarge);

            if (utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF) utf8 = utf8[3..];
            string text;
            try { text = StrictUtf8.GetString(utf8); }
            catch (DecoderFallbackException) { return Fail(Unreadable); }

            if (text.Trim().Length == 0) return Fail(Empty);

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(text);
                // JsonObject builds its dictionary lazily, so a duplicate key only throws when it is first read.
                Materialize(root);
            }
            catch (Exception) { return Fail(NotJson); }

            if (root is not JsonObject rootObject) return Fail(NotObject);

            JsonObject fingerprint = rootObject;
            string? title = null;
            if (rootObject["fingerprint"] is JsonObject inner)
            {
                fingerprint = inner;
                title = StringOrNull(rootObject["title"])?.Trim();
            }
            name = Cut(string.IsNullOrEmpty(title) ? NameFromFile(fileName) : title);

            try
            {
                var parsed = Validate(fingerprint);
                return new FingerprintParseResult(true, parsed, fingerprint, name, null);
            }
            catch (SchemaException ex)
            {
                return new FingerprintParseResult(false, null, null, name, SchemaPrefix + ex.Message);
            }
        }
        catch (Exception)
        {
            return new FingerprintParseResult(false, null, null, name, Unreadable);
        }

        static FingerprintParseResult Fail(string error) => new(false, null, null, null, error);
    }

    /// <summary>The file name without its path and a trailing ".json", or the default name.</summary>
    static string NameFromFile(string? fileName)
    {
        var name = (fileName ?? "").Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) name = name[..^5];
        name = name.Trim();
        return name.Length == 0 ? LexicalFingerprintDefaults.DefaultName : name;
    }

    static string Cut(string name) =>
        name.Length > LexicalFingerprintDefaults.MaxNameChars ? name[..LexicalFingerprintDefaults.MaxNameChars] : name;

    static void Materialize(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (_, child) in o) Materialize(child);
                break;
            case JsonArray a:
                foreach (var child in a) Materialize(child);
                break;
        }
    }

    static LexicalFingerprint Validate(JsonObject fp)
    {
        // Row 6: invariants
        if (fp["invariants"] is not JsonObject invariantsNode) throw new SchemaException("Missing invariants");
        var invariants = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var info in LexicalFingerprintDefaults.Invariants)
        {
            var value = invariantsNode[info.Key];
            if (value is null) invariants[info.Key] = "";
            else if (IsString(value)) invariants[info.Key] = value.GetValue<string>().Trim();
            else throw new SchemaException("Missing invariants");
        }

        // Rows 7-9: criteria
        if (fp["criteria"] is not JsonObject criteriaNode) throw new SchemaException("Missing criteria");
        var criteria = new Dictionary<string, FingerprintCriterion>(StringComparer.Ordinal);
        foreach (var info in LexicalFingerprintDefaults.Criteria)
        {
            if (criteriaNode[info.Key] is not JsonObject c) throw new SchemaException("Missing criterion: " + info.Key);
            var invalid = new SchemaException("Invalid criterion payload: " + info.Key);
            if (!TryRating(c["rating"], out var rating)) throw invalid;
            var measurement = NonBlank(c["measurement"]) ?? throw invalid;
            var pattern = NonBlank(c["pattern"]) ?? throw invalid;
            var example = NonBlank(c["example"]) ?? throw invalid;
            criteria[info.Key] = new FingerprintCriterion(rating, measurement, pattern, example);
        }

        // Rows 10-13: string lists
        var diction = StringList(fp["diction_domains"], "Invalid diction_domains");
        var figurative = StringList(fp["figurative_source_domains"], "Invalid figurative_source_domains");
        var antiPatterns = StringList(fp["anti_patterns"], "Invalid anti_patterns");
        var directives = StringList(fp["style_directives"], "Invalid style_directives");

        // Row 14: signature moves
        var moves = new List<SignatureMove>();
        var movesNode = fp["signature_moves"];
        if (movesNode is not null)
        {
            if (movesNode is not JsonArray movesArray) throw new SchemaException("Invalid signature_moves");
            foreach (var item in movesArray)
            {
                if (item is not JsonObject move) throw new SchemaException("Invalid signature_moves");
                var moveName = TrimmedOrEmpty(move["name"]);
                var template = TrimmedOrEmpty(move["template"]);
                var example = TrimmedOrEmpty(move["example"]);
                var filled = (moveName.Length > 0 ? 1 : 0) + (template.Length > 0 ? 1 : 0) + (example.Length > 0 ? 1 : 0);
                if (filled == 0) continue;
                if (filled < 3) throw new SchemaException("Invalid signature_moves");
                moves.Add(new SignatureMove(moveName, template, example));
            }
        }

        // Row 15: condensed profile
        var profile = NonBlank(fp["condensed_profile"]) ?? throw new SchemaException("Missing condensed_profile");

        return new LexicalFingerprint(invariants, criteria, diction, figurative, moves, antiPatterns, directives, profile);
    }

    static bool IsString(JsonNode? node) => node is JsonValue v && v.GetValueKind() == JsonValueKind.String;

    static string? StringOrNull(JsonNode? node) => IsString(node) ? node!.GetValue<string>() : null;

    static string? NonBlank(JsonNode? node)
    {
        var s = StringOrNull(node)?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    static string TrimmedOrEmpty(JsonNode? node) => StringOrNull(node)?.Trim() ?? "";

    /// <summary>A finite JSON number, rounded the JavaScript way (Math.round), then checked to be in −10…+10.</summary>
    static bool TryRating(JsonNode? node, out int rating)
    {
        rating = 0;
        if (node is not JsonValue v || v.GetValueKind() != JsonValueKind.Number) return false;
        double x;
        try
        {
            if (!v.TryGetValue(out x)) return false;
        }
        catch (Exception) { return false; }
        if (!double.IsFinite(x)) return false;
        var rounded = Math.Floor(x + 0.5);
        if (rounded < -10 || rounded > 10) return false;
        rating = (int)rounded;
        return true;
    }

    static List<string> StringList(JsonNode? node, string error)
    {
        if (node is null) return [];
        if (node is not JsonArray array) throw new SchemaException(error);
        var list = new List<string>();
        foreach (var item in array)
        {
            if (!IsString(item)) throw new SchemaException(error);
            var s = item!.GetValue<string>().Trim();
            if (s.Length > 0) list.Add(s);
        }
        return list;
    }
}
