using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spydersoft.SwingTrade.Api.Services;
using Spydersoft.SwingTrade.Contracts.Trades;

namespace Spydersoft.SwingTrade.Api.Controllers;

[ApiController]
[Route("api/v1/trades")]
public class TradesController(TradeService trades) : ApiControllerBase
{
    [HttpGet]
    [Authorize(Policy = SwingTradeAuth.Policies.Read)]
    public async Task<ActionResult<List<TradeDto>>> GetAll([FromQuery] TradeListQuery query, CancellationToken ct) =>
        Ok(await trades.ListAsync(query, ct));

    [HttpGet("{id:int}")]
    [Authorize(Policy = SwingTradeAuth.Policies.Read)]
    public async Task<ActionResult<TradeDto>> GetById(int id, CancellationToken ct) =>
        Respond(await trades.GetAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = SwingTradeAuth.Policies.Write)]
    public async Task<ActionResult<TradeDto>> Create(CreateTradeRequest request, CancellationToken ct) =>
        Respond(await trades.CreateAsync(request, ct), dto => CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto));

    [HttpPut("{id:int}")]
    [Authorize(Policy = SwingTradeAuth.Policies.Write)]
    public async Task<ActionResult<TradeDto>> Update(int id, UpdateTradeRequest request, CancellationToken ct) =>
        Respond(await trades.UpdateAsync(id, request, ct));

    [HttpDelete("{id:int}")]
    [Authorize(Policy = SwingTradeAuth.Policies.Write)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await trades.DeleteAsync(id, ct) ? NoContent() : NotFound();

    [HttpPost("{id:int}/fill")]
    [Authorize(Policy = SwingTradeAuth.Policies.Write)]
    public async Task<ActionResult<TradeDto>> Fill(int id, FillTradeRequest request, CancellationToken ct) =>
        Respond(await trades.FillAsync(id, request, ct));

    [HttpPost("{id:int}/close")]
    [Authorize(Policy = SwingTradeAuth.Policies.Write)]
    public async Task<ActionResult<TradeDto>> Close(int id, CloseTradeRequest request, CancellationToken ct) =>
        Respond(await trades.CloseAsync(id, request, ct));

    [HttpPost("{id:int}/cancel")]
    [Authorize(Policy = SwingTradeAuth.Policies.Write)]
    public async Task<ActionResult<TradeDto>> Cancel(int id, CancellationToken ct) =>
        Respond(await trades.CancelAsync(id, ct));
}
