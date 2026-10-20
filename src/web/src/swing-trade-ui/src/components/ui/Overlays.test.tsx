import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { ConfirmDialog } from "./ConfirmDialog";
import { confirmDialog, setCurrent } from "./confirmDialogStore";
import { Drawer } from "./Drawer";
import { Modal } from "./Modal";

describe("Modal", () => {
  it("shows its header and content, and closes from the close button", async () => {
    const onClose = vi.fn();
    render(
      <Modal header="Edit" onClose={onClose}>
        <p>Body</p>
      </Modal>,
    );
    expect(screen.getByRole("dialog", { name: "Edit" })).toBeInTheDocument();
    expect(screen.getByText("Body")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Close" }));
    expect(onClose).toHaveBeenCalled();
  });

  it("renders nothing when closed", () => {
    render(
      <Modal open={false} header="Edit" onClose={() => undefined}>
        Body
      </Modal>,
    );
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("closes on Escape", async () => {
    const onClose = vi.fn();
    render(
      <Modal header="Edit" onClose={onClose}>
        Body
      </Modal>,
    );
    await userEvent.keyboard("{Escape}");
    expect(onClose).toHaveBeenCalled();
  });
});

describe("Drawer", () => {
  it("is labelled by its header and closes from the close button", async () => {
    const onClose = vi.fn();
    render(
      <Drawer open onClose={onClose} position="right" header="SPY Index">
        Content
      </Drawer>,
    );
    expect(screen.getByRole("dialog", { name: "SPY Index" })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Close" }));
    expect(onClose).toHaveBeenCalled();
  });

  it("falls back to the aria-label without a header", () => {
    render(
      <Drawer open onClose={() => undefined} aria-label="Menu">
        Content
      </Drawer>,
    );
    expect(screen.getByRole("dialog", { name: "Menu" })).toBeInTheDocument();
  });
});

describe("ConfirmDialog", () => {
  it("runs accept and closes", async () => {
    const accept = vi.fn();
    render(<ConfirmDialog />);
    act(() => confirmDialog({ message: "Sure?", header: "Cancel trade", acceptLabel: "Do it", accept }));
    expect(screen.getByRole("dialog", { name: "Cancel trade" })).toHaveTextContent("Sure?");
    await userEvent.click(screen.getByRole("button", { name: "Do it" }));
    expect(accept).toHaveBeenCalled();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("runs reject and closes, using the default labels", async () => {
    const reject = vi.fn();
    const accept = vi.fn();
    render(<ConfirmDialog />);
    act(() => confirmDialog({ message: "Sure?", accept, reject }));
    await userEvent.click(screen.getByRole("button", { name: "No" }));
    expect(reject).toHaveBeenCalled();
    expect(accept).not.toHaveBeenCalled();
    act(() => setCurrent(null));
  });
});
