using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Spydersoft.SwingTrade.Api.Mcp;
using Spydersoft.SwingTrade.Api.Refresh;
using Spydersoft.SwingTrade.Api.Security;
using Spydersoft.SwingTrade.Api.Services;
using Spydersoft.SwingTrade.Data;
using Spydersoft.SwingTrade.Data.Entities;
using Spydersoft.SwingTrade.MarketData;

namespace Spydersoft.SwingTrade.Api.UnitTests.Support;

/// <summary>
/// The real MCP pipeline (JWT validation with a test signing key, the Mcp and McpOwner policies, host filter, rate limiter,
/// protected resource metadata, tools, resource, prompts, call audit) on an in-process TestServer over an in-memory database.
/// </summary>
internal sealed class McpTestHost : IAsyncDisposable
{
    public const string Owner = "owner-sub";
    public const string PublicHost = "swing.example.test";
    public const string PublicUrl = "https://swing.example.test/mcp";
    public const string Authority = "https://auth.example.test";
    private const string Issuer = Authority;

    private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("unit-test-signing-key-0123456789-abcdef-0123456789"));

    private readonly WebApplication _app;

    private McpTestHost(WebApplication app, FakeTimeProvider clock)
    {
        _app = app;
        Clock = clock;
    }

    public FakeTimeProvider Clock { get; }
    public IServiceProvider Services => _app.Services;
    public TestServer Server => _app.GetTestServer();

    public static async Task<McpTestHost> StartAsync(Dictionary<string, string?>? config = null, Func<SwingTradeDbContext, Task>? arrange = null, bool seed = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        var settings = new Dictionary<string, string?>
        {
            ["Mcp:PublicUrl"] = PublicUrl,
            ["Mcp:OwnerSubject"] = Owner,
            ["Auth:Authority"] = Authority,
            ["MarketData:Providers:0"] = "fake"
        };
        foreach (var (k, v) in config ?? [])
            settings[k] = v;
        builder.Configuration.AddInMemoryCollection(settings);

        var clock = new FakeTimeProvider();
        var dbName = $"mcp-host-{Guid.NewGuid()}";
        var services = builder.Services;
        services.AddSingleton<TimeProvider>(clock);
        services.AddDbContext<SwingTradeDbContext>(o => o.UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.ApplySwingTradeTokenValidation();
                o.TokenValidationParameters.IssuerSigningKey = Key;
                o.TokenValidationParameters.ValidIssuer = Issuer;
                o.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
            });
        services.AddSwingTradeAuthorization();
        services.AddControllers().AddApplicationPart(typeof(SwingTradeAuth).Assembly)
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        services.AddSwingTradeApplicationServices();
        services.AddScoped<McpAdminService>();
        services.AddMarketData(builder.Configuration);
        services.AddSingleton<MarketCalendar>();
        services.AddSingleton<RefreshSchedule>();
        services.AddSingleton<RefreshCoordinator>();
        services.AddScoped<IngestionService>();
        services.AddScoped<RefreshService>();
        services.AddSwingTradeMcp(builder.Configuration, builder.Environment);

        var app = builder.Build();
        app.UseSwingTradeMcp();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapSwingTradeMcp();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SwingTradeDbContext>();
            if (seed)
                await DatabaseSeeder.SeedAsync(db, clock);
            if (arrange is not null)
                await arrange(db);
        }

        await app.StartAsync();
        return new McpTestHost(app, clock);
    }

    public HttpClient CreateClient(string host = PublicHost)
    {
        var client = Server.CreateClient();
        client.BaseAddress = new Uri($"http://{host}/");
        client.DefaultRequestHeaders.Host = host;
        return client;
    }

    /// <summary>A signed token. Roles become repeated role claims (an array in the JWT payload).</summary>
    public static string Token(string sub = Owner, string[]? roles = null, string audience = SwingTradeAuth.Audience, DateTime? expires = null)
    {
        var claims = new List<Claim> { new("sub", sub) };
        claims.AddRange((roles ?? [SwingTradeAuth.Roles.Mcp]).Select(r => new Claim("role", r)));
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = DateTime.UtcNow.AddMinutes(-5),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public static HttpRequestMessage Rpc(string method, object? @params = null, string? token = null, int id = 1, string path = "/mcp")
    {
        var body = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = @params
        });
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    /// <summary>Sends a JSON-RPC request and returns the JSON-RPC response, whether it came as JSON or as an SSE frame.</summary>
    public static async Task<JsonElement> CallAsync(HttpClient client, string method, object? @params = null, string? token = null)
    {
        var response = await client.SendAsync(Rpc(method, @params, token ?? Token()));
        var text = await response.Content.ReadAsStringAsync();
        Assert.That((int)response.StatusCode, Is.EqualTo(200), text);
        return ParseRpc(text);
    }

    public static JsonElement ParseRpc(string text)
    {
        var json = text.TrimStart().StartsWith('{')
            ? text
            : string.Join('\n', text.Split('\n').Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].Trim()).Where(l => l.Length > 0).Take(1));
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    public static async Task<JsonElement> ToolAsync(HttpClient client, string name, object? arguments = null, string? token = null)
    {
        var rpc = await CallAsync(client, "tools/call", new { name, arguments = arguments ?? new { } }, token);
        return rpc;
    }

    /// <summary>The JSON text of a successful tool result, parsed.</summary>
    public static JsonElement ToolPayload(JsonElement rpc)
    {
        var result = rpc.GetProperty("result");
        Assert.That(result.TryGetProperty("isError", out var isError) && isError.GetBoolean(), Is.False, result.ToString());
        return JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
    }

    public async Task<T> WithDbAsync<T>(Func<SwingTradeDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<SwingTradeDbContext>());
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
