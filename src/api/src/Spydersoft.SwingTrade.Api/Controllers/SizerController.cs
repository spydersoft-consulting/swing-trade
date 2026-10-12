using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spydersoft.SwingTrade.Api.Services;
using Spydersoft.SwingTrade.Contracts.Sizer;

namespace Spydersoft.SwingTrade.Api.Controllers;

[ApiController]
[Route("api/v1/sizer")]
public class SizerController(SizerService sizer) : ApiControllerBase
{
    /// <summary>A pure calculation (it saves nothing), so it needs only the Read policy.</summary>
    [HttpPost]
    [Authorize(Policy = SwingTradeAuth.Policies.Read)]
    public async Task<ActionResult<SizerResultDto>> Size(SizerRequest request, CancellationToken ct) =>
        Respond(await sizer.SizeAsync(request, ct));
}
