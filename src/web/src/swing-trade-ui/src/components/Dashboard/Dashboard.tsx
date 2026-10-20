import React, { useEffect, useMemo } from "react";
import {
  faBriefcase,
  faDollarSign,
  faLock,
  faPercent,
  faTriangleExclamation,
  faWallet,
} from "@fortawesome/free-solid-svg-icons";
import type { DashboardDto } from "../../api/generated/types.gen";
import { PageHeader } from "../layout/PageHeader";
import { KpiTile } from "../layout/KpiTile";
import { EmptyBlock, ErrorBlock, LoadingBlock } from "../common/QueryState";
import { LineChart } from "../common/LineChart";
import { useAppDispatch, useAppSelector } from "../../store/hooks";
import { fetchDashboard } from "../../store/slices/dashboardSlice";
import { fetchSettings } from "../../store/slices/settingsSlice";
import {
  EMPTY,
  formatCount,
  formatDate,
  formatDays,
  formatMoney,
  formatPercent,
  formatR,
  formatRatio,
  formatSignedPercent,
  pnlClass,
} from "../../utils/format";

const Metric: React.FC<{ label: string; value: React.ReactNode; note?: string; testId?: string }> = ({
  label,
  value,
  note,
  testId,
}) => (
  <div className="min-w-0" data-testid={testId}>
    <dt className="text-meta uppercase tracking-wide text-content-muted">{label}</dt>
    <dd className="font-numeric text-lg m-0">{value}</dd>
    {note && <dd className="text-meta text-content-muted m-0">{note}</dd>}
  </div>
);

const SETUP_LABELS: Record<string, string> = {
  Pullback: "Pullback",
  Breakout: "Breakout",
  MeanReversion: "Mean reversion",
  Other: "Other",
};

export const Dashboard: React.FC = () => {
  const dispatch = useAppDispatch();
  const { data, status, error } = useAppSelector((s) => s.dashboard);
  const settings = useAppSelector((s) => s.settings.settings);

  useEffect(() => {
    void dispatch(fetchDashboard());
    void dispatch(fetchSettings());
  }, [dispatch]);

  return (
    <div className="space-y-4">
      <PageHeader
        title="Dashboard"
        subtitle="Every figure is calculated by the server from your closed and open trades."
      />
      {status === "loading" && !data && <LoadingBlock label="Loading the dashboard" />}
      {status === "failed" && (
        <ErrorBlock
          problem={error}
          onRetry={() => void dispatch(fetchDashboard())}
          title="Could not load the dashboard"
        />
      )}
      {data && (
        <DashboardBody
          data={data}
          maxOpen={settings?.maxOpenPositions}
          taxRate={settings?.taxSetAsideRate}
          startingCapital={settings?.startingCapital}
        />
      )}
    </div>
  );
};

