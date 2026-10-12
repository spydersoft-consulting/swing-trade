using Microsoft.AspNetCore.Mvc;
using Spydersoft.SwingTrade.Api.Controllers;
using Spydersoft.SwingTrade.Api.Refresh;
using Spydersoft.SwingTrade.Api.UnitTests.Support;
using Spydersoft.SwingTrade.Contracts.Common;
using Spydersoft.SwingTrade.Contracts.Refresh;

namespace Spydersoft.SwingTrade.Api.UnitTests.Controllers;

[TestFixture]
public class RefreshControllerTests
{
    private sealed class RecordingLauncher : IRefreshLauncher
    {
        public List<int> Launched { get; } = [];
        public void Launch(int runId) => Launched.Add(runId);
    }

    [Test]
    public async Task Post_Returns202WithTheRunId_AndLaunchesTheBackgroundRun()
    {
        using var f = new RefreshFixture();
        var launcher = new RecordingLauncher();
        var controller = new RefreshController(f.Refresh, launcher);

        var result = await controller.Start(default);

        var accepted = (AcceptedResult)result.Result!;
        var dto = (RefreshRunDto)accepted.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(accepted.StatusCode, Is.EqualTo(202));
            Assert.That(dto.Status, Is.EqualTo(RefreshStatus.Running));
            Assert.That(accepted.Location, Is.EqualTo($"/api/v1/refresh/runs/{dto.Id}"));
            Assert.That(launcher.Launched, Is.EqualTo(new[] { dto.Id }));
        });
    }

    [Test]
    public async Task Post_WhileARunIsInProgress_Returns409AndLaunchesNothing()
    {
        using var f = new RefreshFixture();
        var launcher = new RecordingLauncher();
        var controller = new RefreshController(f.Refresh, launcher);
        await controller.Start(default);

        var second = await controller.Start(default);

        var problem = (ObjectResult)second.Result!;
        Assert.That(problem.StatusCode, Is.EqualTo(409));
        Assert.That(launcher.Launched, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetRuns_ReturnsAPagedList_AndGetRunReturns404ForUnknownIds()
    {
        using var f = new RefreshFixture();
        await f.AddInstrumentsAsync("SPY");
        var run = await f.Refresh.RunAsync("test");
        var controller = new RefreshController(f.Refresh, new RecordingLauncher());

        var list = (PagedResult<RefreshRunDto>)((OkObjectResult)(await controller.GetRuns(new RefreshRunListQuery(), default)).Result!).Value!;
        var found = await controller.GetRun(run!.Id, default);
        var missing = await controller.GetRun(12345, default);

        Assert.Multiple(() =>
        {
            Assert.That(list.Items.Single().Id, Is.EqualTo(run.Id));
            Assert.That(((OkObjectResult)found.Result!).StatusCode, Is.EqualTo(200));
            Assert.That(missing.Result, Is.TypeOf<NotFoundResult>());
        });
    }
}
