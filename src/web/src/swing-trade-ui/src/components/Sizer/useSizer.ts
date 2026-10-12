import { useEffect, useRef, useState } from "react";
import type { SizerResultDto } from "../../api/generated/types.gen";
import { swingTradeApi } from "../../api/swingTradeApi";
import { toProblem, type ApiProblem } from "../../api/problem";
import { parseDecimal } from "../../utils/decimal";

export interface SizerInput {
  ticker?: string;
  entry: string;
  stop: string;
  target: string;
}

export interface SizerState {
  /** All three prices parse as positive numbers, so a calculation was requested. */
  ready: boolean;
  loading: boolean;
  result: SizerResultDto | null;
  problem: ApiProblem | null;
}

export const SIZER_DEBOUNCE_MS = 400;

/**
 * The live sizer: POST /sizer after a short pause whenever entry, stop and target are all valid numbers. Every figure
 * shown comes from the response; nothing is calculated here. Out-of-order responses are discarded.
 */
export function useSizer(input: SizerInput, debounceMs: number = SIZER_DEBOUNCE_MS): SizerState {
  const entry = parseDecimal(input.entry);
  const stop = parseDecimal(input.stop);
  const target = parseDecimal(input.target);
  const ticker = input.ticker?.trim().toUpperCase() || undefined;
  const ready = entry !== null && stop !== null && target !== null && entry > 0 && stop > 0 && target > 0;

  const [state, setState] = useState<SizerState>({ ready: false, loading: false, result: null, problem: null });
  const sequence = useRef(0);

  useEffect(() => {
    const id = ++sequence.current;
    if (!ready) {
      setState({ ready: false, loading: false, result: null, problem: null });
      return;
    }
    setState((s) => ({ ...s, ready: true, loading: true, problem: null }));
    const timer = setTimeout(() => {
      swingTradeApi
        .size({ ticker, entry: entry!, stop: stop!, target: target! })
        .then((result) => {
          if (id === sequence.current) setState({ ready: true, loading: false, result, problem: null });
        })
        .catch((error: unknown) => {
          if (id === sequence.current)
            setState({ ready: true, loading: false, result: null, problem: toProblem(error) });
        });
    }, debounceMs);
    return () => clearTimeout(timer);
  }, [ready, entry, stop, target, ticker, debounceMs]);

  return state;
}
