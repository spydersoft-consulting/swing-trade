import type { RiskBasis, SettingsDto, UpdateSettingsRequest } from "../../api/generated/types.gen";
import { fractionToPercentText, parseDecimal, percentTextToFraction } from "../../utils/decimal";

export type NumericKey = Exclude<keyof SettingsDto, "riskBasis">;
type FieldKind = "money" | "percent" | "integer" | "ratio" | "days";

export interface FieldDef {
  key: NumericKey;
  label: string;
  kind: FieldKind;
  /** One line saying what the threshold does. */
  help: string;
  /** Flagged in the UI as an assumption the owner should confirm. */
  assumption?: boolean;
}

export const FIELDS: readonly FieldDef[] = [
  {
    key: "startingCapital",
    label: "Starting capital",
    kind: "money",
    help: "The account size that risk and position limits are measured against.",
  },
  {
    key: "maxRiskPct",
    label: "Max risk per trade",
    kind: "percent",
    help: "The most the stop-out loss of one trade may be, as a percent of capital.",
  },
  {
    key: "maxOpenPositions",
    label: "Max open positions",
    kind: "integer",
    help: "The sizer warns, and the dashboard flags it, when open trades reach this number.",
  },
  {
    key: "minRewardRisk",
    label: "Minimum reward : risk",
    kind: "ratio",
    help: "Trades whose planned reward divided by risk is lower are flagged, and the sizer says SKIP.",
  },
  {
    key: "taxSetAsideRate",
    label: "Tax set-aside rate",
    kind: "percent",
    help: "The share of realized gains shown as set aside for tax.",
    assumption: true,
  },
  {
    key: "washSaleWindowDays",
    label: "Wash-sale window",
    kind: "days",
    help: "Days either side of a losing exit in which re-entering the same ticker is flagged.",
  },
  {
    key: "maxPositionPct",
    label: "Max position size",
    kind: "percent",
    help: "No single position may cost more than this percent of capital.",
  },
  {
    key: "pullbackBandLow",
    label: "Pullback band, low",
    kind: "percent",
    help: "Lowest distance from the 20-day average (inclusive) that still counts as a pullback in an uptrend.",
  },
  {
    key: "pullbackBandHigh",
    label: "Pullback band, high",
    kind: "percent",
    help: "Highest distance above the 20-day average (inclusive) that still counts as a pullback in an uptrend.",
  },
  {
    key: "extendedThreshold",
    label: "Extended threshold",
    kind: "percent",
    help: "In an uptrend, price further above the 20-day average than this is Extended - wait.",
  },
];

/** The plan's defaults (docs/BUILD_PLAN.md, Settings). The server owns the real values; this is only the reset button. */
export const DEFAULT_SETTINGS: SettingsDto = {
  startingCapital: 2000,
  maxRiskPct: 0.015,
  maxOpenPositions: 3,
  minRewardRisk: 2,
  taxSetAsideRate: 0.27,
  washSaleWindowDays: 30,
  maxPositionPct: 0.5,
  pullbackBandLow: -0.01,
  pullbackBandHigh: 0.02,
  extendedThreshold: 0.05,
  riskBasis: "StartingCapital",
};

export const RISK_BASIS_OPTIONS: readonly { value: RiskBasis; label: string }[] = [
  { value: "StartingCapital", label: "Starting capital" },
  { value: "AccountValue", label: "Account value (starting capital plus realized P&L)" },
];

export type FormValues = Record<NumericKey, string> & { riskBasis: RiskBasis };

const def = (key: NumericKey) => FIELDS.find((f) => f.key === key)!;

/** Percent fields are edited as percent ("1.5") but stored as fractions (0.015). */
export function toFormValues(settings: SettingsDto): FormValues {
  const values = { riskBasis: settings.riskBasis } as FormValues;
  for (const f of FIELDS) {
    values[f.key] = f.kind === "percent" ? fractionToPercentText(settings[f.key]) : String(settings[f.key]);
  }
  return values;
}

export interface ParsedForm {
  request: UpdateSettingsRequest | null;
  errors: Partial<Record<NumericKey, string>>;
}

export function toRequest(values: FormValues): ParsedForm {
  const errors: Partial<Record<NumericKey, string>> = {};
  const request = { riskBasis: values.riskBasis } as Record<string, unknown>;
  for (const f of FIELDS) {
    const text = values[f.key];
    const parsed = f.kind === "percent" ? percentTextToFraction(text) : parseDecimal(text);
    if (parsed === null) {
      errors[f.key] = `Enter ${def(f.key).label.toLowerCase()} as a number.`;
    } else if ((f.kind === "integer" || f.kind === "days") && !Number.isInteger(parsed)) {
      errors[f.key] = "Enter a whole number.";
    } else {
      request[f.key] = parsed;
    }
  }
  return { request: Object.keys(errors).length === 0 ? (request as unknown as UpdateSettingsRequest) : null, errors };
}

export const unitOf = (kind: FieldKind): string => {
  switch (kind) {
    case "money":
      return "$";
    case "percent":
      return "%";
    case "ratio":
      return "x";
    case "days":
      return "days";
    default:
      return "";
  }
};
