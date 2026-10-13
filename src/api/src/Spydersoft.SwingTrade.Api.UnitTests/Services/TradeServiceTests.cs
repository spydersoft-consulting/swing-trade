using Spydersoft.SwingTrade.Api.Services;
using Spydersoft.SwingTrade.Api.UnitTests.Support;
using Spydersoft.SwingTrade.Contracts.Settings;
using Spydersoft.SwingTrade.Contracts.Trades;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Api.UnitTests.Services;

[TestFixture]
public class TradeServiceTests
{
    private TestHarness _ctx = null!;

    [SetUp]
    public async Task SetUp() => _ctx = await TestHarness.SeededAsync();

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    private static CreateTradeRequest Plan(string ticker = "XLK") => new()
    {
        Ticker = ticker,
        Status = TradeStatus.Planned,
        Setup = TradeSetup.Pullback,
        Shares = 5,
        EntryPrice = 192m,
        StopPrice = 187.5m,
        TargetPrice = 202m
    };

    private static CreateTradeRequest OpenTrade(string ticker, DateOnly openDate) =>
        new()
        {
            Ticker = ticker, Status = TradeStatus.Open, Shares = 10, EntryPrice = 50m, StopPrice = 48m,
            TargetPrice = 56m, OpenDate = openDate
        };

    private async Task UpdateSettingsAsync(Action<UpdateSettingsRequest> change)
    {
        var s = await _ctx.Settings.GetAsync();
        var req = new UpdateSettingsRequest
        {
            StartingCapital = s.StartingCapital, MaxRiskPct = s.MaxRiskPct, MaxOpenPositions = s.MaxOpenPositions,
            MinRewardRisk = s.MinRewardRisk, TaxSetAsideRate = s.TaxSetAsideRate, WashSaleWindowDays = s.WashSaleWindowDays,
            MaxPositionPct = s.MaxPositionPct, PullbackBandLow = s.PullbackBandLow, PullbackBandHigh = s.PullbackBandHigh,
            ExtendedThreshold = s.ExtendedThreshold, RiskBasis = s.RiskBasis
        };
        change(req);
        var result = await _ctx.Settings.UpdateAsync(req);
        Assert.That(result.IsOk, Is.True);
    }

