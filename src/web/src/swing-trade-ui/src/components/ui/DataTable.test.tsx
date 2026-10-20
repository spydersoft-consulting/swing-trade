import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { Column, DataTable } from "./DataTable";

interface Row {
  id: string;
  name: string;
  qty: number | null;
  nested: { tag: string };
}

const rows: Row[] = [
  { id: "a", name: "Charlie", qty: 3, nested: { tag: "x" } },
  { id: "b", name: "alpha", qty: null, nested: { tag: "y" } },
  { id: "c", name: "Bravo", qty: 10, nested: { tag: "z" } },
];

const names = () =>
  screen
    .getAllByRole("row")
    .slice(1)
    .map((r) => within(r).getAllByRole("rowheader")[0]?.textContent);

const renderTable = (props: Partial<React.ComponentProps<typeof DataTable<Row>>> = {}) =>
  render(
    <DataTable value={rows} aria-label="Things" {...props}>
      <Column field="name" header="Name" sortable />
      <Column field="qty" header="Qty" sortable align="right" />
      <Column field="nested.tag" header="Tag" frozen alignFrozen="right" />
      <Column header="Custom" body={(r: Row) => <span>custom {r.id}</span>} />
    </DataTable>,
  );

describe("DataTable", () => {
  it("renders headers, nested fields and custom bodies", () => {
    renderTable();
    expect(screen.getByRole("grid", { name: "Things" })).toBeInTheDocument();
    expect(screen.getAllByRole("columnheader")).toHaveLength(4);
    expect(screen.getByText("y")).toBeInTheDocument();
    expect(screen.getByText("custom c")).toBeInTheDocument();
  });

  it("cycles ascending, descending, then source order, with empty values last", async () => {
    renderTable();
    expect(names()).toEqual(["Charlie", "alpha", "Bravo"]);

    await userEvent.click(screen.getByRole("columnheader", { name: /Name/ }));
    expect(names()).toEqual(["alpha", "Bravo", "Charlie"]);

    await userEvent.click(screen.getByRole("columnheader", { name: /Name/ }));
    expect(names()).toEqual(["Charlie", "Bravo", "alpha"]);

    await userEvent.click(screen.getByRole("columnheader", { name: /Name/ }));
    expect(names()).toEqual(["Charlie", "alpha", "Bravo"]);
  });

  it("sorts numbers numerically", async () => {
    renderTable();
    await userEvent.click(screen.getByRole("columnheader", { name: /Qty/ }));
    expect(names()).toEqual(["Charlie", "Bravo", "alpha"]);
  });

  it("shows the empty message", () => {
    render(
      <DataTable value={[]} aria-label="Empty" emptyMessage="Nothing here.">
        <Column field="name" header="Name" />
      </DataTable>,
    );
    expect(screen.getByText("Nothing here.")).toBeInTheDocument();
  });

  it("calls onRowClick with the row and applies row classes", async () => {
    const onRowClick = vi.fn();
    renderTable({ onRowClick, rowClassName: (r) => (r.id === "a" ? "hot" : "") });
    const row = screen.getAllByRole("row")[1];
    expect(row.className).toContain("hot");
    await userEvent.click(row);
    expect(onRowClick).toHaveBeenCalledWith(rows[0]);
  });

  it("keys rows by dataKey", () => {
    render(
      <DataTable value={[{ ticker: "SPY" }, { ticker: "QQQ" }]} dataKey="ticker" aria-label="Tickers" stripedRows>
        <Column field="ticker" header="Ticker" />
      </DataTable>,
    );
    expect(screen.getAllByRole("row")).toHaveLength(3);
  });
});
