import React, { useEffect, useMemo, useState } from "react";
import { DataTable } from "primereact/datatable";
import { Column } from "primereact/column";
import { Button } from "primereact/button";
import type { Signal, WatchlistRowDto } from "../../api/generated/types.gen";
import { PageHeader } from "../layout/PageHeader";
import { EmptyBlock, ErrorBlock, LoadingBlock } from "../common/QueryState";
import { SignalPill, StaleBadge, Pill } from "../common/Badges";
import { useAuth, useToast } from "../../context";
import { useAppDispatch, useAppSelector } from "../../store/hooks";
import { fetchLastRun, fetchWatchlist, runRefresh } from "../../store/slices/watchlistSlice";
import { formatDate, formatDateTime, formatMoney, formatSignedPercent } from "../../utils/format";
import { AddTickerDialog } from "./AddTickerDialog";
import { InstrumentDrawer } from "./InstrumentDrawer";
import { RefreshStatus } from "./RefreshStatus";

type SignalFilter = "All" | Signal;

const FILTERS: { value: SignalFilter; label: string }[] = [
  { value: "All", label: "All" },
  { value: "PullbackCandidate", label: "Pullback candidate" },
  { value: "ExtendedWait", label: "Extended - wait" },
  { value: "Watch", label: "Watch" },
  { value: "Avoid", label: "Avoid" },
  { value: "InsufficientData", label: "Insufficient data" },
];

