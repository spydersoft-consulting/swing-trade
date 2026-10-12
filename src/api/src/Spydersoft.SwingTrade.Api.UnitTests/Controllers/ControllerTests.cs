using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Spydersoft.SwingTrade.Api.Controllers;
using Spydersoft.SwingTrade.Api.UnitTests.Support;
using Spydersoft.SwingTrade.Contracts.Dashboard;
using Spydersoft.SwingTrade.Contracts.Instruments;
using Spydersoft.SwingTrade.Contracts.Notes;
using Spydersoft.SwingTrade.Contracts.Settings;
using Spydersoft.SwingTrade.Contracts.Trades;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Api.UnitTests.Controllers;

[TestFixture]
public class ControllerTests
{
    private TestHarness _ctx = null!;
    private TradesController _trades = null!;

    [SetUp]
    public async Task SetUp()
    {
        _ctx = await TestHarness.SeededAsync();
        _trades = new TradesController(_ctx.Trades) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    private static CreateTradeRequest Plan() => new()
    {
        Ticker = "XLK", Shares = 5, EntryPrice = 192m, StopPrice = 187.5m, TargetPrice = 202m
    };

    private static T Value<T>(ActionResult<T> result) =>
        (T)((ObjectResult)result.Result!).Value!;

    [Test]
    public async Task Trades_GetAll_FiltersByStatus()
    {
        var result = await _trades.GetAll(new TradeListQuery { Status = TradeStatus.Closed }, CancellationToken.None);

        Assert.That(Value(result).Single().Ticker, Is.EqualTo("XLE"));
    }

    [Test]
    public async Task Trades_Create_ReturnsCreatedAtAction()
    {
        var result = await _trades.Create(Plan(), CancellationToken.None);

        var created = result.Result as CreatedAtActionResult;
        Assert.That(created, Is.Not.Null);
        Assert.That(created!.StatusCode, Is.EqualTo(StatusCodes.Status201Created));
        Assert.That(((TradeDto)created.Value!).Status, Is.EqualTo(TradeStatus.Planned));
    }

    [Test]
    public async Task Trades_Create_Invalid_ReturnsValidationProblemWithFieldErrors()
    {
        var req = Plan();
        req.Shares = 0;
        req.StopPrice = 200m;

        var result = await _trades.Create(req, CancellationToken.None);

        var obj = result.Result as ObjectResult;
        Assert.That(obj!.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        var problem = (ValidationProblemDetails)obj.Value!;
        Assert.That(problem.Errors.Keys, Is.SupersetOf(new[] { "Shares", "EntryPrice" }));
    }

    [Test]
    public async Task Trades_GetById_MissingIsNotFound()
    {
        var result = await _trades.GetById(404, CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<NotFoundResult>());
    }

    [Test]
    public async Task Trades_FillCloseCancel_FollowTheStateMachine()
    {
        var id = ((TradeDto)((CreatedAtActionResult)(await _trades.Create(Plan(), CancellationToken.None)).Result!).Value!).Id;

        var fill = await _trades.Fill(id, new FillTradeRequest { FillPrice = 192m, FillDate = new DateOnly(2026, 10, 13) }, CancellationToken.None);
        var cancelAfterFill = await _trades.Cancel(id, CancellationToken.None);
        var close = await _trades.Close(id, new CloseTradeRequest { ExitPrice = 200m, ExitDate = new DateOnly(2026, 10, 20) }, CancellationToken.None);
        var fillAgain = await _trades.Fill(id, new FillTradeRequest { FillPrice = 192m, FillDate = new DateOnly(2026, 10, 21) }, CancellationToken.None);

        Assert.That(Value(fill).Status, Is.EqualTo(TradeStatus.Open));
        Assert.That(((ObjectResult)cancelAfterFill.Result!).StatusCode, Is.EqualTo(StatusCodes.Status409Conflict));
        Assert.That(Value(close).Status, Is.EqualTo(TradeStatus.Closed));
        Assert.That(Value(close).Pnl, Is.EqualTo(40m));
        var conflict = (ObjectResult)fillAgain.Result!;
        Assert.That(conflict.StatusCode, Is.EqualTo(StatusCodes.Status409Conflict));
        Assert.That(conflict.Value, Is.InstanceOf<ProblemDetails>());
    }

    [Test]
    public async Task Trades_Delete_RemovesTheTrade()
    {
        var seeded = _ctx.Db.Trades.Single().Id;

        Assert.That(await _trades.Delete(seeded, CancellationToken.None), Is.TypeOf<NoContentResult>());
        Assert.That(await _trades.Delete(seeded, CancellationToken.None), Is.TypeOf<NotFoundResult>());
    }

    [Test]
    public async Task Settings_GetAndPut()
    {
        var controller = new SettingsController(_ctx.Settings);

        var get = await controller.Get(CancellationToken.None);
        var put = await controller.Update(new UpdateSettingsRequest
        {
            StartingCapital = 2500m, MaxRiskPct = 0.015m, MaxOpenPositions = 3, MinRewardRisk = 2m, TaxSetAsideRate = 0.27m,
            WashSaleWindowDays = 30, MaxPositionPct = 0.5m, PullbackBandLow = -0.01m, PullbackBandHigh = 0.02m,
            ExtendedThreshold = 0.05m
        }, CancellationToken.None);
        var bad = await controller.Update(new UpdateSettingsRequest(), CancellationToken.None);

        Assert.That(Value(get).StartingCapital, Is.EqualTo(2000m));
        Assert.That(Value(put).StartingCapital, Is.EqualTo(2500m));
        Assert.That(((ObjectResult)bad.Result!).StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
    }

    [Test]
    public async Task Instruments_ListCreateUpdate()
    {
        var controller = new InstrumentsController(_ctx.Instruments);

        var active = await controller.GetAll(true, CancellationToken.None);
        var created = await controller.Create(new CreateInstrumentRequest { Ticker = "VTI", Name = "Total Market" }, CancellationToken.None);
        var duplicate = await controller.Create(new CreateInstrumentRequest { Ticker = "VTI", Name = "Total Market" }, CancellationToken.None);
        var updated = await controller.Update("VTI", new UpdateInstrumentRequest { Name = "Total Market", Active = false }, CancellationToken.None);
        var missing = await controller.Update("NOPE", new UpdateInstrumentRequest { Name = "x" }, CancellationToken.None);

        Assert.That(Value(active), Has.Count.EqualTo(12));
        Assert.That(((CreatedResult)created.Result!).StatusCode, Is.EqualTo(StatusCodes.Status201Created));
        Assert.That(((ObjectResult)duplicate.Result!).StatusCode, Is.EqualTo(StatusCodes.Status409Conflict));
        Assert.That(Value(updated).Active, Is.False);
        Assert.That(missing.Result, Is.TypeOf<NotFoundResult>());
    }

    [Test]
    public async Task Notes_CreateAndList()
    {
        var controller = new NotesController(_ctx.Notes);

        var created = await controller.Create(new CreateNoteRequest { Date = new DateOnly(2026, 10, 9), Kind = NoteKind.Pass, Text = "Passed" }, CancellationToken.None);
        var list = await controller.GetAll(new NoteListQuery(), CancellationToken.None);

        Assert.That(((CreatedResult)created.Result!).StatusCode, Is.EqualTo(StatusCodes.Status201Created));
        Assert.That(Value(list).Single().Kind, Is.EqualTo(NoteKind.Pass));
    }

    [Test]
    public async Task Dashboard_Get_ReturnsTheFixture()
    {
        var controller = new DashboardController(_ctx.Dashboard);

        DashboardDto dto = Value(await controller.Get(CancellationToken.None));

        Assert.That(dto.AccountValue, Is.EqualTo(2053.55m));
    }
}
