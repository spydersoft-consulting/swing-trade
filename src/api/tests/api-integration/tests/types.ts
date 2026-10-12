// Response and request shapes, mirroring Spydersoft.SwingTrade.Contracts (camelCase JSON, enums as strings).

export type TradeStatus = "Planned" | "Open" | "Closed" | "Cancelled";
export type TradeSetup = "Pullback" | "Breakout" | "MeanReversion" | "Other";
export type SignalName = "InsufficientData" | "PullbackCandidate" | "ExtendedWait" | "Avoid" | "Watch";
export type TrendName = "InsufficientData" | "Uptrend" | "Downtrend" | "Mixed";

export interface SettingsDto {
  startingCapital: number;
  maxRiskPct: number;
  maxOpenPositions: number;
  minRewardRisk: number;
  taxSetAsideRate: number;
  washSaleWindowDays: number;
  maxPositionPct: number;
  pullbackBandLow: number;
  pullbackBandHigh: number;
  extendedThreshold: number;
  riskBasis: "StartingCapital" | "AccountValue";
}

export interface InstrumentDto {
  ticker: string;
  name: string;
  category?: string | null;
  active: boolean;
  isBenchmark: boolean;
  washSaleGroup?: string | null;
}

export interface CreateTradeRequest {
  ticker: string;
  status?: "Planned" | "Open";
  setup?: TradeSetup;
  openDate?: string;
  shares: number;
  entryPrice: number;
  stopPrice: number;
  targetPrice: number;
  rationale?: string;
}

export interface TradeDto {
  id: number;
  ticker: string;
  status: TradeStatus;
  setup: TradeSetup;
  openDate: string | null;
  shares: number;
  entryPrice: number;
  stopPrice: number;
  targetPrice: number;
  exitPrice: number | null;
  exitDate: string | null;
  followedPlan: boolean | null;
  rationale: string | null;
  lessons: string | null;
  position: number;
  riskDollars: number;
  riskPct: number;
  plannedRewardRisk: number;
  pnl: number | null;
  pnlPct: number | null;
  rMultiple: number | null;
  daysHeld: number | null;
  riskOverLimit: boolean;
  rewardRiskBelowMin: boolean;
  washSaleFlag: boolean;
}

export interface NoteDto {
  id: number;
  date: string;
  ticker: string | null;
  kind: "Pass" | "Review" | "General";
  text: string;
}

export interface WatchlistRowDto {
  ticker: string;
  name: string;
  category: string | null;
  asOfDate: string | null;
  last: number | null;
  ma20: number | null;
  ma50: number | null;
  pctVs20: number | null;
  trend: TrendName;
  trendText: string;
  signal: SignalName;
  signalText: string;
  reentryOkAfter: string | null;
  stale: boolean;
}

export interface BarPointDto {
  date: string;
  close: number;
  ma20: number | null;
  ma50: number | null;
}

export interface BarsDto {
  ticker: string;
  days: number;
  bars: BarPointDto[];
}

export interface SizerResultDto {
  ticker: string | null;
  entry: number;
  stop: number;
  target: number;
  capital: number;
  riskBudget: number;
  riskPerShare: number;
  sharesByRisk: number;
  sharesByCap: number;
  sharesByCash: number | null;
  shares: number;
  positionCost: number;
  riskDollars: number;
  riskPct: number;
  rewardRisk: number | null;
  maxEntryForMinRewardRisk: number;
  verdict: string;
  isOk: boolean;
  warning: string | null;
  availableCash: number;
  openCount: number;
}

export interface DashboardDto {
  realizedPnl: number;
  accountValue: number;
  returnPct: number;
  taxSetAside: number;
  afterTaxPnl: number;
  openCount: number;
  openCountOverLimit: boolean;
  capitalAtWork: number;
  capitalAtWorkPct: number;
  riskAtStops: number;
  riskAtStopsPct: number;
  reservedByPlanned: number;
  availableCash: number;
  closedCount: number;
  winRate: number | null;
  avgWin: number | null;
  avgLoss: number | null;
  profitFactor: number | null;
  avgR: number | null;
  planAdherence: number | null;
  avgDaysHeld: number | null;
  bySetup: Record<string, { trades: number; winRate: number | null; netPnl: number }>;
  spyReturnPct: number | null;
  youMinusSpy: number | null;
  equityCurve: { date: string; accountValue: number }[];
}

export interface RefreshRunDto {
  id: number;
  startedAt: string;
  finishedAt: string | null;
  provider: string;
  status: string;
  tickersOk: number;
  tickersFailed: number;
  message: string | null;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface ValidationProblem {
  status: number;
  errors: Record<string, string[]>;
}
