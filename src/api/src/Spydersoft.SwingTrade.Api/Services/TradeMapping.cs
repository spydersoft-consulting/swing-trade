using Spydersoft.SwingTrade.Data.Entities;
using Spydersoft.SwingTrade.Rules;

namespace Spydersoft.SwingTrade.Api.Services;

internal static class TradeMapping
{
    public static TradeInput ToInput(Trade t) =>
        new(t.Shares, t.EntryPrice, t.StopPrice, t.TargetPrice, t.ExitPrice, t.OpenDate, t.ExitDate);

    public static DashboardTrade ToDashboardTrade(Trade t) =>
        new(t.Status, t.Setup, t.Shares, t.EntryPrice, t.StopPrice, t.ExitPrice, t.OpenDate, t.ExitDate, t.FollowedPlan);
}
