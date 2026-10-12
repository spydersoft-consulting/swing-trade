import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { PrimeReactProvider } from "primereact/api";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AppRouter } from "./AppRouter";
import { AccessNotGranted } from "./AccessNotGranted/AccessNotGranted";
import { NavigationBar } from "./layout/NavigationBar";
import { Landing } from "./Landing/Landing";
import { AuthContext, type IAuthContext } from "../context/AuthContext";
import { PageTitleProvider } from "../context/PageTitleContext";
import { ToastProvider } from "../context/ToastContext";
import { makeStore } from "../store/store";
import { authFor, renderScreen, type Persona } from "../test/utils";
import { MemoryRouter } from "react-router-dom";

// The screens have their own tests; here they are stand-ins so the router's decisions are what is tested.
vi.mock("./Watchlist/Watchlist", () => ({ Watchlist: () => <h1>Watchlist screen</h1> }));
vi.mock("./Trades/Trades", () => ({ Trades: () => <h1>Trades screen</h1> }));
vi.mock("./Sizer/Sizer", () => ({ Sizer: () => <h1>Sizer screen</h1> }));
vi.mock("./Dashboard/Dashboard", () => ({ Dashboard: () => <h1>Dashboard screen</h1> }));
vi.mock("./Journal/Journal", () => ({ Journal: () => <h1>Journal screen</h1> }));
vi.mock("./Settings/Settings", () => ({ Settings: () => <h1>Settings screen</h1> }));

const renderApp = (auth: IAuthContext) =>
  render(
    <PrimeReactProvider>
      <ToastProvider>
        <AuthContext.Provider value={auth}>
          <Provider store={makeStore()}>
            <AppRouter />
          </Provider>
        </AuthContext.Provider>
      </ToastProvider>
    </PrimeReactProvider>,
  );

beforeEach(() => {
  window.history.pushState({}, "", "/");
});

describe("AppRouter", () => {
  it("shows a spinner while the session is being checked", () => {
    renderApp(authFor("trader", { isLoading: true }));
    expect(screen.getByRole("status", { name: "Loading" })).toBeInTheDocument();
    expect(screen.queryByText("Watchlist screen")).not.toBeInTheDocument();
  });

  it("shows the sign-in landing page to a visitor who is not signed in", async () => {
    const login = vi.fn();
    renderApp(authFor("trader", { isAuthenticated: false, access: "unknown", login }));

    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));
    expect(login).toHaveBeenCalled();
  });

  it("waits for the role check before showing anything", () => {
    renderApp(authFor("trader", { access: "unknown" }));
    expect(screen.getByRole("status", { name: "Loading" })).toBeInTheDocument();
  });

  it("sends a granted user to the watchlist", async () => {
    renderApp(authFor("reader"));
    expect(await screen.findByRole("heading", { name: "Watchlist screen" })).toBeInTheDocument();
  });

  it("routes each screen", async () => {
    const user = userEvent.setup();
    renderApp(authFor("admin"));
    await screen.findByRole("heading", { name: "Watchlist screen" });
    const nav = screen.getByRole("navigation", { name: "Screens" });

    for (const name of ["Trades", "Sizer", "Dashboard", "Journal", "Settings", "Watchlist"]) {
      await user.click(within(nav).getByRole("link", { name }));
      expect(await screen.findByRole("heading", { name: `${name} screen` })).toBeInTheDocument();
    }
  });

  it("shows Access not granted, not an empty app, to a signed-in user with no role, on any URL", async () => {
    window.history.pushState({}, "", "/trades");
    renderApp(authFor("norole"));

    const notice = await screen.findByTestId("access-not-granted");
    expect(notice).toHaveTextContent("Access not granted");
    expect(screen.queryByText("Trades screen")).not.toBeInTheDocument();
    expect(within(screen.getByRole("navigation", { name: "Screens" })).queryAllByRole("link")).toHaveLength(0);
  });

  it("offers a retry when the role check failed on the server", async () => {
    const refreshAuth = vi.fn().mockResolvedValue(undefined);
    renderApp(authFor("trader", { access: "error", refreshAuth }));

    await userEvent.click(await screen.findByRole("button", { name: "Try again" }));
    expect(refreshAuth).toHaveBeenCalled();
  });
});

