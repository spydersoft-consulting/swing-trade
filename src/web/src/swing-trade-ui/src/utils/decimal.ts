// The API stores percentages as fractions (0.015 is 1.5%). The settings form shows percent. These helpers move the
// decimal point in the text, so no floating-point arithmetic can turn 7 into 7.000000000000001.

const DECIMAL = /^-?\d*\.?\d+$/;

/** Parses a plain decimal such as "62.82" or "-1". Anything else (empty, "1e3", "abc", "1,5") is null. */
export function parseDecimal(text: string): number | null {
  const trimmed = text.trim();
  if (!DECIMAL.test(trimmed)) return null;
  const value = Number(trimmed);
  return Number.isFinite(value) ? value : null;
}

/** Moves the decimal point by `places` (positive = to the right) in the text of a number. */
export function shiftDecimal(text: string, places: number): string {
  const trimmed = text.trim();
  if (!DECIMAL.test(trimmed)) return trimmed;
  const negative = trimmed.startsWith("-");
  const unsigned = negative ? trimmed.slice(1) : trimmed;
  const [whole = "", fraction = ""] = unsigned.split(".");
  let digits = whole + fraction;
  let point = whole.length + places;
  if (point < 0) {
    digits = "0".repeat(-point) + digits;
    point = 0;
  } else if (point > digits.length) {
    digits += "0".repeat(point - digits.length);
  }
  const intPart = digits.slice(0, point).replace(/^0+(?=\d)/, "") || "0";
  const fracPart = digits.slice(point).replace(/0+$/, "");
  const result = fracPart ? `${intPart}.${fracPart}` : intPart;
  return negative && result !== "0" ? `-${result}` : result;
}

/** 0.015 -> "1.5" */
export const fractionToPercentText = (fraction: number): string => shiftDecimal(String(fraction), 2);

/** "1.5" -> 0.015 (null if the text is not a number) */
export const percentTextToFraction = (text: string): number | null => {
  const shifted = shiftDecimal(text, -2);
  return parseDecimal(shifted);
};
