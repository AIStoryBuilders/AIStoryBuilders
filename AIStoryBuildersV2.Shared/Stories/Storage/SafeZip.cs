using System.IO.Compression;

namespace AIStoryBuildersV2.Shared.Stories.Storage;

/// <summary>How far an archive may inflate (SEC-005). The 50 MB upload caps only bound the compressed size.</summary>
public sealed record ZipLimits(int MaxEntries, long MaxEntryBytes, long MaxTotalBytes)
{
    /// <summary>A story is text plus 384-float vectors; 512 MB is far above any real story.</summary>
    public static readonly ZipLimits Story = new(MaxEntries: 20_000, MaxEntryBytes: 64L * 1024 * 1024, MaxTotalBytes: 512L * 1024 * 1024);

    /// <summary>Browser: MEMFS is WebAssembly memory, and localStorage holds only a few MB of zip anyway.</summary>
    public static readonly ZipLimits BrowserStory = new(MaxEntries: 20_000, MaxEntryBytes: 32L * 1024 * 1024, MaxTotalBytes: 128L * 1024 * 1024);

    /// <summary>A .docx manuscript, checked before DocX inflates it.</summary>
    public static readonly ZipLimits Docx = new(MaxEntries: 2_000, MaxEntryBytes: 128L * 1024 * 1024, MaxTotalBytes: 256L * 1024 * 1024);
}

/// <summary>
/// Zip extraction entry by entry with a Zip Slip guard (D22). Never ZipFile.ExtractToDirectory, which aborts the
/// WASM runtime. Every entry path is validated before anything is written, so a bad entry rejects the whole archive.
/// </summary>
public static class SafeZip
{
    public sealed class UnsafeEntryException(string entry) : IOException($"The file contains an unsafe path: {entry}")
    {
        public string Entry { get; } = entry;
    }

    /// <summary>
    /// Normalizes an entry name to a relative path with '/' separators. Returns null for a rooted path, a drive letter,
    /// a '..' segment or an empty name.
    /// </summary>
    public static string? NormalizeEntryName(string entryName)
    {
        var name = entryName.Replace('\\', '/');
        if (name.Length == 0 || name.StartsWith('/') || name.Contains(':')) return null;
        var segments = name.Split('/');
        if (segments.Any(s => s == "..")) return null;
        var kept = segments.Where(s => s.Length > 0 && s != ".").ToArray();
        return kept.Length == 0 ? null : string.Join('/', kept);
    }

    /// <summary>Thrown when an archive has too many entries or inflates past its <see cref="ZipLimits"/> (SEC-005).</summary>
    public sealed class ZipLimitExceededException(string message) : IOException(message);

    public const string TooLargeMessage = "The file is too large to import.";

    /// <summary>The file entries of the archive with normalized names (directory entries are skipped).</summary>
    /// <exception cref="UnsafeEntryException">When any entry path is unsafe.</exception>
    /// <exception cref="ZipLimitExceededException">When the declared sizes or the entry count exceed <paramref name="limits"/>.</exception>
    public static List<(string Path, ZipArchiveEntry Entry)> ValidatedEntries(ZipArchive archive, ZipLimits limits)
    {
        if (archive.Entries.Count > limits.MaxEntries) throw new ZipLimitExceededException(TooLargeMessage);
        long declared = 0;
        var result = new List<(string, ZipArchiveEntry)>();
        foreach (var entry in archive.Entries)
        {
            declared += entry.Length;
            if (entry.Length > limits.MaxEntryBytes || declared > limits.MaxTotalBytes) throw new ZipLimitExceededException(TooLargeMessage);
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                if (NormalizeEntryName(entry.FullName.TrimEnd('/', '\\')) is null && entry.FullName.Trim('/', '\\').Length > 0)
                    throw new UnsafeEntryException(entry.FullName);
                continue;
            }
            var path = NormalizeEntryName(entry.FullName) ?? throw new UnsafeEntryException(entry.FullName);
            result.Add((path, entry));
        }
        return result;
    }

    /// <summary>
    /// Extracts the validated entries under <paramref name="targetFolder"/>, skipping those <paramref name="skip"/> rejects.
    /// The bytes actually inflated are counted against <paramref name="limits"/>, because entry headers can lie.
    /// </summary>
    /// <exception cref="ZipLimitExceededException">When an entry or the total inflates past the limits; files already written stay.</exception>
    public static void ExtractTo(IEnumerable<(string Path, ZipArchiveEntry Entry)> entries, string targetFolder, ZipLimits limits, Func<string, bool>? skip = null)
    {
        var root = Path.GetFullPath(targetFolder);
        long total = 0;
        foreach (var (path, entry) in entries)
        {
            if (skip?.Invoke(path) == true) continue;
            var full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new UnsafeEntryException(entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            using var source = entry.Open();
            using var target = File.Create(full);
            total += CopyBounded(source, target, limits.MaxEntryBytes, limits.MaxTotalBytes - total);
        }
    }

    /// <summary>Reads every entry in full, counting the inflated bytes, without keeping them (for archives a library opens).</summary>
    /// <exception cref="ZipLimitExceededException">When the archive exceeds <paramref name="limits"/>.</exception>
    public static void CheckLimits(ZipArchive archive, ZipLimits limits)
    {
        long total = 0;
        foreach (var (_, entry) in ValidatedEntries(archive, limits))
        {
            using var source = entry.Open();
            total += CopyBounded(source, Stream.Null, limits.MaxEntryBytes, limits.MaxTotalBytes - total);
        }
    }

    /// <summary>Copies at most <paramref name="maxEntry"/> bytes and never more than <paramref name="remaining"/>.</summary>
    /// <exception cref="ZipLimitExceededException">When the source has more.</exception>
    public static long CopyBounded(Stream source, Stream target, long maxEntry, long remaining)
    {
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            copied += read;
            if (copied > maxEntry || copied > remaining) throw new ZipLimitExceededException(TooLargeMessage);
            target.Write(buffer, 0, read);
        }
        return copied;
    }

    /// <summary>Zips every file under <paramref name="folder"/> in memory, with '/' separators and no temp files.</summary>
    public static byte[] ZipFolder(string folder, Func<string, bool>? skip = null)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
                if (skip?.Invoke(relative) == true) continue;
                var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                using var target = entry.Open();
                using var source = File.OpenRead(file);
                source.CopyTo(target);
            }
        }
        return buffer.ToArray();
    }
}
