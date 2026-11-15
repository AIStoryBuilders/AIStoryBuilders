#nullable disable
using AIStoryBuildersV2.Shared.Models.JSON;
using Character = AIStoryBuildersV2.Shared.Models.Character;
using Location = AIStoryBuildersV2.Shared.Models.Location;
using Timeline = AIStoryBuildersV2.Shared.Models.Timeline;

namespace AIStoryBuildersV2.Shared.Stories;

// Readers. Unlike V1, a bad record is skipped (and logged) instead of emptying the whole collection (D32).
public partial class AIStoryBuildersService
{
    #region *** Story ***
    /// <summary>V1's GetStorys, now async over <see cref="IStoryLibraryStore"/> (D25). Rows keep file order.</summary>
    public async Task<List<Story>> GetStorysAsync()
    {
        try
        {
            var entries = await _library.ListAsync();
            return entries.Select((e, i) => new Story
            {
                Id = int.TryParse(e.Id, out var id) ? id : i + 1,
                Title = e.Title,
                Style = e.Style,
                Theme = e.Theme,
                Synopsis = e.Synopsis,
                WorldFacts = e.WorldFacts ?? "",
            }).ToList();
        }
        catch (Exception ex)
        {
            LogService.WriteToLog("GetStorys: " + ex.Message);
            return [];
        }
    }

    /// <summary>Whether a story with this title exists (case-insensitive). Never Directory.Exists (D5, D25).</summary>
    public async Task<bool> StoryExistsAsync(string title) =>
        (await _library.ListAsync()).Any(e => StoryTitle.SameTitle(e.Title, title));
    #endregion

    #region *** Timelines ***
    public List<Timeline> GetTimelines(Story story)
    {
        var timelines = new List<Timeline>();
        var path = Path.Combine(StoryPath(story.Title), "Timelines.csv");
        if (!File.Exists(path)) return timelines;

        try
        {
            var lines = StoryFormat.ReadNonBlankLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var f = lines[i].Split('|');
                if (f.Length < 3 || !StoryFormat.TryParseDate(f[2], out var start))
                {
                    LogService.WriteToLog($"GetTimelines: skipped line {i + 1} of '{story.Title}/Timelines.csv'");
                    continue;
                }

                timelines.Add(new Timeline
                {
                    Id = i + 1,
                    TimelineName = f[0],
                    TimelineDescription = f[1],
                    StartDate = start,
                    StopDate = f.Length > 3 && StoryFormat.TryParseDate(f[3], out var stop) ? stop : null,
                });
            }
        }
        catch (Exception ex)
        {
            LogService.WriteToLog("GetTimelines: " + ex.Message);
        }

