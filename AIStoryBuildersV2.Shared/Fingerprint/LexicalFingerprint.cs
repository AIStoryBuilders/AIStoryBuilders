using System.Text.Json.Nodes;

namespace AIStoryBuildersV2.Shared.Fingerprint;

public sealed record InvariantInfo(string Key, string Label);

public sealed record CriterionInfo(string Key, string Label, string LowPole, string HighPole);

/// <summary>One trait rating, already rounded and range-checked (−10 to +10).</summary>
public sealed record FingerprintCriterion(int Rating, string Measurement, string Pattern, string Example);

public sealed record SignatureMove(string Name, string Template, string Example);

/// <summary>A validated lexical fingerprint (plan §6.1). Strings are trimmed and blank list items dropped.</summary>
public sealed record LexicalFingerprint(
    IReadOnlyDictionary<string, string> Invariants,
    IReadOnlyDictionary<string, FingerprintCriterion> Criteria,
    IReadOnlyList<string> DictionDomains,
    IReadOnlyList<string> FigurativeSourceDomains,
    IReadOnlyList<SignatureMove> SignatureMoves,
    IReadOnlyList<string> AntiPatterns,
    IReadOnlyList<string> StyleDirectives,
    string CondensedProfile);

/// <summary>
/// The result of <see cref="LexicalFingerprintParser.Parse"/>. <see cref="Raw"/> is the fingerprint object as uploaded,
/// unknown keys kept (LF5). <see cref="SuggestedName"/> is set whenever the JSON root could be read.
/// </summary>
public sealed record FingerprintParseResult(bool Ok, LexicalFingerprint? Fingerprint, JsonObject? Raw, string? SuggestedName, string? Error);

public enum FingerprintStatus { None, Ok, Invalid }

/// <summary>A story's fingerprint as the UI and the AI see it (plan §6.3).</summary>
public sealed record StoryFingerprint(FingerprintStatus Status, string? Name, LexicalFingerprint? Fingerprint, DateTimeOffset? UploadedUtc, string? Error)
{
    public static readonly StoryFingerprint NoneValue = new(FingerprintStatus.None, null, null, null, null);
}
