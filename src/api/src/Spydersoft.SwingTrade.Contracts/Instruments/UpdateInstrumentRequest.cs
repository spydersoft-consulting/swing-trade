using System.ComponentModel.DataAnnotations;

namespace Spydersoft.SwingTrade.Contracts.Instruments;

public class UpdateInstrumentRequest
{
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Category { get; set; }

    public bool Active { get; set; } = true;
    public bool IsBenchmark { get; set; }

    [MaxLength(50)]
    public string? WashSaleGroup { get; set; }
}
