import React from "react";
import type { RefreshRunDto } from "../../api/generated/types.gen";
import type { RefreshPhase } from "../../store/slices/watchlistSlice";
import { formatDateTime } from "../../utils/format";
import { Pill, type Tone } from "../common/Badges";

const statusTone = (status: string): Tone => {
  if (status === "Succeeded") return "good";
  if (status === "Running") return "neutral";
  return "bad";
};

/** The last RefreshRun: when, how it ended and how many tickers were updated. */
export const RefreshStatus: React.FC<{ run: RefreshRunDto | null; phase: RefreshPhase }> = ({ run, phase }) => {
  if (!run) {
    return (
      <p className="text-sm text-content-muted" data-testid="refresh-status">
        No price refresh has run yet.
      </p>
    );
  }
  return (
    <div
      className="flex flex-wrap items-center gap-2 text-sm text-content-muted"
      data-testid="refresh-status"
      aria-live="polite"
    >
      <span>Last refresh</span>
      <Pill tone={statusTone(run.status)}>{run.status}</Pill>
      <span>
        {formatDateTime(run.finishedAt ?? run.startedAt)} &middot; {run.tickersOk} ok, {run.tickersFailed} failed
        {run.provider ? ` · ${run.provider}` : ""}
      </span>
      {run.message && run.status !== "Succeeded" && <span className="text-bad">{run.message}</span>}
      {phase === "running" && <span>Updating prices...</span>}
    </div>
  );
};
