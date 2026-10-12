import { createAsyncThunk } from "@reduxjs/toolkit";
import type { ApiProblem } from "../api/problem";

/** Thunks reject with an ApiProblem (status, message, field errors), never with a raw AxiosError. */
export const createAppThunk = createAsyncThunk.withTypes<{ rejectValue: ApiProblem }>();

export type LoadStatus = "idle" | "loading" | "succeeded" | "failed";
