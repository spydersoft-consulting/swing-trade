import { Input } from "react-aria-components";

const field =
  "w-full rounded-md border bg-surface px-3 py-2 text-base text-content placeholder:text-content-muted " +
  "focus:outline-2 focus:outline-offset-0 focus:outline-brand disabled:opacity-60";

type InputProps = Omit<React.InputHTMLAttributes<HTMLInputElement>, "className"> & {
  invalid?: boolean;
  className?: string;
};

export const InputText: React.FC<InputProps> = ({ invalid, className, ...rest }) => (
  <Input
    {...rest}
    aria-invalid={rest["aria-invalid"] ?? (invalid || undefined)}
    className={[field, invalid ? "border-bad" : "border-border-strong", className].filter(Boolean).join(" ")}
  />
);
