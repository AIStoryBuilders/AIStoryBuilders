using System.Text;
using System.Text.Json.Nodes;
using AIStoryBuildersV2.Shared.Fingerprint;
using P = AIStoryBuildersV2.Shared.Fingerprint.LexicalFingerprintParser;

namespace AIStoryBuildersV2.Tests.Fingerprint;

/// <summary>Lexical-Fingerprint-Plan §10 parser rows (§6.1 rules).</summary>
public class LexicalFingerprintParserTests
{
    static FingerprintParseResult Parse(JsonNode node, string? fileName = "voice.json") => P.Parse(FingerprintFixtures.Utf8(node), fileName);

    static FingerprintParseResult Parse(string text, string? fileName = "voice.json") => P.Parse(Encoding.UTF8.GetBytes(text), fileName);

    static JsonObject With(Action<JsonObject> change)
    {
        var fp = FingerprintFixtures.Bare();
        change(fp);
        return fp;
    }

    static void AssertError(string expected, FingerprintParseResult result)
    {
        Assert.False(result.Ok);
        Assert.Null(result.Fingerprint);
        Assert.Equal(expected, result.Error);
    }

    static string Schema(string message) => P.SchemaPrefix + message;

    [Fact]
    public void A_bare_file_is_ok_and_named_after_the_file()
    {
        var r = P.Parse(FingerprintFixtures.Bytes("valid-bare.json"), @"C:\Users\someone\Downloads\My Voice.JSON");
        Assert.True(r.Ok, r.Error);
        Assert.Equal("My Voice", r.SuggestedName);
        var fp = r.Fingerprint!;
        Assert.Equal(17, fp.Criteria.Count);
        Assert.Equal("Past tense", fp.Invariants["tense"]);
        Assert.Equal(4, fp.Criteria["sentence_length_variability"].Rating);
        Assert.Equal(-7, fp.Criteria["sonic_ornamentation"].Rating);
        Assert.Equal(2, fp.SignatureMoves.Count);
        Assert.Equal(4, fp.StyleDirectives.Count);
        Assert.StartsWith("Close third-person", fp.CondensedProfile);
        Assert.NotNull(r.Raw);
    }

    [Fact]
    public void A_wrapped_file_uses_its_trimmed_title_and_keeps_unknown_keys()
    {
        var r = P.Parse(FingerprintFixtures.Bytes("valid-wrapped.json"), "ignored.json");
        Assert.True(r.Ok, r.Error);
        Assert.Equal("Harbor voice", r.SuggestedName);
        Assert.Equal("kept as an unknown key", r.Raw!["generator_note"]!.GetValue<string>());
    }

    [Fact]
    public void A_bom_and_surrounding_whitespace_are_accepted()
    {
        var bytes = FingerprintFixtures.Bytes("valid-bom.json");
        Assert.Equal(0xEF, bytes[0]);
        Assert.True(P.Parse(bytes, "valid-bom.json").Ok);
    }

    [Theory]
    [InlineData("", P.Empty)]
    [InlineData("  \r\n\t ", P.Empty)]
    [InlineData("{ not json", P.NotJson)]
    [InlineData("[1,2,3]", P.NotObject)]
    [InlineData("\"text\"", P.NotObject)]
    public void Pre_checks_return_their_messages(string text, string expected) => AssertError(expected, Parse(text));

    [Fact]
    public void Invalid_utf8_is_unreadable() => AssertError(P.Unreadable, P.Parse(new byte[] { 0x7B, 0xC3, 0x28, 0x7D }, "x.json"));

    [Fact]
    public void A_file_over_1_MB_is_refused_before_reading()
    {
        var bytes = new byte[LexicalFingerprintDefaults.MaxFileBytes + 1];
        Array.Fill(bytes, (byte)' ');
        AssertError(P.TooLarge, P.Parse(bytes, "big.json"));
        // Exactly 1,000,000 bytes passes the size check (and then fails as blank).
        AssertError(P.Empty, P.Parse(bytes.AsSpan(1), "big.json"));
    }

