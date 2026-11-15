#if DEBUG
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Shared.Llm;

/// <summary>
/// DEBUG-only scripted client for demos and manual checks without an API key. It understands a few fixed commands and
/// calls the Story Chat <c>kb_</c> tools for them (plan D14, Q19, Q20); anything else becomes a <c>kb_search_pages</c>
/// call. Commands (case-insensitive):
/// <list type="bullet">
/// <item><c>create a {name} page</c> → <c>kb_create_page</c></item>
/// <item><c>append to page {path}: {text}</c> → <c>kb_read_page</c>, then <c>kb_update_page</c> with the text added</item>
/// <item><c>delete page {path}</c> → <c>kb_delete_page</c></item>
/// <item><c>create folder {path}</c>, <c>rename folder {path} to {name}</c>, <c>move folder {path} to {folder|root}</c>,
/// <c>delete folder {path}</c> → the matching folder tool</item>
/// </list>
/// </summary>
public sealed partial class FakeChatClient : IChatClient
{
    [GeneratedRegex(@"append to (?:the )?page (?<path>[^:]+?):\s*(?<text>.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AppendPattern();

    [GeneratedRegex(@"delete (?:the )?page (?<path>\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex DeletePagePattern();

    [GeneratedRegex(@"create (?:a |the )?folder (?<path>\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex CreateFolderPattern();

    [GeneratedRegex(@"rename (?:the )?folder (?<path>\S+) to (?<name>\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex RenameFolderPattern();

    [GeneratedRegex(@"move (?:the )?folder (?<path>\S+) (?:to|into) (?:the )?(?<parent>\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex MoveFolderPattern();

    [GeneratedRegex(@"delete (?:the )?folder (?<path>\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex DeleteFolderPattern();

    [GeneratedRegex(@"create (?:a |an |the )?(?<name>[\w ]+?) page", RegexOptions.IgnoreCase)]
    private static partial Regex CreatePattern();

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var u in GetStreamingResponseAsync(messages, options, cancellationToken)) updates.Add(u);
        return updates.ToChatResponse();
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        var id = Guid.NewGuid().ToString("N");
        var last = list.LastOrDefault();
        var results = last?.Contents.OfType<FunctionResultContent>().ToList() ?? [];
        var userText = Strip(list.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "");

        if (results.Count > 0)
        {
            var calls = list.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToDictionary(c => c.CallId, c => c.Name);
            var tool = calls.TryGetValue(results[0].CallId, out var n) ? n : "";
            var json = Json(results[0]);

            // Second step of "append to page": the page was read, so propose the new full text.
            if (tool == "kb_read_page" && AppendPattern().Match(userText) is { Success: true } append && !json.Contains("\"error\"", StringComparison.Ordinal))
            {
                var markdown = Prop(JsonDocument.Parse(json).RootElement, "markdown")?.GetString() ?? "";
                if (!markdown.EndsWith('\n')) markdown += "\n";
                yield return Call(id, "kb_update_page", ("path", append.Groups["path"].Value.Trim()), ("markdown", markdown + append.Groups["text"].Value.Trim() + "\n"));
                yield break;
            }

            foreach (var word in Answer(tool, json).Split(' '))
            {
                await Task.Delay(25, cancellationToken);
                yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ") { MessageId = id };
            }
            yield return new ChatResponseUpdate { Role = ChatRole.Assistant, MessageId = id, FinishReason = ChatFinishReason.Stop };
            yield break;
        }

        await Task.Delay(150, cancellationToken);
        yield return FirstCall(id, userText);
    }

    /// <summary>Story Chat puts "[App note: …]" lines ahead of the user's text; commands match what follows them.</summary>
    static string Strip(string text) => Regex.Replace(text, @"^(\[App note:[^\n]*\]\n)+\n?", "").Trim();

    static ChatResponseUpdate FirstCall(string id, string text)
    {
        if (AppendPattern().Match(text) is { Success: true } a) return Call(id, "kb_read_page", ("path", a.Groups["path"].Value.Trim()));
        if (RenameFolderPattern().Match(text) is { Success: true } r)
            return Call(id, "kb_rename_folder", ("path", r.Groups["path"].Value), ("newName", r.Groups["name"].Value));
        if (MoveFolderPattern().Match(text) is { Success: true } mv)
        {
            var parent = mv.Groups["parent"].Value.TrimEnd('.');
            return Call(id, "kb_move_folder", ("path", mv.Groups["path"].Value), ("newParentFolder", parent.Equals("root", StringComparison.OrdinalIgnoreCase) ? "" : parent));
        }
        if (DeleteFolderPattern().Match(text) is { Success: true } df) return Call(id, "kb_delete_folder", ("path", df.Groups["path"].Value.TrimEnd('.')));
        if (CreateFolderPattern().Match(text) is { Success: true } cf) return Call(id, "kb_create_folder", ("path", cf.Groups["path"].Value.TrimEnd('.')));
        if (DeletePagePattern().Match(text) is { Success: true } dp) return Call(id, "kb_delete_page", ("path", dp.Groups["path"].Value.TrimEnd('.')));
        if (CreatePattern().Match(text) is { Success: true } m)
        {
            var name = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(m.Groups["name"].Value.Trim());
            return Call(id, "kb_create_page", ("path", name + ".md"),
                ("markdown", $"---\ntitle: {name}\ntags: [draft]\n---\n# {name}\n\n- First step\n- Second step\n- Third step\n"));
        }
        return Call(id, "kb_search_pages", ("query", string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 3).Take(3))));
    }

    static ChatResponseUpdate Call(string id, string name, params (string Key, object? Value)[] args)
    {
        var call = new FunctionCallContent("call_" + Guid.NewGuid().ToString("N")[..8], name, args.ToDictionary(a => a.Key, a => a.Value));
        var update = new ChatResponseUpdate { Role = ChatRole.Assistant, MessageId = id, FinishReason = ChatFinishReason.ToolCalls };
        update.Contents.Add(call);
        return update;
    }

    /// <summary>The tool result as JSON text. Story Chat passes it as text, and the kb_ read tools' JSON arrives as a JSON string.</summary>
    static string Json(FunctionResultContent r)
    {
        var text = r.Result switch
        {
            JsonElement { ValueKind: JsonValueKind.String } s => s.GetString() ?? "",
            JsonElement e => e.GetRawText(),
            var other => other?.ToString() ?? "",
        };
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.String ? doc.RootElement.GetString() ?? "" : text;
        }
        catch (JsonException) { return text; }
    }

    static JsonElement? Prop(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in e.EnumerateObject())
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    static string Answer(string tool, string json)
    {
        if (tool == "kb_search_pages")
        {
            var paths = Regex.Matches(json, "\"path\"\\s*:\\s*\"([^\"]+)\"").Select(x => x.Groups[1].Value.Replace(".md", "")).Distinct().Take(2).ToList();
            return paths.Count == 0
                ? "The knowledge base doesn't cover this. (Fake provider)"
                : $"I found {string.Join(" and ", paths.Select(p => $"[[{p}]]"))}. (Fake provider: this is a scripted answer.)";
        }
        JsonElement root;
        try { root = JsonDocument.Parse(json.Length == 0 ? "{}" : json).RootElement; }
        catch (JsonException) { return "Done. (Fake provider)"; }
        if (Prop(root, "pendingId") is not null)
        {
            var summary = Prop(root, "preview") is { } preview ? Prop(preview, "summary")?.GetString() : null;
            return $"I proposed this change: {summary}. Click Apply to make it, or Discard to drop it. (Fake provider)";
        }
        var error = Prop(root, "error");
        return error is { ValueKind: JsonValueKind.String } ? $"I couldn't propose that change: {error.Value.GetString()} (Fake provider)" : "Done. (Fake provider)";
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("fake", null, "fake") : null;

    public void Dispose() { }
}
#endif
