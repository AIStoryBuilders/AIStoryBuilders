using System.Net;
using System.Text.Json;
using AIStoryBuildersV2.Shared.AI;
using AIStoryBuildersV2.Shared.Knowledge.Persistence;
using AIStoryBuildersV2.Shared.Llm;
using AIStoryBuildersV2.Shared.Settings;
using AIStoryBuildersV2.Shared.Stories;
using AIStoryBuildersV2.Shared.StoryChat;
using Microsoft.Extensions.AI;
using RichardSzalay.MockHttp;

namespace AIStoryBuildersV2.Tests;

/// <summary>The Blazor DataExchange provider and connect flow (plan D34, UI-Behavior-Spec §6.5).</summary>
public class DataExchangeTests
{
    const string Base = "https://gateway.test/v1/";
    const string Key = "dx_ABCDEFGH_secretsecretsecret";
    const string Prefix = "dx_ABCDEFGH";

    const string ChatCompletion = """
        {"id":"c1","object":"chat.completion","created":1,"model":"gemini-2.5-flash-lite",
         "choices":[{"index":0,"message":{"role":"assistant","content":"OK"},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}
        """;

    // ---- LlmDefaults and ChatClientFactory ----

    [Fact]
    public void Defaults_for_DataExchange()
    {
        Assert.Equal("Blazor DataExchange", LlmDefaults.DisplayName(LlmProvider.DataExchange));
        Assert.True(LlmDefaults.RequiresKey(LlmProvider.DataExchange));
        Assert.True(LlmDefaults.RequiresEndpoint(LlmProvider.DataExchange));
        Assert.Equal("", LlmDefaults.DefaultModel(LlmProvider.DataExchange));
        Assert.Equal(LlmProvider.DataExchange, LlmDefaults.Available().First());
        Assert.Equal("gateway.test", LlmDefaults.HostFor(LlmProvider.DataExchange, Base));
        Assert.Equal("localhost:7084", LlmDefaults.HostFor(LlmProvider.DataExchange, "https://localhost:7084/v1/"));
        Assert.Null(LlmDefaults.HostFor(LlmProvider.DataExchange, null));
    }

    [Fact]
    public void DataExchange_is_appended_to_the_enum_so_existing_values_keep_their_numbers()
    {
        Assert.Equal(LlmProvider.DataExchange, Enum.GetValues<LlmProvider>()[^1]);
        Assert.Equal(5, (int)LlmProvider.Fake);
        // Settings store the provider by name.
        Assert.Equal(LlmProvider.DataExchange, new StoredSettings("DataExchange", "m", Base, Key).ProviderValue);
    }

    [Fact]
    public async Task The_client_posts_to_the_configured_gateway_with_the_bearer_key()
    {
        var mock = new MockHttpMessageHandler();
        mock.Expect(HttpMethod.Post, Base + "chat/completions")
            .WithHeaders("Authorization", "Bearer " + Key)
            .Respond("application/json", ChatCompletion);
        var http = new HttpClient(mock) { Timeout = Timeout.InfiniteTimeSpan };

        var client = ChatClientFactory.Create(new LlmOptions(LlmProvider.DataExchange, "gemini-2.5-flash-lite", Base, Key), http, isBrowser: true);
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Reply with OK.")]);

        Assert.Equal("OK", response.Text);
        Assert.Equal("gemini-2.5-flash-lite", client.GetService<ChatClientMetadata>()?.DefaultModelId);
        mock.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task The_client_never_retries_a_failed_request()
    {
        var mock = new MockHttpMessageHandler();
        var calls = 0;
        mock.When(HttpMethod.Post, Base + "chat/completions").Respond(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("""{"error":{"message":"busy","type":"server_error","code":"upstream_error","param":null}}"""),
            };
        });
        var http = new HttpClient(mock) { Timeout = Timeout.InfiniteTimeSpan };

        var client = ChatClientFactory.Create(new LlmOptions(LlmProvider.DataExchange, "m", Base, Key), http, isBrowser: false);
        await Assert.ThrowsAnyAsync<Exception>(() => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        Assert.Equal(1, calls);
    }

