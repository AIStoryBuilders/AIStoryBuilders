using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AIStoryBuilders.AI;
using AIStoryBuilders.Model;
using AIStoryBuilders.Models;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace AIStoryBuilders.Services;

public interface IStoryChatService
{
    IAsyncEnumerable<string> SendMessageAsync(string userInput, string sessionId,
        CancellationToken cancellationToken = default);
    void ClearSession(string sessionId);
    void RefreshClient();
}

public class StoryChatService : IStoryChatService
{
    private static readonly TimeSpan LlmTimeout = TimeSpan.FromMinutes(10);

    private const string SystemPrompt = """
        You are a professional story analyst and editor assistant. You have access to a
        knowledge graph of a story with characters, locations, timelines,
        chapters, and paragraphs connected by relationship edges.

        You can help the user with:
        - Critiquing the story for inconsistencies, plot holes, and orphaned entities
        - Answering questions about characters, locations, timelines, and chapters
        - Tracing character arcs across chapters
        - Finding timeline conflicts or location mismatches
        - Summarizing story structure and relationships
        - Brainstorming improvements or alternatives
        - Making changes to the story — adding, updating, renaming, or removing
          characters, locations, timelines, chapters, and paragraphs

        Use the available tools to query the graph when you need specific data.
        Do not guess — always verify with a tool call when facts are available
        in the graph.

        When modifying the story: Always call the mutation tool with
        confirmed=false first to preview the change, then present the preview
        to the user. Only call with confirmed=true after the user approves.
        Explain what files will be affected and whether embeddings will be
        regenerated.

        Be conversational and helpful. When you find issues, explain them clearly
        and suggest potential fixes.
        """;

    private readonly ConcurrentDictionary<string, ConversationSession> _sessions = new();
    private readonly IGraphQueryService _queryService;
    private readonly IGraphMutationService _mutationService;
    private readonly SettingsService _settingsService;

    private IChatClient _chatClient;
    private string _cachedSettingsKey;

    public StoryChatService(
        IGraphQueryService queryService,
        IGraphMutationService mutationService,
        SettingsService settingsService)
    {
        _queryService = queryService;
        _mutationService = mutationService;
        _settingsService = settingsService;
    }

    public async IAsyncEnumerable<string> SendMessageAsync(
        string userInput, string sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var session = GetOrCreateSession(sessionId);
        session.Messages.Add(new ChatMessage(ChatRole.User, userInput));

        var chatClient = GetOrCreateChatClient();
        if (chatClient == null)
        {
            yield return "⚠️ AI is not configured. Please go to Settings and configure your AI provider and API key.";
            yield break;
        }

        if (GraphState.Current == null)
        {
            yield return "⚠️ No story graph is loaded. Please open a story first.";
            yield break;
        }

        var messages = new List<ChatMessage> { new(ChatRole.System, SystemPrompt) };
        messages.AddRange(session.Messages.TakeLast(20));

        var tools = BuildTools();
        var options = new ChatOptions
        {
            ModelId = _settingsService.AIModel,
            Temperature = 0.7f,
            MaxOutputTokens = 4096,
            Tools = tools
        };

        var responseBuilder = new StringBuilder();

        // Tool-call loop: max 10 rounds
        for (int round = 0; round < 10; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await chatClient.GetResponseAsync(messages, options, cancellationToken);
            var lastMessage = response.Messages[^1];

            var toolCalls = lastMessage.Contents.OfType<FunctionCallContent>().ToList();
            if (toolCalls.Count > 0)
            {
                messages.Add(lastMessage);
                foreach (var toolCall in toolCalls)
                {
                    var result = await DispatchToolCallAsync(toolCall.Name, toolCall.Arguments);
                    messages.Add(new ChatMessage(ChatRole.Tool,
                        [new FunctionResultContent(toolCall.CallId, result)]));
                }
                continue;
            }

            // No tool calls — return text
            var text = response.Text ?? "";
            responseBuilder.Append(text);
            yield return text;
            break;
        }

        session.Messages.Add(new ChatMessage(ChatRole.Assistant, responseBuilder.ToString()));
    }

