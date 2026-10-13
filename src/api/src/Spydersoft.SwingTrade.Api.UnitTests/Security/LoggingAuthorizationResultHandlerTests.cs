using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spydersoft.SwingTrade.Api.Security;

namespace Spydersoft.SwingTrade.Api.UnitTests.Security;

/// <summary>DECISIONS #35 / #103: refusals are logged with sub, path and required policy, and never with a token.</summary>
[TestFixture]
public class LoggingAuthorizationResultHandlerTests
{
    private const string Token = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJvd25lciJ9.c2lnbmF0dXJl";

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    /// <summary>Answers challenge with 401 and forbid with 403 without any real authentication, like the JWT bearer scheme does.</summary>
    private sealed class StubAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
    }

    private static readonly IServiceProvider Services = new ServiceCollection()
        .AddLogging()
        .AddAuthentication("stub").AddScheme<AuthenticationSchemeOptions, StubAuthHandler>("stub", null).Services
        .BuildServiceProvider();

    private static readonly AuthorizationPolicy AnyPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

    private static DefaultHttpContext Context(string path, string? sub, string policy)
    {
        var http = new DefaultHttpContext { RequestServices = Services.CreateScope().ServiceProvider };
        http.Request.Path = path;
        http.Request.Headers.Authorization = "Bearer " + Token;
        http.Response.Body = new MemoryStream();
        var claims = sub is null ? [] : new[] { new Claim("sub", sub) };
        http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, sub is null ? null : "test"));
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new AuthorizeAttribute(policy)), "test"));
        return http;
    }

    [Test]
    public async Task AForbiddenRequest_IsLoggedAsAWarning_WithSubPathAndPolicy_AndStill403()
    {
        var logger = new CapturingLogger<LoggingAuthorizationResultHandler>();
        var handler = new LoggingAuthorizationResultHandler(logger);
        var http = Context("/api/v1/settings", "reader-sub", SwingTradeAuth.Policies.Admin);
        var nextCalled = false;

        await handler.HandleAsync(_ => { nextCalled = true; return Task.CompletedTask; }, http, AnyPolicy, PolicyAuthorizationResult.Forbid());

        var entry = logger.Entries.Single();
        Assert.Multiple(() =>
        {
            Assert.That(http.Response.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
            Assert.That(nextCalled, Is.False);
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Message, Does.Contain("reader-sub").And.Contain("/api/v1/settings").And.Contain(SwingTradeAuth.Policies.Admin));
            Assert.That(entry.Message, Does.Not.Contain(Token).And.Not.Contain("Bearer"));
        });
    }

    [Test]
    public async Task AChallengedRequest_IsLoggedAsInformation_WithoutAToken()
    {
        var logger = new CapturingLogger<LoggingAuthorizationResultHandler>();
        var handler = new LoggingAuthorizationResultHandler(logger);
        var http = Context("/api/v1/trades", null, SwingTradeAuth.Policies.Read);

        await handler.HandleAsync(_ => Task.CompletedTask, http, AnyPolicy, PolicyAuthorizationResult.Challenge());

        var entry = logger.Entries.Single();
        Assert.Multiple(() =>
        {
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Information));
            Assert.That(entry.Message, Does.Contain("/api/v1/trades").And.Contain(SwingTradeAuth.Policies.Read));
            Assert.That(entry.Message, Does.Not.Contain(Token));
        });
    }

    [Test]
    public async Task ASuccessfulRequest_LogsNothing_AndContinuesThePipeline()
    {
        var logger = new CapturingLogger<LoggingAuthorizationResultHandler>();
        var handler = new LoggingAuthorizationResultHandler(logger);
        var http = Context("/api/v1/trades", "owner", SwingTradeAuth.Policies.Read);
        var nextCalled = false;

        await handler.HandleAsync(_ => { nextCalled = true; return Task.CompletedTask; }, http, AnyPolicy, PolicyAuthorizationResult.Success());

        Assert.Multiple(() =>
        {
            Assert.That(logger.Entries, Is.Empty);
            Assert.That(nextCalled, Is.True);
        });
    }

    [Test]
    public void TheHandlerIsRegisteredByAddSwingTradeAuthorization()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSwingTradeAuthorization();
        using var provider = services.BuildServiceProvider();

        Assert.That(provider.GetService<IAuthorizationMiddlewareResultHandler>(), Is.TypeOf<LoggingAuthorizationResultHandler>());
    }
}
