using System.Net;
using AIStoryBuildersV2.Shared.Hosting;
using AIStoryBuildersV2.Shared.Knowledge.Persistence;
using AIStoryBuildersV2.Shared.Models;
using AIStoryBuildersV2.Shared.Services;
using AIStoryBuildersV2.Shared.Stories;
using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;

namespace AIStoryBuildersV2.Tests;

/// <summary>The browser app's version call matches V1 Online's MainLayout (plan Q24).</summary>
public class UpdateCheckTests
{
    const string Url = "https://aistorybuilders.com/api/VersionOnline/getversiononline";
    const string Version = "03.62.00";

    readonly InMemoryKeyValueStore _kv = new();
    readonly MockHttpMessageHandler _http = new();

    OnlineUpdateCheck Check() => new(_http.ToHttpClient(), _kv, new AppMetadata { Version = Version });

    [Theory]
    [InlineData("03.62.01", true)]
    [InlineData("03.63.00", true)]
    [InlineData("04.00.00", true)]
    [InlineData("03.62.00", false)]
    [InlineData("03.00.00", false)]
    public async Task Reports_an_update_only_when_the_published_version_is_newer(string published, bool expected)
    {
        _http.When(Url).Respond("text/plain", published);

        Assert.Equal(expected, await Check().IsUpdateAvailableAsync());
    }

    [Fact]
    public async Task Sends_the_stored_guid_and_the_app_version()
    {
        await _kv.SetAsync(StoryStorageConstants.BrowserGuidKey, "stored-guid");
        var request = _http.Expect(Url).WithExactQueryString($"paramGUID=stored-guid&paramCurrentVersion={Version}").Respond("text/plain", "03.00.00");

        await Check().IsUpdateAvailableAsync();

        Assert.Equal(1, _http.GetMatchCount(request));
    }

    [Fact]
    public async Task Creates_one_guid_and_reuses_it_on_the_next_app_start()
    {
        var sent = new List<string?>();
        _http.When(Url).Respond(request =>
        {
            sent.Add(request.RequestUri!.Query);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("03.00.00") };
        });

        await Check().IsUpdateAvailableAsync();
        await Check().IsUpdateAvailableAsync();

        var guid = await _kv.GetAsync(StoryStorageConstants.BrowserGuidKey);
        Assert.True(Guid.TryParse(guid, out _));
        Assert.Equal(2, sent.Count);
        Assert.All(sent, query => Assert.Contains($"paramGUID={guid}&", query));
    }

    [Fact]
    public async Task Calls_the_api_once_per_app_start()
    {
        var request = _http.When(Url).Respond("text/plain", "04.00.00");
        var check = Check();

        Assert.True(await check.IsUpdateAvailableAsync());
        Assert.True(await check.IsUpdateAvailableAsync());

        Assert.Equal(1, _http.GetMatchCount(request));
    }

    [Theory]
    [InlineData("not a version")]
    [InlineData("1.2.3.4")]
    [InlineData("<html></html>")]
    public async Task A_bad_reply_shows_no_notice(string published)
    {
        _http.When(Url).Respond("text/plain", published);

        Assert.False(await Check().IsUpdateAvailableAsync());
    }

    [Fact]
    public async Task A_server_error_shows_no_notice()
    {
        _http.When(Url).Respond(HttpStatusCode.InternalServerError);

        Assert.False(await Check().IsUpdateAvailableAsync());
    }

    [Fact]
    public async Task An_unreachable_server_shows_no_notice()
    {
        _http.When(Url).Throw(new HttpRequestException("offline"));

        Assert.False(await Check().IsUpdateAvailableAsync());
    }

    [Fact]
    public void Hosts_that_register_nothing_make_no_check()
    {
        using var provider = new ServiceCollection().AddAIStoryBuildersV2Shared().BuildServiceProvider();

        Assert.IsType<NoUpdateCheck>(provider.GetRequiredService<IUpdateCheck>());
    }

    [Fact]
    public void The_browser_registration_survives_the_shared_defaults()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddSingleton<IKeyValueStore>(_kv);
        services.AddSingleton<IUpdateCheck, OnlineUpdateCheck>();
        services.AddAIStoryBuildersV2Shared();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<OnlineUpdateCheck>(provider.GetRequiredService<IUpdateCheck>());
    }
}
