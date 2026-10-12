export interface RoutineStep {
  id: string;
  label: string;
  /** The time box in minutes (the one-hour weekly routine). */
  minutes: number;
  /** Where in the app this step is done. */
  to: string;
  linkLabel: string;
}

export const ROUTINE_STEPS: readonly RoutineStep[] = [
  { id: "review-open", label: "Review open trades", minutes: 10, to: "/trades?tab=open", linkLabel: "Open trades" },
  { id: "read-signals", label: "Read the watchlist signals", minutes: 15, to: "/watchlist", linkLabel: "Watchlist" },
  {
    id: "inspect-candidates",
    label: "Inspect candidates on the chart for swing low and prior high",
    minutes: 15,
    to: "/watchlist",
    linkLabel: "Watchlist charts",
  },
  { id: "size-trade", label: "Size the trade", minutes: 10, to: "/sizer", linkLabel: "Sizer" },
  {
    id: "log-orders",
    label: "Log it and place orders",
    minutes: 10,
    to: "/trades?tab=planned",
    linkLabel: "Planned trades",
  },
];

// The checklist state is a per-browser convenience, never shared and never relied on: any storage failure just means
// the list starts empty.
const key = (weekStart: string) => `swingtrade.routine.${weekStart}`;

export function loadRoutine(weekStart: string): string[] {
  try {
    const raw = localStorage.getItem(key(weekStart));
    const parsed: unknown = raw ? JSON.parse(raw) : [];
    return Array.isArray(parsed) ? parsed.filter((id): id is string => typeof id === "string") : [];
  } catch {
    return [];
  }
}

export function saveRoutine(weekStart: string, done: string[]): void {
  try {
    localStorage.setItem(key(weekStart), JSON.stringify(done));
  } catch {
    // Storage may be unavailable (private window, blocked site data). The checklist still works for this visit.
  }
}

export const totalMinutes = ROUTINE_STEPS.reduce((sum, s) => sum + s.minutes, 0);
