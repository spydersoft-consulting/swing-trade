using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Spydersoft.SwingTrade.Api.Security;

public static class SwingTradeAuthorizationExtensions
{
    /// <summary>
    /// Registers the Read, Write, Admin and Mcp policies. Each requires an authenticated user, the swingtrade audience,
    /// a sub claim, and a role. Admin satisfies Write, Write satisfies Read. Mcp is independent of the REST roles.
    /// </summary>
    public static IServiceCollection AddSwingTradeAuthorization(this IServiceCollection services) =>
        services.AddAuthorizationBuilder()
            .AddPolicy(SwingTradeAuth.Policies.Read, p => Build(p, SwingTradeAuth.Roles.Reader, SwingTradeAuth.Roles.Trader, SwingTradeAuth.Roles.Admin))
            .AddPolicy(SwingTradeAuth.Policies.Write, p => Build(p, SwingTradeAuth.Roles.Trader, SwingTradeAuth.Roles.Admin))
            .AddPolicy(SwingTradeAuth.Policies.Admin, p => Build(p, SwingTradeAuth.Roles.Admin))
            .AddPolicy(SwingTradeAuth.Policies.Mcp, p => Build(p, SwingTradeAuth.Roles.Mcp))
            .Services;

    /// <summary>Apply to JwtBearerOptions: no claim-type remapping, roles read from the "role" claim.</summary>
    public static void ApplySwingTradeTokenValidation(this JwtBearerOptions options)
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters.RoleClaimType = SwingTradeAuth.RoleClaimType;
        options.TokenValidationParameters.NameClaimType = SwingTradeAuth.SubjectClaimType;
        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidAudience = SwingTradeAuth.Audience;
    }

    private static void Build(AuthorizationPolicyBuilder policy, params string[] roles) =>
        policy.RequireAuthenticatedUser()
            .RequireAssertion(ctx => ctx.User.HasClaim(c => c.Type == SwingTradeAuth.AudienceClaimType && c.Value == SwingTradeAuth.Audience))
            .RequireClaim(SwingTradeAuth.SubjectClaimType)
            .RequireAssertion(ctx => ctx.User.Claims.Any(c => c.Type == SwingTradeAuth.RoleClaimType && roles.Contains(c.Value)));
}
