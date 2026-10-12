namespace Spydersoft.SwingTrade.Data.Entities;

public class RefreshRun
{
    public int Id { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int TickersOk { get; set; }
    public int TickersFailed { get; set; }
    public string? Message { get; set; }
}
