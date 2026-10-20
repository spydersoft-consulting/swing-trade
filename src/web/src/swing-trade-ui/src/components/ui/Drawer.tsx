import { faXmark } from "@fortawesome/free-solid-svg-icons";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { Dialog, Heading, Modal, ModalOverlay } from "react-aria-components";
import { Button } from "./Button";

interface DrawerProps {
  open: boolean;
  onClose: () => void;
  position?: "left" | "right";
  header?: React.ReactNode;
  /** CSS width, such as "min(40rem, 100vw)". */
  width?: string;
  "aria-label"?: string;
  children?: React.ReactNode;
}

export const Drawer: React.FC<DrawerProps> = ({
  open,
  onClose,
  position = "left",
  header,
  width = "18rem",
  "aria-label": ariaLabel,
  children,
}) => (
  <ModalOverlay
    isOpen={open}
    onOpenChange={(isOpen) => {
      if (!isOpen) onClose();
    }}
    isDismissable
    className={`fixed inset-0 z-1100 flex bg-black/40 ${position === "right" ? "justify-end" : "justify-start"}`}
  >
    <Modal style={{ width }} className="h-full max-w-full overflow-y-auto bg-surface shadow-xl">
      <Dialog aria-label={header ? undefined : (ariaLabel ?? "Panel")} className="min-h-full p-4 outline-none">
        <div className="mb-4 flex items-start justify-between gap-3">
          {header ? (
            <Heading slot="title" className="m-0 text-lg font-semibold">
              {header}
            </Heading>
          ) : (
            <span />
          )}
          <Button
            variant="text"
            size="sm"
            aria-label="Close"
            icon={<FontAwesomeIcon icon={faXmark} />}
            onClick={onClose}
          />
        </div>
        {children}
      </Dialog>
    </Modal>
  </ModalOverlay>
);
