using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIStoryBuildersV2.Shared.Knowledge;
using AIStoryBuildersV2.Shared.Settings;

namespace AIStoryBuildersV2.Shared.Llm;

public enum DataExchangeConnectStatus
{
    /// <summary>The key was redeemed and saved with a verified model.</summary>
    Connected,

    /// <summary>The key was redeemed, but no model was recommended or the recommended one failed the probe. Nothing was saved.</summary>
    NeedsModel,

    InvalidCode,
    AccountDisabled,
    TokenLimit,
    NotConfigured,
    Unreachable,
    Failed,
}

/// <summary>
/// What <c>POST {base}connect/redeem</c> returned. <see cref="ToString"/> never prints <see cref="ApiKey"/>; show
/// <see cref="KeyPrefix"/> instead.
/// </summary>
public sealed record DataExchangeRedeemed(string ApiKey, string KeyPrefix, Uri BaseUrl, string? RecommendedModel, decimal? CreditsAvailable)
{
    public override string ToString() => $"DataExchangeRedeemed {{ KeyPrefix = {KeyPrefix}, BaseUrl = {BaseUrl}, RecommendedModel = {RecommendedModel} }}";
}

/// <param name="Redeemed">Set from a successful redemption on; it holds the only copy of the new key when nothing was saved.</param>
/// <param name="Save">The settings save result when a save was attempted.</param>
public sealed record DataExchangeConnectResult(DataExchangeConnectStatus Status, string Message, DataExchangeRedeemed? Redeemed = null,
                                               SettingsSaveResult? Save = null)
{
    public override string ToString() => $"DataExchangeConnectResult {{ Status = {Status}, Message = {Message}, Redeemed = {Redeemed} }}";
}

