import React from "react";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import type { IconDefinition } from "@fortawesome/free-solid-svg-icons";

interface KpiTileProps {
  /** A FontAwesome icon definition. */
  icon: IconDefinition;
  label: string;
  value: string;
  /** A second line under the value (a percentage, a limit, a caveat). */
  detail?: string;
  /** Tone of the value: good (green), bad (red) or neutral. */
  tone?: "good" | "bad" | "neutral";
  testId?: string;
}

const toneClass = { good: "text-good", bad: "text-bad", neutral: "text-content" } as const;

export const KpiTile: React.FC<KpiTileProps> = ({ icon, label, value, detail, tone = "neutral", testId }) => (
  <div
    className="flex items-center gap-2 sm:gap-3 rounded-xl bg-surface px-3 py-3 border border-border min-w-0"
    data-testid={testId}
  >
    <span className="flex h-9 w-9 sm:h-10 sm:w-10 items-center justify-center rounded-lg bg-brand-tint text-brand shrink-0">
      <FontAwesomeIcon icon={icon} className="text-base sm:text-lg" aria-hidden="true" />
    </span>
    <div className="min-w-0 flex-1">
      <p className="text-meta uppercase tracking-wide text-content-muted truncate">{label}</p>
      <p className={`font-numeric text-lg sm:text-2xl leading-tight truncate ${toneClass[tone]}`}>{value}</p>
      {detail && <p className="text-meta text-content-muted truncate">{detail}</p>}
    </div>
  </div>
);
