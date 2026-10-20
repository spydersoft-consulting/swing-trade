// A minimal MCP client over Streamable HTTP: JSON-RPC 2.0 in a POST, answered either as JSON or as one SSE frame.
// The API runs the MCP server stateless, so there is no initialize handshake or session to manage: every call stands alone.
import type { APIRequestContext, APIResponse } from "@playwright/test";

export const MCP_PATH = "/mcp";
export const METADATA_PATH = "/.well-known/oauth-protected-resource";

export const MCP_READ_TOOLS = [
  "get_dashboard",
  "get_watchlist",
  "get_instrument",
  "get_bars",
  "list_trades",
  "get_trade",
  "size_position",
  "get_settings",
  "get_refresh_status",
  "list_notes",
  "get_levels",
] as const;

export interface RpcResponse {
  jsonrpc: "2.0";
  id: number | string | null;
  result?: any;
  error?: { code: number; message: string };
}

export interface Envelope<T = any> {
  asOf: string | null;
  dataStatus: "fresh" | "stale";
  settings: Record<string, unknown>;
  data: T;
}

let nextId = 1;

export const mcpHeaders = { Accept: "application/json, text/event-stream" };

/** Parses a response body that is JSON or an SSE stream whose first data line is the JSON-RPC message. */
export function parseRpc(text: string): RpcResponse {
  const trimmed = text.trimStart();
  if (trimmed.startsWith("{")) return JSON.parse(trimmed) as RpcResponse;
  const data = trimmed
    .split(/\r?\n/)
    .filter((line) => line.startsWith("data:"))
    .map((line) => line.slice(5).trim())
    .find((line) => line.length > 0);
  if (!data) throw new Error(`No JSON-RPC message in the response: ${text.slice(0, 200)}`);
  return JSON.parse(data) as RpcResponse;
}

/** The raw POST, for tests that care about the HTTP status and headers. */
export function postRpc(ctx: APIRequestContext, method: string, params?: unknown, path = MCP_PATH): Promise<APIResponse> {
  return ctx.post(path, { headers: mcpHeaders, data: { jsonrpc: "2.0", id: nextId++, method, params } });
}

export async function rpc(ctx: APIRequestContext, method: string, params?: unknown): Promise<RpcResponse> {
  const response = await postRpc(ctx, method, params);
  const text = await response.text();
  if (response.status() !== 200) throw new Error(`${method} -> HTTP ${response.status()}: ${text}`);
  return parseRpc(text);
}

/** Calls a tool and returns the parsed envelope. Throws if the tool reports an error. */
export async function callTool<T = any>(ctx: APIRequestContext, name: string, args: Record<string, unknown> = {}): Promise<Envelope<T>> {
  const response = await rpc(ctx, "tools/call", { name, arguments: args });
  if (response.error || response.result?.isError) {
    throw new Error(`${name} failed: ${JSON.stringify(response.error ?? response.result)}`);
  }
  return JSON.parse(response.result.content[0].text) as Envelope<T>;
}

/** Calls a tool that is expected to fail and returns the error text. */
export async function callToolExpectingError(ctx: APIRequestContext, name: string, args: Record<string, unknown> = {}): Promise<string> {
  const response = await rpc(ctx, "tools/call", { name, arguments: args });
  if (response.error) return response.error.message;
  if (!response.result?.isError) throw new Error(`${name} unexpectedly succeeded.`);
  return response.result.content[0].text as string;
}
