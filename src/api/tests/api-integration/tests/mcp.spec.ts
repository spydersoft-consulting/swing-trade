// The MCP endpoint, driven over Streamable HTTP with real tokens from the mock OIDC server. The "mcponly" user holds
// swingtrade.mcp and is the configured owner (Mcp:OwnerSubject = its subject); "mcpother" holds the same role but is a
// different subject. Each tool result must equal the matching REST response for the same data.
import { BASE_URL, MCP_OTHER_USER, claimsOf, mintToken } from "./auth";
import { expect, json, test } from "./fixtures";
import { MCP_PATH, MCP_READ_TOOLS, METADATA_PATH, callTool, callToolExpectingError, postRpc, rpc } from "./mcp";
import type { BarsDto, DashboardDto, NoteDto, PagedResult, RefreshRunDto, SettingsDto, SizerResultDto, TradeDto, WatchlistRowDto } from "./types";

test.describe.configure({ mode: "serial" });

const OWNER_SUBJECT = "mcponly-1";
const PUBLIC_MCP_URL = `${BASE_URL}${MCP_PATH}`;
const METADATA_URL = `${BASE_URL}${METADATA_PATH}`;

test("tools/list returns exactly the documented read tools, and add_note is absent by default", async ({ api }) => {
  const response = await rpc(api.mcponly, "tools/list", {});
  const names = response.result.tools.map((t: { name: string }) => t.name);

  expect([...names].sort()).toEqual([...MCP_READ_TOOLS].sort());
  expect(names).not.toContain("add_note");

  for (const tool of response.result.tools) {
    expect(tool.description, tool.name).toContain("review against the owner's own rules");
    expect(tool.description, tool.name).toContain("not investment advice");
    expect(tool.description, tool.name).toContain("Quote the numbers");
  }
});

test("calling add_note while the flag is off fails and writes nothing", async ({ api }) => {
  const before = await json<NoteDto[]>(await api.reader.get("/api/v1/notes"));
  const message = await callToolExpectingError(api.mcponly, "add_note", { date: "2026-10-12", kind: "Pass", text: "mcp-e2e should not exist" });
  expect(message.length).toBeGreaterThan(0);
  expect(await json<NoteDto[]>(await api.reader.get("/api/v1/notes"))).toEqual(before);
});

test("every tool result carries asOf, dataStatus and the settings snapshot", async ({ api }) => {
  const rest = await json<SettingsDto>(await api.reader.get("/api/v1/settings"));
  const trades = await json<TradeDto[]>(await api.reader.get("/api/v1/trades?status=Closed"));
  const calls: [string, Record<string, unknown>][] = [
    ["get_dashboard", {}],
    ["get_watchlist", {}],
    ["get_instrument", { ticker: "SPY" }],
    ["get_bars", { ticker: "SPY", days: 10 }],
    ["list_trades", {}],
    ["get_trade", { id: trades[0].id }],
    ["size_position", { entry: 62.82, stop: 61.4, target: 65.9 }],
    ["get_settings", {}],
    ["get_refresh_status", {}],
    ["list_notes", {}],
    ["get_levels", { ticker: "SPY" }],
  ];
  expect(calls.map(([name]) => name).sort()).toEqual([...MCP_READ_TOOLS].sort());

  for (const [name, args] of calls) {
    const envelope = await callTool(api.mcponly, name, args);
    expect(envelope.asOf, name).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(["fresh", "stale"], name).toContain(envelope.dataStatus);
    expect(envelope.settings, name).toEqual(rest);
    expect(envelope.data, name).toBeDefined();
  }
});

test("get_dashboard equals GET /dashboard", async ({ api }) => {
  const rest = await json<DashboardDto>(await api.reader.get("/api/v1/dashboard"));
  const tool = await callTool<DashboardDto>(api.mcponly, "get_dashboard");

  expect(tool.data).toEqual(rest);
  expect(tool.data).toHaveProperty("availableCash");
  expect(tool.data).toHaveProperty("reservedByPlanned");
  expect(tool.data).toHaveProperty("taxSetAside");
  expect(tool.data).not.toHaveProperty("expectancy");
  expect(tool.data).not.toHaveProperty("drawdown");
});

