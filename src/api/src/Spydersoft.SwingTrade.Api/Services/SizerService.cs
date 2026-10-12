using Microsoft.EntityFrameworkCore;
using Spydersoft.SwingTrade.Contracts.Sizer;
using Spydersoft.SwingTrade.Data;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Api.Services;

/// <summary>Pure calculation over the Rules library; saves nothing.</summary>
public class SizerService(SwingTradeDbContext db, SettingsService settings, DashboardService dashboard)
{
    public async Task<ServiceResult<SizerResultDto>> SizeAsync(SizerRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new ValidationErrors();
        if (request.Entry <= 0m) errors.Add(nameof(request.Entry), "Entry must be greater than zero.");
        if (request.Stop <= 0m) errors.Add(nameof(request.Stop), "Stop must be greater than zero.");
        if (request.Target <= 0m) errors.Add(nameof(request.Target), "Target must be greater than zero.");

        string? ticker = null;
        if (!string.IsNullOrWhiteSpace(request.Ticker))
        {
            ticker = InstrumentService.Normalize(request.Ticker);
            if (!await db.Instruments.AnyAsync(i => i.Ticker == ticker, ct))
                errors.Add(nameof(request.Ticker), $"Unknown ticker {ticker}.");
        }

        if (errors.Any)
            return errors.ToResult<SizerResultDto>();

        var s = await settings.GetRulesAsync(ct);
        var d = await dashboard.GetAsync(ct);
        var r = PositionSizer.Size(
            new SizerInput(request.Entry, request.Stop, request.Target, d.AvailableCash, d.OpenCount, d.AccountValue), s);

        var warning = r.OpenPositionWarning
            ? $"{d.OpenCount} open position(s); the limit is {s.MaxOpenPositions}."
            : null;

        return ServiceResult<SizerResultDto>.Ok(new SizerResultDto(
            ticker, request.Entry, request.Stop, request.Target, r.Capital, r.RiskBudget, r.RiskPerShare,
            r.SharesByRisk, r.SharesByCap, r.SharesByCash, r.Shares, r.PositionCost, r.RiskDollars, r.RiskPct,
            r.RewardRisk, r.MaxEntryForMinRewardRisk, r.Verdict, r.Verdict == PositionSizer.VerdictOk, warning,
            d.AvailableCash, d.OpenCount));
    }
}
