import React from "react";
import type { TradeDto } from "../../api/generated/types.gen";
import { Pill } from "../common/Badges";

/** The flags as labelled pills (colour is never the only signal). */
export const TradeFlags: React.FC<{ trade: TradeDto }> = ({ trade }) => (
  <span className="inline-flex flex-wrap gap-1">
    {trade.riskOverLimit && (
      <Pill tone="bad" title="Risk at the stop is above the maximum risk per trade." testId="flag-risk">
        Risk over limit
      </Pill>
    )}
    {trade.rewardRiskBelowMin && (
      <Pill tone="bad" title="Planned reward:risk is below the minimum." testId="flag-rr">
        R:R below min
      </Pill>
    )}
    {trade.washSaleFlag && (
      <Pill
        tone="warn"
        title="A losing trade in this ticker closed inside the wash-sale window. Informational; confirm tax treatment with a professional."
        testId="flag-wash"
      >
        Wash sale
      </Pill>
    )}
  </span>
);
