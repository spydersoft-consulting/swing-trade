using Microsoft.EntityFrameworkCore;
using Spydersoft.SwingTrade.Contracts.Bars;
using Spydersoft.SwingTrade.Data;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Api.Services;

public class BarService(SwingTradeDbContext db, TimeProvider clock)
{
    /// <summary>
    /// The most recent completed closes (at most 120), oldest first, with the 20 and 50 day averages ending at each bar.
    /// Averages are computed from the bars before the window too, so the chart's first bar already has its MA.
    /// </summary>
    public async Task<ServiceResult<BarsDto>> GetAsync(string ticker, BarsQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var key = InstrumentService.Normalize(ticker);
        if (!await db.Instruments.AnyAsync(i => i.Ticker == key, ct))
            return ServiceResult<BarsDto>.NotFound();

        if (query.Days < 1)
        {
            var errors = new ValidationErrors();
            errors.Add(nameof(query.Days), "Days must be at least 1.");
            return errors.ToResult<BarsDto>();
        }

        var days = Math.Min(query.Days, BarsQuery.MaxDays);
        var now = clock.GetUtcNow();

        // Two extra rows cover a stored bar that is not completed yet; the Rules library decides what is.
        var take = days + IndicatorCalculator.LongWindow - 1 + 2;
        var rows = await db.PriceBars.AsNoTracking()
            .Where(b => b.Ticker == key)
            .OrderByDescending(b => b.Date)
            .Take(take)
            .Select(b => new { b.Date, b.Close })
            .ToListAsync(ct);

        var completed = rows.Where(b => BarCompletion.IsCompleted(b.Date, now)).OrderBy(b => b.Date).ToList();
        var closes = completed.Select(b => b.Close).ToList();
        var ma20 = IndicatorCalculator.MovingAverageSeries(closes, IndicatorCalculator.ShortWindow);
        var ma50 = IndicatorCalculator.MovingAverageSeries(closes, IndicatorCalculator.LongWindow);

        var skip = Math.Max(0, completed.Count - days);
        var points = Enumerable.Range(skip, completed.Count - skip)
            .Select(i => new BarPointDto(completed[i].Date, completed[i].Close, ma20[i], ma50[i]))
            .ToList();

        return ServiceResult<BarsDto>.Ok(new BarsDto(key, days, points));
    }
}
