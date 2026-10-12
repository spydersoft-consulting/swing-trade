import type { TradeSetup } from "../../api/generated/types.gen";

export const SETUP_OPTIONS: readonly { value: TradeSetup; label: string }[] = [
  { value: "Pullback", label: "Pullback" },
  { value: "Breakout", label: "Breakout" },
  { value: "MeanReversion", label: "Mean reversion" },
  { value: "Other", label: "Other" },
];
