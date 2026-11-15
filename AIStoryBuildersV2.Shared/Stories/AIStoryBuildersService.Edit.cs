#nullable disable
using AIStoryBuildersV2.Shared.Models.JSON;
using Character = AIStoryBuildersV2.Shared.Models.Character;

namespace AIStoryBuildersV2.Shared.Stories;

// Story, chapter and paragraph writers and the renumbering of V1-File-Format §4.4. Each method in the §6.2 MarkDirty
// list marks the story dirty once, after its last write.
public partial class AIStoryBuildersService
{
    #region *** Story ***
    /// <summary>
    /// Creates a story with AI (F2): the model parses the synopsis into characters, locations and timelines, then
    /// writes the chapters. The files are written by <see cref="WriteNewStoryAsync"/>.
    /// </summary>
    public async Task AddStory(Story story, string GPTModelId)
    {
        var parsedStoryJson = await OrchestratorMethods.ParseNewStory(story.Title, story.Synopsis, GPTModelId);
        var parsedStory = ParseJSONNewStory(parsedStoryJson);

        var chaptersResponse = await OrchestratorMethods.CreateNewChapters(parsedStoryJson, story.ChapterCount, GPTModelId);
        var parsedChapters = ParseJSONNewChapters(GetOnlyJSON(chaptersResponse.Text));
        if (parsedChapters.chapter == null || parsedChapters.chapter.Length == 0)
        {
            // Deterministic JSON repair (V1 replaced an LLM-based clean-up with it).
            parsedChapters = ParseJSONNewChapters(GetOnlyJSON(AI.JsonRepairUtility.ExtractAndRepair(chaptersResponse.Text)));
        }

        await WriteNewStoryAsync(story, parsedStory, parsedChapters);
    }

    /// <summary>The file-writing half of V1's AddStory, with D32 cleaning and V1's "]" bug fixed.</summary>
    public async Task WriteNewStoryAsync(Story story, JSONStory parsedStory, JSONChapters parsedChapters)
    {
        var storyPath = StoryPath(story.Title);
        var charactersPath = Path.Combine(storyPath, "Characters");
        var chaptersPath = Path.Combine(storyPath, "Chapters");
        var locationsPath = Path.Combine(storyPath, "Locations");
        try
        {
            CreateDirectory(storyPath);
            CreateDirectory(charactersPath);
            CreateDirectory(chaptersPath);
            CreateDirectory(locationsPath);

            await _library.UpsertAsync(ToIndexEntry(story));
            LogService.WriteToLog($"Story created {story.Title}");

            RaiseText("Create the Character files", 5);
            foreach (var character in parsedStory.characters ?? [])
            {
                if (character?.name == null) continue;
                var records = new List<string>();
                foreach (var description in character.descriptions ?? [])
                {
                    if (description == null) continue;
                    var text = StoryFormat.CleanField(description.description);
                    var withVector = await OrchestratorMethods.GetVectorEmbedding(text, true);
                    records.Add($"{StoryFormat.CleanField(description.description_type)}|{StoryFormat.CleanField(description.timeline_name)}|{withVector}" + StoryFormat.NewLine);
                }
                StoryFormat.WriteAllLines(Path.Combine(charactersPath, StoryFormat.SanitizeFileName(character.name) + ".csv"), records);
            }

            RaiseText("Create the Location files", 5);
            foreach (var location in parsedStory.locations ?? [])
            {
                if (location?.name == null) continue;
                var records = new List<string>();
                // Like V1, a location without descriptions gets one record that uses its name.
                foreach (var description in location.descriptions ?? [location.name])
                {
                    var text = StoryFormat.CleanField(description);
                    var vector = await OrchestratorMethods.GetVectorEmbedding(text, false);
                    records.Add($"{text}||{vector}" + StoryFormat.NewLine);
                }
                StoryFormat.WriteAllLines(Path.Combine(locationsPath, StoryFormat.SanitizeFileName(location.name) + ".csv"), records);
            }

            RaiseText("Create the Timeline file", 5);
            var timelineLines = new List<string>();
            var i = 0;
            foreach (var timeline in parsedStory.timelines ?? [])
            {
                if (timeline?.name == null) continue;
                var start = DateTime.Now.AddDays(i);
                timelineLines.Add($"{StoryFormat.CleanField(StoryFormat.SanitizeFileName(timeline.name))}|{StoryFormat.CleanField(timeline.description)}|" +
                                  $"{StoryFormat.FormatTimelineDate(start)}|{StoryFormat.FormatTimelineDate(start.AddDays(1))}");
                i += 2;
            }
            StoryFormat.WriteAllLines(Path.Combine(storyPath, "Timelines.csv"), timelineLines);

            var chapterNumber = 1;
            foreach (var chapter in parsedChapters.chapter ?? [])
            {
                var chapterPath = Path.Combine(chaptersPath, "Chapter" + chapterNumber);
                CreateDirectory(chapterPath);
                RaiseText($"Create Chapter {chapterNumber}", 5);

                // V1 skips the synopsis file when the model gave none; V2 always writes one, because a chapter
                // folder without its file hides every chapter from V1 (V1-File-Format §7.5).
                var synopsis = StoryFormat.CleanField(chapter?.chapter_synopsis ?? " ");
                StoryFormat.WriteAllText(Path.Combine(chapterPath, $"Chapter{chapterNumber}.txt"), await OrchestratorMethods.GetVectorEmbedding(synopsis, true));

                var first = chapter?.paragraphs?.FirstOrDefault();
                if (first != null)
                {
                    var location = (first.location_name ?? "").Split(',')[0];
                    var timeline = (first.timeline_name ?? "").Split(',')[0];
                    var content = StoryFormat.CleanContent(first.contents);
                    var vector = await OrchestratorMethods.GetVectorEmbedding(StoryFormat.NormalizeLineBreaks(content), false);
                    StoryFormat.WriteAllText(Path.Combine(chapterPath, "Paragraph1.txt"),
                        StoryFormat.FormatParagraph(location, timeline, first.character_names, first.contents, vector));
                }

                chapterNumber++;
            }
        }
        finally
        {
            Touched(story.Title);
        }
    }

