using Spydersoft.SwingTrade.Contracts.Notes;

namespace Spydersoft.SwingTrade.Data.Entities;

public class JournalNote
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public string? Ticker { get; set; }
    public NoteKind Kind { get; set; }
    public string Text { get; set; } = string.Empty;
}