    [Fact]
    public void Missing_pieces_name_the_problem()
    {
        AssertError(Schema("Missing invariants"), Parse(With(fp => fp.Remove("invariants"))));
        AssertError(Schema("Missing criteria"), Parse(With(fp => fp.Remove("criteria"))));
        AssertError(Schema("Missing criterion: fragment_usage"), P.Parse(FingerprintFixtures.Bytes("missing-criterion.json"), "m.json"));
        AssertError(Schema("Missing condensed_profile"), Parse(With(fp => fp.Remove("condensed_profile"))));
        AssertError(Schema("Missing condensed_profile"), Parse(With(fp => fp["condensed_profile"] = "   ")));
    }

    [Fact]
    public void Wrong_types_fail()
    {
        AssertError(Schema("Missing invariants"), Parse(With(fp => fp["invariants"] = new JsonArray())));
        AssertError(Schema("Missing invariants"), Parse(With(fp => fp["invariants"]!["tense"] = 3)));
        AssertError(Schema("Missing condensed_profile"), Parse(With(fp => fp["condensed_profile"] = 42)));
        AssertError(Schema("Missing criterion: verb_energy"), Parse(With(fp => fp["criteria"]!["verb_energy"] = "high")));
        AssertError(Schema("Invalid criterion payload: verb_energy"), Parse(With(fp => fp["criteria"]!["verb_energy"]!["pattern"] = " ")));
        AssertError(Schema("Invalid criterion payload: verb_energy"), Parse(With(fp => fp["criteria"]!["verb_energy"]!.AsObject().Remove("example"))));
    }

    [Fact]
    public void Missing_or_null_invariants_become_empty()
    {
        var r = Parse(With(fp =>
        {
            fp["invariants"]!.AsObject().Remove("tense");
            fp["invariants"]!["dialogue_ratio"] = null;
            fp["invariants"]!["extra"] = 5;
        }));
        Assert.True(r.Ok, r.Error);
        Assert.Equal("", r.Fingerprint!.Invariants["tense"]);
        Assert.Equal("", r.Fingerprint.Invariants["dialogue_ratio"]);
    }

    [Theory]
    [InlineData("10.4", true, 10)]
    [InlineData("10.5", false, 0)]   // Math.round(10.5) is 11
    [InlineData("-10.5", true, -10)] // Math.round(-10.5) is -10
    [InlineData("-10.6", false, 0)]
    [InlineData("-0.4", true, 0)]
    [InlineData("\"5\"", false, 0)]
    [InlineData("1e400", false, 0)]
    [InlineData("null", false, 0)]
    [InlineData("true", false, 0)]
    public void Ratings_round_like_javascript_then_range_check(string json, bool ok, int expected)
    {
        var text = FingerprintFixtures.Bare().ToJsonString().Replace("\"rating\":3,", "\"rating\":" + json + ",");
        Assert.Contains("\"rating\":" + json, text);   // verb_energy is the only criterion rated 3
        var r = Parse(text);
        if (ok)
        {
            Assert.True(r.Ok, r.Error);
            Assert.Equal(expected, r.Fingerprint!.Criteria["verb_energy"].Rating);
        }
        else AssertError(Schema("Invalid criterion payload: verb_energy"), r);
    }

    [Fact]
    public void A_rating_out_of_range_fails() =>
        AssertError(Schema("Invalid criterion payload: verb_energy"), P.Parse(FingerprintFixtures.Bytes("bad-rating.json"), "b.json"));

    [Fact]
    public void A_wrapper_with_an_empty_title_falls_back_to_the_file_name()
    {
        var wrapped = new JsonObject { ["title"] = "  ", ["fingerprint"] = FingerprintFixtures.Bare() };
        Assert.Equal("from-file", Parse(wrapped, "folder/from-file.json").SuggestedName);
        var numeric = new JsonObject { ["title"] = 7, ["fingerprint"] = FingerprintFixtures.Bare() };
        Assert.Equal("from-file", Parse(numeric, "from-file.json").SuggestedName);
        Assert.Equal(LexicalFingerprintDefaults.DefaultName, Parse(FingerprintFixtures.Bare(), null).SuggestedName);
        Assert.Equal(LexicalFingerprintDefaults.DefaultName, Parse(FingerprintFixtures.Bare(), " .json").SuggestedName);
    }

    [Fact]
    public void A_long_title_is_cut_to_200_characters()
    {
        var wrapped = new JsonObject { ["title"] = new string('x', 500), ["fingerprint"] = FingerprintFixtures.Bare() };
        Assert.Equal(200, Parse(wrapped).SuggestedName!.Length);
    }

