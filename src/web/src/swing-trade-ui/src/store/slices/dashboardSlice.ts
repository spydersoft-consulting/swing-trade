import { createSlice } from "@reduxjs/toolkit";
import { swingTradeApi } from "../../api/swingTradeApi";
import { toProblem, type ApiProblem } from "../../api/problem";
import type { DashboardDto } from "../../api/generated/types.gen";
import { createAppThunk, type LoadStatus } from "../thunk";

interface DashboardState {
  data: DashboardDto | null;
  status: LoadStatus;
  error: ApiProblem | null;
}

const initialState: DashboardState = { data: null, status: "idle", error: null };

export const fetchDashboard = createAppThunk("dashboard/fetch", async (_: void, { rejectWithValue }) => {
  try {
    return await swingTradeApi.dashboard();
  } catch (error) {
    return rejectWithValue(toProblem(error));
  }
});

const dashboardSlice = createSlice({
  name: "dashboard",
  initialState,
  reducers: {},
  extraReducers: (builder) => {
    builder
      .addCase(fetchDashboard.pending, (state) => {
        state.status = "loading";
        state.error = null;
      })
      .addCase(fetchDashboard.fulfilled, (state, action) => {
        state.status = "succeeded";
        state.data = action.payload;
      })
      .addCase(fetchDashboard.rejected, (state, action) => {
        state.status = "failed";
        state.error = action.payload ?? toProblem(action.error);
      });
  },
});

export const dashboardSliceReducer = dashboardSlice.reducer;
