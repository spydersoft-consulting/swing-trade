using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Spydersoft.SwingTrade.Api.Controllers;
using Spydersoft.SwingTrade.Api.Security;

namespace Spydersoft.SwingTrade.Api.UnitTests.Controllers;

public class AuthorizationTests
{
    private static readonly string[] KnownPolicies =
        [SwingTradeAuth.Policies.Read, SwingTradeAuth.Policies.Write, SwingTradeAuth.Policies.Admin, SwingTradeAuth.Policies.Mcp];

    private static ClaimsPrincipal User(string[] roles, string? aud = SwingTradeAuth.Audience, string? sub = "owner-1", bool authenticated = true)
    {
        var claims = new List<Claim>();
        if (aud is not null) claims.Add(new Claim("aud", aud));
        if (sub is not null) claims.Add(new Claim("sub", sub));
        claims.AddRange(roles.Select(r => new Claim("role", r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "test" : null, "sub", "role"));
    }

    private static async Task<bool> Allowed(ClaimsPrincipal user, string policy)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSwingTradeAuthorization();
        await using var sp = services.BuildServiceProvider();
        var result = await sp.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, policy);
        return result.Succeeded;
    }

    // Columns: roles, Read, Write, Admin, Mcp
    [TestCase(new string[0], false, false, false, false)]
    [TestCase(new[] { "swingtrade.reader" }, true, false, false, false)]
    [TestCase(new[] { "swingtrade.trader" }, true, true, false, false)]
    [TestCase(new[] { "swingtrade.admin" }, true, true, true, false)]
    [TestCase(new[] { "swingtrade.mcp" }, false, false, false, true)]
    [TestCase(new[] { "swingtrade.reader", "swingtrade.mcp" }, true, false, false, true)]
    [TestCase(new[] { "authentication.admin" }, false, false, false, false)]
    public async Task RoleMatrix(string[] roles, bool read, bool write, bool admin, bool mcp)
    {
        var user = User(roles);
        var actual = (
            await Allowed(user, SwingTradeAuth.Policies.Read),
            await Allowed(user, SwingTradeAuth.Policies.Write),
            await Allowed(user, SwingTradeAuth.Policies.Admin),
            await Allowed(user, SwingTradeAuth.Policies.Mcp));
        Assert.That(actual, Is.EqualTo((read, write, admin, mcp)));
    }

    [TestCase(SwingTradeAuth.Policies.Read)]
    [TestCase(SwingTradeAuth.Policies.Write)]
    [TestCase(SwingTradeAuth.Policies.Admin)]
    [TestCase(SwingTradeAuth.Policies.Mcp)]
    public async Task WrongAudience_MissingAudience_MissingSub_Unauthenticated_AreDenied(string policy)
    {
        string[] all = [SwingTradeAuth.Roles.Admin, SwingTradeAuth.Roles.Mcp];
        Assert.Multiple(async () =>
        {
            Assert.That(await Allowed(User(all, aud: "other"), policy), Is.False, "wrong audience");
            Assert.That(await Allowed(User(all, aud: null), policy), Is.False, "no audience");
            Assert.That(await Allowed(User(all, sub: null), policy), Is.False, "no sub");
            Assert.That(await Allowed(User(all, authenticated: false), policy), Is.False, "unauthenticated");
        });
    }

    private static string PolicyOf<TController>(string action) =>
        typeof(TController).GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>()!.Policy!;

    // Columns: roles, POST /refresh allowed, GET /refresh/runs allowed
    [TestCase(new string[0], false, false)]
    [TestCase(new[] { "swingtrade.reader" }, false, true)]
    [TestCase(new[] { "swingtrade.trader" }, false, true)]
    [TestCase(new[] { "swingtrade.admin" }, true, true)]
    [TestCase(new[] { "swingtrade.mcp" }, false, false)]
    public async Task RefreshEndpoints_RoleMatrix(string[] roles, bool canTrigger, bool canListRuns)
    {
        var user = User(roles);
        Assert.Multiple(async () =>
        {
            Assert.That(await Allowed(user, PolicyOf<RefreshController>(nameof(RefreshController.Start))), Is.EqualTo(canTrigger), "POST /refresh");
            Assert.That(await Allowed(user, PolicyOf<RefreshController>(nameof(RefreshController.GetRuns))), Is.EqualTo(canListRuns), "GET /refresh/runs");
            Assert.That(await Allowed(user, PolicyOf<RefreshController>(nameof(RefreshController.GetRun))), Is.EqualTo(canListRuns), "GET /refresh/runs/{id}");
        });
    }

    [Test]
    public async Task RoleArray_ExpandedToMultipleClaims_IsHonoured()
    {
        var user = User([SwingTradeAuth.Roles.Reader, SwingTradeAuth.Roles.Trader]);
        Assert.That(await Allowed(user, SwingTradeAuth.Policies.Write), Is.True);
    }

    private static IEnumerable<MethodInfo> ControllerActions() =>
        typeof(ApiControllerBase).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => !m.IsSpecialName && !m.IsDefined(typeof(NonActionAttribute), true));

    [Test]
    public void EveryControllerAction_HasAKnownAuthorizePolicy_AndNoneAllowsAnonymous()
    {
        var actions = ControllerActions().ToList();
        Assert.That(actions, Is.Not.Empty);

        var offenders = actions.Where(m =>
        {
            if (m.IsDefined(typeof(AllowAnonymousAttribute), true) || m.DeclaringType!.IsDefined(typeof(AllowAnonymousAttribute), true))
                return true;
            var policy = m.GetCustomAttributes<AuthorizeAttribute>(true).Concat(m.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>(true))
                .Select(a => a.Policy).FirstOrDefault(p => !string.IsNullOrEmpty(p));
            return policy is null || !KnownPolicies.Contains(policy);
        }).Select(m => $"{m.DeclaringType!.Name}.{m.Name}").ToList();

        Assert.That(offenders, Is.Empty, "Actions without a known [Authorize(Policy = ...)]: " + string.Join(", ", offenders));
    }

    [Test]
    public void EndpointMapping_MatchesTheRoleTable()
    {
        static string? PolicyOf(Type controller, string action, HttpMethod verb)
        {
            var m = controller.GetMethods().Single(x => x.Name == action);
            return m.GetCustomAttribute<AuthorizeAttribute>()?.Policy;
        }

        var expected = new (Type Controller, string Action, string Policy)[]
        {
            (typeof(TradesController), "GetAll", SwingTradeAuth.Policies.Read),
            (typeof(TradesController), "GetById", SwingTradeAuth.Policies.Read),
            (typeof(TradesController), "Create", SwingTradeAuth.Policies.Write),
            (typeof(TradesController), "Update", SwingTradeAuth.Policies.Write),
            (typeof(TradesController), "Delete", SwingTradeAuth.Policies.Write),
            (typeof(TradesController), "Fill", SwingTradeAuth.Policies.Write),
            (typeof(TradesController), "Close", SwingTradeAuth.Policies.Write),
            (typeof(TradesController), "Cancel", SwingTradeAuth.Policies.Write),
            (typeof(DashboardController), "Get", SwingTradeAuth.Policies.Read),
            (typeof(SettingsController), "Get", SwingTradeAuth.Policies.Read),
            (typeof(SettingsController), "Update", SwingTradeAuth.Policies.Admin),
            (typeof(RefreshController), "Start", SwingTradeAuth.Policies.Admin),
            (typeof(RefreshController), "GetRuns", SwingTradeAuth.Policies.Read),
            (typeof(RefreshController), "GetRun", SwingTradeAuth.Policies.Read),
        };
        foreach (var (controller, action, policy) in expected)
            Assert.That(PolicyOf(controller, action, HttpMethod.Get), Is.EqualTo(policy), $"{controller.Name}.{action}");

        // Overloaded-name controllers: check by HTTP verb instead.
        foreach (var (controller, verb, policy) in new (Type, Type, string)[]
        {
            (typeof(NotesController), typeof(HttpGetAttribute), SwingTradeAuth.Policies.Read),
            (typeof(NotesController), typeof(HttpPostAttribute), SwingTradeAuth.Policies.Write),
            (typeof(InstrumentsController), typeof(HttpGetAttribute), SwingTradeAuth.Policies.Read),
            (typeof(InstrumentsController), typeof(HttpPostAttribute), SwingTradeAuth.Policies.Write),
            (typeof(InstrumentsController), typeof(HttpPutAttribute), SwingTradeAuth.Policies.Write),
        })
        {
            var methods = controller.GetMethods().Where(m => m.IsDefined(verb, false)).ToList();
            Assert.That(methods, Is.Not.Empty, $"{controller.Name} {verb.Name}");
            Assert.That(methods.Select(m => m.GetCustomAttribute<AuthorizeAttribute>()!.Policy), Has.All.EqualTo(policy), $"{controller.Name} {verb.Name}");
        }
    }
}
