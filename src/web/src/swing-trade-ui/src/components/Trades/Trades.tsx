import React, { useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { DataTable } from "primereact/datatable";
import { Column } from "primereact/column";
import { Button } from "primereact/button";
import { ConfirmDialog, confirmDialog } from "primereact/confirmdialog";
import type { TradeDto, TradeStatus } from "../../api/generated/types.gen";
import { PageHeader } from "../layout/PageHeader";
import { EmptyBlock, ErrorBlock, LoadingBlock } from "../common/QueryState";
import { Pill, type Tone } from "../common/Badges";
import { TradeFlags } from "./tradeFlags";
import { tradeRowClass } from "./tradeRowClass";
import { TradeFormDialog } from "./TradeFormDialog";
import { CloseDialog, FillDialog } from "./TradeActionDialogs";
import { useAuth, useToast } from "../../context";
import { useAppDispatch, useAppSelector } from "../../store/hooks";
import { cancelTrade, fetchTrades } from "../../store/slices/tradesSlice";
import {
  formatCount,
  formatDate,
  formatMoney,
  formatPercent,
  formatR,
  formatRatio,
  pnlClass,
} from "../../utils/format";

type Tab = "planned" | "open" | "closed" | "all";

const TABS: { key: Tab; label: string; status?: TradeStatus }[] = [
  { key: "planned", label: "Planned", status: "Planned" },
  { key: "open", label: "Open", status: "Open" },
  { key: "closed", label: "Closed", status: "Closed" },
  { key: "all", label: "All" },
];

const statusTone = (status: TradeStatus): Tone => {
  if (status === "Open") return "good";
  if (status === "Cancelled") return "neutral";
  return "neutral";
};

const money = (v: number | null) => <span className="font-numeric">{formatMoney(v)}</span>;

export const Trades: React.FC = () => {
  const dispatch = useAppDispatch();
  const toast = useToast();
  const { canWrite } = useAuth();
  const { items, status, error } = useAppSelector((s) => s.trades);
  const [params, setParams] = useSearchParams();
  const requested = params.get("tab");
  const tab: Tab = TABS.some((t) => t.key === requested) ? (requested as Tab) : "open";
  const [formTrade, setFormTrade] = useState<TradeDto | null>(null);
  const [formOpen, setFormOpen] = useState(false);
  const [fillTarget, setFillTarget] = useState<TradeDto | null>(null);
  const [closeTarget, setCloseTarget] = useState<TradeDto | null>(null);

  useEffect(() => {
    void dispatch(fetchTrades());
  }, [dispatch]);

  const counts = useMemo(() => {
    const result: Record<Tab, number> = { planned: 0, open: 0, closed: 0, all: items.length };
    for (const t of items) {
      if (t.status === "Planned") result.planned++;
      else if (t.status === "Open") result.open++;
      else if (t.status === "Closed") result.closed++;
    }
    return result;
  }, [items]);

  const visible = useMemo(() => {
    const wanted = TABS.find((t) => t.key === tab)?.status;
    return wanted ? items.filter((t) => t.status === wanted) : items;
  }, [items, tab]);

  const openForm = (trade: TradeDto | null) => {
    setFormTrade(trade);
    setFormOpen(true);
  };

  const askCancel = (trade: TradeDto) =>
    confirmDialog({
      message: `Cancel the planned ${trade.ticker} order? The limit order is no longer working and the reserved cash is released.`,
      header: "Cancel planned trade",
      icon: "pi pi-exclamation-triangle",
      acceptLabel: "Cancel the trade",
      rejectLabel: "Keep it",
      accept: async () => {
        const result = await dispatch(cancelTrade(trade.id));
        if (cancelTrade.fulfilled.match(result)) toast.success("Trade cancelled", trade.ticker);
        else if (result.payload?.status === 409) toast.warn("Cannot cancel", result.payload.message);
        else toast.error("Could not cancel", result.payload?.message);
      },
    });

  const showStatusColumn = tab === "all";

  const actionsFor = (t: TradeDto) => {
    if (!canWrite) return null;
    return (
      <span className="inline-flex gap-1 whitespace-nowrap">
        {t.status === "Planned" && (
          <>
            <Button
              type="button"
              label="Fill"
              size="small"
              outlined
              aria-label={`Fill ${t.ticker}`}
              onClick={() => setFillTarget(t)}
            />
            <Button
              type="button"
              label="Edit"
              size="small"
              text
              aria-label={`Edit ${t.ticker}`}
              onClick={() => openForm(t)}
            />
            <Button
              type="button"
              label="Cancel"
              size="small"
              text
              severity="danger"
              aria-label={`Cancel ${t.ticker}`}
              onClick={() => askCancel(t)}
            />
          </>
        )}
        {t.status === "Open" && (
          <>
            <Button
              type="button"
              label="Close"
              size="small"
              outlined
              aria-label={`Close ${t.ticker}`}
              onClick={() => setCloseTarget(t)}
            />
            <Button
              type="button"
              label="Edit"
              size="small"
              text
              aria-label={`Edit ${t.ticker}`}
              onClick={() => openForm(t)}
            />
          </>
        )}
        {t.status === "Closed" && (
          <Button
            type="button"
            label="Edit"
            size="small"
            text
            aria-label={`Edit ${t.ticker}`}
            onClick={() => openForm(t)}
          />
        )}
      </span>
    );
  };

  return (
    <div className="space-y-4">
      <PageHeader
        title="Trades"
        subtitle="Position, risk and reward:risk are computed by the server from your settings."
        actions={canWrite && <Button label="New trade" icon="pi pi-plus" size="small" onClick={() => openForm(null)} />}
      />

      <div role="tablist" aria-label="Trade status" className="flex flex-wrap gap-1 border-b border-border">
        {TABS.map((t) => (
          <button
            key={t.key}
            role="tab"
            type="button"
            id={`tab-${t.key}`}
            aria-selected={tab === t.key}
            aria-controls="trades-panel"
            onClick={() => setParams({ tab: t.key }, { replace: true })}
            className={[
              "chip-toggle px-4 py-2 text-sm font-medium border-0 border-b-2 bg-transparent cursor-pointer",
              tab === t.key ? "border-brand text-brand" : "border-transparent text-content-muted hover:text-content",
            ].join(" ")}
          >
            {t.label} <span className="font-numeric text-meta">{counts[t.key]}</span>
          </button>
        ))}
      </div>

      <div id="trades-panel" role="tabpanel" aria-labelledby={`tab-${tab}`}>
        {status === "loading" && items.length === 0 && <LoadingBlock label="Loading trades" />}
        {status === "failed" && (
          <ErrorBlock problem={error} onRetry={() => void dispatch(fetchTrades())} title="Could not load trades" />
        )}
        {status === "succeeded" && visible.length === 0 && (
          <EmptyBlock>
            {tab === "all" ? "No trades yet." : `No ${tab} trades.`}{" "}
            {canWrite ? "Use New trade, or size one from the Watchlist." : ""}
          </EmptyBlock>
        )}
        {visible.length > 0 && (
          <div className="rounded-xl bg-surface border border-border overflow-hidden">
            <DataTable
              value={visible}
              dataKey="id"
              size="small"
              scrollable
              removableSort
              rowClassName={tradeRowClass}
              aria-label="Trades"
            >
              <Column
                field="ticker"
                header="Position"
                sortable
                frozen
                body={(t: TradeDto) => (
                  <span>
                    <span className="font-semibold">{t.ticker}</span>{" "}
                    <span className="text-meta text-content-muted">
                      {t.shares} sh &middot; {t.setup}
                    </span>
                    <br />
                    <span className="text-meta text-content-muted">{formatDate(t.openDate)}</span>
                  </span>
                )}
              />
              {showStatusColumn && (
                <Column
                  field="status"
                  header="Status"
                  sortable
                  body={(t: TradeDto) => <Pill tone={statusTone(t.status)}>{t.status}</Pill>}
                />
              )}
              <Column
                field="entryPrice"
                header="Entry / stop / target"
                sortable
                body={(t: TradeDto) => (
                  <span className="font-numeric whitespace-nowrap">
                    {formatMoney(t.entryPrice)} / {formatMoney(t.stopPrice)} / {formatMoney(t.targetPrice)}
                  </span>
                )}
              />
              <Column
                field="position"
                header="Position $"
                align="right"
                sortable
                body={(t: TradeDto) => money(t.position)}
              />
              <Column
                field="riskDollars"
                header="Risk $"
                align="right"
                sortable
                body={(t: TradeDto) => money(t.riskDollars)}
              />
              <Column
                field="riskPct"
                header="Risk %"
                align="right"
                sortable
                body={(t: TradeDto) => <span className="font-numeric">{formatPercent(t.riskPct)}</span>}
              />
              <Column
                field="plannedRewardRisk"
                header="Planned R:R"
                align="right"
                sortable
                body={(t: TradeDto) => <span className="font-numeric">{formatRatio(t.plannedRewardRisk)}</span>}
              />
              <Column
                field="pnl"
                header="P&L"
                align="right"
                sortable
                body={(t: TradeDto) => <span className={`font-numeric ${pnlClass(t.pnl)}`}>{formatMoney(t.pnl)}</span>}
              />
              <Column
                field="rMultiple"
                header="R-multiple"
                align="right"
                sortable
                body={(t: TradeDto) => (
                  <span className={`font-numeric ${pnlClass(t.rMultiple)}`}>{formatR(t.rMultiple)}</span>
                )}
              />
              <Column
                field="daysHeld"
                header="Days held"
                align="right"
                sortable
                body={(t: TradeDto) => formatCount(t.daysHeld)}
              />
              <Column header="Flags" body={(t: TradeDto) => <TradeFlags trade={t} />} />
              {canWrite && <Column header="Actions" frozen alignFrozen="right" body={actionsFor} />}
            </DataTable>
          </div>
        )}
      </div>

      <TradeFormDialog trade={formTrade} visible={formOpen} onHide={() => setFormOpen(false)} />
      <FillDialog trade={fillTarget} onHide={() => setFillTarget(null)} />
      <CloseDialog trade={closeTarget} onHide={() => setCloseTarget(null)} />
      <ConfirmDialog />
    </div>
  );
};