    public void ClearSession(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId ?? "default", out var session))
            session.Messages.Clear();
    }

    public void RefreshClient()
    {
        _chatClient = null;
        _cachedSettingsKey = null;
    }

    // ── Private ────────────────────────────────────────────────

    private ConversationSession GetOrCreateSession(string sessionId)
    {
        sessionId ??= "default";
        return _sessions.GetOrAdd(sessionId, id => new ConversationSession
        {
            Id = id,
            CreatedAt = DateTime.UtcNow
        });
    }

    private IChatClient GetOrCreateChatClient()
    {
        _settingsService.LoadSettings();

        var key = $"{_settingsService.AIType}|{_settingsService.ApiKey}|{_settingsService.AIModel}|{_settingsService.Endpoint}";
        if (_chatClient != null && _cachedSettingsKey == key)
            return _chatClient;

        if (string.IsNullOrWhiteSpace(_settingsService.ApiKey))
            return null;

        _chatClient = CreateChatClient();
        _cachedSettingsKey = key;
        return _chatClient;
    }

    private IChatClient CreateChatClient()
    {
        return _settingsService.AIType switch
        {
            "Azure OpenAI" => new AzureOpenAIClient(
                new Uri(_settingsService.Endpoint),
                new System.ClientModel.ApiKeyCredential(_settingsService.ApiKey),
                new AzureOpenAIClientOptions { NetworkTimeout = LlmTimeout })
                .GetChatClient(_settingsService.AIModel).AsIChatClient(),

            "Anthropic" => new Anthropic.SDK.AnthropicClient(
                _settingsService.ApiKey).Messages,

            "Google AI" => new Mscc.GenerativeAI.Microsoft.GeminiChatClient(
                apiKey: _settingsService.ApiKey, _settingsService.AIModel),

            _ => new OpenAIClient(new System.ClientModel.ApiKeyCredential(_settingsService.ApiKey),
                new OpenAIClientOptions { NetworkTimeout = LlmTimeout })
                .GetChatClient(_settingsService.AIModel).AsIChatClient(),
        };
    }

    private IList<AITool> BuildTools()
    {
        return
        [
            // ── Read Tools (15) ────────────────────────────────────
            AIFunctionFactory.Create(
                [Description("List all characters in the story with name, role, and backstory")]
                () => _queryService.GetCharacters(),
                "GetCharacters"),

            AIFunctionFactory.Create(
                [Description("List all locations in the story")]
                () => _queryService.GetLocations(),
                "GetLocations"),

            AIFunctionFactory.Create(
                [Description("List all timelines in the story")]
                () => _queryService.GetTimelines(),
                "GetTimelines"),

            AIFunctionFactory.Create(
                [Description("List all chapters with title, beats summary, and paragraph count")]
                () => _queryService.GetChapters(),
                "GetChapters"),

            AIFunctionFactory.Create(
                [Description("Get a specific paragraph's full text, location, timeline, and characters")]
                ([Description("The chapter title")] string chapterTitle,
                 [Description("The paragraph index")] int paragraphIndex)
                    => _queryService.GetParagraph(chapterTitle, paragraphIndex),
                "GetParagraph"),

            AIFunctionFactory.Create(
                [Description("Get all graph edges for a character")]
                ([Description("The character name")] string characterName)
                    => _queryService.GetCharacterRelationships(characterName),
                "GetCharacterRelationships"),

            AIFunctionFactory.Create(
                [Description("Get chapters and paragraphs where a character appears")]
                ([Description("The character name")] string characterName)
                    => _queryService.GetCharacterAppearances(characterName),
                "GetCharacterAppearances"),

            AIFunctionFactory.Create(
                [Description("Get all characters in a specific chapter")]
                ([Description("The chapter title")] string chapterTitle)
                    => _queryService.GetChapterCharacters(chapterTitle),
                "GetChapterCharacters"),

            AIFunctionFactory.Create(
                [Description("Get chapters where a location is used and which characters are there")]
                ([Description("The location name")] string locationName)
                    => _queryService.GetLocationUsage(locationName),
                "GetLocationUsage"),

            AIFunctionFactory.Create(
                [Description("Get co-appearing characters and chapters of interactions")]
                ([Description("The character name")] string characterName)
                    => _queryService.GetCharacterInteractions(characterName),
                "GetCharacterInteractions"),

            AIFunctionFactory.Create(
                [Description("Get chapters covering a timeline")]
                ([Description("The timeline name")] string timelineName)
                    => _queryService.GetTimelineChapters(timelineName),
                "GetTimelineChapters"),

            AIFunctionFactory.Create(
                [Description("Get nodes with no edges (disconnected entities)")]
                () => _queryService.GetOrphanedNodes(),
                "GetOrphanedNodes"),

            AIFunctionFactory.Create(
                [Description("Get the chronological journey of a character across chapters")]
                ([Description("The character name")] string characterName)
                    => _queryService.GetCharacterArc(characterName),
                "GetCharacterArc"),

            AIFunctionFactory.Create(
                [Description("Get sequence of events at a location over time")]
                ([Description("The location name")] string locationName)
                    => _queryService.GetLocationTimeline(locationName),
                "GetLocationTimeline"),

            AIFunctionFactory.Create(
                [Description("Get high-level graph statistics")]
                () => _queryService.GetGraphSummary(),
                "GetGraphSummary"),

            // ── Write / Mutation Tools (20) ────────────────────────

            AIFunctionFactory.Create(
                [Description("Add a new character to the story. Set confirmed=false to preview, true to apply.")]
                ([Description("Character name")] string name,
                 [Description("Character type (e.g. Protagonist, Antagonist, Supporting)")] string type,
                 [Description("Character description/backstory")] string description,
                 [Description("Timeline name (optional)")] string timelineName,
                 [Description("false=preview, true=apply")] bool confirmed)
                    => _mutationService.AddCharacterAsync(name, type, description, timelineName, confirmed),
                "AddCharacter"),

            AIFunctionFactory.Create(
                [Description("Update an existing character's description. Set confirmed=false to preview.")]
                ([Description("Character name")] string name,
                 [Description("Character type")] string type,
                 [Description("Updated description")] string description,
                 [Description("Timeline name (optional)")] string timelineName,
                 [Description("false=preview, true=apply")] bool confirmed)
                    => _mutationService.UpdateCharacterAsync(name, type, description, timelineName, confirmed),
                "UpdateCharacter"),

            AIFunctionFactory.Create(
                [Description("Rename a character (updates all paragraph references). Set confirmed=false to preview.")]
                ([Description("Current character name")] string currentName,
                 [Description("New character name")] string newName,
                 [Description("false=preview, true=apply")] bool confirmed)
                    => _mutationService.RenameCharacterAsync(currentName, newName, confirmed),
                "RenameCharacter"),

            AIFunctionFactory.Create(
                [Description("Delete a character and remove from all paragraphs. Set confirmed=false to preview.")]
                ([Description("Character name to delete")] string name,
                 [Description("false=preview, true=apply")] bool confirmed)
                    => _mutationService.DeleteCharacterAsync(name, confirmed),
                "DeleteCharacter"),

            AIFunctionFactory.Create(
                [Description("Add a new location. Set confirmed=false to preview.")]
                ([Description("Location name")] string name,
                 [Description("Location description")] string description,
                 [Description("Timeline name (optional)")] string timelineName,
                 [Description("false=preview, true=apply")] bool confirmed)
                    => _mutationService.AddLocationAsync(name, description, timelineName, confirmed),
                "AddLocation"),

            AIFunctionFactory.Create(
                [Description("Update a location's description. Set confirmed=false to preview.")]
                ([Description("Location name")] string name,
                 [Description("Updated description")] string description,
                 [Description("Timeline name (optional)")] string timelineName,
                 [Description("false=preview, true=apply")] bool confirmed)
                    => _mutationService.UpdateLocationAsync(name, description, timelineName, confirmed),
                "UpdateLocation"),

            AIFunctionFactory.Create(
                [Description("Rename a location (updates all paragraph references). Set confirmed=false to preview.")]
                ([Description("Current location name")] string currentName,
                 [Description("New location name")] string newName,
                 [Description("false=preview, true=apply")] bool confirmed)
                    => _mutationService.RenameLocationAsync(currentName, newName, confirmed),
                "RenameLocation"),

            AIFunctionFactory.Create(
                [Description("Delete a location and clear from all paragraphs. Set confirmed=false to preview.")]
                ([Description("Location name to delete")] string name,
                 [Description("false=preview, true=apply")] bool confirmed)
                    => _mutationService.DeleteLocationAsync(name, confirmed),
                "DeleteLocation"),

            AIFunctionFactory.Create(
                [Description("Add a new timeline. Set confirmed=false to preview.")]
                ([Description("Timeline name")] string name,
                 [Description("Timeline description")] string description,
                 [Description("Start date")] DateTime startDate,
                 [Description("Stop date (optional)")] DateTime? stopDate,
                 [Description("false=preview, true=apply")] bool confirmed)
                    => _mutationService.AddTimelineAsync(name, description, startDate, stopDate, confirmed),
                "AddTimeline"),

            AIFunctionFactory.Create(
                [Description("Update a timeline's details. Set confirmed=false to preview.")]
                ([Description("Timeline name")] string name,
                 [Description("Updated description")] string description,
                 [Description("Start date")] DateTime startDate,
                 [Description("Stop date (optional)")] DateTime? stopDate,
                 [Description("false=preview, true=apply")] bool confirmed)
                    => _mutationService.UpdateTimelineAsync(name, description, startDate, stopDate, confirmed),
                "UpdateTimeline"),

            AIFunctionFactory.Create(
                [Description("Rename a timeline (updates all characters, locations, and paragraphs). Set confirmed=false to preview.")]
                ([Description("Current timeline name")] string currentName,
                 [Description("New timeline name")] string newName,
                 [Description("Description")] string description,
                 [Description("Start date")] DateTime startDate,
                 [Description("Stop date (optional)")] DateTime? stopDate,
                 [Description("false=preview, true=apply")] bool confirmed)
                    => _mutationService.RenameTimelineAsync(currentName, newName, description, startDate, stopDate, confirmed),
                "RenameTimeline"),

            AIFunctionFactory.Create(
                [Description("Delete a timeline and clear from all entities. Set confirmed=false to preview.")]
                ([Description("Timeline name to delete")] string name,
                 [Description("false=preview, true=apply")] bool confirmed)
                    => _mutationService.DeleteTimelineAsync(name, confirmed),
                "DeleteTimeline"),

            AIFunctionFactory.Create(
                [Description("Add a new chapter (optionally insert at a position). Set confirmed=false to preview.")]
                ([Description("Chapter synopsis")] string synopsis,
                 [Description("Insert position (null = append at end)")] int? insertAtPosition,
                 [Description("false=preview, true=apply")] bool confirmed)
                    => _mutationService.AddChapterAsync(synopsis, insertAtPosition, confirmed),
                "AddChapter"),

            AIFunctionFactory.Create(
                [Description("Update a chapter's synopsis. Set confirmed=false to preview.")]
                ([Description("Chapter number")] int chapterNumber,
                 [Description("Updated synopsis")] string synopsis,
                 [Description("false=preview, true=apply")] bool confirmed)
                    => _mutationService.UpdateChapterAsync(chapterNumber, synopsis, confirmed),
                "UpdateChapter"),

            AIFunctionFactory.Create(
                [Description("Delete a chapter and all its paragraphs. Set confirmed=false to preview.")]
                ([Description("Chapter number to delete")] int chapterNumber,
                 [Description("false=preview, true=apply")] bool confirmed)
                    => _mutationService.DeleteChapterAsync(chapterNumber, confirmed),
                "DeleteChapter"),

            AIFunctionFactory.Create(
                [Description("Add an empty paragraph at a position in a chapter. Set confirmed=false to preview.")]
                ([Description("Chapter number")] int chapterNumber,
                 [Description("Paragraph position")] int position,
                 [Description("Location name")] string locationName,
                 [Description("Timeline name")] string timelineName,
                 [Description("Character names")] List<string> characterNames,
                 [Description("false=preview, true=apply")] bool confirmed)
                    => _mutationService.AddParagraphAsync(chapterNumber, position, locationName, timelineName, characterNames, confirmed),
                "AddParagraph"),

            AIFunctionFactory.Create(
                [Description("Update a paragraph's content, location, timeline, and characters. Set confirmed=false to preview.")]
                ([Description("Chapter number")] int chapterNumber,
                 [Description("Paragraph number")] int paragraphNumber,
                 [Description("New paragraph text content")] string content,
                 [Description("Location name")] string locationName,
                 [Description("Timeline name")] string timelineName,
                 [Description("Character names")] List<string> characterNames,
                 [Description("false=preview, true=apply")] bool confirmed)
                    => _mutationService.UpdateParagraphAsync(chapterNumber, paragraphNumber, content, locationName, timelineName, characterNames, confirmed),
                "UpdateParagraph"),

            AIFunctionFactory.Create(
                [Description("Delete a paragraph and renumber remaining. Set confirmed=false to preview.")]
                ([Description("Chapter number")] int chapterNumber,
                 [Description("Paragraph number to delete")] int paragraphNumber,
                 [Description("false=preview, true=apply")] bool confirmed)
                    => _mutationService.DeleteParagraphAsync(chapterNumber, paragraphNumber, confirmed),
                "DeleteParagraph"),

            AIFunctionFactory.Create(
                [Description("Update story-level details (style, theme, synopsis). Set confirmed=false to preview.")]
                ([Description("Story style (null to keep current)")] string style,
                 [Description("Story theme (null to keep current)")] string theme,
                 [Description("Story synopsis (null to keep current)")] string synopsis,
                 [Description("false=preview, true=apply")] bool confirmed)
                    => _mutationService.UpdateStoryDetailsAsync(style, theme, synopsis, confirmed),
                "UpdateStoryDetails"),

            AIFunctionFactory.Create(
                [Description("Regenerate all embeddings for the entire story. Set confirmed=false to preview.")]
                ([Description("false=preview, true=apply")] bool confirmed)
                    => _mutationService.ReEmbedStoryAsync(confirmed),
                "ReEmbedStory"),
        ];
    }

    private async Task<string> DispatchToolCallAsync(string toolName, IDictionary<string, object> args)
    {
        args ??= new Dictionary<string, object>();

        object result;
        try
        {
            result = toolName switch
            {
                // Read Tools
                "GetCharacters" => _queryService.GetCharacters(),
                "GetLocations" => _queryService.GetLocations(),
                "GetTimelines" => _queryService.GetTimelines(),
                "GetChapters" => _queryService.GetChapters(),
                "GetParagraph" => _queryService.GetParagraph(
                    GetArg<string>(args, "chapterTitle"), GetArg<int>(args, "paragraphIndex")),
                "GetCharacterRelationships" => _queryService.GetCharacterRelationships(
                    GetArg<string>(args, "characterName")),
                "GetCharacterAppearances" => _queryService.GetCharacterAppearances(
                    GetArg<string>(args, "characterName")),
                "GetChapterCharacters" => _queryService.GetChapterCharacters(
                    GetArg<string>(args, "chapterTitle")),
                "GetLocationUsage" => _queryService.GetLocationUsage(
                    GetArg<string>(args, "locationName")),
                "GetCharacterInteractions" => _queryService.GetCharacterInteractions(
                    GetArg<string>(args, "characterName")),
                "GetTimelineChapters" => _queryService.GetTimelineChapters(
                    GetArg<string>(args, "timelineName")),
                "GetOrphanedNodes" => _queryService.GetOrphanedNodes(),
                "GetCharacterArc" => _queryService.GetCharacterArc(
                    GetArg<string>(args, "characterName")),
                "GetLocationTimeline" => _queryService.GetLocationTimeline(
                    GetArg<string>(args, "locationName")),
                "GetGraphSummary" => _queryService.GetGraphSummary(),

                // Write / Mutation Tools
                "AddCharacter" => await _mutationService.AddCharacterAsync(
                    GetArg<string>(args, "name"), GetArg<string>(args, "type"),
                    GetArg<string>(args, "description"),
                    GetArgOrNull(args, "timelineName"),
                    GetArg<bool>(args, "confirmed")),
                "UpdateCharacter" => await _mutationService.UpdateCharacterAsync(
                    GetArg<string>(args, "name"), GetArg<string>(args, "type"),
                    GetArg<string>(args, "description"),
                    GetArgOrNull(args, "timelineName"),
                    GetArg<bool>(args, "confirmed")),
                "RenameCharacter" => await _mutationService.RenameCharacterAsync(
                    GetArg<string>(args, "currentName"), GetArg<string>(args, "newName"),
                    GetArg<bool>(args, "confirmed")),
                "DeleteCharacter" => await _mutationService.DeleteCharacterAsync(
                    GetArg<string>(args, "name"), GetArg<bool>(args, "confirmed")),
                "AddLocation" => await _mutationService.AddLocationAsync(
                    GetArg<string>(args, "name"), GetArg<string>(args, "description"),
                    GetArgOrNull(args, "timelineName"),
                    GetArg<bool>(args, "confirmed")),
                "UpdateLocation" => await _mutationService.UpdateLocationAsync(
                    GetArg<string>(args, "name"), GetArg<string>(args, "description"),
                    GetArgOrNull(args, "timelineName"),
                    GetArg<bool>(args, "confirmed")),
                "RenameLocation" => await _mutationService.RenameLocationAsync(
                    GetArg<string>(args, "currentName"), GetArg<string>(args, "newName"),
                    GetArg<bool>(args, "confirmed")),
                "DeleteLocation" => await _mutationService.DeleteLocationAsync(
                    GetArg<string>(args, "name"), GetArg<bool>(args, "confirmed")),
                "AddTimeline" => await _mutationService.AddTimelineAsync(
                    GetArg<string>(args, "name"), GetArg<string>(args, "description"),
                    GetArg<DateTime>(args, "startDate"),
                    GetArgOrNull<DateTime>(args, "stopDate"),
                    GetArg<bool>(args, "confirmed")),
                "UpdateTimeline" => await _mutationService.UpdateTimelineAsync(
                    GetArg<string>(args, "name"), GetArg<string>(args, "description"),
                    GetArg<DateTime>(args, "startDate"),
                    GetArgOrNull<DateTime>(args, "stopDate"),
                    GetArg<bool>(args, "confirmed")),
                "RenameTimeline" => await _mutationService.RenameTimelineAsync(
                    GetArg<string>(args, "currentName"), GetArg<string>(args, "newName"),
                    GetArg<string>(args, "description"),
                    GetArg<DateTime>(args, "startDate"),
                    GetArgOrNull<DateTime>(args, "stopDate"),
                    GetArg<bool>(args, "confirmed")),
                "DeleteTimeline" => await _mutationService.DeleteTimelineAsync(
                    GetArg<string>(args, "name"), GetArg<bool>(args, "confirmed")),
                "AddChapter" => await _mutationService.AddChapterAsync(
                    GetArg<string>(args, "synopsis"),
                    GetArgOrNull<int>(args, "insertAtPosition"),
                    GetArg<bool>(args, "confirmed")),
                "UpdateChapter" => await _mutationService.UpdateChapterAsync(
                    GetArg<int>(args, "chapterNumber"),
                    GetArg<string>(args, "synopsis"),
                    GetArg<bool>(args, "confirmed")),
                "DeleteChapter" => await _mutationService.DeleteChapterAsync(
                    GetArg<int>(args, "chapterNumber"),
                    GetArg<bool>(args, "confirmed")),
                "AddParagraph" => await _mutationService.AddParagraphAsync(
                    GetArg<int>(args, "chapterNumber"),
                    GetArg<int>(args, "position"),
                    GetArg<string>(args, "locationName"),
                    GetArg<string>(args, "timelineName"),
                    GetArg<List<string>>(args, "characterNames"),
                    GetArg<bool>(args, "confirmed")),
                "UpdateParagraph" => await _mutationService.UpdateParagraphAsync(
                    GetArg<int>(args, "chapterNumber"),
                    GetArg<int>(args, "paragraphNumber"),
                    GetArg<string>(args, "content"),
                    GetArg<string>(args, "locationName"),
                    GetArg<string>(args, "timelineName"),
                    GetArg<List<string>>(args, "characterNames"),
                    GetArg<bool>(args, "confirmed")),
                "DeleteParagraph" => await _mutationService.DeleteParagraphAsync(
                    GetArg<int>(args, "chapterNumber"),
                    GetArg<int>(args, "paragraphNumber"),
                    GetArg<bool>(args, "confirmed")),
                "UpdateStoryDetails" => await _mutationService.UpdateStoryDetailsAsync(
                    GetArgOrNull(args, "style"),
                    GetArgOrNull(args, "theme"),
                    GetArgOrNull(args, "synopsis"),
                    GetArg<bool>(args, "confirmed")),
                "ReEmbedStory" => await _mutationService.ReEmbedStoryAsync(
                    GetArg<bool>(args, "confirmed")),

                _ => new { error = $"Unknown tool: {toolName}" }
            };
        }
        catch (Exception ex)
        {
            result = new { error = ex.Message };
        }

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false });
    }

    private static T GetArg<T>(IDictionary<string, object> args, string key)
    {
        if (!args.TryGetValue(key, out var val) || val == null)
            return default;

        if (val is T typed) return typed;

        if (val is JsonElement je)
            return JsonSerializer.Deserialize<T>(je.GetRawText());

        return (T)Convert.ChangeType(val, typeof(T));
    }

    private static T? GetArgOrNull<T>(IDictionary<string, object> args, string key) where T : struct
    {
        if (!args.TryGetValue(key, out var val) || val == null)
            return null;

        if (val is T typed) return typed;

        if (val is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Null) return null;
            return JsonSerializer.Deserialize<T>(je.GetRawText());
        }

        return (T)Convert.ChangeType(val, typeof(T));
    }

    private static string GetArgOrNull(IDictionary<string, object> args, string key)
    {
        if (!args.TryGetValue(key, out var val) || val == null)
            return null;

        if (val is string s) return s;

        if (val is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Null) return null;
            return je.GetString();
        }

        return val.ToString();
    }
}
