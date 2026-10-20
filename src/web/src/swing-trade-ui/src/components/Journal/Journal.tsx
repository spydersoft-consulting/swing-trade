import React, { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { faCircleMinus, faPlus } from "@fortawesome/free-solid-svg-icons";
import { Button } from "../ui/Button";
import type { NoteKind } from "../../api/generated/types.gen";
import { PageHeader } from "../layout/PageHeader";
import { EmptyBlock, ErrorBlock, LoadingBlock } from "../common/QueryState";
import { Pill, type Tone } from "../common/Badges";
import { NoteDialog } from "./NoteDialog";
import { loadRoutine, ROUTINE_STEPS, saveRoutine, totalMinutes } from "./routine";
import { useAuth } from "../../context";
import { useAppDispatch, useAppSelector } from "../../store/hooks";
import { fetchNotes } from "../../store/slices/notesSlice";
import { todayInNewYork, weekStart } from "../../utils/dates";
import { formatDate } from "../../utils/format";

type KindFilter = "All" | NoteKind;
const KIND_FILTERS: KindFilter[] = ["All", "Pass", "Review", "General"];
const kindTone = (kind: NoteKind): Tone => (kind === "Pass" ? "warn" : "neutral");

export const Journal: React.FC = () => {
  const dispatch = useAppDispatch();
  const { canWrite } = useAuth();
  const { items, status, error } = useAppSelector((s) => s.notes);
  const week = useMemo(() => weekStart(todayInNewYork()), []);
  const [done, setDone] = useState<string[]>(() => loadRoutine(week));
  const [filter, setFilter] = useState<KindFilter>("All");
  const [dialogKind, setDialogKind] = useState<NoteKind | null>(null);

  useEffect(() => {
    void dispatch(fetchNotes());
  }, [dispatch]);

  const toggle = (id: string) => {
    const next = done.includes(id) ? done.filter((d) => d !== id) : [...done, id];
    setDone(next);
    saveRoutine(week, next);
  };

  const reset = () => {
    setDone([]);
    saveRoutine(week, []);
  };

  const doneMinutes = ROUTINE_STEPS.filter((s) => done.includes(s.id)).reduce((sum, s) => sum + s.minutes, 0);

  const notes = useMemo(() => {
    const filtered = filter === "All" ? items : items.filter((n) => n.kind === filter);
    return [...filtered].sort((a, b) => b.date.localeCompare(a.date) || b.id - a.id);
  }, [items, filter]);

  return (
    <div className="space-y-4">
      <PageHeader
        title="Journal"
        subtitle="The one-hour weekly routine, the pass log and your review notes."
        actions={
          canWrite && (
            <>
              <Button
                label="Record a pass"
                icon={<FontAwesomeIcon icon={faCircleMinus} />}
                size="sm"
                variant="outlined"
                onClick={() => setDialogKind("Pass")}
              />
              <Button
                label="Add note"
                icon={<FontAwesomeIcon icon={faPlus} />}
                size="sm"
                onClick={() => setDialogKind("General")}
              />
            </>
          )
        }
      />

      <div className="grid grid-cols-1 xl:grid-cols-5 gap-4">
        <section className="xl:col-span-2 rounded-xl bg-surface border border-border p-4" aria-label="Weekly routine">
          <div className="flex items-baseline justify-between gap-2 mb-1">
            <h2 className="text-base font-semibold">Weekly routine</h2>
            <span className="text-meta text-content-muted">Week of {formatDate(week)}</span>
          </div>
          <p className="text-sm text-content-muted mb-3" data-testid="routine-progress">
            {doneMinutes} of {totalMinutes} minutes done
          </p>
          <ol className="list-none p-0 m-0 space-y-2">
            {ROUTINE_STEPS.map((step) => {
              const checked = done.includes(step.id);
              return (
                <li key={step.id} className="flex items-start gap-3 rounded-lg border border-border p-3">
                  <input
                    id={`routine-${step.id}`}
                    type="checkbox"
                    className="mt-1 h-4 w-4"
                    checked={checked}
                    onChange={() => toggle(step.id)}
                  />
                  <div className="min-w-0 flex-1">
                    <label
                      htmlFor={`routine-${step.id}`}
                      className={`text-sm font-medium ${checked ? "line-through text-content-muted" : ""}`}
                    >
                      {step.label}
                    </label>
                    <p className="text-meta text-content-muted m-0">
                      {step.minutes} min &middot;{" "}
                      <Link to={step.to} className="text-brand">
                        {step.linkLabel}
                      </Link>
                    </p>
                  </div>
                </li>
              );
            })}
          </ol>
          <div className="flex items-center justify-between gap-2 mt-3">
            <p className="text-meta text-content-muted m-0">The checklist is kept in this browser only.</p>
            <Button type="button" label="Reset week" size="sm" variant="text" onClick={reset} />
          </div>
        </section>

        <section className="xl:col-span-3 rounded-xl bg-surface border border-border p-4" aria-label="Notes">
          <div className="flex flex-wrap items-center justify-between gap-2 mb-3">
            <h2 className="text-base font-semibold">Notes and pass log</h2>
            <div className="flex flex-wrap gap-1" role="group" aria-label="Filter by kind">
              {KIND_FILTERS.map((k) => (
                <button
                  key={k}
                  type="button"
                  aria-pressed={filter === k}
                  onClick={() => setFilter(k)}
                  className={[
                    "chip-toggle rounded-full border px-3 py-0.5 text-sm cursor-pointer",
                    filter === k
                      ? "bg-brand text-brand-fg border-brand"
                      : "bg-surface border-border-strong hover:bg-surface-sunken",
                  ].join(" ")}
                >
                  {k}
                </button>
              ))}
            </div>
          </div>
          {status === "loading" && items.length === 0 && <LoadingBlock label="Loading notes" />}
          {status === "failed" && (
            <ErrorBlock problem={error} onRetry={() => void dispatch(fetchNotes())} title="Could not load notes" />
          )}
          {status === "succeeded" && notes.length === 0 && (
            <EmptyBlock>
              {filter === "All" ? "No notes yet." : `No ${filter.toLowerCase()} notes.`}{" "}
              {canWrite ? "A week with no trade is a decision: record a pass." : ""}
            </EmptyBlock>
          )}
          <ul className="list-none p-0 m-0 space-y-2" aria-label="Journal entries">
            {notes.map((n) => (
              <li key={n.id} className="rounded-lg border border-border p-3" data-testid="note">
                <div className="flex flex-wrap items-center gap-2 text-sm">
                  <span className="font-medium">{formatDate(n.date)}</span>
                  <Pill tone={kindTone(n.kind)}>{n.kind}</Pill>
                  {n.ticker && <span className="font-semibold">{n.ticker}</span>}
                </div>
                <p className="text-sm mt-1 mb-0 whitespace-pre-wrap">{n.text}</p>
              </li>
            ))}
          </ul>
        </section>
      </div>

      <NoteDialog visible={dialogKind !== null} kind={dialogKind ?? "General"} onHide={() => setDialogKind(null)} />
    </div>
  );
};
