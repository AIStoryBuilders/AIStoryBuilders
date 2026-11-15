using System.Buffers.Binary;
using System.IO.Compression;
using AIStoryBuildersV2.Shared.Stories;
using AIStoryBuildersV2.Shared.Stories.Storage;

namespace AIStoryBuildersV2.Tests;

/// <summary>SEC-005: archives are bounded by entry count and by the bytes they actually inflate to, not only by upload size.</summary>
public class ZipLimitTests : IDisposable
{
    readonly ServiceFixture _f = new(writeBefore: false);
    readonly TempDir _out = new();

    public void Dispose()
    {
        _f.Dispose();
        _out.Dispose();
    }

    const string Manifest = """{"Version":"01.00.00","Title":"Bomb","Style":"","Theme":"","Synopsis":""}""";

    /// <summary>A zip whose entries are text or runs of zeros (zeros compress about 1000:1).</summary>
    static byte[] Zip(params (string Name, string? Text, long Zeros)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var block = new byte[1 << 20];
            foreach (var (name, text, zeros) in entries)
            {
                using var target = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
                if (text is not null)
                {
                    using var w = new StreamWriter(target);
                    w.Write(text);
                    continue;
                }
                for (var left = zeros; left > 0; left -= block.Length) target.Write(block, 0, (int)Math.Min(block.Length, left));
            }
        }
        return buffer.ToArray();
    }

    /// <summary>Rewrites every header that declares <paramref name="actual"/> uncompressed bytes to declare <paramref name="claimed"/>.</summary>
    static byte[] LieAboutSize(byte[] zip, uint actual, uint claimed)
    {
        var patched = 0;
        for (var i = 0; i + 30 <= zip.Length; i++)
        {
            var sig = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(i));
            var offset = sig switch { 0x04034b50 => 22, 0x02014b50 => 24, _ => -1 };
            if (offset < 0 || BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(i + offset)) != actual) continue;
            BinaryPrimitives.WriteUInt32LittleEndian(zip.AsSpan(i + offset), claimed);
            patched++;
        }
        Assert.Equal(2, patched);
        return zip;
    }

    [Fact]
    public async Task An_import_that_inflates_past_the_limit_is_refused_and_leaves_nothing_behind()
    {
        var bytes = Zip(("Manifest.config", Manifest, 0), ("Characters/Big.csv", null, ZipLimits.Story.MaxEntryBytes + 1));
        Assert.True(bytes.Length < StoryStorageConstants.MaxStybldImportBytes);

        Assert.Equal(SafeZip.TooLargeMessage, await _f.Service.ImportProjectAsync(bytes));

        Assert.False(Directory.Exists(_f.Dir.Combine("Bomb")));
        Assert.DoesNotContain(await _f.Library.ListAsync(), e => e.Title == "Bomb");
    }

    [Fact]
    public async Task An_import_whose_headers_understate_the_size_is_stopped_while_extracting_and_cleaned_up()
    {
        const uint actual = 70 * 1024 * 1024;   // above Story.MaxEntryBytes
        var bytes = LieAboutSize(Zip(("Manifest.config", Manifest, 0), ("Characters/Big.csv", null, actual)), actual, 10);

        var message = await _f.Service.ImportProjectAsync(bytes);

        // .NET's own reader stops at the declared size (a CRC failure) before CopyBounded's count is reached;
        // either way the import fails and is cleaned up. Extraction_counts_the_bytes_it_inflates covers CopyBounded.
        Assert.NotEqual(AIStoryBuildersService.ImportMessages.Success("Bomb"), message);
        Assert.False(Directory.Exists(_f.Dir.Combine("Bomb")));
        Assert.DoesNotContain(await _f.Library.ListAsync(), e => e.Title == "Bomb");
    }

    [Fact]
    public void Too_many_entries_are_refused_before_anything_is_written()
    {
        using var archive = new ZipArchive(new MemoryStream(Zip(("a.txt", "a", 0), ("b.txt", "b", 0), ("c.txt", "c", 0))));

        Assert.Throws<SafeZip.ZipLimitExceededException>(() => SafeZip.ValidatedEntries(archive, new ZipLimits(2, 1024, 1024)));
    }

    [Fact]
    public void Declared_total_above_the_limit_is_refused()
    {
        using var archive = new ZipArchive(new MemoryStream(Zip(("a.txt", null, 600), ("b.txt", null, 600))));

        Assert.Throws<SafeZip.ZipLimitExceededException>(() => SafeZip.ValidatedEntries(archive, new ZipLimits(10, 1000, 1000)));
    }

    [Fact]
    public void Extraction_counts_the_bytes_it_inflates()
    {
        using var archive = new ZipArchive(new MemoryStream(Zip(("a.txt", null, 600), ("b.txt", null, 600))));
        var entries = SafeZip.ValidatedEntries(archive, new ZipLimits(10, 1000, 2000));

        // Extract with a tighter total than the declared sizes passed, as if the headers had lied.
        Assert.Throws<SafeZip.ZipLimitExceededException>(() => SafeZip.ExtractTo(entries, _out.Path, new ZipLimits(10, 1000, 1000)));
        Assert.Equal(600, new FileInfo(Path.Combine(_out.Path, "a.txt")).Length);
    }

    [Fact]
    public void An_archive_within_the_limits_extracts_unchanged()
    {
        using var archive = new ZipArchive(new MemoryStream(Zip(("Dir/a.txt", "alpha", 0), ("b.txt", null, 600))));

        SafeZip.ExtractTo(SafeZip.ValidatedEntries(archive, new ZipLimits(10, 1000, 1000)), _out.Path, new ZipLimits(10, 1000, 1000));

        Assert.Equal("alpha", File.ReadAllText(Path.Combine(_out.Path, "Dir", "a.txt")));
        Assert.Equal(600, new FileInfo(Path.Combine(_out.Path, "b.txt")).Length);
    }

    [Fact]
    public async Task A_docx_that_inflates_past_the_limit_is_refused_before_parsing()
    {
        var bytes = Zip(("[Content_Types].xml", "<Types/>", 0), ("word/document.xml", null, ZipLimits.Docx.MaxEntryBytes + 1));
        var parser = new ManuscriptParsingService(_f.Orchestrator, _f.Log);

        var ex = await Assert.ThrowsAsync<SafeZip.ZipLimitExceededException>(() =>
            parser.ParseManuscriptAsync(new MemoryStream(bytes), "bomb.docx", new Progress<int>(), new Progress<string>()));
        Assert.Equal(SafeZip.TooLargeMessage, ex.Message);
    }
}
