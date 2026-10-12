import React from "react";
import { InputText } from "primereact/inputtext";

interface FieldShellProps {
  id: string;
  label: string;
  error?: string;
  hint?: string;
  children: React.ReactNode;
}

/** A label, a control and its field error, wired together for assistive technology. */
export const FieldShell: React.FC<FieldShellProps> = ({ id, label, error, hint, children }) => (
  <div className="flex flex-col gap-1 min-w-0">
    <label htmlFor={id} className="text-sm font-medium text-content">
      {label}
    </label>
    {children}
    {hint && !error && (
      <small id={`${id}-hint`} className="text-meta text-content-muted">
        {hint}
      </small>
    )}
    {error && (
      <small id={`${id}-error`} role="alert" className="text-meta text-bad">
        {error}
      </small>
    )}
  </div>
);

interface TextFieldProps {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  error?: string;
  hint?: string;
  type?: "text" | "date" | "number";
  inputMode?: "decimal" | "numeric" | "text";
  disabled?: boolean;
  readOnly?: boolean;
  placeholder?: string;
  maxLength?: number;
  autoFocus?: boolean;
}

export const TextField: React.FC<TextFieldProps> = ({
  id,
  label,
  value,
  onChange,
  error,
  hint,
  type = "text",
  inputMode,
  disabled,
  readOnly,
  placeholder,
  maxLength,
  autoFocus,
}) => (
  <FieldShell id={id} label={label} error={error} hint={hint}>
    <InputText
      id={id}
      type={type}
      value={value}
      inputMode={inputMode}
      disabled={disabled}
      readOnly={readOnly}
      placeholder={placeholder}
      maxLength={maxLength}
      autoFocus={autoFocus}
      invalid={!!error}
      aria-invalid={!!error}
      aria-describedby={error ? `${id}-error` : hint ? `${id}-hint` : undefined}
      onChange={(e) => onChange(e.target.value)}
      className="w-full"
    />
  </FieldShell>
);

interface SelectFieldProps<T extends string> {
  id: string;
  label: string;
  value: T;
  options: readonly { value: T; label: string }[];
  onChange: (value: T) => void;
  error?: string;
  disabled?: boolean;
}

/** A native select: dependable on touch devices and for assistive technology. */
export function SelectField<T extends string>({
  id,
  label,
  value,
  options,
  onChange,
  error,
  disabled,
}: SelectFieldProps<T>) {
  return (
    <FieldShell id={id} label={label} error={error}>
      <select
        id={id}
        value={value}
        disabled={disabled}
        aria-invalid={!!error}
        aria-describedby={error ? `${id}-error` : undefined}
        onChange={(e) => onChange(e.target.value as T)}
        className="w-full rounded-md border border-border-strong bg-surface px-3 py-2 text-sm"
      >
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
    </FieldShell>
  );
}

interface TextAreaFieldProps {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  error?: string;
  rows?: number;
  maxLength?: number;
  disabled?: boolean;
}

export const TextAreaField: React.FC<TextAreaFieldProps> = ({
  id,
  label,
  value,
  onChange,
  error,
  rows = 3,
  maxLength,
  disabled,
}) => (
  <FieldShell id={id} label={label} error={error}>
    <textarea
      id={id}
      value={value}
      rows={rows}
      maxLength={maxLength}
      disabled={disabled}
      aria-invalid={!!error}
      aria-describedby={error ? `${id}-error` : undefined}
      onChange={(e) => onChange(e.target.value)}
      className="w-full rounded-md border border-border-strong bg-surface px-3 py-2 text-sm"
    />
  </FieldShell>
);
