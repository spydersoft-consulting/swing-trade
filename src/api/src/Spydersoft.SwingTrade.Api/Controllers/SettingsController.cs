using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spydersoft.SwingTrade.Api.Services;
using Spydersoft.SwingTrade.Contracts.Settings;

namespace Spydersoft.SwingTrade.Api.Controllers;

[ApiController]
[Route("api/v1/settings")]
public class SettingsController(SettingsService settings) : ApiControllerBase
{
    [HttpGet]
    [Authorize(Policy = SwingTradeAuth.Policies.Read)]
    public async Task<ActionResult<SettingsDto>> Get(CancellationToken ct) =>
        Ok(await settings.GetAsync(ct));

    [HttpPut]
    [Authorize(Policy = SwingTradeAuth.Policies.Admin)]
    public async Task<ActionResult<SettingsDto>> Update(UpdateSettingsRequest request, CancellationToken ct) =>
        Respond(await settings.UpdateAsync(request, ct));
}
