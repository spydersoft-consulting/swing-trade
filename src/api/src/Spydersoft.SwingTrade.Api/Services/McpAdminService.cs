using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Spydersoft.SwingTrade.Api.Mcp;
using Spydersoft.SwingTrade.Contracts.Mcp;
using Spydersoft.SwingTrade.Data;

namespace Spydersoft.SwingTrade.Api.Services;

/// <summary>Backs the Settings MCP panel: the endpoint facts and the latest audited calls.</summary>
public class McpAdminService(SwingTradeDbContext db, IOptions<McpOptions> options)
{
    public const int DefaultTake = 20;
    public const int MaxTake = 200;

    public McpInfoDto GetInfo()
    {
        var o = options.Value;
        return new McpInfoDto(o.PublicUrl, o.OwnerSubject, o.EnableAddNote, SwingTradeAuth.Audience,
            SwingTradeAuth.Roles.Mcp, SwingTradeAuth.Roles.Trader);
    }

    public async Task<List<McpCallDto>> ListCallsAsync(int take, CancellationToken ct = default)
    {
        var rows = await db.McpCallLogs.AsNoTracking()
            .OrderByDescending(l => l.At).ThenByDescending(l => l.Id)
            .Take(Math.Clamp(take, 1, MaxTake))
            .ToListAsync(ct);
        return rows.Select(l => new McpCallDto(l.Id, l.At, l.Tool, l.Arguments, l.DurationMs, l.Caller, l.Status)).ToList();
    }
}
