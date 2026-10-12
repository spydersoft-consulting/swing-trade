// Token helper: mints real access tokens from the mock OIDC server (ghcr.io/soluto/oidc-server-mock) that the AppHost
// Testing profile starts. The API validates them exactly as it would a production token: issuer, signature (keys from the
// authority's discovery document), audience, lifetime. Users, clients and scopes are defined in
// src/Spydersoft.SwingTrade.AppHost/MockOidc.cs.

export const BASE_URL = process.env.SWINGTRADE_BASE_URL ?? "http://localhost:8180";
export const OIDC_URL = process.env.SWINGTRADE_OIDC_URL ?? "http://localhost:8210";

export const PERSONAS = ["norole", "reader", "trader", "admin", "mcponly"] as const;
export type Persona = (typeof PERSONAS)[number];

const PASSWORD = "Test123!";
const CLIENT_SECRET = "swingtrade-api-test-secret";
const CLIENT_ID = "swingtrade-api-test";
const SHORT_LIVED_CLIENT_ID = "swingtrade-api-test-short";

interface MintOptions {
  scope?: string;
  clientId?: string;
}

/** Resource-owner password grant for one persona. */
export async function mintToken(persona: Persona, options: MintOptions = {}): Promise<string> {
  const response = await fetch(`${OIDC_URL}/connect/token`, {
    method: "POST",
    headers: { "content-type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      grant_type: "password",
      client_id: options.clientId ?? CLIENT_ID,
      client_secret: CLIENT_SECRET,
      username: persona,
      password: PASSWORD,
      scope: options.scope ?? "swingtrade",
    }),
  });
  if (!response.ok) {
    throw new Error(`Could not mint a token for ${persona}: HTTP ${response.status} ${await response.text()}`);
  }
  const body = (await response.json()) as { access_token?: string };
  if (!body.access_token) throw new Error(`No access_token in the response for ${persona}.`);
  return body.access_token;
}

/** A validly signed token for the wrong audience ("other-api"), carrying the admin role. */
export const mintWrongAudienceToken = () => mintToken("admin", { scope: "other-api" });

/** A one-second token, already past its lifetime when this resolves (the Testing API allows no clock skew). */
export async function mintExpiredToken(): Promise<string> {
  const token = await mintToken("admin", { clientId: SHORT_LIVED_CLIENT_ID });
  await new Promise((resolve) => setTimeout(resolve, 2500));
  return token;
}

/** Decodes a JWT payload without verifying it (for assertions about the claims the mock issued). */
export function claimsOf(token: string): Record<string, unknown> {
  return JSON.parse(Buffer.from(token.split(".")[1], "base64url").toString("utf-8")) as Record<string, unknown>;
}