const DashboardBody: React.FC<{ data: DashboardDto; maxOpen?: number; taxRate?: number; startingCapital?: number }> = ({
  data,
  maxOpen,
  taxRate,
  startingCapital,
}) => {
  const labels = useMemo(() => data.equityCurve.map((p) => p.date), [data.equityCurve]);
  const series = useMemo(
    () => [
      { label: "Account value", values: data.equityCurve.map((p) => p.accountValue), color: "#1d4ed8", fill: true },
    ],
    [data.equityCurve],
  );
  const setups = Object.entries(data.bySetup);
  const lastPoint = data.equityCurve.at(-1);

  return (
    <>
      <section aria-label="Key figures" className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-3 gap-3">
        <KpiTile
          testId="kpi-account"
          icon={faWallet}
          label="Account value"
          value={formatMoney(data.accountValue)}
          detail={`${formatSignedPercent(data.returnPct)} on ${startingCapital !== undefined ? formatMoney(startingCapital) : "starting capital"}`}
        />
        <KpiTile
          testId="kpi-realized"
          icon={faDollarSign}
          label="Realized P&L"
          value={formatMoney(data.realizedPnl)}
          tone={data.realizedPnl > 0 ? "good" : data.realizedPnl < 0 ? "bad" : "neutral"}
          detail={`After tax set-aside: ${formatMoney(data.afterTaxPnl)}`}
        />
        <KpiTile
          testId="kpi-tax"
          icon={faPercent}
          label="Tax set-aside"
          value={formatMoney(data.taxSetAside)}
          detail={
            taxRate !== undefined ? `At ${formatPercent(taxRate)}, an assumption you set` : "An assumption you set"
          }
        />
        <KpiTile
          testId="kpi-open"
          icon={faBriefcase}
          label="Open positions"
          value={
            maxOpen !== undefined
              ? `${formatCount(data.openCount)} of ${formatCount(maxOpen)}`
              : formatCount(data.openCount)
          }
          tone={data.openCountOverLimit ? "bad" : "neutral"}
          detail={data.openCountOverLimit ? "Over the position limit" : "Against the limit in Settings"}
        />
        <KpiTile
          testId="kpi-capital"
          icon={faLock}
          label="Capital at work"
          value={formatMoney(data.capitalAtWork)}
          detail={`${formatPercent(data.capitalAtWorkPct)} of starting capital`}
        />
        <KpiTile
          testId="kpi-risk"
          icon={faTriangleExclamation}
          label="Risk at stops"
          value={formatMoney(data.riskAtStops)}
          detail={`${formatPercent(data.riskAtStopsPct)} of starting capital`}
        />
      </section>

      <div className="grid grid-cols-1 xl:grid-cols-3 gap-4">
        <section className="xl:col-span-2 rounded-xl bg-surface border border-border p-4" aria-label="Equity curve">
          <h2 className="text-base font-semibold mb-1">Equity curve</h2>
          {data.equityCurve.length < 2 ? (
            <EmptyBlock>The curve needs at least two closed trades.</EmptyBlock>
          ) : (
            <>
              <LineChart
                labels={labels}
                series={series}
                ariaLabel="Account value after each closed trade"
                formatValue={formatMoney}
              />
              <p className="text-meta text-content-muted mt-1">
                Account value after each closed trade, through {lastPoint ? formatDate(lastPoint.date) : EMPTY}.
              </p>
            </>
          )}
        </section>

        <section
          className="rounded-xl bg-surface border border-border p-4"
          aria-label="Benchmark comparison"
          data-testid="spy-panel"
        >
          <h2 className="text-base font-semibold mb-2">You vs SPY</h2>
          {data.spyReturnPct === null ? (
            <p className="text-sm text-content-muted" data-testid="spy-missing">
              No SPY comparison yet: it needs a first trade and SPY prices.
            </p>
          ) : (
            <dl className="grid grid-cols-1 gap-3 m-0">
              <Metric label="Your return" value={formatSignedPercent(data.returnPct)} />
              <Metric
                label="SPY, first trade to latest close"
                value={formatSignedPercent(data.spyReturnPct)}
                testId="spy-return"
              />
              <Metric
                label="You minus SPY"
                value={<span className={pnlClass(data.youMinusSpy)}>{formatSignedPercent(data.youMinusSpy)}</span>}
                testId="you-minus-spy"
              />
            </dl>
          )}
        </section>
      </div>

      <section className="rounded-xl bg-surface border border-border p-4" aria-label="Performance">
        <h2 className="text-base font-semibold mb-3">Performance</h2>
        {data.closedCount === 0 ? (
          <EmptyBlock>No closed trades yet, so there are no performance figures to show.</EmptyBlock>
        ) : (
          <dl className="grid grid-cols-2 md:grid-cols-4 gap-4 m-0" data-testid="performance">
            <Metric label="Closed trades" value={formatCount(data.closedCount)} />
            <Metric label="Win rate" value={formatPercent(data.winRate)} />
            <Metric label="Average win" value={formatMoney(data.avgWin)} />
            <Metric label="Average loss" value={data.avgLoss === null ? "None yet" : formatMoney(data.avgLoss)} />
            <Metric
              label="Profit factor"
              value={data.profitFactor === null ? "n/a" : formatRatio(data.profitFactor)}
              note={data.profitFactor === null ? "No losing trades yet" : undefined}
              testId="profit-factor"
            />
            <Metric label="Average R" value={formatR(data.avgR)} />
            <Metric label="Plan adherence" value={formatPercent(data.planAdherence)} />
            <Metric label="Average days held" value={formatDays(data.avgDaysHeld)} />
          </dl>
        )}
      </section>

      <section className="rounded-xl bg-surface border border-border p-4" aria-label="Results by setup">
        <h2 className="text-base font-semibold mb-3">Results by setup</h2>
        {setups.length === 0 ? (
          <EmptyBlock>No closed trades to group yet.</EmptyBlock>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm" data-testid="by-setup">
              <thead>
                <tr className="text-left text-content-muted">
                  <th className="py-1 pr-4 font-medium">Setup</th>
                  <th className="py-1 pr-4 font-medium text-right">Trades</th>
                  <th className="py-1 pr-4 font-medium text-right">Win rate</th>
                  <th className="py-1 font-medium text-right">Net P&amp;L</th>
                </tr>
              </thead>
              <tbody>
                {setups.map(([setup, stats]) => (
                  <tr key={setup} className="border-t border-border">
                    <td className="py-1 pr-4">{SETUP_LABELS[setup] ?? setup}</td>
                    <td className="py-1 pr-4 text-right font-numeric">{formatCount(stats.trades)}</td>
                    <td className="py-1 pr-4 text-right font-numeric">{formatPercent(stats.winRate)}</td>
                    <td className={`py-1 text-right font-numeric ${pnlClass(stats.netPnl)}`}>
                      {formatMoney(stats.netPnl)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <p className="text-meta text-content-muted">
        Cash available for new trades: {formatMoney(data.availableCash)} (planned orders reserve{" "}
        {formatMoney(data.reservedByPlanned)}). The tax figure is an assumption, not tax advice.
      </p>
    </>
  );
};