    [Test]
    public async Task Create_Planned_ReturnsComputedFields()
    {
        var result = await _ctx.Trades.CreateAsync(Plan());

        Assert.That(result.IsOk, Is.True);
        var t = result.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(t.Status, Is.EqualTo(TradeStatus.Planned));
            Assert.That(t.OpenDate, Is.Null);
            Assert.That(t.Position, Is.EqualTo(960m));
            Assert.That(t.RiskDollars, Is.EqualTo(22.5m));
            Assert.That(Math.Round(t.PlannedRewardRisk, 3), Is.EqualTo(2.222m));
            Assert.That(t.Pnl, Is.Null);
            Assert.That(t.CreatedAt, Is.EqualTo(_ctx.Clock.UtcNow));
        });
    }

    [Test]
    public async Task Create_FlagsRiskOverLimitAndRewardRiskBelowMin()
    {
        var req = Plan();
        req.Shares = 15;        // risk 67.50 on 2,000 is 3.4%, above 1.5%
        req.TargetPrice = 195m; // reward:risk 0.67

        var t = (await _ctx.Trades.CreateAsync(req)).Value!;

        Assert.That(t.RiskOverLimit, Is.True);
        Assert.That(t.RewardRiskBelowMin, Is.True);
    }

    [Test]
    public async Task Create_ThresholdsComeFromSettings()
    {
        await UpdateSettingsAsync(r =>
        {
            r.MaxRiskPct = 0.05m;
            r.MinRewardRisk = 2.5m;
        });

        var t = (await _ctx.Trades.CreateAsync(Plan())).Value!;

        Assert.That(t.RiskOverLimit, Is.False, "1.125% is under the new 5% limit");
        Assert.That(t.RewardRiskBelowMin, Is.True, "2.222 is under the new 2.5 minimum");
    }

    [Test]
    public async Task Create_EntryNotAboveStop_ReturnsFieldError()
    {
        var req = Plan();
        req.StopPrice = req.EntryPrice;

        var result = await _ctx.Trades.CreateAsync(req);

        Assert.That(result.Kind, Is.EqualTo(ResultKind.Invalid));
        Assert.That(result.Errors, Does.ContainKey("EntryPrice"));
    }

    [Test]
    public async Task Create_ZeroShares_ReturnsFieldError()
    {
        var req = Plan();
        req.Shares = 0;

        var result = await _ctx.Trades.CreateAsync(req);

        Assert.That(result.Errors, Does.ContainKey("Shares"));
    }

    [Test]
    public async Task Create_OpenWithoutOpenDate_ReturnsFieldError()
    {
        var req = OpenTrade("XLK", new DateOnly(2026, 10, 13));
        req.OpenDate = null;

        var result = await _ctx.Trades.CreateAsync(req);

        Assert.That(result.Errors, Does.ContainKey("OpenDate"));
    }

    [Test]
    public async Task Create_PlannedWithOpenDate_ReturnsFieldError()
    {
        var req = Plan();
        req.OpenDate = new DateOnly(2026, 10, 13);

        Assert.That((await _ctx.Trades.CreateAsync(req)).Errors, Does.ContainKey("OpenDate"));
    }

    [Test]
    public async Task Create_ClosedStatus_IsRejected()
    {
        var req = Plan();
        req.Status = TradeStatus.Closed;

        Assert.That((await _ctx.Trades.CreateAsync(req)).Errors, Does.ContainKey("Status"));
    }

    [Test]
    public async Task Create_UnknownTicker_ReturnsFieldError()
    {
        var result = await _ctx.Trades.CreateAsync(Plan("NOPE"));

        Assert.That(result.Errors, Does.ContainKey("Ticker"));
    }

    [Test]
    public async Task Create_LowercaseTicker_IsNormalised()
    {
        var result = await _ctx.Trades.CreateAsync(Plan("xlk"));

        Assert.That(result.Value!.Ticker, Is.EqualTo("XLK"));
    }

    [Test]
    public async Task Fill_PlannedBecomesOpenAtTheFillPrice()
    {
        var id = (await _ctx.Trades.CreateAsync(Plan())).Value!.Id;

        var result = await _ctx.Trades.FillAsync(id, new FillTradeRequest { FillPrice = 191.5m, FillDate = new DateOnly(2026, 10, 14) });

        Assert.That(result.IsOk, Is.True);
        Assert.That(result.Value!.Status, Is.EqualTo(TradeStatus.Open));
        Assert.That(result.Value.EntryPrice, Is.EqualTo(191.5m));
        Assert.That(result.Value.OpenDate, Is.EqualTo(new DateOnly(2026, 10, 14)));
    }

    [Test]
    public async Task Fill_AtOrBelowStop_ReturnsFieldError()
    {
        var id = (await _ctx.Trades.CreateAsync(Plan())).Value!.Id;

        var result = await _ctx.Trades.FillAsync(id, new FillTradeRequest { FillPrice = 187.5m, FillDate = new DateOnly(2026, 10, 14) });

        Assert.That(result.Errors, Does.ContainKey("FillPrice"));
    }

    [Test]
    public async Task Fill_OpenTrade_IsAConflict()
    {
        var id = (await _ctx.Trades.CreateAsync(OpenTrade("XLK", new DateOnly(2026, 10, 13)))).Value!.Id;

        var result = await _ctx.Trades.FillAsync(id, new FillTradeRequest { FillPrice = 51m, FillDate = new DateOnly(2026, 10, 14) });

        Assert.That(result.Kind, Is.EqualTo(ResultKind.Conflict));
    }

    [Test]
    public async Task Close_OpenBecomesClosedWithPnlAndR()
    {
        var id = (await _ctx.Trades.CreateAsync(OpenTrade("XLK", new DateOnly(2026, 10, 13)))).Value!.Id;

        var result = await _ctx.Trades.CloseAsync(id, new CloseTradeRequest
        {
            ExitPrice = 54m, ExitDate = new DateOnly(2026, 10, 20), FollowedPlan = true, Lessons = "Held to target"
        });

        var t = result.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(t.Status, Is.EqualTo(TradeStatus.Closed));
            Assert.That(t.Pnl, Is.EqualTo(40m));
            Assert.That(t.RMultiple, Is.EqualTo(2m));
            Assert.That(t.DaysHeld, Is.EqualTo(7));
            Assert.That(t.FollowedPlan, Is.True);
            Assert.That(t.Lessons, Is.EqualTo("Held to target"));
        });
    }

    [Test]
    public async Task Close_ExitBeforeOpen_ReturnsFieldError()
    {
        var id = (await _ctx.Trades.CreateAsync(OpenTrade("XLK", new DateOnly(2026, 10, 13)))).Value!.Id;

        var result = await _ctx.Trades.CloseAsync(id, new CloseTradeRequest { ExitPrice = 54m, ExitDate = new DateOnly(2026, 10, 12) });

        Assert.That(result.Errors, Does.ContainKey("ExitDate"));
    }

    [Test]
    public async Task Close_PlannedTrade_IsAConflict()
    {
        var id = (await _ctx.Trades.CreateAsync(Plan())).Value!.Id;

        var result = await _ctx.Trades.CloseAsync(id, new CloseTradeRequest { ExitPrice = 54m, ExitDate = new DateOnly(2026, 10, 20) });

        Assert.That(result.Kind, Is.EqualTo(ResultKind.Conflict));
    }

    [Test]
    public async Task Cancel_PlannedBecomesCancelled_ButOpenIsAConflict()
    {
        var plannedId = (await _ctx.Trades.CreateAsync(Plan())).Value!.Id;
        var openId = (await _ctx.Trades.CreateAsync(OpenTrade("XLF", new DateOnly(2026, 10, 13)))).Value!.Id;

        Assert.That((await _ctx.Trades.CancelAsync(plannedId)).Value!.Status, Is.EqualTo(TradeStatus.Cancelled));
        Assert.That((await _ctx.Trades.CancelAsync(openId)).Kind, Is.EqualTo(ResultKind.Conflict));
        Assert.That((await _ctx.Trades.CancelAsync(plannedId)).Kind, Is.EqualTo(ResultKind.Conflict), "already cancelled");
    }

    [Test]
    public async Task MissingTrade_IsNotFound()
    {
        Assert.That((await _ctx.Trades.GetAsync(999)).Kind, Is.EqualTo(ResultKind.NotFound));
        Assert.That((await _ctx.Trades.CancelAsync(999)).Kind, Is.EqualTo(ResultKind.NotFound));
        Assert.That(await _ctx.Trades.DeleteAsync(999), Is.False);
    }

    [Test]
    public async Task Update_EditsFieldsAndValidatesAgainstTheCurrentStatus()
    {
        var created = (await _ctx.Trades.CreateAsync(Plan())).Value!;
        var edit = new UpdateTradeRequest
        {
            Ticker = "XLK", Setup = TradeSetup.Breakout, Shares = 4, EntryPrice = 193m, StopPrice = 188m,
            TargetPrice = 205m, Rationale = "edited"
        };

        var ok = await _ctx.Trades.UpdateAsync(created.Id, edit);
        edit.OpenDate = new DateOnly(2026, 10, 14);
        var bad = await _ctx.Trades.UpdateAsync(created.Id, edit);

        Assert.That(ok.Value!.Setup, Is.EqualTo(TradeSetup.Breakout));
        Assert.That(ok.Value.Shares, Is.EqualTo(4));
        Assert.That(bad.Errors, Does.ContainKey("OpenDate"), "a Planned trade has no open date");
    }

    [Test]
    public async Task Update_ClosedTradeNeedsExitFields()
    {
        var id = _ctx.Db.Trades.Single(t => t.Ticker == "XLE").Id;

        var result = await _ctx.Trades.UpdateAsync(id, new UpdateTradeRequest
        {
            Ticker = "XLE", Shares = 15, EntryPrice = 62.34m, StopPrice = 61.40m, TargetPrice = 65.90m,
            OpenDate = new DateOnly(2026, 10, 5)
        });

        Assert.That(result.Errors, Does.ContainKey("ExitPrice"));
        Assert.That(result.Errors, Does.ContainKey("ExitDate"));
    }

    [Test]
    public async Task List_FiltersByStatus()
    {
        await _ctx.Trades.CreateAsync(Plan());

        var planned = await _ctx.Trades.ListAsync(new TradeListQuery { Status = TradeStatus.Planned });
        var closed = await _ctx.Trades.ListAsync(new TradeListQuery { Status = TradeStatus.Closed });
        var all = await _ctx.Trades.ListAsync(new TradeListQuery());

        Assert.That(planned, Has.Count.EqualTo(1));
        Assert.That(closed.Single().Ticker, Is.EqualTo("XLE"));
        Assert.That(all, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task WashSale_FlagsAReentryWithinTheWindowOfALoss_ButNotOutsideOrAfterAWin()
    {
        await _ctx.AddClosedAsync("XLK", entry: 100m, exit: 95m, open: new DateOnly(2026, 9, 20), close: new DateOnly(2026, 10, 5));

        var day30 = (await _ctx.Trades.CreateAsync(OpenTrade("XLK", new DateOnly(2026, 11, 4)))).Value!;
        var day31 = (await _ctx.Trades.CreateAsync(OpenTrade("XLK", new DateOnly(2026, 11, 5)))).Value!;
        var before30 = (await _ctx.Trades.CreateAsync(OpenTrade("XLK", new DateOnly(2026, 9, 5)))).Value!;
        var before31 = (await _ctx.Trades.CreateAsync(OpenTrade("XLK", new DateOnly(2026, 9, 4)))).Value!;
        var afterWin = (await _ctx.Trades.CreateAsync(OpenTrade("XLE", new DateOnly(2026, 10, 14)))).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(day30.WashSaleFlag, Is.True);
            Assert.That(day31.WashSaleFlag, Is.False);
            Assert.That(before30.WashSaleFlag, Is.True);
            Assert.That(before31.WashSaleFlag, Is.False);
            Assert.That(afterWin.WashSaleFlag, Is.False, "the seeded XLE trade was a win");
        });
    }

    [Test]
    public async Task WashSale_PlannedTradeIsNeverFlagged_AndATradeNeverFlagsItself()
    {
        var loser = await _ctx.AddClosedAsync("XLK", entry: 100m, exit: 95m, open: new DateOnly(2026, 10, 1), close: new DateOnly(2026, 10, 5));
        await _ctx.Trades.CreateAsync(Plan("XLK"));

        var all = await _ctx.Trades.ListAsync(new TradeListQuery { Ticker = "XLK" });

        Assert.That(all.Single(t => t.Id == loser.Id).WashSaleFlag, Is.False);
        Assert.That(all.Single(t => t.Status == TradeStatus.Planned).WashSaleFlag, Is.False);
    }

    [Test]
    public async Task WashSale_UsesTheWindowFromSettings()
    {
        await _ctx.AddClosedAsync("XLK", entry: 100m, exit: 95m, open: new DateOnly(2026, 9, 20), close: new DateOnly(2026, 10, 5));
        await UpdateSettingsAsync(r => r.WashSaleWindowDays = 10);

        var t = (await _ctx.Trades.CreateAsync(OpenTrade("XLK", new DateOnly(2026, 10, 20)))).Value!;

        Assert.That(t.WashSaleFlag, Is.False);
    }

    [Test]
    public async Task RiskPct_UsesAccountValueWhenTheRiskBasisSaysSo()
    {
        await UpdateSettingsAsync(r => r.RiskBasis = RiskBasis.AccountValue);

        var xle = (await _ctx.Trades.ListAsync(new TradeListQuery { Status = TradeStatus.Closed })).Single();

        Assert.That(xle.RiskPct, Is.EqualTo(14.10m / 2053.55m));
    }

    [Test]
    public async Task Create_RejectsOverlongTextAndOversizedPricesAsFieldErrors()
    {
        var request = Plan();
        request.Rationale = new string('x', 4001);
        request.TargetPrice = 1e20m;

        var result = await _ctx.Trades.CreateAsync(request);

        Assert.That(result.Errors.Keys, Is.SupersetOf(new[] { "Rationale", "TargetPrice" }));
    }

    [Test]
    public async Task FillAndClose_RejectOversizedPrices()
    {
        var planned = (await _ctx.Trades.CreateAsync(Plan())).Value!;
        var fill = await _ctx.Trades.FillAsync(planned.Id, new FillTradeRequest { FillPrice = 1e20m, FillDate = new DateOnly(2026, 10, 12) });
        Assert.That(fill.Errors.Keys, Does.Contain("FillPrice"));

        var open = (await _ctx.Trades.CreateAsync(OpenTrade("XLK", new DateOnly(2026, 10, 1)))).Value!;
        var close = await _ctx.Trades.CloseAsync(open.Id, new CloseTradeRequest { ExitPrice = 1e20m, ExitDate = new DateOnly(2026, 10, 12), Lessons = new string('y', 4001) });
        Assert.That(close.Errors.Keys, Is.SupersetOf(new[] { "ExitPrice", "Lessons" }));
    }
}