    [Fact]
    public void A_short_HttpClient_timeout_is_replaced_so_the_five_minute_network_timeout_applies()
    {
        var shortClient = new HttpClient();   // 100 s default, as in the browser host
        Assert.NotSame(shortClient, ChatClientFactory.LongRunning(shortClient));
        Assert.Equal(Timeout.InfiniteTimeSpan, ChatClientFactory.LongRunning(shortClient).Timeout);

        var desktop = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        Assert.Same(desktop, ChatClientFactory.LongRunning(desktop));
        var infinite = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        Assert.Same(infinite, ChatClientFactory.LongRunning(infinite));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://gateway.test/v1/")]
    public void A_missing_or_invalid_gateway_URL_throws(string? endpoint)
    {
        Assert.Throws<ArgumentException>(() => ChatClientFactory.Create(new LlmOptions(LlmProvider.DataExchange, "m", endpoint, Key), new HttpClient(), false));
    }

    [Fact]
    public void A_missing_key_throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => ChatClientFactory.Create(new LlmOptions(LlmProvider.DataExchange, "m", Base, null), new HttpClient(), false));
        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public void Json_mode_is_sent_through_the_gateway()
    {
        Assert.Same(ChatResponseFormat.Json, ChatOptionsFactory.CreateJsonOptions(LlmProvider.DataExchange).ResponseFormat);
        Assert.Null(ChatOptionsFactory.CreateJsonOptions(LlmProvider.Anthropic).ResponseFormat);
    }

    [Theory]
    [InlineData(LlmProvider.GoogleAI, "gemini-2.5-flash", true)]
    [InlineData(LlmProvider.GoogleAI, "anything", true)]
    [InlineData(LlmProvider.DataExchange, "gemini-2.5-flash-lite", true)]
    [InlineData(LlmProvider.DataExchange, " Gemini-3-pro", true)]
    [InlineData(LlmProvider.DataExchange, "claude-sonnet-5-5", false)]
    [InlineData(LlmProvider.DataExchange, "gpt-4.1", false)]
    [InlineData(LlmProvider.DataExchange, null, false)]
    [InlineData(LlmProvider.OpenAI, "gemini-2.5-flash", false)]
    public void Story_chat_retries_without_tools_for_Gemini_models(LlmProvider provider, string? model, bool expected)
    {
        Assert.Equal(expected, StoryChatService.RetriesWithoutTools(provider, model));
    }

    [Fact]
    public void Error_messages_redact_gateway_keys()
    {
        var message = AppSettingsService.DescribeError(new InvalidOperationException("bad key " + Key), "gateway.test", false);
        Assert.DoesNotContain(Key, message);
        Assert.Contains("[redacted]", message);
    }

    // ---- ModelCatalog ----

    [Fact]
    public async Task Model_list_comes_from_the_gateway_without_the_gpt_filter()
    {
        var mock = new MockHttpMessageHandler();
        mock.Expect(HttpMethod.Get, Base + "models").WithHeaders("Authorization", "Bearer " + Key).Respond("application/json", """
            {"object":"list","data":[
             {"id":"gemini-2.5-flash-lite","object":"model","owned_by":"google","x_pricing":{"input_credits_per_1k":0.01}},
             {"id":"claude-sonnet-5-5","object":"model","owned_by":"anthropic"},
             {"id":"gpt-4.1","object":"model","owned_by":"openai"}]}
            """);

        var result = await ModelCatalog.ListAsync(new LlmOptions(LlmProvider.DataExchange, "", Base, Key), mock.ToHttpClient(), true, default);

        Assert.Equal(ModelListStatus.Success, result.Status);
        Assert.Equal(["gemini-2.5-flash-lite", "claude-sonnet-5-5", "gpt-4.1"], result.Models);
        mock.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task Model_list_resolves_under_v1_even_without_a_trailing_slash()
    {
        var mock = new MockHttpMessageHandler();
        mock.Expect(HttpMethod.Get, Base + "models").Respond("application/json", """{"object":"list","data":[{"id":"m"}]}""");

        var result = await ModelCatalog.ListAsync(new LlmOptions(LlmProvider.DataExchange, "", "https://gateway.test/v1", Key), mock.ToHttpClient(), false, default);

        Assert.Equal(["m"], result.Models);
    }

    [Fact]
    public async Task Model_list_reports_a_rejected_key_and_a_missing_gateway_URL()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(Base + "models").Respond(HttpStatusCode.Unauthorized);
        var rejected = await ModelCatalog.ListAsync(new LlmOptions(LlmProvider.DataExchange, "", Base, Key), mock.ToHttpClient(), false, default);
        Assert.Equal(ModelListStatus.InvalidKey, rejected.Status);

        var noUrl = await ModelCatalog.ListAsync(new LlmOptions(LlmProvider.DataExchange, "", null, Key), mock.ToHttpClient(), false, default);
        Assert.Equal(ModelListStatus.NotConfigured, noUrl.Status);
    }

