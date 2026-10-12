using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spydersoft.SwingTrade.Api.Services;
using Spydersoft.SwingTrade.Contracts.Bars;
using Spydersoft.SwingTrade.Contracts.Instruments;

namespace Spydersoft.SwingTrade.Api.Controllers;

[ApiController]
[Route("api/v1/instruments")]
public class InstrumentsController(InstrumentService instruments, BarService bars) : ApiControllerBase
{
    [HttpGet]
    [Authorize(Policy = SwingTradeAuth.Policies.Read)]
    public async Task<ActionResult<List<InstrumentDto>>> GetAll([FromQuery] bool? active, CancellationToken ct) =>
        Ok(await instruments.ListAsync(active, ct));

    [HttpPost]
    [Authorize(Policy = SwingTradeAuth.Policies.Write)]
    public async Task<ActionResult<InstrumentDto>> Create(CreateInstrumentRequest request, CancellationToken ct) =>
        Respond(await instruments.CreateAsync(request, ct),
            dto => Created($"/api/v1/instruments/{dto.Ticker}", dto));

    [HttpPut("{ticker}")]
    [Authorize(Policy = SwingTradeAuth.Policies.Write)]
    public async Task<ActionResult<InstrumentDto>> Update(string ticker, UpdateInstrumentRequest request, CancellationToken ct) =>
        Respond(await instruments.UpdateAsync(ticker, request, ct));

    /// <summary>Completed closes with MA20 and MA50 for the chart: <c>?days=120</c> (default and maximum 120).</summary>
    [HttpGet("{ticker}/bars")]
    [Authorize(Policy = SwingTradeAuth.Policies.Read)]
    public async Task<ActionResult<BarsDto>> GetBars(string ticker, [FromQuery] BarsQuery query, CancellationToken ct) =>
        Respond(await bars.GetAsync(ticker, query, ct));
}
