namespace AIStoryBuildersV2.Shared.Fingerprint;

/// <summary>Constants for the story's lexical fingerprint (Docs/Lexical-Fingerprint-Plan.md §6.3).</summary>
public static class LexicalFingerprintDefaults
{
    /// <summary>The file inside the story folder: {Title}/LexicalFingerprint.json (LF1, D33).</summary>
    public const string FileName = "LexicalFingerprint.json";

    /// <summary>The fingerprint tool's own importer limit (LF12).</summary>
    public const long MaxFileBytes = 1_000_000;

    /// <summary>The style card cap, about 3,000 tokens (LF7).</summary>
    public const int MaxPromptChars = 12_000;

    public const int MaxNameChars = 200;

    /// <summary>The public, free fingerprint tool. The only copy of this URL in code (LF15).</summary>
    public const string ToolUrl = "https://worldspring.press/lexical-fingerprint";

    /// <summary>The name used when neither the wrapper nor the file name gives one.</summary>
    public const string DefaultName = "Lexical fingerprint";

    public static readonly IReadOnlyList<InvariantInfo> Invariants =
    [
        new("point_of_view", "Point of view"),
        new("tense", "Tense"),
        new("narrative_distance", "Narrative distance"),
        new("dialogue_ratio", "Dialogue ratio"),
    ];

    /// <summary>The 17 criteria in the tool's order, with the pole labels the style card uses (§6.1).</summary>
    public static readonly IReadOnlyList<CriterionInfo> Criteria =
    [
        new("sentence_length_variability", "Sentence length", "Uniform", "Wild swings"),
        new("clause_architecture", "Clause architecture", "Single-clause", "Heavily subordinated"),
        new("fragment_usage", "Fragments", "Always complete", "Constant device"),
        new("paragraph_density", "Paragraph density", "Terse", "Dense blocks"),
        new("punctuation_texture", "Punctuation texture", "Periods and commas", "Dashes, semicolons, ellipses"),
        new("formality_register", "Register", "Colloquial", "Elevated"),
        new("concreteness", "Concreteness", "Abstract", "Sensory"),
        new("verb_energy", "Verb energy", "Static", "Kinetic"),
        new("modifier_frequency", "Modifiers", "Spare", "Dense"),
        new("lexical_diversity", "Lexical diversity", "Recycled", "Broad"),
        new("intentional_repetition", "Intentional repetition", "Avoids", "Refrains"),
        new("sentence_openers", "Sentence openers", "Patterned", "Varied"),
        new("transitions_connectors", "Transitions", "Implicit", "Signposted"),
        new("figurative_density", "Figurative density", "Literal", "Saturated"),
        new("dialogue_tag_style", "Dialogue tags", "'Said' only", "Expressive beats"),
        new("dialogue_rendering", "Dialogue rendering", "Flat voices", "Idiosyncratic speech"),
        new("sonic_ornamentation", "Sonic ornamentation", "None", "Frequent soundplay"),
    ];
}
