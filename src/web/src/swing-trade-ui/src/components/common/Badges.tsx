import React from "react";
import type { Signal } from "../../api/generated/types.gen";
import { signalTone } from "./signalTone";

const toneClass = {
  good: "bg-good-tint text-good border-good/30",
  bad: "bg-bad-tint text-bad border-bad/30",
  warn: "bg-warn-tint text-warn border-warn/30",
  neutral: "bg-neutral-tint text-content border-border-strong",
} as const;

export type Tone = keyof typeof toneClass;

interface PillProps {
  tone: Tone;
  children: React.ReactNode;
  title?: string;
  testId?: string;
}

/** A small status pill. Colour is never the only signal: the text always says what it is. */
export const Pill: React.FC<PillProps> = ({ tone, children, title, testId }) => (
  <span
    className={`inline-flex items-center rounded-full border px-2 py-0.5 text-meta font-medium whitespace-nowrap ${toneClass[tone]}`}
    title={title}
    data-testid={testId}
  >
    {children}
  </span>
);

export const SignalPill: React.FC<{ signal: Signal; text: string }> = ({ signal, text }) => (
  <Pill tone={signalTone(signal)} testId="signal">
    {text}
  </Pill>
);

export const StaleBadge: React.FC<{ asOf?: string | null }> = ({ asOf }) => (
  <Pill
    tone="warn"
    testId="stale-badge"
    title={asOf ? `Last price is from ${asOf}, older than the most recent market close.` : "No price data yet."}
  >
    Stale
  </Pill>
);
