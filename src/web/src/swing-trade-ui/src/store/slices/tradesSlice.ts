import { createSlice, type PayloadAction } from "@reduxjs/toolkit";
import { swingTradeApi } from "../../api/swingTradeApi";
import { toProblem, type ApiProblem } from "../../api/problem";
import type {
  CloseTradeRequest,
  CreateTradeRequest,
  FillTradeRequest,
  TradeDto,
  UpdateTradeRequest,
} from "../../api/generated/types.gen";
import { createAppThunk, type LoadStatus } from "../thunk";

interface TradesState {
  items: TradeDto[];
  status: LoadStatus;
  error: ApiProblem | null;
}

const initialState: TradesState = { items: [], status: "idle", error: null };

export const fetchTrades = createAppThunk("trades/fetch", async (_: void, { rejectWithValue }) => {
  try {
    return await swingTradeApi.trades();
  } catch (error) {
    return rejectWithValue(toProblem(error));
  }
});

export const createTrade = createAppThunk("trades/create", async (request: CreateTradeRequest, { rejectWithValue }) => {
  try {
    return await swingTradeApi.createTrade(request);
  } catch (error) {
    return rejectWithValue(toProblem(error));
  }
});

export const updateTrade = createAppThunk(
  "trades/update",
  async (arg: { id: number; body: UpdateTradeRequest }, { rejectWithValue }) => {
    try {
      return await swingTradeApi.updateTrade(arg.id, arg.body);
    } catch (error) {
      return rejectWithValue(toProblem(error));
    }
  },
);

export const fillTrade = createAppThunk(
  "trades/fill",
  async (arg: { id: number; body: FillTradeRequest }, { rejectWithValue }) => {
    try {
      return await swingTradeApi.fillTrade(arg.id, arg.body);
    } catch (error) {
      return rejectWithValue(toProblem(error));
    }
  },
);

export const closeTrade = createAppThunk(
  "trades/close",
  async (arg: { id: number; body: CloseTradeRequest }, { rejectWithValue }) => {
    try {
      return await swingTradeApi.closeTrade(arg.id, arg.body);
    } catch (error) {
      return rejectWithValue(toProblem(error));
    }
  },
);

export const cancelTrade = createAppThunk("trades/cancel", async (id: number, { rejectWithValue }) => {
  try {
    return await swingTradeApi.cancelTrade(id);
  } catch (error) {
    return rejectWithValue(toProblem(error));
  }
});

const upsert = (state: TradesState, trade: TradeDto) => {
  const index = state.items.findIndex((t) => t.id === trade.id);
  if (index >= 0) state.items[index] = trade;
  else state.items.unshift(trade);
};

const tradesSlice = createSlice({
  name: "trades",
  initialState,
  reducers: {
    tradeReplaced(state, action: PayloadAction<TradeDto>) {
      upsert(state, action.payload);
    },
  },
  extraReducers: (builder) => {
    builder
      .addCase(fetchTrades.pending, (state) => {
        state.status = "loading";
        state.error = null;
      })
      .addCase(fetchTrades.fulfilled, (state, action) => {
        state.status = "succeeded";
        state.items = action.payload;
      })
      .addCase(fetchTrades.rejected, (state, action) => {
        state.status = "failed";
        state.error = action.payload ?? toProblem(action.error);
      });
    // A changed trade comes back with every computed field, so it replaces the stored row as it is.
    for (const thunk of [createTrade, updateTrade, fillTrade, closeTrade, cancelTrade]) {
      builder.addCase(thunk.fulfilled, (state, action) => {
        upsert(state, action.payload);
      });
    }
  },
});

export const tradesSliceReducer = tradesSlice.reducer;
