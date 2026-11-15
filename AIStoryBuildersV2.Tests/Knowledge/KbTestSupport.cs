using AIStoryBuildersV2.Shared.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIStoryBuildersV2.Tests;

/// <summary>KGB's test workspace: a file store, graph and workspace on a temp folder, optionally with KGB's seed pages.</summary>
public sealed class WorkspaceFixture : IDisposable
{
    public WorkspaceFixture(bool seed = true)
    {
        Store = new FileSystemKnowledgeStore(new KnowledgeStoreOptions(Dir.Path), new PermanentFileDeleter());
        Graph = new KnowledgeGraphService(Store);
        Workspace = new LocalKnowledgeWorkspace(Store, Graph, NullLogger<LocalKnowledgeWorkspace>.Instance);
        if (seed) KnowledgeSeeder.SeedIfEmptyAsync(Store).GetAwaiter().GetResult();
    }

    public TempDir Dir { get; } = new();
    public FileSystemKnowledgeStore Store { get; }
    public KnowledgeGraphService Graph { get; }
    public LocalKnowledgeWorkspace Workspace { get; }

    public void Dispose()
    {
        Workspace.Dispose();
        Dir.Dispose();
    }
}
