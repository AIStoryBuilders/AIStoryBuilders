#nullable disable
using Character = AIStoryBuildersV2.Shared.Models.Character;
using Location = AIStoryBuildersV2.Shared.Models.Location;
using Timeline = AIStoryBuildersV2.Shared.Models.Timeline;

namespace AIStoryBuildersV2.Shared.Stories;

// Timeline, location and character writers and their cascades (V1 Story.cs; V1-File-Format §3.6–§3.8, §6).
// Every free-text field is cleaned before it is written (D32), vectors are invariant and timeline dates use V1's
// current-culture short form (D30). Each method ends with MarkDirty (plan §6.2).
public partial class AIStoryBuildersService
{
    #region *** Timelines ***
    /// <summary>Appends one row. V1 wrote culture "G" dates here; V2 writes the short form V1 writes elsewhere (D30).</summary>
    public void AddTimeline(Timeline objTimeline)
    {
        var title = objTimeline.Story.Title;
        try
        {
            var path = Path.Combine(StoryPath(title), "Timelines.csv");
            var lines = File.Exists(path) ? StoryFormat.ReadNonBlankLines(path).ToList() : [];
            lines.Add(FormatTimeline(objTimeline));
            StoryFormat.WriteAllLines(path, lines);
        }
        catch (Exception ex)
        {
            LogService.WriteToLog("AddTimeline: " + ex.Message);
        }
        finally
        {
            Touched(title);
        }
    }

    /// <summary>Rewrites Timelines.csv with the edited row last. Like V1, the row keeps <paramref name="paramTimelineNameOriginal"/>.</summary>
    public void UpdateTimeline(Timeline objTimeline, string paramTimelineNameOriginal)
    {
        var title = objTimeline.Story.Title;
        try
        {
            var timelines = GetTimelines(objTimeline.Story).Where(t => t.TimelineName != paramTimelineNameOriginal).ToList();
            objTimeline.TimelineName = paramTimelineNameOriginal;
            timelines.Add(objTimeline);
            WriteTimelines(title, timelines);
        }
        catch (Exception ex)
        {
            LogService.WriteToLog("UpdateTimeline: " + ex.Message);
        }
        finally
        {
            Touched(title);
        }
    }

    /// <summary>Renames a timeline and cascades into paragraphs, location files and character files (V1-File-Format §6).</summary>
    public async Task UpdateTimelineAndTimelineNameAsync(Timeline objTimeline, string paramTimelineNameOriginal)
    {
        var title = objTimeline.Story.Title;
        try
        {
            objTimeline.TimelineName = StoryFormat.CleanField(objTimeline.TimelineName);
            var timelines = GetTimelines(objTimeline.Story).Where(t => t.TimelineName != paramTimelineNameOriginal).ToList();
            timelines.Add(objTimeline);
            WriteTimelines(title, timelines);

            await ReplaceTimelineReferencesAsync(objTimeline.Story, paramTimelineNameOriginal, objTimeline.TimelineName);
        }
        catch (Exception ex)
        {
            LogService.WriteToLog("UpdateTimelineAndTimelineNameAsync: " + ex.Message);
        }
        finally
        {
            Touched(title);
        }
    }

    /// <summary>Deletes a timeline; references to it become "".</summary>
    public async Task DeleteTimelineAndTimelineNameAsync(Timeline objTimeline, string paramTimelineNameOriginal)
    {
        var title = objTimeline.Story.Title;
        try
        {
            WriteTimelines(title, GetTimelines(objTimeline.Story).Where(t => t.TimelineName != paramTimelineNameOriginal));
            await ReplaceTimelineReferencesAsync(objTimeline.Story, paramTimelineNameOriginal, "");
        }
        catch (Exception ex)
        {
            LogService.WriteToLog("DeleteTimelineAndTimelineNameAsync: " + ex.Message);
        }
        finally
        {
            Touched(title);
        }
    }

    /// <summary>
    /// V1's cascade: matching paragraphs get the new name; every location and character file is rewritten and
    /// every record re-embedded, with matching timelines replaced.
    /// </summary>
    private async Task ReplaceTimelineReferencesAsync(Story story, string oldName, string newName)
    {
        RewriteParagraphs(story, r => r.Timeline == oldName ? r with { Timeline = newName } : null);

        foreach (var location in GetLocations(story))
        {
            foreach (var d in location.LocationDescription)
            {
                if (d.Timeline?.TimelineName == oldName) d.Timeline = new Timeline { TimelineName = newName };
            }
            await WriteLocationFileAsync(story.Title, location.LocationName, location.LocationDescription);
        }

        foreach (var character in GetCharacters(story))
        {
            foreach (var b in character.CharacterBackground)
            {
                if (b.Timeline?.TimelineName == oldName) b.Timeline = new Timeline { TimelineName = newName };
            }
            await WriteCharacterFileAsync(story.Title, character.CharacterName, character.CharacterBackground);
        }
    }

