using AIStoryBuildersV2.Shared.Knowledge;

namespace AIStoryBuildersV2.Tests;

public sealed class FileSystemKnowledgeStoreTests : IDisposable
{
    sealed class RecordingDeleter : IFileDeleter
    {
        public List<string> Files { get; } = [];
        public List<string> Directories { get; } = [];
        public bool IsRecoverable => true;
        public void DeleteFile(string fullPath) { Files.Add(fullPath); File.Delete(fullPath); }
        public void DeleteDirectory(string fullPath) { Directories.Add(fullPath); Directory.Delete(fullPath, true); }
    }

    readonly TempDir _dir = new();
    readonly RecordingDeleter _deleter = new();
    readonly FileSystemKnowledgeStore _store;

    public FileSystemKnowledgeStoreTests()
    {
        _store = new FileSystemKnowledgeStore(new KnowledgeStoreOptions(_dir.Path), _deleter);
    }

    public void Dispose() => _dir.Dispose();

    FileSystemKnowledgeStore StoreWithLimit(int maxBytes) => new(new KnowledgeStoreOptions(_dir.Path, maxBytes), _deleter);

    [Fact]
    public async Task Save_Read_Update_RoundTrip()
    {
        var created = await _store.SavePageAsync("Notes/Idea", "# Idea\n", 0);
        Assert.Equal(SaveStatus.Created, created.Status);
        Assert.True(File.Exists(_dir.Combine("Notes/Idea.md")));

        var page = await _store.ReadPageAsync("Notes/Idea.md");
        Assert.Equal("Notes/Idea.md", page.Path);
        Assert.Equal("# Idea\n", page.Markdown);
        Assert.Equal(created.ModifiedTicks, page.ModifiedTicks);

        var saved = await _store.SavePageAsync("Notes/Idea.md", "# Idea v2\n", page.ModifiedTicks);
        Assert.Equal(SaveStatus.Saved, saved.Status);
        Assert.True(saved.ModifiedTicks > created.ModifiedTicks);
        Assert.Equal("# Idea v2\n", (await _store.ReadPageAsync("Notes/Idea")).Markdown);

        var unconditional = await _store.SavePageAsync("Notes/Idea.md", "# v3\n", null);
        Assert.Equal(SaveStatus.Saved, unconditional.Status);
    }

    [Fact]
    public async Task Save_WritesUtf8WithoutBom()
    {
        await _store.SavePageAsync("u.md", "héllo", null);
        var bytes = await File.ReadAllBytesAsync(_dir.Combine("u.md"));
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal("héllo", (await _store.ReadPageAsync("u.md")).Markdown);
    }

    [Fact]
    public async Task Children_AreFoldersFirst_ThenPages_CaseInsensitive()
    {
        await _store.SavePageAsync("beta.md", "", null);
        await _store.SavePageAsync("Alpha.md", "", null);
        await _store.SavePageAsync("zeta/x.md", "", null);
        await _store.CreateFolderAsync("Apple");
        await _store.CreateFolderAsync("banana");

        var children = await _store.GetChildrenAsync("");

        Assert.Equal(["Apple", "banana", "zeta", "Alpha.md", "beta.md"], children.Select(c => c.Name));
        Assert.Equal([true, true, true, false, false], children.Select(c => c.IsFolder));
        Assert.Equal("zeta", children[2].Path);
        Assert.Equal(["zeta/x.md"], (await _store.GetChildrenAsync("zeta")).Select(c => c.Path));
    }

    [Fact]
    public async Task HiddenEntries_AreNotListed_ButAreCounted()
    {
        await _store.SavePageAsync("a.md", "", null);
        await _store.SavePageAsync("sub/b.md", "", null);
        File.WriteAllText(_dir.Combine("notes.txt"), "x");
        Directory.CreateDirectory(_dir.Combine(".hidden"));
        File.WriteAllText(_dir.Combine(".hidden/c.md"), "x");
        File.WriteAllText(_dir.Combine("~kgb-temp.md"), "x");

        var children = await _store.GetChildrenAsync("");
        Assert.Equal(["sub", "a.md"], children.Select(c => c.Name));

        var paths = await _store.GetAllPagePathsAsync();
        Assert.Equal(["a.md", "sub/b.md"], paths.Order(StringComparer.Ordinal));

        var stats = await _store.GetFolderStatsAsync("");
        Assert.Equal(2, stats.Pages);
        Assert.Equal(2, stats.Folders);           // sub, .hidden
        Assert.Equal(3, stats.OtherFiles);        // notes.txt, .hidden/c.md, ~kgb-temp.md
    }

