namespace Spydersoft.SwingTrade.MarketData;

/// <summary>
/// Bound from the "MarketData" section. Secrets (KeyId, SecretKey, Token) come from user-secrets or
/// environment variables (MarketData__Alpaca__KeyId ...), never from committed appsettings.
/// </summary>
public class MarketDataOptions
{
    public const string SectionName = "MarketData";

    /// <summary>Ordered provider names, tried in order: "alpaca", "tiingo", "fake".</summary>
    public List<string> Providers { get; set; } = [];

    public AlpacaOptions Alpaca { get; set; } = new();
    public TiingoOptions Tiingo { get; set; } = new();
    public FakeOptions Fake { get; set; } = new();

    /// <summary>Attempts per provider per ticker (the plan says up to three).</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Back-off before retry n is BaseDelayMs * 2^(n-1) unless the provider sent Retry-After.</summary>
    public int RetryBaseDelayMs { get; set; } = 1000;

    /// <summary>Calendar days loaded for a ticker with no stored bars.</summary>
    public int BackfillDays { get; set; } = 200;

    /// <summary>Days before the latest stored bar that are re-requested (late corrections overwrite).</summary>
    public int OverlapDays { get; set; } = 5;

    /// <summary>
    /// If a re-requested close differs from the stored close by more than this fraction, the history was
    /// restated (a split) and the ticker is re-backfilled. Ordinary corrections are cents.
    /// </summary>
    public decimal SplitBreakThreshold { get; set; } = 0.10m;
}

public class AlpacaOptions
{
    public string? KeyId { get; set; }
    public string? SecretKey { get; set; }

    /// <summary>"sip" is required for consolidated closes. IEX-only closes can differ from the official close.</summary>
    public string Feed { get; set; } = "sip";

    public string BaseUrl { get; set; } = "https://data.alpaca.markets";
    public int MinDelayMs { get; set; } = 100;
}

public class TiingoOptions
{
    public string? Token { get; set; }
    public string BaseUrl { get; set; } = "https://api.tiingo.com";
    public int MinDelayMs { get; set; } = 1000;
}

public class FakeOptions
{
    public int MinDelayMs { get; set; }

    /// <summary>Tickers for which the fake throws, to exercise failure handling locally.</summary>
    public List<string> FailTickers { get; set; } = [];
}