    // ---- Settings ----

    [Fact]
    public async Task Saving_DataExchange_keeps_the_gateway_URL_and_verifies_for_its_host()
    {
        var kv = new InMemoryKeyValueStore();
        var service = new AppSettingsService(new BrowserSettingsStore(kv), new TestHost("") { IsBrowser = true }, new HttpClient(), new StubProbe());

        var r = await service.SaveAsync(LlmProvider.DataExchange, "gemini-2.5-flash-lite", Base, Key, default);

        Assert.True(r.Saved);
        var s = await service.GetAsync();
        Assert.Equal(LlmProvider.DataExchange, s.Provider);
        Assert.Equal(Base, s.Endpoint);
        Assert.Equal("gateway.test", Assert.Single(s.VerifiedModels).Host);
        Assert.Equal("DataExchange", JsonDocument.Parse(kv.Items[StoryStorageConstants.SettingsKey]).RootElement.GetProperty("provider").GetString());
    }

    // ---- Options ----

    [Theory]
    [InlineData("https://gateway.test/v1/", "https://gateway.test/v1/")]
    [InlineData(" https://gateway.test/v1 ", "https://gateway.test/v1/")]
    [InlineData("http://localhost:7084/v1/", "http://localhost:7084/v1/")]
    [InlineData("", null)]
    [InlineData("gateway.test/v1/", null)]
    [InlineData("ftp://gateway.test/v1/", null)]
    public void The_base_URL_is_normalized(string value, string? expected)
    {
        Assert.Equal(expected, new DataExchangeOptions { BaseUrl = value }.BaseUri?.ToString());
    }

    [Theory]
    [InlineData("https://exchange.test/", "https://exchange.test/#tokens")]
    [InlineData("https://exchange.test", "https://exchange.test/#tokens")]
    [InlineData("https://exchange.test/app/", "https://exchange.test/app/#tokens")]
    [InlineData("", null)]
    public void The_tokens_link_comes_from_the_exchange_URL(string value, string? expected)
    {
        Assert.Equal(expected, new DataExchangeOptions { ExchangeUrl = value }.TokensUrl);
    }

    [Fact]
    public void Options_are_read_from_configuration_with_host_defaults()
    {
        var config = new Dictionary<string, string?>
        {
            ["DataExchange:BaseUrl"] = " https://gateway.test/v1/ ",
            ["DataExchange:ExchangeUrl"] = "",
        };
        var o = DataExchangeOptions.From(k => config.GetValueOrDefault(k), "https://fallback.test/v1/", "https://exchange.test/");
        Assert.Equal("https://gateway.test/v1/", o.BaseUrl);
        Assert.Equal("https://exchange.test/", o.ExchangeUrl);   // blank falls back

        var empty = DataExchangeOptions.From(_ => null);
        Assert.Null(empty.BaseUri);
        Assert.Null(empty.TokensUrl);
    }

    // ---- Connect flow ----

