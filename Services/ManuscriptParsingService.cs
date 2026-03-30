using AIStoryBuilders.AI;
using AIStoryBuilders.Model;
using AIStoryBuilders.Models;
using DocumentFormat.OpenXml.Packaging;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using Location = AIStoryBuilders.Models.Location;

namespace AIStoryBuilders.Services;

/// <summary>
/// Four-phase pipeline that imports a .docx, .pdf, .txt, or .md manuscript
/// and converts it into a fully structured <see cref="Story"/> object.
/// </summary>
public class ManuscriptParsingService
{
    private readonly OrchestratorMethods _orchestrator;
    private readonly LogService _logService;

    private static readonly string WorkingDir =
        Path.Combine(Path.GetTempPath(), "AIStoryBuilders", "_ManuscriptImport");

    public ManuscriptParsingService(
        OrchestratorMethods orchestrator,
        LogService logService)
    {
        _orchestrator = orchestrator;
        _logService = logService;
    }

    // ═══════════════════════════════════════════════════════
    //  Main Parse Pipeline
    // ═══════════════════════════════════════════════════════

    public async Task<Story> ParseManuscriptAsync(
        string filePath,
        IProgress<int> progress,
        IProgress<string> statusProgress)
    {
        progress.Report(0);

        // Initialize working directory
        statusProgress.Report("Preparing working directory…");
        InitWorkingDirectory();

        try
        {
            // ══════════════════════════════════════════════════
            //  Phase 1: Text Extraction (0→5%)
            // ══════════════════════════════════════════════════
            statusProgress.Report("Extracting text from file…");
            _logService.WriteToLog("ManuscriptImport Phase 1: Extracting text…");

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            string rawText = extension switch
            {
                ".docx" => ExtractDocx(filePath),
                ".pdf" => ExtractPdf(filePath),
                ".txt" or ".md" => await File.ReadAllTextAsync(filePath),
                _ => throw new NotSupportedException($"Unsupported file format: {extension}")
            };

            // Sanitise
            var (cleanedText, _) = TextSanitiser.Sanitise(rawText);
            rawText = cleanedText;

            _logService.WriteToLog($"ManuscriptImport: Extracted {rawText.Length:N0} characters");
            progress.Report(5);

            // ══════════════════════════════════════════════════
            //  Phase 2: Chapter & Paragraph Splitting (5→20%)
            // ══════════════════════════════════════════════════
            statusProgress.Report("Identifying chapter boundaries…");
            _logService.WriteToLog("ManuscriptImport Phase 2: Chapter splitting…");

            var sentences = SplitIntoSentences(rawText);
            var chunks = ChunkSentences(sentences, 100);

            // Internal chapter model for parsing
            var allChapters = new List<ParsedChapter>();

            for (int i = 0; i < chunks.Count; i++)
            {
                statusProgress.Report($"Parsing chapters — chunk {i + 1} of {chunks.Count}…");

                List<ParsedChapter> chunkChapters;
                try
                {
                    chunkChapters = await _orchestrator.ParseChaptersFromTextAsync(chunks[i]);
                }
                catch (Exception ex)
                {
                    _logService.WriteToLog($"ManuscriptImport: Chapter parse failed chunk {i + 1}: {ex.Message}");
                    // Fallback: treat entire chunk as continuation
                    chunkChapters = new List<ParsedChapter>
                    {
                        new() { Title = "Chapter 1", RawText = chunks[i] }
                    };
                }

                // Merge continuations across chunk boundaries
                if (allChapters.Count > 0 && chunkChapters.Count > 0)
                {
                    var lastExisting = allChapters[^1];
                    var firstNew = chunkChapters[0];

                    bool titlesMatch = string.Equals(lastExisting.Title, firstNew.Title, StringComparison.OrdinalIgnoreCase);
                    bool isFallbackChunk = chunkChapters.Count == 1
                        && (firstNew.Title == "Chapter 1" || firstNew.Title == lastExisting.Title);

                    if (titlesMatch || isFallbackChunk)
                    {
                        lastExisting.RawText += "\n" + firstNew.RawText;
                        chunkChapters.RemoveAt(0);
                    }
                }

                foreach (var ch in chunkChapters)
                {
                    ch.Index = allChapters.Count + 1;
                    allChapters.Add(ch);
                }
            }

            progress.Report(15);

            // Split each chapter into paragraphs
            statusProgress.Report("Creating paragraph structures…");
            foreach (var chapter in allChapters)
            {
                var textForParagraphs = StripChapterHeading(chapter.RawText, chapter.Title);
                chapter.Paragraphs = SplitIntoParagraphs(textForParagraphs);
            }

            // Detect and split oversized paragraphs (>500 words)
            const int MaxWordsPerParagraph = 500;
            for (int ci = 0; ci < allChapters.Count; ci++)
            {
                var chapter = allChapters[ci];
                var newParagraphs = new List<ParsedParagraph>();
                bool anySplit = false;

                foreach (var para in chapter.Paragraphs)
                {
                    int wordCount = para.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

                    if (wordCount > MaxWordsPerParagraph)
                    {
                        statusProgress.Report($"Chapter {ci + 1}: Splitting oversized paragraph ({wordCount} words)…");

                        List<string> subTexts;
                        try
                        {
                            subTexts = await _orchestrator.SplitLongParagraphAsync(para.Text);

                            // Integrity check
                            var normalizedOriginal = Regex.Replace(para.Text.Trim(), @"\s+", " ");
                            var normalizedRejoined = Regex.Replace(string.Join(" ", subTexts).Trim(), @"\s+", " ");

                            if (!normalizedOriginal.Equals(normalizedRejoined, StringComparison.Ordinal))
                            {
                                subTexts = HeuristicSplit(para.Text, 250);
                            }
                        }
                        catch
                        {
                            subTexts = HeuristicSplit(para.Text, 250);
                        }

                        foreach (var subText in subTexts)
                            newParagraphs.Add(new ParsedParagraph { Text = subText });
                        anySplit = true;
                    }
                    else
                    {
                        newParagraphs.Add(para);
                    }
                }

                if (anySplit)
                {
                    for (int i = 0; i < newParagraphs.Count; i++)
                        newParagraphs[i].Index = i + 1;
                    chapter.Paragraphs = newParagraphs;
                }
            }

            progress.Report(20);

            // ══════════════════════════════════════════════════
            //  Phase 3: AI Entity Extraction (20→70%)
            // ══════════════════════════════════════════════════
            _logService.WriteToLog("ManuscriptImport Phase 3: AI entity extraction…");

            var allCharacters = new List<ParsedCharacterInfo>();
            var characterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allLocations = new List<ParsedLocationInfo>();
            var locationNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allTimelines = new List<ParsedTimelineInfo>();
            var timelineNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int ci = 0; ci < allChapters.Count; ci++)
            {
                var chapter = allChapters[ci];
                var chapterPctBase = 20 + (int)(50.0 * ci / Math.Max(1, allChapters.Count));

                // 3.1: Summarize
                statusProgress.Report($"Chapter {ci + 1}/{allChapters.Count}: Generating summary…");
                try
                {
                    chapter.Synopsis = await _orchestrator.SummarizeChapterAsync(chapter.RawText);
                }
                catch (Exception ex)
                {
                    _logService.WriteToLog($"ManuscriptImport: Summary failed ch {ci + 1}: {ex.Message}");
                    chapter.Synopsis = "";
                }
                progress.Report(chapterPctBase + 2);

                // 3.2: Extract beats
                statusProgress.Report($"Chapter {ci + 1}/{allChapters.Count}: Extracting narrative beats…");
                try
                {
                    chapter.BeatsSummary = await _orchestrator.ExtractBeatsAsync(chapter.Synopsis);
                }
                catch (Exception ex)
                {
                    _logService.WriteToLog($"ManuscriptImport: Beats failed ch {ci + 1}: {ex.Message}");
                    chapter.BeatsSummary = "";
                }
                progress.Report(chapterPctBase + 4);

                // 3.3: Characters
                statusProgress.Report($"Chapter {ci + 1}/{allChapters.Count}: Identifying characters…");
                try
                {
                    var chapterCharacters = await _orchestrator.ExtractCharactersFromSummaryAsync(
                        chapter.Synopsis, chapter.Title);

                    foreach (var newChar in chapterCharacters)
                    {
                        if (characterNames.Add(newChar.Name))
                        {
                            allCharacters.Add(newChar);
                        }
                        else
                        {
                            var existing = allCharacters.First(c =>
                                c.Name.Equals(newChar.Name, StringComparison.OrdinalIgnoreCase));
                            existing.Backgrounds.AddRange(newChar.Backgrounds);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logService.WriteToLog($"ManuscriptImport: Characters failed ch {ci + 1}: {ex.Message}");
                }
                progress.Report(chapterPctBase + 8);

                // 3.4: Locations
                statusProgress.Report($"Chapter {ci + 1}/{allChapters.Count}: Identifying locations…");
                try
                {
                    var chapterLocations = await _orchestrator.ExtractLocationsFromSummaryAsync(
                        chapter.Synopsis, chapter.Title);

                    foreach (var newLoc in chapterLocations)
                    {
                        if (locationNames.Add(newLoc.Name))
                        {
                            allLocations.Add(newLoc);
                        }
                        else
                        {
                            var existing = allLocations.First(l =>
                                l.Name.Equals(newLoc.Name, StringComparison.OrdinalIgnoreCase));
                            if (!string.IsNullOrWhiteSpace(newLoc.Description)
                                && newLoc.Description.Length > existing.Description.Length)
                                existing.Description = newLoc.Description;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logService.WriteToLog($"ManuscriptImport: Locations failed ch {ci + 1}: {ex.Message}");
                }
                progress.Report(chapterPctBase + 11);

                // 3.5: Timelines
                statusProgress.Report($"Chapter {ci + 1}/{allChapters.Count}: Identifying timelines…");
                try
                {
                    var chapterTimelines = await _orchestrator.ExtractTimelinesFromSummaryAsync(
                        chapter.Synopsis, chapter.Title, chapter.Index);

                    foreach (var newTl in chapterTimelines)
                    {
                        if (timelineNames.Add(newTl.Name))
                        {
                            allTimelines.Add(newTl);
                        }
                        else
                        {
                            var existing = allTimelines.First(t =>
                                t.Name.Equals(newTl.Name, StringComparison.OrdinalIgnoreCase));
                            if (!string.IsNullOrWhiteSpace(newTl.Description)
                                && string.IsNullOrWhiteSpace(existing.Description))
                                existing.Description = newTl.Description;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logService.WriteToLog($"ManuscriptImport: Timelines failed ch {ci + 1}: {ex.Message}");
                }
                progress.Report(chapterPctBase + 13);

                // Yield to UI between chapters
                await Task.Delay(1);
            }

            progress.Report(70);

            // Associate paragraphs with entities
            statusProgress.Report("Associating paragraphs with entities…");
            for (int ci = 0; ci < allChapters.Count; ci++)
            {
                var chapter = allChapters[ci];
                try
                {
                    var annotated = await _orchestrator.AssociateParagraphEntitiesAsync(
                        chapter.Paragraphs, allCharacters, allLocations, allTimelines);
                    chapter.Paragraphs = annotated;
                }
                catch (Exception ex)
                {
                    _logService.WriteToLog($"ManuscriptImport: Entity association failed ch {ci + 1}: {ex.Message}");
                }
            }

            progress.Report(75);

            // ══════════════════════════════════════════════════
            //  Phase 4: Assemble Story & Generate Embeddings (75→100%)
            // ══════════════════════════════════════════════════
            statusProgress.Report("Assembling story and generating embeddings…");
            _logService.WriteToLog("ManuscriptImport Phase 4: Assembling story…");

            var storyTitle = Path.GetFileNameWithoutExtension(filePath);

            // Build the Story object in AIStoryBuilders format
            var story = new Story
            {
                Title = storyTitle,
                Style = "",
                Theme = "You are a software program that creates prose for novels.",
                Synopsis = allChapters.Count > 0 && !string.IsNullOrEmpty(allChapters[0].Synopsis)
                    ? allChapters[0].Synopsis
                    : $"Imported manuscript: {storyTitle}",
                WorldFacts = "",
                Chapter = new List<Chapter>(),
                Character = new List<Character>(),
                Location = new List<Location>(),
                Timeline = new List<Timeline>()
            };

            // Map timelines
            int timelineIdx = 0;
            foreach (var tl in allTimelines)
            {
                story.Timeline.Add(new Timeline
                {
                    TimelineName = _orchestrator.SanitizeFileName(tl.Name),
                    TimelineDescription = tl.Description ?? "",
                    StartDate = DateTime.Now.AddDays(timelineIdx),
                    StopDate = DateTime.Now.AddDays(timelineIdx + 1)
                });
                timelineIdx += 2;
            }

            // Map characters
            foreach (var ch in allCharacters)
            {
                var character = new Character
                {
                    CharacterName = _orchestrator.SanitizeFileName(ch.Name),
                    CharacterBackground = new List<CharacterBackground>()
                };

                foreach (var bg in ch.Backgrounds)
                {
                    character.CharacterBackground.Add(new CharacterBackground
                    {
                        Type = bg.BackgroundType ?? "Facts",
                        Description = bg.Description ?? "",
                        Timeline = story.Timeline.FirstOrDefault(t =>
                            t.TimelineName.Equals(bg.TimelineName, StringComparison.OrdinalIgnoreCase))
                    });
                }

                // If no backgrounds, add a default one
                if (character.CharacterBackground.Count == 0)
                {
                    character.CharacterBackground.Add(new CharacterBackground
                    {
                        Type = "Facts",
                        Description = ch.Backstory ?? ch.Name
                    });
                }

                story.Character.Add(character);
            }

            // Map locations
            foreach (var loc in allLocations)
            {
                var location = new Location
                {
                    LocationName = _orchestrator.SanitizeFileName(loc.Name),
                    LocationDescription = new List<LocationDescription>
                    {
                        new LocationDescription
                        {
                            Description = loc.Description ?? loc.Name
                        }
                    }
                };
                story.Location.Add(location);
            }

            // Map chapters and paragraphs
            for (int ci = 0; ci < allChapters.Count; ci++)
            {
                var parsedChapter = allChapters[ci];
                var chapter = new Chapter
                {
                    Sequence = ci + 1,
                    ChapterName = $"Chapter{ci + 1}",
                    Synopsis = parsedChapter.BeatsSummary ?? parsedChapter.Synopsis ?? "",
                    Paragraph = new List<Paragraph>()
                };

                for (int pi = 0; pi < parsedChapter.Paragraphs.Count; pi++)
                {
                    var parsedPara = parsedChapter.Paragraphs[pi];

                    var paragraph = new Paragraph
                    {
                        Sequence = pi + 1,
                        ParagraphContent = parsedPara.Text,
                        Location = story.Location.FirstOrDefault(l =>
                            l.LocationName.Equals(parsedPara.Location, StringComparison.OrdinalIgnoreCase)),
                        Timeline = story.Timeline.FirstOrDefault(t =>
                            t.TimelineName.Equals(parsedPara.Timeline, StringComparison.OrdinalIgnoreCase)),
                        Characters = story.Character.Where(c =>
                            parsedPara.Characters.Any(pc =>
                                pc.Equals(c.CharacterName, StringComparison.OrdinalIgnoreCase))).ToList()
                    };

                    chapter.Paragraph.Add(paragraph);
                }

                story.Chapter.Add(chapter);

                statusProgress.Report($"Assembling chapter {ci + 1}/{allChapters.Count}…");
                progress.Report(75 + (int)(25.0 * (ci + 1) / allChapters.Count));
            }

            progress.Report(100);
            statusProgress.Report("Import complete!");
            _logService.WriteToLog(
                $"ManuscriptImport complete: {story.Chapter.Count} chapters, " +
                $"{story.Character.Count} characters, {story.Location.Count} locations, " +
                $"{story.Timeline.Count} timelines");

            return story;
        }
        finally
        {
            // Clean up working directory
            CleanupWorkingDirectory();
        }
    }

    // ═══════════════════════════════════════════════════════
    //  Working Directory
    // ═══════════════════════════════════════════════════════

    private static void InitWorkingDirectory()
    {
        if (Directory.Exists(WorkingDir))
            Directory.Delete(WorkingDir, recursive: true);
        Directory.CreateDirectory(WorkingDir);
    }

    private static void CleanupWorkingDirectory()
    {
        try
        {
            if (Directory.Exists(WorkingDir))
                Directory.Delete(WorkingDir, recursive: true);
        }
        catch { /* best effort */ }
    }

    // ═══════════════════════════════════════════════════════
    //  Text Extraction
    // ═══════════════════════════════════════════════════════

    private static string ExtractDocx(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart?.Document.Body;
        if (body is null) return string.Empty;

        var sb = new StringBuilder();
        foreach (var para in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            sb.AppendLine(para.InnerText);
        }
        return sb.ToString();
    }

    private static string ExtractPdf(string path)
    {
        using var document = PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            sb.AppendLine(page.Text);
        }
        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════
    //  Sentence Splitting & Chunking
    // ═══════════════════════════════════════════════════════

    private static List<string> SplitIntoSentences(string text)
    {
        var sentences = new List<string>();
        var pattern = """(?<=[.!?])\s+(?=[A-Z"'\(\[])""";
        var parts = Regex.Split(text.Trim(), pattern);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                sentences.Add(trimmed);
        }

        if (sentences.Count <= 1 && text.Length > 1000)
        {
            sentences.Clear();
            foreach (var line in text.Split('\n'))
            {
                var t = line.Trim();
                if (!string.IsNullOrWhiteSpace(t))
                    sentences.Add(t);
            }
        }

        return sentences;
    }

    private static List<string> ChunkSentences(List<string> sentences, int chunkSize)
    {
        var chunks = new List<string>();
        for (int i = 0; i < sentences.Count; i += chunkSize)
        {
            var batch = sentences.Skip(i).Take(chunkSize);
            chunks.Add(string.Join(" ", batch));
        }

        if (chunks.Count == 0)
            chunks.Add(string.Join(" ", sentences));

        return chunks;
    }

    // ═══════════════════════════════════════════════════════
    //  Paragraph Splitting
    // ═══════════════════════════════════════════════════════

    private static List<ParsedParagraph> SplitIntoParagraphs(string rawText)
    {
        var paragraphs = new List<ParsedParagraph>();
        if (string.IsNullOrWhiteSpace(rawText))
            return paragraphs;

        var blocks = Regex.Split(rawText, @"\n\s*\n");
        int index = 1;

        foreach (var block in blocks)
        {
            var trimmed = block.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                var text = Regex.Replace(trimmed, @"\s*\n\s*", " ");
                paragraphs.Add(new ParsedParagraph
                {
                    Index = index++,
                    Text = text
                });
            }
        }

        return paragraphs;
    }

    // ═══════════════════════════════════════════════════════
    //  Chapter Heading Removal
    // ═══════════════════════════════════════════════════════

    private static string StripChapterHeading(string rawText, string chapterTitle)
    {
        if (string.IsNullOrWhiteSpace(rawText) || string.IsNullOrWhiteSpace(chapterTitle))
            return rawText;

        var lines = rawText.Split('\n');
        int linesToSkip = 0;

        for (int i = 0; i < Math.Min(lines.Length, 3); i++)
        {
            var trimmed = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                linesToSkip++;
                continue;
            }

            bool isHeading =
                trimmed.Equals(chapterTitle, StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("chapter", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(trimmed, @"^(part|book|section|act|prologue|epilogue)\b",
                              RegexOptions.IgnoreCase) ||
                Regex.IsMatch(trimmed, @"^\d+$") ||
                Regex.IsMatch(trimmed, @"^[IVXLCDM]+\.?$");

            if (isHeading)
            {
                linesToSkip = i + 1;
                while (linesToSkip < lines.Length
                    && string.IsNullOrWhiteSpace(lines[linesToSkip]))
                {
                    linesToSkip++;
                }
                break;
            }
            else
            {
                break;
            }
        }

        if (linesToSkip == 0)
            return rawText;

        return string.Join('\n', lines.Skip(linesToSkip));
    }

    // ═══════════════════════════════════════════════════════
    //  Heuristic Split Fallback
    // ═══════════════════════════════════════════════════════

    private static List<string> HeuristicSplit(string text, int targetWordsPerParagraph)
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

// ═══════════════════════════════════════════════════════
//  Parsing models (intermediate, not persisted)
// ═══════════════════════════════════════════════════════

public class ParsedChapter
{
    public int Index { get; set; }
    public string Title { get; set; } = "";
    public string RawText { get; set; } = "";
    public string Synopsis { get; set; } = "";
    public string BeatsSummary { get; set; } = "";
    public List<ParsedParagraph> Paragraphs { get; set; } = new();
}

public class ParsedParagraph
{
    public int Index { get; set; }
    public string Text { get; set; } = "";
    public string Location { get; set; } = "Unknown";
    public string Timeline { get; set; } = "Unknown";
    public List<string> Characters { get; set; } = new();
}

public class ParsedCharacterInfo
{
    public string Name { get; set; } = "";
    public string Backstory { get; set; } = "";
    public List<ParsedBackground> Backgrounds { get; set; } = new();
}

public class ParsedBackground
{
    public string BackgroundType { get; set; } = "Facts";
    public string TimelineName { get; set; } = "";
    public string Description { get; set; } = "";
}

public class ParsedLocationInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

public class ParsedTimelineInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}
