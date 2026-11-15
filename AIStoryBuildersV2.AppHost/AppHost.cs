var builder = DistributedApplication.CreateBuilder(args);

// Dev-time only: launches both apps. Neither app reads anything from Aspire.
builder.AddMauiProject("aistorybuildersv2", "../AIStoryBuildersV2/AIStoryBuildersV2.csproj")
       .AddWindowsDevice();                                    // resource "aistorybuildersv2-windows"
builder.AddProject<Projects.AIStoryBuildersV2_Web>("web"); // the standalone WASM dev server

builder.Build().Run();
