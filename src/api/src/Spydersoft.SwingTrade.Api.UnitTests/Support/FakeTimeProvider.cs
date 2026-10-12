namespace Spydersoft.SwingTrade.Api.UnitTests.Support;

/// <summary>A controllable clock.</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 12, 21, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
