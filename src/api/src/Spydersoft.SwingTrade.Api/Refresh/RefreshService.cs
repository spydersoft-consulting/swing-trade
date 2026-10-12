using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Spydersoft.SwingTrade.Api.Services;
using Spydersoft.SwingTrade.Contracts.Common;
using Spydersoft.SwingTrade.Contracts.Refresh;
using Spydersoft.SwingTrade.Data;
using Spydersoft.SwingTrade.Data.Entities;
using Spydersoft.SwingTrade.MarketData;

namespace Spydersoft.SwingTrade.Api.Refresh;

public static class RefreshStatus
{
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Partial = "Partial";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string Abandoned = "Abandoned";
}

/// <summary>
/// Owns RefreshRun rows and the one-at-a-time rule. A run is started (row written, lock taken) and then executed;
/// the lock is released when execution finishes, however it ends.
/// </summary>
public class RefreshService(
    SwingTradeDbContext db,
    IngestionService ingestion,
    MarketCalendar calendar,
    RefreshCoordinator coordinator,
    IOptions<MarketDataOptions> options,
    TimeProvider time,
    ILogger<RefreshService> logger)
{
    private const int MaxMessageLength = 2000;
    private const int MaxProviderLength = 50;

    /// <summary>Takes the lock and records a Running row. Null when another refresh is already running.</summary>
    public async Task<RefreshRun?> TryBeginAsync(string trigger, CancellationToken ct = default)
    {
        if (!coordinator.TryAcquire())
            return null;

        try
        {
            // The lock is ours, so any row still marked Running was left behind by a crash or restart.
            var orphans = await db.RefreshRuns.Where(r => r.Status == RefreshStatus.Running).ToListAsync(ct);
            foreach (var orphan in orphans)
            {
                orphan.Status = RefreshStatus.Abandoned;
                orphan.FinishedAt ??= time.GetUtcNow();
            }

            var run = new RefreshRun
            {
                StartedAt = time.GetUtcNow(),
                Provider = Truncate(string.Join('>', options.Value.Providers), MaxProviderLength),
                Status = RefreshStatus.Running,
                Message = $"[{trigger}]"
            };
            db.RefreshRuns.Add(run);
            await db.SaveChangesAsync(ct);
            return run;
        }
        catch
        {
            coordinator.Release();
            throw;
        }
    }

    /// <summary>Runs the ingestion for a run created by <see cref="TryBeginAsync"/> and always releases the lock.</summary>
    public async Task<RefreshRun> ExecuteAsync(int runId, CancellationToken ct = default)
    {
        try
        {
            IngestionSummary? summary = null;
            string? error = null;
            var cancelled = false;
            try
            {
                summary = await ingestion.IngestAsync(ct);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Refresh run {RunId} failed", runId);
                error = ex.Message;
            }

            db.ChangeTracker.Clear();
            var run = await db.RefreshRuns.SingleAsync(r => r.Id == runId, CancellationToken.None);
            var trigger = run.Message ?? string.Empty;
            run.FinishedAt = time.GetUtcNow();

            if (summary is not null)
            {
                run.TickersOk = summary.TickersOk;
                run.TickersFailed = summary.TickersFailed;
                run.Status = summary.TickersFailed == 0 ? RefreshStatus.Succeeded
                    : summary.TickersOk > 0 ? RefreshStatus.Partial
                    : RefreshStatus.Failed;
                if (summary.ProvidersUsed.Count > 0)
                    run.Provider = Truncate(string.Join('+', summary.ProvidersUsed), MaxProviderLength);
                run.Message = Truncate(Compose(trigger, summary), MaxMessageLength);
            }
            else
            {
                run.Status = cancelled ? RefreshStatus.Cancelled : RefreshStatus.Failed;
                run.Message = Truncate($"{trigger} {(cancelled ? "Cancelled." : error)}".Trim(), MaxMessageLength);
            }

            await db.SaveChangesAsync(CancellationToken.None);
            return run;
        }
        finally
        {
            coordinator.Release();
        }
    }

    /// <summary>Starts and executes a run inline (the scheduler). Null when another refresh is running.</summary>
    public async Task<RefreshRun?> RunAsync(string trigger, CancellationToken ct = default)
    {
        var run = await TryBeginAsync(trigger, ct);
        return run is null ? null : await ExecuteAsync(run.Id, ct);
    }

    /// <summary>Manual refresh: 202-style start. The caller launches <see cref="ExecuteAsync"/> in the background.</summary>
    public async Task<ServiceResult<RefreshRunDto>> StartManualAsync(CancellationToken ct = default)
    {
        var run = await TryBeginAsync("manual", ct);
        return run is null
            ? ServiceResult<RefreshRunDto>.Conflict("A refresh is already running.")
            : ServiceResult<RefreshRunDto>.Ok(ToDto(run));
    }

    /// <summary>True when any refreshed ticker has no bar for the latest expected session (drives the 08:00 catch-up).</summary>
    public async Task<bool> IsLatestBarMissingAsync(CancellationToken ct = default)
    {
        var expected = calendar.LatestExpectedBar();
        var tickers = await ingestion.LoadTickersAsync(ct);
        if (tickers.Count == 0)
            return false;

        var latest = (await db.PriceBars.AsNoTracking()
                .GroupBy(b => b.Ticker)
                .Select(g => new { Ticker = g.Key, Date = g.Max(b => b.Date) })
                .ToListAsync(ct))
            .ToDictionary(x => x.Ticker, x => x.Date);

        return tickers.Any(t => !latest.TryGetValue(t, out var date) || date < expected);
    }

    public async Task<PagedResult<RefreshRunDto>> ListAsync(RefreshRunListQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var total = await db.RefreshRuns.CountAsync(ct);
        var rows = await db.RefreshRuns.AsNoTracking()
            .OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);
        return new PagedResult<RefreshRunDto>(rows.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<ServiceResult<RefreshRunDto>> GetAsync(int id, CancellationToken ct = default)
    {
        var run = await db.RefreshRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return run is null ? ServiceResult<RefreshRunDto>.NotFound() : ServiceResult<RefreshRunDto>.Ok(ToDto(run));
    }

    private static string Compose(string trigger, IngestionSummary s)
    {
        var parts = new List<string> { $"{s.TickersOk} ok, {s.TickersFailed} failed." };
        if (s.Notes.Count > 0) parts.Add("Notes: " + string.Join(", ", s.Notes) + ".");
        if (s.Failures.Count > 0) parts.Add("Failures: " + string.Join(" | ", s.Failures));
        return $"{trigger} {string.Join(' ', parts)}".Trim();
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static RefreshRunDto ToDto(RefreshRun r) =>
        new(r.Id, r.StartedAt, r.FinishedAt, r.Provider, r.Status, r.TickersOk, r.TickersFailed, r.Message);
}
