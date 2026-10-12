namespace Spydersoft.SwingTrade.Data.Entities;

/// <summary>Audit of Claude reads. Never stores tokens. Keep 90 days.</summary>
public class McpCallLog
{
    public int Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string Tool { get; set; } = string.Empty;
    public string? Arguments { get; set; }
    public int DurationMs { get; set; }
    public string? Caller { get; set; }
    public string Status { get; set; } = string.Empty;
}
