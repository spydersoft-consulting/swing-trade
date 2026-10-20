import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { Button } from "./Button";

describe("Button", () => {
  it("renders the label and fires onClick", async () => {
    const onClick = vi.fn();
    render(<Button label="Save" onClick={onClick} />);
    await userEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it("renders children when there is no label", () => {
    render(<Button>Child</Button>);
    expect(screen.getByRole("button", { name: "Child" })).toBeInTheDocument();
  });

  it("is disabled while loading and does not click", async () => {
    const onClick = vi.fn();
    render(<Button label="Save" loading onClick={onClick} />);
    const button = screen.getByRole("button", { name: "Save" });
    expect(button).toBeDisabled();
    await userEvent.click(button);
    expect(onClick).not.toHaveBeenCalled();
  });

  it("is disabled when disabled is set", () => {
    render(<Button label="Save" disabled />);
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
  });

  it("places the icon on the requested side", () => {
    render(<Button label="Go" iconPos="right" icon={<span data-testid="icon" />} />);
    const button = screen.getByRole("button", { name: "Go" });
    expect(button.lastElementChild).toBe(screen.getByTestId("icon"));
  });

  it("applies the variant, severity and size classes", () => {
    render(<Button label="Delete" variant="outlined" severity="danger" size="sm" className="extra" />);
    const cls = screen.getByRole("button", { name: "Delete" }).className;
    expect(cls).toContain("text-bad");
    expect(cls).toContain("min-h-9");
    expect(cls).toContain("extra");
  });
});
