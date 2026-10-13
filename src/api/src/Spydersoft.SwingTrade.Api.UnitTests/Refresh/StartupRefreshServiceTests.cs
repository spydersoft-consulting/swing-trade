using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Spydersoft.SwingTrade.Api.Refresh;
using Spydersoft.SwingTrade.Api.UnitTests.Support;
using Spydersoft.SwingTrade.Data.Entities;

namespace Spydersoft.SwingTrade.Api.UnitTests.Refresh;

/// <summary>DECISIONS #102: the opt-in first-run load runs once, and only when no price bars exist.</summary>
[TestFixture]
public class StartupRefreshServiceTests
{
    private static StartupRefreshService Service(RefreshFixture f) =>
        new(f.ScopeFactory, NullLogger<StartupRefreshService>.Instance);

    [Test]
    public async Task WithNoBars_ItRunsOneStartupRefresh_AndSnapshotsExist()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY", "QQQ");

        await Service(f).RunIfEmptyAsync(CancellationToken.None);

        var runs = await f.Db.RefreshRuns.ToListAsync();
        Assert.That(runs, Has.Count.EqualTo(1));
        Assert.That(runs[0].Message, Does.StartWith("[startup]"));
        Assert.That(runs[0].Status, Is.EqualTo(RefreshStatus.Succeeded));
        Assert.That(await f.Db.PriceBars.AnyAsync(), Is.True);
        Assert.That(await f.Db.IndicatorSnapshots.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task WhenBarsAlreadyExist_ItDoesNothing()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY");
        f.Db.PriceBars.Add(new PriceBar { Ticker = "SPY", Date = new DateOnly(2026, 10, 9), Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 });
        await f.Db.SaveChangesAsync();

        await Service(f).RunIfEmptyAsync(CancellationToken.None);

        Assert.That(await f.Db.RefreshRuns.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task WhenAnotherRefreshIsRunning_ItSkipsWithoutThrowing()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY");
        await f.Refresh.TryBeginAsync("manual");

        Assert.DoesNotThrowAsync(() => Service(f).RunIfEmptyAsync(CancellationToken.None));
        Assert.That(await f.Db.RefreshRuns.CountAsync(), Is.EqualTo(1), "only the manual run exists");
    }

    [Test]
    public void TheShippedConfiguration_IsOffByDefault_AndOnlyDevelopmentTurnsItOn()
    {
        static JsonElement Refresh(string file) =>
            JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file))).RootElement.GetProperty("Refresh");

        Assert.That(Refresh("appsettings.json").GetProperty("RunOnStartupWhenEmpty").GetBoolean(), Is.False, "DECISIONS #46: production never refreshes at start");
        Assert.That(Refresh("appsettings.Development.json").GetProperty("RunOnStartupWhenEmpty").GetBoolean(), Is.True);
        Assert.That(Refresh("appsettings.Testing.json").TryGetProperty("RunOnStartupWhenEmpty", out _), Is.False);
    }
}
