namespace Spydersoft.SwingTrade.Contracts.Notes;

public sealed record NoteDto(int Id, DateOnly Date, string? Ticker, NoteKind Kind, string Text);
