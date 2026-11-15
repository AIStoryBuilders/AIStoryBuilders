using System.IO.Compression;
using System.Text;
using AIStoryBuildersV2.Shared.Models;
using AIStoryBuildersV2.Shared.Stories;
using Microsoft.Extensions.AI;
using Xceed.Words.NET;

namespace AIStoryBuildersV2.Tests;

/// <summary>Manuscript import keeps every word and every paragraph break, whatever the file type (plan Q23).</summary>
public class ManuscriptImportTests : IDisposable
{
    const string Ch1P1 = "Ansel opened the clock shop at dawn and found the great clock stopped at seven. Nobody had touched it in years.";
    const string Ch1P2 = "His sister Bryn arrived at noon carrying a letter from the harbor. It said the clock had been built to warn of floods.";
    const string Ch2P1 = "Ansel and Bryn climbed into the clock tower that evening. Inside the case they found a brass float tied to a long chain.";
    const string Ch2P2 = "When the tide rose the float lifted, and the great clock began to strike. Ansel ran to wake the town.";
    static readonly string[] Lines = ["Chapter 1", Ch1P1, Ch1P2, "Chapter 2", Ch2P1, Ch2P2];

    readonly ScriptedChatClient _chat = new();
    readonly ServiceFixture _f;
    readonly ManuscriptParsingService _parser;

    public ManuscriptImportTests()
    {
        _f = new ServiceFixture(chat: _chat);
        _parser = new ManuscriptParsingService(_f.Orchestrator, _f.Log);
        // The chapter-boundary call answers first; the later calls don't change the paragraphs.
        _chat.EnqueueText("""{"chapters":[{"index":1,"title":"Chapter 1","headingText":"Chapter 1"},{"index":2,"title":"Chapter 2","headingText":"Chapter 2"}]}""");
        _chat.Fallback = () => new ScriptStep([_chat.Text("{}")]);
    }

    public void Dispose() => _f.Dispose();

    Task<Story> ImportAsync(byte[] bytes, string name) =>
        _parser.ParseManuscriptAsync(new MemoryStream(bytes), name, new Progress<int>(), new Progress<string>());

    static void AssertTwoChaptersOfTwoParagraphs(Story story)
    {
        Assert.Equal(2, story.Chapter.Count);
        Assert.Equal([Ch1P1, Ch1P2], story.Chapter[0].Paragraph.Select(p => p.ParagraphContent));
        Assert.Equal([Ch2P1, Ch2P2], story.Chapter[1].Paragraph.Select(p => p.ParagraphContent));
    }

    [Fact]
    public async Task A_text_file_keeps_its_paragraphs() =>
        AssertTwoChaptersOfTwoParagraphs(await ImportAsync(Encoding.UTF8.GetBytes(string.Join("\n\n", Lines) + "\n"), "Clock.txt"));

    [Fact]
    public async Task A_text_file_with_windows_line_ends_keeps_its_paragraphs() =>
        AssertTwoChaptersOfTwoParagraphs(await ImportAsync(Encoding.UTF8.GetBytes(string.Join("\r\n\r\n", Lines) + "\r\n"), "Clock.txt"));

    [Fact]
    public async Task A_word_file_keeps_each_paragraph()
    {
        using var buffer = new MemoryStream();
        using (var doc = DocX.Create(buffer))
        {
            foreach (var line in Lines) doc.InsertParagraph(line);
            doc.Save();
        }
        AssertTwoChaptersOfTwoParagraphs(await ImportAsync(buffer.ToArray(), "Clock.docx"));
    }

    [Fact]
    public async Task A_valid_word_file_with_only_a_document_part_imports() =>
        AssertTwoChaptersOfTwoParagraphs(await ImportAsync(MinimalDocx(Lines), "Clock.docx"));

