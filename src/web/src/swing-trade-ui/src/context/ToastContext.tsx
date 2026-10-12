import React, { createContext, useCallback, useContext, useEffect, useMemo, useRef } from "react";
import { Toast } from "primereact/toast";
import { registerForbiddenHandler } from "./authSession";

type Severity = "success" | "info" | "warn" | "error";

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
  const toast = useRef<Toast>(null);
  const lastShown = useRef<{ key: string; at: number } | null>(null);

  const show = useCallback((severity: Severity, summary: string, detail?: string) => {
    // Several panels can fail with the same 403 at once; one message is enough.
    const key = `${severity}|${summary}|${detail ?? ""}`;
    const now = Date.now();
    if (lastShown.current && lastShown.current.key === key && now - lastShown.current.at < DEDUPE_MS) return;
    lastShown.current = { key, at: now };
    toast.current?.show({ severity, summary, detail, life: severity === "error" ? 8000 : 4000 });
  }, []);

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
      <Toast ref={toast} position="top-right" />
    </ToastContext.Provider>
  );
};