    [Theory]
    [InlineData("abc123", "abc123")]
    [InlineData("  abc123 ", "abc123")]
    [InlineData("https://app.test/connect/dataexchange#code=abc123", "abc123")]
    [InlineData("#code=abc123", "abc123")]
    [InlineData("code=abc123", "abc123")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void A_pasted_code_or_connect_link_is_reduced_to_the_code(string? text, string? expected)
    {
        Assert.Equal(expected, DataExchangeConnectService.NormalizePastedCode(text));
    }

    [Fact]
    public async Task A_pasted_code_redeems_at_the_gateway_URL_from_the_form_when_nothing_is_configured()
    {
        // The desktop Release build has no configured gateway; the user typed it in Settings.
        var rig = new Rig { BaseUrl = "" };
        rig.Mock.Expect(HttpMethod.Post, "https://typed-gateway.test/v1/connect/redeem")
            .Respond("application/json", Redeemed(baseUrl: "https://typed-gateway.test/v1/"));

        var result = await rig.Build().ConnectAsync("abc", default, "https://typed-gateway.test/v1");

        Assert.Equal(DataExchangeConnectStatus.Connected, result.Status);
        Assert.Equal("https://typed-gateway.test/v1/", (await rig.Settings.GetAsync()).Endpoint);
        rig.Mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [InlineData("https://app.test/connect/dataexchange#code=abc123", "abc123")]
    [InlineData("https://app.test/connect/dataexchange#x=1&code=a%2Bb", "a+b")]
    [InlineData("https://app.test/connect/dataexchange#code=", null)]
    [InlineData("https://app.test/connect/dataexchange", null)]
    [InlineData("https://app.test/connect/dataexchange?code=abc", null)]
    [InlineData(null, null)]
    public void The_code_is_read_from_the_fragment_only(string? uri, string? expected)
    {
        Assert.Equal(expected, DataExchangeConnectService.ReadCodeFromFragment(uri));
    }

    sealed class Rig
    {
        public MockHttpMessageHandler Mock { get; } = new();
        public InMemoryKeyValueStore Kv { get; } = new();
        public StubProbe Probe { get; init; } = new();
        public LogService Log { get; } = new(new MemoryAppDataStore()) { SaveDelay = TimeSpan.FromMinutes(10) };
        public AppSettingsService Settings { get; private set; } = null!;
        public string BaseUrl { get; init; } = Base;

        public DataExchangeConnectService Build()
        {
            var host = new TestHost("") { IsBrowser = true };
            Settings = new AppSettingsService(new BrowserSettingsStore(Kv), host, new HttpClient(), Probe);
            return new DataExchangeConnectService(Mock.ToHttpClient(), new DataExchangeOptions { BaseUrl = BaseUrl }, Settings, host, Log);
        }
    }

    static string Redeemed(string? model = "gemini-2.5-flash-lite", string baseUrl = Base) =>
        JsonSerializer.Serialize(new { api_key = Key, key_prefix = Prefix, base_url = baseUrl, recommended_model = model, credits_available = 10 });

    static HttpResponseMessage Error(HttpStatusCode status, string code) => new(status)
    {
        Content = new StringContent($$$"""{"error":{"message":"m","type":"invalid_request_error","code":"{{{code}}}","param":null}}"""),
    };

    [Fact]
    public async Task Connect_redeems_the_code_once_and_saves_the_recommended_model()
    {
        var rig = new Rig();
        string? body = null;
        rig.Mock.Expect(HttpMethod.Post, Base + "connect/redeem").Respond(async req =>
        {
            body = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Redeemed()) };
        });
        var connect = rig.Build();

        var result = await connect.ConnectAsync("abc123", default);

        Assert.Equal(DataExchangeConnectStatus.Connected, result.Status);
        Assert.Equal("""{"code":"abc123","client":"AIStoryBuilders"}""", body);
        Assert.Equal(10m, result.Redeemed!.CreditsAvailable);
        var probed = Assert.Single(rig.Probe.Calls);
        Assert.Equal(new LlmOptions(LlmProvider.DataExchange, "gemini-2.5-flash-lite", Base, Key), probed);
        var saved = await rig.Settings.GetAsync();
        Assert.Equal(LlmProvider.DataExchange, saved.Provider);
        Assert.Equal("gemini-2.5-flash-lite", saved.Model);
        Assert.Equal(Base, saved.Endpoint);
        Assert.True(saved.ApiKeyConfigured);
        rig.Mock.VerifyNoOutstandingExpectation();

        // The key is never logged or printed; its prefix is.
        Assert.DoesNotContain(rig.Log.Lines, l => l.Contains(Key));
        Assert.Contains(rig.Log.Lines, l => l.Contains(Prefix));
        Assert.DoesNotContain(Key, result.ToString());
        Assert.DoesNotContain(Key, result.Redeemed.ToString());
    }

    [Fact]
    public async Task The_response_base_url_is_authoritative()
    {
        var rig = new Rig();
        rig.Mock.When(HttpMethod.Post, Base + "connect/redeem").Respond("application/json", Redeemed(baseUrl: "https://other-gateway.test/v1"));
        var result = await rig.Build().ConnectAsync("abc", default);

        Assert.Equal(DataExchangeConnectStatus.Connected, result.Status);
        Assert.Equal("https://other-gateway.test/v1/", (await rig.Settings.GetAsync()).Endpoint);
    }

