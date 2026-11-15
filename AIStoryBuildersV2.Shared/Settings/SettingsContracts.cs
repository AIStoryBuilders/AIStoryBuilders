using System.Diagnostics.CodeAnalysis;
using AIStoryBuildersV2.Shared.Llm;
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Shared.Settings;

/// <summary>A model that passed the capability probe for a provider and endpoint host (D9).</summary>
public sealed record VerifiedModel(LlmProvider Provider, string? Host, string Model, int ProbeVersion, DateTimeOffset VerifiedAt);

/// <summary>What the host stores: Preferences + SecureStorage on MAUI, asb2:settings in the browser (D7).</summary>
public sealed record StoredSettings(string Provider, string Model, string? Endpoint, string? ApiKey,
                                    IReadOnlyList<VerifiedModel>? VerifiedModels = null)
{
    public static StoredSettings Default => new(LlmDefaults.Provider.ToString(), LlmDefaults.DefaultModel(LlmDefaults.Provider), null, null);

    [System.Text.Json.Serialization.JsonIgnore]
    public LlmProvider ProviderValue =>
        Enum.TryParse<LlmProvider>(Provider, ignoreCase: true, out var p) && LlmDefaults.Available().Contains(p) ? p : LlmDefaults.Provider;
}

/// <summary>Host-specific settings storage (KGB).</summary>
public interface ISettingsStore
{
    Task<StoredSettings> LoadAsync();
    Task SaveAsync(StoredSettings settings);
    Task ClearApiKeyAsync();
}

/// <param name="VerifiedModels">Only the models for the current provider and host (plan §6.3).</param>
public sealed record AppSettings(string RootDisplay, LlmProvider Provider, string Model, string? Endpoint,
                                 bool ApiKeyConfigured, IReadOnlyList<VerifiedModel> VerifiedModels);

public enum ProbeStage { Connect, ToolCall, GraphParse }

public sealed record ModelProbeResult(bool Passed, ProbeStage? FailedAt, string? Message, TimeSpan Elapsed);

public sealed record SettingsSaveResult(bool Saved, ModelProbeResult? Probe, string? Error);

public interface IAppSettingsService
{
    Task<AppSettings> GetAsync();

    /// <summary>
    /// Saves the settings. When <paramref name="model"/> is not yet verified for this provider and host, it is probed first
    /// and nothing is saved unless it passes (plan §5.9).
    /// </summary>
    /// <param name="newApiKey">null keeps the current key.</param>
    Task<SettingsSaveResult> SaveAsync(LlmProvider provider, string model, string? endpoint, string? newApiKey, CancellationToken ct);
    Task ClearApiKeyAsync();

    /// <returns>null when the connection works, otherwise an error message.</returns>
    Task<string?> TestConnectionAsync(CancellationToken ct);

    /// <summary>Lists the provider's models for the form's current values. <paramref name="newApiKey"/> null uses the saved key.</summary>
    Task<ModelListResult> ListModelsAsync(LlmProvider provider, string? endpoint, string? newApiKey, bool refresh, CancellationToken ct);

    /// <summary>Probes a model with the saved provider, endpoint and key, and adds it to the verified list when it passes.</summary>
    Task<ModelProbeResult> VerifyModelAsync(string model, CancellationToken ct);
    Task RemoveVerifiedModelAsync(string model);

    event Action? Changed;
}

public readonly record struct ChatClientResult(IChatClient? Client, string? Reason)
{
    [MemberNotNullWhen(true, nameof(Client))]
    public bool Ok => Client is not null;
}

public interface IChatClientProvider
{
    /// <summary>
    /// The client for <paramref name="model"/> (the saved default when null), or a reason: "Add an API key in Settings" or
    /// "Model '&lt;m&gt;' has not passed the capability check. Verify it in Settings."
    /// </summary>
    Task<ChatClientResult> GetClientAsync(string? model = null);
}

public interface IModelCapabilityProbe
{
    Task<ModelProbeResult> ProbeAsync(LlmOptions options, CancellationToken ct);
}
