namespace Spydersoft.SwingTrade.Api.Mcp;

/// <summary>Configuration section <c>Mcp</c>. Secrets do not belong here; there are none.</summary>
public sealed class McpOptions
{
    public const string Section = "Mcp";
    public const string McpPath = "/mcp";
    public const string MetadataPath = "/.well-known/oauth-protected-resource";
    public const string RateLimitPolicy = "mcp";
    public const int DefaultRequestsPerMinute = 120;
    public const int RetentionDays = 90;

    /// <summary>The absolute public URL of the MCP endpoint (https outside Development and Testing). Used as the RFC 9728 <c>resource</c>.</summary>
    public string? PublicUrl { get; set; }

    /// <summary>The only <c>sub</c> allowed through, in addition to the swingtrade.mcp role. Required outside Development and Testing.</summary>
    public string? OwnerSubject { get; set; }

    /// <summary>Registers the add_note tool (which additionally needs the swingtrade.trader role). Off by default.</summary>
    public bool EnableAddNote { get; set; }

    /// <summary>Registers record_pass and log_planned_trade (which additionally need the swingtrade.trader role). Off by default.</summary>
    public bool EnableJournalWrites { get; set; }

    /// <summary>Registers fill_trade, close_trade and cancel_trade (which additionally need the swingtrade.trader role). Off by default.</summary>
    public bool EnableTradeLifecycle { get; set; }

    /// <summary>Registers add_instrument and set_instrument_active (which additionally need the swingtrade.trader role). Off by default.</summary>
    public bool EnableWatchlistManagement { get; set; }

    /// <summary>Host names the MCP routes answer to. Empty means the host of <see cref="PublicUrl"/>.</summary>
    public string[] AllowedHosts { get; set; } = [];

    public int RequestsPerMinute { get; set; } = DefaultRequestsPerMinute;

    /// <summary>The issuer of the authorization server (from <c>Auth:Authority</c>), listed in the protected resource metadata.</summary>
    public string? Authority { get; set; }

    public Uri? PublicUri => Uri.TryCreate(PublicUrl, UriKind.Absolute, out var u) ? u : null;

    /// <summary>Absolute URL of <c>/.well-known/oauth-protected-resource</c> on the public origin.</summary>
    public string? MetadataUrl => PublicUri is { } u ? $"{u.GetLeftPart(UriPartial.Authority)}{MetadataPath}" : null;

    public IReadOnlyList<string> EffectiveAllowedHosts =>
        AllowedHosts.Length > 0 ? AllowedHosts : PublicUri is { } u ? [u.Host] : [];

    /// <summary>Throws when the configuration is unsafe for the environment. Development and Testing may omit the owner.</summary>
    public void Validate(bool relaxed)
    {
        if (PublicUri is null)
        {
            if (!relaxed) throw new InvalidOperationException("Mcp:PublicUrl must be configured as an absolute URL.");
        }
        else if (PublicUri.Scheme != Uri.UriSchemeHttps && !relaxed)
        {
            throw new InvalidOperationException("Mcp:PublicUrl must be https.");
        }

        if (!relaxed && string.IsNullOrWhiteSpace(OwnerSubject))
            throw new InvalidOperationException("Mcp:OwnerSubject must be configured; the MCP endpoint is owner-only and fails closed.");
        if (RequestsPerMinute < 1)
            throw new InvalidOperationException("Mcp:RequestsPerMinute must be at least 1.");
    }
}
