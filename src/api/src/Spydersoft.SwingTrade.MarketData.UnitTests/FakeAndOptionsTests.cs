using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spydersoft.SwingTrade.MarketData.UnitTests.Support;

namespace Spydersoft.SwingTrade.MarketData.UnitTests;

[TestFixture]
public class FakeAndOptionsTests
{
    [Test]
    public async Task Fake_IsDeterministic_SkipsWeekends_AndIsRangeIndependent()
    {
        var fake = new FakeMarketDataProvider();
        var wide = await fake.GetDailyBars("SPY", new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 9), default);
        var narrow = await fake.GetDailyBars("SPY", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 9), default);

        Assert.Multiple(() =>
        {
            Assert.That(wide.Select(b => b.Date.DayOfWeek), Has.None.EqualTo(DayOfWeek.Saturday).And.None.EqualTo(DayOfWeek.Sunday));
            Assert.That(narrow, Has.Count.EqualTo(5));
            Assert.That(wide.Skip(wide.Count - 5).Select(b => b.Close), Is.EqualTo(narrow.Select(b => b.Close)));
        });
    }

    [Test]
    public async Task Fake_CanThrowPerTicker_FromConfigOrCode()
    {
        var fake = new FakeMarketDataProvider(Opts.Create(o => o.Fake.FailTickers = ["XLE"])).FailFor("XLK");
        await Assert.ThrowsAsync<ProviderUnavailableException>(() => fake.GetDailyBars("XLE", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), default));
        await Assert.ThrowsAsync<ProviderUnavailableException>(() => fake.GetDailyBars("XLK", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), default));
        Assert.DoesNotThrowAsync(() => fake.GetDailyBars("SPY", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), default));
    }

    [Test]
    public async Task Fake_ReturnsExplicitBarsWhenSet()
    {
        var fake = new FakeMarketDataProvider().SetBars("SPY", [new DailyBar(new DateOnly(2026, 10, 9), 1, 2, 0.5m, 1.5m, 10)]);
        var bars = await fake.GetDailyBars("SPY", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 12), default);
        Assert.That(bars.Single().Close, Is.EqualTo(1.5m));
    }

    [TestCase("alpaca", null, null, null, false)]
    [TestCase("alpaca", "id", "secret", null, true)]
    [TestCase("tiingo", null, null, null, false)]
    [TestCase("tiingo", null, null, "tok", true)]
    [TestCase("fake", null, null, null, true)]
    [TestCase("polygon", null, null, null, false)]
    public void Validator_RequiresSecretsForConfiguredProvidersAndKnownNames(string provider, string? keyId, string? secret, string? token, bool valid)
    {
        var options = new MarketDataOptions
        {
            Providers = [provider],
            Alpaca = { KeyId = keyId, SecretKey = secret },
            Tiingo = { Token = token }
        };
        Assert.That(new MarketDataOptionsValidator().Validate(null, options).Succeeded, Is.EqualTo(valid));
    }

    [Test]
    public void Validator_RejectsAnEmptyProviderList() =>
        Assert.That(new MarketDataOptionsValidator().Validate(null, new MarketDataOptions()).Succeeded, Is.False);

    [Test]
    public void Registration_BindsEnvStyleConfigAndBuildsFallbackInOrder()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MarketData:Providers:0"] = "alpaca",
            ["MarketData:Providers:1"] = "tiingo",
            ["MarketData:Alpaca:KeyId"] = "id",
            ["MarketData:Alpaca:SecretKey"] = "secret",
            ["MarketData:Tiingo:Token"] = "tok",
            ["MarketData:Alpaca:MinDelayMs"] = "50"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMarketData(config);
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var fetcher = scope.ServiceProvider.GetRequiredService<IBarFetcher>();

        Assert.Multiple(() =>
        {
            Assert.That(fetcher.ProviderChain, Is.EqualTo("alpaca>tiingo"));
            Assert.That(sp.GetRequiredService<IOptions<MarketDataOptions>>().Value.Alpaca.MinDelayMs, Is.EqualTo(50));
            Assert.That(sp.GetRequiredService<IOptions<MarketDataOptions>>().Value.Alpaca.Feed, Is.EqualTo("sip"));
        });
    }
}
