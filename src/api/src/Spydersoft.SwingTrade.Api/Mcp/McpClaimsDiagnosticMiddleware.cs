namespace Spydersoft.SwingTrade.Api.Mcp;

/// <summary>TEMPORARY: logs the roles and audience the /mcp caller's token carries. Never logs the token. Remove once the connector role question is settled.</summary>
public sealed class McpClaimsDiagnosticMiddleware(RequestDelegate next, ILogger<McpClaimsDiagnosticMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (McpExtensions.IsMcpRoute(context) && context.User.Identity?.IsAuthenticated == true)
        {
            var user = context.User;
            logger.LogInformation(
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
