import React, { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { Button } from "primereact/button";
import { PageHeader } from "../layout/PageHeader";
import { SelectField, TextAreaField, TextField } from "../common/Fields";
import { InlineMessage } from "../common/QueryState";
import { SizerResultPanel } from "./SizerResultPanel";
import { useSizer } from "./useSizer";
import { useAuth, useToast } from "../../context";
import { useAppDispatch } from "../../store/hooks";
import { createTrade } from "../../store/slices/tradesSlice";
import { parseDecimal } from "../../utils/decimal";
import type { TradeSetup } from "../../api/generated/types.gen";
import { SETUP_OPTIONS } from "./setupOptions";

export const Sizer: React.FC = () => {
  const [params] = useSearchParams();
  const { canWrite } = useAuth();
  const toast = useToast();
  const dispatch = useAppDispatch();
  const [ticker, setTicker] = useState(params.get("ticker")?.toUpperCase() ?? "");
  const [entry, setEntry] = useState(params.get("entry") ?? "");
  const [stop, setStop] = useState("");
  const [target, setTarget] = useState("");
  const [setup, setSetup] = useState<TradeSetup>("Pullback");
  const [rationale, setRationale] = useState("");
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [createdId, setCreatedId] = useState<number | null>(null);

  // A link from the watchlist ("Plan a trade") can change the query while this screen is open.
  useEffect(() => {
    const t = params.get("ticker");
    const e = params.get("entry");
    if (t) setTicker(t.toUpperCase());
    if (e) setEntry(e);
  }, [params]);

  const sizer = useSizer({ ticker, entry, stop, target });
  const result = sizer.result;

  const whyNotCreate = !canWrite
    ? null
    : !ticker.trim()
      ? "Enter a ticker to create a planned trade."
      : !result
        ? "Waiting for a sizing result."
        : !result.isOk
          ? "The sizer verdict is not OK, so there is nothing to plan."
          : result.shares < 1
            ? "The sizer returned no shares."
            : null;

  const create = async () => {
    const entryPrice = parseDecimal(entry);
    const stopPrice = parseDecimal(stop);
    const targetPrice = parseDecimal(target);
    if (!result || entryPrice === null || stopPrice === null || targetPrice === null) return;
    setSaving(true);
    setSaveError(null);
    setFieldErrors({});
    const outcome = await dispatch(
      createTrade({
        ticker: ticker.trim().toUpperCase(),
        status: "Planned",
        setup,
        shares: result.shares,
        entryPrice,
        stopPrice,
        targetPrice,
        rationale: rationale.trim() || null,
      }),
    );
    setSaving(false);
    if (createTrade.fulfilled.match(outcome)) {
      setCreatedId(outcome.payload.id);
      toast.success(
        "Planned trade created",
        `${outcome.payload.ticker}: ${outcome.payload.shares} shares at ${outcome.payload.entryPrice}`,
      );
    } else if (outcome.payload) {
      setFieldErrors(outcome.payload.fieldErrors);
      setSaveError(outcome.payload.message);
    }
  };

  return (
    <div className="space-y-4">
      <PageHeader
        title="Sizer"
        subtitle="How many shares fit the risk, position cap and cash rules. Nothing is saved until you create a planned trade."
      />
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <section className="rounded-xl bg-surface border border-border p-4 space-y-3" aria-label="Trade inputs">
          <TextField
            id="sizer-ticker"
            label="Ticker (optional)"
            value={ticker}
            onChange={(v) => setTicker(v.toUpperCase())}
            maxLength={16}
          />
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
            <TextField id="sizer-entry" label="Entry" value={entry} onChange={setEntry} inputMode="decimal" />
            <TextField id="sizer-stop" label="Stop" value={stop} onChange={setStop} inputMode="decimal" />
            <TextField id="sizer-target" label="Target" value={target} onChange={setTarget} inputMode="decimal" />
          </div>
          {canWrite && (
            <>
              <SelectField
                id="sizer-setup"
                label="Setup"
                value={setup}
                options={SETUP_OPTIONS}
                onChange={setSetup}
                error={fieldErrors.setup}
              />
              <TextAreaField
                id="sizer-rationale"
                label="Rationale (optional)"
                value={rationale}
                onChange={setRationale}
                maxLength={4000}
                error={fieldErrors.rationale}
              />
              {saveError && <InlineMessage severity="error">{saveError}</InlineMessage>}
              {createdId !== null && (
                <InlineMessage severity="success" testId="created-message">
                  Planned trade saved. <Link to="/trades?tab=planned">See it in Trades</Link>.
                </InlineMessage>
              )}
              <div>
                <Button
                  type="button"
                  label="Create planned trade"
                  icon="pi pi-check"
                  loading={saving}
                  disabled={whyNotCreate !== null}
                  onClick={() => void create()}
                />
                {whyNotCreate && <p className="text-meta text-content-muted mt-1">{whyNotCreate}</p>}
              </div>
            </>
          )}
        </section>
        <section className="rounded-xl bg-surface border border-border p-4" aria-label="Sizing result">
          <h2 className="text-base font-semibold mb-2">Result</h2>
          <SizerResultPanel state={sizer} />
        </section>
      </div>
    </div>
  );
};
