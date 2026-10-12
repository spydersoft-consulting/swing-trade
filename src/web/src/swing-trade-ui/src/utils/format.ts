// Formatting only. Every number shown in the UI is computed by the API; nothing here derives a new value
// (the rules live in Spydersoft.SwingTrade.Rules). Money is shown to cents and percentages to one decimal.

export const EMPTY = "—";

const money = new Intl.NumberFormat("en-US", {
  style: "currency",
  currency: "USD",
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});
const percent = new Intl.NumberFormat("en-US", {
  style: "percent",
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});
const signedPercent = new Intl.NumberFormat("en-US", {
  style: "percent",
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
  signDisplay: "exceptZero",
});
const ratio = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const wholeNumber = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });
const oneDecimal = new Intl.NumberFormat("en-US", { minimumFractionDigits: 1, maximumFractionDigits: 1 });

type Nullable = number | null | undefined;

const isMissing = (value: Nullable): value is null | undefined =>
  value === null || value === undefined || Number.isNaN(value);

/** $1,234.56 (negatives as -$12.30); a missing value is an em dash. */
export const formatMoney = (value: Nullable): string => (isMissing(value) ? EMPTY : money.format(value));

/** A fraction (0.0063) as a percentage to one decimal: 0.6%. */
export const formatPercent = (value: Nullable): string => (isMissing(value) ? EMPTY : percent.format(value));

/** Same, with an explicit sign for non-zero values: +0.6%, -1.0%. */
export const formatSignedPercent = (value: Nullable): string =>
  isMissing(value) ? EMPTY : signedPercent.format(value);

/** A ratio such as reward:risk to two decimals: 2.17. */
export const formatRatio = (value: Nullable): string => (isMissing(value) ? EMPTY : ratio.format(value));

/** An R-multiple: 3.80R. */
export const formatR = (value: Nullable): string => (isMissing(value) ? EMPTY : `${ratio.format(value)}R`);

export const formatCount = (value: Nullable): string => (isMissing(value) ? EMPTY : wholeNumber.format(value));

export const formatDays = (value: Nullable): string => (isMissing(value) ? EMPTY : oneDecimal.format(value));

const dateFormat = new Intl.DateTimeFormat("en-US", {
  year: "numeric",
  month: "short",
  day: "numeric",
  timeZone: "UTC",
});

/** A calendar date ("2026-10-09") as "Oct 9, 2026", without any time zone shift. */
export function formatDate(iso: string | null | undefined): string {
  if (!iso) return EMPTY;
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
  if (!match) return iso;
  return dateFormat.format(new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]))));
}

const dateTimeFormat = new Intl.DateTimeFormat("en-US", { dateStyle: "medium", timeStyle: "short" });

/** An instant (ISO timestamp) in the viewer's local time. */
export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return EMPTY;
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : dateTimeFormat.format(date);
}

/** Positive/negative colour class for P&L style numbers: green, red, neutral at zero or missing. */
export const pnlClass = (value: Nullable): string => {
  if (isMissing(value) || value === 0) return "text-content";
  return value > 0 ? "text-good" : "text-bad";
};