test("get_watchlist equals GET /watchlist, and get_instrument returns the matching row", async ({ api }) => {
  const rest = await json<WatchlistRowDto[]>(await api.reader.get("/api/v1/watchlist"));
  const tool = await callTool<WatchlistRowDto[]>(api.mcponly, "get_watchlist");
  expect(tool.data).toEqual(rest);
  expect(tool.data.length).toBeGreaterThanOrEqual(12);

  const all = await callTool<WatchlistRowDto[]>(api.mcponly, "get_watchlist", { activeOnly: false });
  expect(all.data.length).toBeGreaterThan(tool.data.length);

  const spy = rest.find((r) => r.ticker === "SPY")!;
  const instrument = await callTool(api.mcponly, "get_instrument", { ticker: "spy" });
  expect(instrument.data.row).toEqual(spy);
  expect(instrument.data.last5Closes).toHaveLength(5);
  expect(instrument.data.washSale.clear).toBe(true);
  expect(await callToolExpectingError(api.mcponly, "get_instrument", { ticker: "NOPE9" })).toContain("not found");
});

test("get_bars equals GET /instruments/{ticker}/bars and clamps the request to 120", async ({ api }) => {
  const rest = await json<BarsDto>(await api.reader.get("/api/v1/instruments/QQQ/bars?days=120"));
  const tool = await callTool<BarsDto>(api.mcponly, "get_bars", { ticker: "QQQ", days: 99999 });
  expect(tool.data).toEqual(rest);
  expect(tool.data.bars.length).toBeLessThanOrEqual(120);

  const few = await callTool<BarsDto>(api.mcponly, "get_bars", { ticker: "QQQ", days: 7 });
  expect(few.data.bars).toHaveLength(7);
  expect(few.data.bars).toEqual(rest.bars.slice(-7));
});

test("list_trades and get_trade equal GET /trades and GET /trades/{id}", async ({ api }) => {
  const rest = await json<TradeDto[]>(await api.reader.get("/api/v1/trades"));
  const tool = await callTool(api.mcponly, "list_trades", { limit: 100000 });
  expect(tool.data.totalCount).toBe(rest.length);
  expect(tool.data.trades).toEqual(rest.slice(0, 100));

  const closed = await callTool(api.mcponly, "list_trades", { status: "closed", ticker: "xle" });
  expect(closed.data.trades).toEqual(await json<TradeDto[]>(await api.reader.get("/api/v1/trades?status=Closed&ticker=XLE")));

  const xle = rest.find((t) => t.ticker === "XLE" && t.status === "Closed")!;
  const one = await callTool(api.mcponly, "get_trade", { id: xle.id });
  expect(one.data.trade).toEqual(await json<TradeDto>(await api.reader.get(`/api/v1/trades/${xle.id}`)));
  expect(one.data.entryTrendSignal).toBeNull();
  expect(await callToolExpectingError(api.mcponly, "get_trade", { id: 999999 })).toContain("not found");
  expect(await callToolExpectingError(api.mcponly, "list_trades", { status: "Bogus" })).toContain("Unknown status");
});

test("size_position equals POST /sizer for the plan fixtures", async ({ api }) => {
  const fixtures = [
    { entry: 62.82, stop: 61.4, target: 65.9, shares: 15, verdict: "OK - log it" },
    { entry: 769.64, stop: 753.5, target: 773.5, shares: 1, verdict: "SKIP - reward:risk below minimum" },
    { entry: 62.34, stop: 61.4, target: 65.9, shares: 16, verdict: "OK - log it" },
    { entry: 62.34, stop: 61.4, target: 65.9, ticker: "XLE", shares: 16, verdict: "OK - log it" },
  ];
  for (const { shares, verdict, ...input } of fixtures) {
    const rest = await json<SizerResultDto>(await api.reader.post("/api/v1/sizer", { data: input }));
    const tool = await callTool<{ entrySource: string; sizing: SizerResultDto }>(api.mcponly, "size_position", input);
    expect(tool.data.sizing).toEqual(rest);
    expect(tool.data.entrySource).toBe("provided");
    expect(tool.data.sizing.shares).toBe(shares);
    expect(tool.data.sizing.verdict).toBe(verdict);
  }

  const xle = await callTool<{ sizing: SizerResultDto }>(api.mcponly, "size_position", { entry: 62.82, stop: 61.4, target: 65.9 });
  expect(xle.data.sizing.positionCost).toBe(942.3);
  expect(xle.data.sizing.riskDollars).toBe(21.3);
  expect(xle.data.sizing.rewardRisk).toBeCloseTo(2.169, 3);
  expect(xle.data.sizing.maxEntryForMinRewardRisk).toBeCloseTo(62.9, 2);

  expect(await callToolExpectingError(api.mcponly, "size_position", { stop: 61.4, target: 65.9 })).toContain("entry");
});