describe("AccessNotGranted", () => {
  it("names the roles to request and lets the user sign out", async () => {
    const logout = vi.fn();
    renderScreen(<AccessNotGranted />, { persona: "norole", auth: { logout } });

    const page = screen.getByTestId("access-not-granted");
    expect(within(page).getByRole("heading", { level: 1, name: "Access not granted" })).toBeInTheDocument();
    for (const role of ["swingtrade.reader", "swingtrade.trader", "swingtrade.admin"]) {
      expect(page).toHaveTextContent(role);
    }
    expect(page).toHaveTextContent("signed in as norole");
    await userEvent.click(within(page).getByRole("button", { name: "Sign out" }));
    expect(logout).toHaveBeenCalled();
  });
});

describe("NavigationBar", () => {
  const renderNav = (persona: Persona, overrides: Partial<IAuthContext> = {}) =>
    render(
      <MemoryRouter>
        <PrimeReactProvider>
          <AuthContext.Provider value={authFor(persona, overrides)}>
            <PageTitleProvider>
              <NavigationBar />
            </PageTitleProvider>
          </AuthContext.Provider>
        </PrimeReactProvider>
      </MemoryRouter>,
    );

  it("lists the six screens in order", () => {
    renderNav("reader");
    const links = within(screen.getByRole("navigation", { name: "Screens" })).getAllByRole("link");
    expect(links.map((l) => l.textContent)).toEqual([
      "Watchlist",
      "Trades",
      "Sizer",
      "Dashboard",
      "Journal",
      "Settings",
    ]);
    expect(links.map((l) => l.getAttribute("href"))).toEqual([
      "/watchlist",
      "/trades",
      "/sizer",
      "/dashboard",
      "/journal",
      "/settings",
    ]);
  });

  it("shows who is signed in and with which roles, and signs out", async () => {
    const logout = vi.fn();
    renderNav("trader", { logout });

    const summary = screen.getByTestId("account-summary");
    expect(summary).toHaveTextContent("trader");
    expect(summary).toHaveTextContent("Roles: trader");
    await userEvent.click(screen.getByRole("button", { name: "Sign out" }));
    expect(logout).toHaveBeenCalled();
  });

  it("offers no screens to a user without a role", () => {
    renderNav("norole");
    expect(within(screen.getByRole("navigation", { name: "Screens" })).queryAllByRole("link")).toHaveLength(0);
  });

  it("opens the menu on a small screen", async () => {
    renderNav("reader");
    await userEvent.click(screen.getByRole("button", { name: "Open menu" }));
    await waitFor(() => expect(screen.getByRole("navigation", { name: "Screens (menu)" })).toBeInTheDocument());
    expect(within(screen.getByRole("navigation", { name: "Screens (menu)" })).getAllByRole("link")).toHaveLength(6);
  });

  it("offers sign in when not signed in", async () => {
    const login = vi.fn();
    renderNav("reader", { isAuthenticated: false, login });
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));
    expect(login).toHaveBeenCalled();
  });
});

describe("Landing", () => {
  it("has a heading and a sign in button", () => {
    const login = vi.fn();
    render(
      <PrimeReactProvider>
        <AuthContext.Provider value={authFor("reader", { login, isAuthenticated: false })}>
          <Landing />
        </AuthContext.Provider>
      </PrimeReactProvider>,
    );
    expect(screen.getByRole("heading", { level: 1 })).toBeInTheDocument();
    screen.getByRole("button", { name: "Sign in" }).click();
    expect(login).toHaveBeenCalled();
  });
});
