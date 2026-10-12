// Role and policy names are constants of the API (SwingTradeAuth). They are repeated here only for the
// "Access not granted" page, which has no API response to read them from (the API answers 403). Settings shows the
// authoritative names from GET /api/v1/auth/info.
export const ROLE_NAMES = {
  reader: "swingtrade.reader",
  trader: "swingtrade.trader",
  admin: "swingtrade.admin",
} as const;

export const ROLE_DESCRIPTIONS: Record<keyof typeof ROLE_NAMES, string> = {
  reader: "Read-only: watchlist, trades, dashboard, notes, settings, sizer and the CSV export.",
  trader: "Everything a reader can do, plus create, edit, fill, close and cancel trades, add notes and manage tickers.",
  admin: "Everything a trader can do, plus change Settings, refresh market data and see the MCP call log.",
};

/** Roles are hierarchical in the API's policies: admin satisfies trader, trader satisfies reader. Display gating only. */
const RANK: Record<string, number> = {
  [ROLE_NAMES.reader]: 1,
  [ROLE_NAMES.trader]: 2,
  [ROLE_NAMES.admin]: 3,
};

/** True when one of the user's roles is the required role or a higher one (mcp is independent and matches only itself). */
export function roleSatisfies(userRoles: readonly string[], required: string): boolean {
  const needed = RANK[required];
  if (needed === undefined) return userRoles.includes(required);
  return userRoles.some((r) => (RANK[r] ?? 0) >= needed);
}
