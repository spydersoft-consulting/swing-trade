import type { Signal } from "../../api/generated/types.gen";

/** Green for a pullback candidate, red for Avoid, neutral for everything else (including Extended - wait). */
export const signalTone = (signal: Signal): "good" | "bad" | "neutral" => {
  if (signal === "PullbackCandidate") return "good";
  if (signal === "Avoid") return "bad";
  return "neutral";
};
