using AIStoryBuilders.Models;
using AIStoryBuilders.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Radzen;
using System.Reflection;

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
            // Database connection
            builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            builder.Services.AddDbContext<AIStoryBuildersContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

            // Add services to the container.
            AppMetadata appMetadata = new AppMetadata() { Version = "00.01.00"};
            builder.Services.AddSingleton(appMetadata);

            builder.Services.AddScoped<AIStoryBuildersService>();

            // Radzen
            builder.Services.AddScoped<DialogService>();
            builder.Services.AddScoped<NotificationService>();
            builder.Services.AddScoped<TooltipService>();
            builder.Services.AddScoped<ContextMenuService>();

            return builder.Build();
        }
    }
}