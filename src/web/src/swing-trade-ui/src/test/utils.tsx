import React from "react";
import { render } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { Provider } from "react-redux";
import { PrimeReactProvider } from "primereact/api";
import { AuthContext, type IAuthContext } from "../context/AuthContext";
import { ToastProvider } from "../context/ToastContext";
import { makeStore, type RootState } from "../store/store";
import { roleSatisfies } from "../utils/roles";
import type {
  AuthInfoDto,
  DashboardDto,
  SettingsDto,
  SizerResultDto,
  TradeDto,
  WatchlistRowDto,
} from "../api/generated/types.gen";

export type Persona = "reader" | "trader" | "admin" | "norole";

const ROLE_OF: Record<Persona, string[]> = {
  reader: ["swingtrade.reader"],
  trader: ["swingtrade.trader"],
  admin: ["swingtrade.admin"],
  norole: [],
};

export function authInfoFor(persona: Persona): AuthInfoDto {
  const roles = ROLE_OF[persona];
  return {
    subject: `${persona}-1`,
    roles,
    canRead: roleSatisfies(roles, "swingtrade.reader"),
    canWrite: roleSatisfies(roles, "swingtrade.trader"),
    canAdmin: roleSatisfies(roles, "swingtrade.admin"),
    audience: "swingtrade",
    readerRole: "swingtrade.reader",
    traderRole: "swingtrade.trader",
    adminRole: "swingtrade.admin",
    mcpRole: "swingtrade.mcp",
    readPolicy: "Read",
    writePolicy: "Write",
    adminPolicy: "Admin",
    mcpPolicy: "Mcp",
  };
}

export function authFor(persona: Persona, overrides: Partial<IAuthContext> = {}): IAuthContext {
  const info = authInfoFor(persona);
  const granted = persona !== "norole";
  return {
    isLoading: false,
    isAuthenticated: true,
    user: { name: persona, authenticated: true, exp: 0 },
    access: granted ? "granted" : "denied",
    authInfo: granted ? info : undefined,
    roles: info.roles,
    hasRole: (role) => roleSatisfies(info.roles, role),
    canRead: info.canRead,
    canWrite: info.canWrite,
    canAdmin: info.canAdmin,
    login: () => undefined,
    logout: () => undefined,
    refreshAuth: async () => undefined,
    ...overrides,
  };
}

interface RenderOptions {
  persona?: Persona;
  auth?: Partial<IAuthContext>;
  route?: string;
  preloadedState?: Partial<RootState>;
}

/** Renders a screen the way the app does: router, store, auth context and toasts. */
export function renderScreen(ui: React.ReactElement, options: RenderOptions = {}) {
  const store = makeStore(options.preloadedState);
  const auth = authFor(options.persona ?? "trader", options.auth);
  const result = render(
    <PrimeReactProvider>
      <ToastProvider>
        <AuthContext.Provider value={auth}>
          <Provider store={store}>
            <MemoryRouter initialEntries={[options.route ?? "/"]}>{ui}</MemoryRouter>
          </Provider>
        </AuthContext.Provider>
      </ToastProvider>
    </PrimeReactProvider>,
  );
  return { store, auth, ...result };
}

// ---- Sample API data (the shapes the generated client returns) ----

export const row = (overrides: Partial<WatchlistRowDto> = {}): WatchlistRowDto => ({
  ticker: "QQQ",
  name: "Invesco QQQ",
  category: null,
  asOfDate: "2026-10-09",
  last: 751.27,
  ma20: 737.54,
  ma50: 725.27,
  pctVs20: 0.018616,
  trend: "Uptrend",
  trendText: "Uptrend",
  signal: "PullbackCandidate",
  signalText: "Pullback candidate",
  reentryOkAfter: null,
  stale: false,
  ...overrides,
});

export const trade = (overrides: Partial<TradeDto> = {}): TradeDto => ({
  id: 1,
  ticker: "XLE",
  status: "Closed",
  setup: "Pullback",
  openDate: "2026-10-05",
  shares: 15,
  entryPrice: 62.34,
  stopPrice: 61.4,
  targetPrice: 65.9,
  exitPrice: 65.91,
  exitDate: "2026-10-12",
  followedPlan: true,
  rationale: null,
  lessons: null,
  createdAt: "2026-10-05T14:00:00Z",
  updatedAt: "2026-10-12T14:00:00Z",
  position: 935.1,
  riskDollars: 14.1,
  riskPct: 0.00705,
  plannedRewardRisk: 3.787234,
  pnl: 53.55,
  pnlPct: 0.057266,
  rMultiple: 3.797872,
  daysHeld: 7,
  riskOverLimit: false,
  rewardRiskBelowMin: false,
  washSaleFlag: false,
  ...overrides,
});

export const sizerResult = (overrides: Partial<SizerResultDto> = {}): SizerResultDto => ({
  ticker: "XLE",
  entry: 62.82,
  stop: 61.4,
  target: 65.9,
  capital: 2000,
  riskBudget: 30,
  riskPerShare: 1.42,
  sharesByRisk: 21,
  sharesByCap: 15,
  sharesByCash: 31,
  shares: 15,
  positionCost: 942.3,
  riskDollars: 21.3,
  riskPct: 0.01065,
  rewardRisk: 2.169014,
  maxEntryForMinRewardRisk: 62.9,
  verdict: "OK - log it",
  isOk: true,
  warning: null,
  availableCash: 1950,
  openCount: 0,
  ...overrides,
});

export const dashboard = (overrides: Partial<DashboardDto> = {}): DashboardDto => ({
  realizedPnl: 53.55,
  accountValue: 2053.55,
  returnPct: 0.026775,
  taxSetAside: 14.4585,
  afterTaxPnl: 39.0915,
  openCount: 0,
  openCountOverLimit: false,
  capitalAtWork: 0,
  capitalAtWorkPct: 0,
  riskAtStops: 0,
  riskAtStopsPct: 0,
  reservedByPlanned: 0,
  availableCash: 2053.55,
  closedCount: 1,
  winRate: 1,
  avgWin: 53.55,
  avgLoss: null,
  profitFactor: null,
  avgR: 3.797872,
  planAdherence: 1,
  avgDaysHeld: 7,
  bySetup: { Pullback: { trades: 1, winRate: 1, netPnl: 53.55 } },
  spyReturnPct: 0.0123,
  youMinusSpy: 0.014475,
  equityCurve: [
    { date: "2026-10-05", accountValue: 2000 },
    { date: "2026-10-12", accountValue: 2053.55 },
  ],
  ...overrides,
});

export const settings = (overrides: Partial<SettingsDto> = {}): SettingsDto => ({
  startingCapital: 2000,
  maxRiskPct: 0.015,
  maxOpenPositions: 3,
  minRewardRisk: 2,
  taxSetAsideRate: 0.27,
  washSaleWindowDays: 30,
  maxPositionPct: 0.5,
  pullbackBandLow: -0.01,
  pullbackBandHigh: 0.02,
  extendedThreshold: 0.05,
  riskBasis: "StartingCapital",
  ...overrides,
});