    [Fact]
    public void A_fingerprint_property_that_is_not_an_object_means_the_root_is_the_fingerprint()
    {
        var r = Parse(With(fp => { fp["fingerprint"] = "not an object"; fp["title"] = "Ignored"; }), "root.json");
        Assert.True(r.Ok, r.Error);
        Assert.Equal("root", r.SuggestedName);
    }

    [Fact]
    public void Duplicate_keys_and_deep_nesting_are_not_valid_json()
    {
        var bare = FingerprintFixtures.Bare().ToJsonString();
        AssertError(P.NotJson, Parse("{\"a\":1,\"a\":2," + bare[1..]));
        var deep = new string('[', 70) + new string(']', 70);
        AssertError(P.NotJson, Parse("{\"x\":" + deep + "}"));
    }

    [Fact]
    public void The_first_failure_in_rule_order_wins()
    {
        var r = Parse(With(fp => { fp.Remove("criteria"); fp["anti_patterns"] = "bad"; }));
        AssertError(Schema("Missing criteria"), r);
    }

    [Fact]
    public void Signature_moves_skip_empty_items_and_refuse_partial_ones()
    {
        var r = Parse(With(fp => fp["signature_moves"]!.AsArray().Add(new JsonObject { ["name"] = " ", ["template"] = null, ["example"] = 4 })));
        Assert.True(r.Ok, r.Error);
        Assert.Equal(2, r.Fingerprint!.SignatureMoves.Count);

        AssertError(Schema("Invalid signature_moves"), P.Parse(FingerprintFixtures.Bytes("partial-move.json"), "p.json"));
        AssertError(Schema("Invalid signature_moves"), Parse(With(fp => fp["signature_moves"]!.AsArray().Add("text"))));
        AssertError(Schema("Invalid signature_moves"), Parse(With(fp => fp["signature_moves"] = new JsonObject())));
    }

    [Theory]
    [InlineData("diction_domains")]
    [InlineData("figurative_source_domains")]
    [InlineData("anti_patterns")]
    [InlineData("style_directives")]
    public void String_lists_must_hold_strings(string key)
    {
        AssertError(Schema("Invalid " + key), Parse(With(fp => fp[key] = "one")));
        AssertError(Schema("Invalid " + key), Parse(With(fp => fp[key] = new JsonArray("ok", 3))));
        AssertError(Schema("Invalid " + key), Parse(With(fp => fp[key] = new JsonArray("ok", null))));
    }

    [Fact]
    public void Null_lists_become_empty_and_blank_items_are_dropped()
    {
        var r = Parse(With(fp =>
        {
            fp["diction_domains"] = null;
            fp.Remove("figurative_source_domains");
            fp["signature_moves"] = null;
            fp["style_directives"] = new JsonArray(" Keep it plain. ", "", "   ");
        }));
        Assert.True(r.Ok, r.Error);
        Assert.Empty(r.Fingerprint!.DictionDomains);
        Assert.Empty(r.Fingerprint.FigurativeSourceDomains);
        Assert.Empty(r.Fingerprint.SignatureMoves);
        Assert.Equal(["Keep it plain."], r.Fingerprint.StyleDirectives);
    }

    [Fact]
    public void Every_fixture_and_random_bytes_parse_without_throwing()
    {
        foreach (var name in FingerprintFixtures.All)
        {
            var r = P.Parse(FingerprintFixtures.Bytes(name), name);
            Assert.True(r.Ok || r.Error is not null, name);
        }

        var rng = new Random(20261114);
        var sample = FingerprintFixtures.Bytes("valid-bare.json");
        for (var i = 0; i < 500; i++)
        {
            var bytes = new byte[rng.Next(0, 300)];
            rng.NextBytes(bytes);
            Assert.NotNull(P.Parse(bytes, "random.json").Error);

            // Corrupt a valid file in a few places.
            var mutated = (byte[])sample.Clone();
            for (var j = 0; j < 3; j++) mutated[rng.Next(mutated.Length)] = (byte)rng.Next(256);
            var r = P.Parse(mutated, "mutated.json");
            Assert.True(r.Ok || r.Error is not null);
        }
    }
}
