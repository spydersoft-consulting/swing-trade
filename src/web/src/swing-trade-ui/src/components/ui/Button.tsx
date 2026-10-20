import { Button as AriaButton, type ButtonProps as AriaButtonProps } from "react-aria-components";
import { faSpinner } from "@fortawesome/free-solid-svg-icons";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";

export type ButtonVariant = "solid" | "text" | "outlined";
export type ButtonSeverity = "primary" | "danger";
export type ButtonSize = "sm" | "md" | "lg";

export interface ButtonProps extends Omit<AriaButtonProps, "children" | "className" | "isDisabled"> {
  label?: string;
  icon?: React.ReactNode;
  iconPos?: "left" | "right";
  loading?: boolean;
  disabled?: boolean;
  variant?: ButtonVariant;
  severity?: ButtonSeverity;
  size?: ButtonSize;
  className?: string;
  children?: React.ReactNode;
}

const base =
  "inline-flex items-center justify-center gap-2 rounded-md font-medium transition-colors cursor-pointer " +
  "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand " +
  "disabled:opacity-60 disabled:cursor-not-allowed";

const sizes: Record<ButtonSize, string> = {
  sm: "px-3 py-1.5 text-sm min-h-9",
  md: "px-4 py-2 text-base min-h-10",
  lg: "px-5 py-3 text-lg min-h-12",
};

const palette: Record<ButtonSeverity, Record<ButtonVariant, string>> = {
  primary: {
    solid: "bg-brand text-brand-fg hover:bg-brand-hover border border-brand",
    outlined: "bg-transparent text-brand border border-brand hover:bg-brand-tint",
    text: "bg-transparent text-brand border border-transparent hover:bg-brand-tint",
  },
  danger: {
    solid: "bg-bad text-white hover:bg-bad/90 border border-bad",
    outlined: "bg-transparent text-bad border border-bad hover:bg-bad-tint",
    text: "bg-transparent text-bad border border-transparent hover:bg-bad-tint",
  },
};

export const Button: React.FC<ButtonProps> = ({
  label,
  icon,
  iconPos = "left",
  loading = false,
  disabled = false,
  variant = "solid",
  severity = "primary",
  size = "md",
  className,
  children,
  ...rest
}) => {
  const classes = [base, sizes[size], palette[severity][variant], className].filter(Boolean).join(" ");
  const iconOnLeft = iconPos === "left";
  const leading = loading ? <FontAwesomeIcon icon={faSpinner} spin /> : iconOnLeft && icon;
  const trailing = !loading && !iconOnLeft && icon;

  return (
    <AriaButton {...rest} isDisabled={disabled || loading} className={classes}>
      {leading}
      {label ?? children}
      {trailing}
    </AriaButton>
  );
};
