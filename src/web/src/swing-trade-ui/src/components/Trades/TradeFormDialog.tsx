import React, { useEffect, useMemo, useState } from "react";
import { Dialog } from "primereact/dialog";
import { Button } from "../ui/Button";
import type { TradeDto, TradeSetup, TradeStatus } from "../../api/generated/types.gen";
import { SelectField, TextAreaField, TextField } from "../common/Fields";
import { InlineMessage } from "../common/QueryState";
import { SizerResultPanel } from "../Sizer/SizerResultPanel";
import { SETUP_OPTIONS } from "../Sizer/setupOptions";
import { useSizer } from "../Sizer/useSizer";
import { useToast } from "../../context";
import { useAppDispatch, useAppSelector } from "../../store/hooks";
import { createTrade, updateTrade } from "../../store/slices/tradesSlice";
import { fetchWatchlist } from "../../store/slices/watchlistSlice";
import { parseDecimal } from "../../utils/decimal";
import { todayInNewYork } from "../../utils/dates";

interface Props {
  /** The trade being edited, or null to plan a new one. */
  trade: TradeDto | null;
  visible: boolean;
  onHide: () => void;
}

export const TradeFormDialog: React.FC<Props> = ({ trade, visible, onHide }) => {
  if (!visible) return null;
  return <TradeForm key={trade?.id ?? "new"} trade={trade} onHide={onHide} />;
};

const NEW_STATUSES: readonly { value: "Planned" | "Open"; label: string }[] = [
  { value: "Planned", label: "Planned (limit order working)" },
  { value: "Open", label: "Open (already filled)" },
];

const textOf = (n: number | null | undefined) => (n === null || n === undefined ? "" : String(n));

