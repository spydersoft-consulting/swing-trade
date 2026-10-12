// The OpenAPI document the running API serves (Testing exposes it at /openapi/v1.json) must contain every route in the
// plan's API table, and must agree with the committed copy at src/api/openapi/swing-trade.json (regenerated with
// `dotnet build src/api/src/Spydersoft.SwingTrade.Api -p:GenerateOpenApi=true`).
import { readFileSync } from "node:fs";
import path from "node:path";
import { expect, test } from "./fixtures";

const PLAN_TABLE = [
  "GET /api/v1/settings", "PUT /api/v1/settings",
  "GET /api/v1/instruments", "POST /api/v1/instruments", "PUT /api/v1/instruments/{ticker}",
  "GET /api/v1/watchlist",
  "GET /api/v1/instruments/{ticker}/bars",
  "GET /api/v1/trades", "POST /api/v1/trades",
  "GET /api/v1/trades/{id}", "PUT /api/v1/trades/{id}", "DELETE /api/v1/trades/{id}",
  "POST /api/v1/trades/{id}/fill", "POST /api/v1/trades/{id}/close", "POST /api/v1/trades/{id}/cancel",
  "POST /api/v1/sizer",
  "GET /api/v1/dashboard",
  "GET /api/v1/notes", "POST /api/v1/notes",
  "GET /api/v1/refresh/runs", "POST /api/v1/refresh", "GET /api/v1/refresh/runs/{id}",
  "GET /api/v1/export/trades.csv",
  "GET /api/v1/mcp/info", "GET /api/v1/mcp/calls", "GET /api/v1/auth/info",
];

interface OpenApiDoc {
  paths: Record<string, Record<string, unknown>>;
}

const operations = (doc: OpenApiDoc) =>
  Object.entries(doc.paths).flatMap(([route, verbs]) => Object.keys(verbs).map((verb) => `${verb.toUpperCase()} ${route}`));

test("the served OpenAPI document contains every route in the API table", async ({ api }) => {
  const response = await api.anonymous.get("/openapi/v1.json");
  expect(response.status()).toBe(200);

  const served = operations((await response.json()) as OpenApiDoc);

  expect(served.sort()).toEqual([...PLAN_TABLE].sort());
});

test("the committed spec equals the served document's operations", async ({ api }) => {
  const committed = JSON.parse(
    readFileSync(path.resolve(import.meta.dirname, "../../../openapi/swing-trade.json"), "utf-8"),
  ) as OpenApiDoc;
  const served = (await (await api.anonymous.get("/openapi/v1.json")).json()) as OpenApiDoc;

  expect(operations(committed).sort()).toEqual(operations(served).sort());
});
