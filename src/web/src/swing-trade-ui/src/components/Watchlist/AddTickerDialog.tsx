import React, { useState } from "react";
import { Modal } from "../ui/Modal";
import { Button } from "../ui/Button";
import { TextField } from "../common/Fields";
import { InlineMessage } from "../common/QueryState";
import { useToast } from "../../context";
import { useAppDispatch } from "../../store/hooks";
import { addInstrument } from "../../store/slices/watchlistSlice";

interface Props {
  visible: boolean;
  onHide: () => void;
}

export const AddTickerDialog: React.FC<Props> = ({ visible, onHide }) => {
  const dispatch = useAppDispatch();
  const toast = useToast();
  const [ticker, setTicker] = useState("");
  const [name, setName] = useState("");
  const [category, setCategory] = useState("");
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const close = () => {
    setTicker("");
    setName("");
    setCategory("");
    setFieldErrors({});
    setFormError(null);
    onHide();
  };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    setFieldErrors({});
    setFormError(null);
    const result = await dispatch(
      addInstrument({
        ticker: ticker.trim().toUpperCase(),
        name: name.trim(),
        category: category.trim() || null,
        active: true,
      }),
    );
    setSaving(false);
    if (addInstrument.fulfilled.match(result)) {
      toast.success(
        "Ticker added",
        `${result.payload.ticker} joins the watchlist after the next refresh brings its prices.`,
      );
      close();
    } else if (result.payload) {
      setFieldErrors(result.payload.fieldErrors);
      setFormError(result.payload.message);
    }
  };

  return (
    <Modal open={visible} header="Add ticker" onClose={close} width="min(28rem, 95vw)">
      <form onSubmit={(e) => void submit(e)} className="space-y-3" noValidate>
        <TextField
          id="add-ticker"
          label="Ticker"
          value={ticker}
          onChange={setTicker}
          error={fieldErrors.ticker}
          maxLength={16}
          autoFocus
        />
        <TextField
          id="add-name"
          label="Name"
          value={name}
          onChange={setName}
          error={fieldErrors.name}
          maxLength={200}
        />
        <TextField
          id="add-category"
          label="Category (optional)"
          value={category}
          onChange={setCategory}
          error={fieldErrors.category}
          maxLength={100}
        />
        {formError && Object.keys(fieldErrors).length === 0 && (
          <InlineMessage severity="error">{formError}</InlineMessage>
        )}
        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" label="Cancel" variant="outlined" onClick={close} />
          <Button type="submit" label="Add ticker" loading={saving} />
        </div>
      </form>
    </Modal>
  );
};
