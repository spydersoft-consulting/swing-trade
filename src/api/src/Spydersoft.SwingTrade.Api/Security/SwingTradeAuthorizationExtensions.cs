using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Spydersoft.SwingTrade.Api.Mcp;

namespace Spydersoft.SwingTrade.Api.Security;

public static class SwingTradeAuthorizationExtensions
{
    /// <summary>
    /// Registers the Read, Write, Admin and Mcp policies. Each requires an authenticated user, the swingtrade audience,
    /// a sub claim, and a role. Admin satisfies Write, Write satisfies Read. Mcp is independent of the REST roles.
    /// McpOwner is Mcp plus the configured owner subject; it guards the /mcp route and fails closed with no owner configured.
    /// </summary>
    public static IServiceCollection AddSwingTradeAuthorization(this IServiceCollection services)
    {
        services.AddOptions();
        services.AddSingleton<IAuthorizationHandler, McpOwnerHandler>();
        return services.AddAuthorizationBuilder()
            .AddPolicy(SwingTradeAuth.Policies.Read, p => Build(p, SwingTradeAuth.Roles.Reader, SwingTradeAuth.Roles.Trader, SwingTradeAuth.Roles.Admin))
            .AddPolicy(SwingTradeAuth.Policies.Write, p => Build(p, SwingTradeAuth.Roles.Trader, SwingTradeAuth.Roles.Admin))
            .AddPolicy(SwingTradeAuth.Policies.Admin, p => Build(p, SwingTradeAuth.Roles.Admin))
            .AddPolicy(SwingTradeAuth.Policies.Mcp, p => Build(p, SwingTradeAuth.Roles.Mcp))
            .AddPolicy(SwingTradeAuth.Policies.McpOwner, p =>
            {
                Build(p, SwingTradeAuth.Roles.Mcp);
                p.AddRequirements(new McpOwnerRequirement());
            })
            .Services;
    }

    /// <summary>
    /// Apply to JwtBearerOptions: no claim-type remapping, roles read from the "role" claim, and the /mcp 401 challenge
    /// carries the RFC 9728 <c>resource_metadata</c> pointer. Tokens are read from the Authorization header only, never the query string.
    /// </summary>
    public static void ApplySwingTradeTokenValidation(this JwtBearerOptions options)
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters.RoleClaimType = SwingTradeAuth.RoleClaimType;
        options.TokenValidationParameters.NameClaimType = SwingTradeAuth.SubjectClaimType;
        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidAudience = SwingTradeAuth.Audience;
        options.Events = new JwtBearerEvents { OnChallenge = McpChallenge };
    }

    private static Task McpChallenge(JwtBearerChallengeContext context)
    {
        var request = context.HttpContext.Request;
        if (!request.Path.StartsWithSegments(McpOptions.McpPath, StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        var metadata = context.HttpContext.RequestServices.GetRequiredService<IOptions<McpOptions>>().Value.MetadataUrl;
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        var challenge = metadata is null ? "Bearer" : $"Bearer resource_metadata=\"{metadata}\"";
        if (context.AuthenticateFailure is not null)
            challenge += ", error=\"invalid_token\"";
        context.Response.Headers.WWWAuthenticate = challenge;
        return Task.CompletedTask;
    }

    private static void Build(AuthorizationPolicyBuilder policy, params string[] roles) =>
        policy.RequireAuthenticatedUser()
            .RequireAssertion(ctx => ctx.User.HasClaim(c => c.Type == SwingTradeAuth.AudienceClaimType && c.Value == SwingTradeAuth.Audience))
            .RequireClaim(SwingTradeAuth.SubjectClaimType)
            .RequireAssertion(ctx => ctx.User.Claims.Any(c => c.Type == SwingTradeAuth.RoleClaimType && roles.Contains(c.Value)));
}

public sealed class McpOwnerRequirement : IAuthorizationRequirement;

/// <summary>Allows only the configured owner subject. With none configured nobody passes (fail closed).</summary>
public sealed class McpOwnerHandler(IOptions<McpOptions> options) : AuthorizationHandler<McpOwnerRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, McpOwnerRequirement requirement)
    {
        var owner = options.Value.OwnerSubject;
        var sub = context.User.FindFirst(SwingTradeAuth.SubjectClaimType)?.Value;
        if (!string.IsNullOrWhiteSpace(owner) && string.Equals(sub, owner, StringComparison.Ordinal))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
