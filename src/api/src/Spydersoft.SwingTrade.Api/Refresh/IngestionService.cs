using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Spydersoft.SwingTrade.Data;
using Spydersoft.SwingTrade.Data.Entities;
using Spydersoft.SwingTrade.MarketData;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Api.Refresh;

public sealed record IngestionSummary(
    int TickersOk,
    int TickersFailed,
    IReadOnlyList<string> ProvidersUsed,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Failures);

/// <summary>
/// Pulls bars for the active instruments plus the benchmark and keeps PriceBar and IndicatorSnapshot current.
/// Completed-bar filtering and every indicator come from the Rules library. Idempotent: bars upsert on (Ticker, Date)
/// and the overlap window lets late exchange corrections overwrite stored rows.
/// </summary>
public class IngestionService(
    SwingTradeDbContext db,
    IBarFetcher fetcher,
    IOptions<MarketDataOptions> options,
    TimeProvider time,
    ILogger<IngestionService> logger)
{
    public async Task<IngestionSummary> IngestAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var tickers = await LoadTickersAsync(ct);
        var primary = fetcher.ProviderChain.Split('>')[0];

        var ok = 0;
        var providers = new SortedSet<string>(StringComparer.Ordinal);
        var notes = new List<string>();
        var failures = new List<string>();

        foreach (var ticker in tickers)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var outcome = await IngestTickerAsync(ticker, now, ct);
                ok++;
                if (outcome.Provider is not null)
                {
                    providers.Add(outcome.Provider);
                    if (!string.Equals(outcome.Provider, primary, StringComparison.OrdinalIgnoreCase))
                        notes.Add($"{ticker} via {outcome.Provider}");
                }

                if (outcome.Rebackfilled)
                    notes.Add($"{ticker} re-backfilled after a restated close (split)");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Refresh failed for {Ticker}", ticker);
                failures.Add($"{ticker}: {ex.Message}");
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        return new IngestionSummary(ok, failures.Count, providers.ToList(), notes, failures);
    }

    /// <summary>Active instruments plus the benchmark (always refreshed, even if deactivated), benchmark first.</summary>
    public async Task<List<string>> LoadTickersAsync(CancellationToken ct = default)
    {
        var rows = await db.Instruments.AsNoTracking()
            .Where(i => i.Active || i.IsBenchmark)
            .Select(i => new { i.Ticker, i.IsBenchmark })
            .ToListAsync(ct);
        return rows.OrderByDescending(r => r.IsBenchmark).ThenBy(r => r.Ticker, StringComparer.Ordinal)
            .Select(r => r.Ticker).Distinct().ToList();
    }

    private sealed record TickerOutcome(string? Provider, bool Rebackfilled);

    private async Task<TickerOutcome> IngestTickerAsync(string ticker, DateTimeOffset now, CancellationToken ct)
    {
        var opts = options.Value;
        var (today, clock) = BarCompletion.ToNewYork(now);
        var expected = MarketCalendar.LatestExpectedBar(now);
        var backfillFrom = today.AddDays(-opts.BackfillDays);

        var latest = await db.PriceBars.Where(b => b.Ticker == ticker).MaxAsync(b => (DateOnly?)b.Date, ct);
        var from = latest is null ? backfillFrom : latest.Value.AddDays(-opts.OverlapDays);

        var fetched = await fetcher.FetchAsync(ticker, from, today, expected, ct);
        var bars = Completed(fetched.Bars, today, clock);

        var rebackfilled = false;
        if (latest is not null && await RestatedAsync(ticker, bars, opts.SplitBreakThreshold, ct))
        {
            logger.LogWarning("{Ticker}: a stored close differs by more than {Threshold:P0} from the provider's; re-backfilling {Days} days",
                ticker, opts.SplitBreakThreshold, opts.BackfillDays);
            fetched = await fetcher.FetchAsync(ticker, backfillFrom, today, expected, ct);
            bars = Completed(fetched.Bars, today, clock);

            // Anything older than the new window is on the old (pre-split) basis; drop it rather than mix bases.
            var stale = await db.PriceBars.Where(b => b.Ticker == ticker && b.Date < backfillFrom).ToListAsync(ct);
            db.PriceBars.RemoveRange(stale);
            rebackfilled = true;
        }

        if (latest is null && bars.Count == 0)
            throw new InvalidOperationException($"No bars returned by {fetched.Provider}.");

        await UpsertAsync(ticker, bars, ct);
        await db.SaveChangesAsync(ct);
        await UpdateSnapshotAsync(ticker, today, clock, ct);
        await db.SaveChangesAsync(ct);

        return new TickerOutcome(bars.Count > 0 ? fetched.Provider : null, rebackfilled);
    }

    private static List<DailyBar> Completed(IReadOnlyList<DailyBar> bars, DateOnly today, TimeOnly clock) =>
        bars.Where(b => BarCompletion.IsCompleted(b.Date, today, clock)).OrderBy(b => b.Date).ToList();

    /// <summary>Does any returned close differ materially from the one stored for the same date?</summary>
    private async Task<bool> RestatedAsync(string ticker, List<DailyBar> bars, decimal threshold, CancellationToken ct)
    {
        if (bars.Count == 0) return false;
        var min = bars[0].Date;
        var max = bars[^1].Date;
        var stored = await db.PriceBars.AsNoTracking()
            .Where(b => b.Ticker == ticker && b.Date >= min && b.Date <= max)
            .ToDictionaryAsync(b => b.Date, b => b.Close, ct);

        return bars.Any(b => stored.TryGetValue(b.Date, out var old) && old != 0m && Math.Abs(b.Close - old) / old > threshold);
    }

    private async Task UpsertAsync(string ticker, List<DailyBar> bars, CancellationToken ct)
    {
        if (bars.Count == 0) return;
        var min = bars[0].Date;
        var max = bars[^1].Date;
        var existing = await db.PriceBars
            .Where(b => b.Ticker == ticker && b.Date >= min && b.Date <= max)
            .ToDictionaryAsync(b => b.Date, ct);

        foreach (var bar in bars)
        {
            if (!existing.TryGetValue(bar.Date, out var row))
            {
                row = new PriceBar { Ticker = ticker, Date = bar.Date };
                db.PriceBars.Add(row);
            }

            row.Open = bar.Open;
            row.High = bar.High;
            row.Low = bar.Low;
            row.Close = bar.Close;
            row.Volume = bar.Volume;
        }
    }

    private async Task UpdateSnapshotAsync(string ticker, DateOnly today, TimeOnly clock, CancellationToken ct)
    {
        // One extra row in case a stale incomplete bar was ever stored; the Rules library decides what is complete.
        var recent = await db.PriceBars.AsNoTracking()
            .Where(b => b.Ticker == ticker)
            .OrderByDescending(b => b.Date)
            .Take(IndicatorCalculator.LongWindow + 1)
            .Select(b => new { b.Date, b.Close })
            .ToListAsync(ct);

        var completed = recent.Where(b => BarCompletion.IsCompleted(b.Date, today, clock)).Take(IndicatorCalculator.LongWindow).Reverse().ToList();
        if (completed.Count == 0)
            return;

        var indicators = IndicatorCalculator.Calculate(completed.Select(b => b.Close).ToList());
        var asOf = completed[^1].Date;

        var snapshot = await db.IndicatorSnapshots.FirstOrDefaultAsync(s => s.Ticker == ticker && s.AsOfDate == asOf, ct);
        if (snapshot is null)
        {
            snapshot = new IndicatorSnapshot { Ticker = ticker, AsOfDate = asOf };
            db.IndicatorSnapshots.Add(snapshot);
        }

        snapshot.LastClose = indicators.Last;
        snapshot.Ma20 = indicators.Ma20;
        snapshot.Ma50 = indicators.Ma50;
        snapshot.BarsUsed = indicators.BarsUsed;
    }
}
