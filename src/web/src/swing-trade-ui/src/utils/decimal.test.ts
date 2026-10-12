import { describe, expect, it } from "vitest";
import { fractionToPercentText, parseDecimal, percentTextToFraction, shiftDecimal } from "./decimal";

describe("parseDecimal", () => {
  it("accepts plain decimals", () => {
    expect(parseDecimal("62.82")).toBe(62.82);
    expect(parseDecimal(" 5 ")).toBe(5);
    expect(parseDecimal("-1")).toBe(-1);
    expect(parseDecimal(".5")).toBe(0.5);
  });

  it("rejects everything else", () => {
    for (const text of ["", "abc", "1e3", "1,5", "1.2.3", "--1", "$5"]) {
      expect(parseDecimal(text), text).toBeNull();
    }
  });
});

describe("shiftDecimal moves the decimal point in the text", () => {
  it.each([
    ["0.015", 2, "1.5"],
    ["0.07", 2, "7"],
    ["0.27", 2, "27"],
    ["-0.01", 2, "-1"],
    ["0.5", 2, "50"],
    ["1.5", -2, "0.015"],
    ["7", -2, "0.07"],
    ["27", -2, "0.27"],
    ["-1", -2, "-0.01"],
    ["100", -2, "1"],
    ["0", 2, "0"],
    ["12.50", 1, "125"],
  ])("%s shifted by %i is %s", (input, places, expected) => {
    expect(shiftDecimal(input, places)).toBe(expected);
  });

  it("leaves non-numbers alone", () => {
    expect(shiftDecimal("abc", 2)).toBe("abc");
  });
});

describe("percent <-> fraction", () => {
  it("shows a stored fraction as percent text without floating-point noise", () => {
    expect(fractionToPercentText(0.015)).toBe("1.5");
    expect(fractionToPercentText(0.07)).toBe("7");
    expect(fractionToPercentText(-0.01)).toBe("-1");
    expect(fractionToPercentText(0.5)).toBe("50");
  });

  it("turns percent text back into the fraction the API stores", () => {
    expect(percentTextToFraction("1.5")).toBe(0.015);
    expect(percentTextToFraction("7")).toBe(0.07);
    expect(percentTextToFraction("-1")).toBe(-0.01);
    expect(percentTextToFraction("x")).toBeNull();
  });
});
