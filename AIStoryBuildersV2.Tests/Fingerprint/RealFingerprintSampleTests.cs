using AIStoryBuildersV2.Shared.Fingerprint;
using Xunit.Abstractions;

namespace AIStoryBuildersV2.Tests.Fingerprint;

/// <summary>
/// Opt-in check of a real fingerprint downloaded from the tool. It skips unless ASB_FINGERPRINT_SAMPLE names a .json file.
/// Real fingerprints quote the author's prose, so they are never committed (Lexical-Fingerprint-Plan §9).
/// </summary>
public class RealFingerprintSampleTests(ITestOutputHelper output)
{
    static string? SamplePath()
    {
        var path = Environment.GetEnvironmentVariable("ASB_FINGERPRINT_SAMPLE");
        return string.IsNullOrWhiteSpace(path) || !File.Exists(path) ? null : path;
    }

    [SkippableFact]
    public async Task A_real_fingerprint_parses_saves_and_renders_a_bounded_card()
    {
        var path = SamplePath();
        Skip.If(path is null, "Set ASB_FINGERPRINT_SAMPLE to a fingerprint .json file to run this test.");

        var bytes = await File.ReadAllBytesAsync(path!);
        var parsed = LexicalFingerprintParser.Parse(bytes, Path.GetFileName(path));
        Assert.True(parsed.Ok, parsed.Error);

        using var f = new ServiceFixture();
        var saved = await f.Service.SaveLexicalFingerprintAsync(FixtureFactory.Title, bytes, Path.GetFileName(path!));
        Assert.Equal(FingerprintStatus.Ok, saved.Status);
        var read = await f.Service.GetLexicalFingerprintAsync(FixtureFactory.Title, logInvalid: true);
        Assert.Equal(FingerprintStatus.Ok, read.Status);

        var card = await f.Service.GetFingerprintPromptAsync(FixtureFactory.Title);
        Assert.NotNull(card);
        Assert.True(card!.Length <= LexicalFingerprintDefaults.MaxPromptChars);
        Assert.StartsWith(FingerprintPromptRenderer.OpenTagStart, card);
        Assert.EndsWith(FingerprintPromptRenderer.CloseTag, card);

        output.WriteLine($"Name: {read.Name}; criteria: {read.Fingerprint!.Criteria.Count}; directives: {read.Fingerprint.StyleDirectives.Count}; card: {card.Length} chars");
        output.WriteLine(card);
    }
}
