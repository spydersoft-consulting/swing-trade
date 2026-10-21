namespace Spydersoft.SwingTrade.Api.Mcp;

/// <summary>Logs at Debug the claims of the /mcp caller's token (never the token itself). Enable with <c>Logging__LogLevel__Spydersoft.SwingTrade.Api.Mcp.McpClaimsDiagnosticMiddleware=Debug</c>.</summary>
public sealed class McpClaimsDiagnosticMiddleware(RequestDelegate next, ILogger<McpClaimsDiagnosticMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (McpExtensions.IsMcpRoute(context) && context.User.Identity?.IsAuthenticated == true)
        {
            var user = context.User;
            logger.LogDebug(
                "MCP token claims: sub {Sub}, client {Client}, aud [{Aud}], roles [{Roles}], scope [{Scope}]",
                user.FindFirst(SwingTradeAuth.SubjectClaimType)?.Value,
                user.FindFirst("client_id")?.Value,
                string.Join(',', user.FindAll(SwingTradeAuth.AudienceClaimType).Select(c => c.Value)),
                string.Join(',', user.FindAll(SwingTradeAuth.RoleClaimType).Select(c => c.Value)),
                string.Join(',', user.FindAll("scope").Select(c => c.Value)));
        }

        await next(context);
    }
}