        return timelines.OrderBy(x => x.StartDate).ToList();
    }
    #endregion

    #region *** Locations ***
    public List<Location> GetLocations(Story story)
    {
        var locations = new List<Location>();
        var folder = Path.Combine(StoryPath(story.Title), "Locations");
        if (!Directory.Exists(folder)) return locations;

        var i = 1;
        foreach (var file in EnumerateEntityFiles(folder))
        {
            try
            {
                var location = new Location
                {
                    Id = i++,
                    LocationName = Path.GetFileNameWithoutExtension(file),
                    LocationDescription = [],
                };

                var ii = 1;
                foreach (var line in StoryFormat.ReadNonBlankLines(file))
                {
                    var f = line.Split('|');
                    if (f.Length < 2)
                    {
                        LogService.WriteToLog($"GetLocations: skipped a record without '|' in '{location.LocationName}'");
                        continue;
                    }

                    location.LocationDescription.Add(new LocationDescription
                    {
                        Id = ii++,
                        Description = StoryFormat.LooksLikeVector(f[0]) ? "" : f[0],
                        Timeline = new Timeline { TimelineName = f[1] },
                    });
                }

                locations.Add(location);
            }
            catch (Exception ex)
            {
                LogService.WriteToLog("GetLocations: " + ex.Message);
            }
        }

        return locations;
    }

    /// <summary>Case-insensitive match against the location file names (V1).</summary>
    public bool LocationExists(Location objLocation)
    {
        try
        {
            var folder = Path.Combine(StoryPath(objLocation.Story.Title), "Locations");
            if (!Directory.Exists(folder)) return false;
            return EnumerateEntityFiles(folder)
                .Any(f => string.Equals(Path.GetFileNameWithoutExtension(f), objLocation.LocationName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            LogService.WriteToLog("LocationExists: " + ex.Message);
            return true;
        }
    }
    #endregion

    #region *** Characters ***
    public List<Character> GetCharacters(Story story)
    {
        var characters = new List<Character>();
        var folder = Path.Combine(StoryPath(story.Title), "Characters");
        if (!Directory.Exists(folder)) return characters;

        var i = 1;
        foreach (var file in EnumerateEntityFiles(folder))
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var backgrounds = new List<CharacterBackground>();
                var ii = 1;
                foreach (var line in StoryFormat.ReadNonBlankLines(file))
                {
                    var f = line.Split('|');
                    var background = new CharacterBackground
                    {
                        Id = ii,
                        Sequence = ii,
                        Type = f.Length > 0 ? f[0] : "",
                        Timeline = new Timeline { TimelineName = f.Length > 1 ? f[1] : "" },
                        Description = f.Length > 2 ? f[2] : "",
                        VectorContent = f.Length > 3 ? f[3] : "",
                        Character = new Character { CharacterName = name },
                    };

                    // A vector that ended up in the description field (V1's guard).
                    if (StoryFormat.LooksLikeVector(background.Description))
                    {
                        background.VectorContent = background.Description;
                        background.Description = "";
                    }

                    backgrounds.Add(background);
                    ii++;
                }

                characters.Add(new Character { Id = i++, CharacterName = name, CharacterBackground = backgrounds });
            }
            catch (Exception ex)
            {
                LogService.WriteToLog("GetCharacters: " + ex.Message);
            }
        }

        return characters;
    }
    #endregion

    #region *** Chapter ***
    public List<Chapter> GetChapters(Story story)
    {
        var chapters = new List<Chapter>();
        var folder = Path.Combine(StoryPath(story.Title), "Chapters");
        if (!Directory.Exists(folder)) return chapters;

        foreach (var chapterFolder in Directory.GetDirectories(folder).OrderBy(x => x, StringComparer.Ordinal))
        {
            var folderName = Path.GetFileName(chapterFolder);
            if (StoryFormat.ChapterNumber(folderName) is not { } number)
            {
                LogService.WriteToLog($"GetChapters: ignored folder '{folderName}' in '{story.Title}'");
                continue;
            }

            var synopsis = "";
            var file = Path.Combine(chapterFolder, folderName + ".txt");
            try
            {
                if (File.Exists(file))
                {
                    synopsis = StoryFormat.ReadNonBlankLines(file).Select(l => l.Split('|')[0]).FirstOrDefault() ?? "";
                    if (StoryFormat.LooksLikeVector(synopsis)) synopsis = "";
                }
                else
                {
                    LogService.WriteToLog($"GetChapters: '{folderName}.txt' is missing in '{story.Title}'");
                }
            }
            catch (Exception ex)
            {
                LogService.WriteToLog("GetChapters: " + ex.Message);
            }

            chapters.Add(new Chapter
            {
                ChapterName = "Chapter " + number,
                Sequence = number,
                Synopsis = synopsis,
                Story = story,
            });
        }

        return chapters.OrderBy(x => x.Sequence).ToList();
    }

    public int CountChapters(Story story)
    {
        var folder = Path.Combine(StoryPath(story.Title), "Chapters");
        return Directory.Exists(folder) ? Directory.GetDirectories(folder).Length : 0;
    }
    #endregion

    #region *** Paragraph ***
    public List<Paragraph> GetParagraphs(Chapter chapter)
    {
        var paragraphs = new List<Paragraph>();
        foreach (var (number, record) in ReadParagraphRecords(chapter))
        {
            paragraphs.Add(new Paragraph
            {
                Sequence = number,
                Location = new Location { LocationName = record.Location },
                Timeline = new Timeline { TimelineName = record.Timeline },
                Characters = record.Characters.Select(n => new Character { CharacterName = n }).ToList(),
                ParagraphContent = record.Content,
                Chapter = chapter,
            });
        }
        return paragraphs;
    }

    public List<AIParagraph> GetParagraphVectors(Chapter chapter, string TimelineName) => GetParagraphVectorsInternal(chapter, TimelineName);

    public List<AIParagraph> GetAllParagraphVectors(Chapter chapter) => GetParagraphVectorsInternal(chapter, null);

    /// <summary>The paragraphs of a chapter that have a valid 384-number vector (multi-line paragraphs included, unlike V1).</summary>
    public List<ParagraphVectorEntry> GetAllParagraphVectorsForChapter(Chapter chapter)
    {
        var entries = new List<ParagraphVectorEntry>();
        foreach (var (number, record) in ReadParagraphRecords(chapter))
        {
            if (StoryFormat.ParseVector(record.Vector) is { } vector)
            {
                entries.Add(new ParagraphVectorEntry($"Ch{chapter.Sequence}_P{number}", record.Content, vector, record.Timeline));
            }
        }
        return entries;
    }

    private List<AIParagraph> GetParagraphVectorsInternal(Chapter chapter, string TimelineName) =>
        ReadParagraphRecords(chapter)
            .Where(p => TimelineName == null || TimelineName == p.Record.Timeline)
            .Select(p => new AIParagraph
            {
                sequence = p.Number,
                location_name = p.Record.Location,
                timeline_name = p.Record.Timeline,
                character_names = [.. p.Record.Characters],
                contents = p.Record.Content,
                vectors = p.Record.Vector,
            })
            .ToList();

    public int CountParagraphs(Chapter chapter) => ParagraphFiles(chapter).Count;

    /// <summary>Paragraph{M}.txt files of a chapter by M. Other names (such as "Paragraph1 - Copy.txt") are logged and ignored.</summary>
    private SortedDictionary<int, string> ParagraphFiles(Chapter chapter)
    {
        var files = new SortedDictionary<int, string>();
        var folder = ChapterPath(chapter);
        if (!Directory.Exists(folder)) return files;

        foreach (var file in Directory.GetFiles(folder, "Paragraph*.txt", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (StoryFormat.ParagraphNumber(name) is { } number) files[number] = file;
            else LogService.WriteToLog($"GetParagraphs: ignored '{name}.txt' in '{chapter.Story?.Title}/{ChapterFolderName(chapter)}'");
        }
        return files;
    }

    private List<(int Number, StoryFormat.ParagraphRecord Record)> ReadParagraphRecords(Chapter chapter)
    {
        var records = new List<(int, StoryFormat.ParagraphRecord)>();
        foreach (var (number, file) in ParagraphFiles(chapter))
        {
            try
            {
                if (StoryFormat.ParseParagraph(File.ReadAllText(file)) is { } record) records.Add((number, record));
                else LogService.WriteToLog($"GetParagraphs: skipped 'Paragraph{number}.txt' (fewer than 4 fields)");
            }
            catch (Exception ex)
            {
                LogService.WriteToLog("GetParagraphs: " + ex.Message);
            }
        }
        return records;
    }
    #endregion

    /// <summary>The *.csv files of Characters/ or Locations/, including subfolders as in V1, sorted by name.</summary>
    private static IEnumerable<string> EnumerateEntityFiles(string folder) =>
        Directory.GetFiles(folder, "*.csv", SearchOption.AllDirectories)
            .OrderBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase);
}
