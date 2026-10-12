import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import axios from "axios";
import { registerUnauthorizedHandler } from "./authSession";
import { swingTradeApi } from "../api/swingTradeApi";
import type { AuthInfoDto } from "../api/generated/types.gen";
import { toProblem } from "../api/problem";
import { roleSatisfies } from "../utils/roles";

export interface UserInfo {
  name: string;
  authenticated: boolean;
  exp: number;
}

/**
 * granted: the API accepted the token and the user holds a swingtrade.* role.
 * denied: the API answered 403 (a signed-in user with no role): the app shows "Access not granted".
 * error: the roles could not be read (server or network problem); the app offers a retry.
 */
export type AccessState = "unknown" | "granted" | "denied" | "error";

const DISPLAY_NAME_CLAIMS = ["name", "preferred_username", "email", "sub"] as const;
const REFRESH_WINDOW_MS = 5 * 60 * 1000;

const resolveDisplayName = (claims: Record<string, unknown>): string => {
  for (const key of DISPLAY_NAME_CLAIMS) {
    const value = claims[key];
    if (typeof value === "string" && value.length > 0) return value;
  }
  return "";
};

const toEpochSeconds = (value: unknown): number => {
  if (typeof value === "number") return value;
  if (typeof value === "string") {
    const parsed = Number.parseInt(value, 10);
    return Number.isFinite(parsed) ? parsed : 0;
  }
  return 0;
};

const isFresh = (exp: number): boolean => exp > 0 && Date.now() < exp * 1000 - REFRESH_WINDOW_MS;

const readCachedUser = (): UserInfo | undefined => {
  try {
    const saved = localStorage.getItem("user");
    return saved ? (JSON.parse(saved) as UserInfo) : undefined;
  } catch {
    return undefined;
  }
};

export interface IAuthContext {
  isLoading: boolean;
  isAuthenticated: boolean;
  user?: UserInfo;
  /** Whether the API let this user in, and what it said about them (roles, role/policy names). */
  access: AccessState;
  authInfo?: AuthInfoDto;
  roles: readonly string[];
  /** Hierarchical: hasRole("swingtrade.reader") is true for a trader or an admin too. Display gating only. */
  hasRole: (role: string) => boolean;
  canRead: boolean;
  canWrite: boolean;
  canAdmin: boolean;
  login: () => void;
  logout: () => void;
  refreshAuth: () => Promise<void>;
}

const NO_ROLES: readonly string[] = [];

export const AuthContext = React.createContext<IAuthContext>({
  isLoading: false,
  isAuthenticated: false,
  access: "unknown",
  roles: NO_ROLES,
  hasRole: () => false,
  canRead: false,
  canWrite: false,
  canAdmin: false,
  login: () => void 0,
  logout: () => void 0,
  refreshAuth: async () => void 0,
});

export const useAuth = () => React.useContext(AuthContext);