test("get_settings, get_refresh_status and list_notes equal their REST counterparts", async ({ api }) => {
  const settings = await callTool(api.mcponly, "get_settings");
  expect(settings.data.settings).toEqual(await json<SettingsDto>(await api.reader.get("/api/v1/settings")));
  expect(settings.data.movingAverageDays).toEqual({ short: 20, long: 50 });

  const status = await callTool(api.mcponly, "get_refresh_status");
  const runs = await json<PagedResult<RefreshRunDto>>(await api.reader.get("/api/v1/refresh/runs?page=1&pageSize=1"));
  expect(status.data.lastRun).toEqual(runs.items[0]);
  const watch = await json<WatchlistRowDto[]>(await api.reader.get("/api/v1/watchlist"));
  expect(status.data.tickers.map((t: { ticker: string }) => t.ticker).sort()).toEqual(watch.map((r) => r.ticker).sort());
  expect(status.data.nextScheduled.at).toBeTruthy();

  const notes = await callTool(api.mcponly, "list_notes");
  const restNotes = await json<NoteDto[]>(await api.reader.get("/api/v1/notes"));
  expect(notes.data.totalCount).toBe(restNotes.length);
  expect(notes.data.notes).toEqual(restNotes.slice(0, 100));
  expect(await callToolExpectingError(api.mcponly, "list_notes", { kind: "Bogus" })).toContain("Unknown kind");
});

test("journal://rules is plain text built from the live settings", async ({ api }) => {
  const resources = await rpc(api.mcponly, "resources/list", {});
  expect(resources.result.resources.map((r: { uri: string }) => r.uri)).toEqual(["journal://rules"]);

  const read = await rpc(api.mcponly, "resources/read", { uri: "journal://rules" });
  const text = read.result.contents[0].text as string;
  const settings = await json<SettingsDto>(await api.reader.get("/api/v1/settings"));

  expect(read.result.contents[0].mimeType).toBe("text/plain");
  for (const phrase of ["Pullback candidate", "Extended - wait", "OK - log it", "MaxEntryForMinRR", "WashSaleWindowDays", "MA20", "MA50"]) {
    expect(text, phrase).toContain(phrase);
  }
  expect(text).toContain(`StartingCapital = ${settings.startingCapital}`);
  expect(text).toContain(`MaxRiskPct = ${settings.maxRiskPct}`);
  expect(text).toContain(`PullbackBandLow = ${settings.pullbackBandLow}`);
  expect(text).toContain(`PullbackBandHigh = ${settings.pullbackBandHigh}`);
  expect(text).toContain(`ExtendedThreshold = ${settings.extendedThreshold}`);
  expect(text).toContain(`MinRewardRisk = ${settings.minRewardRisk}`);
});

test("prompts/list has weekly_review and trade_postmortem, and both can be fetched", async ({ api }) => {
  const list = await rpc(api.mcponly, "prompts/list", {});
  expect(list.result.prompts.map((p: { name: string }) => p.name).sort()).toEqual(["trade_postmortem", "weekly_review"]);

  const weekly = await rpc(api.mcponly, "prompts/get", { name: "weekly_review" });
  expect(weekly.result.messages[0].content.text).toContain("size_position");

  const trades = await json<TradeDto[]>(await api.reader.get("/api/v1/trades?status=Closed"));
  const post = await rpc(api.mcponly, "prompts/get", { name: "trade_postmortem", arguments: { tradeId: String(trades[0].id) } });
  expect(post.result.messages[0].content.text).toContain(`trade ${trades[0].id}`);
});

test("a request with no token gets 401 and a resource_metadata challenge", async ({ api }) => {
  const response = await postRpc(api.anonymous, "tools/list", {});
  expect(response.status()).toBe(401);
  expect(response.headers()["www-authenticate"]).toBe(`Bearer resource_metadata="${METADATA_URL}"`);
});

test("the protected resource metadata is public and names the MCP URL and the issuer", async ({ api }) => {
  for (const path of [METADATA_PATH, `${METADATA_PATH}/mcp`]) {
    const response = await api.anonymous.get(path);
    expect(response.status(), path).toBe(200);
    const metadata = await response.json();
    expect(metadata.resource).toBe(PUBLIC_MCP_URL);
    expect(metadata.authorization_servers[0]).toBe(claimsOf(await mintToken("mcponly"))["iss"]);
    expect(metadata.scopes_supported).toContain("swingtrade");
    expect(metadata.bearer_methods_supported).toEqual(["header"]);
  }
});

