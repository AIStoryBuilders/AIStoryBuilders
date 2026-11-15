using AIStoryBuildersV2.Shared;
using AIStoryBuildersV2.Shared.Hosting;
using AIStoryBuildersV2.Shared.Knowledge.Persistence;
using AIStoryBuildersV2.Shared.Services;
using AIStoryBuildersV2.Shared.Settings;
using AIStoryBuildersV2.Shared.Stories;
using AIStoryBuildersV2.Shared.Stories.Storage;
using AIStoryBuildersV2.Web.Services;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.AI;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The open story is extracted into the .NET in-memory file system (MEMFS); the durable copy is a zip in localStorage (D3, D5).
var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
var root = Path.Combine(string.IsNullOrEmpty(documents) ? "/home/web_user/Documents" : documents, "AIStoryBuilders");
Directory.CreateDirectory(root);

builder.Services.AddBlazoredLocalStorageAsSingleton();
// AI providers are called directly from the browser, so the client has no base address.
builder.Services.AddSingleton(_ => new HttpClient());
builder.Services.AddSingleton<IHostCapabilities>(new BrowserHostCapabilities(root));
builder.Services.AddScoped<IFileExportService, BrowserFileExportService>();
builder.Services.AddSingleton<IKeyValueStore, LocalStorageKeyValueStore>();
builder.Services.AddSingleton<ZipStoryPersistence>();
builder.Services.AddSingleton<IStoryPersistence>(sp => sp.GetRequiredService<ZipStoryPersistence>());
builder.Services.AddSingleton<StorageEventBridge>();
builder.Services.AddSingleton<IStoryIndexLock, BrowserStoryIndexLock>();
builder.Services.AddSingleton<IStoryLibraryStore, LocalStorageStoryLibraryStore>();
builder.Services.AddSingleton<IAppDataStore, LocalStorageAppDataStore>();
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, BrowserEmbeddingGenerator>();
builder.Services.AddSingleton<ISettingsStore, BrowserSettingsStore>();
builder.Services.AddSingleton<IUpdateCheck, OnlineUpdateCheck>();
builder.Services.AddAIStoryBuildersV2Shared();

var host = builder.Build();
await host.Services.GetRequiredService<LogService>().InitializeAsync();
var bridge = host.Services.GetRequiredService<StorageEventBridge>();
var settings = host.Services.GetRequiredService<AppSettingsService>();
// Another tab changed the settings: reload them (KGB's StorageEventBridge).
bridge.KeyChanged += key => { if (key is null || key == StoryStorageConstants.SettingsKey) _ = settings.ReloadAsync(); };
await bridge.StartAsync();
await host.RunAsync();
