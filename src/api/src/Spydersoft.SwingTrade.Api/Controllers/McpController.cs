using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spydersoft.SwingTrade.Api.Services;
using Spydersoft.SwingTrade.Contracts.Mcp;

namespace Spydersoft.SwingTrade.Api.Controllers;

/// <summary>Facts about the MCP endpoint for the Settings screen. The endpoint itself (/mcp) is not a controller.</summary>
[ApiController]
[Route("api/v1/mcp")]
public class McpController(McpAdminService mcp) : ApiControllerBase
{
    [HttpGet("info")]
    [Authorize(Policy = SwingTradeAuth.Policies.Read)]
    public ActionResult<McpInfoDto> GetInfo() => Ok(mcp.GetInfo());

    /// <summary>The latest audited MCP calls, newest first: <c>?take=20</c> (default 20, at most 200).</summary>
    [HttpGet("calls")]
    [Authorize(Policy = SwingTradeAuth.Policies.Admin)]
    public async Task<ActionResult<List<McpCallDto>>> GetCalls([FromQuery] int take = McpAdminService.DefaultTake, CancellationToken ct = default) =>
        Ok(await mcp.ListCallsAsync(take, ct));
}
