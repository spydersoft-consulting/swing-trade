type Handler = () => void;
type MessageHandler = (message: string) => void;

let unauthorized: Handler | null = null;
let forbidden: MessageHandler | null = null;

/** Registered by AuthProvider: called when any API call answers 401 (the BFF session or its token is stale). */
export const registerUnauthorizedHandler = (fn: Handler) => {
  unauthorized = fn;
  return () => {
    if (unauthorized === fn) unauthorized = null;
  };
};

export const notifyUnauthorized = () => {
  unauthorized?.();
};

/** Registered by ToastProvider: called when any API call answers 403 so the user gets a clear message. */
export const registerForbiddenHandler = (fn: MessageHandler) => {
  forbidden = fn;
  return () => {
    if (forbidden === fn) forbidden = null;
  };
};

export const notifyForbidden = (message: string) => {
  forbidden?.(message);
};
