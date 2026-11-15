#nullable disable
using System.IO.Compression;
using System.Text;
using AIStoryBuildersV2.Shared.Knowledge;
using AIStoryBuildersV2.Shared.Models.JSON;
using AIStoryBuildersV2.Shared.Stories.Storage;
using Newtonsoft.Json;
using Xceed.Document.NET;
using Xceed.Words.NET;

namespace AIStoryBuildersV2.Shared.Stories;

// Word export (F6) and .stybld export and import (F3, F6), built in memory with no temp folders (plan §6.2).
public partial class AIStoryBuildersService
{
    public const string ManifestFileName = "Manifest.config";

    public static class ImportMessages
    {
        public static string Success(string title) => $"The project '{title}' was successfully imported.";
        public const string Incompatible = "The version of the file you are trying to import is not compatible with this version of AI Story Builders. Please update AI Story Builders to the latest version and try again.";
        public static string Exists(string title) => $"The story {title} already exists. Backup and delete it before trying to import a new version.";
    }

    /// <summary>V1's Word layout: a title page, then a section per chapter, one Word paragraph per line with a 28 pt indent.</summary>
    public byte[] ExportWordDocument(Story objStory)
    {
        using var buffer = new MemoryStream();
        using (var document = DocX.Create(buffer))
        {
            document.InsertParagraph(objStory.Title).FontSize(25d).SpacingAfter(50d).Alignment = Alignment.center;
            foreach (var chapter in GetChapters(objStory))
            {
                document.InsertSectionPageBreak();
                document.InsertParagraph(chapter.ChapterName).FontSize(20d).SpacingAfter(25d).Alignment = Alignment.center;
                foreach (var paragraph in GetParagraphs(chapter))
                {
                    var content = (paragraph.ParagraphContent ?? "").Replace("&nbsp;", " ").Replace("<br />", "\n");
                    foreach (var line in content.Split('\n'))
                    {
                        var p = document.InsertParagraph(line.Trim());
                        p.IndentationFirstLine = 28f;
                    }
                }
            }
            document.Save();
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// A .stybld: the story folder plus a fresh Manifest.config with V1's format version and WorldFacts (D6). A stale
    /// Manifest.config inside the folder is never copied (D32), and the KnowledgeBase folder travels with the story (D11).
    /// </summary>
    public byte[] ExportProject(Story objStory)
    {
        var manifest = new JSONManifest
        {
            Version = StoryStorageConstants.StybldFormatVersion,
            Title = objStory.Title,
            Style = objStory.Style,
            Theme = objStory.Theme,
            Synopsis = objStory.Synopsis,
            ExportedDate = DateTime.Now.ToString(),
            WorldFacts = objStory.WorldFacts ?? "",
        };

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry(ManifestFileName, CompressionLevel.Optimal).Open(), StoryFormat.Utf8NoBom))
            {
                writer.Write(JsonConvert.SerializeObject(manifest) + StoryFormat.NewLine);
            }

            var folder = StoryPath(objStory.Title);
            if (Directory.Exists(folder))
            {
                foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                {
                    var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
                    if (relative.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase)) continue;
                    using var target = archive.CreateEntry(relative, CompressionLevel.Optimal).Open();
                    using var source = File.OpenRead(file);
                    source.CopyTo(target);
                }
            }
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// V1's ImportProject, async (D25): validates the manifest version and title, checks existence through the library
    /// store, validates every entry path before writing (D22), and returns V1's messages.
    /// </summary>
    /// <exception cref="StorageQuotaExceededException">WASM: browser storage is full; the partial story has been removed.</exception>
    public async Task<string> ImportProjectAsync(byte[] stybldFile)
    {
        string title = null;
        var written = false;
        try
        {
            var limits = _host.IsBrowser ? ZipLimits.BrowserStory : ZipLimits.Story;
            using var archive = new ZipArchive(new MemoryStream(stybldFile), ZipArchiveMode.Read);
            var entries = SafeZip.ValidatedEntries(archive, limits);

            var manifestEntry = entries.FirstOrDefault(e => e.Path.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase)).Entry;
            if (manifestEntry is null) return ImportMessages.Incompatible;
            JSONManifest manifest;
            using (var reader = new StreamReader(manifestEntry.Open()))
            {
                manifest = JsonConvert.DeserializeObject<JSONManifest>(await reader.ReadToEndAsync());
            }
            if (manifest is null || !IsSupportedVersion(manifest.Version)) return ImportMessages.Incompatible;

            title = manifest.Title;
            if (StoryTitle.Validate(title) is { } invalid) return invalid;
            if (await StoryExistsAsync(title)) return ImportMessages.Exists(title);

            var folder = StoryPath(title);
            // A leftover MEMFS or disk folder without a library entry is not a story; replace it.
            if (Directory.Exists(folder)) await ForceDeleteDirectoryAsync(folder);
            Directory.CreateDirectory(folder);
            written = true;
            SafeZip.ExtractTo(entries, folder, limits, path => path.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase));

            await _library.UpsertAsync(new StoryIndexEntry("0", title, StoryFormat.CleanField(manifest.Style), StoryFormat.CleanField(manifest.Theme),
                StoryFormat.CleanField(manifest.Synopsis), StoryFormat.CleanField(manifest.WorldFacts), DateTimeOffset.UtcNow));
            LogService.WriteToLog($"Story imported {title}");

            Touched(title);
            await _persistence.FlushAsync(title);
            return ImportMessages.Success(title);
        }
        catch (StorageQuotaExceededException)
        {
            if (written && title is not null) await DeleteStoryAsync(title);
            throw;
        }
        catch (Exception ex)
        {
            LogService.WriteToLog("ImportProject: " + ex.Message);
            if (written && title is not null)
            {
                try { await DeleteStoryAsync(title); } catch (Exception) { /* best effort clean-up */ }
            }
            return ex.Message;
        }
    }

    /// <summary>V1's version gate: at most 3 numeric segments, not above 03.62.00 (D6). Anything unparseable is refused.</summary>
    public static bool IsSupportedVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return false;
        var segments = version.Split('.');
        if (segments.Length > 3) return false;
        int[] multipliers = [10000, 100, 1];
        var value = 0;
        for (var i = 0; i < segments.Length; i++)
        {
            if (!int.TryParse(segments[i], out var n)) return false;
            value += n * multipliers[i];
        }
        return value <= 36200;
    }
}
