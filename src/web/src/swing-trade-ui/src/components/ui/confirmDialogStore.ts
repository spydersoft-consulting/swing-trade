export interface ConfirmDialogOptions {
  message: string;
  header?: string;
  accept?: () => void | Promise<void>;
  reject?: () => void;
  acceptLabel?: string;
  rejectLabel?: string;
}

let current: ConfirmDialogOptions | null = null;
const listeners = new Set<() => void>();

export const setCurrent = (next: ConfirmDialogOptions | null) => {
  current = next;
  listeners.forEach((l) => l());
};

export const subscribe = (listener: () => void) => {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
};

export const getSnapshot = () => current;

export const confirmDialog = (options: ConfirmDialogOptions) => setCurrent(options);
