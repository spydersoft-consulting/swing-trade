import { describe, expect, it } from "vitest";
import { todayInNewYork, weekStart } from "./dates";
import { getConfig } from "./Config";
import { ROLE_NAMES, roleSatisfies } from "./roles";

describe("todayInNewYork", () => {
  it("uses the New York calendar day, not the viewer's", () => {
    // 03:30 UTC on the 13th is still the evening of the 12th in New York (EDT).
    expect(todayInNewYork(new Date("2026-10-13T03:30:00Z"))).toBe("2026-10-12");
    expect(todayInNewYork(new Date("2026-10-13T12:00:00Z"))).toBe("2026-10-13");
  });
});

describe("weekStart", () => {
  it("is the Monday of the week", () => {
    expect(weekStart("2026-10-12")).toBe("2026-10-12"); // a Monday
    expect(weekStart("2026-10-14")).toBe("2026-10-12");
    expect(weekStart("2026-10-18")).toBe("2026-10-12"); // Sunday belongs to the week before the next Monday
    expect(weekStart("2026-10-19")).toBe("2026-10-19");
  });

  it("crosses month and year boundaries", () => {
    expect(weekStart("2026-11-01")).toBe("2026-10-26");
    expect(weekStart("2027-01-01")).toBe("2026-12-28");
  });
});

describe("getConfig", () => {
  it("returns the default and an override from the BFF's /config.js", () => {
    globalThis.__config = undefined;
    expect(getConfig("api_url")).toBe("/api/v1");
    globalThis.__config = { api_url: "https://example.test/api/v1" };
    expect(getConfig("api_url")).toBe("https://example.test/api/v1");
    globalThis.__config = {};
    expect(getConfig("api_url")).toBe("/api/v1");
    globalThis.__config = undefined;
  });
});

describe("roleSatisfies (display gating that mirrors the API's hierarchy)", () => {
  it("lets admin satisfy trader and reader, and trader satisfy reader", () => {
    expect(roleSatisfies([ROLE_NAMES.admin], ROLE_NAMES.reader)).toBe(true);
    expect(roleSatisfies([ROLE_NAMES.admin], ROLE_NAMES.trader)).toBe(true);
    expect(roleSatisfies([ROLE_NAMES.admin], ROLE_NAMES.admin)).toBe(true);
    expect(roleSatisfies([ROLE_NAMES.trader], ROLE_NAMES.reader)).toBe(true);
    expect(roleSatisfies([ROLE_NAMES.trader], ROLE_NAMES.admin)).toBe(false);
    expect(roleSatisfies([ROLE_NAMES.reader], ROLE_NAMES.trader)).toBe(false);
  });

  it("gives nothing to a user with no role, and treats the mcp role as independent", () => {
    expect(roleSatisfies([], ROLE_NAMES.reader)).toBe(false);
    expect(roleSatisfies(["swingtrade.mcp"], ROLE_NAMES.reader)).toBe(false);
    expect(roleSatisfies(["swingtrade.mcp"], "swingtrade.mcp")).toBe(true);
    expect(roleSatisfies([ROLE_NAMES.admin], "swingtrade.mcp")).toBe(false);
  });
});
