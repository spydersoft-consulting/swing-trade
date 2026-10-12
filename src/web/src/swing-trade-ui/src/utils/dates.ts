const newYorkDate = new Intl.DateTimeFormat("en-CA", {
  timeZone: "America/New_York",
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
});

/** Today's date in US market time (America/New_York) as yyyy-MM-dd. The same convention as the API. */
export const todayInNewYork = (now: Date = new Date()): string => newYorkDate.format(now);

/** The Monday of the week containing the given yyyy-MM-dd date: the key the weekly routine checklist is stored under. */
export function weekStart(iso: string): string {
  const [y, m, d] = iso.split("-").map(Number);
  const date = new Date(Date.UTC(y, m - 1, d));
  const sinceMonday = (date.getUTCDay() + 6) % 7;
  date.setUTCDate(date.getUTCDate() - sinceMonday);
  return date.toISOString().slice(0, 10);
}
