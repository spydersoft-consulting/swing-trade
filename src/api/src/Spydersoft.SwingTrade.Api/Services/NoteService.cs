using Microsoft.EntityFrameworkCore;
using Spydersoft.SwingTrade.Contracts.Notes;
using Spydersoft.SwingTrade.Data;
using Spydersoft.SwingTrade.Data.Entities;

namespace Spydersoft.SwingTrade.Api.Services;

public class NoteService(SwingTradeDbContext db)
{
    public async Task<List<NoteDto>> ListAsync(NoteListQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var notes = db.JournalNotes.AsNoTracking().AsQueryable();
        if (query.From is { } from) notes = notes.Where(n => n.Date >= from);
        if (query.To is { } to) notes = notes.Where(n => n.Date <= to);
        if (query.Kind is { } kind) notes = notes.Where(n => n.Kind == kind);
        if (!string.IsNullOrWhiteSpace(query.Ticker))
        {
            var ticker = InstrumentService.Normalize(query.Ticker);
            notes = notes.Where(n => n.Ticker == ticker);
        }

        var rows = await notes.OrderByDescending(n => n.Date).ThenByDescending(n => n.Id).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<ServiceResult<NoteDto>> CreateAsync(CreateNoteRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new ValidationErrors();
        if (request.Date == default) errors.Add(nameof(request.Date), "Date is required.");
        if (string.IsNullOrWhiteSpace(request.Text)) errors.Add(nameof(request.Text), "Text is required.");
        if (!Enum.IsDefined(request.Kind)) errors.Add(nameof(request.Kind), "Unknown note kind.");
        if (errors.Any)
            return errors.ToResult<NoteDto>();

        var note = new JournalNote
        {
            Date = request.Date,
            Ticker = string.IsNullOrWhiteSpace(request.Ticker) ? null : InstrumentService.Normalize(request.Ticker),
            Kind = request.Kind,
            Text = request.Text.Trim()
        };
        db.JournalNotes.Add(note);
        await db.SaveChangesAsync(ct);

        return ServiceResult<NoteDto>.Ok(ToDto(note));
    }

    private static NoteDto ToDto(JournalNote n) => new(n.Id, n.Date, n.Ticker, n.Kind, n.Text);
}
