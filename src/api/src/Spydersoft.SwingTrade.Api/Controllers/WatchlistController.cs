using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spydersoft.SwingTrade.Api.Services;
using Spydersoft.SwingTrade.Contracts.Watchlist;

namespace Spydersoft.SwingTrade.Api.Controllers;

[ApiController]
[Route("api/v1/watchlist")]
public class WatchlistController(WatchlistService watchlist) : ApiControllerBase
{
    [HttpGet]
    [Authorize(Policy = SwingTradeAuth.Policies.Read)]
    public async Task<ActionResult<List<WatchlistRowDto>>> Get([FromQuery] WatchlistQuery query, CancellationToken ct) =>
        Respond(await watchlist.GetAsync(query, ct));
}
