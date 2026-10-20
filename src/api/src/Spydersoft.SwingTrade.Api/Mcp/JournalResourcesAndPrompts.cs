using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Spydersoft.SwingTrade.Api.Services;

namespace Spydersoft.SwingTrade.Api.Mcp;

[McpServerResourceType]
public class JournalResources(SettingsService settings)
{
    public const string RulesUri = "journal://rules";

    [McpServerResource(UriTemplate = RulesUri, Name = "rules", Title = "Swing trade rule set", MimeType = "text/plain")]
    [Description("The owner's full rule set in plain text: trend and signal definitions, sizer formulas, the wash-sale rule and the current Settings values. Read it before giving an opinion.")]
    public async Task<string> Rules(CancellationToken ct) => RulesText.Build(await settings.GetRulesAsync(ct));
}

[McpServerPromptType]
public class JournalPrompts(TradeService trades)
{
    public const string WeeklyReview = "weekly_review";
    public const string TradePostmortem = "trade_postmortem";

    [McpServerPrompt(Name = WeeklyReview, Title = "Weekly review")]
    [Description("The one-hour weekly review: dashboard, watchlist, open and planned trades, last week's notes, pullback candidates sized with your levels, stale data and wash-sale conflicts.")]
    public static string WeeklyReviewPrompt() =>
        $"""
        Run my weekly swing-trade review against my own rules. Use only the journal tools and the {JournalResources.RulesUri} resource; quote the numbers the tools return and do not calculate your own. Signals are informational, not advice.

        1. Read {JournalResources.RulesUri} first so your judgement is anchored to my real rules.
        2. Call get_dashboard, get_watchlist, list_trades with status Open, list_trades with status Planned, and list_notes for the last 7 days.
        3. Check dataStatus and asOf on every result. If anything is stale, say so before anything else.
        4. List every ticker whose Signal is "Pullback candidate". For each one, call get_levels for the swing low and prior high as stop and target candidates, ask me for my entry, stop and target (or offer the last close as the entry), then call size_position and report shares, cost, risk, reward:risk, MaxEntryForMinRR and the verdict.
        5. Flag wash-sale conflicts (get_instrument shows the re-entry date) and any open position that breaks my limits.
        6. Summarise open and planned trades against their stops and targets.
        7. End by asking whether I want to record a pass for this week.
        """;

    [McpServerPrompt(Name = TradePostmortem, Title = "Trade postmortem")]
    [Description("Compare a closed trade with its plan and the rules, in plain language, and suggest one thing to keep or change.")]
    public async Task<string> TradePostmortemPrompt(
        [Description("Id of the trade to review.")] int tradeId,
        CancellationToken ct = default)
    {
        var result = await trades.GetAsync(tradeId, ct);
        if (!result.IsOk)
            throw new McpException($"Trade {tradeId} was not found.");

        return $"""
            Write a short postmortem of trade {tradeId} ({result.Value!.Ticker}) in plain language. Use only the journal tools and the {JournalResources.RulesUri} resource; quote returned numbers and do not calculate your own. This is a review against my own rules, not advice.

            1. Call get_trade with id {tradeId} (it returns the plan, the result, the rationale, the lessons and notes) and read {JournalResources.RulesUri}.
            2. Compare the plan (entry, stop, target, planned reward:risk, risk percent) with what happened (exit, P&L, R-multiple, days held, whether I followed the plan).
            3. Check it against the rules: was the setup a Pullback candidate under my thresholds, was the size within my risk and position limits, and does the wash-sale flag apply?
            4. If the trade is not Closed yet, say so and review the plan only.
            5. Finish with exactly one thing to keep and one thing to change.
            """;
    }
}