    /// <summary>V1's UpdateStory, async over the library store (D25). Free text loses line breaks and '|'.</summary>
    public async Task UpdateStoryAsync(Story story)
    {
        try
        {
            await SaveStoryRowAsync(story);
        }
        finally
        {
            Touched(story.Title);
        }
    }

    private Task SaveStoryRowAsync(Story story)
    {
        story.Style = StoryFormat.CleanField(story.Style);
        story.Theme = StoryFormat.CleanField(story.Theme);
        story.Synopsis = StoryFormat.CleanField(story.Synopsis);
        story.WorldFacts = StoryFormat.CleanField(story.WorldFacts);
        return _library.UpsertAsync(ToIndexEntry(story));
    }

    /// <summary>Removes the library entry, then the stored copy (WASM), then the folder (D25).</summary>
    public async Task DeleteStoryAsync(string StoryTitle)
    {
        await _library.RemoveAsync(StoryTitle);
        await _persistence.DeleteAsync(StoryTitle);
        await ForceDeleteDirectoryAsync(StoryPath(StoryTitle));
        LogService.WriteToLog($"Story deleted {StoryTitle}");
    }

    private static StoryIndexEntry ToIndexEntry(Story story) =>
        new("0", story.Title, story.Style ?? "", story.Theme ?? "", story.Synopsis ?? "", story.WorldFacts ?? "", DateTimeOffset.UtcNow);

