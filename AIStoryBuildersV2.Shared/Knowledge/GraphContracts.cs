namespace AIStoryBuildersV2.Shared.Knowledge;

public enum LinkKind { Wiki, Markdown }

public sealed record PageInfo(string Path, string Title, IReadOnlyList<string> Tags, IReadOnlyList<string> Aliases, long ModifiedTicks);

public sealed record LinkRef(string SourcePath, string RawTarget, string? ResolvedPath, LinkKind Kind, int Line);

public sealed record PageLinks(IReadOnlyList<LinkRef> Outgoing, IReadOnlyList<LinkRef> Backlinks);

public sealed record SearchHit(string Path, string Title, string Snippet, double Score);

public sealed record GraphStats(int Pages, int Links, int Unresolved);

public sealed record Neighborhood(IReadOnlyList<PageInfo> Pages, IReadOnlyList<LinkRef> Links);

public interface IKnowledgeGraphQuery
{
    Task<GraphStats> RebuildAsync(CancellationToken ct = default);
    Task<PageLinks> GetLinksAsync(string path, CancellationToken ct = default);
    Task<IReadOnlyList<LinkRef>> GetUnresolvedLinksAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int top = 10, CancellationToken ct = default);
    Task<Neighborhood> GetNeighborhoodAsync(string path, int depth = 1, CancellationToken ct = default);
    Task<PageInfo?> GetPageInfoAsync(string path, CancellationToken ct = default);
    Task<string?> ResolveAsync(string sourcePath, string rawTarget, CancellationToken ct = default);
    Task<GraphStats> GetStatsAsync(CancellationToken ct = default);
}

public interface IKnowledgeGraphIndex : IKnowledgeGraphQuery
{
    Task UpdatePageAsync(string path, CancellationToken ct = default);
    Task RemovePathAsync(string path, CancellationToken ct = default);
    GraphSnapshot Snapshot { get; }
    bool IsBuilt { get; }
}
