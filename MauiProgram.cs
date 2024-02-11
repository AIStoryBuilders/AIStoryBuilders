//
// AIStoryBuilders.com
// Copyright (c) 2024
// by Michael Washington
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated 
// documentation files (the "Software"), to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and 
// to permit persons to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or substantial portions 
// of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED 
// TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL 
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF 
// CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
//
//
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
using AIStoryBuilders.AI;
using CommunityToolkit.Maui;

namespace AIStoryBuilders
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            var builder = MauiApp.CreateBuilder();

            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif            
            // Add services to the container.
            AppMetadata appMetadata = new AppMetadata() { Version = "01.01.10" };
            builder.Services.AddSingleton(appMetadata);

            builder.Services.AddSingleton<LogService>();
            builder.Services.AddSingleton<SettingsService>();
            builder.Services.AddSingleton<DatabaseService>();
            builder.Services.AddSingleton<OrchestratorMethods>();
            builder.Services.AddSingleton<AIStoryBuildersService>();

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
                // create file with a blank line
                using (var streamWriter = new StreamWriter(filePath))
                {
                    streamWriter.WriteLine("");
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
                            "ApiKey": ""
                          },
                          "ApplicationSettings": {
                            "AIModel": "gpt-4-turbo-preview"
                          }
                        }
                        """
                    );
                }
            }

            return builder.Build();
        }
    }
}