    /// <summary>V1's delete with OneDrive retries; the retries wait with Task.Delay instead of Thread.Sleep.</summary>
    private static async Task ForceDeleteDirectoryAsync(string path)
    {
        if (!Directory.Exists(path)) return;

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).ToList())
        {
            File.SetAttributes(file, FileAttributes.Normal);
            await RetryDeleteAsync(() => File.Delete(file));
        }
        foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToList())
        {
            ClearReadOnly(dir);
            await RetryDeleteAsync(() => Directory.Delete(dir, false));
        }
        ClearReadOnly(path);
        await RetryDeleteAsync(() => Directory.Delete(path, false));
    }

    /// <summary>OneDrive can mark folders read-only, and Directory.Delete refuses those with access denied.</summary>
    private static void ClearReadOnly(string dir)
    {
        var info = new DirectoryInfo(dir);
        if (info.Attributes.HasFlag(FileAttributes.ReadOnly)) info.Attributes &= ~FileAttributes.ReadOnly;
    }

    private static async Task RetryDeleteAsync(Action deleteAction, int maxRetries = 5)
    {
        for (var i = 0; ; i++)
        {
            try
            {
                deleteAction();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && i < maxRetries - 1)
            {
                await Task.Delay(200 * (i + 1));
            }
        }
    }

    /// <summary>Deletes a folder without waiting (used for a single chapter).</summary>
    private static void DeleteDirectoryNow(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, recursive: true);
    }
    #endregion

    #region *** Chapter ***
    /// <summary>Appends a chapter folder named <paramref name="ChapterName"/> (for example "Chapter4").</summary>
    public async Task AddChapterAsync(Chapter objChapter, string ChapterName)
    {
        var title = objChapter.Story.Title;
        try
        {
            await WriteChapterFileAsync(title, ChapterName, objChapter.Synopsis ?? " ");
        }
        finally
        {
            Touched(title);
        }
    }

    /// <summary>Creates Chapter{N} for a chapter named "Chapter N", after <see cref="RestructureChapters"/> made room.</summary>
    public async Task InsertChapterAsync(Chapter objChapter)
    {
        var title = objChapter.Story.Title;
        try
        {
            await WriteChapterFileAsync(title, ChapterFolderName(objChapter), objChapter.Synopsis ?? " ");
        }
        finally
        {
            Touched(title);
        }
    }

    /// <summary>Rewrites the chapter's synopsis and its vector.</summary>
    public async Task UpdateChapterAsync(Chapter objChapter)
    {
        var title = objChapter.Story.Title;
        try
        {
            await WriteChapterFileAsync(title, ChapterFolderName(objChapter), objChapter.Synopsis ?? " ");
        }
        finally
        {
            Touched(title);
        }
    }

    /// <summary>Deletes the chapter folder and its paragraphs. The caller renumbers with <see cref="RestructureChapters"/>.</summary>
    public void DeleteChapter(Chapter objChapter)
    {
        var title = objChapter.Story.Title;
        try
        {
            DeleteDirectoryNow(ChapterPath(objChapter));
        }
        catch (Exception ex)
        {
            LogService.WriteToLog("DeleteChapter: " + ex.Message);
        }
        finally
        {
            Touched(title);
        }
    }

    /// <summary>"{synopsis}|{vector}" with no trailing newline (V1-File-Format §3.9).</summary>
    private async Task WriteChapterFileAsync(string title, string folderName, string synopsis)
    {
        var folder = Path.Combine(StoryPath(title), "Chapters", folderName);
        CreateDirectory(folder);
        var text = StoryFormat.CleanField(synopsis);
        StoryFormat.WriteAllText(Path.Combine(folder, folderName + ".txt"), await OrchestratorMethods.GetVectorEmbedding(text, true));
    }

    /// <summary>
    /// Renumbers chapter folders (V1-File-Format §4.4). Add: Chapter{N} → Chapter{N+1} for every N ≥ the chapter's
    /// sequence, highest first, to make room. Delete: every N above the deleted chapter moves down by one.
    /// Each folder's ChapterN.txt is renamed before the folder, as in V1.
    /// </summary>
    public void RestructureChapters(Chapter objChapter, RestructureType RestructureType)
    {
        var title = objChapter.Story.Title;
        try
        {
            var chaptersPath = Path.Combine(StoryPath(title), "Chapters");
            var numbers = Directory.Exists(chaptersPath)
                ? Directory.GetDirectories(chaptersPath).Select(d => StoryFormat.ChapterNumber(Path.GetFileName(d))).OfType<int>().ToList()
                : [];

            var moves = RestructureType == RestructureType.Add
                ? numbers.Where(n => n >= objChapter.Sequence).OrderByDescending(n => n).Select(n => (From: n, To: n + 1))
                : numbers.Where(n => n > objChapter.Sequence).OrderBy(n => n).Select(n => (From: n, To: n - 1));

            foreach (var (from, to) in moves.ToList())
            {
                var oldFolder = Path.Combine(chaptersPath, "Chapter" + from);
                var oldFile = Path.Combine(oldFolder, $"Chapter{from}.txt");
                if (File.Exists(oldFile)) File.Move(oldFile, Path.Combine(oldFolder, $"Chapter{to}.txt"));
                Directory.Move(oldFolder, Path.Combine(chaptersPath, "Chapter" + to));
            }
        }
        catch (Exception ex)
        {
            LogService.WriteToLog("RestructureChapters: " + ex.Message);
        }
        finally
        {
            Touched(title);
        }
    }
    #endregion

    #region *** Paragraph ***
    /// <summary>Makes room at the paragraph's sequence, then writes an empty paragraph: "{loc}|{tl}|[chars]||".</summary>
    public void AddParagraph(Chapter chapter, Paragraph Paragraph)
    {
        var title = chapter.Story.Title;
        try
        {
            RenumberParagraphs(chapter, Paragraph.Sequence, RestructureType.Add);
            StoryFormat.WriteAllText(Path.Combine(ChapterPath(chapter), $"Paragraph{Paragraph.Sequence}.txt"),
                StoryFormat.FormatParagraph(Paragraph.Location?.LocationName, Paragraph.Timeline?.TimelineName,
                    Paragraph.Characters?.Select(c => c.CharacterName), "", ""));
        }
        catch (Exception ex)
        {
            LogService.WriteToLog("AddParagraph: " + ex.Message);
        }
        finally
        {
            Touched(title);
        }
    }

    /// <summary>Writes the paragraph with a fresh vector. Content line breaks are written as CRLF; no trailing newline.</summary>
    public async Task UpdateParagraph(Chapter chapter, Paragraph Paragraph)
    {
        var title = chapter.Story.Title;
        try
        {
            var content = StoryFormat.NormalizeLineBreaks((Paragraph.ParagraphContent ?? "").Replace("|", ""));
            var vector = await OrchestratorMethods.GetVectorEmbedding(content, false);
            var folder = ChapterPath(chapter);
            CreateDirectory(folder);
            StoryFormat.WriteAllText(Path.Combine(folder, $"Paragraph{Paragraph.Sequence}.txt"),
                StoryFormat.FormatParagraph(Paragraph.Location?.LocationName, Paragraph.Timeline?.TimelineName,
                    Paragraph.Characters?.Select(c => c.CharacterName), content, vector));
        }
        catch (Exception ex)
        {
            LogService.WriteToLog("UpdateParagraph: " + ex.Message);
        }
        finally
        {
            Touched(title);
        }
    }

    /// <summary>Deletes the paragraph's file and moves the later ones down by one.</summary>
    public void DeleteParagraph(Chapter chapter, Paragraph Paragraph)
    {
        var title = chapter.Story.Title;
        try
        {
            File.Delete(Path.Combine(ChapterPath(chapter), $"Paragraph{Paragraph.Sequence}.txt"));
            RenumberParagraphs(chapter, Paragraph.Sequence, RestructureType.Delete);
        }
        catch (Exception ex)
        {
            LogService.WriteToLog("DeleteParagraph: " + ex.Message);
        }
        finally
        {
            Touched(title);
        }
    }

    /// <summary>V1's public renumbering entry point (V1-File-Format §4.4).</summary>
    public void RestructureParagraphs(Chapter objChapter, int ParagraphNumber, RestructureType RestructureType)
    {
        try
        {
            RenumberParagraphs(objChapter, ParagraphNumber, RestructureType);
        }
        catch (Exception ex)
        {
            LogService.WriteToLog("RestructureParagraphs: " + ex.Message);
        }
        finally
        {
            Touched(objChapter.Story.Title);
        }
    }

    /// <summary>Add: Paragraph{M} → Paragraph{M+1} for M ≥ the number, highest first. Delete: every M above it moves down.</summary>
    private void RenumberParagraphs(Chapter chapter, int number, RestructureType type)
    {
        var files = ParagraphFiles(chapter);
        var folder = ChapterPath(chapter);
        var moves = type == RestructureType.Add
            ? files.Keys.Where(m => m >= number).OrderByDescending(m => m).Select(m => (From: m, To: m + 1))
            : files.Keys.Where(m => m > number).OrderBy(m => m).Select(m => (From: m, To: m - 1));
        foreach (var (from, to) in moves.ToList())
        {
            File.Move(files[from], Path.Combine(folder, $"Paragraph{to}.txt"));
        }
    }

    public List<Paragraph> AddParagraphIndenting(List<Paragraph> paramParagraphs) => paramParagraphs;

    /// <summary>V1 stored HTML indenting in the UI copy; V2 indents with CSS, so this only undoes V1-style markup.</summary>
    public Paragraph RemoveParagraphIndenting(Paragraph paramParagraph) => new()
    {
        ParagraphContent = (paramParagraph.ParagraphContent ?? "").Replace("&nbsp;", "").Replace("<br />", "\n"),
        Chapter = paramParagraph.Chapter,
        Characters = paramParagraph.Characters,
        Location = paramParagraph.Location,
        Sequence = paramParagraph.Sequence,
        Timeline = paramParagraph.Timeline,
        Id = paramParagraph.Id,
    };
    #endregion
}
