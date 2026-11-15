using System.Text.Json.Nodes;
using AIStoryBuildersV2.Shared.Fingerprint;

namespace AIStoryBuildersV2.Tests.Fingerprint;

/// <summary>Lexical-Fingerprint-Plan §10 storage rows: {Title}/LexicalFingerprint.json (§6.2).</summary>
public class FingerprintStorageTests : IDisposable
{
    readonly ServiceFixture _f = new();
    const string Title = FixtureFactory.Title;

    public void Dispose() => _f.Dispose();

    string FilePath => Path.Combine(_f.StoryDir, LexicalFingerprintDefaults.FileName);

    int LogLinesAbout(string text) => _f.Log.Lines.Count(l => l.Contains(text, StringComparison.Ordinal));

    [Fact]
    public void The_file_lives_in_the_story_root()
    {
        Assert.Equal(FilePath, _f.Service.FingerprintPath(Title));
    }

    [Fact]
    public async Task Get_without_a_file_is_none_and_logs_nothing()
    {
        var before = _f.Log.Lines.Count;
        var fp = await _f.Service.GetLexicalFingerprintAsync(Title, logInvalid: true);
        Assert.Same(StoryFingerprint.NoneValue, fp);
        Assert.Equal(before, _f.Log.Lines.Count);
        Assert.Null(await _f.Service.GetFingerprintPromptAsync(Title));
        Assert.Equal(StoryFingerprint.NoneValue, await _f.Service.GetLexicalFingerprintAsync("No such story"));
    }

