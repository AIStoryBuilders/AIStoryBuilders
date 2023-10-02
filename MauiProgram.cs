using AIStoryBuilders.Model;
using AIStoryBuilders.Models;
using AIStoryBuilders.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;
using Radzen;
using System.Reflection;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Runtime.Intrinsics.X86;
using System.Windows.Forms;

namespace AIStoryBuilders
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif            
            // Add services to the container.
            AppMetadata appMetadata = new AppMetadata() { Version = "00.01.00"};
            builder.Services.AddSingleton(appMetadata);

            builder.Services.AddScoped<AIStoryBuildersService>();
            builder.Services.AddSingleton<LogService>();
            builder.Services.AddSingleton<SettingsService>();
            builder.Services.AddSingleton<OrchestratorMethods>();

            // Radzen
            builder.Services.AddScoped<DialogService>();
            builder.Services.AddScoped<NotificationService>();
            builder.Services.AddScoped<TooltipService>();
            builder.Services.AddScoped<ContextMenuService>();

            // Load Default files
            var folderPath = "";
            var filePath = "";

            // AIStoryBuilders Directory
            folderPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}/AIStoryBuilders";
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            // AIStoryBuildersLog.csv
            filePath = Path.Combine(folderPath, "AIStoryBuildersLog.csv");

            if (!File.Exists(filePath))
            {
                using (var streamWriter = new StreamWriter(filePath))
                {
                    streamWriter.WriteLine("Application started at " + DateTime.Now);
                }
            }
            else
            {
                // File already exists
                string[] AIStoryBuildersLog;

                // Open the file to get existing content
                using (var file = new System.IO.StreamReader(filePath))
                {
                    AIStoryBuildersLog = file.ReadToEnd().Split('\n');

                    if (AIStoryBuildersLog[AIStoryBuildersLog.Length - 1].Trim() == "")
                    {
                        AIStoryBuildersLog = AIStoryBuildersLog.Take(AIStoryBuildersLog.Length - 1).ToArray();
                    }
                }

                // Append the text to csv file
                using (var streamWriter = new StreamWriter(filePath))
                {
                    streamWriter.WriteLine(string.Join("\n", "Application started at " + DateTime.Now));
                    streamWriter.WriteLine(string.Join("\n", AIStoryBuildersLog));
                }
            }

            // AIStoryBuildersMemory.csv
            filePath = Path.Combine(folderPath, "AIStoryBuildersMemory.csv");

            if (!File.Exists(filePath))
            {
                using (var streamWriter = new StreamWriter(filePath))
                {
                    streamWriter.WriteLine("** AIStoryBuildersMemory started at " + DateTime.Now + "|");
                }
            }

            // AIStoryBuildersDatabase.json
            filePath = Path.Combine(folderPath, "AIStoryBuildersDatabase.json");

            if (!File.Exists(filePath))
            {
                using (var streamWriter = new StreamWriter(filePath))
                {
                    streamWriter.WriteLine(
                        """
                        {                         
                        }
                        """);
                }
            }

            // AIStoryBuildersStories.json
            filePath = Path.Combine(folderPath, "AIStoryBuildersStories.csv");
            if (!File.Exists(filePath))
            {
                using (var streamWriter = new StreamWriter(filePath))
                {
                    streamWriter.WriteLine("A Beautiful Day|Comedy|People discover that any day can be a beautiful day.|Mary wakes up thinking today will not be a good day.After a series of events, she realizes that any day can be a beautiful day.");
                }
            }

            // AIStoryBuildersSettings.config
            filePath = Path.Combine(folderPath, "AIStoryBuildersSettings.config");
            if (!File.Exists(filePath))
            {
                using (var streamWriter = new StreamWriter(filePath))
                {
                    streamWriter.WriteLine(
                    """
                        {
                         "OpenAIServiceOptions": {
                         "Organization": "",
                         "ApiKey": "" } 
                        }
                        """
                    );
                }
            }

            return builder.Build();
        }
    }
}