using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spydersoft.SwingTrade.Api.Refresh;
using Spydersoft.SwingTrade.Contracts.Common;
using Spydersoft.SwingTrade.Contracts.Refresh;

namespace Spydersoft.SwingTrade.Api.Controllers;

[ApiController]
[Route("api/v1/refresh")]
public class RefreshController(RefreshService refresh, IRefreshLauncher launcher) : ApiControllerBase
{
    /// <summary>Starts a refresh now. 202 with the run, or 409 if one is already running.</summary>
    [HttpPost]
    [Authorize(Policy = SwingTradeAuth.Policies.Admin)]
    public async Task<ActionResult<RefreshRunDto>> Start(CancellationToken ct) =>
        Respond(await refresh.StartManualAsync(ct), dto =>
        {
            launcher.Launch(dto.Id);
            return Accepted($"/api/v1/refresh/runs/{dto.Id}", dto);
        });

    [HttpGet("runs")]
    [Authorize(Policy = SwingTradeAuth.Policies.Read)]
    public async Task<ActionResult<PagedResult<RefreshRunDto>>> GetRuns([FromQuery] RefreshRunListQuery query, CancellationToken ct) =>
        Ok(await refresh.ListAsync(query, ct));

    [HttpGet("runs/{id:int}")]
    [Authorize(Policy = SwingTradeAuth.Policies.Read)]
    public async Task<ActionResult<RefreshRunDto>> GetRun(int id, CancellationToken ct) =>
        Respond(await refresh.GetAsync(id, ct));
}