    private void WriteTimelines(string title, IEnumerable<Timeline> timelines) =>
        StoryFormat.WriteAllLines(Path.Combine(StoryPath(title), "Timelines.csv"), timelines.Select(FormatTimeline));

    private static string FormatTimeline(Timeline t) =>
        $"{StoryFormat.CleanField(t.TimelineName)}|{StoryFormat.CleanField(t.TimelineDescription)}|" +
        $"{(t.StartDate is { } start ? StoryFormat.FormatTimelineDate(start) : "")}|" +
        $"{(t.StopDate is { } stop ? StoryFormat.FormatTimelineDate(stop) : "")}";
    #endregion

    #region *** Locations ***
    public async Task AddLocationAsync(Location objLocation)
    {
        var title = objLocation.Story.Title;
        try
        {
            await WriteLocationFileAsync(title, StoryFormat.SanitizeFileName(objLocation.LocationName), objLocation.LocationDescription ?? []);
        }
        catch (Exception ex)
        {
            LogService.WriteToLog("AddLocationAsync: " + ex.Message);
        }
        finally
        {
            Touched(title);
        }
    }

    public async Task UpdateLocationDescriptions(Location objLocation)
    {
        var title = objLocation.Story.Title;
        try
        {
            await WriteLocationFileAsync(title, StoryFormat.SanitizeFileName(objLocation.LocationName), objLocation.LocationDescription ?? []);
        }
        catch (Exception ex)
        {
            LogService.WriteToLog("UpdateLocationDescriptions: " + ex.Message);
        }
        finally
        {
            Touched(title);
        }
    }

