using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Spydersoft.SwingTrade.Contracts.Instruments;
using Spydersoft.SwingTrade.Data;
using Spydersoft.SwingTrade.Data.Entities;

namespace Spydersoft.SwingTrade.Api.Services;

public partial class InstrumentService(SwingTradeDbContext db)
{
    public async Task<List<InstrumentDto>> ListAsync(bool? active, CancellationToken ct = default)
    {
        var query = db.Instruments.AsNoTracking();
        if (active is { } a)
            query = query.Where(i => i.Active == a);

        var rows = await query.OrderBy(i => i.Ticker).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<ServiceResult<InstrumentDto>> CreateAsync(CreateInstrumentRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticker = Normalize(request.Ticker);
        var errors = new ValidationErrors();
        if (!TickerPattern().IsMatch(ticker))
            errors.Add(nameof(request.Ticker), "Ticker is required and may contain only letters, digits, dot and dash.");
        if (string.IsNullOrWhiteSpace(request.Name))
            errors.Add(nameof(request.Name), "Name is required.");
        LengthChecks(errors, request.Name, request.Category, request.WashSaleGroup);
        if (errors.Any)
            return errors.ToResult<InstrumentDto>();

        if (await db.Instruments.AnyAsync(i => i.Ticker == ticker, ct))
            return ServiceResult<InstrumentDto>.Conflict($"Instrument {ticker} already exists.");

        var instrument = new Instrument
        {
            Ticker = ticker,
            Name = request.Name.Trim(),
            Category = Clean(request.Category),
            Active = request.Active,
            IsBenchmark = request.IsBenchmark,
            WashSaleGroup = Clean(request.WashSaleGroup)
        };
        db.Instruments.Add(instrument);
        if (instrument.IsBenchmark)
            await ClearOtherBenchmarksAsync(ticker, ct);
        await db.SaveChangesAsync(ct);

        return ServiceResult<InstrumentDto>.Ok(ToDto(instrument));
    }

    public async Task<ServiceResult<InstrumentDto>> UpdateAsync(string ticker, UpdateInstrumentRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = Normalize(ticker);
        var instrument = await db.Instruments.FirstOrDefaultAsync(i => i.Ticker == key, ct);
        if (instrument is null)
            return ServiceResult<InstrumentDto>.NotFound();

        var errors = new ValidationErrors();
        if (string.IsNullOrWhiteSpace(request.Name))
            errors.Add(nameof(request.Name), "Name is required.");
        LengthChecks(errors, request.Name, request.Category, request.WashSaleGroup);
        if (errors.Any)
            return errors.ToResult<InstrumentDto>();

        instrument.Name = request.Name.Trim();
        instrument.Category = Clean(request.Category);
        instrument.Active = request.Active;
        instrument.IsBenchmark = request.IsBenchmark;
        instrument.WashSaleGroup = Clean(request.WashSaleGroup);
        if (instrument.IsBenchmark)
            await ClearOtherBenchmarksAsync(key, ct);
        await db.SaveChangesAsync(ct);

        return ServiceResult<InstrumentDto>.Ok(ToDto(instrument));
    }

    private static void LengthChecks(ValidationErrors errors, string? name, string? category, string? washSaleGroup)
    {
        errors.MaxLength("Name", name, 200);
        errors.MaxLength("Category", category, 100);
        errors.MaxLength("WashSaleGroup", washSaleGroup, 50);
    }

    // Only one instrument can be the benchmark.
    private async Task ClearOtherBenchmarksAsync(string ticker, CancellationToken ct)
    {
        var others = await db.Instruments.Where(i => i.IsBenchmark && i.Ticker != ticker).ToListAsync(ct);
        foreach (var other in others)
            other.IsBenchmark = false;
    }

    internal static string Normalize(string? ticker) => (ticker ?? string.Empty).Trim().ToUpperInvariant();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static InstrumentDto ToDto(Instrument i) =>
        new(i.Ticker, i.Name, i.Category, i.Active, i.IsBenchmark, i.WashSaleGroup);

    [GeneratedRegex("^[A-Z0-9.-]{1,16}$")]
    private static partial Regex TickerPattern();
}
