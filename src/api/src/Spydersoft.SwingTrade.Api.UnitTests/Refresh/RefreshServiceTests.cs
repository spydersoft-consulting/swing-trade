using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Spydersoft.SwingTrade.Api.Refresh;
using Spydersoft.SwingTrade.Api.Services;
using Spydersoft.SwingTrade.Api.UnitTests.Support;
using Spydersoft.SwingTrade.Contracts.Refresh;
using Spydersoft.SwingTrade.Data.Entities;

namespace Spydersoft.SwingTrade.Api.UnitTests.Refresh;

[TestFixture]
public class RefreshServiceTests
{
    [Test]
    public async Task RunAsync_RecordsAStartedAndFinishedRun()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY", "QQQ");

        var run = await f.Refresh.RunAsync("manual");

        Assert.Multiple(() =>
        {
            Assert.That(run!.Status, Is.EqualTo(RefreshStatus.Succeeded));
            Assert.That(run.TickersOk, Is.EqualTo(2));
            Assert.That(run.StartedAt, Is.EqualTo(f.Clock.UtcNow));
            Assert.That(run.FinishedAt, Is.Not.Null);
            Assert.That(run.Provider, Is.EqualTo("fake"));
            Assert.That(run.Message, Does.StartWith("[manual]"));
        });
    }

    [Test]
    public async Task AnOverlappingRun_IsRejected_UntilTheFirstFinishes()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY");

        var first = await f.Refresh.TryBeginAsync("scheduled");
        var second = await f.Refresh.TryBeginAsync("manual");
        var manual = await f.Refresh.StartManualAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Null);
            Assert.That(manual.Kind, Is.EqualTo(ResultKind.Conflict));
            Assert.That(f.Coordinator.IsRunning, Is.True);
        });

        await f.Refresh.ExecuteAsync(first!.Id);

        Assert.That(f.Coordinator.IsRunning, Is.False);
        Assert.That(await f.Refresh.TryBeginAsync("manual"), Is.Not.Null);
    }

    [Test]
    public async Task ARunLeftRunningByACrash_IsMarkedAbandoned()
    {
        using var f = new RefreshFixture();
        f.Db.RefreshRuns.Add(new RefreshRun { StartedAt = f.Clock.UtcNow.AddHours(-3), Provider = "fake", Status = RefreshStatus.Running });
        await f.Db.SaveChangesAsync();

        var run = await f.Refresh.TryBeginAsync("scheduled");

        Assert.That(run, Is.Not.Null);
        Assert.That(await f.Db.RefreshRuns.CountAsync(r => r.Status == RefreshStatus.Abandoned), Is.EqualTo(1));
    }

    [Test]
    public async Task ACancelledRun_IsRecordedAndReleasesTheLock()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var started = await f.Refresh.TryBeginAsync("manual");
        var run = await f.Refresh.ExecuteAsync(started!.Id, cts.Token);

        Assert.That(run.Status, Is.EqualTo(RefreshStatus.Cancelled));
        Assert.That(f.Coordinator.IsRunning, Is.False);
    }

    [Test]
    public async Task LatestBarMissing_IsTrueUntilEveryTickerHasTheExpectedBar()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY", "QQQ");
        Assert.That(await f.Refresh.IsLatestBarMissingAsync(), Is.True, "nothing stored yet");

        f.Fake.SetBars("SPY", RefreshFixture.WeekdaysEnding(new DateOnly(2026, 10, 12), 60).Select(d => RefreshFixture.Bar(d, 100m)));
        f.Fake.SetBars("QQQ", RefreshFixture.WeekdaysEnding(new DateOnly(2026, 10, 9), 60).Select(d => RefreshFixture.Bar(d, 100m)));
        await f.Refresh.RunAsync("test");
        f.Db.ChangeTracker.Clear();
        Assert.That(await f.Refresh.IsLatestBarMissingAsync(), Is.True, "QQQ lacks Monday");

        f.Fake.SetBars("QQQ", RefreshFixture.WeekdaysEnding(new DateOnly(2026, 10, 12), 60).Select(d => RefreshFixture.Bar(d, 100m)));
        await f.Refresh.RunAsync("test");
        f.Db.ChangeTracker.Clear();
        Assert.That(await f.Refresh.IsLatestBarMissingAsync(), Is.False);
    }

    [Test]
    public async Task ListAsync_PagesNewestFirst_AndClampsPageSize()
    {
        using var f = new RefreshFixture();
        for (var i = 0; i < 5; i++)
            f.Db.RefreshRuns.Add(new RefreshRun { StartedAt = f.Clock.UtcNow.AddHours(i), Provider = "fake", Status = RefreshStatus.Succeeded, TickersOk = i });
        await f.Db.SaveChangesAsync();

        var page1 = await f.Refresh.ListAsync(new RefreshRunListQuery { Page = 1, PageSize = 2 });
        var page3 = await f.Refresh.ListAsync(new RefreshRunListQuery { Page = 3, PageSize = 2 });
        var huge = await f.Refresh.ListAsync(new RefreshRunListQuery { Page = 0, PageSize = 5000 });

        Assert.Multiple(() =>
        {
            Assert.That(page1.TotalCount, Is.EqualTo(5));
            Assert.That(page1.Items.Select(r => r.TickersOk), Is.EqualTo(new[] { 4, 3 }));
            Assert.That(page3.Items.Select(r => r.TickersOk), Is.EqualTo(new[] { 0 }));
            Assert.That(huge.PageSize, Is.EqualTo(100));
            Assert.That(huge.Page, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetAsync_ReturnsTheRunOrNotFound()
    {
        using var f = new RefreshFixture();
        var run = await f.Refresh.RunAsync("test");

        Assert.That((await f.Refresh.GetAsync(run!.Id)).IsOk, Is.True);
        Assert.That((await f.Refresh.GetAsync(999)).Kind, Is.EqualTo(ResultKind.NotFound));
    }

    [Test]
    public async Task HostedService_CatchUp_RunsOnlyWhenTheLatestBarIsMissing()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY");
        f.SetClock(2026, 10, 13, 12); // Tuesday 08:00 New York; Monday's close is the expected bar
        var host = new RefreshHostedService(f.ScopeFactory, new RefreshSchedule(f.Clock), f.Clock, NullLogger<RefreshHostedService>.Instance);
        var catchUp = new ScheduledRun(f.Clock.UtcNow, RefreshAttempt.CatchUp);

        await host.RunAttemptAsync(catchUp, default);
        var afterFirst = await f.Db.RefreshRuns.CountAsync();
        f.Db.ChangeTracker.Clear();

        // The fake generated Monday's bar, so a second catch-up has nothing to do.
        await host.RunAttemptAsync(catchUp, default);

        Assert.That(afterFirst, Is.EqualTo(1));
        Assert.That(await f.Db.RefreshRuns.CountAsync(), Is.EqualTo(1));
        Assert.That((await f.Db.RefreshRuns.SingleAsync()).Message, Does.StartWith("[scheduled-catchup]"));
    }

    [Test]
    public async Task HostedService_SkipsAScheduledRunWhileAnotherIsRunning()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY");
        var host = new RefreshHostedService(f.ScopeFactory, new RefreshSchedule(f.Clock), f.Clock, NullLogger<RefreshHostedService>.Instance);
        var manual = await f.Refresh.TryBeginAsync("manual");

        await host.RunAttemptAsync(new ScheduledRun(f.Clock.UtcNow, RefreshAttempt.Primary), default);

        Assert.That(await f.Db.RefreshRuns.CountAsync(), Is.EqualTo(1), "only the manual row exists");
        Assert.That(f.Coordinator.IsRunning, Is.True);
        await f.Refresh.ExecuteAsync(manual!.Id);
    }
}
