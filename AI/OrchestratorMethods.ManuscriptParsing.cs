using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using AIStoryBuilders.Services;

namespace AIStoryBuilders.AI;

/// <summary>
/// OrchestratorMethods partial class containing AI prompt methods
/// for the manuscript import pipeline.
/// </summary>
public partial class OrchestratorMethods
{
    // ═══════════════════════════════════════════════════════
    //  Chapter boundary detection
    // ═══════════════════════════════════════════════════════

    public async Task<List<ParsedChapter>> ParseChaptersFromTextAsync(string rawText)
    {
        LogService.WriteToLog("ManuscriptParsing: ParseChaptersFromTextAsync - Start");

        var client = CreateOpenAIClient();

        var systemPrompt = """
            You are a professional manuscript editor. Your task is to identify chapter boundaries in the following raw manuscript text.
            Instructions:
            - Ignore any front matter such as table of contents, dedications, epigraphs, acknowledgments.
            - Identify chapter boundaries from headings, numbering, or structural cues.
            - If no chapter headings are found, return a JSON with an empty chapters array.
            - For each chapter, return the heading text EXACTLY as it appears.
            - Also provide a clean display title.
            Output ONLY a valid JSON object. No commentary, no markdown fences.
            The JSON must match this exact schema:
            {
              "chapters": [
                {
                  "index": <int>,
                  "title": "<clean display title>",
                  "headingText": "<exact heading from manuscript>"
                }
              ]
            }
            """;

        var userPrompt = $"Identify chapter boundaries in the following manuscript text:\n\n{rawText}";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt)
        };

        var options = ChatOptionsFactory.CreateJsonOptions(SettingsService.AIType, SettingsService.AIModel);

        var result = await LlmCallHelper.CallLlmWithRetry(
            client, messages, options,
            jObj =>
            {
                var chapters = new List<ParsedChapter>();
                var chaptersArray = jObj["chapters"] as JArray;
                if (chaptersArray != null)
                {
                    foreach (var ch in chaptersArray)
                    {
                        chapters.Add(new ParsedChapter
                        {
                            Index = ch["index"]?.Value<int>() ?? 0,
                            Title = ch["title"]?.Value<string>() ?? "Chapter",
                        });
                    }
                }
                return Tuple.Create(chapters, jObj);
            },
            LogService);

        if (result == null || result.Item1.Count == 0)
        {
            return new List<ParsedChapter>
            {
                new() { Index = 1, Title = "Chapter 1", RawText = rawText.Trim() }
            };
        }

        // Split text by boundaries using heading text from the LLM
        return SplitTextByBoundaries(rawText, result.Item2);
    }

    private static List<ParsedChapter> SplitTextByBoundaries(string rawText, JObject jObj)
    {
        var chaptersArray = jObj["chapters"] as JArray;
        if (chaptersArray == null || chaptersArray.Count == 0)
            return new List<ParsedChapter> { new() { Index = 1, Title = "Chapter 1", RawText = rawText.Trim() } };

        var positions = new List<(int position, string title, string headingText)>();

        foreach (var ch in chaptersArray)
        {
            var headingText = ch["headingText"]?.Value<string>() ?? "";
            var title = ch["title"]?.Value<string>() ?? "Chapter";

            if (string.IsNullOrWhiteSpace(headingText))
                continue;

            var pos = rawText.IndexOf(headingText.Trim(), StringComparison.OrdinalIgnoreCase);

            if (pos < 0)
            {
                // Try normalized whitespace
                var normalizedText = Regex.Replace(rawText, @"\s+", " ");
                var normalizedHeading = Regex.Replace(headingText.Trim(), @"\s+", " ");
                var normalizedPos = normalizedText.IndexOf(normalizedHeading, StringComparison.OrdinalIgnoreCase);
                if (normalizedPos >= 0)
                {
                    // Approximate mapping back
                    pos = FindOriginalPosition(rawText, normalizedPos, headingText.Trim());
                }
            }

            if (pos >= 0 && !positions.Any(p => Math.Abs(p.position - pos) < 5))
            {
                positions.Add((pos, title, headingText));
            }
        }

        if (positions.Count == 0)
            return new List<ParsedChapter> { new() { Index = 1, Title = "Chapter 1", RawText = rawText.Trim() } };

        positions.Sort((a, b) => a.position.CompareTo(b.position));

        var chapters = new List<ParsedChapter>();
        for (int i = 0; i < positions.Count; i++)
        {
            var start = positions[i].position;
            var end = i + 1 < positions.Count ? positions[i + 1].position : rawText.Length;
            var chapterText = rawText[start..end].Trim();

            chapters.Add(new ParsedChapter
            {
                Index = i + 1,
                Title = positions[i].title,
                RawText = chapterText
            });
        }

        return chapters.Count > 0 ? chapters
            : new List<ParsedChapter> { new() { Index = 1, Title = "Chapter 1", RawText = rawText.Trim() } };
    }

    private static int FindOriginalPosition(string original, int normalizedPos, string heading)
    {
        int origIdx = 0;
        int normIdx = 0;
        bool prevWasSpace = false;

        while (origIdx < original.Length && normIdx < normalizedPos)
        {
            if (char.IsWhiteSpace(original[origIdx]))
            {
                if (!prevWasSpace) normIdx++;
                prevWasSpace = true;
            }
            else
            {
                normIdx++;
                prevWasSpace = false;
            }
            origIdx++;
        }

        var searchStart = Math.Max(0, origIdx - 20);
        var searchEnd = Math.Min(original.Length, origIdx + heading.Length + 20);
        var region = original[searchStart..searchEnd];
        var found = region.IndexOf(heading, StringComparison.OrdinalIgnoreCase);
        return found >= 0 ? searchStart + found : origIdx;
    }

    // ═══════════════════════════════════════════════════════
    //  Chapter summarization
    // ═══════════════════════════════════════════════════════

    public async Task<string> SummarizeChapterAsync(string chapterText)
    {
        LogService.WriteToLog("ManuscriptParsing: SummarizeChapterAsync - Start");

        var client = CreateOpenAIClient();

        var systemPrompt = """
            You are a narrative analysis assistant. Write a comprehensive summary of this chapter. Include:
            - Key events that advance the plot
            - Character decisions, revelations, or arcs
            - Changes in situation, stakes, or setting
            - Important dialogue or confrontations
            - Emotional beats and tonal shifts
            Be thorough — this summary will be used to extract characters, locations, timelines, and story beats.
            Return ONLY the summary text (no JSON, no markdown fences).
            """;

        var preview = chapterText.Length > 8000 ? chapterText[..8000] + "..." : chapterText;
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, preview)
        };

        var options = new ChatOptions { ModelId = SettingsService.AIModel };

        return await LlmCallHelper.CallLlmForText(client, messages, options, LogService);
    }

    // ═══════════════════════════════════════════════════════
    //  Beats extraction
    // ═══════════════════════════════════════════════════════

    public async Task<string> ExtractBeatsAsync(string chapterSummary)
    {
        LogService.WriteToLog("ManuscriptParsing: ExtractBeatsAsync - Start");

        var client = CreateOpenAIClient();

        var systemPrompt = """
            You are a narrative analysis assistant. Based on this chapter summary, identify the narrative beats.
            Return the beats on a SINGLE LINE using EXACTLY this format:
            #Beat 1 - <description> #Beat 2 - <description> #Beat 3 - <description>
            Rules:
            - Each beat starts with #Beat N (numbered sequentially starting at 1) followed by ' - ' and a one-sentence description.
            - Separate beats with a single space.
            - Everything must be on ONE line (no line breaks).
            - Do NOT use markdown fences, JSON, bullet points, or any other formatting.
            - Do NOT include a pipe character '|' anywhere.
            """;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, chapterSummary)
        };

        var options = new ChatOptions { ModelId = SettingsService.AIModel };

        return await LlmCallHelper.CallLlmForText(client, messages, options, LogService);
    }

    // ═══════════════════════════════════════════════════════
    //  Character extraction from summary
    // ═══════════════════════════════════════════════════════

    public async Task<List<ParsedCharacterInfo>> ExtractCharactersFromSummaryAsync(
        string chapterSummary, string chapterTitle)
    {
        LogService.WriteToLog("ManuscriptParsing: ExtractCharactersFromSummaryAsync - Start");

        var client = CreateOpenAIClient();

        var systemPrompt = $$$"""
            You are a narrative analysis assistant. Extract named characters from this chapter summary.
            Return ONLY a JSON object with this schema (no markdown fences, no explanation):
            {
              "characters": [
                {
                  "name": "<character's full name>",
                  "backstory": "<1-2 sentence description>",
                  "backgrounds": [
                    {
                      "backgroundType": "Appearance"|"Goals"|"History"|"Aliases"|"Facts",
                      "timelineName": "{{{chapterTitle}}}",
                      "description": "<the background detail>"
                    }
                  ]
                }
              ]
            }
            Exclude pronouns, generic nouns, and common words that happen to be capitalized.
            """;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, chapterSummary)
        };

        var options = ChatOptionsFactory.CreateJsonOptions(SettingsService.AIType, SettingsService.AIModel);

        var result = await LlmCallHelper.CallLlmWithRetry(
            client, messages, options,
            jObj =>
            {
                var characters = new List<ParsedCharacterInfo>();
                var arr = jObj["characters"] as JArray;
                if (arr != null)
                {
                    foreach (var ch in arr)
                    {
                        var info = new ParsedCharacterInfo
                        {
                            Name = ch["name"]?.Value<string>() ?? "",
                            Backstory = ch["backstory"]?.Value<string>() ?? ""
                        };

                        var bgs = ch["backgrounds"] as JArray;
                        if (bgs != null)
                        {
                            foreach (var bg in bgs)
                            {
                                info.Backgrounds.Add(new ParsedBackground
                                {
                                    BackgroundType = bg["backgroundType"]?.Value<string>() ?? "Facts",
                                    TimelineName = bg["timelineName"]?.Value<string>() ?? chapterTitle,
                                    Description = bg["description"]?.Value<string>() ?? ""
                                });
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(info.Name))
                            characters.Add(info);
                    }
                }
                return characters;
            },
            LogService);

        return result ?? new List<ParsedCharacterInfo>();
    }

    // ═══════════════════════════════════════════════════════
    //  Location extraction from summary
    // ═══════════════════════════════════════════════════════

    public async Task<List<ParsedLocationInfo>> ExtractLocationsFromSummaryAsync(
        string chapterSummary, string chapterTitle)
    {
        LogService.WriteToLog("ManuscriptParsing: ExtractLocationsFromSummaryAsync - Start");

        var client = CreateOpenAIClient();

        var systemPrompt = """
            You are a narrative analysis assistant. Extract named or clearly described locations from this chapter summary.
            Identify named places, specific rooms/areas, and outdoor locations.
            Exclude vague references and generic terms.
            Output ONLY a valid JSON object. No commentary, no markdown fences.
            The JSON must match this exact schema:
            {
              "locations": [
                {
                  "name": "<location name>",
                  "description": "<brief physical or contextual description>"
                }
              ]
            }
            """;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, chapterSummary)
        };

        var options = ChatOptionsFactory.CreateJsonOptions(SettingsService.AIType, SettingsService.AIModel);

        var result = await LlmCallHelper.CallLlmWithRetry(
            client, messages, options,
            jObj =>
            {
                var locations = new List<ParsedLocationInfo>();
                var arr = jObj["locations"] as JArray;
                if (arr != null)
                {
                    foreach (var loc in arr)
                    {
                        var name = loc["name"]?.Value<string>() ?? "";
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            locations.Add(new ParsedLocationInfo
                            {
                                Name = name,
                                Description = loc["description"]?.Value<string>() ?? ""
                            });
                        }
                    }
                }
                return locations;
            },
            LogService);

        return result ?? new List<ParsedLocationInfo>();
    }

    // ═══════════════════════════════════════════════════════
    //  Timeline extraction from summary
    // ═══════════════════════════════════════════════════════

    public async Task<List<ParsedTimelineInfo>> ExtractTimelinesFromSummaryAsync(
        string chapterSummary, string chapterTitle, int chapterIndex)
    {
        LogService.WriteToLog("ManuscriptParsing: ExtractTimelinesFromSummaryAsync - Start");

        var client = CreateOpenAIClient();

        var systemPrompt = """
            You are a narrative analysis assistant. Based on this chapter summary, identify timeline events.
            Consider chronological order, character involvement, plot beats, and temporal markers.
            Output ONLY a valid JSON object. No commentary, no markdown fences.
            The JSON must match this exact schema:
            {
              "timelines": [
                {
                  "name": "<timeline name, e.g. 'Main Plot', 'Backstory'>",
                  "description": "<what this timeline represents>"
                }
              ]
            }
            """;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, chapterSummary)
        };

        var options = ChatOptionsFactory.CreateJsonOptions(SettingsService.AIType, SettingsService.AIModel);

        var result = await LlmCallHelper.CallLlmWithRetry(
            client, messages, options,
            jObj =>
            {
                var timelines = new List<ParsedTimelineInfo>();
                var arr = jObj["timelines"] as JArray;
                if (arr != null)
                {
                    foreach (var tl in arr)
                    {
                        var name = tl["name"]?.Value<string>() ?? "";
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            timelines.Add(new ParsedTimelineInfo
                            {
                                Name = name,
                                Description = tl["description"]?.Value<string>() ?? ""
                            });
                        }
                    }
                }
                return timelines;
            },
            LogService);

        return result ?? new List<ParsedTimelineInfo>();
    }

    // ═══════════════════════════════════════════════════════
    //  Paragraph entity association
    // ═══════════════════════════════════════════════════════

    public async Task<List<ParsedParagraph>> AssociateParagraphEntitiesAsync(
        List<ParsedParagraph> paragraphs,
        List<ParsedCharacterInfo> characters,
        List<ParsedLocationInfo> locations,
        List<ParsedTimelineInfo> timelines)
    {
        LogService.WriteToLog("ManuscriptParsing: AssociateParagraphEntitiesAsync - Start");

        var client = CreateOpenAIClient();

        var sb = new StringBuilder();
        sb.AppendLine("You are a literary analyst. Annotate the following paragraphs with contextual metadata.");
        sb.AppendLine("For each paragraph (by index), determine:");
        sb.AppendLine("- location: which of the known locations (exact name, or \"Unknown\")");
        sb.AppendLine("- timeline: which of the known timelines (exact name, or \"Unknown\")");
        sb.AppendLine("- characters: which known characters appear or speak");
        sb.AppendLine();
        sb.AppendLine("Known locations: " + string.Join(", ", locations.Select(l => l.Name)));
        sb.AppendLine("Known timelines: " + string.Join(", ", timelines.Select(t => t.Name)));
        sb.AppendLine("Known characters: " + string.Join(", ", characters.Select(c => c.Name)));
        sb.AppendLine();
        sb.AppendLine("Output ONLY a valid JSON object. No commentary, no markdown fences.");
        sb.AppendLine("""
            {
              "paragraphs": [
                {
                  "index": <1-based int>,
                  "location": "<location name or 'Unknown'>",
                  "timeline": "<timeline name or 'Unknown'>",
                  "characters": ["<character name>"]
                }
              ]
            }
            """);

        var userSb = new StringBuilder();
        foreach (var p in paragraphs)
        {
            userSb.AppendLine($"[Paragraph {p.Index}]");
            // Limit each paragraph to prevent token overflow
            var preview = p.Text.Length > 500 ? p.Text[..500] + "..." : p.Text;
            userSb.AppendLine(preview);
            userSb.AppendLine();
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, sb.ToString()),
            new(ChatRole.User, userSb.ToString())
        };

        var options = ChatOptionsFactory.CreateJsonOptions(SettingsService.AIType, SettingsService.AIModel);

        var result = await LlmCallHelper.CallLlmWithRetry(
            client, messages, options,
            jObj =>
            {
                var arr = jObj["paragraphs"] as JArray;
                if (arr != null)
                {
                    foreach (var annotation in arr)
                    {
                        var index = annotation["index"]?.Value<int>() ?? 0;
                        var para = paragraphs.FirstOrDefault(p => p.Index == index);
                        if (para != null)
                        {
                            para.Location = annotation["location"]?.Value<string>() ?? "Unknown";
                            para.Timeline = annotation["timeline"]?.Value<string>() ?? "Unknown";
                            var chars = annotation["characters"] as JArray;
                            if (chars != null)
                                para.Characters = chars.Select(c => c.Value<string>() ?? "").Where(c => c != "").ToList();
                        }
                    }
                }
                return paragraphs;
            },
            LogService);

        return result ?? paragraphs;
    }

    // ═══════════════════════════════════════════════════════
    //  Long paragraph splitting
    // ═══════════════════════════════════════════════════════

    public async Task<List<string>> SplitLongParagraphAsync(string text)
    {
        LogService.WriteToLog("ManuscriptParsing: SplitLongParagraphAsync - Start");

        var client = CreateOpenAIClient();

        var systemPrompt = """
            You are a professional manuscript editor.
            The following block of text should be multiple paragraphs
            but was extracted as a single block (likely due to formatting loss).
            Split it into logical paragraphs. Each paragraph should:
            - Contain a coherent thought, action, or piece of dialogue.
            - Be roughly 50 to 300 words (but prioritize logical breaks over word count).
            - Preserve every word of the original text exactly (do not add, remove, or rephrase).
            Output ONLY a valid JSON object. No commentary, no markdown fences.
            The JSON must match this exact schema:
            {
              "paragraphs": [
                "<paragraph 1 text>",
                "<paragraph 2 text>"
              ]
            }
            """;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, text)
        };

        var options = ChatOptionsFactory.CreateJsonOptions(SettingsService.AIType, SettingsService.AIModel);

        var result = await LlmCallHelper.CallLlmWithRetry(
            client, messages, options,
            jObj =>
            {
                var arr = jObj["paragraphs"] as JArray;
                if (arr != null && arr.Count > 1)
                {
                    return arr.Select(p => p.Value<string>() ?? "").Where(p => p != "").ToList();
                }
                return (List<string>)null;
            },
            LogService);

        if (result != null && result.Count > 1)
            return result;

        // Fallback heuristic
        return HeuristicSplitText(text, 250);
    }

    private static List<string> HeuristicSplitText(string text, int targetWordsPerParagraph)
    {
        var sentences = Regex.Split(text, @"(?<=[.!?])\s+");
        var paragraphs = new List<string>();
        var current = new StringBuilder();
        int wordCount = 0;

        foreach (var sentence in sentences)
        {
            var sentenceWords = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount + sentenceWords > targetWordsPerParagraph && current.Length > 0)
            {
                paragraphs.Add(current.ToString().Trim());
                current.Clear();
                wordCount = 0;
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(sentence);
            wordCount += sentenceWords;
        }

        if (current.Length > 0)
            paragraphs.Add(current.ToString().Trim());

        return paragraphs;
    }
}
