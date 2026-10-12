using Spydersoft.SwingTrade.Api.Services;
using Spydersoft.SwingTrade.Api.UnitTests.Support;
using Spydersoft.SwingTrade.Contracts.Instruments;
using Spydersoft.SwingTrade.Contracts.Notes;
using Spydersoft.SwingTrade.Contracts.Settings;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Api.UnitTests.Services;

[TestFixture]
public class SettingsInstrumentNoteServiceTests
{
    private TestHarness _ctx = null!;

    [SetUp]
    public async Task SetUp() => _ctx = await TestHarness.SeededAsync();

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    private static UpdateSettingsRequest ValidSettings() => new()
    {
        StartingCapital = 3000m, MaxRiskPct = 0.01m, MaxOpenPositions = 2, MinRewardRisk = 3m, TaxSetAsideRate = 0.30m,
        WashSaleWindowDays = 30, MaxPositionPct = 0.4m, PullbackBandLow = -0.02m, PullbackBandHigh = 0.01m,
        ExtendedThreshold = 0.04m, RiskBasis = RiskBasis.StartingCapital
    };

    [Test]
    public async Task Settings_GetReturnsTheSeededDefaults()
    {
        var s = await _ctx.Settings.GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(s.StartingCapital, Is.EqualTo(2000m));
            Assert.That(s.MaxRiskPct, Is.EqualTo(0.015m));
            Assert.That(s.MaxOpenPositions, Is.EqualTo(3));
            Assert.That(s.MinRewardRisk, Is.EqualTo(2.0m));
            Assert.That(s.TaxSetAsideRate, Is.EqualTo(0.27m));
            Assert.That(s.WashSaleWindowDays, Is.EqualTo(30));
            Assert.That(s.MaxPositionPct, Is.EqualTo(0.50m));
            Assert.That(s.PullbackBandLow, Is.EqualTo(-0.01m));
            Assert.That(s.PullbackBandHigh, Is.EqualTo(0.02m));
            Assert.That(s.ExtendedThreshold, Is.EqualTo(0.05m));
            Assert.That(s.RiskBasis, Is.EqualTo(RiskBasis.StartingCapital));
        });
    }

    [Test]
    public async Task Settings_UpdatePersistsAndFeedsTheRules()
    {
        var result = await _ctx.Settings.UpdateAsync(ValidSettings());

        Assert.That(result.IsOk, Is.True);
        Assert.That((await _ctx.Settings.GetAsync()).StartingCapital, Is.EqualTo(3000m));
        Assert.That((await _ctx.Settings.GetRulesAsync()).MinRewardRisk, Is.EqualTo(3m));
    }

    [Test]
    public async Task Settings_InvalidValuesAreReportedPerField()
    {
        var req = ValidSettings();
        req.StartingCapital = 0m;
        req.MaxRiskPct = 1.5m;
        req.MaxOpenPositions = 0;
        req.PullbackBandLow = 0.05m;

        var result = await _ctx.Settings.UpdateAsync(req);

        Assert.That(result.Kind, Is.EqualTo(ResultKind.Invalid));
        Assert.That(result.Errors.Keys, Is.SupersetOf(new[] { "StartingCapital", "MaxRiskPct", "MaxOpenPositions", "PullbackBandLow" }));
    }

    [Test]
    public async Task Instruments_ListFiltersByActive()
    {
        Assert.That(await _ctx.Instruments.ListAsync(null), Has.Count.EqualTo(24));
        Assert.That(await _ctx.Instruments.ListAsync(true), Has.Count.EqualTo(12));
        Assert.That(await _ctx.Instruments.ListAsync(false), Has.Count.EqualTo(12));
    }

    [Test]
    public async Task Instruments_CreateNormalisesTickerAndRejectsDuplicates()
    {
        var created = await _ctx.Instruments.CreateAsync(new CreateInstrumentRequest { Ticker = " vti ", Name = "Vanguard Total Market" });
        var duplicate = await _ctx.Instruments.CreateAsync(new CreateInstrumentRequest { Ticker = "VTI", Name = "again" });

        Assert.That(created.Value!.Ticker, Is.EqualTo("VTI"));
        Assert.That(duplicate.Kind, Is.EqualTo(ResultKind.Conflict));
    }

    [Test]
    public async Task Instruments_CreateValidatesTickerAndName()
    {
        var result = await _ctx.Instruments.CreateAsync(new CreateInstrumentRequest { Ticker = "BAD TICKER", Name = "" });

        Assert.That(result.Errors.Keys, Is.SupersetOf(new[] { "Ticker", "Name" }));
    }

    [Test]
    public async Task Instruments_UpdateActivatesAndKeepsASingleBenchmark()
    {
        var activated = await _ctx.Instruments.UpdateAsync("xlp", new UpdateInstrumentRequest { Name = "Staples", Active = true });
        var newBenchmark = await _ctx.Instruments.UpdateAsync("QQQ", new UpdateInstrumentRequest { Name = "QQQ", Active = true, IsBenchmark = true });

        Assert.That(activated.Value!.Active, Is.True);
        Assert.That(newBenchmark.IsOk, Is.True);
        var all = await _ctx.Instruments.ListAsync(null);
        Assert.That(all.Where(i => i.IsBenchmark).Select(i => i.Ticker), Is.EqualTo(new[] { "QQQ" }));
        Assert.That((await _ctx.Instruments.UpdateAsync("ZZZZ", new UpdateInstrumentRequest { Name = "x" })).Kind, Is.EqualTo(ResultKind.NotFound));
    }

    [Test]
    public async Task Notes_CreateAndFilter()
    {
        await _ctx.Notes.CreateAsync(new CreateNoteRequest { Date = new DateOnly(2026, 10, 9), Kind = NoteKind.Pass, Text = "No clean setups" });
        await _ctx.Notes.CreateAsync(new CreateNoteRequest { Date = new DateOnly(2026, 10, 12), Kind = NoteKind.Review, Ticker = "xle", Text = "Reviewed XLE" });

        var passes = await _ctx.Notes.ListAsync(new NoteListQuery { Kind = NoteKind.Pass });
        var recent = await _ctx.Notes.ListAsync(new NoteListQuery { From = new DateOnly(2026, 10, 10) });
        var xle = await _ctx.Notes.ListAsync(new NoteListQuery { Ticker = "XLE" });
        var all = await _ctx.Notes.ListAsync(new NoteListQuery());

        Assert.That(passes.Single().Text, Is.EqualTo("No clean setups"));
        Assert.That(recent.Single().Ticker, Is.EqualTo("XLE"));
        Assert.That(xle, Has.Count.EqualTo(1));
        Assert.That(all.First().Date, Is.EqualTo(new DateOnly(2026, 10, 12)), "newest first");
    }

    [Test]
    public async Task Notes_RequireDateAndText()
    {
        var result = await _ctx.Notes.CreateAsync(new CreateNoteRequest { Text = "  " });

        Assert.That(result.Errors.Keys, Is.SupersetOf(new[] { "Date", "Text" }));
    }
}