    [Fact]
    public async Task Conflict_And_AlreadyExists()
    {
        var created = await _store.SavePageAsync("p.md", "one", 0);

        var again = await _store.SavePageAsync("p.md", "two", 0);
        Assert.Equal(SaveStatus.AlreadyExists, again.Status);

        var stale = await _store.SavePageAsync("p.md", "two", created.ModifiedTicks - 1);
        Assert.Equal(SaveStatus.Conflict, stale.Status);
        Assert.Equal(created.ModifiedTicks, stale.ModifiedTicks);

        var missing = await _store.SavePageAsync("missing.md", "x", 12345);
        Assert.Equal(SaveStatus.Conflict, missing.Status);
        Assert.False(File.Exists(_dir.Combine("missing.md")));

        Assert.Equal("one", (await _store.ReadPageAsync("p.md")).Markdown);
    }

    [Fact]
    public async Task CreateFolder_Twice_Throws()
    {
        await _store.CreateFolderAsync("f");
        await Assert.ThrowsAsync<KnowledgeAlreadyExistsException>(() => _store.CreateFolderAsync("f"));
    }

    [Fact]
    public async Task ReadMissing_Throws_NotFound()
    {
        await Assert.ThrowsAsync<KnowledgeNotFoundException>(() => _store.ReadPageAsync("nope.md"));
        await Assert.ThrowsAsync<KnowledgeNotFoundException>(() => _store.GetChildrenAsync("nope"));
    }

