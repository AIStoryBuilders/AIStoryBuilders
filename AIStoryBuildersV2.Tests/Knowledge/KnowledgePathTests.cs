using AIStoryBuildersV2.Shared.Knowledge;

namespace AIStoryBuildersV2.Tests;

public class KnowledgePathTests
{
    [Theory]
    [InlineData("Welcome", "Welcome.md")]
    [InlineData("Welcome.md", "Welcome.md")]
    [InlineData("Welcome.MD", "Welcome.md")]
    [InlineData("Getting Started/Install", "Getting Started/Install.md")]
    [InlineData("Getting Started\\Install.md", "Getting Started/Install.md")]
    [InlineData("  Projects/Alpha/Overview  ", "Projects/Alpha/Overview.md")]
    [InlineData("v1.2 notes", "v1.2 notes.md")]
    [InlineData("Ünïcødé/页面", "Ünïcødé/页面.md")]
    public void Page_IsNormalized_WithExactlyOneExtension(string raw, string expected)
    {
        Assert.True(KnowledgePath.TryParse(raw, isPage: true, out var path, out var error), error);
        Assert.Equal(expected, path.Value);
        Assert.Equal(expected, KnowledgePath.Parse(raw, isPage: true).Value);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("Getting Started", "Getting Started")]
    [InlineData("a\\b\\c", "a/b/c")]
    [InlineData("notes.md", "notes.md")]
    public void Folder_IsNormalized(string? raw, string expected)
    {
        Assert.True(KnowledgePath.TryParse(raw, isPage: false, out var path, out var error), error);
        Assert.Equal(expected, path.Value);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../x")]
    [InlineData("a/../b")]
    [InlineData(".")]
    [InlineData("a/./b")]
    [InlineData("a//b")]
    [InlineData("a/")]
    [InlineData("/a")]
    [InlineData("\\a")]
    [InlineData("C:/x")]
    [InlineData("c:x")]
    [InlineData("\\\\server\\share\\x")]
    [InlineData("//server/share/x")]
    [InlineData("a:b")]
    [InlineData("ab:c")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a\"b")]
    [InlineData("a|b")]
    [InlineData("a?b")]
    [InlineData("a*b")]
    [InlineData("a\tb")]
    [InlineData("a\nb")]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("folder/NUL")]
    [InlineData("COM1")]
    [InlineData("lpt9")]
    [InlineData("aux.txt")]
    [InlineData("trailing./x")]
    [InlineData("trailing /x")]
    [InlineData("a/ leading")]
    [InlineData(".hidden")]
    [InlineData("a/.git/b")]
    [InlineData("~kgb-temp")]
    [InlineData("a/~KGB-temp")]
    public void Invalid_IsRejected(string raw)
    {
        Assert.False(KnowledgePath.TryParse(raw, isPage: false, out _, out var folderError));
        Assert.False(string.IsNullOrEmpty(folderError));
        Assert.False(KnowledgePath.TryParse(raw, isPage: true, out _, out var pageError));
        Assert.False(string.IsNullOrEmpty(pageError));
        Assert.Throws<InvalidKnowledgePathException>(() => KnowledgePath.Parse(raw, isPage: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".md")]
    [InlineData("folder/.md")]
    public void Page_RequiresAName(string raw)
    {
        Assert.False(KnowledgePath.TryParse(raw, isPage: true, out _, out _));
    }

    [Fact]
    public void Root_IsValidForFolders()
    {
        Assert.True(KnowledgePath.TryParse("", isPage: false, out var root, out _));
        Assert.True(root.IsRoot);
        Assert.Equal("", root.Value);
    }

    [Fact]
    public void Length_Over240_IsRejected()
    {
        var ok = new string('a', 237);                     // + ".md" = 240
        Assert.True(KnowledgePath.TryParse(ok, isPage: true, out var p, out _));
        Assert.Equal(240, p.Value.Length);

        var tooLong = new string('a', 238);                // + ".md" = 241
        Assert.False(KnowledgePath.TryParse(tooLong, isPage: true, out _, out _));
        Assert.False(KnowledgePath.TryParse(new string('b', 241), isPage: false, out _, out _));
    }

    [Fact]
    public void Parent_And_Name()
    {
        var p = KnowledgePath.Parse("a/b/c", isPage: true);
        Assert.Equal("c.md", p.Name);
        Assert.Equal("a/b", p.Parent.Value);
        Assert.Equal("a", p.Parent.Parent.Value);
        Assert.True(p.Parent.Parent.Parent.IsRoot);

        var top = KnowledgePath.Parse("top", isPage: true);
        Assert.Equal("top.md", top.Name);
        Assert.True(top.Parent.IsRoot);
        Assert.Equal("", KnowledgePath.Root.Name);
    }

    [Theory]
    [InlineData("a/b.md", "a", true)]
    [InlineData("a/b/c.md", "a", true)]
    [InlineData("A/b.md", "a", true)]
    [InlineData("ab/c.md", "a", false)]
    [InlineData("a.md", "a", false)]
    [InlineData("a", "a", false)]
    [InlineData("a/b", "a/b", false)]
    [InlineData("x.md", "", true)]
    public void IsUnder_UsesPrefixWithSlash(string path, string folder, bool expected)
    {
        var p = KnowledgePath.Parse(path, isPage: false);
        var f = KnowledgePath.Parse(folder, isPage: false);
        Assert.Equal(expected, p.IsUnder(f));
    }

    [Fact]
    public void Root_IsNotUnderRoot()
    {
        Assert.False(KnowledgePath.Root.IsUnder(KnowledgePath.Root));
    }

    [Fact]
    public void Combine_And_Helpers()
    {
        Assert.Equal("x.md", KnowledgePath.Root.Combine("x.md").Value);
        Assert.Equal("a/x.md", KnowledgePath.Parse("a", false).Combine("x.md").Value);
        Assert.True(KnowledgePath.IsPagePath("a/B.MD"));
        Assert.False(KnowledgePath.IsPagePath("a/b.txt"));
        Assert.Equal("a/b", KnowledgePath.StripExtension("a/b.md"));
        Assert.Equal("a/b.txt", KnowledgePath.StripExtension("a/b.txt"));
    }
}
