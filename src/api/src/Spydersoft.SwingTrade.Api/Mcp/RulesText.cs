using System.Globalization;
using System.Text;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Api.Mcp;

/// <summary>
/// The plain-text rule set behind <c>journal://rules</c>. Every number comes from the Rules library constants or the
/// current Settings, and the verdict and signal wording from the library's own strings, so the text cannot drift from the
/// code. The prose only names the formulas; it never restates a threshold as a literal.
/// </summary>
public static class RulesText
{
    /// <summary>Invariant, no trailing zeros: 0.015 stays 0.015, 2000 stays 2000, 2.0 becomes 2.</summary>
    public static string Format(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);

    public static string Percent(decimal fraction) => Format(fraction * 100m) + "%";

    private static string Both(decimal fraction) => $"{Format(fraction)} ({Percent(fraction)})";

    public static string Build(RulesSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);

        var sw = IndicatorCalculator.ShortWindow;
        var lw = IndicatorCalculator.LongWindow;
        var completedAfter = BarCompletion.CompletedAfter.ToString("HH':'mm", CultureInfo.InvariantCulture);
        var zone = BarCompletion.MarketTimeZoneId;
        var sb = new StringBuilder();
        void Line(string text = "") => sb.Append(text).Append('\n');

        Line("SWING TRADE RULE SET");
        Line("Generated from the Rules library and the current Settings. These are the owner's own rules: review against them,");
        Line("quote the numbers the tools return, and do not calculate your own. Signals are informational, not investment advice.");
        Line();
        Line("DATA");
        Line("- Last = Close of the most recent COMPLETED daily bar (split-adjusted close, not dividend-adjusted).");
        Line($"- A bar is completed when its date is before today in {zone}, or it is after {completedAfter} {zone} on the day itself.");
        Line($"- MA{sw} = average Close of the {sw} most recent completed bars. MA{lw} = average Close of the {lw} most recent completed bars.");
        Line($"- PctVs{sw} = Last / MA{sw} - 1.");
        Line($"- Fewer than {lw} bars -> Trend and Signal = '{Trend.InsufficientData.Display()}'.");
        Line();
        Line("TREND");
        Line($"- {Trend.Uptrend.Display()}: Last > MA{lw} AND MA{sw} > MA{lw}.");
        Line($"- {Trend.Downtrend.Display()}: Last < MA{lw} AND MA{sw} < MA{lw}.");
        Line($"- {Trend.Mixed.Display()}: otherwise (equality counts as Mixed).");
        Line();
        Line("SIGNAL (checked in this order; thresholds are the current Settings, applied on read)");
        Line($"1. {Signal.PullbackCandidate.Display()}: Trend = {Trend.Uptrend.Display()} AND PullbackBandLow <= PctVs{sw} <= PullbackBandHigh, i.e. {Both(s.PullbackBandLow)} to {Both(s.PullbackBandHigh)} inclusive at both ends.");
        Line($"2. {Signal.ExtendedWait.Display()}: Trend = {Trend.Uptrend.Display()} AND PctVs{sw} > ExtendedThreshold, i.e. above {Both(s.ExtendedThreshold)}.");
        Line($"3. {Signal.Avoid.Display()}: Trend = {Trend.Downtrend.Display()}.");
        Line($"4. {Signal.Watch.Display()}: anything else (an uptrend between {Both(s.PullbackBandHigh)} and {Both(s.ExtendedThreshold)} is Watch, not a candidate).");
        Line();
        Line("POSITION SIZER (inputs: Entry, Stop, Target, optionally AvailableCash)");
        Line($"- Capital = StartingCapital ({Format(s.StartingCapital)}), or the live AccountValue when RiskBasis = {nameof(RiskBasis.AccountValue)}. Current RiskBasis = {s.RiskBasis}.");
        Line($"- RiskBudget = Capital * MaxRiskPct ({Both(s.MaxRiskPct)}).");
        Line("- RiskPerShare = Entry - Stop (invalid when Stop >= Entry).");
        Line("- SharesByRisk = floor(RiskBudget / RiskPerShare).");
        Line($"- SharesByCap = floor(Capital * MaxPositionPct ({Both(s.MaxPositionPct)}) / Entry).");
        Line("- SharesByCash = floor(AvailableCash / Entry) (only when supplied). Shares = min(SharesByRisk, SharesByCap, SharesByCash).");
        Line("- PositionCost = Shares * Entry. RiskDollars = Shares * RiskPerShare. RiskPct = RiskDollars / Capital.");
        Line("- RewardRisk = (Target - Entry) / RiskPerShare.");
        Line($"- MaxEntryForMinRR = (Target + MinRewardRisk * Stop) / (1 + MinRewardRisk), with MinRewardRisk = {Format(s.MinRewardRisk)}.");
        Line("- AvailableCash = StartingCapital + RealizedPnL - cost of Open trades - cost of Planned trades (Planned orders reserve cash).");
        Line("- Verdict, first match wins:");
        Line($"  1. '{PositionSizer.VerdictStopAboveEntry}' when Stop >= Entry");
        Line($"  2. '{PositionSizer.VerdictRewardRisk}' when RewardRisk < MinRewardRisk ({Format(s.MinRewardRisk)})");
        Line($"  3. '{PositionSizer.VerdictRiskBudget}' when SharesByRisk < 1");
        Line($"  4. '{PositionSizer.VerdictPositionCap}' when SharesByCap < 1");
        Line($"  5. '{PositionSizer.VerdictCash}' when SharesByCash < 1 (when supplied)");
        Line($"  6. '{PositionSizer.VerdictOk}' otherwise");
        Line($"- Warning (does not change the verdict): count(Open) >= MaxOpenPositions ({s.MaxOpenPositions}).");
        Line();
        Line("TRADE MATH");
        Line("- Position = Shares * Entry. RiskDollars = (Entry - Stop) * Shares. RiskPct = RiskDollars / Capital.");
        Line("- PlannedRR = (Target - Entry) / (Entry - Stop). PnL = (Exit - Entry) * Shares (Closed only). PnLPct = Exit / Entry - 1.");
        Line("- RMultiple = PnL / RiskDollars. DaysHeld = ExitDate - OpenDate (calendar days).");
        Line($"- Flags: RiskPct > MaxRiskPct ({Both(s.MaxRiskPct)}); PlannedRR < MinRewardRisk ({Format(s.MinRewardRisk)}).");
        Line();
        Line("WASH-SALE FLAG (informational; never changes P&L; tax treatment is for the owner and a tax professional)");
        Line($"- A trade is flagged when ANOTHER Closed trade for the same ticker (or the same WashSaleGroup) has PnL < 0 and OpenDate - WashSaleWindowDays <= its ExitDate <= OpenDate + WashSaleWindowDays, with WashSaleWindowDays = {s.WashSaleWindowDays}.");
        Line($"- ReentryOkAfter = latest ExitDate of a losing Closed trade + WashSaleWindowDays ({s.WashSaleWindowDays}) + 1 day; no losing trade -> Clear.");
        Line();
        Line("DASHBOARD");
        Line("- RealizedPnL = sum PnL of Closed trades. AccountValue = StartingCapital + RealizedPnL. ReturnPct = RealizedPnL / StartingCapital.");
        Line($"- TaxSetAside = max(0, RealizedPnL) * TaxSetAsideRate ({Both(s.TaxSetAsideRate)}; an assumption, not tax advice). AfterTaxPnL = RealizedPnL - TaxSetAside.");
        Line($"- OpenCount warns above MaxOpenPositions ({s.MaxOpenPositions}). CapitalAtWork = sum Position of Open trades. RiskAtStops = sum RiskDollars of Open trades.");
        Line("- WinRate = count(PnL > 0) / ClosedCount. ProfitFactor = sum of wins / -(sum of losses), null with no losses. AvgR = mean RMultiple. PlanAdherence = count(FollowedPlan) / ClosedCount.");
        Line("- Benchmark: SPY return from the first trade's OpenDate to the latest close; YouMinusSPY = ReturnPct - SPY return.");
        Line();
        Line("CURRENT SETTINGS (live values; thresholds apply on the next read, no refresh needed)");
        Line($"- {nameof(RulesSettings.StartingCapital)} = {Format(s.StartingCapital)}");
        Line($"- {nameof(RulesSettings.MaxRiskPct)} = {Both(s.MaxRiskPct)}");
        Line($"- {nameof(RulesSettings.MaxOpenPositions)} = {s.MaxOpenPositions}");
        Line($"- {nameof(RulesSettings.MinRewardRisk)} = {Format(s.MinRewardRisk)}");
        Line($"- {nameof(RulesSettings.TaxSetAsideRate)} = {Both(s.TaxSetAsideRate)}");
        Line($"- {nameof(RulesSettings.WashSaleWindowDays)} = {s.WashSaleWindowDays}");
        Line($"- {nameof(RulesSettings.MaxPositionPct)} = {Both(s.MaxPositionPct)}");
        Line($"- {nameof(RulesSettings.PullbackBandLow)} = {Both(s.PullbackBandLow)}");
        Line($"- {nameof(RulesSettings.PullbackBandHigh)} = {Both(s.PullbackBandHigh)}");
        Line($"- {nameof(RulesSettings.ExtendedThreshold)} = {Both(s.ExtendedThreshold)}");
        Line($"- {nameof(RulesSettings.RiskBasis)} = {s.RiskBasis}");
        Line($"- Moving average windows: {sw} and {lw} days.");
        return sb.ToString();
    }
}
