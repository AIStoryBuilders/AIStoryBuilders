#nullable disable
using AIStoryBuildersV2.Shared.Graph;

namespace AIStoryBuildersV2.Shared.Stories;

// Re-embedding, Re-load Story (F7) and the story graph's entry points (T8). Unlike V1, multi-line paragraphs keep every
// line when they are re-embedded, and character and location files keep their records (V1-File-Format §6, §7.4).
public partial class AIStoryBuildersService
{
    /// <summary>
    /// V1's Re-load Story: saves the story row, re-embeds every paragraph, chapter, character and location, rebuilds and
    /// persists the graph, and reloads the story's entities.
    /// </summary>
    public async Task ReloadStoryAsync(Story story, IProgress<string> progress = null)
    {
        using var _ = StoryGraphContext.Use(story.Title);
        try
        {
            progress?.Report("Step 1/5: Saving all story files…");
            StepText(progress, "Saving story metadata…", 2);
            await SaveStoryRowAsync(story);

            await ReEmbedFilesAsync(story, progress);

            progress?.Report("Step 4/5: Rebuilding Knowledge Graph…");
            StepText(progress, "Rebuilding Knowledge Graph…", 3);
            var fullStory = await LoadFullStoryAsync(new Story { Title = story.Title });
            var graph = _graphBuilder.Build(fullStory);
            await PersistGraphAsync(fullStory, graph, StoryPath(story.Title));
            var entry = _graphCache.Get(story.Title);
            entry.Current = graph;
            entry.CurrentStory = fullStory;
            entry.IsDirty = false;

            progress?.Report("Step 5/5: Reloading story data…");
            StepText(progress, "Reloading story data…", 2);
            story.Character = fullStory.Character;
            story.Location = fullStory.Location;
            story.Timeline = fullStory.Timeline;
            story.Chapter = fullStory.Chapter;

            progress?.Report("Re-load complete!");
            StepText(progress, "Re-load complete — all embeddings regenerated, Knowledge Graph rebuilt.", 5);
        }
        finally
        {
            _persistence.MarkDirty(story.Title);
        }
    }

    /// <summary>V1's ReEmbedStory (used by the chat tool): regenerates every vector of the story.</summary>
    public async Task ReEmbedStory(Story story)
    {
        try
        {
            await ReEmbedFilesAsync(story, null);
            RaiseText("Re-embedding complete.", 5);
        }
        finally
        {
            _persistence.MarkDirty(story.Title);
        }
    }

    // V1 toasted every Re-load step. V2 shows them only in the progress dialog when there is one (UI spec §10.2).
    private void StepText(IProgress<string> progress, string text, int displayLength)
    {
        if (progress is null) RaiseText(text, displayLength);
    }

    private async Task ReEmbedFilesAsync(Story story, IProgress<string> progress)
    {
        var meta = new Story { Title = story.Title };

        progress?.Report("Step 2/5: Re-embedding paragraphs…");
        var chapters = GetChapters(meta);
        var paragraphFiles = chapters.SelectMany(c => ParagraphFiles(c).Values).ToList();
        var processed = 0;
        foreach (var file in paragraphFiles)
        {
            processed++;
            StepText(progress, $"Re-embedding paragraph {processed}/{paragraphFiles.Count}…", 1);
            var record = StoryFormat.ParseParagraph(File.ReadAllText(file));
            if (record is null || string.IsNullOrWhiteSpace(record.Content)) continue;
            var content = record.Content.Replace("|", "");
            var vector = await OrchestratorMethods.GetVectorEmbedding(content, false);
            StoryFormat.WriteAllText(file, StoryFormat.FormatParagraph(record.Location, record.Timeline, record.Characters, content, vector));
        }

        progress?.Report("Step 3/5: Re-embedding chapters, characters, locations…");
        foreach (var chapter in chapters)
        {
            StepText(progress, "Re-embedding chapter…", 1);
            await WriteChapterFileAsync(story.Title, ChapterFolderName(chapter), string.IsNullOrEmpty(chapter.Synopsis) ? " " : chapter.Synopsis);
        }
        foreach (var character in GetCharacters(meta))
        {
            StepText(progress, "Re-embedding character…", 1);
            await WriteCharacterFileAsync(story.Title, character.CharacterName, character.CharacterBackground);
        }
        foreach (var location in GetLocations(meta))
        {
            StepText(progress, "Re-embedding location…", 1);
            await WriteLocationFileAsync(story.Title, location.LocationName, location.LocationDescription);
        }
    }

    /// <summary>
    /// Makes sure the story's graph is in <see cref="StoryGraphCache"/>: loads Graph/graph.json when it exists, otherwise
    /// builds and persists it. A cached entry marked dirty is rebuilt (D17).
    /// </summary>
    public async Task EnsureGraphExistsAsync(string storyTitle)
    {
        using var _ = StoryGraphContext.Use(storyTitle);
        var entry = _graphCache.Get(storyTitle);
        if (entry.Current is not null && entry.CurrentStory is not null && !entry.IsDirty) return;

        var wrote = false;
        try
        {
            var storyMeta = (await GetStorysAsync()).FirstOrDefault(s => StoryTitle.SameTitle(s.Title, storyTitle));
            if (storyMeta == null)
            {
                LogService.WriteToLog($"EnsureGraphExistsAsync: Story '{storyTitle}' not found in stories list");
                return;
            }

            var fullStory = await LoadFullStoryAsync(storyMeta);
            var existing = entry.IsDirty && entry.Current is not null ? null : LoadGraphFromDisk(StoryPath(storyTitle));
            if (existing is null)
            {
                existing = _graphBuilder.Build(fullStory);
                await PersistGraphAsync(fullStory, existing, StoryPath(storyTitle));
                wrote = true;
                LogService.WriteToLog($"EnsureGraphExistsAsync: Graph built and persisted for '{storyTitle}'");
            }

            entry.Current = existing;
            entry.CurrentStory = fullStory;
            entry.IsDirty = false;
        }
        finally
        {
            if (wrote) _persistence.MarkDirty(storyTitle);
        }
    }

    /// <summary>Marks the story's cached graph stale; the next chat turn or Generate rebuilds it.</summary>
    public void MarkGraphDirty(string storyTitle) => _graphCache.MarkDirty(storyTitle);
}
