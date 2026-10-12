namespace Spydersoft.SwingTrade.Contracts.Sizer;

public class SizerRequest
{
    /// <summary>Optional. Echoed back; must be a known instrument when given.</summary>
    public string? Ticker { get; set; }
    public decimal Entry { get; set; }
    public decimal Stop { get; set; }
    public decimal Target { get; set; }
}
