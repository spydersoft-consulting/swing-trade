using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spydersoft.SwingTrade.Contracts.Auth;

namespace Spydersoft.SwingTrade.Api.Controllers;

/// <summary>The caller's identity and roles, read from the access token the API already validated. Nothing is stored.</summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController : ApiControllerBase
{
    /// <summary>
    /// A caller with no <c>swingtrade.*</c> REST role gets 403 here like everywhere else; the UI treats that as
    /// "Access not granted". Roles are hierarchical (admin satisfies trader satisfies reader), as in the policies.
    /// </summary>
    [HttpGet("info")]
    [Authorize(Policy = SwingTradeAuth.Policies.Read)]
    public ActionResult<AuthInfoDto> GetInfo()
    {
        var roles = User.Claims
            .Where(c => c.Type == SwingTradeAuth.RoleClaimType)
            .Select(c => c.Value)
            .Where(r => r.StartsWith("swingtrade.", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var isAdmin = roles.Contains(SwingTradeAuth.Roles.Admin);
        var canWrite = isAdmin || roles.Contains(SwingTradeAuth.Roles.Trader);
        var canRead = canWrite || roles.Contains(SwingTradeAuth.Roles.Reader);

        return Ok(new AuthInfoDto(
            User.FindFirst(SwingTradeAuth.SubjectClaimType)?.Value,
            roles,
            canRead,
            canWrite,
            isAdmin,
            SwingTradeAuth.Audience,
            SwingTradeAuth.Roles.Reader,
            SwingTradeAuth.Roles.Trader,
            SwingTradeAuth.Roles.Admin,
            SwingTradeAuth.Roles.Mcp,
            SwingTradeAuth.Policies.Read,
            SwingTradeAuth.Policies.Write,
            SwingTradeAuth.Policies.Admin,
            SwingTradeAuth.Policies.Mcp));
    }
}
