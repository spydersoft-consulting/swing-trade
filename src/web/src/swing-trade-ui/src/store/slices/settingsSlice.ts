import { createSlice } from "@reduxjs/toolkit";
import { swingTradeApi } from "../../api/swingTradeApi";
import { toProblem, type ApiProblem } from "../../api/problem";
import type { McpCallDto, McpInfoDto, SettingsDto, UpdateSettingsRequest } from "../../api/generated/types.gen";
import { createAppThunk, type LoadStatus } from "../thunk";

interface SettingsState {
  settings: SettingsDto | null;
  status: LoadStatus;
  error: ApiProblem | null;
  mcpInfo: McpInfoDto | null;
  mcpCalls: McpCallDto[];
  mcpStatus: LoadStatus;
  mcpError: ApiProblem | null;
}

const initialState: SettingsState = {
  settings: null,
  status: "idle",
  error: null,
  mcpInfo: null,
  mcpCalls: [],
  mcpStatus: "idle",
  mcpError: null,
};

export const fetchSettings = createAppThunk("settings/fetch", async (_: void, { rejectWithValue }) => {
  try {
    return await swingTradeApi.settings();
  } catch (error) {
    return rejectWithValue(toProblem(error));
  }
});

export const saveSettings = createAppThunk(
  "settings/save",
  async (request: UpdateSettingsRequest, { rejectWithValue }) => {
    try {
      return await swingTradeApi.updateSettings(request);
    } catch (error) {
      return rejectWithValue(toProblem(error));
    }
  },
);

/** The MCP panel: endpoint facts (Read) and the last calls (Admin). Only an admin is shown the panel, so both load together. */
export const fetchMcp = createAppThunk("settings/mcp", async (_: void, { rejectWithValue }) => {
  try {
    const [info, calls] = await Promise.all([swingTradeApi.mcpInfo(), swingTradeApi.mcpCalls(20)]);
    return { info, calls };
  } catch (error) {
    return rejectWithValue(toProblem(error));
  }
});

const settingsSlice = createSlice({
  name: "settings",
  initialState,
  reducers: {},
  extraReducers: (builder) => {
    builder
      .addCase(fetchSettings.pending, (state) => {
        state.status = "loading";
        state.error = null;
      })
      .addCase(fetchSettings.fulfilled, (state, action) => {
        state.status = "succeeded";
        state.settings = action.payload;
      })
      .addCase(fetchSettings.rejected, (state, action) => {
        state.status = "failed";
        state.error = action.payload ?? toProblem(action.error);
      })
      .addCase(saveSettings.fulfilled, (state, action) => {
        state.settings = action.payload;
      })
      .addCase(fetchMcp.pending, (state) => {
        state.mcpStatus = "loading";
        state.mcpError = null;
      })
      .addCase(fetchMcp.fulfilled, (state, action) => {
        state.mcpStatus = "succeeded";
        state.mcpInfo = action.payload.info;
        state.mcpCalls = action.payload.calls;
      })
      .addCase(fetchMcp.rejected, (state, action) => {
        state.mcpStatus = "failed";
        state.mcpError = action.payload ?? toProblem(action.error);
      });
  },
});

export const settingsSliceReducer = settingsSlice.reducer;
