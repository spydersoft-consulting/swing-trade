import React from "react";
import { Button } from "../ui/Button";
import { Spinner } from "../ui/Spinner";
import type { ApiProblem } from "../../api/problem";

export const LoadingBlock: React.FC<{ label?: string }> = ({ label = "Loading" }) => (
  <div className="flex justify-center py-10" role="status" aria-live="polite">
    <Spinner size={40} />
    <span className="sr-only">{label}</span>
  </div>
);

interface ErrorBlockProps {
  problem: ApiProblem | null;
  onRetry?: () => void;
  title?: string;
}

/** A load failure shown in place of the content, with a retry. 403 reads as a permission message, not a fault. */
export const ErrorBlock: React.FC<ErrorBlockProps> = ({ problem, onRetry, title = "Could not load this" }) => (
  <div
    role="alert"
    className="rounded-xl border border-bad/30 bg-bad-tint p-4 text-bad flex flex-wrap items-center gap-3"
  >
    <div className="min-w-0">
      <p className="font-medium">{problem?.status === 403 ? "Not permitted" : title}</p>
      <p className="text-sm">{problem?.message ?? "Something went wrong."}</p>
    </div>
    {onRetry && problem?.status !== 403 && (
      <Button label="Retry" size="sm" variant="outlined" severity="danger" onClick={onRetry} className="ml-auto" />
    )}
  </div>
);

export const EmptyBlock: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <div className="rounded-xl border border-dashed border-border-strong bg-surface p-6 text-center text-content-muted">
    {children}
  </div>
);

const inlineTone = {
  error: "border-bad/30 bg-bad-tint text-bad",
  warn: "border-warn/30 bg-warn-tint text-warn",
  info: "border-brand/30 bg-brand-tint text-content",
  success: "border-good/30 bg-good-tint text-good",
} as const;

/** An inline error or warning under a form or a panel. */
export const InlineMessage: React.FC<{
  severity: keyof typeof inlineTone;
  children: React.ReactNode;
  testId?: string;
}> = ({ severity, children, testId }) => (
  <div
    data-testid={testId}
    role={severity === "error" ? "alert" : "status"}
    className={`w-full rounded-md border px-3 py-2 text-sm ${inlineTone[severity]}`}
  >
    {children}
  </div>
);