export const AuthProvider = (props: { children: React.ReactNode }) => {
  const [isAuthenticated, setIsAuthenticated] = useState<boolean>(
    () => localStorage.getItem("isAuthenticated") === "true",
  );
  const [user, setUser] = useState<UserInfo | undefined>(readCachedUser);
  const [access, setAccess] = useState<AccessState>("unknown");
  const [authInfo, setAuthInfo] = useState<AuthInfoDto | undefined>(undefined);

  // Start in a loading state unless there is a still-fresh cached session, so the router holds off rendering the
  // logged-out view until the mount-time /oauth2/userinfo check has had a chance to resolve.
  const [isLoading, setIsLoading] = useState(() => {
    const cached = readCachedUser();
    return !(cached && isFresh(cached.exp));
  });
  const inflight = useRef<Promise<void> | null>(null);
  // Guards against a second sign-in/out redirect while the first is in flight: OIDC state/nonce is fresh per request,
  // so two concurrent round trips can fail with a state mismatch at the IdP.
  const isRedirecting = useRef(false);

  const login = useCallback(() => {
    if (isRedirecting.current) return;
    isRedirecting.current = true;
    globalThis.location.href = "/oauth2/sign_in";
  }, []);

  // wasAuthenticated: the session was known-good a moment ago, so this is an expiry (re-login), not a first visit.
  const clearAuthState = useCallback(
    (wasAuthenticated: boolean) => {
      setIsAuthenticated(false);
      setUser(undefined);
      setAuthInfo(undefined);
      setAccess("unknown");
      localStorage.removeItem("isAuthenticated");
      localStorage.removeItem("user");

      // A session that was valid and is now invalid means the access/refresh token expired server-side. Going through
      // sign-in is a silent SSO round trip when the IdP session is alive, and the real login page otherwise. The
      // cached flags are cleared first, so a failed attempt cannot loop. The spinner stays up until the redirect lands.
      if (wasAuthenticated) {
        setIsLoading(true);
        login();
      }
    },
    [login],
  );

  const loadAccess = useCallback(async () => {
    try {
      const info = await swingTradeApi.authInfo();
      setAuthInfo(info);
      setAccess("granted");
    } catch (err) {
      const problem = toProblem(err);
      setAuthInfo(undefined);
      if (problem.status === 403) {
        setAccess("denied");
      } else if (problem.status !== 401) {
        // 401 is handled by the unauthorized handler (re-login); anything else is shown with a retry.
        setAccess("error");
      }
    }
  }, []);

  const fetchUser = useCallback((): Promise<void> => {
    if (inflight.current) return inflight.current;

    setIsLoading(true);
    const p = axios
      .get<Record<string, unknown>>("/oauth2/userinfo")
      .then(async (r) => {
        const userInfo: UserInfo = {
          name: resolveDisplayName(r.data),
          authenticated: true,
          exp: toEpochSeconds(r.data.exp),
        };
        setIsAuthenticated(true);
        setUser(userInfo);
        localStorage.setItem("isAuthenticated", "true");
        localStorage.setItem("user", JSON.stringify(userInfo));
        await loadAccess();
        setIsLoading(false);
      })
      .catch((err: { response?: { status: number } }) => {
        const wasAuthenticated = localStorage.getItem("isAuthenticated") === "true";
        if (err.response?.status === 401) {
          clearAuthState(wasAuthenticated);
        }
        if (!wasAuthenticated) {
          setIsLoading(false);
        }
        // else: clearAuthState already set isLoading back to true for the redirect it just triggered.
      })
      .finally(() => {
        inflight.current = null;
      });

    inflight.current = p;
    return p;
  }, [clearAuthState, loadAccess]);

  // On mount: always read the roles from the API (they can change without the session changing); re-check the
  // session only when the cached one is missing or close to expiry.
  useEffect(() => {
    if (user && isFresh(user.exp)) {
      void loadAccess();
      return;
    }
    void fetchUser();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!isAuthenticated || !user?.exp || !isFresh(user.exp)) return;
    const msUntilRefresh = user.exp * 1000 - REFRESH_WINDOW_MS - Date.now();
    const handle = globalThis.setTimeout(
      () => {
        void fetchUser();
      },
      Math.max(msUntilRefresh, 0),
    );
    return () => globalThis.clearTimeout(handle);
  }, [isAuthenticated, user?.exp, user, fetchUser]);

  // A stale 401 from any API call: the session was good, the token is not. Same recovery as above.
  useEffect(() => {
    return registerUnauthorizedHandler(() => clearAuthState(isAuthenticated));
  }, [isAuthenticated, clearAuthState]);

  const logout = useCallback(() => {
    if (isRedirecting.current) return;
    isRedirecting.current = true;
    clearAuthState(false);
    setIsLoading(true);
    globalThis.location.href = "/oauth2/sign_out";
  }, [clearAuthState]);

  const refreshAuth = useCallback(() => fetchUser(), [fetchUser]);

  const roles = authInfo?.roles ?? NO_ROLES;
  const contextValue = useMemo<IAuthContext>(
    () => ({
      isAuthenticated,
      user,
      isLoading,
      access,
      authInfo,
      roles,
      hasRole: (role: string) => roleSatisfies(roles, role),
      canRead: authInfo?.canRead ?? false,
      canWrite: authInfo?.canWrite ?? false,
      canAdmin: authInfo?.canAdmin ?? false,
      login,
      logout,
      refreshAuth,
    }),
    [isAuthenticated, user, isLoading, access, authInfo, roles, login, logout, refreshAuth],
  );

  return <AuthContext.Provider value={contextValue}>{props.children}</AuthContext.Provider>;
};
