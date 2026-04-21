using Microsoft.Extensions.AI;

namespace AIStoryBuilders.AI;

/// <summary>
/// Central service that builds typed ChatMessage lists for each AI operation.
/// Replaces the inline string-concatenation pattern in each OrchestratorMethods partial class.
/// </summary>
public class PromptTemplateService
{
    public static class Templates
    {
        // WriteParagraph
        public const string WriteParagraph_System =
            """
            You are a function that produces JSON containing the contents of a single paragraph for a novel.
            Output ONLY a valid JSON object. No commentary, no markdown fences.
            The JSON must match this exact schema:
            { "paragraph_content": "<string>" }
            """;

        public const string WriteParagraph_User =
            """
            <story_title>{StoryTitle}</story_title>
            <story_style>{StoryStyle}</story_style>
            <story_synopsis>{StorySynopsis}</story_synopsis>
            <system_directions>{SystemMessage}</system_directions>
            <world_facts>{WorldFacts}</world_facts>
            <timeline_summary>{TimelineSummary}</timeline_summary>
            <current_chapter>{CurrentChapter}</current_chapter>
            <previous_paragraphs>{PreviousParagraphs}</previous_paragraphs>
            <current_location>{CurrentLocation}</current_location>
            <characters>{CharacterList}</characters>
            <related_paragraphs>{RelatedParagraphs}</related_paragraphs>
            <current_paragraph>{CurrentParagraph}</current_paragraph>
            <instructions>{Instructions}</instructions>
            <constraints>
            - Only use information provided. Do not use any information not provided.
            - Write in the writing style of the provided content.
            - Insert a line break before dialogue when a character speaks for the first time.
            - Produce a single paragraph of {NumberOfWords} words maximum.
            - The <timeline_summary> describes what has happened so far in the current timeline. Do not contradict these facts.
            - Do not reference events from other timelines unless they are explicitly mentioned in the provided context.
            </constraints>
            """;

        // ParseNewStory
        public const string ParseNewStory_System =
            """
            You are a function that analyzes story text and extracts structured data as JSON.
            Output ONLY a valid JSON object. No commentary, no markdown fences.
            The JSON must match this exact schema:
            {
              "locations": [{ "name": "<string>", "description": "<string>" }],
              "timelines": [{ "name": "<string>", "description": "<string>" }],
              "characters": [{
                "name": "<string>",
                "descriptions": [{
                  "description_type": "Appearance|Goals|History|Aliases|Facts",
                  "description": "<string>",
                  "timeline_name": "<string>"
                }]
              }]
            }
            """;

        public const string ParseNewStory_User =
            """
            Given a story titled:
            <story_title>{StoryTitle}</story_title>

            With the story text:
            <story_text>{StoryText}</story_text>

            Using only this information, identify:
            1. Locations mentioned in the story with a short description of each.
            2. A short timeline name and description for each chronological event.
            3. Characters present and their descriptions (Appearance, Goals, History, Aliases, Facts)
               with the timeline_name from timelines.
            """;

        // CreateNewChapters
        public const string CreateNewChapters_System =
            """
            You are a function that creates chapter outlines for a novel as JSON.
            Output ONLY a valid JSON object. No commentary, no markdown fences.
            The JSON must match this exact schema:
            {
              "chapter": [{
                "chapter_name": "<string>",
                "chapter_synopsis": "<string with #Beat N markers>",
                "paragraphs": [{
                  "contents": "<string>",
                  "location_name": "<string>",
                  "timeline_name": "<string>",
                  "character_names": ["<string>"]
                }]
              }]
            }
            """;

        public const string CreateNewChapters_User =
            """
            Given a story with the following structure:
            <story_json>{StoryJSON}</story_json>

            Using only this information:
            1. Create {ChapterCount} chapters named Chapter1, Chapter2, etc.
            2. Each chapter gets a chapter_synopsis formatted as story beats: #Beat 1 - ..., #Beat 2 - ..., etc.
            3. Each chapter gets a short (~200 word) first paragraph.
            4. Each paragraph has a single timeline_name and a list of character_names.
            """;

        // DetectCharacters
        public const string DetectCharacters_System =
            """
            You are a function that identifies character names in a paragraph of text.
            Output ONLY a valid JSON object. No commentary, no markdown fences.
            The JSON must match this exact schema:
            { "characters": [{ "name": "<string>" }] }
            """;

        public const string DetectCharacters_User =
            """
            Identify all characters, by name, mentioned in the following paragraph:
            <paragraph>{ParagraphContent}</paragraph>
            """;

        // DetectCharacterAttributes
        public const string DetectCharacterAttributes_System =
            """
            You are a function that detects NEW character descriptions not already present.
            Output ONLY a valid JSON object. No commentary, no markdown fences.
            The JSON must match this exact schema:
            {
              "characters": [{
                "name": "<string>",
                "descriptions": [{
                  "description_type": "Appearance|Goals|History|Aliases|Facts",
                  "description": "<string>"
                }]
              }]
            }
            Rules:
            - Only output characters present in the provided character list.
            - Only output descriptions NOT already present for that character.
            - Output each character at most once.
            """;

        public const string DetectCharacterAttributes_User =
            """
            <paragraph>{ParagraphContent}</paragraph>
            <existing_characters>{CharacterJSON}</existing_characters>

            Identify any NEW descriptions for the characters above that appear in the paragraph
            but are not already listed in their existing descriptions.
            """;

        // GetStoryBeats
        public const string GetStoryBeats_System =
            """
            You are a function that extracts story beats from a paragraph.
            Output ONLY the story beats as plain text, one per line.
            Do not wrap in JSON. Do not add commentary.
            """;

        public const string GetStoryBeats_User =
            """
            Create story beats for the following paragraph:
            <paragraph>{ParagraphContent}</paragraph>
            """;
    }

    /// <summary>
    /// Build a ChatMessage list from a template, hydrating placeholders with values.
    /// </summary>
    public List<ChatMessage> BuildMessages(
        string systemTemplate,
        string userTemplate,
        Dictionary<string, string> values)
    {
        var system = HydratePlaceholders(systemTemplate, values);
        var user = HydratePlaceholders(userTemplate, values);

        return new List<ChatMessage>
        {
            new(ChatRole.System, system),
            new(ChatRole.User, user)
        };
    }

    private static string HydratePlaceholders(string template, Dictionary<string, string> values)
    {
        var result = template;
        foreach (var (key, value) in values)
        {
            result = result.Replace($"{{{key}}}", value ?? "");
        }
        return result;
    }
}
