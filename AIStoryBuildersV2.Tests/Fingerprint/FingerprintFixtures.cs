using System.Text;
using System.Text.Json.Nodes;

namespace AIStoryBuildersV2.Tests.Fingerprint;

/// <summary>The committed fingerprint fixtures (Test-Data §9). All content is invented.</summary>
public static class FingerprintFixtures
{
    public static string Path(string name) =>
        FixtureFactory.SourceFixturePath("Fingerprint/" + name) ?? throw new InvalidOperationException("The test project folder was not found.");

    public static byte[] Bytes(string name) => File.ReadAllBytes(Path(name));

    public static JsonObject Bare() => JsonNode.Parse(Bytes("valid-bare.json"))!.AsObject();

    public static byte[] Utf8(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());

    public static readonly string[] All =
    [
        "valid-bare.json", "valid-wrapped.json", "valid-bom.json", "missing-criterion.json", "bad-rating.json",
        "partial-move.json", "injection.json", "huge-strings.json",
    ];
}
