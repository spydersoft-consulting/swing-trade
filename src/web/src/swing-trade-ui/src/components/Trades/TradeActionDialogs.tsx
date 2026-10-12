import React, { useState } from "react";
import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import type { TradeDto } from "../../api/generated/types.gen";
import { TextAreaField, TextField } from "../common/Fields";
import { InlineMessage } from "../common/QueryState";
import { useToast } from "../../context";
import { useAppDispatch } from "../../store/hooks";
import { closeTrade, fillTrade } from "../../store/slices/tradesSlice";
import { parseDecimal } from "../../utils/decimal";
import { todayInNewYork } from "../../utils/dates";
import { formatMoney } from "../../utils/format";

interface DialogProps {
  trade: TradeDto | null;
  onHide: () => void;
}

/** Planned to Open: the fill price replaces the limit price as the entry. */
export const FillDialog: React.FC<DialogProps> = ({ trade, onHide }) => {
  if (!trade) return null;
  // Remount per trade so the form starts from that trade's values.
  return <FillForm key={trade.id} trade={trade} onHide={onHide} />;
};

const FillForm: React.FC<{ trade: TradeDto; onHide: () => void }> = ({ trade, onHide }) => {
  const dispatch = useAppDispatch();
  const toast = useToast();
  const [price, setPrice] = useState(String(trade.entryPrice));
  const [date, setDate] = useState(todayInNewYork());
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    const fillPrice = parseDecimal(price);
    if (fillPrice === null) {
      setFieldErrors({ fillPrice: "Enter the price you were filled at." });
      return;
    }
    setSaving(true);
    setFieldErrors({});
    setFormError(null);
    const result = await dispatch(fillTrade({ id: trade.id, body: { fillPrice, fillDate: date } }));
    setSaving(false);
    if (fillTrade.fulfilled.match(result)) {
      toast.success("Trade filled", `${trade.ticker} is now open.`);
      onHide();
    } else if (result.payload) {
      setFieldErrors(result.payload.fieldErrors);
      setFormError(result.payload.message);
      if (result.payload.status === 409) toast.warn("Cannot fill this trade", result.payload.message);
    }
  };

  return (
    <Dialog header={`Fill ${trade.ticker}`} visible onHide={onHide} style={{ width: "min(26rem, 95vw)" }} modal>
      <form onSubmit={(e) => void submit(e)} className="space-y-3" noValidate>
        <p className="text-sm text-content-muted">
          Limit price was {formatMoney(trade.entryPrice)}. Enter the price you actually paid.
        </p>
        <TextField
          id="fill-price"
          label="Fill price"
          value={price}
          onChange={setPrice}
          inputMode="decimal"
          error={fieldErrors.fillPrice}
          autoFocus
        />
        <TextField
          id="fill-date"
          label="Fill date"
          type="date"
          value={date}
          onChange={setDate}
          error={fieldErrors.fillDate}
        />
        {formError && Object.keys(fieldErrors).length === 0 && (
          <InlineMessage severity="error">{formError}</InlineMessage>
        )}
        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" label="Cancel" outlined onClick={onHide} />
          <Button type="submit" label="Mark as filled" loading={saving} />
        </div>
      </form>
    </Dialog>
  );
};

/** Open to Closed: exit price and date, whether the plan was followed, and what was learned. */
export const CloseDialog: React.FC<DialogProps> = ({ trade, onHide }) => {
  if (!trade) return null;
  return <CloseForm key={trade.id} trade={trade} onHide={onHide} />;
};

const CloseForm: React.FC<{ trade: TradeDto; onHide: () => void }> = ({ trade, onHide }) => {
  const dispatch = useAppDispatch();
  const toast = useToast();
  const [price, setPrice] = useState("");
  const [date, setDate] = useState(todayInNewYork());
  const [followedPlan, setFollowedPlan] = useState(true);
  const [lessons, setLessons] = useState("");
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    const exitPrice = parseDecimal(price);
    if (exitPrice === null) {
      setFieldErrors({ exitPrice: "Enter the exit price." });
      return;
    }
    setSaving(true);
    setFieldErrors({});
    setFormError(null);
    const result = await dispatch(
      closeTrade({ id: trade.id, body: { exitPrice, exitDate: date, followedPlan, lessons: lessons.trim() || null } }),
    );
    setSaving(false);
    if (closeTrade.fulfilled.match(result)) {
      toast.success("Trade closed", `${trade.ticker} is closed.`);
      onHide();
    } else if (result.payload) {
      setFieldErrors(result.payload.fieldErrors);
      setFormError(result.payload.message);
      if (result.payload.status === 409) toast.warn("Cannot close this trade", result.payload.message);
    }
  };

  return (
    <Dialog header={`Close ${trade.ticker}`} visible onHide={onHide} style={{ width: "min(28rem, 95vw)" }} modal>
      <form onSubmit={(e) => void submit(e)} className="space-y-3" noValidate>
        <p className="text-sm text-content-muted">
          {trade.shares} shares bought at {formatMoney(trade.entryPrice)}.
        </p>
        <TextField
          id="close-price"
          label="Exit price"
          value={price}
          onChange={setPrice}
          inputMode="decimal"
          error={fieldErrors.exitPrice}
          autoFocus
        />
        <TextField
          id="close-date"
          label="Exit date"
          type="date"
          value={date}
          onChange={setDate}
          error={fieldErrors.exitDate}
        />
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={followedPlan} onChange={(e) => setFollowedPlan(e.target.checked)} />I followed
          my plan
        </label>
        <TextAreaField
          id="close-lessons"
          label="Lessons (optional)"
          value={lessons}
          onChange={setLessons}
          maxLength={4000}
          error={fieldErrors.lessons}
        />
        {formError && Object.keys(fieldErrors).length === 0 && (
          <InlineMessage severity="error">{formError}</InlineMessage>
        )}
        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" label="Cancel" outlined onClick={onHide} />
          <Button type="submit" label="Close trade" loading={saving} />
        </div>
      </form>
    </Dialog>
  );
};
