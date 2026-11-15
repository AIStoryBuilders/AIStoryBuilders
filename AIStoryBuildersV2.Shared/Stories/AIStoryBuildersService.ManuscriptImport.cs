#nullable disable
using AIStoryBuildersV2.Shared.Graph;
using Character = AIStoryBuildersV2.Shared.Models.Character;
using Location = AIStoryBuildersV2.Shared.Models.Location;
using Timeline = AIStoryBuildersV2.Shared.Models.Timeline;

namespace AIStoryBuildersV2.Shared.Stories;

// Manuscript import (F4): writes a parsed story in V1's layout, then embeds it and builds the graph.
public partial class AIStoryBuildersService
{
    /// <summary>
    /// The story title for an imported file: the file name without its extension, with characters D29 forbids removed.
    /// </summary>
    public static string TitleFromFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName ?? "");
        name = new string(name.Where(c => !"/\\:*?\"<>|#".Contains(c) && !char.IsControl(c)).ToArray()).Replace("..", ".");
        name = AI.OrchestratorMethods.TrimInnerSpaces(name).Trim(' ', '.');
        return name.Length > StoryTitle.MaxLength ? name[..StoryTitle.MaxLength].Trim(' ', '.') : name;
    }

    /// <summary>
    /// V1's PersistImportedStoryAsync. Unknown locations and timelines are written as "Unknown" (V1); every chapter gets its
    /// ChapterN.txt; free text is cleaned (D32). The graph is built and persisted at the end.
    /// </summary>
    /// <exception cref="InvalidOperationException">The title is invalid or a story with it already exists.</exception>
    public async Task PersistImportedStoryAsync(Story story)
    {
        if (StoryTitle.Validate(story.Title) is { } invalid) throw new InvalidOperationException(invalid);
        if (await StoryExistsAsync(story.Title))
            throw new InvalidOperationException($"A story named '{story.Title}' already exists. Rename or delete it first.");

        var storyPath = StoryPath(story.Title);
        var charactersPath = Path.Combine(storyPath, "Characters");
        var chaptersPath = Path.Combine(storyPath, "Chapters");
        var locationsPath = Path.Combine(storyPath, "Locations");
        try
        {
            if (Directory.Exists(storyPath)) await ForceDeleteDirectoryAsync(storyPath);
            CreateDirectory(storyPath);
            CreateDirectory(charactersPath);
            CreateDirectory(chaptersPath);
            CreateDirectory(locationsPath);

            await SaveStoryRowAsync(story);
            LogService.WriteToLog($"ManuscriptImport: Story row written for '{story.Title}'");

            RaiseText("Writing timelines…", 3);
            var timelineLines = new List<string>();
            var timelineIdx = 0;
            foreach (var timeline in story.Timeline ?? new List<Timeline>())
            {
                var start = timeline.StartDate ?? DateTime.Now.AddDays(timelineIdx);
                var stop = timeline.StopDate ?? DateTime.Now.AddDays(timelineIdx + 1);
                timelineLines.Add($"{StoryFormat.CleanField(StoryFormat.SanitizeFileName(timeline.TimelineName ?? ""))}|{StoryFormat.CleanField(timeline.TimelineDescription)}|" +
                                  $"{StoryFormat.FormatTimelineDate(start)}|{StoryFormat.FormatTimelineDate(stop)}");
                timelineIdx += 2;
            }
            StoryFormat.WriteAllLines(Path.Combine(storyPath, "Timelines.csv"), timelineLines);

            RaiseText("Writing characters…", 3);
            foreach (var character in story.Character ?? new List<Character>())
            {
                var name = StoryFormat.SanitizeFileName(character.CharacterName ?? "");
                if (string.IsNullOrWhiteSpace(name)) continue;
                var records = new List<string>();
                foreach (var bg in character.CharacterBackground ?? new List<CharacterBackground>())
                {
                    var description = StoryFormat.CleanField(bg.Description);
                    records.Add($"{StoryFormat.CleanField(bg.Type ?? "Facts")}|{StoryFormat.CleanField(bg.Timeline?.TimelineName)}|{await OrchestratorMethods.GetVectorEmbedding(description, true)}");
                }
                StoryFormat.WriteAllLines(Path.Combine(charactersPath, name + ".csv"), records);
            }

            RaiseText("Writing locations…", 3);
            foreach (var location in story.Location ?? new List<Location>())
            {
                var name = StoryFormat.SanitizeFileName(location.LocationName ?? "");
                if (string.IsNullOrWhiteSpace(name)) continue;
                var records = new List<string>();
                var descriptions = (location.LocationDescription ?? []).Select(d => d.Description ?? name).DefaultIfEmpty(name);
                foreach (var d in descriptions)
                {
                    var description = StoryFormat.CleanField(d);
                    records.Add($"{description}||{await OrchestratorMethods.GetVectorEmbedding(description, false)}");
                }
                StoryFormat.WriteAllLines(Path.Combine(locationsPath, name + ".csv"), records);
            }

            RaiseText("Writing chapters…", 3);
            var chapterNumber = 1;
            foreach (var chapter in story.Chapter ?? new List<Chapter>())
            {
                var chapterPath = Path.Combine(chaptersPath, "Chapter" + chapterNumber);
                CreateDirectory(chapterPath);
                RaiseText($"Writing Chapter {chapterNumber}…", 3);

                var synopsis = StoryFormat.CleanField(string.IsNullOrWhiteSpace(chapter.Synopsis) ? " " : chapter.Synopsis);
                StoryFormat.WriteAllText(Path.Combine(chapterPath, $"Chapter{chapterNumber}.txt"), await OrchestratorMethods.GetVectorEmbedding(synopsis, true));

                var paragraphNumber = 1;
                foreach (var paragraph in chapter.Paragraph ?? new List<Paragraph>())
                {
                    var content = StoryFormat.NormalizeLineBreaks((paragraph.ParagraphContent ?? "").Replace("|", ""));
                    var vector = await OrchestratorMethods.GetVectorEmbedding(content, false);
                    StoryFormat.WriteAllText(Path.Combine(chapterPath, $"Paragraph{paragraphNumber}.txt"),
                        StoryFormat.FormatParagraph(paragraph.Location?.LocationName ?? "Unknown", paragraph.Timeline?.TimelineName ?? "Unknown",
                            paragraph.Characters?.Select(c => c.CharacterName), content, vector));
                    paragraphNumber++;
                }
                chapterNumber++;
            }

            RaiseText("Building Knowledge Graph…", 3);
            using (StoryGraphContext.Use(story.Title))
            {
                var fullStory = await LoadFullStoryAsync(new Story { Title = story.Title });
                var graph = _graphBuilder.Build(fullStory);
                await PersistGraphAsync(fullStory, graph, storyPath);
                var entry = _graphCache.Get(story.Title);
                entry.Current = graph;
                entry.CurrentStory = fullStory;
                entry.IsDirty = false;
            }

            LogService.WriteToLog($"ManuscriptImport: Story '{story.Title}' persisted successfully");
        }
        finally
        {
            _persistence.MarkDirty(story.Title);
        }
    }
}
