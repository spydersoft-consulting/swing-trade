// The full identity x endpoint matrix: every endpoint is hit by every persona and by the three bad-token cases.
//   norole, mcponly                 -> 403 on every REST endpoint (a valid token is necessary but not sufficient)
//   reader / trader / admin         -> allowed when the endpoint's policy needs that role or a lower one
//   anonymous, wrong audience, expired -> 401
// "Allowed" requests are deliberately harmless (an id that does not exist, an empty body), so a pass means the policy
// let the call through and the endpoint itself answered 400 or 404, never that it changed data. The exception is
// POST /refresh, which really starts a run; the spec waits for it.
import { PERSONAS, claimsOf, mintExpiredToken, mintToken, mintWrongAudienceToken, type Persona } from "./auth";
import { expect, test, waitForRun, type Api } from "./fixtures";
import type { RefreshRunDto } from "./types";

type Level = "reader" | "trader" | "admin";
const RANK: Record<Persona, number> = { norole: -1, reader: 0, trader: 1, admin: 2, mcponly: -1 };
const LEVEL_RANK: Record<Level, number> = { reader: 0, trader: 1, admin: 2 };

interface Endpoint {
  name: string;
  method: "GET" | "POST" | "PUT" | "DELETE";
  path: string;
  data?: unknown;
  min: Level;
  /** Statuses that mean "the policy let the call through". */
  allowed: number[];
}

const body = { ticker: "XLK", setup: "Pullback", shares: 1, entryPrice: 2, stopPrice: 1, targetPrice: 4 };

const ENDPOINTS: Endpoint[] = [
  { name: "GET /settings", method: "GET", path: "/api/v1/settings", min: "reader", allowed: [200] },
  { name: "PUT /settings", method: "PUT", path: "/api/v1/settings", data: {}, min: "admin", allowed: [400] },
  { name: "GET /instruments", method: "GET", path: "/api/v1/instruments", min: "reader", allowed: [200] },
  { name: "POST /instruments", method: "POST", path: "/api/v1/instruments", data: {}, min: "trader", allowed: [400] },
  { name: "PUT /instruments/{ticker}", method: "PUT", path: "/api/v1/instruments/NOPE9", data: { name: "x" }, min: "trader", allowed: [404] },
  { name: "GET /instruments/{ticker}/bars", method: "GET", path: "/api/v1/instruments/SPY/bars", min: "reader", allowed: [200] },
  { name: "GET /watchlist", method: "GET", path: "/api/v1/watchlist", min: "reader", allowed: [200] },
  { name: "GET /trades", method: "GET", path: "/api/v1/trades", min: "reader", allowed: [200] },
  { name: "POST /trades", method: "POST", path: "/api/v1/trades", data: {}, min: "trader", allowed: [400] },
  { name: "GET /trades/{id}", method: "GET", path: "/api/v1/trades/999999", min: "reader", allowed: [404] },
  { name: "PUT /trades/{id}", method: "PUT", path: "/api/v1/trades/999999", data: body, min: "trader", allowed: [404] },
  { name: "DELETE /trades/{id}", method: "DELETE", path: "/api/v1/trades/999999", min: "trader", allowed: [404] },
  { name: "POST /trades/{id}/fill", method: "POST", path: "/api/v1/trades/999999/fill", data: { fillPrice: 2, fillDate: "2026-10-09" }, min: "trader", allowed: [404] },
  { name: "POST /trades/{id}/close", method: "POST", path: "/api/v1/trades/999999/close", data: { exitPrice: 2, exitDate: "2026-10-09" }, min: "trader", allowed: [404] },
  { name: "POST /trades/{id}/cancel", method: "POST", path: "/api/v1/trades/999999/cancel", min: "trader", allowed: [404] },
  { name: "POST /sizer", method: "POST", path: "/api/v1/sizer", data: {}, min: "reader", allowed: [400] },
  { name: "GET /dashboard", method: "GET", path: "/api/v1/dashboard", min: "reader", allowed: [200] },
  { name: "GET /notes", method: "GET", path: "/api/v1/notes", min: "reader", allowed: [200] },
  { name: "POST /notes", method: "POST", path: "/api/v1/notes", data: {}, min: "trader", allowed: [400] },
  { name: "GET /refresh/runs", method: "GET", path: "/api/v1/refresh/runs", min: "reader", allowed: [200] },
  { name: "GET /refresh/runs/{id}", method: "GET", path: "/api/v1/refresh/runs/999999", min: "reader", allowed: [404] },
  { name: "POST /refresh", method: "POST", path: "/api/v1/refresh", min: "admin", allowed: [202, 409] },
  { name: "GET /export/trades.csv", method: "GET", path: "/api/v1/export/trades.csv", min: "reader", allowed: [200] },
  { name: "GET /mcp/info", method: "GET", path: "/api/v1/mcp/info", min: "reader", allowed: [200] },
  { name: "GET /mcp/calls", method: "GET", path: "/api/v1/mcp/calls", min: "admin", allowed: [200] },
  { name: "GET /auth/info", method: "GET", path: "/api/v1/auth/info", min: "reader", allowed: [200] },
];