/// <summary>
/// The <c>/connect/dataexchange</c> flow (D34): redeems the one-time code from the URL fragment for an API key, then saves
/// the DataExchange provider with the recommended model through <see cref="IAppSettingsService.SaveAsync"/>, which runs the
/// capability probe. The redeem POST is sent once and never retried. The key is never logged; only its prefix is.
/// </summary>
public sealed class DataExchangeConnectService(HttpClient http, DataExchangeOptions options, IAppSettingsService settings,
                                               IHostCapabilities host, LogService log)
{
    public const string ClientName = "AIStoryBuilders";
    public const string RoutePath = "connect/dataexchange";
    public static readonly TimeSpan RedeemTimeout = TimeSpan.FromSeconds(30);

    public const string NotConfiguredMessage =
        "This copy of AI StoryBuilders isn't set up to connect to Blazor DataExchange. Choose Blazor DataExchange in Settings and enter the gateway URL and an API key instead.";

    public const string InvalidCodeMessage =
        "This connect link has expired or was already used. Go back to Blazor DataExchange and click \"Use in AIStoryBuilders\" again.";

    public const string AccountDisabledMessage = "Your Blazor DataExchange account is disabled. Contact the exchange's support.";

    public const string TokenLimitMessage =
        "Your Blazor DataExchange account has too many API tokens. Revoke one you don't use on the exchange, then try again.";

    public const string NoCodeMessage =
        "This page needs a connect code from Blazor DataExchange. Click \"Use in AIStoryBuilders\" on the exchange to connect.";

    /// <summary>The <c>code</c> value of a URL fragment such as <c>#code=abc</c>, or null.</summary>
    public static string? ReadCodeFromFragment(string? uri)
    {
        if (string.IsNullOrEmpty(uri)) return null;
        var hash = uri.IndexOf('#');
        if (hash < 0) return null;
        foreach (var part in uri[(hash + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0 || !string.Equals(part[..eq], "code", StringComparison.Ordinal)) continue;
            string value;
            try { value = Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' ')).Trim(); }
            catch (UriFormatException) { return null; }
            return value.Length == 0 ? null : value;
        }
        return null;
    }

    /// <summary>
    /// A code pasted into Settings (the desktop app has no deep link): the bare code, or a whole connect link with
    /// <c>#code=…</c>, which is reduced to its code.
    /// </summary>
    public static string? NormalizePastedCode(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Contains('#')) return ReadCodeFromFragment(text);
        return text.StartsWith("code=", StringComparison.Ordinal) ? ReadCodeFromFragment("#" + text) : text;
    }

    /// <summary>Redeems the code and saves the settings when the recommended model passes the probe.</summary>
    /// <param name="baseUrl">The gateway to redeem at; null uses the configured <see cref="DataExchangeOptions.BaseUrl"/>.
    /// Settings passes the gateway URL shown in the form, which the user may have typed when nothing is configured.</param>
    public async Task<DataExchangeConnectResult> ConnectAsync(string? code, CancellationToken ct, string? baseUrl = null)
    {
        var redeem = await RedeemAsync(code, ct, baseUrl);
        if (redeem.Redeemed is not { } redeemed) return redeem;

        if (string.IsNullOrWhiteSpace(redeemed.RecommendedModel))
            return new DataExchangeConnectResult(DataExchangeConnectStatus.NeedsModel,
                "Connected to Blazor DataExchange. Pick a model to finish.", redeemed);

        SettingsSaveResult save;
        try
        {
            save = await settings.SaveAsync(LlmProvider.DataExchange, redeemed.RecommendedModel, redeemed.BaseUrl.ToString(), redeemed.ApiKey, ct);
        }
        catch (StorageQuotaExceededException)
        {
            return new DataExchangeConnectResult(DataExchangeConnectStatus.NeedsModel,
                "Browser storage is full, so the settings weren't saved. Export stories you don't need with Export Project, then delete them from the Library.",
                redeemed);
        }

        if (save.Saved)
        {
            log.WriteToLog($"Connected to Blazor DataExchange ({redeemed.BaseUrl.Authority}) with key {redeemed.KeyPrefix}… and model {redeemed.RecommendedModel}.");
            return new DataExchangeConnectResult(DataExchangeConnectStatus.Connected,
                $"Connected to Blazor DataExchange with {redeemed.RecommendedModel}.", redeemed, save);
        }

        var why = save.Probe is { } p ? $"{redeemed.RecommendedModel} did not pass the capability check: {p.Message}" : save.Error;
        log.WriteToLog($"Blazor DataExchange key {redeemed.KeyPrefix}… redeemed, but the settings weren't saved: {why}");
        return new DataExchangeConnectResult(DataExchangeConnectStatus.NeedsModel,
            $"Connected to Blazor DataExchange, but {why} Pick another model to finish.", redeemed, save);
    }

    /// <summary><c>POST {BaseUrl}connect/redeem</c>, sent once.</summary>
    public async Task<DataExchangeConnectResult> RedeemAsync(string? code, CancellationToken ct, string? baseUrl = null)
    {
        var baseUri = string.IsNullOrWhiteSpace(baseUrl) ? options.BaseUri : DataExchangeOptions.NormalizeBaseUrl(baseUrl);
        if (baseUri is null)
            return new DataExchangeConnectResult(DataExchangeConnectStatus.NotConfigured, NotConfiguredMessage);
        code = code?.Trim();
        if (string.IsNullOrEmpty(code))
            return new DataExchangeConnectResult(DataExchangeConnectStatus.InvalidCode, NoCodeMessage);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RedeemTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "connect/redeem"))
            {
                // JsonObject rather than a serialized type, so trimming can't drop the property names.
                Content = new StringContent(new JsonObject { ["code"] = code, ["client"] = ClientName }.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            using var response = await http.SendAsync(request, cts.Token);
            var body = await response.Content.ReadAsStringAsync(cts.Token);
            if (!response.IsSuccessStatusCode) return Failure(response.StatusCode, body);
            return Success(body, baseUri);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new DataExchangeConnectResult(DataExchangeConnectStatus.Unreachable,
                $"Blazor DataExchange ({baseUri.Authority}) didn't answer in time. Try again in a moment.");
        }
        catch (HttpRequestException)
        {
            return new DataExchangeConnectResult(DataExchangeConnectStatus.Unreachable, host.IsBrowser
                ? $"The browser couldn't reach Blazor DataExchange at {baseUri.Authority}. It may be down, or it doesn't allow requests from this page (CORS)."
                : $"Couldn't reach Blazor DataExchange at {baseUri.Authority}.");
        }
        catch (JsonException)
        {
            return new DataExchangeConnectResult(DataExchangeConnectStatus.Failed, "Blazor DataExchange returned an unexpected response.");
        }
    }

    static DataExchangeConnectResult Success(string body, Uri configured)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var key = Str(root, "api_key");
        if (string.IsNullOrWhiteSpace(key))
            return new DataExchangeConnectResult(DataExchangeConnectStatus.Failed, "Blazor DataExchange returned an unexpected response.");
        key = key.Trim();
        var prefix = Str(root, "key_prefix") is { Length: > 0 } p ? p : key[..Math.Min(11, key.Length)];
        // The response's base_url is authoritative; the configured one is the fallback.
        var baseUrl = DataExchangeOptions.NormalizeBaseUrl(Str(root, "base_url")) ?? configured;
        var model = Str(root, "recommended_model")?.Trim();
        decimal? credits = root.TryGetProperty("credits_available", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetDecimal(out var d) ? d : null;
        var redeemed = new DataExchangeRedeemed(key, prefix, baseUrl, string.IsNullOrEmpty(model) ? null : model, credits);
        return new DataExchangeConnectResult(DataExchangeConnectStatus.Connected, "Redeemed.", redeemed);
    }

    static DataExchangeConnectResult Failure(HttpStatusCode status, string body)
    {
        var (code, message) = ReadError(body);
        return code switch
        {
            "invalid_or_expired_code" => new(DataExchangeConnectStatus.InvalidCode, InvalidCodeMessage),
            "account_disabled" => new(DataExchangeConnectStatus.AccountDisabled, AccountDisabledMessage),
            "token_limit_reached" => new(DataExchangeConnectStatus.TokenLimit, TokenLimitMessage),
            _ => new(DataExchangeConnectStatus.Failed,
                $"Blazor DataExchange answered HTTP {(int)status}" + (string.IsNullOrWhiteSpace(message) ? "." : $": {Trim(message)}")),
        };
    }

    /// <summary>The OpenAI error shape: <c>{"error":{"message","type","code","param"}}</c>.</summary>
    internal static (string? Code, string? Message) ReadError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object)
                return (Str(e, "code"), Str(e, "message"));
        }
        catch (JsonException) { }
        return (null, null);
    }

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static string Trim(string message) => message.Length > 300 ? message[..300] + "…" : message;
}
