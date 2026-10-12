using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spydersoft.SwingTrade.Api.Services;

namespace Spydersoft.SwingTrade.Api.Controllers;

[ApiController]
[Route("api/v1/export")]
public class ExportController(ExportService export) : ApiControllerBase
{
    [HttpGet("trades.csv")]
    [Authorize(Policy = SwingTradeAuth.Policies.Read)]
    [Produces("text/csv")]
    public async Task<IActionResult> TradesCsv(CancellationToken ct) =>
        File(new UTF8Encoding(false).GetBytes(await export.TradesCsvAsync(ct)), ExportService.ContentType, export.FileName());
}
