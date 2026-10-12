import { AxiosError, AxiosHeaders } from "axios";
import { describe, expect, it } from "vitest";
import { FORBIDDEN_MESSAGE, isApiProblem, normalizeFieldName, toProblem } from "./problem";

const failure = (status: number, data?: unknown) =>
  new AxiosError("failed", "ERR_BAD_REQUEST", undefined, undefined, {
    status,
    statusText: "",
    data,
    headers: {},
    config: { headers: new AxiosHeaders() },
  });

describe("normalizeFieldName", () => {
  it("lowercases the first letter and drops json-path prefixes", () => {
    expect(normalizeFieldName("EntryPrice")).toBe("entryPrice");
    expect(normalizeFieldName("$.entryPrice")).toBe("entryPrice");
    expect(normalizeFieldName("request.StopPrice")).toBe("stopPrice");
    expect(normalizeFieldName("ticker")).toBe("ticker");
  });
});

describe("toProblem", () => {
  it("maps a 400 ValidationProblemDetails onto camelCase field names", () => {
    const problem = toProblem(
      failure(400, {
        title: "One or more validation errors occurred.",
        errors: {
          EntryPrice: ["Entry must be above the stop."],
          Shares: ["Shares must be at least 1.", "Whole shares only."],
        },
      }),
    );

    expect(problem.status).toBe(400);
    expect(problem.fieldErrors).toEqual({
      entryPrice: "Entry must be above the stop.",
      shares: "Shares must be at least 1. Whole shares only.",
    });
    expect(problem.message).toBe("Please correct the highlighted fields.");
  });

  it("uses the detail of a 400 that has no field errors", () => {
    expect(toProblem(failure(400, { detail: "Bad thing." })).message).toBe("Bad thing.");
    expect(toProblem(failure(400)).message).toBe("The request was not valid.");
  });

  it("explains a 403 as a missing role, never as a server fault", () => {
    const problem = toProblem(failure(403));
    expect(problem.status).toBe(403);
    expect(problem.message).toBe(FORBIDDEN_MESSAGE);
    expect(problem.message).toContain("swingtrade.trader");
  });

  it("carries the API's explanation of a 409 transition", () => {
    const problem = toProblem(failure(409, { title: "Conflict", detail: "Only a Planned trade can be filled." }));
    expect(problem.status).toBe(409);
    expect(problem.message).toBe("Only a Planned trade can be filled.");
  });

  it("covers 401, 404, 5xx, other 4xx and network failures", () => {
    expect(toProblem(failure(401)).message).toContain("session");
    expect(toProblem(failure(404)).message).toContain("no longer exists");
    expect(toProblem(failure(500)).message).toContain("server");
    expect(toProblem(failure(422, { title: "Nope" })).message).toBe("Nope");
    expect(toProblem(new AxiosError("Network Error")).message).toContain("Could not reach the server");
    expect(toProblem(new Error("boom")).message).toBe("boom");
    expect(toProblem("odd").message).toBe("Something went wrong.");
  });

  it("produces something isApiProblem recognises", () => {
    expect(isApiProblem(toProblem(failure(500)))).toBe(true);
    expect(isApiProblem({})).toBe(false);
    expect(isApiProblem(null)).toBe(false);
  });
});
