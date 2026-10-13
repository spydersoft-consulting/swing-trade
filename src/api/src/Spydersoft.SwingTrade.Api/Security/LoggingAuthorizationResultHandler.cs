using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Spydersoft.SwingTrade.Api.Security;

/// <summary>
/// Wraps the default authorization middleware result handler and logs every refusal: the user's <c>sub</c>, the path and
/// the policy the endpoint required (DECISIONS #35, #103). It never reads the Authorization header or logs a token.
/// A 401 (no or invalid token) is Information because scanners and expired sessions cause plenty of those; a 403 (a valid
/// user without the role, audience or owner match) is Warning.
/// </summary>
public sealed class LoggingAuthorizationResultHandler(ILogger<LoggingAuthorizationResultHandler> logger) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Succeeded)
        {
            var required = RequiredPolicy(context);
            var path = context.Request.Path.Value;
            if (authorizeResult.Forbidden)
            {
                var sub = context.User.FindFirst(SwingTradeAuth.SubjectClaimType)?.Value ?? "(none)";
                logger.LogWarning("Authorization refused (403): sub {Sub}, path {Path}, required policy {Policy}", sub, path, required);
            }
            else if (authorizeResult.Challenged)
            {
                logger.LogInformation("Authentication required (401): path {Path}, required policy {Policy}", path, required);
            }
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    private static string RequiredPolicy(HttpContext context)
    {
        var names = context.GetEndpoint()?.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(a => a.Policy)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .ToArray() ?? [];
        return names.Length == 0 ? "(default)" : string.Join('+', names);
    }
}
