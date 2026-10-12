import { act, render, screen, waitFor } from "@testing-library/react";
import axios, { AxiosError, AxiosHeaders } from "axios";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AuthProvider, useAuth } from "./AuthContext";
import { notifyUnauthorized } from "./authSession";
import { swingTradeApi } from "../api/swingTradeApi";
import { authInfoFor } from "../test/utils";

vi.mock("../api/swingTradeApi", () => ({ swingTradeApi: { authInfo: vi.fn() } }));

const api = vi.mocked(swingTradeApi);
const httpError = (status: number) =>
  new AxiosError("x", "E", undefined, undefined, {
    status,
    statusText: "",
    data: {},
    headers: {},
    config: { headers: new AxiosHeaders() },
  });
const userinfo = (extra: Record<string, unknown> = {}) => ({
  data: { sub: "trader-1", exp: Math.floor(Date.now() / 1000) + 3600, ...extra },
});

function Consumer() {
  const auth = useAuth();
  return (
    <div>
      <span data-testid="authenticated">{String(auth.isAuthenticated)}</span>
      <span data-testid="loading">{String(auth.isLoading)}</span>
      <span data-testid="access">{auth.access}</span>
      <span data-testid="user">{auth.user?.name ?? ""}</span>
      <span data-testid="roles">{auth.roles.join(",")}</span>
      <span data-testid="flags">{`${auth.canRead}/${auth.canWrite}/${auth.canAdmin}`}</span>
      <span data-testid="has-reader">{String(auth.hasRole("swingtrade.reader"))}</span>
      <span data-testid="has-trader">{String(auth.hasRole("swingtrade.trader"))}</span>
      <span data-testid="has-admin">{String(auth.hasRole("swingtrade.admin"))}</span>
      <button onClick={auth.login}>login</button>
      <button onClick={auth.logout}>logout</button>
    </div>
  );
}

const renderAuth = () =>
  render(
    <AuthProvider>
      <Consumer />
    </AuthProvider>,
  );

beforeEach(() => {
  localStorage.clear();
  vi.restoreAllMocks();
  api.authInfo.mockReset();
  Object.defineProperty(window, "location", { value: { href: "" }, writable: true });
});

describe("AuthProvider", () => {
  it("reads the session and the roles from the BFF and the API", async () => {
    vi.spyOn(axios, "get").mockResolvedValue(userinfo({ name: "Matt" }));
    api.authInfo.mockResolvedValue(authInfoFor("trader"));

    renderAuth();

    await waitFor(() => expect(screen.getByTestId("access")).toHaveTextContent("granted"));
    expect(screen.getByTestId("authenticated")).toHaveTextContent("true");
    expect(screen.getByTestId("user")).toHaveTextContent("Matt");
    expect(screen.getByTestId("roles")).toHaveTextContent("swingtrade.trader");
    expect(axios.get).toHaveBeenCalledWith("/oauth2/userinfo");
  });

  it("falls back to the subject when the token carries no display name", async () => {
    vi.spyOn(axios, "get").mockResolvedValue(userinfo());
    api.authInfo.mockResolvedValue(authInfoFor("reader"));

    renderAuth();

    await waitFor(() => expect(screen.getByTestId("user")).toHaveTextContent("trader-1"));
  });

  it.each([
    ["reader", "true/false/false", "true", "false", "false"],
    ["trader", "true/true/false", "true", "true", "false"],
    ["admin", "true/true/true", "true", "true", "true"],
  ] as const)("exposes %s capabilities and hierarchical hasRole", async (persona, flags, reader, trader, admin) => {
    vi.spyOn(axios, "get").mockResolvedValue(userinfo());
    api.authInfo.mockResolvedValue(authInfoFor(persona));

    renderAuth();

    await waitFor(() => expect(screen.getByTestId("access")).toHaveTextContent("granted"));
    expect(screen.getByTestId("flags")).toHaveTextContent(flags);
    expect(screen.getByTestId("has-reader")).toHaveTextContent(reader);
    expect(screen.getByTestId("has-trader")).toHaveTextContent(trader);
    expect(screen.getByTestId("has-admin")).toHaveTextContent(admin);
  });

  it("marks a signed-in user the API answers 403 for as denied (Access not granted)", async () => {
    vi.spyOn(axios, "get").mockResolvedValue(userinfo());
    api.authInfo.mockRejectedValue(httpError(403));

    renderAuth();

    await waitFor(() => expect(screen.getByTestId("access")).toHaveTextContent("denied"));
    expect(screen.getByTestId("authenticated")).toHaveTextContent("true");
    expect(screen.getByTestId("flags")).toHaveTextContent("false/false/false");
    expect(screen.getByTestId("roles")).toHaveTextContent("");
  });

  it("reports a server failure on the role check as an error, not as denied", async () => {
    vi.spyOn(axios, "get").mockResolvedValue(userinfo());
    api.authInfo.mockRejectedValue(httpError(500));

    renderAuth();

    await waitFor(() => expect(screen.getByTestId("access")).toHaveTextContent("error"));
  });

  it("treats a 401 from userinfo as not signed in, without redirecting a first-time visitor", async () => {
    vi.spyOn(axios, "get").mockRejectedValue(httpError(401));

    renderAuth();

    await waitFor(() => expect(screen.getByTestId("loading")).toHaveTextContent("false"));
    expect(screen.getByTestId("authenticated")).toHaveTextContent("false");
    expect(window.location.href).toBe("");
    expect(api.authInfo).not.toHaveBeenCalled();
  });

  it("re-logs in when a stale 401 arrives for a session that was good", async () => {
    vi.spyOn(axios, "get").mockResolvedValue(userinfo());
    api.authInfo.mockResolvedValue(authInfoFor("trader"));
    renderAuth();
    await waitFor(() => expect(screen.getByTestId("access")).toHaveTextContent("granted"));

    act(() => notifyUnauthorized());

    expect(window.location.href).toBe("/oauth2/sign_in");
    expect(localStorage.getItem("isAuthenticated")).toBeNull();
    expect(screen.getByTestId("authenticated")).toHaveTextContent("false");
  });

  it("uses a fresh cached session without asking the BFF again, but still reads the roles", async () => {
    const exp = Math.floor(Date.now() / 1000) + 3600;
    localStorage.setItem("isAuthenticated", "true");
    localStorage.setItem("user", JSON.stringify({ name: "Cached", authenticated: true, exp }));
    const get = vi.spyOn(axios, "get");
    api.authInfo.mockResolvedValue(authInfoFor("admin"));

    renderAuth();

    expect(screen.getByTestId("loading")).toHaveTextContent("false");
    await waitFor(() => expect(screen.getByTestId("access")).toHaveTextContent("granted"));
    expect(get).not.toHaveBeenCalled();
  });

  it("login and logout go through the BFF once each", async () => {
    vi.spyOn(axios, "get").mockRejectedValue(httpError(401));
    renderAuth();
    await waitFor(() => expect(screen.getByTestId("loading")).toHaveTextContent("false"));

    act(() => screen.getByText("login").click());
    expect(window.location.href).toBe("/oauth2/sign_in");

    act(() => screen.getByText("logout").click());
    // A second redirect while the first is in flight is ignored (OIDC state would mismatch).
    expect(window.location.href).toBe("/oauth2/sign_in");
  });
});
