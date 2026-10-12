import { combineReducers, configureStore } from "@reduxjs/toolkit";
import { dashboardSliceReducer } from "./slices/dashboardSlice";
import { notesSliceReducer } from "./slices/notesSlice";
import { settingsSliceReducer } from "./slices/settingsSlice";
import { tradesSliceReducer } from "./slices/tradesSlice";
import { watchlistSliceReducer } from "./slices/watchlistSlice";

export const rootReducer = combineReducers({
  watchlist: watchlistSliceReducer,
  trades: tradesSliceReducer,
  dashboard: dashboardSliceReducer,
  notes: notesSliceReducer,
  settings: settingsSliceReducer,
});

// Defined from rootReducer (not store.getState) to avoid a circular type reference.
export type RootState = ReturnType<typeof rootReducer>;

// Deliberately not persisted to localStorage: prices and balances must come from the API on every load.
export const makeStore = (preloadedState?: Partial<RootState>) =>
  configureStore({ reducer: rootReducer, preloadedState });

export const store = makeStore();

export type AppDispatch = typeof store.dispatch;
