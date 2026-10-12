import React, { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Sidebar } from "primereact/sidebar";
import { Button } from "primereact/button";
import { ConfirmDialog, confirmDialog } from "primereact/confirmdialog";
import type { BarsDto, WatchlistRowDto } from "../../api/generated/types.gen";
import { swingTradeApi } from "../../api/swingTradeApi";
import { toProblem, type ApiProblem } from "../../api/problem";
import { LineChart, type ChartSeries } from "../common/LineChart";
import { ErrorBlock, LoadingBlock } from "../common/QueryState";
import { SignalPill, StaleBadge } from "../common/Badges";
import { useAuth, useToast } from "../../context";
import { useAppDispatch } from "../../store/hooks";
import { deactivateInstrument } from "../../store/slices/watchlistSlice";
import { formatDate, formatMoney, formatSignedPercent } from "../../utils/format";

interface Props {
  row: WatchlistRowDto | null;
  onHide: () => void;
}

const CHART_DAYS = 120;

export const InstrumentDrawer: React.FC<Props> = ({ row, onHide }) => {
  const { canWrite } = useAuth();
  const toast = useToast();
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const ticker = row?.ticker ?? null;
  const [bars, setBars] = useState<BarsDto | null>(null);
  const [error, setError] = useState<ApiProblem | null>(null);
  const [loading, setLoading] = useState(false);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    if (!ticker) return;
    let cancelled = false;
    setLoading(true);
    setError(null);
    setBars(null);
    swingTradeApi
      .bars(ticker, CHART_DAYS)
      .then((data) => {
        if (!cancelled) setBars(data);
      })
      .catch((e: unknown) => {
        if (!cancelled) setError(toProblem(e));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [ticker, attempt]);

  const labels = useMemo(() => bars?.bars.map((b) => b.date) ?? [], [bars]);
  const series = useMemo<ChartSeries[]>(
    () =>
      bars
        ? [
            { label: "Close", values: bars.bars.map((b) => b.close), color: "#1d4ed8" },
            { label: "20-day", values: bars.bars.map((b) => b.ma20), color: "#b45309", dashed: true },
            { label: "50-day", values: bars.bars.map((b) => b.ma50), color: "#6b7280", dashed: true },
          ]
        : [],
    [bars],
  );

  const planHref = row
    ? `/sizer?ticker=${encodeURIComponent(row.ticker)}${row.last !== null ? `&entry=${row.last}` : ""}`
    : "/sizer";

  const confirmDeactivate = () => {
    if (!row) return;
    confirmDialog({
      message: `Deactivate ${row.ticker}? It leaves the watchlist; trades and history are kept.`,
      header: "Deactivate ticker",
      icon: "pi pi-exclamation-triangle",
      acceptLabel: "Deactivate",
      rejectLabel: "Keep",
      accept: async () => {
        const result = await dispatch(deactivateInstrument(row.ticker));
        if (deactivateInstrument.fulfilled.match(result)) {
          toast.success("Ticker deactivated", row.ticker);
          onHide();
        } else {
          toast.error("Could not deactivate", result.payload?.message);
        }
      },
    });
  };

  return (
    <Sidebar
      visible={row !== null}
      onHide={onHide}
      position="right"
      style={{ width: "min(40rem, 100vw)" }}
      header={row ? <span className="text-lg font-semibold">{`${row.ticker} ${row.name}`}</span> : undefined}
    >
      {row && (
        <div className="space-y-4" data-testid="instrument-drawer">
          <dl className="grid grid-cols-2 sm:grid-cols-3 gap-3 text-sm">
            <Fact label="As of">
              <span className="inline-flex items-center gap-2">
                {formatDate(row.asOfDate)}
                {row.stale && <StaleBadge asOf={formatDate(row.asOfDate)} />}
              </span>
            </Fact>
            <Fact label="Last close">{formatMoney(row.last)}</Fact>
            <Fact label="% vs 20-day">{formatSignedPercent(row.pctVs20)}</Fact>
            <Fact label="20-day">{formatMoney(row.ma20)}</Fact>
            <Fact label="50-day">{formatMoney(row.ma50)}</Fact>
            <Fact label="Trend">{row.trendText}</Fact>
            <Fact label="Signal">
              <SignalPill signal={row.signal} text={row.signalText} />
            </Fact>
            <Fact label="Re-entry date">
              {row.reentryOkAfter ? `after ${formatDate(row.reentryOkAfter)}` : "Clear"}
            </Fact>
          </dl>

          {loading && <LoadingBlock label="Loading the chart" />}
          {error && (
            <ErrorBlock problem={error} onRetry={() => setAttempt((n) => n + 1)} title="Could not load the chart" />
          )}
          {bars && bars.bars.length === 0 && (
            <p className="text-sm text-content-muted">No completed bars are stored for {row.ticker} yet.</p>
          )}
          {bars && bars.bars.length > 0 && (
            <div>
              <LineChart
                labels={labels}
                series={series}
                ariaLabel={`${row.ticker} close with 20-day and 50-day averages, last ${bars.bars.length} sessions`}
                formatValue={formatMoney}
              />
              <p className="text-meta text-content-muted mt-1" data-testid="chart-asof">
                {bars.bars.length} completed sessions, through {formatDate(bars.bars.at(-1)?.date)}.
              </p>
            </div>
          )}

          <div className="flex flex-wrap gap-2 pt-2">
            <Button
              type="button"
              label="Plan a trade"
              icon="pi pi-calculator"
              onClick={() => {
                onHide();
                navigate(planHref);
              }}
            />
            {canWrite && (
              <Button
                type="button"
                label="Deactivate ticker"
                icon="pi pi-eye-slash"
                outlined
                severity="danger"
                onClick={confirmDeactivate}
              />
            )}
          </div>
          <p className="text-meta text-content-muted">
            Plan a trade opens the sizer with the last close ({formatMoney(row.last)}, {formatDate(row.asOfDate)}) as
            the entry. Signals are informational, not advice.
          </p>
        </div>
      )}
      <ConfirmDialog />
    </Sidebar>
  );
};

const Fact: React.FC<{ label: string; children: React.ReactNode }> = ({ label, children }) => (
  <div className="min-w-0">
    <dt className="text-meta uppercase tracking-wide text-content-muted">{label}</dt>
    <dd className="font-numeric m-0">{children}</dd>
  </div>
);
