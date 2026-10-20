import React, { createContext, useCallback, useContext, useEffect, useMemo, useRef } from "react";
import { UNSTABLE_ToastQueue as ToastQueue } from "react-aria-components";
import { ToastHost, type ToastSeverity, type ToastValue } from "../components/ui/Toast";
import { registerForbiddenHandler } from "./authSession";

type Severity = ToastSeverity;

interface ToastApi {
  show: (severity: Severity, summary: string, detail?: string) => void;
  success: (summary: string, detail?: string) => void;
  info: (summary: string, detail?: string) => void;
  warn: (summary: string, detail?: string) => void;
  error: (summary: string, detail?: string) => void;
}

const noop = () => undefined;
const ToastContext = createContext<ToastApi>({ show: noop, success: noop, info: noop, warn: noop, error: noop });

export const useToast = (): ToastApi => useContext(ToastContext);

const DEDUPE_MS = 4000;

export const ToastProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const queue = useMemo(() => new ToastQueue<ToastValue>({ maxVisibleToasts: 5 }), []);
  const lastShown = useRef<{ key: string; at: number } | null>(null);

  const show = useCallback(
    (severity: Severity, summary: string, detail?: string) => {
      // Several panels can fail with the same 403 at once; one message is enough.
      const key = `${severity}|${summary}|${detail ?? ""}`;
      const now = Date.now();
      if (lastShown.current && lastShown.current.key === key && now - lastShown.current.at < DEDUPE_MS) return;
      lastShown.current = { key, at: now };
      queue.add({ severity, summary, detail }, { timeout: severity === "error" ? 8000 : 4000 });
    },
    [queue],
  );

  useEffect(() => registerForbiddenHandler((message) => show("error", "Not permitted", message)), [show]);

  const api = useMemo<ToastApi>(
    () => ({
      show,
      success: (s, d) => show("success", s, d),
      info: (s, d) => show("info", s, d),
      warn: (s, d) => show("warn", s, d),
      error: (s, d) => show("error", s, d),
    }),
    [show],
  );

  return (
    <ToastContext.Provider value={api}>
      {children}
      <ToastHost queue={queue} />
    </ToastContext.Provider>
  );
};
