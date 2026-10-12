import axios from "axios";

/** What the UI needs to know about a failed API call. */
export interface ApiProblem {
  /** HTTP status, or 0 when the server could not be reached. */
  status: number;
  /** A message fit to show a person. */
  message: string;
  /** Field errors from a 400 ValidationProblemDetails, keyed by camelCase field name (EntryPrice becomes entryPrice). */
  fieldErrors: Record<string, string>;
}

export const FORBIDDEN_MESSAGE =
  "Your account does not have the role this action needs. Ask the owner for swingtrade.trader (trades and notes) or swingtrade.admin (settings and refresh).";

interface ProblemBody {
  title?: unknown;
  detail?: unknown;
  errors?: unknown;
}

/** "$.entryPrice", "EntryPrice" and "request.EntryPrice" all become "entryPrice". */
export function normalizeFieldName(key: string): string {
  const last =
    key
      .replace(/^\$\.?/, "")
      .split(".")
      .pop() ?? key;
  return last.length > 0 ? last.charAt(0).toLowerCase() + last.slice(1) : last;
}

function fieldErrorsOf(errors: unknown): Record<string, string> {
  const result: Record<string, string> = {};
  if (typeof errors !== "object" || errors === null) return result;
  for (const [key, value] of Object.entries(errors as Record<string, unknown>)) {
    const messages = Array.isArray(value) ? value.filter((v): v is string => typeof v === "string") : [];
    if (messages.length > 0) result[normalizeFieldName(key)] = messages.join(" ");
  }
  return result;
}

const asText = (value: unknown): string | undefined =>
  typeof value === "string" && value.length > 0 ? value : undefined;

export function toProblem(error: unknown): ApiProblem {
  if (!axios.isAxiosError(error)) {
    return { status: 0, message: error instanceof Error ? error.message : "Something went wrong.", fieldErrors: {} };
  }
  const status = error.response?.status ?? 0;
  const data: unknown = error.response?.data;
  const body = (typeof data === "object" && data !== null ? data : {}) as ProblemBody;
  const detail = asText(body.detail) ?? asText(body.title);

  switch (status) {
    case 0:
      return { status, message: "Could not reach the server. Check your connection and try again.", fieldErrors: {} };
    case 400: {
      const fieldErrors = fieldErrorsOf(body.errors);
      const hasFields = Object.keys(fieldErrors).length > 0;
      const message = hasFields ? "Please correct the highlighted fields." : (detail ?? "The request was not valid.");
      return { status, message, fieldErrors };
    }
    case 401:
      return { status, message: "Your session has ended. Signing you in again.", fieldErrors: {} };
    case 403:
      return { status, message: FORBIDDEN_MESSAGE, fieldErrors: {} };
    case 404:
      return { status, message: "That item no longer exists.", fieldErrors: {} };
    case 409:
      return { status, message: detail ?? "That change is not allowed in the current state.", fieldErrors: {} };
    default:
      return {
        status,
        message: status >= 500 ? "The server had a problem. Try again in a moment." : (detail ?? "The request failed."),
        fieldErrors: {},
      };
  }
}

/** Thunks reject with a plain ApiProblem (serialisable), never with the AxiosError itself. */
export const isApiProblem = (value: unknown): value is ApiProblem =>
  typeof value === "object" && value !== null && "status" in value && "fieldErrors" in value && "message" in value;
