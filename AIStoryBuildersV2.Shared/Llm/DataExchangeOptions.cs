namespace AIStoryBuildersV2.Shared.Llm;

/// <summary>
/// Where the Blazor DataExchange gateway and its web app live (D34). The origin differs per environment, so it is host
/// configuration, never a constant: the Web host binds section <see cref="SectionName"/> from <c>wwwroot/appsettings*.json</c>;
/// MAUI registers the empty default, and the user enters the gateway URL in Settings.
/// </summary>
public sealed class DataExchangeOptions
{
    public const string SectionName = "DataExchange";

    /// <summary>The gateway base URL, ending in <c>/v1/</c>. Empty = not configured.</summary>
    public string BaseUrl { get; init; } = "";

    /// <summary>The exchange web app's home page, where API tokens are made. Empty hides the "get a key" link.</summary>
    public string ExchangeUrl { get; init; } = "";

    /// <summary>
    /// Reads <c>DataExchange:BaseUrl</c> and <c>DataExchange:ExchangeUrl</c> through <paramref name="read"/> (for example
    /// <c>key =&gt; builder.Configuration[key]</c>). A missing or blank value falls back to the given default.
    /// </summary>
    public static DataExchangeOptions From(Func<string, string?> read, string defaultBaseUrl = "", string defaultExchangeUrl = "")
    {
        ArgumentNullException.ThrowIfNull(read);
        static string Pick(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return new DataExchangeOptions
        {
            BaseUrl = Pick(read($"{SectionName}:BaseUrl"), defaultBaseUrl),
            ExchangeUrl = Pick(read($"{SectionName}:ExchangeUrl"), defaultExchangeUrl),
        };
    }

    /// <summary>The configured gateway base URL with a trailing slash, or null when it is empty or not an http(s) URL.</summary>
    public Uri? BaseUri => NormalizeBaseUrl(BaseUrl);

    /// <summary>The exchange's token list (<c>{ExchangeUrl}#tokens</c>), or null when no exchange URL is configured.</summary>
    public string? TokensUrl =>
        Uri.TryCreate(ExchangeUrl?.Trim(), UriKind.Absolute, out var u) && u.Scheme is "http" or "https"
            ? u.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/#tokens"
            : null;

    /// <summary>An absolute http(s) URL with a trailing slash (so relative paths such as <c>models</c> resolve under <c>/v1/</c>).</summary>
    public static Uri? NormalizeBaseUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https")) return null;
        var text = u.GetLeftPart(UriPartial.Path);
        return new Uri(text.EndsWith('/') ? text : text + "/");
    }
}
