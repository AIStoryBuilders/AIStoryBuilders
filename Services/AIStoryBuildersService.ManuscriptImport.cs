using AIStoryBuilders.AI;
using AIStoryBuilders.Models;
using Location = AIStoryBuilders.Models.Location;

namespace AIStoryBuilders.Services;

public partial class AIStoryBuildersService
{
    /// <summary>
    /// Persists a parsed manuscript Story to the AIStoryBuilders file system
    /// in the standard CSV/folder layout, then generates embeddings and builds
    /// the Knowledge Graph.
    /// </summary>
    public async Task PersistImportedStoryAsync(Story story, OrchestratorMethods orchestrator, IGraphBuilder graphBuilder = null)
    {
        // Validate the story title is not a duplicate
        string StoryPath = $"{BasePath}/{story.Title}";
        if (Directory.Exists(StoryPath))
        {
            throw new InvalidOperationException(
                $"A story named '{story.Title}' already exists. Rename or delete it first.");
        }

        string CharactersPath = $"{StoryPath}/Characters";
        string ChaptersPath = $"{StoryPath}/Chapters";
        string LocationsPath = $"{StoryPath}/Locations";

        // Create directory tree
        CreateDirectory(StoryPath);
        CreateDirectory(CharactersPath);
        CreateDirectory(ChaptersPath);
        CreateDirectory(LocationsPath);

        // ── Write story CSV row ──
        var AIStoryBuildersStoriesPath = $"{BasePath}/AIStoryBuildersStories.csv";
        string[] AIStoryBuildersStoriesContent = ReadCSVFile(AIStoryBuildersStoriesPath);
        AIStoryBuildersStoriesContent = AIStoryBuildersStoriesContent.Where(line => line.Trim() != "").ToArray();
        AIStoryBuildersStoriesContent = AIStoryBuildersStoriesContent.Select(line => line.Trim()).ToArray();

        string cleanStyle = RemoveLineBreaks(story.Style ?? "").Replace("|", "");
        string cleanTheme = RemoveLineBreaks(story.Theme ?? "").Replace("|", "");
        string cleanSynopsis = RemoveLineBreaks(story.Synopsis ?? "").Replace("|", "");
        string cleanWorldFacts = RemoveLineBreaks(story.WorldFacts ?? "").Replace("|", "");

        string newStory = $"{AIStoryBuildersStoriesContent.Length + 1}|{story.Title}|{cleanStyle}|{cleanTheme}|{cleanSynopsis}|{cleanWorldFacts}";
        AIStoryBuildersStoriesContent = AIStoryBuildersStoriesContent.Append(newStory).ToArray();
        File.WriteAllLines(AIStoryBuildersStoriesPath, AIStoryBuildersStoriesContent);

        LogService.WriteToLog($"ManuscriptImport: Story row written for '{story.Title}'");

        // ── Write Timelines.csv ──
        TextEvent?.Invoke(this, new TextEventArgs("Writing timelines…", 3));
        List<string> TimelineContents = new List<string>();
        int timelineIdx = 0;
        foreach (var timeline in story.Timeline ?? new List<Timeline>())
        {
            string TimelineName = orchestrator.SanitizeFileName(timeline.TimelineName ?? "");
            string StartTime = (timeline.StartDate ?? DateTime.Now.AddDays(timelineIdx)).ToShortDateString()
                + " " + (timeline.StartDate ?? DateTime.Now.AddDays(timelineIdx)).ToShortTimeString();
            string StopTime = (timeline.StopDate ?? DateTime.Now.AddDays(timelineIdx + 1)).ToShortDateString()
                + " " + (timeline.StopDate ?? DateTime.Now.AddDays(timelineIdx + 1)).ToShortTimeString();
            string desc = (timeline.TimelineDescription ?? "").Replace("|", "");
            TimelineContents.Add($"{TimelineName}|{desc}|{StartTime}|{StopTime}");
            timelineIdx += 2;
        }
        string TimelinePath = $"{StoryPath}/Timelines.csv";
        File.WriteAllLines(TimelinePath, TimelineContents);

        // ── Write Character CSV files ──
        TextEvent?.Invoke(this, new TextEventArgs("Writing characters…", 3));
        foreach (var character in story.Character ?? new List<Character>())
        {
            string CharacterName = orchestrator.SanitizeFileName(character.CharacterName ?? "");
            if (string.IsNullOrWhiteSpace(CharacterName)) continue;

            string CharacterPath = $"{CharactersPath}/{CharacterName}.csv";
            List<string> CharacterContents = new List<string>();

            foreach (var bg in character.CharacterBackground ?? new List<CharacterBackground>())
            {
                string description_type = bg.Type ?? "Facts";
                string timeline_name = bg.Timeline?.TimelineName ?? "";
                string VectorDescriptionAndEmbedding =
                    await orchestrator.GetVectorEmbedding(bg.Description ?? "", true);
                CharacterContents.Add($"{description_type}|{timeline_name}|{VectorDescriptionAndEmbedding}");
            }

            File.WriteAllLines(CharacterPath, CharacterContents);
        }

        // ── Write Location CSV files ──
        TextEvent?.Invoke(this, new TextEventArgs("Writing locations…", 3));
        foreach (var location in story.Location ?? new List<Location>())
        {
            string LocationName = orchestrator.SanitizeFileName(location.LocationName ?? "");
            if (string.IsNullOrWhiteSpace(LocationName)) continue;

            string LocationPath = $"{LocationsPath}/{LocationName}.csv";
            List<string> LocationContents = new List<string>();

            foreach (var desc in location.LocationDescription ?? new List<LocationDescription>())
            {
                string VectorEmbedding = await orchestrator.GetVectorEmbedding(desc.Description ?? LocationName, false);
                var LocationDescriptionAndTimeline = $"{desc.Description ?? LocationName}|";
                LocationContents.Add($"{LocationDescriptionAndTimeline}|{VectorEmbedding}");
            }

            if (LocationContents.Count == 0)
            {
                string VectorEmbedding = await orchestrator.GetVectorEmbedding(LocationName, false);
                LocationContents.Add($"{LocationName}||{VectorEmbedding}");
            }

            File.WriteAllLines(LocationPath, LocationContents);
        }

        // ── Write Chapter folders with paragraph files ──
        TextEvent?.Invoke(this, new TextEventArgs("Writing chapters…", 3));
        int ChapterNumber = 1;
        foreach (var chapter in story.Chapter ?? new List<Chapter>())
        {
            string ChapterPath = $"{ChaptersPath}/Chapter{ChapterNumber}";
            CreateDirectory(ChapterPath);

            TextEvent?.Invoke(this, new TextEventArgs($"Writing Chapter {ChapterNumber}…", 3));

            // Write chapter synopsis file
            string chapterSynopsis = chapter.Synopsis ?? "";
            if (!string.IsNullOrWhiteSpace(chapterSynopsis))
            {
                string ChapterFilePath = $"{ChapterPath}/Chapter{ChapterNumber}.txt";
                string ChapterSynopsisAndEmbedding =
                    await orchestrator.GetVectorEmbedding(chapterSynopsis, true);
                File.WriteAllText(ChapterFilePath, ChapterSynopsisAndEmbedding);
            }

            // Write paragraph files
            int ParagraphNumber = 1;
            foreach (var paragraph in chapter.Paragraph ?? new List<Paragraph>())
            {
                string ParagraphPath = $"{ChapterPath}/Paragraph{ParagraphNumber}.txt";

                string VectorContentAndEmbedding =
                    await orchestrator.GetVectorEmbedding(paragraph.ParagraphContent ?? "", true);

                string Location = paragraph.Location?.LocationName ?? "Unknown";
                string Timeline = paragraph.Timeline?.TimelineName ?? "Unknown";

                string Characters = "[";
                if (paragraph.Characters != null && paragraph.Characters.Count > 0)
                {
                    Characters += string.Join(",", paragraph.Characters.Select(c => c.CharacterName));
                }
                Characters += "]";

                File.WriteAllText(ParagraphPath,
                    $"{Location}|{Timeline}|{Characters}|{VectorContentAndEmbedding}");

                ParagraphNumber++;
            }

            ChapterNumber++;
        }

        // ── Build and persist Knowledge Graph ──
        if (graphBuilder != null)
        {
            TextEvent?.Invoke(this, new TextEventArgs("Building Knowledge Graph…", 3));
            var fullStory = LoadFullStory(new Story { Title = story.Title });
            var graph = graphBuilder.Build(fullStory);
            await PersistGraphAsync(fullStory, graph, StoryPath);
            GraphState.Current = graph;
            GraphState.CurrentStory = fullStory;
        }

        LogService.WriteToLog($"ManuscriptImport: Story '{story.Title}' persisted successfully");
    }
}