    /// <summary>Clears the location from paragraphs that use it, then deletes its file.</summary>
    public void DeleteLocation(Location objLocation)
    {
        var title = objLocation.Story.Title;
        try
        {
            var name = objLocation.LocationName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                RewriteParagraphs(objLocation.Story, r => r.Location == name ? r with { Location = "" } : null);
            }
            File.Delete(LocationFilePath(title, name));
        }
        catch (Exception ex)
        {
            LogService.WriteToLog("DeleteLocation: " + ex.Message);
        }
        finally
        {
            Touched(title);
        }
    }

    /// <summary>Renames the location in paragraphs, then moves its file. Prose is not rewritten (V1).</summary>
    public void UpdateLocationName(Location objLocation, string paramOriginalLocationName)
    {
        var title = objLocation.Story.Title;
        try
        {
            objLocation.LocationName = StoryFormat.CleanField(objLocation.LocationName).Trim();
            var newName = objLocation.LocationName;
            if (newName.Length > 0)
            {
                RewriteParagraphs(objLocation.Story, r => r.Location == paramOriginalLocationName ? r with { Location = newName } : null);
                File.Move(LocationFilePath(title, paramOriginalLocationName), LocationFilePath(title, newName));
            }
        }
        catch (Exception ex)
        {
            LogService.WriteToLog("UpdateLocationName: " + ex.Message);
        }
        finally
        {
            Touched(title);
        }
    }

    private string LocationFilePath(string title, string name) =>
        Path.Combine(StoryPath(title), "Locations", StoryFormat.SanitizeFileName(name) + ".csv");

    /// <summary>"{description}|{timeline}|{vector}" per record, each followed by a blank line (V1's UI writers).</summary>
    private async Task WriteLocationFileAsync(string title, string name, IEnumerable<LocationDescription> descriptions)
    {
        var records = new List<string>();
        foreach (var d in descriptions)
        {
            var description = StoryFormat.CleanField(d.Description);
            var vector = await OrchestratorMethods.GetVectorEmbedding(description, false);
            records.Add($"{description}|{StoryFormat.CleanField(d.Timeline?.TimelineName)}|{vector}" + StoryFormat.NewLine);
        }
        var folder = Path.Combine(StoryPath(title), "Locations");
        CreateDirectory(folder);
        StoryFormat.WriteAllLines(Path.Combine(folder, StoryFormat.SanitizeFileName(name) + ".csv"), records);
    }
    #endregion

    #region *** Characters ***
    /// <summary>Rewrites the character's whole file at the (sanitized) original name and re-embeds every entry.</summary>
    public async Task AddUpdateCharacterAsync(Character character, string paramOrginalCharacterName)
    {
        var title = character.Story.Title;
        try
        {
            await WriteCharacterFileAsync(title, paramOrginalCharacterName, character.CharacterBackground ?? []);
        }
        finally
        {
            Touched(title);
        }
    }

    /// <summary>Removes the character from every paragraph's list (every file is rewritten, as in V1), then deletes its file.</summary>
    public void DeleteCharacter(Character character, string paramOrginalCharcterName)
    {
        var title = character.Story.Title;
        try
        {
            if (!string.IsNullOrWhiteSpace(character.CharacterName))
            {
                // V1 rewrites every paragraph file, even when nothing changed.
                RewriteParagraphs(character.Story, r => r with { Characters = r.Characters.Where(c => c != paramOrginalCharcterName).ToList() });
                File.Delete(CharacterFilePath(title, paramOrginalCharcterName));
            }
        }
        finally
        {
            Touched(title);
        }
    }

    /// <summary>
    /// Renames a character in every paragraph's list and, as whole words, in the prose, re-embedding changed prose.
    /// Then moves the character file. Unlike V1, every line of a multi-line paragraph is updated.
    /// </summary>
    public async Task UpdateCharacterNameAsync(Character character, string paramOrginalCharcterName)
    {
        var title = character.Story.Title;
        try
        {
            character.CharacterName = StoryFormat.CleanField(character.CharacterName).Replace("[", "").Replace("]", "").Replace(",", "");
            var newName = character.CharacterName;
            if (newName.Trim().Length == 0) return;

            // V1 rewrites every paragraph file, even when nothing changed.
            foreach (var (file, r) in ParagraphRecordsOf(character.Story))
            {
                var characters = r.Characters.Select(c => c == paramOrginalCharcterName ? newName : c).ToList();
                var prose = ReplaceCharacterNameInProse(r.Content, paramOrginalCharcterName, newName);
                var vector = r.Vector;
                if (prose != r.Content && vector.Length > 0) vector = await OrchestratorMethods.GetVectorEmbedding(prose.Replace("|", ""), false);
                WriteParagraphRecord(file, r with { Characters = characters, Content = prose, Vector = vector });
            }

            File.Move(CharacterFilePath(title, paramOrginalCharcterName), CharacterFilePath(title, newName.Trim()));
        }
        finally
        {
            Touched(title);
        }
    }

    private static string ReplaceCharacterNameInProse(string prose, string oldName, string newName) =>
        string.IsNullOrEmpty(oldName) ? prose : Regex.Replace(prose ?? "", @"\b" + Regex.Escape(oldName) + @"\b", newName);

    private string CharacterFilePath(string title, string name) =>
        Path.Combine(StoryPath(title), "Characters", StoryFormat.SanitizeFileName(name) + ".csv");

    /// <summary>"{type}|{timeline}|{description}|{vector}" per record, each followed by a blank line (V1's UI writers).</summary>
    private async Task WriteCharacterFileAsync(string title, string name, IEnumerable<CharacterBackground> backgrounds)
    {
        var records = new List<string>();
        foreach (var b in backgrounds)
        {
            var description = StoryFormat.CleanField(b.Description);
            var withVector = await OrchestratorMethods.GetVectorEmbedding(description, true);
            records.Add($"{StoryFormat.CleanField(b.Type)}|{StoryFormat.CleanField(b.Timeline?.TimelineName)}|{withVector}" + StoryFormat.NewLine);
        }
        var folder = Path.Combine(StoryPath(title), "Characters");
        CreateDirectory(folder);
        StoryFormat.WriteAllLines(CharacterFilePath(title, name), records);
    }
    #endregion

    #region *** Paragraph rewrite helpers ***
    /// <summary>Every readable paragraph record of the story, with its file. Records with fewer than 4 fields are logged and skipped.</summary>
    private List<(string File, StoryFormat.ParagraphRecord Record)> ParagraphRecordsOf(Story story)
    {
        var result = new List<(string, StoryFormat.ParagraphRecord)>();
        foreach (var chapter in GetChapters(story))
        {
            foreach (var (number, file) in ParagraphFiles(chapter))
            {
                if (StoryFormat.ParseParagraph(File.ReadAllText(file)) is { } record) result.Add((file, record));
                else LogService.WriteToLog($"Skipped 'Paragraph{number}.txt' in {ChapterFolderName(chapter)} (fewer than 4 fields)");
            }
        }
        return result;
    }

    /// <summary>Writes a paragraph the way V1's cascades (WriteAllLines) do: one record and a trailing CRLF.</summary>
    private static void WriteParagraphRecord(string file, StoryFormat.ParagraphRecord r) =>
        StoryFormat.WriteAllText(file, StoryFormat.FormatParagraph(r.Location, r.Timeline, r.Characters, r.Content, r.Vector) + StoryFormat.NewLine);

    /// <summary>Applies <paramref name="change"/> to every paragraph; a null result leaves the file alone.</summary>
    private void RewriteParagraphs(Story story, Func<StoryFormat.ParagraphRecord, StoryFormat.ParagraphRecord> change)
    {
        foreach (var (file, record) in ParagraphRecordsOf(story))
        {
            if (change(record) is { } updated) WriteParagraphRecord(file, updated);
        }
    }
    #endregion
}