    [Fact]
    public async Task A_model_that_fails_the_probe_saves_nothing_and_hands_back_the_key()
    {
        var rig = new Rig { Probe = new StubProbe(pass: false) };
        rig.Mock.When(HttpMethod.Post, Base + "connect/redeem").Respond("application/json", Redeemed());
        var result = await rig.Build().ConnectAsync("abc", default);

        Assert.Equal(DataExchangeConnectStatus.NeedsModel, result.Status);
        Assert.Equal(Key, result.Redeemed!.ApiKey);
        Assert.False(result.Save!.Saved);
        Assert.Contains("capability check", result.Message);
        Assert.Empty(rig.Kv.Items);
    }

    [Fact]
    public async Task No_recommended_model_asks_the_user_to_pick_one()
    {
        var rig = new Rig();
        rig.Mock.When(HttpMethod.Post, Base + "connect/redeem").Respond("application/json", Redeemed(model: null));
        var result = await rig.Build().ConnectAsync("abc", default);

        Assert.Equal(DataExchangeConnectStatus.NeedsModel, result.Status);
        Assert.Empty(rig.Probe.Calls);
        Assert.Empty(rig.Kv.Items);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "invalid_or_expired_code", DataExchangeConnectStatus.InvalidCode)]
    [InlineData(HttpStatusCode.Forbidden, "account_disabled", DataExchangeConnectStatus.AccountDisabled)]
    [InlineData(HttpStatusCode.Conflict, "token_limit_reached", DataExchangeConnectStatus.TokenLimit)]
    [InlineData(HttpStatusCode.InternalServerError, "server_error", DataExchangeConnectStatus.Failed)]
    public async Task Gateway_errors_map_to_friendly_states(HttpStatusCode status, string code, DataExchangeConnectStatus expected)
    {
        var rig = new Rig();
        var calls = 0;
        rig.Mock.When(HttpMethod.Post, Base + "connect/redeem").Respond(_ => { calls++; return Error(status, code); });
        var result = await rig.Build().ConnectAsync("abc", default);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Redeemed);
        Assert.Equal(1, calls);   // never retried
        Assert.Empty(rig.Probe.Calls);
        Assert.Empty(rig.Kv.Items);
    }

    [Fact]
    public async Task A_network_or_CORS_failure_is_reported_as_unreachable()
    {
        var rig = new Rig();
        rig.Mock.When(HttpMethod.Post, Base + "connect/redeem").Throw(new HttpRequestException("TypeError: Failed to fetch"));
        var result = await rig.Build().ConnectAsync("abc", default);

        Assert.Equal(DataExchangeConnectStatus.Unreachable, result.Status);
        Assert.Contains("gateway.test", result.Message);
        Assert.Contains("CORS", result.Message);
    }

    [Fact]
    public async Task Missing_configuration_or_code_never_calls_the_gateway()
    {
        var calls = 0;
        var unconfigured = new Rig { BaseUrl = "" };
        unconfigured.Mock.Fallback.Respond(_ => { calls++; return new HttpResponseMessage(HttpStatusCode.OK); });
        var r1 = await unconfigured.Build().ConnectAsync("abc", default);
        Assert.Equal(DataExchangeConnectStatus.NotConfigured, r1.Status);

        var noCode = new Rig();
        noCode.Mock.Fallback.Respond(_ => { calls++; return new HttpResponseMessage(HttpStatusCode.OK); });
        var r2 = await noCode.Build().ConnectAsync("  ", default);
        Assert.Equal(DataExchangeConnectStatus.InvalidCode, r2.Status);

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task A_success_without_a_key_is_an_unexpected_response()
    {
        var rig = new Rig();
        rig.Mock.When(HttpMethod.Post, Base + "connect/redeem").Respond("application/json", """{"base_url":"https://gateway.test/v1/"}""");
        var result = await rig.Build().ConnectAsync("abc", default);

        Assert.Equal(DataExchangeConnectStatus.Failed, result.Status);
        Assert.Null(result.Redeemed);
    }
}
