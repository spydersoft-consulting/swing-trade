import React, { useState } from "react";
import { Modal } from "../ui/Modal";
import { Button } from "../ui/Button";
import type { NoteKind } from "../../api/generated/types.gen";
import { SelectField, TextAreaField, TextField } from "../common/Fields";
import { InlineMessage } from "../common/QueryState";
import { useToast } from "../../context";
import { useAppDispatch } from "../../store/hooks";
import { createNote } from "../../store/slices/notesSlice";
import { todayInNewYork } from "../../utils/dates";

interface Props {
  visible: boolean;
  /** "Pass" opens the Record a pass form; anything else is a general note form. */
  kind: NoteKind;
  onHide: () => void;
}

const KIND_OPTIONS: readonly { value: NoteKind; label: string }[] = [
  { value: "Pass", label: "Pass (no trade this week)" },
  { value: "Review", label: "Review" },
  { value: "General", label: "General" },
];

export const NoteDialog: React.FC<Props> = ({ visible, kind, onHide }) => {
  if (!visible) return null;
  return <NoteForm key={kind} initialKind={kind} onHide={onHide} />;
};

const NoteForm: React.FC<{ initialKind: NoteKind; onHide: () => void }> = ({ initialKind, onHide }) => {
  const dispatch = useAppDispatch();
  const toast = useToast();
  const [kind, setKind] = useState<NoteKind>(initialKind);
  const [date, setDate] = useState(todayInNewYork());
  const [ticker, setTicker] = useState("");
  const [text, setText] = useState("");
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const isPass = initialKind === "Pass";

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    setFieldErrors({});
    setFormError(null);
    const result = await dispatch(
      createNote({ date, kind, ticker: ticker.trim().toUpperCase() || null, text: text.trim() }),
    );
    setSaving(false);
    if (createNote.fulfilled.match(result)) {
      toast.success(kind === "Pass" ? "Pass recorded" : "Note saved");
      onHide();
    } else if (result.payload) {
      setFieldErrors(result.payload.fieldErrors);
      setFormError(result.payload.message);
    }
  };

  return (
    <Modal header={isPass ? "Record a pass" : "Add a note"} onClose={onHide} width="min(30rem, 95vw)">
      <form onSubmit={(e) => void submit(e)} className="space-y-3" noValidate>
        {isPass && (
          <p className="text-sm text-content-muted">A week with no trade is a decision. Say why, in a sentence.</p>
        )}
        <TextField id="note-date" label="Date" type="date" value={date} onChange={setDate} error={fieldErrors.date} />
        {!isPass && (
          <SelectField
            id="note-kind"
            label="Kind"
            value={kind}
            options={KIND_OPTIONS}
            onChange={setKind}
            error={fieldErrors.kind}
          />
        )}
        <TextField
          id="note-ticker"
          label="Ticker (optional)"
          value={ticker}
          onChange={(v) => setTicker(v.toUpperCase())}
          maxLength={16}
          error={fieldErrors.ticker}
        />
        <TextAreaField
          id="note-text"
          label={isPass ? "Why no trade?" : "Note"}
          value={text}
          onChange={setText}
          maxLength={4000}
          error={fieldErrors.text}
        />
        {formError && Object.keys(fieldErrors).length === 0 && (
          <InlineMessage severity="error">{formError}</InlineMessage>
        )}
        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" label="Cancel" variant="outlined" onClick={onHide} />
          <Button type="submit" label={isPass ? "Record pass" : "Save note"} loading={saving} />
        </div>
      </form>
    </Modal>
  );
};
