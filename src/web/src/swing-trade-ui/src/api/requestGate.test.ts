import axios, { AxiosError } from "axios";
import { describe, expect, it } from "vitest";
import { serializeRequests } from "./requestGate";

/** An axios instance whose adapter records when each request starts and ends, so overlap is visible. */
function gatedInstance(fail: (url: string) => boolean = () => false) {
  const instance = axios.create();
  const log: string[] = [];
  let running = 0;
  let maxRunning = 0;
  instance.defaults.adapter = async (config) => {
    running++;
    maxRunning = Math.max(maxRunning, running);
    log.push(`start ${config.url}`);
    await new Promise((resolve) => setTimeout(resolve, 5));
    running--;
    log.push(`end ${config.url}`);
    if (fail(config.url ?? "")) {
      throw new AxiosError("nope", "ERR_BAD_RESPONSE", config);
    }
    return { data: config.url, status: 200, statusText: "OK", headers: {}, config };
  };
  serializeRequests(instance);
  return { instance, log, maxRunning: () => maxRunning };
}

describe("serializeRequests", () => {
  it("lets one request through at a time, in order", async () => {
    const { instance, log, maxRunning } = gatedInstance();

    const results = await Promise.all([instance.get("/a"), instance.get("/b"), instance.get("/c")]);

    expect(results.map((r) => r.data)).toEqual(["/a", "/b", "/c"]);
    expect(maxRunning()).toBe(1);
    expect(log).toEqual(["start /a", "end /a", "start /b", "end /b", "start /c", "end /c"]);
  });

  it("keeps the queue moving after a failed request, and rethrows the original error", async () => {
    const { instance } = gatedInstance((url) => url === "/bad");

    const outcomes = await Promise.allSettled([instance.get("/bad"), instance.get("/ok")]);

    expect(outcomes[0].status).toBe("rejected");
    expect((outcomes[0] as PromiseRejectedResult).reason).toBeInstanceOf(AxiosError);
    expect(outcomes[1].status).toBe("fulfilled");
  });
});
