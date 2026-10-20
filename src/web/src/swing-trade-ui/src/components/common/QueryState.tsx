import React from "react";
import { Button } from "../ui/Button";
import { Message } from "primereact/message";
import { ProgressSpinner } from "primereact/progressspinner";
import type { ApiProblem } from "../../api/problem";

export const LoadingBlock: React.FC<{ label?: string }> = ({ label = "Loading" }) => (
  <div className="flex justify-center py-10" role="status" aria-live="polite">
    <ProgressSpinner style={{ width: "2.5rem", height: "2.5rem" }} aria-label={label} />
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

/** An inline error or warning under a form or a panel. */
export const InlineMessage: React.FC<{
  severity: "error" | "warn" | "info" | "success";
  children: React.ReactNode;
  testId?: string;
}> = ({ severity, children, testId }) => (
  <div data-testid={testId}>
    <Message
      severity={severity}
      text={undefined}
      content={<span className="text-sm">{children}</span>}
      className="w-full justify-start"
    />
  </div>
);