    [Fact]
    public async Task Save_writes_the_wrapper_and_get_reads_it_back()
    {
        var saved = await _f.Service.SaveLexicalFingerprintAsync(Title, FingerprintFixtures.Bytes("valid-wrapped.json"), "valid-wrapped.json");
        Assert.Equal(FingerprintStatus.Ok, saved.Status);
        Assert.Equal("Harbor voice", saved.Name);
        Assert.Equal([Title], _f.Persistence.Dirty);
        Assert.False(File.Exists(FilePath + ".tmp"));

        var bytes = File.ReadAllBytes(FilePath);
        Assert.NotEqual(0xEF, bytes[0]);   // UTF-8 without BOM
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("\r\n", text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
        var root = JsonNode.Parse(text)!.AsObject();
        Assert.Equal(["title", "uploadedUtc", "fingerprint"], root.Select(p => p.Key));
        Assert.Equal("Harbor voice", root["title"]!.GetValue<string>());
        Assert.EndsWith("Z", root["uploadedUtc"]!.GetValue<string>());
        Assert.DoesNotContain("\\u002B", text);
        Assert.Equal("kept as an unknown key", root["fingerprint"]!["generator_note"]!.GetValue<string>());

        var read = await _f.Service.GetLexicalFingerprintAsync(Title);
        Assert.Equal(FingerprintStatus.Ok, read.Status);
        Assert.Equal("Harbor voice", read.Name);
        Assert.NotNull(read.UploadedUtc);
        Assert.InRange(read.UploadedUtc!.Value, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(saved.Fingerprint!.CondensedProfile, read.Fingerprint!.CondensedProfile);

        // The stored wrapper is still valid input to the tool's importer (and ours).
        Assert.True(LexicalFingerprintParser.Parse(bytes, null).Ok);
    }

    [Fact]
    public async Task Save_names_a_bare_upload_after_its_file_and_replaces_an_old_one()
    {
        await _f.Service.SaveLexicalFingerprintAsync(Title, FingerprintFixtures.Bytes("valid-wrapped.json"), "a.json");
        File.WriteAllText(FilePath + ".tmp", "stale");
        File.SetAttributes(FilePath, FileAttributes.ReadOnly);

        var saved = await _f.Service.SaveLexicalFingerprintAsync(Title, FingerprintFixtures.Bytes("valid-bare.json"), "Sea voice.json");

        Assert.Equal("Sea voice", saved.Name);
        Assert.Equal("Sea voice", (await _f.Service.GetLexicalFingerprintAsync(Title)).Name);
        Assert.False(File.Exists(FilePath + ".tmp"));
        Assert.Equal(2, _f.Persistence.Dirty.Count);
    }

    [Fact]
    public async Task Save_cleans_control_characters_from_the_title()
    {
        var wrapped = new JsonObject { ["title"] = "Line\none\ttab", ["fingerprint"] = FingerprintFixtures.Bare() };
        var saved = await _f.Service.SaveLexicalFingerprintAsync(Title, FingerprintFixtures.Utf8(wrapped), "x.json");
        Assert.Equal("Lineonetab", saved.Name);
    }

    [Fact]
    public async Task Save_of_an_invalid_file_writes_nothing()
    {
        var result = await _f.Service.SaveLexicalFingerprintAsync(Title, FingerprintFixtures.Bytes("missing-criterion.json"), "m.json");
        Assert.Equal(FingerprintStatus.Invalid, result.Status);
        Assert.Equal(LexicalFingerprintParser.SchemaPrefix + "Missing criterion: fragment_usage", result.Error);
        Assert.False(File.Exists(FilePath));
        Assert.Empty(_f.Persistence.Dirty);
    }

    [Fact]
    public async Task A_corrupt_file_is_invalid_and_logged_once_without_content()
    {
        File.WriteAllText(FilePath, "{ \"secret prose\": ");
        var fp = await _f.Service.GetLexicalFingerprintAsync(Title, logInvalid: true);
        Assert.Equal(FingerprintStatus.Invalid, fp.Status);
        Assert.Equal(LexicalFingerprintParser.NotJson, fp.Error);
        Assert.Equal(1, LogLinesAbout($"Lexical fingerprint for '{Title}' is invalid: {LexicalFingerprintParser.NotJson}"));
        Assert.Equal(0, LogLinesAbout("secret prose"));

        // AI calls skip it silently.
        Assert.Null(await _f.Service.GetFingerprintPromptAsync(Title));
        Assert.Equal(1, LogLinesAbout("Lexical fingerprint for"));
    }

    [Fact]
    public async Task An_invalid_wrapped_file_keeps_its_name()
    {
        var bad = FingerprintFixtures.Bare();
        bad.Remove("criteria");
        File.WriteAllText(FilePath, new JsonObject { ["title"] = "Old voice", ["fingerprint"] = bad }.ToJsonString());
        var fp = await _f.Service.GetLexicalFingerprintAsync(Title);
        Assert.Equal(FingerprintStatus.Invalid, fp.Status);
        Assert.Equal("Old voice", fp.Name);
    }

    [Fact]
    public async Task A_file_over_1_MB_is_invalid()
    {
        File.WriteAllBytes(FilePath, new byte[LexicalFingerprintDefaults.MaxFileBytes + 10]);
        var fp = await _f.Service.GetLexicalFingerprintAsync(Title, logInvalid: true);
        Assert.Equal(FingerprintStatus.Invalid, fp.Status);
        Assert.Equal(LexicalFingerprintParser.TooLarge, fp.Error);
        Assert.Equal(1, LogLinesAbout("Lexical fingerprint for"));
    }

    [Fact]
    public async Task A_bare_stored_file_is_ok_with_the_default_name_and_no_upload_time()
    {
        File.WriteAllBytes(FilePath, FingerprintFixtures.Bytes("valid-bom.json"));
        var fp = await _f.Service.GetLexicalFingerprintAsync(Title);
        Assert.Equal(FingerprintStatus.Ok, fp.Status);
        Assert.Equal(LexicalFingerprintDefaults.DefaultName, fp.Name);
        Assert.Null(fp.UploadedUtc);
        Assert.StartsWith("<writing_style_fingerprint name=\"Lexical fingerprint\">", await _f.Service.GetFingerprintPromptAsync(Title));
    }

    [Fact]
    public async Task An_unparsable_upload_time_is_unknown()
    {
        File.WriteAllText(FilePath, new JsonObject { ["title"] = "T", ["uploadedUtc"] = "yesterday", ["fingerprint"] = FingerprintFixtures.Bare() }.ToJsonString());
        var fp = await _f.Service.GetLexicalFingerprintAsync(Title);
        Assert.Equal(FingerprintStatus.Ok, fp.Status);
        Assert.Null(fp.UploadedUtc);
    }

    [Fact]
    public async Task Remove_deletes_the_file_once()
    {
        File.WriteAllBytes(FilePath, FingerprintFixtures.Bytes("valid-bare.json"));
        File.SetAttributes(FilePath, FileAttributes.ReadOnly);
        Assert.True(await _f.Service.RemoveLexicalFingerprintAsync(Title));
        Assert.False(File.Exists(FilePath));
        Assert.Equal([Title], _f.Persistence.Dirty);

        Assert.False(await _f.Service.RemoveLexicalFingerprintAsync(Title));
        Assert.Single(_f.Persistence.Dirty);
    }

    [Fact]
    public async Task Story_readers_ignore_the_file_and_delete_removes_it()
    {
        await _f.Service.SaveLexicalFingerprintAsync(Title, FingerprintFixtures.Bytes("valid-bare.json"), "v.json");
        var story = (await _f.Service.GetStorysAsync()).Single();
        var full = await _f.Service.LoadFullStoryAsync(story);
        Assert.Equal(2, full.Chapter.Count);
        Assert.Equal(3, full.Character.Count);

        await _f.Service.DeleteStoryAsync(Title);
        Assert.False(File.Exists(FilePath));
    }
}
