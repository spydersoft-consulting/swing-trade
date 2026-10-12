import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { PrimeReactProvider } from "primereact/api";
import { SignalPill, StaleBadge } from "./Badges";
import { signalTone } from "./signalTone";
import { ErrorBlock, LoadingBlock } from "./QueryState";
import { LineChart } from "./LineChart";
import { KpiTile } from "../layout/KpiTile";
import { PageHeader } from "../layout/PageHeader";
import { PageTitleProvider, usePageTitle } from "../../context/PageTitleContext";
import { ToastProvider, useToast } from "../../context/ToastContext";
import { notifyForbidden } from "../../context/authSession";
import { FORBIDDEN_MESSAGE } from "../../api/problem";
import { act } from "react";
import userEvent from "@testing-library/user-event";

const chart = vi.hoisted(() => ({ ctor: vi.fn(), destroy: vi.fn() }));
vi.mock("chart.js/auto", () => ({
  Chart: class {
    constructor(element: unknown, config: unknown) {
      chart.ctor(element, config);
    }
    destroy() {
      chart.destroy();
    }
  },
}));

beforeEach(() => {
  chart.ctor.mockReset();
  chart.destroy.mockReset();
});

describe("signal colours", () => {
  it("is green for a pullback candidate, red for Avoid, neutral for the rest", () => {
    expect(signalTone("PullbackCandidate")).toBe("good");
    expect(signalTone("Avoid")).toBe("bad");
    for (const s of ["Watch", "ExtendedWait", "InsufficientData"] as const) expect(signalTone(s)).toBe("neutral");
  });

  it("renders the API's signal text in a pill with the matching colour", () => {
    render(<SignalPill signal="Avoid" text="Avoid" />);
    expect(screen.getByTestId("signal")).toHaveTextContent("Avoid");
    expect(screen.getByTestId("signal").className).toContain("text-bad");
  });

  it("explains the stale badge", () => {
    render(<StaleBadge asOf="Oct 2, 2026" />);
    expect(screen.getByTestId("stale-badge")).toHaveAttribute("title", expect.stringContaining("Oct 2, 2026"));
    expect(screen.getByTestId("stale-badge")).toHaveTextContent("Stale");
  });
});

describe("query states", () => {
  it("announces loading", () => {
    render(
      <PrimeReactProvider>
        <LoadingBlock label="Loading trades" />
      </PrimeReactProvider>,
    );
    expect(screen.getByRole("status")).toHaveTextContent("Loading trades");
  });

  it("offers a retry for a failure but not for a permission problem", () => {
    const retry = vi.fn();
    const { rerender } = render(
      <PrimeReactProvider>
        <ErrorBlock
          problem={{ status: 500, message: "Server trouble", fieldErrors: {} }}
          onRetry={retry}
          title="Could not load X"
        />
      </PrimeReactProvider>,
    );
    expect(screen.getByRole("alert")).toHaveTextContent("Could not load X");
    screen.getByRole("button", { name: "Retry" }).click();
    expect(retry).toHaveBeenCalled();

    rerender(
      <PrimeReactProvider>
        <ErrorBlock problem={{ status: 403, message: FORBIDDEN_MESSAGE, fieldErrors: {} }} onRetry={retry} />
      </PrimeReactProvider>,
    );
    expect(screen.getByRole("alert")).toHaveTextContent("Not permitted");
    expect(screen.queryByRole("button", { name: "Retry" })).not.toBeInTheDocument();
  });
});

describe("layout pieces", () => {
  it("KpiTile shows the label, value and detail", () => {
    render(<KpiTile icon="pi-wallet" label="Account value" value="$2,053.55" detail="+2.7%" tone="good" testId="t" />);
    expect(screen.getByTestId("t")).toHaveTextContent("Account value");
    expect(screen.getByText("$2,053.55").className).toContain("text-good");
    expect(screen.getByText("+2.7%")).toBeInTheDocument();
  });

  it("PageHeader sets the page title and the document title", () => {
    function Title() {
      return <span data-testid="title">{usePageTitle()}</span>;
    }
    render(
      <PageTitleProvider>
        <PageHeader title="Trades" subtitle="All of them" actions={<button>Act</button>} />
        <Title />
      </PageTitleProvider>,
    );
    expect(screen.getByRole("heading", { level: 1, name: "Trades" })).toBeInTheDocument();
    expect(screen.getByTestId("title")).toHaveTextContent("Trades");
    expect(document.title).toBe("Trades - Swing Trade");
    expect(screen.getByRole("button", { name: "Act" })).toBeInTheDocument();
  });
});

describe("toasts", () => {
  it("shows a clear message for any 403 an API call reports, once", async () => {
    function Probe() {
      const toast = useToast();
      return <button onClick={() => toast.success("Saved", "It worked")}>go</button>;
    }
    render(
      <PrimeReactProvider>
        <ToastProvider>
          <Probe />
        </ToastProvider>
      </PrimeReactProvider>,
    );

    act(() => {
      notifyForbidden(FORBIDDEN_MESSAGE);
      notifyForbidden(FORBIDDEN_MESSAGE);
    });
    expect(await screen.findAllByText("Not permitted")).toHaveLength(1);
    expect(screen.getByText(/Ask the owner for swingtrade.trader/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "go" }));
    expect(await screen.findByText("Saved")).toBeInTheDocument();
  });
});

describe("LineChart", () => {
  it("builds a Chart.js line chart from exactly the values it is given, and cleans up", () => {
    const { unmount } = render(
      <LineChart
        labels={["a", "b"]}
        series={[
          { label: "Close", values: [1, 2], color: "#111" },
          { label: "20-day", values: [null, 1.5], color: "#222", dashed: true },
        ]}
        ariaLabel="Test chart"
        formatValue={(v) => `$${v}`}
      />,
    );

    expect(screen.getByRole("img", { name: "Test chart" })).toBeInTheDocument();
    const config = chart.ctor.mock.calls[0][1] as {
      type: string;
      data: { labels: string[]; datasets: { label: string; data: (number | null)[]; borderDash?: number[] }[] };
    };
    expect(config.type).toBe("line");
    expect(config.data.labels).toEqual(["a", "b"]);
    expect(config.data.datasets.map((d) => d.label)).toEqual(["Close", "20-day"]);
    expect(config.data.datasets[0].data).toEqual([1, 2]);
    expect(config.data.datasets[1].data).toEqual([null, 1.5]);
    expect(config.data.datasets[1].borderDash).toBeDefined();
    expect(config.data.datasets[0].borderDash).toBeUndefined();

    unmount();
    expect(chart.destroy).toHaveBeenCalled();
  });
});
