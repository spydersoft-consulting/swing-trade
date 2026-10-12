import { createSlice, type PayloadAction } from "@reduxjs/toolkit";
import { swingTradeApi } from "../../api/swingTradeApi";
import { toProblem, type ApiProblem } from "../../api/problem";
import type { CreateInstrumentRequest, RefreshRunDto, WatchlistRowDto } from "../../api/generated/types.gen";
import { createAppThunk, type LoadStatus } from "../thunk";

export type RefreshPhase = "idle" | "running" | "succeeded" | "failed";

interface WatchlistState {
  rows: WatchlistRowDto[];
  status: LoadStatus;
  error: ApiProblem | null;
  lastRun: RefreshRunDto | null;
  lastRunStatus: LoadStatus;
  refreshPhase: RefreshPhase;
  refreshError: ApiProblem | null;
}

const initialState: WatchlistState = {
  rows: [],
  status: "idle",
  error: null,
  lastRun: null,
  lastRunStatus: "idle",
  refreshPhase: "idle",
  refreshError: null,
};

// The server reports a run as "Running" until the background job finishes (DECISIONS #48). Anything else is final.
const RUNNING = "Running";
const MAX_POLLS = 200;
let pollIntervalMs = 1500;

/** For tests: shortens the delay between polls of a running refresh. */
export const setRefreshPollInterval = (ms: number) => {
  pollIntervalMs = ms;
};

const sleep = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

export const fetchWatchlist = createAppThunk("watchlist/fetch", async (_: void, { rejectWithValue }) => {
  try {
    return await swingTradeApi.watchlist();
  } catch (error) {
    return rejectWithValue(toProblem(error));
  }
});

export const fetchLastRun = createAppThunk("watchlist/lastRun", async (_: void, { rejectWithValue }) => {
  try {
    const page = await swingTradeApi.refreshRuns(1);
    return page.items[0] ?? null;
  } catch (error) {
    return rejectWithValue(toProblem(error));
  }
});

export const addInstrument = createAppThunk(
  "watchlist/addInstrument",
  async (request: CreateInstrumentRequest, { dispatch, rejectWithValue }) => {
    try {
      const created = await swingTradeApi.createInstrument(request);
      await dispatch(fetchWatchlist());
      return created;
    } catch (error) {
      return rejectWithValue(toProblem(error));
    }
  },
);

/** Deactivating needs the whole instrument (the PUT replaces it), so it is read first. */
export const deactivateInstrument = createAppThunk(
  "watchlist/deactivate",
  async (ticker: string, { dispatch, rejectWithValue }) => {
    try {
      const all = await swingTradeApi.instruments();
      const current = all.find((i) => i.ticker === ticker);
      if (!current) return rejectWithValue({ status: 404, message: "That ticker no longer exists.", fieldErrors: {} });
      await swingTradeApi.updateInstrument(ticker, {
        name: current.name,
        category: current.category,
        active: false,
        isBenchmark: current.isBenchmark,
        washSaleGroup: current.washSaleGroup,
      });
      await dispatch(fetchWatchlist());
      return ticker;
    } catch (error) {
      return rejectWithValue(toProblem(error));
    }
  },
);

/** POST /refresh (202 with a Running run), then poll GET /refresh/runs/{id} until it ends; then reload everything. */
export const runRefresh = createAppThunk("watchlist/runRefresh", async (_: void, { dispatch, rejectWithValue }) => {
  try {
    let run = await swingTradeApi.startRefresh();
    dispatch(watchlistSlice.actions.refreshStarted(run));
    for (let polls = 0; run.status === RUNNING && polls < MAX_POLLS; polls++) {
      await sleep(pollIntervalMs);
      run = await swingTradeApi.refreshRun(run.id);
    }
    await Promise.all([dispatch(fetchWatchlist()), dispatch(fetchLastRun())]);
    return run;
  } catch (error) {
    return rejectWithValue(toProblem(error));
  }
});

const watchlistSlice = createSlice({
  name: "watchlist",
  initialState,
  reducers: {
    refreshStarted(state, action: PayloadAction<RefreshRunDto>) {
      state.refreshPhase = "running";
      state.refreshError = null;
      state.lastRun = action.payload;
    },
    clearRefreshError(state) {
      state.refreshError = null;
    },
  },
  extraReducers: (builder) => {
    builder
      .addCase(fetchWatchlist.pending, (state) => {
        state.status = "loading";
        state.error = null;
      })
      .addCase(fetchWatchlist.fulfilled, (state, action) => {
        state.status = "succeeded";
        state.rows = action.payload;
      })
      .addCase(fetchWatchlist.rejected, (state, action) => {
        state.status = "failed";
        state.error = action.payload ?? toProblem(action.error);
      })
      .addCase(fetchLastRun.pending, (state) => {
        state.lastRunStatus = "loading";
      })
      .addCase(fetchLastRun.fulfilled, (state, action) => {
        state.lastRunStatus = "succeeded";
        state.lastRun = action.payload;
      })
      .addCase(fetchLastRun.rejected, (state) => {
        state.lastRunStatus = "failed";
      })
      .addCase(runRefresh.fulfilled, (state, action) => {
        state.lastRun = action.payload;
        const ok = action.payload.status === "Succeeded" || action.payload.status === "Partial";
        state.refreshPhase = ok ? "succeeded" : "failed";
      })
      .addCase(runRefresh.rejected, (state, action) => {
        state.refreshPhase = "failed";
        state.refreshError = action.payload ?? toProblem(action.error);
      });
  },
});

export const { clearRefreshError } = watchlistSlice.actions;
export const watchlistSliceReducer = watchlistSlice.reducer;
