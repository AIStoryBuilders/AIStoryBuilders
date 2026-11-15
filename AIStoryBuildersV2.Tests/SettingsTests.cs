using System.Net;
using System.Text.Json;
using AIStoryBuildersV2.Shared.AI;
using AIStoryBuildersV2.Shared.Knowledge;
using AIStoryBuildersV2.Shared.Knowledge.Persistence;
using AIStoryBuildersV2.Shared.Llm;
using AIStoryBuildersV2.Shared.Settings;
using AIStoryBuildersV2.Shared.Stories;
using RichardSzalay.MockHttp;

namespace AIStoryBuildersV2.Tests;

/// <summary>KGB's settings tests, adapted to V2's keys and the capability gate (plan §5.9, §10 AppSettingsService).</summary>
public class SettingsTests
{
    readonly InMemoryKeyValueStore _kv = new();
    readonly TestHost _host = new("") { IsBrowser = true };
    readonly StubProbe _probe = new();

    BrowserSettingsStore Store() => new(_kv);

    AppSettingsService Service(HttpClient? http = null, IModelCapabilityProbe? probe = null) =>
        new(Store(), _host, http ?? new HttpClient(), probe ?? _probe);

    [Fact]
    public async Task Load_returns_defaults_when_nothing_is_stored()
    {
        var s = await Store().LoadAsync();
        Assert.Equal(LlmProvider.Anthropic, s.ProviderValue);
        Assert.Null(s.ApiKey);
    }

