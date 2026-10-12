using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Spydersoft.SwingTrade.Api.Controllers;
using Spydersoft.SwingTrade.Api.UnitTests.Support;
using Spydersoft.SwingTrade.Contracts.Bars;
using Spydersoft.SwingTrade.Contracts.Sizer;
using Spydersoft.SwingTrade.Contracts.Watchlist;

namespace Spydersoft.SwingTrade.Api.UnitTests.Controllers;

[TestFixture]
public class M4ControllerTests
{
    private TestHarness _ctx = null!;

    [SetUp]
    public async Task SetUp() => _ctx = await TestHarness.SeededAsync();

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    private static T Value<T>(ActionResult<T> result) => (T)((ObjectResult)result.Result!).Value!;

    private static int? Status<T>(ActionResult<T> result) => (result.Result as ObjectResult)?.StatusCode;

    [Test]
    public async Task Watchlist_ReturnsRows_And400ForABadFilter()
    {
        var controller = new WatchlistController(_ctx.Watchlist);

        var ok = await controller.Get(new WatchlistQuery(), CancellationToken.None);
        var bad = await controller.Get(new WatchlistQuery { Signal = "nonsense" }, CancellationToken.None);

        Assert.That(Value(ok), Has.Count.EqualTo(12));
        Assert.That(Status(bad), Is.EqualTo(StatusCodes.Status400BadRequest));
    }

    [Test]
    public async Task Bars_ReturnsOk_NotFound_And400()
    {
        var controller = new InstrumentsController(_ctx.Instruments, _ctx.Bars);

        var ok = await controller.GetBars("spy", new BarsQuery(), CancellationToken.None);
        var missing = await controller.GetBars("NOPE", new BarsQuery(), CancellationToken.None);
        var bad = await controller.GetBars("SPY", new BarsQuery { Days = 0 }, CancellationToken.None);

        Assert.That(Value(ok).Ticker, Is.EqualTo("SPY"));
        Assert.That(missing.Result, Is.TypeOf<NotFoundResult>());
        Assert.That(Status(bad), Is.EqualTo(StatusCodes.Status400BadRequest));
    }

    [Test]
    public async Task Sizer_ReturnsTheXleFixture_And400ForMissingPrices()
    {
        var controller = new SizerController(_ctx.Sizer);

        var ok = await controller.Size(new SizerRequest { Entry = 62.82m, Stop = 61.40m, Target = 65.90m }, CancellationToken.None);
        var bad = await controller.Size(new SizerRequest(), CancellationToken.None);

        Assert.That(Value(ok).Shares, Is.EqualTo(15));
        Assert.That(Status(bad), Is.EqualTo(StatusCodes.Status400BadRequest));
    }

    [Test]
    public async Task Export_ReturnsACsvFileResult()
    {
        var controller = new ExportController(_ctx.Export);

        var result = await controller.TradesCsv(CancellationToken.None);

        var file = (FileContentResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(file.ContentType, Is.EqualTo("text/csv; charset=utf-8"));
            Assert.That(file.FileDownloadName, Is.EqualTo("swing-trade-trades-20261012.csv"));
            Assert.That(file.FileContents[0], Is.Not.EqualTo(0xEF), "no byte order mark");
        });
    }
}
