import {
  UNSTABLE_Toast as Toast,
  UNSTABLE_ToastContent as ToastContent,
  UNSTABLE_ToastRegion as ToastRegion,
} from "react-aria-components";
import type { UNSTABLE_ToastQueue as ToastQueue } from "react-aria-components";
import { faXmark } from "@fortawesome/free-solid-svg-icons";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { Button } from "./Button";

export type ToastSeverity = "success" | "info" | "warn" | "error";

export interface ToastValue {
  severity: ToastSeverity;
  summary: string;
  detail?: string;
}

const tone: Record<ToastSeverity, string> = {
  success: "border-good bg-good-tint text-good",
  info: "border-brand bg-brand-tint text-content",
  warn: "border-warn bg-warn-tint text-warn",
  error: "border-bad bg-bad-tint text-bad",
};

export const ToastHost: React.FC<{ queue: ToastQueue<ToastValue> }> = ({ queue }) => (
  <ToastRegion queue={queue} className="fixed top-4 right-4 z-1200 flex w-80 max-w-[calc(100vw-2rem)] flex-col gap-2">
    {({ toast }) => (
      <Toast
        toast={toast}
        className={`flex items-start gap-2 rounded-lg border p-3 shadow-lg ${tone[toast.content.severity]}`}
      >
        <ToastContent className="min-w-0 flex-1">
          <p slot="title" className="m-0 font-semibold">
            {toast.content.summary}
          </p>
          {toast.content.detail && (
            <p slot="description" className="m-0 text-sm">
              {toast.content.detail}
            </p>
          )}
        </ToastContent>
        <Button slot="close" aria-label="Close" variant="text" size="sm" icon={<FontAwesomeIcon icon={faXmark} />} />
      </Toast>
    )}
  </ToastRegion>
);
