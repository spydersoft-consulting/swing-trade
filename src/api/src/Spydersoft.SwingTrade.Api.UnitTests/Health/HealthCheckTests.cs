using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spydersoft.Platform.Hosting.StartupExtensions;
using Spydersoft.SwingTrade.Api.Health;
using Spydersoft.SwingTrade.Api.Refresh;
using Spydersoft.SwingTrade.Api.UnitTests.Support;
using Spydersoft.SwingTrade.Data;
using Spydersoft.SwingTrade.Data.Entities;

namespace Spydersoft.SwingTrade.Api.UnitTests.Health;

/// <summary>
/// /livez and /readyz as the API wires them: the real Spydersoft health-check endpoints, the shipped appsettings.json tags,
/// and the two readiness checks. Postgres being down is simulated with an Npgsql context pointed at a closed port.
/// </summary>
[TestFixture]
public class HealthCheckTests
{
    // The default FakeTimeProvider clock is Monday 2026-10-12 17:00 New York, so the latest expected bar is that Monday.
    private static readonly DateOnly LatestExpected = new(2026, 10, 12);

    private static async Task<WebApplication> StartAsync(bool databaseDown, Func<SwingTradeDbContext, Task>? arrange = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        // The configuration the API ships with, so a change to the HealthCheck tags is caught here.
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false);

        var healthCheckOptions = builder.AddSpydersoftHealthChecks();
        builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        builder.Services.AddSingleton<MarketCalendar>();
        if (databaseDown)
        {
            builder.Services.AddDbContext<SwingTradeDbContext>(o => o.UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1;Command Timeout=1"));
        }
        else
        {
            var dbName = $"health-{Guid.NewGuid()}";
            builder.Services.AddDbContext<SwingTradeDbContext>(o => o.UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        }

        builder.Services.AddSwingTradeHealthChecks();

        var app = builder.Build();
        if (arrange is not null)
        {
            using var scope = app.Services.CreateScope();
            await arrange(scope.ServiceProvider.GetRequiredService<SwingTradeDbContext>());
        }

        app.UseSpydersoftHealthChecks(healthCheckOptions);
        await app.StartAsync();
        return app;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(WebApplication app, string path)
    {
        var response = await app.GetTestClient().GetAsync(path);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    private static Task CurrentSnapshot(SwingTradeDbContext db) => AddSnapshot(db, LatestExpected);

    private static async Task AddSnapshot(SwingTradeDbContext db, DateOnly asOf)
    {
        db.IndicatorSnapshots.Add(new IndicatorSnapshot { Ticker = "SPY", AsOfDate = asOf, LastClose = 100m, Ma20 = 100m, Ma50 = 100m, BarsUsed = 50 });
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task Livez_IsHealthy_AndRunsNoDependencyCheck_EvenWhenTheDatabaseIsDown()
    {
        await using var app = await StartAsync(databaseDown: true);

        var (status, body) = await GetAsync(app, "/livez");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.GetProperty("status").GetString(), Is.EqualTo("Healthy"));
            Assert.That(body.GetProperty("results").TryGetProperty("database", out _), Is.False, "liveness must not depend on Postgres");
            Assert.That(body.GetProperty("results").TryGetProperty("market-data", out _), Is.False);
        });
    }

    [Test]
    public async Task Readyz_IncludesTheDatabase_AndIsHealthyWhenPricesAreCurrent()
    {
        await using var app = await StartAsync(databaseDown: false, CurrentSnapshot);

        var (status, body) = await GetAsync(app, "/readyz");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.GetProperty("status").GetString(), Is.EqualTo("Healthy"));
            Assert.That(body.GetProperty("results").TryGetProperty("database", out var database), Is.True);
            Assert.That(database.GetProperty("status").GetString(), Is.EqualTo("Healthy"));
            Assert.That(body.GetProperty("results").GetProperty("market-data").GetProperty("status").GetString(), Is.EqualTo("Healthy"));
        });
    }

    [Test]
    public async Task Readyz_IsUnhealthyWith503_WhenTheDatabaseIsUnreachable()
    {
        await using var app = await StartAsync(databaseDown: true);

        var (status, body) = await GetAsync(app, "/readyz");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(body.GetProperty("status").GetString(), Is.EqualTo("Unhealthy"));
            Assert.That(body.GetProperty("results").GetProperty("database").GetProperty("status").GetString(), Is.EqualTo("Unhealthy"));
        });
    }

    [Test]
    public async Task Readyz_IsDegradedButStillServing_WhenThePricesAreStale()
    {
        await using var app = await StartAsync(databaseDown: false, db => AddSnapshot(db, LatestExpected.AddDays(-3)));

        var (status, body) = await GetAsync(app, "/readyz");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), "Degraded must not take the pod out of rotation");
            Assert.That(body.GetProperty("status").GetString(), Is.EqualTo("Degraded"));
            Assert.That(body.GetProperty("results").GetProperty("database").GetProperty("status").GetString(), Is.EqualTo("Healthy"));
            Assert.That(body.GetProperty("results").GetProperty("market-data").GetProperty("description").GetString(), Does.Contain("stale"));
        });
    }

    [Test]
    public async Task Readyz_IsDegraded_WhenNoPricesHaveBeenLoadedYet()
    {
        await using var app = await StartAsync(databaseDown: false);

        var (status, body) = await GetAsync(app, "/readyz");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.GetProperty("status").GetString(), Is.EqualTo("Degraded"));
            Assert.That(body.GetProperty("results").GetProperty("market-data").GetProperty("description").GetString(), Does.Contain("refresh"));
        });
    }
}
