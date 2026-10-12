import { describe, expect, it } from "vitest";
import {
  EMPTY,
  formatCount,
  formatDate,
  formatDateTime,
  formatDays,
  formatMoney,
  formatPercent,
  formatR,
  formatRatio,
  formatSignedPercent,
  pnlClass,
} from "./format";

describe("formatMoney (money is shown to the cent)", () => {
  it("always shows two decimals and thousands separators", () => {
    expect(formatMoney(2053.55)).toBe("$2,053.55");
    expect(formatMoney(942.3)).toBe("$942.30");
    expect(formatMoney(0)).toBe("$0.00");
    expect(formatMoney(1234567.891)).toBe("$1,234,567.89");
  });

  it("rounds to the cent for display only", () => {
    expect(formatMoney(14.4585)).toBe("$14.46");
    expect(formatMoney(39.0915)).toBe("$39.09");
  });

  it("shows losses with a leading minus", () => {
    expect(formatMoney(-12.3)).toBe("-$12.30");
  });

  it("shows an em dash for a missing value", () => {
    expect(formatMoney(null)).toBe(EMPTY);
    expect(formatMoney(undefined)).toBe(EMPTY);
    expect(formatMoney(Number.NaN)).toBe(EMPTY);
  });
});

describe("formatPercent (percentages are shown to one decimal)", () => {
  it("converts a fraction to a percentage with one decimal", () => {
    expect(formatPercent(0.015)).toBe("1.5%");
    expect(formatPercent(0.00705)).toBe("0.7%");
    expect(formatPercent(1)).toBe("100.0%");
    expect(formatPercent(0)).toBe("0.0%");
    expect(formatPercent(0.026775)).toBe("2.7%");
  });

  it("formatSignedPercent signs non-zero values", () => {
    expect(formatSignedPercent(0.00629)).toBe("+0.6%");
    expect(formatSignedPercent(-0.01307)).toBe("-1.3%");
    expect(formatSignedPercent(0)).toBe("0.0%");
  });

  it("shows an em dash for a missing value", () => {
    expect(formatPercent(null)).toBe(EMPTY);
    expect(formatSignedPercent(undefined)).toBe(EMPTY);
  });
});

describe("ratios, R-multiples, counts", () => {
  it("formats ratio and R to two decimals", () => {
    expect(formatRatio(2.169014)).toBe("2.17");
    expect(formatR(3.797872)).toBe("3.80R");
    expect(formatR(-1)).toBe("-1.00R");
    expect(formatRatio(null)).toBe(EMPTY);
    expect(formatR(null)).toBe(EMPTY);
  });

  it("formats whole counts and days held", () => {
    expect(formatCount(1234)).toBe("1,234");
    expect(formatCount(null)).toBe(EMPTY);
    expect(formatDays(7)).toBe("7.0");
    expect(formatDays(null)).toBe(EMPTY);
  });
});

describe("dates", () => {
  it("formats a calendar date without a time zone shift", () => {
    expect(formatDate("2026-10-09")).toBe("Oct 9, 2026");
    expect(formatDate("2026-01-01")).toBe("Jan 1, 2026");
    expect(formatDate("2026-12-31")).toBe("Dec 31, 2026");
  });

  it("shows an em dash when there is no date, and passes an unrecognised string through", () => {
    expect(formatDate(null)).toBe(EMPTY);
    expect(formatDate(undefined)).toBe(EMPTY);
    expect(formatDate("")).toBe(EMPTY);
    expect(formatDate("soon")).toBe("soon");
  });

  it("formats an instant in local time and tolerates junk", () => {
    expect(formatDateTime("2026-10-12T14:30:00Z")).toMatch(/Oct 12, 2026/);
    expect(formatDateTime(null)).toBe(EMPTY);
    expect(formatDateTime("not a date")).toBe("not a date");
  });
});

describe("pnlClass", () => {
  it("is green for gains, red for losses and neutral otherwise", () => {
    expect(pnlClass(1)).toBe("text-good");
    expect(pnlClass(-1)).toBe("text-bad");
    expect(pnlClass(0)).toBe("text-content");
    expect(pnlClass(null)).toBe("text-content");
  });
});