export const Watchlist: React.FC = () => {
  const dispatch = useAppDispatch();
  const toast = useToast();
  const { canWrite, canAdmin } = useAuth();
  const { rows, status, error, lastRun, refreshPhase } = useAppSelector((s) => s.watchlist);
  const [filter, setFilter] = useState<SignalFilter>("All");
  const [selected, setSelected] = useState<string | null>(null);
  const [addOpen, setAddOpen] = useState(false);

  useEffect(() => {
    void dispatch(fetchWatchlist());
    void dispatch(fetchLastRun());
  }, [dispatch]);

  const counts = useMemo(() => {
    const result = new Map<SignalFilter, number>([["All", rows.length]]);
    for (const row of rows) result.set(row.signal, (result.get(row.signal) ?? 0) + 1);
    return result;
  }, [rows]);

  const visible = useMemo(() => (filter === "All" ? rows : rows.filter((r) => r.signal === filter)), [rows, filter]);
  const staleCount = rows.filter((r) => r.stale).length;
  const selectedRow = rows.find((r) => r.ticker === selected) ?? null;

  const onRefresh = async () => {
    const result = await dispatch(runRefresh());
    if (runRefresh.fulfilled.match(result)) {
      const run = result.payload;
      if (run.status === "Succeeded") toast.success("Refresh finished", `${run.tickersOk} tickers updated.`);
      else
        toast.warn(
          `Refresh ${run.status.toLowerCase()}`,
          run.message ?? `${run.tickersOk} ok, ${run.tickersFailed} failed.`,
        );
    } else if (result.payload?.status === 409) {
      toast.warn("A refresh is already running", "Wait for it to finish, then reload.");
    } else {
      toast.error("Refresh failed", result.payload?.message);
    }
  };

  const actions = (
    <>
      {canAdmin && (
        <Button
          label={refreshPhase === "running" ? "Refreshing..." : "Refresh now"}
          icon="pi pi-refresh"
          size="small"
          outlined
          loading={refreshPhase === "running"}
          disabled={refreshPhase === "running"}
          onClick={() => void onRefresh()}
        />
      )}
      {canWrite && <Button label="Add ticker" icon="pi pi-plus" size="small" onClick={() => setAddOpen(true)} />}
    </>
  );

  const lastClose = rows
    .map((r) => r.asOfDate)
    .filter((d): d is string => !!d)
    .sort()
    .at(-1);

  return (
    <div className="space-y-4">
      <PageHeader
        title="Watchlist"
        subtitle={
          lastClose
            ? `Signals use the last completed close (latest as-of ${formatDate(lastClose)}).`
            : "Signals use the last completed close."
        }
        actions={actions}
      />

      <RefreshStatus run={lastRun} phase={refreshPhase} />

      {staleCount > 0 && (
        <div
          role="alert"
          className="rounded-lg border border-warn/30 bg-warn-tint text-warn px-3 py-2 text-sm"
          data-testid="stale-banner"
        >
          {staleCount} {staleCount === 1 ? "ticker has" : "tickers have"} prices older than the most recent market
          close. The signal below may not reflect today.
        </div>
      )}

      <div className="flex flex-wrap gap-2" role="group" aria-label="Filter by signal">
        {FILTERS.map((f) => (
          <button
            key={f.value}
            type="button"
            aria-pressed={filter === f.value}
            onClick={() => setFilter(f.value)}
            className={[
              "chip-toggle rounded-full border px-3 py-1 text-sm cursor-pointer",
              filter === f.value
                ? "bg-brand text-brand-fg border-brand"
                : "bg-surface text-content border-border-strong hover:bg-surface-sunken",
            ].join(" ")}
          >
            {f.label} <span className="font-numeric text-meta opacity-80">{counts.get(f.value) ?? 0}</span>
          </button>
        ))}
      </div>

      {status === "loading" && rows.length === 0 && <LoadingBlock label="Loading the watchlist" />}
      {status === "failed" && (
        <ErrorBlock
          problem={error}
          onRetry={() => void dispatch(fetchWatchlist())}
          title="Could not load the watchlist"
        />
      )}
      {status === "succeeded" && rows.length === 0 && (
        <EmptyBlock>
          No active tickers yet. {canWrite ? "Add a ticker to start." : "Ask a trader to add one."}
        </EmptyBlock>
      )}

      {rows.length > 0 && (
        <div className="rounded-xl bg-surface border border-border overflow-hidden">
          <DataTable
            value={visible}
            dataKey="ticker"
            size="small"
            stripedRows
            scrollable
            removableSort
            rowClassName={() => "row-clickable"}
            onRowClick={(e) => setSelected((e.data as WatchlistRowDto).ticker)}
            emptyMessage={`No tickers with the signal "${FILTERS.find((f) => f.value === filter)?.label}".`}
            aria-label="Watchlist"
          >
            <Column
              field="ticker"
              header="Ticker"
              sortable
              frozen
              body={(r: WatchlistRowDto) => (
                <button
                  type="button"
                  className="font-semibold text-brand bg-transparent border-0 p-0 cursor-pointer hover:underline"
                  aria-label={`Open ${r.ticker} chart`}
                  onClick={(e) => {
                    e.stopPropagation();
                    setSelected(r.ticker);
                  }}
                >
                  {r.ticker}
                </button>
              )}
            />
            <Column field="name" header="Name" sortable style={{ minWidth: "12rem" }} />
            <Column
              field="asOfDate"
              header="As of"
              sortable
              body={(r: WatchlistRowDto) => (
                <span className="inline-flex items-center gap-2 whitespace-nowrap">
                  <span>{formatDate(r.asOfDate)}</span>
                  {r.stale && <StaleBadge asOf={formatDate(r.asOfDate)} />}
                </span>
              )}
            />
            <Column
              field="last"
              header="Last"
              sortable
              align="right"
              body={(r: WatchlistRowDto) => <span className="font-numeric">{formatMoney(r.last)}</span>}
            />
            <Column
              field="ma20"
              header="20-day"
              sortable
              align="right"
              body={(r: WatchlistRowDto) => <span className="font-numeric">{formatMoney(r.ma20)}</span>}
            />
            <Column
              field="ma50"
              header="50-day"
              sortable
              align="right"
              body={(r: WatchlistRowDto) => <span className="font-numeric">{formatMoney(r.ma50)}</span>}
            />
            <Column
              field="pctVs20"
              header="% vs 20-day"
              sortable
              align="right"
              body={(r: WatchlistRowDto) => <span className="font-numeric">{formatSignedPercent(r.pctVs20)}</span>}
            />
            <Column field="trendText" header="Trend" sortable />
            <Column
              field="signalText"
              header="Signal"
              sortable
              body={(r: WatchlistRowDto) => <SignalPill signal={r.signal} text={r.signalText} />}
            />
            <Column
              field="reentryOkAfter"
              header="Re-entry date"
              sortable
              body={(r: WatchlistRowDto) =>
                r.reentryOkAfter ? (
                  <Pill
                    tone="warn"
                    title="Wash-sale window: a loss on this ticker is still inside the window until this date."
                  >
                    after {formatDate(r.reentryOkAfter)}
                  </Pill>
                ) : (
                  <span className="text-content-muted">Clear</span>
                )
              }
            />
          </DataTable>
        </div>
      )}

      {lastRun?.finishedAt && (
        <p className="text-meta text-content-muted">Prices last refreshed {formatDateTime(lastRun.finishedAt)}.</p>
      )}

      <InstrumentDrawer row={selectedRow} onHide={() => setSelected(null)} />
      <AddTickerDialog visible={addOpen} onHide={() => setAddOpen(false)} />
    </div>
  );
};
