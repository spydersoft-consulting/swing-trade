import type { TradeDto } from "../../api/generated/types.gen";

/** Row highlight from the flags the API computed: red for risk over the limit, amber for the other flags. */
export function tradeRowClass(trade: TradeDto): string {
  if (trade.riskOverLimit) return "row-bad bg-bad-tint";
  if (trade.rewardRiskBelowMin || trade.washSaleFlag) return "row-warn bg-warn-tint";
  return "";
}
