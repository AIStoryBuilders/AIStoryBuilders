using AIStoryBuildersV2.Shared.Knowledge.Persistence;
using AIStoryBuildersV2.Shared.Models;
using AIStoryBuildersV2.Shared.Stories;

namespace AIStoryBuildersV2.Shared.Services;

/// <summary>Tells the app shell whether a newer version has been published. Never throws.</summary>
public interface IUpdateCheck
{
    Task<bool> IsUpdateAvailableAsync();
}

/// <summary>The default for hosts that don't check, such as the Windows app (plan Q24).</summary>
public sealed class NoUpdateCheck : IUpdateCheck
{
    public Task<bool> IsUpdateAvailableAsync() => Task.FromResult(false);
}

/// <summary>
/// The browser app's check, as in V1 Online's MainLayout: one request per app start with a random browser GUID and the
/// app version (plan Q24). It sends nothing else, and a failed check just shows no notice.
/// </summary>
public sealed class OnlineUpdateCheck(HttpClient http, IKeyValueStore store, AppMetadata app) : IUpdateCheck
{
    Task<bool>? _check;

    public Task<bool> IsUpdateAvailableAsync() => _check ??= CheckAsync();

    async Task<bool> CheckAsync()
    {
        try
        {
            var guid = await GetOrCreateGuidAsync();
            using var response = await http.GetAsync($"https://aistorybuilders.com/api/VersionOnline/getversiononline?paramGUID={guid}&paramCurrentVersion={app.Version}");
            response.EnsureSuccessStatusCode();
            var published = await response.Content.ReadAsStringAsync();
            return ConvertToInteger(published) > ConvertToInteger(app.Version);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return false;
        }
    }

    async Task<string> GetOrCreateGuidAsync()
    {
        var guid = await store.GetAsync(StoryStorageConstants.BrowserGuidKey);
        if (!string.IsNullOrEmpty(guid)) return guid;

        guid = Guid.NewGuid().ToString();
        await store.SetAsync(StoryStorageConstants.BrowserGuidKey, guid);
        return guid;
    }

    /// <summary>V1's version number: "03.62.00" is 36200. More than three segments or a non-number throws.</summary>
    static int ConvertToInteger(string version)
    {
        int[] multipliers = [10000, 100, 1];
        var total = 0;
        var segments = version.Split('.');
        for (var i = 0; i < segments.Length; i++)
            total += Convert.ToInt32(segments[i]) * multipliers[i];
        return total;
    }
}