const TradeForm: React.FC<{ trade: TradeDto | null; onHide: () => void }> = ({ trade, onHide }) => {
  const dispatch = useAppDispatch();
  const toast = useToast();
  const rows = useAppSelector((s) => s.watchlist.rows);
  const editing = trade !== null;

  const [ticker, setTicker] = useState(trade?.ticker ?? "");
  const [status, setStatus] = useState<TradeStatus>(trade?.status ?? "Planned");
  const [setup, setSetup] = useState<TradeSetup>(trade?.setup ?? "Pullback");
  const [openDate, setOpenDate] = useState(trade?.openDate ?? "");
  const [shares, setShares] = useState(textOf(trade?.shares));
  const [entry, setEntry] = useState(textOf(trade?.entryPrice));
  const [stop, setStop] = useState(textOf(trade?.stopPrice));
  const [target, setTarget] = useState(textOf(trade?.targetPrice));
  const [exitPrice, setExitPrice] = useState(textOf(trade?.exitPrice));
  const [exitDate, setExitDate] = useState(trade?.exitDate ?? "");
  const [followedPlan, setFollowedPlan] = useState<boolean>(trade?.followedPlan ?? true);
  const [rationale, setRationale] = useState(trade?.rationale ?? "");
  const [lessons, setLessons] = useState(trade?.lessons ?? "");
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (rows.length === 0) void dispatch(fetchWatchlist());
  }, [dispatch, rows.length]);

  const tickerOptions = useMemo(() => {
    const tickers = new Set(rows.map((r) => r.ticker));
    if (trade) tickers.add(trade.ticker);
    return [{ value: "", label: "Choose a ticker" }, ...[...tickers].sort().map((t) => ({ value: t, label: t }))];
  }, [rows, trade]);

  // The live sizer is for planning a new trade; an edited trade's own cost is already counted in available cash.
  const sizer = useSizer(editing ? { entry: "", stop: "", target: "" } : { ticker, entry, stop, target });

  const showOpenDate = status === "Open" || status === "Closed";
  const showExit = status === "Closed";

  const setNewStatus = (value: "Planned" | "Open") => {
    setStatus(value);
    if (value === "Open" && !openDate) setOpenDate(todayInNewYork());
  };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    const local: Record<string, string> = {};
    const num = (text: string, field: string, label: string): number => {
      const value = parseDecimal(text);
      if (value === null) local[field] = `Enter ${label} as a number.`;
      return value ?? 0;
    };
    const sharesN = num(shares, "shares", "the number of shares");
    const entryN = num(entry, "entryPrice", "the entry price");
    const stopN = num(stop, "stopPrice", "the stop price");
    const targetN = num(target, "targetPrice", "the target price");
    let exitN: number | null = null;
    if (showExit) exitN = num(exitPrice, "exitPrice", "the exit price");
    if (Object.keys(local).length > 0) {
      setFieldErrors(local);
      return;
    }

    setSaving(true);
    setFieldErrors({});
    setFormError(null);
    const result = trade
      ? await dispatch(
          updateTrade({
            id: trade.id,
            body: {
              ticker: trade.ticker,
              setup,
              openDate: showOpenDate ? openDate || null : null,
              shares: sharesN,
              entryPrice: entryN,
              stopPrice: stopN,
              targetPrice: targetN,
              exitPrice: showExit ? exitN : null,
              exitDate: showExit ? exitDate || null : null,
              followedPlan: showExit ? followedPlan : (trade.followedPlan ?? null),
              rationale: rationale.trim() || null,
              lessons: showExit ? lessons.trim() || null : (trade.lessons ?? null),
            },
          }),
        )
      : await dispatch(
          createTrade({
            ticker: ticker.trim().toUpperCase(),
            status,
            setup,
            openDate: status === "Open" ? openDate || null : null,
            shares: sharesN,
            entryPrice: entryN,
            stopPrice: stopN,
            targetPrice: targetN,
            rationale: rationale.trim() || null,
          }),
        );
    setSaving(false);

    if (result.meta.requestStatus === "fulfilled") {
      toast.success(editing ? "Trade updated" : "Trade created", editing ? trade?.ticker : ticker.toUpperCase());
      onHide();
    } else if ("payload" in result && result.payload && typeof result.payload === "object") {
      const problem = result.payload as { fieldErrors: Record<string, string>; message: string; status: number };
      setFieldErrors(problem.fieldErrors);
      setFormError(problem.message);
      if (problem.status === 409) toast.warn("That change is not allowed", problem.message);
    }
  };

  return (
    <Dialog
      header={editing ? `Edit ${trade?.ticker}` : "New trade"}
      visible
      onHide={onHide}
      style={{ width: "min(54rem, 96vw)" }}
      modal
    >
      <form onSubmit={(e) => void submit(e)} noValidate>
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div className="space-y-3">
            {editing ? (
              <TextField id="trade-ticker" label="Ticker" value={ticker} onChange={setTicker} disabled />
            ) : (
              <SelectField
                id="trade-ticker"
                label="Ticker"
                value={ticker}
                options={tickerOptions}
                onChange={setTicker}
                error={fieldErrors.ticker}
              />
            )}
            {!editing && (
              <SelectField
                id="trade-status"
                label="Status"
                value={status as "Planned" | "Open"}
                options={NEW_STATUSES}
                onChange={setNewStatus}
                error={fieldErrors.status}
              />
            )}
            <SelectField
              id="trade-setup"
              label="Setup"
              value={setup}
              options={SETUP_OPTIONS}
              onChange={setSetup}
              error={fieldErrors.setup}
            />
            {showOpenDate && (
              <TextField
                id="trade-open-date"
                label="Open date"
                type="date"
                value={openDate}
                onChange={setOpenDate}
                error={fieldErrors.openDate}
              />
            )}
            <div className="grid grid-cols-2 gap-3">
              <TextField
                id="trade-entry"
                label={status === "Planned" ? "Entry (limit price)" : "Entry price"}
                value={entry}
                onChange={setEntry}
                inputMode="decimal"
                error={fieldErrors.entryPrice}
              />
              <TextField
                id="trade-stop"
                label="Stop price"
                value={stop}
                onChange={setStop}
                inputMode="decimal"
                error={fieldErrors.stopPrice}
              />
              <TextField
                id="trade-target"
                label="Target price"
                value={target}
                onChange={setTarget}
                inputMode="decimal"
                error={fieldErrors.targetPrice}
              />
              <TextField
                id="trade-shares"
                label="Shares"
                value={shares}
                onChange={setShares}
                inputMode="numeric"
                error={fieldErrors.shares}
              />
            </div>
            {showExit && (
              <div className="grid grid-cols-2 gap-3">
                <TextField
                  id="trade-exit-price"
                  label="Exit price"
                  value={exitPrice}
                  onChange={setExitPrice}
                  inputMode="decimal"
                  error={fieldErrors.exitPrice}
                />
                <TextField
                  id="trade-exit-date"
                  label="Exit date"
                  type="date"
                  value={exitDate}
                  onChange={setExitDate}
                  error={fieldErrors.exitDate}
                />
                <label className="flex items-center gap-2 text-sm col-span-2">
                  <input type="checkbox" checked={followedPlan} onChange={(e) => setFollowedPlan(e.target.checked)} />I
                  followed my plan
                </label>
              </div>
            )}
            <TextAreaField
              id="trade-rationale"
              label="Rationale"
              value={rationale}
              onChange={setRationale}
              maxLength={4000}
              error={fieldErrors.rationale}
            />
            {showExit && (
              <TextAreaField
                id="trade-lessons"
                label="Lessons"
                value={lessons}
                onChange={setLessons}
                maxLength={4000}
                error={fieldErrors.lessons}
              />
            )}
          </div>

          <aside className="rounded-xl bg-surface-muted border border-border p-3 h-fit" aria-label="Live sizer">
            <h3 className="text-sm font-semibold mb-2">{editing ? "Computed on save" : "Sizer (live)"}</h3>
            {editing ? (
              <p className="text-sm text-content-muted">
                Position, risk, reward:risk and flags are recalculated by the server when you save.
              </p>
            ) : (
              <>
                <SizerResultPanel state={sizer} compact />
                {sizer.result && sizer.result.shares > 0 && (
                  <Button
                    type="button"
                    size="sm"
                    variant="outlined"
                    className="mt-3"
                    label={`Use ${sizer.result.shares} shares`}
                    onClick={() => setShares(String(sizer.result?.shares ?? ""))}
                  />
                )}
              </>
            )}
          </aside>
        </div>

        {formError && Object.keys(fieldErrors).length === 0 && (
          <div className="mt-3">
            <InlineMessage severity="error" testId="trade-form-error">
              {formError}
            </InlineMessage>
          </div>
        )}
        {formError && Object.keys(fieldErrors).length > 0 && (
          <p className="mt-3 text-sm text-bad" role="alert">
            {formError}
          </p>
        )}
        <div className="flex justify-end gap-2 pt-4">
          <Button type="button" label="Cancel" variant="outlined" onClick={onHide} />
          <Button type="submit" label={editing ? "Save changes" : "Create trade"} loading={saving} />
        </div>
      </form>
    </Dialog>
  );
};
