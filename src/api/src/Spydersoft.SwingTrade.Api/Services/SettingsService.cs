using Microsoft.EntityFrameworkCore;
using Spydersoft.SwingTrade.Contracts.Settings;
using Spydersoft.SwingTrade.Data;
using Spydersoft.SwingTrade.Data.Entities;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Api.Services;

public class SettingsService(SwingTradeDbContext db)
{
    /// <summary>The thresholds every rule reads. Falls back to the plan defaults if the row is missing.</summary>
    public async Task<RulesSettings> GetRulesAsync(CancellationToken ct = default)
    {
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct);
        return row is null ? RulesSettings.Default : SettingsMapping.ToRules(row);
    }

    public async Task<SettingsDto> GetAsync(CancellationToken ct = default)
    {
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct);
        return row is null ? SettingsMapping.ToDto(RulesSettings.Default) : SettingsMapping.ToDto(row);
    }

    public async Task<ServiceResult<SettingsDto>> UpdateAsync(UpdateSettingsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = Validate(request);
        if (errors.Any)
            return errors.ToResult<SettingsDto>();

        var row = await db.Settings.FirstOrDefaultAsync(ct);
        if (row is null)
        {
            row = new Settings { Id = Settings.SingletonId };
            db.Settings.Add(row);
        }

        row.StartingCapital = request.StartingCapital;
        row.MaxRiskPct = request.MaxRiskPct;
        row.MaxOpenPositions = request.MaxOpenPositions;
        row.MinRewardRisk = request.MinRewardRisk;
        row.TaxSetAsideRate = request.TaxSetAsideRate;
        row.WashSaleWindowDays = request.WashSaleWindowDays;
        row.MaxPositionPct = request.MaxPositionPct;
        row.PullbackBandLow = request.PullbackBandLow;
        row.PullbackBandHigh = request.PullbackBandHigh;
        row.ExtendedThreshold = request.ExtendedThreshold;
        row.RiskBasis = request.RiskBasis;
        await db.SaveChangesAsync(ct);

        return ServiceResult<SettingsDto>.Ok(SettingsMapping.ToDto(row));
    }

    private static ValidationErrors Validate(UpdateSettingsRequest r)
    {
        var e = new ValidationErrors();
        if (r.StartingCapital <= 0m) e.Add(nameof(r.StartingCapital), "Starting capital must be greater than zero.");
        if (r.MaxRiskPct is <= 0m or > 1m) e.Add(nameof(r.MaxRiskPct), "Max risk must be a fraction above 0 and at most 1.");
        if (r.MaxOpenPositions < 1) e.Add(nameof(r.MaxOpenPositions), "Max open positions must be at least 1.");
        if (r.MinRewardRisk <= 0m) e.Add(nameof(r.MinRewardRisk), "Minimum reward:risk must be greater than zero.");
        if (r.TaxSetAsideRate is < 0m or > 1m) e.Add(nameof(r.TaxSetAsideRate), "Tax set-aside rate must be a fraction from 0 to 1.");
        if (r.WashSaleWindowDays < 0) e.Add(nameof(r.WashSaleWindowDays), "Wash-sale window cannot be negative.");
        if (r.MaxPositionPct is <= 0m or > 1m) e.Add(nameof(r.MaxPositionPct), "Position cap must be a fraction above 0 and at most 1.");
        if (r.PullbackBandLow > r.PullbackBandHigh) e.Add(nameof(r.PullbackBandLow), "Pullback band low cannot exceed the band high.");
        if (!Enum.IsDefined(r.RiskBasis)) e.Add(nameof(r.RiskBasis), "Unknown risk basis.");
        return e;
    }
}
