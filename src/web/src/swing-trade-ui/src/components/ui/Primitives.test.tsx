import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { InlineMessage } from "../common/QueryState";
import { Spinner } from "./Spinner";
import { InputText } from "./TextField";

describe("ui primitives", () => {
  it("Spinner exposes an indeterminate progress bar", () => {
    render(<Spinner />);
    expect(screen.getByRole("progressbar", { name: "Loading" })).toBeInTheDocument();
  });

  it("InputText marks itself invalid", () => {
    render(<InputText aria-label="Ticker" invalid />);
    const input = screen.getByRole("textbox", { name: "Ticker" });
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(input.className).toContain("border-bad");
  });

  it("InputText respects an explicit aria-invalid", () => {
    render(<InputText aria-label="Ticker" aria-invalid={false} />);
    expect(screen.getByRole("textbox", { name: "Ticker" })).toHaveAttribute("aria-invalid", "false");
  });

  it("InlineMessage is an alert for errors and a status otherwise", () => {
    render(
      <>
        <InlineMessage severity="error">Broken</InlineMessage>
        <InlineMessage severity="info">Fyi</InlineMessage>
      </>,
    );
    expect(screen.getByRole("alert")).toHaveTextContent("Broken");
    expect(screen.getByRole("status")).toHaveTextContent("Fyi");
  });
});
