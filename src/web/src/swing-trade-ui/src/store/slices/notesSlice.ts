import { createSlice } from "@reduxjs/toolkit";
import { swingTradeApi } from "../../api/swingTradeApi";
import { toProblem, type ApiProblem } from "../../api/problem";
import type { CreateNoteRequest, NoteDto } from "../../api/generated/types.gen";
import { createAppThunk, type LoadStatus } from "../thunk";

interface NotesState {
  items: NoteDto[];
  status: LoadStatus;
  error: ApiProblem | null;
}

const initialState: NotesState = { items: [], status: "idle", error: null };

export const fetchNotes = createAppThunk("notes/fetch", async (_: void, { rejectWithValue }) => {
  try {
    return await swingTradeApi.notes();
  } catch (error) {
    return rejectWithValue(toProblem(error));
  }
});

export const createNote = createAppThunk("notes/create", async (request: CreateNoteRequest, { rejectWithValue }) => {
  try {
    return await swingTradeApi.createNote(request);
  } catch (error) {
    return rejectWithValue(toProblem(error));
  }
});

const notesSlice = createSlice({
  name: "notes",
  initialState,
  reducers: {},
  extraReducers: (builder) => {
    builder
      .addCase(fetchNotes.pending, (state) => {
        state.status = "loading";
        state.error = null;
      })
      .addCase(fetchNotes.fulfilled, (state, action) => {
        state.status = "succeeded";
        state.items = action.payload;
      })
      .addCase(fetchNotes.rejected, (state, action) => {
        state.status = "failed";
        state.error = action.payload ?? toProblem(action.error);
      })
      .addCase(createNote.fulfilled, (state, action) => {
        state.items.unshift(action.payload);
      });
  },
});

export const notesSliceReducer = notesSlice.reducer;
