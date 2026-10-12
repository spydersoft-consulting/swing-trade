import type { AxiosInstance, InternalAxiosRequestConfig } from "axios";

const RELEASE = Symbol("releaseRequestGate");
type Gated = InternalAxiosRequestConfig & { [RELEASE]?: () => void };

/**
 * Lets one API request at a time through the axios instance.
 *
 * Why: the BFF's OidcProxy.Net 1.1.x guards its per-session token renewal with an in-memory lock that throws
 * "Unable to create a lock" (HTTP 500) when two requests of the same session arrive at the same moment and the lock does
 * not exist yet. That is the in-memory session store used without Redis (local runs and the Testing stack); a screen
 * that loads two resources at once would then fail at random. Every call here answers in milliseconds, so queueing them
 * costs nothing noticeable. Remove it when the library no longer needs it.
 */
export function serializeRequests(instance: AxiosInstance): void {
  let tail: Promise<void> = Promise.resolve();

  instance.interceptors.request.use(async (config) => {
    const previous = tail;
    let release: () => void = () => undefined;
    tail = new Promise<void>((resolve) => {
      release = resolve;
    });
    await previous;
    (config as Gated)[RELEASE] = release;
    return config;
  });

  const done = (config: Gated | undefined) => config?.[RELEASE]?.();

  instance.interceptors.response.use(
    (response) => {
      done(response.config as Gated);
      return response;
    },
    (error: unknown) => {
      done((error as { config?: Gated }).config);
      throw error;
    },
  );
}
