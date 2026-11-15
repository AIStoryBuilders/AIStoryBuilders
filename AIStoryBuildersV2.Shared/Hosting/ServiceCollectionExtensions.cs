using AIStoryBuildersV2.Shared.AI;
using AIStoryBuildersV2.Shared.Graph;
using AIStoryBuildersV2.Shared.Llm;
using AIStoryBuildersV2.Shared.Settings;
using AIStoryBuildersV2.Shared.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Radzen;

namespace AIStoryBuildersV2.Shared.Hosting;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared services used by both the MAUI (Windows) app and the Web (WASM) app (plan §5.3).
    /// The host registers its own services first: <see cref="IHostCapabilities"/>, <see cref="IFileExportService"/>,
    /// <see cref="IStoryPersistence"/>, <see cref="IStoryLibraryStore"/>, <see cref="IAppDataStore"/>,
    /// the embedding generator and an <see cref="HttpClient"/>.
    /// </summary>
    public static IServiceCollection AddAIStoryBuildersV2Shared(this IServiceCollection services)
    {
        services.AddRadzenComponents();
        // Scoped: in MAUI, IJSRuntime is scoped to the BlazorWebView, so a singleton would capture the root one and fail.
        services.AddScoped<BrowserInterop>();
        services.AddSingleton<AppSession>();
        // The browser host registers OnlineUpdateCheck (plan Q24).
        services.TryAddSingleton<IUpdateCheck, NoUpdateCheck>();

        // The .stybld and graph manifests carry V1's version as a fixed format version (D6).
        services.AddSingleton(new AppMetadata { Version = StoryStorageConstants.StybldFormatVersion });
        services.TryAddSingleton<IEmbeddingGenerator<string, Embedding<float>>, UnavailableEmbeddingGenerator>();
        services.AddSingleton<LogService>();
        services.AddSingleton<OrchestratorMethods>();
        services.AddSingleton<StoryGraphCache>();
        services.AddSingleton<IGraphBuilder, GraphBuilder>();
        services.AddSingleton<AIStoryBuildersService>();

        // AI settings and the capability gate (D7, D9, D10). The host registers ISettingsStore.
        services.AddSingleton<IModelCapabilityProbe, ModelCapabilityProbe>();
        services.AddSingleton<AppSettingsService>();
        services.AddSingleton<IAppSettingsService>(sp => sp.GetRequiredService<AppSettingsService>());
        services.AddSingleton<IChatClientProvider>(sp => sp.GetRequiredService<AppSettingsService>());

        // Story graph (D17, D26) and manuscript import.
        services.AddSingleton<IGraphQueryService, GraphQueryService>();
        services.AddSingleton<IGraphMutationService, GraphMutationService>();
        services.AddSingleton<ITimelineSummaryGenerator, TimelineSummaryGenerator>();
        services.AddSingleton<ManuscriptParsingService>();
        services.AddSingleton<ParagraphAiHelper>();
        services.AddSingleton<StoryChat.IStoryChatService, StoryChat.StoryChatService>();

        // Per-story knowledge bases (D12, D13). MAUI replaces the deleter with the Recycle Bin one.
        services.TryAddSingleton<Knowledge.IFileDeleter, Knowledge.PermanentFileDeleter>();
        services.AddSingleton<Knowledge.IStoryKnowledgeBaseProvider, Knowledge.StoryKnowledgeBaseProvider>();
        services.AddScoped<Knowledge.ICurrentStoryKnowledgeBase, Knowledge.CurrentStoryKnowledgeBase>();

        // "Search knowledgebase" (D14-D16).
        services.AddSingleton<KnowledgeAssist.KnowledgeSearchChoice>();
        services.AddSingleton<KnowledgeAssist.IKnowledgeContextRetriever, KnowledgeAssist.KnowledgeContextRetriever>();
        return services;
    }
}
