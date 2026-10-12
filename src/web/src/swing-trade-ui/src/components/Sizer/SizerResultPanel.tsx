import React from "react";
import type { SizerResultDto } from "../../api/generated/types.gen";
import { formatCount, formatMoney, formatPercent, formatRatio } from "../../utils/format";
import { Pill } from "../common/Badges";
import { LoadingBlock } from "../common/QueryState";
import type { SizerState } from "./useSizer";

const Row: React.FC<{ label: string; value: React.ReactNode; strong?: boolean }> = ({ label, value, strong }) => (
  <div className="flex items-baseline justify-between gap-3 py-1 border-b border-border last:border-0">
    <dt className="text-sm text-content-muted">{label}</dt>
    <dd className={`m-0 font-numeric ${strong ? "text-lg font-semibold" : ""}`}>{value}</dd>
  </div>
);

/** Shows what POST /sizer returned. The verdict is green when the API says OK, red otherwise. */
export const SizerResultPanel: React.FC<{ state: SizerState; compact?: boolean }> = ({ state, compact }) => {
  const { result, loading, problem, ready } = state;

  if (!ready) {
    return <p className="text-sm text-content-muted">Enter an entry, a stop and a target to see the position size.</p>;
  }
  if (problem) {
    return (
      <p role="alert" className="text-sm text-bad" data-testid="sizer-error">
        {Object.values(problem.fieldErrors).join(" ") || problem.message}
      </p>
    );
  }
  if (!result) return <LoadingBlock label="Sizing" />;

  return <ResultBody result={result} loading={loading} compact={compact} />;
};

const ResultBody: React.FC<{ result: SizerResultDto; loading: boolean; compact?: boolean }> = ({
  result,
  loading,
  compact,
}) => (
  <div className={loading ? "opacity-60" : ""} data-testid="sizer-result" aria-busy={loading}>
    <div className="flex flex-wrap items-center gap-2 mb-2" role="status" aria-live="polite">
      <Pill tone={result.isOk ? "good" : "bad"} testId="sizer-verdict">
        {result.verdict}
      </Pill>
      {result.warning && (
        <Pill tone="bad" testId="sizer-warning">
          {result.warning}
        </Pill>
      )}
    </div>
    <dl className="m-0">
      <Row label="Shares to buy" value={<span data-testid="sizer-shares">{formatCount(result.shares)}</span>} strong />
      <Row label="Shares by risk" value={formatCount(result.sharesByRisk)} />
      <Row label="Shares by position cap" value={formatCount(result.sharesByCap)} />
      <Row
        label="Shares by cash"
        value={result.sharesByCash === null ? "not limited" : formatCount(result.sharesByCash)}
      />
      <Row label="Position cost" value={formatMoney(result.positionCost)} />
      <Row label="Risk at the stop" value={`${formatMoney(result.riskDollars)} (${formatPercent(result.riskPct)})`} />
      <Row label="Reward : risk" value={result.rewardRisk === null ? "n/a" : formatRatio(result.rewardRisk)} />
      <Row
        label="Max entry for the minimum reward : risk"
        value={<span data-testid="sizer-max-entry">{formatMoney(result.maxEntryForMinRewardRisk)}</span>}
        strong
      />
      {!compact && (
        <>
          <Row label="Capital used for sizing" value={formatMoney(result.capital)} />
          <Row label="Risk budget" value={formatMoney(result.riskBudget)} />
          <Row label="Risk per share" value={formatMoney(result.riskPerShare)} />
          <Row label="Cash available" value={formatMoney(result.availableCash)} />
          <Row label="Open positions" value={formatCount(result.openCount)} />
        </>
      )}
    </dl>
  </div>
);
