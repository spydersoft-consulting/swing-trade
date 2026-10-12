using Microsoft.EntityFrameworkCore;
using Spydersoft.SwingTrade.Contracts.Trades;
using Spydersoft.SwingTrade.Data;
using Spydersoft.SwingTrade.Data.Entities;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Api.Services;

public class TradeService(SwingTradeDbContext db, SettingsService settings, TimeProvider clock)
{
    public async Task<List<TradeDto>> ListAsync(TradeListQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var ctx = await LoadContextAsync(ct);
        var rows = ctx.Trades.AsEnumerable();
        if (query.Status is { } status)
            rows = rows.Where(t => t.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Ticker))
        {
            var ticker = InstrumentService.Normalize(query.Ticker);
            rows = rows.Where(t => t.Ticker == ticker);
        }

        return rows.OrderByDescending(t => t.OpenDate ?? DateOnly.MaxValue).ThenByDescending(t => t.Id)
            .Select(ctx.ToDto).ToList();
    }

    /// <summary>Closed trades with their P&L under current settings, for the wash-sale rules (watchlist re-entry date).</summary>
    public async Task<IReadOnlyList<ClosedTradeRef>> ClosedRefsAsync(CancellationToken ct = default) =>
        (await LoadContextAsync(ct)).Closed;

    public async Task<ServiceResult<TradeDto>> GetAsync(int id, CancellationToken ct = default)
    {
        var ctx = await LoadContextAsync(ct);
        var trade = ctx.Trades.FirstOrDefault(t => t.Id == id);
        return trade is null ? ServiceResult<TradeDto>.NotFound() : ServiceResult<TradeDto>.Ok(ctx.ToDto(trade));
    }

    public async Task<ServiceResult<TradeDto>> CreateAsync(CreateTradeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new ValidationErrors();
        if (request.Status is not (TradeStatus.Planned or TradeStatus.Open))
            errors.Add(nameof(request.Status), "A new trade must be Planned or Open.");
        var ticker = InstrumentService.Normalize(request.Ticker);
        await ValidateAsync(errors, new Fields(ticker, request.Status, request.Setup, request.Shares, request.EntryPrice,
            request.StopPrice, request.TargetPrice, request.OpenDate, null, null), ct);
        if (errors.Any)
            return errors.ToResult<TradeDto>();

        var now = clock.GetUtcNow();
        var trade = new Trade
        {
            Ticker = ticker,
            Status = request.Status,
            Setup = request.Setup,
            OpenDate = request.OpenDate,
            Shares = request.Shares,
            EntryPrice = request.EntryPrice,
            StopPrice = request.StopPrice,
            TargetPrice = request.TargetPrice,
            Rationale = Clean(request.Rationale),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Trades.Add(trade);
        await db.SaveChangesAsync(ct);
        return await GetAsync(trade.Id, ct);
    }

    public async Task<ServiceResult<TradeDto>> UpdateAsync(int id, UpdateTradeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (trade is null)
            return ServiceResult<TradeDto>.NotFound();

        var errors = new ValidationErrors();
        var ticker = InstrumentService.Normalize(request.Ticker);
        await ValidateAsync(errors, new Fields(ticker, trade.Status, request.Setup, request.Shares, request.EntryPrice,
            request.StopPrice, request.TargetPrice, request.OpenDate, request.ExitPrice, request.ExitDate), ct);
        if (errors.Any)
            return errors.ToResult<TradeDto>();

        trade.Ticker = ticker;
        trade.Setup = request.Setup;
        trade.OpenDate = request.OpenDate;
        trade.Shares = request.Shares;
        trade.EntryPrice = request.EntryPrice;
        trade.StopPrice = request.StopPrice;
        trade.TargetPrice = request.TargetPrice;
        trade.ExitPrice = request.ExitPrice;
        trade.ExitDate = request.ExitDate;
        trade.FollowedPlan = request.FollowedPlan;
        trade.Rationale = Clean(request.Rationale);
        trade.Lessons = Clean(request.Lessons);
        trade.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (trade is null)
            return false;

        db.Trades.Remove(trade);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Planned to Open. The fill price replaces the limit price.</summary>
    public async Task<ServiceResult<TradeDto>> FillAsync(int id, FillTradeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (trade is null)
            return ServiceResult<TradeDto>.NotFound();
        if (trade.Status != TradeStatus.Planned)
            return ServiceResult<TradeDto>.Conflict($"Only a Planned trade can be filled; this trade is {trade.Status}.");

        var errors = new ValidationErrors();
        if (request.FillPrice <= 0m)
            errors.Add(nameof(request.FillPrice), "Fill price must be greater than zero.");
        else if (request.FillPrice <= trade.StopPrice)
            errors.Add(nameof(request.FillPrice), "Fill price must be above the stop.");
        if (request.FillDate == default)
            errors.Add(nameof(request.FillDate), "Fill date is required.");
        if (errors.Any)
            return errors.ToResult<TradeDto>();

        trade.Status = TradeStatus.Open;
        trade.EntryPrice = request.FillPrice;
        trade.OpenDate = request.FillDate;
        trade.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Open to Closed.</summary>
    public async Task<ServiceResult<TradeDto>> CloseAsync(int id, CloseTradeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (trade is null)
            return ServiceResult<TradeDto>.NotFound();
        if (trade.Status != TradeStatus.Open)
            return ServiceResult<TradeDto>.Conflict($"Only an Open trade can be closed; this trade is {trade.Status}.");

        var errors = new ValidationErrors();
        if (request.ExitPrice <= 0m)
            errors.Add(nameof(request.ExitPrice), "Exit price must be greater than zero.");
        if (request.ExitDate == default)
            errors.Add(nameof(request.ExitDate), "Exit date is required.");
        else if (trade.OpenDate is { } open && request.ExitDate < open)
            errors.Add(nameof(request.ExitDate), "Exit date cannot be before the open date.");
        if (errors.Any)
            return errors.ToResult<TradeDto>();

        trade.Status = TradeStatus.Closed;
        trade.ExitPrice = request.ExitPrice;
        trade.ExitDate = request.ExitDate;
        trade.FollowedPlan = request.FollowedPlan ?? trade.FollowedPlan;
        trade.Lessons = Clean(request.Lessons) ?? trade.Lessons;
        trade.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Planned to Cancelled.</summary>
    public async Task<ServiceResult<TradeDto>> CancelAsync(int id, CancellationToken ct = default)
    {
        var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (trade is null)
            return ServiceResult<TradeDto>.NotFound();
        if (trade.Status != TradeStatus.Planned)
            return ServiceResult<TradeDto>.Conflict($"Only a Planned trade can be cancelled; this trade is {trade.Status}.");

        trade.Status = TradeStatus.Cancelled;
        trade.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private sealed record Fields(
        string Ticker, TradeStatus Status, TradeSetup Setup, int Shares, decimal Entry, decimal Stop, decimal Target,
        DateOnly? OpenDate, decimal? ExitPrice, DateOnly? ExitDate);

    private async Task ValidateAsync(ValidationErrors e, Fields f, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(f.Ticker))
            e.Add("Ticker", "Ticker is required.");
        else if (!await db.Instruments.AnyAsync(i => i.Ticker == f.Ticker, ct))
            e.Add("Ticker", $"Unknown ticker {f.Ticker}; add it as an instrument first.");

        if (!Enum.IsDefined(f.Setup)) e.Add("Setup", "Unknown setup.");
        if (f.Shares < 1) e.Add("Shares", "Shares must be at least 1.");
        if (f.Entry <= 0m) e.Add("EntryPrice", "Entry price must be greater than zero.");
        if (f.Stop <= 0m) e.Add("StopPrice", "Stop price must be greater than zero.");
        if (f.Target <= 0m) e.Add("TargetPrice", "Target price must be greater than zero.");
        if (f.Entry > 0m && f.Stop > 0m && f.Entry <= f.Stop)
            e.Add("EntryPrice", "Entry must be above the stop.");

        var needsOpen = f.Status is TradeStatus.Open or TradeStatus.Closed;
        if (needsOpen && f.OpenDate is null)
            e.Add("OpenDate", "An Open or Closed trade needs an open date.");
        if (f.Status == TradeStatus.Planned && f.OpenDate is not null)
            e.Add("OpenDate", "A Planned trade has no open date until it fills.");

        if (f.Status == TradeStatus.Closed)
        {
            if (f.ExitPrice is null or <= 0m)
                e.Add("ExitPrice", "A Closed trade needs an exit price greater than zero.");
            if (f.ExitDate is null)
                e.Add("ExitDate", "A Closed trade needs an exit date.");
            else if (f.OpenDate is { } open && f.ExitDate < open)
                e.Add("ExitDate", "Exit date cannot be before the open date.");
        }
        else
        {
            if (f.ExitPrice is not null) e.Add("ExitPrice", "Only a Closed trade has an exit price.");
            if (f.ExitDate is not null) e.Add("ExitDate", "Only a Closed trade has an exit date.");
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<TradeContext> LoadContextAsync(CancellationToken ct)
    {
        var s = await settings.GetRulesAsync(ct);
        var trades = await db.Trades.AsNoTracking().ToListAsync(ct);
        var groups = await db.Instruments.AsNoTracking().Where(i => i.WashSaleGroup != null)
            .ToDictionaryAsync(i => i.Ticker, i => i.WashSaleGroup!, ct);

        decimal? accountValue = null;
        if (s.RiskBasis == RiskBasis.AccountValue)
            accountValue = DashboardCalculator.Compute(trades.Select(TradeMapping.ToDashboardTrade).ToList(), s, null).AccountValue;
        var capital = PositionSizer.CapitalFor(s, accountValue);

        var closed = trades
            .Where(t => t is { Status: TradeStatus.Closed, OpenDate: not null, ExitDate: not null, ExitPrice: not null })
            .Select(t => new ClosedTradeRef(
                t.Id, t.Ticker, groups.GetValueOrDefault(t.Ticker), t.OpenDate!.Value, t.ExitDate!.Value,
                TradeMath.Compute(TradeMapping.ToInput(t), s, capital).Pnl ?? 0m))
            .ToList();

        return new TradeContext(trades, s, capital, closed, groups);
    }

    private sealed class TradeContext(
        List<Trade> trades, RulesSettings s, decimal capital, List<ClosedTradeRef> closed, Dictionary<string, string> groups)
    {
        public List<Trade> Trades { get; } = trades;
        public List<ClosedTradeRef> Closed => closed;

        public TradeDto ToDto(Trade t)
        {
            var m = TradeMath.Compute(TradeMapping.ToInput(t), s, capital);
            var washSale = t is { Status: TradeStatus.Open or TradeStatus.Closed, OpenDate: not null }
                && WashSale.IsFlagged(t.Id, t.Ticker, groups.GetValueOrDefault(t.Ticker), t.OpenDate.Value, closed, s);

            return new TradeDto(
                t.Id, t.Ticker, t.Status, t.Setup, t.OpenDate, t.Shares, t.EntryPrice, t.StopPrice, t.TargetPrice,
                t.ExitPrice, t.ExitDate, t.FollowedPlan, t.Rationale, t.Lessons, t.CreatedAt, t.UpdatedAt,
                m.Position, m.RiskDollars, m.RiskPct, m.PlannedRewardRisk, m.Pnl, m.PnlPct, m.RMultiple, m.DaysHeld,
                m.RiskOverLimit, m.RewardRiskBelowMin, washSale);
        }
    }
}
