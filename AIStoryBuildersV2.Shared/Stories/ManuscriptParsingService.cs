#nullable disable
using AIStoryBuildersV2.Shared.AI;
using AIStoryBuildersV2.Shared.Models;
using AIStoryBuildersV2.Shared.Stories.Storage;
using System.IO.Compression;
using Xceed.Words.NET;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Location = AIStoryBuildersV2.Shared.Models.Location;

namespace AIStoryBuildersV2.Shared.Stories;
/// <summary>
/// Four-phase pipeline that imports a .docx, .pdf, .txt, or .md manuscript
/// and converts it into a fully structured <see cref="Story"/> object.
/// </summary>
public class ManuscriptParsingService
{
    private readonly OrchestratorMethods _orchestrator;
    private readonly LogService _logService;

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

    /// <summary>
    /// V1's import pipeline, reading the file from a stream (V2 has no working folder, so nothing is written to temp).
    /// Docx is read with DocX in both apps (D21).
    /// </summary>
    public async Task<Story> ParseManuscriptAsync(
        Stream content,
        string fileName,
        IProgress<int> progress,
        IProgress<string> statusProgress)
    {
        progress.Report(0);
        statusProgress.Report("Preparing working directory…");

        {
            // ══════════════════════════════════════════════════
            //  Phase 1: Text Extraction (0→5%)
            // ══════════════════════════════════════════════════
            statusProgress.Report("Extracting text from file…");
            _logService.WriteToLog("ManuscriptImport Phase 1: Extracting text…");

            // DocX and PdfPig need a seekable stream.
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer);
            buffer.Position = 0;

            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            string rawText = extension switch
            {
                ".docx" => ExtractDocx(buffer),
                ".pdf" => ExtractPdf(buffer),
                ".txt" or ".md" => await new StreamReader(buffer).ReadToEndAsync(),
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

            var chunks = ChunkText(rawText, 100);

            // Internal chapter model for parsing
            var allChapters = new List<ParsedChapter>();

            for (int i = 0; i < chunks.Count; i++)
            {
                statusProgress.Report($"Parsing chapters — chunk {i + 1} of {chunks.Count}…");

                List<ParsedChapter> chunkChapters;
                try
                {
                    chunkChapters = await _orchestrator.ParseChaptersFromTextAsync(chunks[i].Text);
                }
                catch (Exception ex)
                {
                    _logService.WriteToLog($"ManuscriptImport: Chapter parse failed chunk {i + 1}: {ex.Message}");
                    // Fallback: treat entire chunk as continuation
                    chunkChapters = new List<ParsedChapter>
                    {
                        new() { Title = "Chapter 1", RawText = chunks[i].Text }
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
                        lastExisting.RawText += (chunks[i - 1].EndsParagraph ? "\n\n" : " ") + firstNew.RawText;
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
                catch (LlmCallException ex) when (ex.IsAuthenticationError) { throw; }
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
                catch (LlmCallException ex) when (ex.IsAuthenticationError) { throw; }
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
                        chapter.Synopsis, chapter.Title, chapter.RawText);

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
                catch (LlmCallException ex) when (ex.IsAuthenticationError) { throw; }
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
                        chapter.Synopsis, chapter.Title, chapter.RawText);

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
                catch (LlmCallException ex) when (ex.IsAuthenticationError) { throw; }
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
                        chapter.Synopsis, chapter.Title, chapter.Index, chapter.RawText);

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
                catch (LlmCallException ex) when (ex.IsAuthenticationError) { throw; }
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
                // A chapter with only a heading has nothing to annotate, and an empty request is an API error.
                if (chapter.Paragraphs.Count == 0) continue;
                try
                {
                    var annotated = await _orchestrator.AssociateParagraphEntitiesAsync(
                        chapter.Paragraphs, allCharacters, allLocations, allTimelines);
                    chapter.Paragraphs = annotated;
                }
                catch (LlmCallException ex) when (ex.IsAuthenticationError) { throw; }
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

            var storyTitle = AIStoryBuildersService.TitleFromFileName(fileName);

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
    }

    // ═══════════════════════════════════════════════════════
    //  Text Extraction
    // ═══════════════════════════════════════════════════════

    /// <summary>More pages than any manuscript has; PdfPig keeps every page in memory (SEC-005).</summary>
    internal const int MaxPdfPages = 2_000;

    internal const string UnreadableDocxMessage = "This Word file can't be read. Open it in Word, save it again and retry.";
    internal const string UnreadablePdfMessage = "This PDF can't be read. Export it again, or import the text as a .txt file.";

    private static string ExtractDocx(MemoryStream stream)
    {
        // SEC-005: a .docx is a zip. Refuse one that inflates past the limits before DocX loads it.
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            SafeZip.CheckLimits(archive, ZipLimits.Docx);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException(UnreadableDocxMessage, ex);
        }
        var bytes = stream.ToArray();

        List<string> paragraphs;
        try
        {
            using var doc = DocX.Load(new MemoryStream(bytes));
            paragraphs = doc.Paragraphs.Select(p => p.Text).ToList();
        }
        catch (Exception)
        {
            // DocX needs parts that a valid file may leave out, such as styles.
            paragraphs = ReadDocumentXml(bytes);
        }

        // Each Word paragraph is a paragraph of the story; the blank line keeps them apart in SplitIntoParagraphs.
        return string.Join("\n\n", paragraphs.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static List<string> ReadDocumentXml(byte[] bytes)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var entry = archive.GetEntry("word/document.xml") ?? throw new InvalidDataException(UnreadableDocxMessage);
            using var xml = entry.Open();
            using var reader = XmlReader.Create(xml, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            return XDocument.Load(reader).Descendants(w + "p")
                .Select(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value)))
                .ToList();
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException(UnreadableDocxMessage, ex);
        }
    }

    private static string ExtractPdf(Stream stream)
    {
        try
        {
            using var document = PdfDocument.Open(stream);
            if (document.NumberOfPages > MaxPdfPages) throw new InvalidDataException(SafeZip.TooLargeMessage);
            var sb = new StringBuilder();
            // page.Text has no line breaks, which makes a whole chapter look like one heading line.
            foreach (var page in document.GetPages()) sb.AppendLine(ContentOrderTextExtractor.GetText(page, addDoubleNewline: true));
            return sb.ToString();
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException(UnreadablePdfMessage, ex);
        }
    }

    // ═══════════════════════════════════════════════════════
    //  Chunking
    // ═══════════════════════════════════════════════════════

    private static readonly Regex SentenceBoundary = new("""(?<=[.!?])\s+(?=[A-Z"'\(\[])""");
    private static readonly Regex LineBoundary = new(@"\s*\n\s*");

    /// <summary>A piece of the manuscript for the chapter-boundary call, and whether a paragraph break followed it.</summary>
    internal readonly record struct ManuscriptChunk(string Text, bool EndsParagraph);

    /// <summary>
    /// Cuts the text at every Nth sentence end. The text between cuts is left as it is, because the paragraph breaks in
    /// it are what <see cref="SplitIntoParagraphs"/> works from later (V1 joined the sentences with spaces and lost them).
    /// </summary>
    internal static List<ManuscriptChunk> ChunkText(string text, int sentencesPerChunk)
    {
        text = text.Trim();
        var boundaries = SentenceBoundary.Matches(text);
        if (boundaries.Count == 0 && text.Length > 1000) boundaries = LineBoundary.Matches(text);

        var chunks = new List<ManuscriptChunk>();
        int start = 0, sentences = 0;
        foreach (Match boundary in boundaries)
        {
            if (++sentences < sentencesPerChunk) continue;
            chunks.Add(new ManuscriptChunk(text[start..boundary.Index], boundary.Value.Count(c => c == '\n') >= 2));
            start = boundary.Index + boundary.Length;
            sentences = 0;
        }
        chunks.Add(new ManuscriptChunk(text[start..], true));
        return chunks;
    }

    // ═══════════════════════════════════════════════════════
    //  Paragraph Splitting
    // ═══════════════════════════════════════════════════════

    internal static List<ParsedParagraph> SplitIntoParagraphs(string rawText)
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

    private const string Numeral = @"(\d+|(?-i:[IVXLCDM]+)|(one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety|hundred)([ -](one|two|three|four|five|six|seven|eight|nine))?)";
    private static readonly Regex ChapterPrefix = new($@"^chapter\s+{Numeral}\b[\s:.\-–—]*", RegexOptions.IgnoreCase);
    private static readonly Regex HeadingWord = new($@"^(chapter\b|(part|book|section|act)\s+{Numeral}\b|prologue\b|epilogue\b)", RegexOptions.IgnoreCase);
    private static readonly Regex BareNumber = new(@"^(\d+|[IVXLCDM]+\.?)$");
    private const int MaxHeadingWords = 12;

    /// <summary>
    /// Removes the heading from the start of a chapter and keeps everything else. A first line that only begins with a
    /// heading word ("Part of me wanted to leave.") is prose, and text without line breaks has the prose on the same line.
    /// </summary>
    internal static string StripChapterHeading(string rawText, string chapterTitle)
    {
        if (string.IsNullOrWhiteSpace(rawText) || string.IsNullOrWhiteSpace(chapterTitle))
            return rawText;

        var lines = rawText.Split('\n');
        var first = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        if (first < 0 || first >= 3) return rawText;

        var remainder = TextAfterHeading(lines[first].Trim().TrimStart('#', ' '), chapterTitle.Trim());
        if (remainder is null) return rawText;

        var rest = lines.Skip(first + 1);
        return string.Join('\n', remainder.Length == 0 ? rest : rest.Prepend(remainder)).TrimStart();
    }

    /// <summary>The text after the heading on this line: empty for a line that is only a heading, null for no heading.</summary>
    private static string TextAfterHeading(string line, string title)
    {
        if (line.Equals(title, StringComparison.OrdinalIgnoreCase) || BareNumber.IsMatch(line)) return "";

        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var endsLikeProse = line.EndsWith('!') || line.EndsWith('?') || line.EndsWith(',') || line.EndsWith(';') || (line.EndsWith('.') && words > 3);
        if (words <= MaxHeadingWords && !endsLikeProse && HeadingWord.IsMatch(line)) return "";

        var rest = line[ChapterPrefix.Match(line).Length..];
        if (rest.StartsWith(title, StringComparison.OrdinalIgnoreCase) && (rest.Length == title.Length || !char.IsLetterOrDigit(rest[title.Length])))
            rest = rest[title.Length..].TrimStart(' ', ':', '.', '-', '–', '—');
        return rest.Length == line.Length ? null : rest;
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
