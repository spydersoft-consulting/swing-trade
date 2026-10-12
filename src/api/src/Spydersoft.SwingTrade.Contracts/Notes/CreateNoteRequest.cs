using System.ComponentModel.DataAnnotations;

namespace Spydersoft.SwingTrade.Contracts.Notes;

public class CreateNoteRequest
{
    public DateOnly Date { get; set; }

    [MaxLength(16)]
    public string? Ticker { get; set; }

    public NoteKind Kind { get; set; } = NoteKind.General;

    [MaxLength(4000)]
    public string Text { get; set; } = string.Empty;
}