    [Fact]
    public async Task Traversal_IsRejected()
    {
        await Assert.ThrowsAsync<InvalidKnowledgePathException>(() => _store.ReadPageAsync("../outside.md"));
        await Assert.ThrowsAsync<InvalidKnowledgePathException>(() => _store.SavePageAsync("a/../../outside.md", "x", null));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_dir.Path)!, "outside.md")));
    }

    [Fact]
    public async Task Rename_Page_And_Folder()
    {
        await _store.SavePageAsync("Old.md", "x", null);
        Assert.Equal("New.md", await _store.RenameAsync("Old.md", "New"));
        Assert.True(File.Exists(_dir.Combine("New.md")));
        Assert.False(File.Exists(_dir.Combine("Old.md")));

        await _store.SavePageAsync("Folder/a.md", "x", null);
        Assert.Equal("Renamed", await _store.RenameAsync("Folder", "Renamed"));
        Assert.True(File.Exists(_dir.Combine("Renamed/a.md")));

        // A page can be addressed without its extension.
        Assert.Equal("Newer.md", await _store.RenameAsync("New", "Newer.md"));
    }

    [Fact]
    public async Task Rename_OntoExisting_Throws()
    {
        await _store.SavePageAsync("a.md", "a", null);
        await _store.SavePageAsync("b.md", "b", null);
        await Assert.ThrowsAsync<KnowledgeAlreadyExistsException>(() => _store.RenameAsync("a.md", "b"));
        Assert.Equal("a", (await _store.ReadPageAsync("a.md")).Markdown);
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("CON")]
    [InlineData(".hidden")]
    public async Task Rename_ToInvalidName_Throws(string newName)
    {
        await _store.SavePageAsync("a.md", "a", null);
        await Assert.ThrowsAsync<InvalidKnowledgePathException>(() => _store.RenameAsync("a.md", newName));
    }

    [Fact]
    public async Task CaseOnlyRename_Works_AndLeavesNoTemporaryFiles()
    {
        await _store.SavePageAsync("Page.md", "x", null);
        await _store.SavePageAsync("Folder/a.md", "x", null);

        Assert.Equal("page.md", await _store.RenameAsync("Page.md", "page"));
        Assert.Equal("folder", await _store.RenameAsync("Folder", "folder"));

        var names = Directory.EnumerateFileSystemEntries(_dir.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["folder", "page.md"], names);
        Assert.Equal(["folder", "page.md"], (await _store.GetChildrenAsync("")).Select(c => c.Name));
    }

    [Fact]
    public void RecoverInterruptedRenames_RestoresTheOriginalName()
    {
        File.WriteAllText(_dir.Combine("~kgb-abc"), "content");
        File.WriteAllText(_dir.Combine("~kgb-abc.restore"), "Original.md");
        Directory.CreateDirectory(_dir.Combine("~kgb-dir"));
        File.WriteAllText(_dir.Combine("~kgb-dir/x.md"), "x");
        File.WriteAllText(_dir.Combine("~kgb-dir.restore"), "Folder");

        Assert.Equal(2, _store.RecoverInterruptedRenames());

        Assert.Equal("content", File.ReadAllText(_dir.Combine("Original.md")));
        Assert.True(File.Exists(_dir.Combine("Folder/x.md")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir.Path, "~kgb-*"));
    }

    [Fact]
    public async Task Move_PageAndFolder()
    {
        await _store.SavePageAsync("a.md", "x", null);
        await _store.CreateFolderAsync("Target");
        Assert.Equal("Target/a.md", await _store.MoveAsync("a.md", "Target"));

        await _store.SavePageAsync("Src/b.md", "x", null);
        Assert.Equal("Target/Src", await _store.MoveAsync("Src", "Target"));
        Assert.True(File.Exists(_dir.Combine("Target/Src/b.md")));

        Assert.Equal("Src", await _store.MoveAsync("Target/Src", ""));
        await Assert.ThrowsAsync<KnowledgeNotFoundException>(() => _store.MoveAsync("Src", "Missing"));
    }

    [Fact]
    public async Task Move_FolderIntoItselfOrADescendant_Throws()
    {
        await _store.SavePageAsync("A/B/c.md", "x", null);
        await Assert.ThrowsAsync<InvalidKnowledgePathException>(() => _store.MoveAsync("A", "A"));
        await Assert.ThrowsAsync<InvalidKnowledgePathException>(() => _store.MoveAsync("A", "A/B"));
        Assert.True(File.Exists(_dir.Combine("A/B/c.md")));
    }

    [Fact]
    public async Task Move_OntoExisting_Throws()
    {
        await _store.SavePageAsync("a.md", "root", null);
        await _store.SavePageAsync("T/a.md", "nested", null);
        await Assert.ThrowsAsync<KnowledgeAlreadyExistsException>(() => _store.MoveAsync("a.md", "T"));
    }

    [Fact]
    public async Task SymbolicLinks_AreHidden_AndRefused()
    {
        await _store.SavePageAsync("Real/a.md", "x", null);
        var outside = new TempDir();
        try
        {
            File.WriteAllText(Path.Combine(outside.Path, "secret.md"), "secret");
            try
            {
                Directory.CreateSymbolicLink(_dir.Combine("Link"), outside.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return; // Creating symlinks needs Developer Mode or admin rights on Windows: skip.
            }

            Assert.Equal(["Real"], (await _store.GetChildrenAsync("")).Select(c => c.Name));
            Assert.DoesNotContain(await _store.GetAllPagePathsAsync(), p => p.Contains("secret"));
            await Assert.ThrowsAsync<InvalidKnowledgePathException>(() => _store.DeleteAsync("Link"));
            await Assert.ThrowsAsync<InvalidKnowledgePathException>(() => _store.RenameAsync("Link", "Other"));
            Assert.True(File.Exists(Path.Combine(outside.Path, "secret.md")));
        }
        finally
        {
            if (Directory.Exists(_dir.Combine("Link"))) Directory.Delete(_dir.Combine("Link"));
            outside.Dispose();
        }
    }

    [Fact]
    public async Task SizeLimit_IsEnforcedInUtf8Bytes()
    {
        var store = StoreWithLimit(10);
        Assert.Equal(SaveStatus.Created, (await store.SavePageAsync("ok.md", "0123456789", null)).Status);
        var ex = await Assert.ThrowsAsync<PageTooLargeException>(() => store.SavePageAsync("big.md", "ééééé é", null)); // 13 bytes
        Assert.Equal(13, ex.Bytes);
        Assert.False(File.Exists(_dir.Combine("big.md")));
    }

    [Fact]
    public async Task Delete_UsesTheFileDeleter()
    {
        await _store.SavePageAsync("a.md", "x", null);
        await _store.SavePageAsync("F/b.md", "x", null);

        await _store.DeleteAsync("a");
        await _store.DeleteAsync("F");

        Assert.Equal([Path.GetFullPath(_dir.Combine("a.md"))], _deleter.Files);
        Assert.Equal([Path.GetFullPath(_dir.Combine("F"))], _deleter.Directories);
        Assert.Empty(await _store.GetChildrenAsync(""));
        await Assert.ThrowsAsync<KnowledgeNotFoundException>(() => _store.DeleteAsync("a.md"));
        await Assert.ThrowsAsync<InvalidKnowledgePathException>(() => _store.DeleteAsync(""));
    }

    [Fact]
    public async Task Exists_PagesAndFolders()
    {
        await _store.SavePageAsync("F/a.md", "x", null);
        Assert.True(await _store.ExistsAsync("F"));
        Assert.True(await _store.ExistsAsync("F/a.md"));
        Assert.False(await _store.ExistsAsync("F/b.md"));
        Assert.False(await _store.ExistsAsync("../x"));
    }

    [Fact]
    public async Task SavePage_UnderAFile_Throws()
    {
        await _store.SavePageAsync("a.md", "x", null);
        File.WriteAllText(_dir.Combine("blocker"), "file");
        await Assert.ThrowsAsync<KnowledgeAlreadyExistsException>(() => _store.SavePageAsync("blocker/x.md", "x", null));
    }
}
