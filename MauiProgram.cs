using AIStoryBuilders.Models;
using AIStoryBuilders.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Radzen;

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

            string DefaultConnection = "Data Source=(local);initial catalog=AIStoryBuilders;TrustServerCertificate=True;persist security info=True;user id=databaseuser;password=password;";

            // Database connection
            builder.Services.AddDbContext<AIStoryBuildersContext>(options =>
            options.UseSqlServer(DefaultConnection));

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