    [Fact]
    public async Task A_file_that_is_not_a_word_file_gets_a_clear_message()
    {
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => ImportAsync(Encoding.UTF8.GetBytes("not a zip"), "Clock.docx"));
        Assert.Equal(ManuscriptParsingService.UnreadableDocxMessage, ex.Message);
    }

    [Fact]
    public async Task A_file_that_is_not_a_pdf_gets_a_clear_message()
    {
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => ImportAsync(Encoding.UTF8.GetBytes("not a pdf"), "Clock.pdf"));
        Assert.Equal(ManuscriptParsingService.UnreadablePdfMessage, ex.Message);
    }

    [Fact]
    public async Task A_pdf_with_chapter_headings_keeps_all_its_prose()
    {
        var story = await ImportAsync(MinimalPdf(Lines), "Clock.pdf");

        Assert.Equal(2, story.Chapter.Count);
        var text = string.Join(" ", story.Chapter.SelectMany(c => c.Paragraph).Select(p => p.ParagraphContent));
        Assert.Contains("found the great clock stopped at seven", text);
        Assert.Contains("built to warn of floods", text);
        Assert.Contains("a brass float tied to a long chain", text);
        Assert.Contains("Ansel ran to wake the town", text);
        Assert.DoesNotContain("Chapter 1", text);
        Assert.DoesNotContain("Chapter 2", text);
    }

    [Fact]
    public async Task A_chapter_with_only_a_heading_sends_no_empty_request()
    {
        var story = await ImportAsync(Encoding.UTF8.GetBytes("Chapter 1\n\nChapter 2\n\n" + Ch2P1), "Clock.txt");

        Assert.Empty(story.Chapter[0].Paragraph);
        Assert.Single(story.Chapter[1].Paragraph);
        Assert.DoesNotContain(_chat.Requests, r => r.Any(m => m.Role == ChatRole.User && string.IsNullOrWhiteSpace(m.Text)));
    }

    [Theory]
    [InlineData("Chapter 1\n\nProse here.", "Chapter 1", "Prose here.")]
    [InlineData("CHAPTER ONE\nProse here.", "Chapter 1", "Prose here.")]
    [InlineData("Chapter 1: The Clock\n\nProse here.", "Chapter 1", "Prose here.")]
    [InlineData("# Chapter 1\n\nProse here.", "Chapter 1", "Prose here.")]
    [InlineData("The Clock\n\nProse here.", "The Clock", "Prose here.")]
    [InlineData("\n7\n\nProse here.", "Chapter 7", "Prose here.")]
    [InlineData("IV.\nProse here.", "Chapter 4", "Prose here.")]
    [InlineData("Part II\nProse here.", "Part II", "Prose here.")]
    [InlineData("Prologue\nProse here.", "The Start", "Prose here.")]
    [InlineData("Chapter 1 Ansel opened the shop at dawn and found the great clock stopped at seven.", "Chapter 1", "Ansel opened the shop at dawn and found the great clock stopped at seven.")]
    [InlineData("Chapter 1. He ran.", "Chapter 1", "He ran.")]
    [InlineData("Chapter 3: The Clock Ansel opened the shop at dawn and found the great clock had stopped.", "The Clock", "Ansel opened the shop at dawn and found the great clock had stopped.")]
    public void The_heading_is_removed_and_the_prose_stays(string raw, string title, string expected) =>
        Assert.Equal(expected, ManuscriptParsingService.StripChapterHeading(raw, title));

    [Theory]
    [InlineData("Part of me wanted to leave.\nSecond line.")]
    [InlineData("Act now, he said, before the tide turns.\nSecond line.")]
    [InlineData("Book of Hours lay open on the table.\nSecond line.")]
    [InlineData("Chapters of his life were missing.\nSecond line.")]
    [InlineData("Section by section the wall came down.\nSecond line.")]
    [InlineData("Part of me")]
    public void Prose_that_starts_with_a_heading_word_is_kept(string raw) =>
        Assert.Equal(raw, ManuscriptParsingService.StripChapterHeading(raw, "Chapter 1"));

    [Fact]
    public void Chunks_keep_the_paragraph_breaks_and_lose_no_text()
    {
        var text = "One. Two.\n\nThree. Four.\nFive. Six.";

        var chunks = ManuscriptParsingService.ChunkText(text, 2);

        Assert.Equal(["One. Two.", "Three. Four.", "Five. Six."], chunks.Select(c => c.Text));
        Assert.Equal([true, false, true], chunks.Select(c => c.EndsParagraph));
    }

    [Fact]
    public void Chunks_inside_one_batch_keep_their_line_breaks() =>
        Assert.Equal("One. Two.\n\nThree.", Assert.Single(ManuscriptParsingService.ChunkText("One. Two.\n\nThree.", 100)).Text);

    [Fact]
    public void Text_without_sentence_ends_is_chunked_by_line()
    {
        var text = string.Join("\n", Enumerable.Repeat(new string('x', 200), 8));
        Assert.Equal(2, ManuscriptParsingService.ChunkText(text, 5).Count);
    }

    /// <summary>A .docx with only the parts the format requires: no styles, settings or properties.</summary>
    static byte[] MinimalDocx(IEnumerable<string> paragraphs)
    {
        var body = string.Concat(paragraphs.Select(p => $"<w:p><w:r><w:t>{p}</w:t></w:r></w:p>"));
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string content)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(content);
            }
            Add("[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>""");
            Add("_rels/.rels", """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>""");
            Add("word/document.xml", $"""<?xml version="1.0" encoding="UTF-8"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>{body}<w:sectPr/></w:body></w:document>""");
        }
        return buffer.ToArray();
    }

    /// <summary>A one-page PDF with a text line per 80 characters and a gap between paragraphs.</summary>
    static byte[] MinimalPdf(IEnumerable<string> paragraphs)
    {
        var ops = new StringBuilder("BT\n/F1 11 Tf\n14 TL\n60 760 Td\n");
        foreach (var paragraph in paragraphs)
        {
            var line = new StringBuilder();
            foreach (var word in paragraph.Split(' '))
            {
                if (line.Length + word.Length + 1 > 80) { ops.Append($"({line}) Tj T*\n"); line.Clear(); }
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
            ops.Append($"({line}) Tj T*\n() Tj T*\n");
        }
        ops.Append("ET");
        var stream = ops.ToString();
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ];
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
