using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spydersoft.SwingTrade.Api.Services;
using Spydersoft.SwingTrade.Contracts.Dashboard;

namespace Spydersoft.SwingTrade.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
public class DashboardController(DashboardService dashboard) : ApiControllerBase
{
    [HttpGet]
    [Authorize(Policy = SwingTradeAuth.Policies.Read)]
    public async Task<ActionResult<DashboardDto>> Get(CancellationToken ct) =>
        Ok(await dashboard.GetAsync(ct));
}
