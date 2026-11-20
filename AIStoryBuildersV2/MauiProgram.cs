using AIStoryBuildersV2.Embeddings.Onnx;
using AIStoryBuildersV2.Services;
using AIStoryBuildersV2.Shared.Hosting;
using AIStoryBuildersV2.Shared.Knowledge;
using AIStoryBuildersV2.Shared.Llm;
using AIStoryBuildersV2.Shared.Settings;
using AIStoryBuildersV2.Shared.Stories;
using AIStoryBuildersV2.Shared.Stories.Storage;
using CommunityToolkit.Maui;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AIStoryBuildersV2;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

#if WINDOWS
        Platforms.Windows.WindowsShell.Configure(builder);
#endif

        // V1's folder and file formats (D4). V2 adds only {Title}/KnowledgeBase/.
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AIStoryBuilders");
        EnsureStoriesRoot(root);

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(5) });
        builder.Services.AddSingleton<IHostCapabilities>(new MauiHostCapabilities(root));
        builder.Services.AddSingleton<IFileExportService, MauiFileExportService>();
        builder.Services.AddSingleton<IStoryPersistence, DiskStoryPersistence>();
        builder.Services.AddSingleton<IStoryLibraryStore>(new CsvStoryLibraryStore(root));
        builder.Services.AddSingleton<IAppDataStore>(new FileAppDataStore(root));
        builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new LocalEmbeddingGenerator());
        builder.Services.AddSingleton<ISettingsStore, MauiSettingsStore>();
        builder.Services.AddSingleton<IFileDeleter, RecycleBinFileDeleter>();
        // D34: the Blazor DataExchange gateway. MauiAppBuilder.Configuration has no settings file, so its values (if a
        // source is ever added) win over these defaults: DEBUG points at the local gateway and exchange; Release is
        // empty, so the user types the gateway URL in Settings. No connect link reaches the desktop app; the user
        // pastes a token or a connect code in Settings instead.
#if DEBUG
        const string DataExchangeBaseUrl = "https://localhost:7084/v1/";
        const string DataExchangeExchangeUrl = "https://localhost:7220/";
#else
        const string DataExchangeBaseUrl = "";
        const string DataExchangeExchangeUrl = "";
#endif
        builder.Services.AddSingleton(DataExchangeOptions.From(key => builder.Configuration[key], DataExchangeBaseUrl, DataExchangeExchangeUrl));
        builder.Services.AddAIStoryBuildersV2Shared();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        // Load the log before the first page writes to it. The host may block here; Shared code never does (D25).
        Task.Run(() => app.Services.GetRequiredService<LogService>().InitializeAsync()).GetAwaiter().GetResult();

        return app;
    }

    /// <summary>
    /// Creates only the stories root and V1's three shared files when they are missing (plan §8). V2 never creates
    /// AIStoryBuildersSettings.config or ModelCache/, which V1 creates (D7).
    /// </summary>
    static void EnsureStoriesRoot(string root)
    {
        Directory.CreateDirectory(root);

        var stories = Path.Combine(root, "AIStoryBuildersStories.csv");
        if (!File.Exists(stories)) File.WriteAllText(stories, Environment.NewLine);

        var log = Path.Combine(root, "AIStoryBuildersLog.csv");
        if (!File.Exists(log)) File.WriteAllText(log, "Application started at " + DateTime.Now + Environment.NewLine);

        var database = Path.Combine(root, "AIStoryBuildersDatabase.json");
        if (!File.Exists(database)) File.WriteAllText(database, "{" + Environment.NewLine + "}" + Environment.NewLine);
    }
}