    [Fact]
    public async Task Settings_live_in_asb2_settings_with_the_key_and_verified_models()
    {
        await Service().SaveAsync(LlmProvider.OpenAI, "gpt-4.1", null, "sk-test", default);
        Assert.Equal([StoryStorageConstants.SettingsKey], _kv.Items.Keys);
        var json = JsonDocument.Parse(_kv.Items["asb2:settings"]).RootElement;
        Assert.Equal("OpenAI", json.GetProperty("provider").GetString());
        Assert.Equal("sk-test", json.GetProperty("apiKey").GetString());
        var vm = json.GetProperty("verifiedModels")[0];
        Assert.Equal("OpenAI", vm.GetProperty("provider").GetString());
        Assert.Equal("api.openai.com", vm.GetProperty("host").GetString());
        Assert.Equal("gpt-4.1", vm.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Save_probes_only_unverified_models()
    {
        var service = Service();
        var first = await service.SaveAsync(LlmProvider.OpenAI, "gpt-4.1", null, "sk-1", default);
        Assert.True(first.Saved);
        Assert.NotNull(first.Probe);
        Assert.Single(_probe.Calls);
        Assert.Equal(new LlmOptions(LlmProvider.OpenAI, "gpt-4.1", null, "sk-1"), _probe.Calls[0]);

        var second = await service.SaveAsync(LlmProvider.OpenAI, "gpt-4.1", null, null, default);
        Assert.True(second.Saved);
        Assert.Null(second.Probe);
        Assert.Single(_probe.Calls);
    }

    [Fact]
    public async Task A_failed_probe_saves_nothing()
    {
        var service = Service(probe: new StubProbe(pass: false));
        var result = await service.SaveAsync(LlmProvider.OpenAI, "tiny", null, "sk-1", default);
        Assert.False(result.Saved);
        Assert.Equal(ProbeStage.ToolCall, result.Probe!.FailedAt);
        Assert.Empty(_kv.Items);
    }

    [Fact]
    public async Task Provider_or_host_changes_filter_verified_models_but_a_key_change_keeps_them()
    {
        var service = Service();
        await service.SaveAsync(LlmProvider.AzureOpenAI, "dep-a", "https://a.openai.azure.com/", "k1", default);
        await service.VerifyModelAsync("dep-b", default);
        Assert.Equal(["dep-a", "dep-b"], (await service.GetAsync()).VerifiedModels.Select(v => v.Model).Order());

        // Only the key changes: the list stays and nothing is probed.
        var calls = _probe.Calls.Count;
        await service.SaveAsync(LlmProvider.AzureOpenAI, "dep-b", "https://a.openai.azure.com/", "k2", default);
        Assert.Equal(calls, _probe.Calls.Count);
        Assert.Equal(2, (await service.GetAsync()).VerifiedModels.Count);

        // Another host: the old entries no longer match.
        await service.SaveAsync(LlmProvider.AzureOpenAI, "dep-a", "https://b.openai.azure.com/", null, default);
        Assert.Equal(["dep-a"], (await service.GetAsync()).VerifiedModels.Select(v => v.Model));
        Assert.Equal("b.openai.azure.com", (await service.GetAsync()).VerifiedModels[0].Host);
    }

    [Fact]
    public async Task A_probe_version_bump_clears_the_list()
    {
        await Store().SaveAsync(new StoredSettings("OpenAI", "gpt-4.1", null, "sk", [new VerifiedModel(LlmProvider.OpenAI, "api.openai.com", "gpt-4.1", ProbeConstants.Version - 1, DateTimeOffset.UtcNow)]));
        var service = Service();
        Assert.Empty((await service.GetAsync()).VerifiedModels);
        Assert.Equal(AppSettingsService.NotVerifiedReason("gpt-4.1"), (await service.GetClientAsync()).Reason);
    }

    [Fact]
    public async Task GetClient_returns_reasons_for_missing_key_and_unverified_models()
    {
        var service = Service();
        Assert.Equal("Add an API key in Settings", (await service.GetClientAsync()).Reason);

        await service.SaveAsync(LlmProvider.Anthropic, "claude-x", null, "sk-ant", default);
        var ok = await service.GetClientAsync();
        Assert.True(ok.Ok);
        Assert.IsType<TemperatureFallbackChatClient>(ok.Client);
        Assert.Same(ok.Client, (await service.GetClientAsync("claude-x")).Client);

        var other = await service.GetClientAsync("claude-unverified");
        Assert.False(other.Ok);
        Assert.Equal("Model 'claude-unverified' has not passed the capability check. Verify it in Settings.", other.Reason);
    }

    [Fact]
    public async Task Remove_verified_model()
    {
        var service = Service();
        await service.SaveAsync(LlmProvider.OpenAI, "gpt-4.1", null, "sk", default);
        await service.VerifyModelAsync("gpt-5", default);
        await service.RemoveVerifiedModelAsync("gpt-5");
        Assert.Equal(["gpt-4.1"], (await service.GetAsync()).VerifiedModels.Select(v => v.Model));
    }

    [Fact]
    public async Task Verify_uses_the_saved_provider_endpoint_and_key()
    {
        var service = Service();
        await service.SaveAsync(LlmProvider.Ollama, "llama3.2", "http://localhost:11434/", null, default);
        await service.VerifyModelAsync("qwen3", default);
        Assert.Equal(new LlmOptions(LlmProvider.Ollama, "qwen3", "http://localhost:11434/", null), _probe.Calls[^1]);
    }

    [Fact]
    public async Task Save_validates_and_quota_errors_propagate()
    {
        var service = Service();
        Assert.Equal("Pick or type a model name.", (await service.SaveAsync(LlmProvider.Anthropic, " ", null, "k", default)).Error);
        Assert.Equal("Enter a full URL that starts with https:// or http://.", (await service.SaveAsync(LlmProvider.Ollama, "llama", null, null, default)).Error);
        Assert.Equal("Enter an API key.", (await service.SaveAsync(LlmProvider.OpenAI, "gpt", null, null, default)).Error);

        _kv.QuotaBytes = 10;
        await Assert.ThrowsAsync<StorageQuotaExceededException>(() => service.SaveAsync(LlmProvider.OpenAI, "gpt", null, "sk-test", default));
    }

    [Fact]
    public async Task Reload_picks_up_another_tab()
    {
        var service = Service();
        var changed = 0;
        service.Changed += () => changed++;
        await Store().SaveAsync(new StoredSettings("OpenAI", "gpt-4.1", null, "sk-other-tab"));
        await service.ReloadAsync();
        Assert.Equal(1, changed);
        Assert.Equal(LlmProvider.OpenAI, (await service.GetAsync()).Provider);
    }

    [Fact]
    public async Task TestConnection_reports_rejected_keys_without_leaking_them()
    {
        var mock = new MockHttpMessageHandler();
        var service = Service(mock.ToHttpClient());
        await service.SaveAsync(LlmProvider.Anthropic, "claude-x", null, "sk-ant-secret-123456789", default);

        mock.When(HttpMethod.Post, AnthropicChatClient.Endpoint).Respond("text/event-stream", "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n");
        Assert.Null(await service.TestConnectionAsync(default));

        mock.Clear();
        mock.When(HttpMethod.Post, AnthropicChatClient.Endpoint)
            .Respond(HttpStatusCode.Unauthorized, "application/json", """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""");
        var error = await service.TestConnectionAsync(default);
        Assert.Contains("401", error);
        Assert.DoesNotContain("sk-ant-secret", error);
    }

    [Fact]
    public void DescribeError_never_leaks_a_key()
    {
        Assert.Contains("[redacted]", AppSettingsService.DescribeError(new InvalidOperationException("key sk-ant-api03-abcdefghijkl"), "api.anthropic.com", false));
        Assert.Contains("[redacted]", AppSettingsService.DescribeError(new InvalidOperationException("key AIzaSyA1234567890abcdefghij"), LlmDefaults.GoogleAIHost, false));
        Assert.Contains("blocked", AppSettingsService.DescribeError(new HttpRequestException("TypeError: Failed to fetch"), "api.anthropic.com", true));
    }
}