test("tokens that are not the owner's mcp token are refused", async ({ api, playwright }) => {
  for (const persona of ["norole", "reader", "trader", "admin"] as const) {
    expect((await postRpc(api.as(persona), "tools/list", {})).status(), `${persona} has no mcp role`).toBe(403);
  }

  const otherToken = await mintToken(MCP_OTHER_USER);
  const other = await playwright.request.newContext({
    baseURL: BASE_URL,
    extraHTTPHeaders: { Authorization: `Bearer ${otherToken}` },
  });
  const claims = claimsOf(otherToken);
  expect(claims["role"]).toBe("swingtrade.mcp");
  expect(claims["sub"]).not.toBe(OWNER_SUBJECT);
  expect((await postRpc(other, "tools/list", {})).status(), "mcp role but not the owner").toBe(403);
  await other.dispose();

  expect((await postRpc(api.wrongAudience, "tools/list", {})).status(), "wrong audience").toBe(401);
  expect((await postRpc(api.expired, "tools/list", {})).status(), "expired").toBe(401);

  const owner = await postRpc(api.mcponly, "tools/list", {});
  expect(owner.status()).toBe(200);
  expect(claimsOf(await mintToken("mcponly"))["sub"]).toBe(OWNER_SUBJECT);
});

test("a token in the query string is not accepted", async ({ api }) => {
  const token = await mintToken("mcponly");
  for (const name of ["access_token", "token"]) {
    const response = await postRpc(api.anonymous, "tools/list", {}, `${MCP_PATH}?${name}=${token}`);
    expect(response.status(), name).toBe(401);
  }
});

test("MCP routes refuse a Host that is not allowlisted, and REST routes do not use that list", async ({ api, playwright }) => {
  const token = await mintToken("mcponly");
  const ctx = await playwright.request.newContext({
    baseURL: BASE_URL,
    extraHTTPHeaders: { Authorization: `Bearer ${token}`, Host: "evil.example.test" },
  });
  expect((await postRpc(ctx, "tools/list", {})).status()).toBe(421);
  expect((await ctx.get(METADATA_PATH)).status()).toBe(421);
  await ctx.dispose();
  expect((await api.anonymous.get("/livez")).status()).toBe(200);
});

test("the REST MCP info endpoint reports the endpoint facts", async ({ api }) => {
  const info = await json<{ endpointUrl: string; ownerSubject: string; addNoteEnabled: boolean; requiredRole: string }>(
    await api.reader.get("/api/v1/mcp/info"),
  );
  expect(info).toMatchObject({ endpointUrl: PUBLIC_MCP_URL, ownerSubject: OWNER_SUBJECT, addNoteEnabled: false, requiredRole: "swingtrade.mcp" });
});

// Keep this last: it reads the audit trail the specs above produced.
test("every MCP call was logged for the owner, and no token was stored", async ({ api }) => {
  const calls = await json<{ tool: string; arguments: string | null; caller: string | null; status: string; durationMs: number; at: string }[]>(
    await api.admin.get("/api/v1/mcp/calls?take=200"),
  );

  const tools = new Set(calls.map((c) => c.tool));
  for (const name of [...MCP_READ_TOOLS, "resource:journal://rules", "prompt:weekly_review", "prompt:trade_postmortem"]) {
    expect(tools, name).toContain(name);
  }
  expect(calls.filter((c) => c.tool === "get_instrument" && c.status === "error").length).toBeGreaterThan(0);
  expect(new Set(calls.map((c) => c.caller))).toEqual(new Set([OWNER_SUBJECT]));
  expect(calls.every((c) => c.durationMs >= 0 && !Number.isNaN(Date.parse(c.at)))).toBe(true);
  expect(calls.some((c) => c.arguments?.includes("SPY"))).toBe(true);

  const stored = JSON.stringify(calls);
  expect(stored).not.toContain(await mintToken("mcponly"));
  expect(stored).not.toContain(await mintToken(MCP_OTHER_USER));
  expect(stored).not.toContain("eyJ");
  expect(stored.toLowerCase()).not.toContain("bearer");

  expect((await api.reader.get("/api/v1/mcp/calls")).status()).toBe(403);
});
