using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spydersoft.SwingTrade.Api.Services;
using Spydersoft.SwingTrade.Contracts.Notes;

namespace Spydersoft.SwingTrade.Api.Controllers;

[ApiController]
[Route("api/v1/notes")]
public class NotesController(NoteService notes) : ApiControllerBase
{
    [HttpGet]
    [Authorize(Policy = SwingTradeAuth.Policies.Read)]
    public async Task<ActionResult<List<NoteDto>>> GetAll([FromQuery] NoteListQuery query, CancellationToken ct) =>
        Ok(await notes.ListAsync(query, ct));

    [HttpPost]
    [Authorize(Policy = SwingTradeAuth.Policies.Write)]
    public async Task<ActionResult<NoteDto>> Create(CreateNoteRequest request, CancellationToken ct) =>
        Respond(await notes.CreateAsync(request, ct), dto => Created($"/api/v1/notes/{dto.Id}", dto));
}
