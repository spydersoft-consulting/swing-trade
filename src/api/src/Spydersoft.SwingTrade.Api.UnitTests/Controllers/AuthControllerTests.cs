using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Spydersoft.SwingTrade.Api.Controllers;
using Spydersoft.SwingTrade.Contracts.Auth;

namespace Spydersoft.SwingTrade.Api.UnitTests.Controllers;

public class AuthControllerTests
{
    private static AuthInfoDto Info(params string[] roles)
    {
        var claims = roles.Select(r => new Claim(SwingTradeAuth.RoleClaimType, r))
            .Append(new Claim(SwingTradeAuth.SubjectClaimType, "user-1"));
        var controller = new AuthController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
            }
        };
        var result = controller.GetInfo();
        return (AuthInfoDto)((OkObjectResult)result.Result!).Value!;
    }

    [TestCase(new[] { "swingtrade.reader" }, true, false, false)]
    [TestCase(new[] { "swingtrade.trader" }, true, true, false)]
    [TestCase(new[] { "swingtrade.admin" }, true, true, true)]
    [TestCase(new[] { "swingtrade.reader", "swingtrade.admin" }, true, true, true)]
    public void Flags_FollowTheRoleHierarchy(string[] roles, bool read, bool write, bool admin)
    {
        var info = Info(roles);

        Assert.That((info.CanRead, info.CanWrite, info.CanAdmin), Is.EqualTo((read, write, admin)));
        Assert.That(info.Subject, Is.EqualTo("user-1"));
    }

    [Test]
    public void Roles_AreOnlySwingTradeRoles_AndTheNamesAreExposed()
    {
        var info = Info("swingtrade.reader", "authentication.admin", "swingtrade.reader");

        Assert.That(info.Roles, Is.EqualTo(new[] { "swingtrade.reader" }));
        Assert.That(info.Audience, Is.EqualTo(SwingTradeAuth.Audience));
        Assert.That(info.AdminRole, Is.EqualTo(SwingTradeAuth.Roles.Admin));
        Assert.That(info.WritePolicy, Is.EqualTo(SwingTradeAuth.Policies.Write));
    }
}