const call = (ctx: Api["admin"], e: Endpoint) => ctx.fetch(e.path, { method: e.method, data: e.data });

for (const endpoint of ENDPOINTS) {
  test(`role matrix: ${endpoint.name}`, async ({ api }) => {
    const started: number[] = [];

    for (const persona of PERSONAS) {
      const response = await call(api.as(persona), endpoint);
      const permitted = RANK[persona] >= LEVEL_RANK[endpoint.min];
      if (permitted) {
        expect(endpoint.allowed, `${persona} on ${endpoint.name} got ${response.status()}`).toContain(response.status());
        if (endpoint.path === "/api/v1/refresh" && response.status() === 202) started.push(((await response.json()) as RefreshRunDto).id);
      } else {
        expect(response.status(), `${persona} on ${endpoint.name}`).toBe(403);
      }
    }

    for (const [label, ctx] of [["anonymous", api.anonymous], ["wrong audience", api.wrongAudience], ["expired token", api.expired]] as const) {
      const response = await call(ctx, endpoint);
      expect(response.status(), `${label} on ${endpoint.name}`).toBe(401);
      expect(response.headers()["www-authenticate"] ?? "", label).toMatch(/^Bearer/i);
    }

    for (const id of started) await waitForRun(api.admin, id);
  });
}

test("each persona's token carries what the matrix assumes", async () => {
  const expectedRole: Record<Persona, string | undefined> = {
    norole: undefined,
    reader: "swingtrade.reader",
    trader: "swingtrade.trader",
    admin: "swingtrade.admin",
    mcponly: "swingtrade.mcp",
  };
  for (const persona of PERSONAS) {
    const claims = claimsOf(await mintToken(persona));
    expect(claims["aud"], `${persona} aud`).toEqual("swingtrade");
    expect(claims["sub"], `${persona} sub`).toBeTruthy();
    expect(claims["iss"], `${persona} iss`).toBe("http://localhost:8210");
    expect(claims["role"], `${persona} role`).toEqual(expectedRole[persona]);
  }

  const wrong = claimsOf(await mintWrongAudienceToken());
  expect(wrong["aud"]).toBe("other-api");
  expect(wrong["role"]).toBe("swingtrade.admin"); // the role is right; only the audience is wrong

  const expired = claimsOf(await mintExpiredToken());
  expect(Number(expired["exp"])).toBeLessThan(Date.now() / 1000);
});

test("a token that was not issued by the authority is rejected (signature is checked)", async ({ playwright }) => {
  const real = await mintToken("admin");
  const [header, payload] = real.split(".");
  const forged = `${header}.${payload}.${Buffer.from("not-the-signature").toString("base64url")}`;
  const ctx = await playwright.request.newContext({ baseURL: process.env.SWINGTRADE_BASE_URL ?? "http://localhost:8180", extraHTTPHeaders: { Authorization: `Bearer ${forged}` } });

  expect((await ctx.get("/api/v1/dashboard")).status()).toBe(401);
  await ctx.dispose();
});

test("health checks need no token", async ({ api }) => {
  expect((await api.anonymous.get("/livez")).status()).toBe(200);
  expect([200, 503]).toContain((await api.anonymous.get("/readyz")).status());
});

test("an unknown route is 404 for an authenticated caller and never reveals data", async ({ api }) => {
  expect((await api.admin.get("/api/v1/nothing-here")).status()).toBe(404);
});

test("GET /auth/info reports each persona's roles and what the UI may offer", async ({ api }) => {
  const expected: Array<[Persona, string[], boolean, boolean]> = [
    ["reader", ["swingtrade.reader"], false, false],
    ["trader", ["swingtrade.trader"], true, false],
    ["admin", ["swingtrade.admin"], true, true],
  ];
  for (const [persona, roles, canWrite, canAdmin] of expected) {
    const response = await api.as(persona).get("/api/v1/auth/info");
    expect(response.status(), persona).toBe(200);
    const info = await response.json();
    expect(info.roles, persona).toEqual(roles);
    expect(info.canRead, persona).toBe(true);
    expect(info.canWrite, persona).toBe(canWrite);
    expect(info.canAdmin, persona).toBe(canAdmin);
    expect(info.audience).toBe("swingtrade");
    expect(info.adminRole).toBe("swingtrade.admin");
    expect(info.subject, persona).toBeTruthy();
  }
});
