namespace Spydersoft.SwingTrade.Contracts.Notes;

public class NoteListQuery
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public NoteKind? Kind { get; set; }
    public string? Ticker { get; set; }
}
