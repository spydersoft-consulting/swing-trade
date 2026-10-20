import { ProgressBar } from "react-aria-components";

interface SpinnerProps {
  size?: number;
  className?: string;
}

export const Spinner: React.FC<SpinnerProps> = ({ size = 50, className }) => (
  <ProgressBar isIndeterminate aria-label="Loading" className={className}>
    <svg
      viewBox="0 0 50 50"
      width={size}
      height={size}
      className="animate-spin text-brand"
      fill="none"
      stroke="currentColor"
      strokeWidth="4"
      strokeLinecap="round"
      aria-hidden="true"
    >
      <circle cx="25" cy="25" r="20" strokeDasharray="90 150" />
    </svg>
  </ProgressBar>
);
