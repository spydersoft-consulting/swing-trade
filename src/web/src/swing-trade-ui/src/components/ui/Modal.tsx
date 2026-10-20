import { faXmark } from "@fortawesome/free-solid-svg-icons";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { Dialog, Heading, Modal as AriaModal, ModalOverlay } from "react-aria-components";
import { Button } from "./Button";

interface ModalProps {
  open?: boolean;
  onClose: () => void;
  header?: React.ReactNode;
  /** CSS width, such as "min(30rem, 95vw)". */
  width?: string;
  children?: React.ReactNode;
}

export const Modal: React.FC<ModalProps> = ({ open = true, onClose, header, width, children }) => (
  <ModalOverlay
    isOpen={open}
    onOpenChange={(isOpen) => {
      if (!isOpen) onClose();
    }}
    isDismissable
    className="fixed inset-0 z-1100 flex items-end justify-center bg-black/40 sm:items-center"
  >
    <AriaModal
      style={{ width }}
      className="flex max-h-[90dvh] w-full flex-col overflow-hidden rounded-t-2xl bg-surface shadow-xl sm:rounded-xl"
    >
      <Dialog className="flex min-h-0 flex-1 flex-col outline-none" aria-label={header ? undefined : "Dialog"}>
        <div className="flex items-start justify-between gap-3 px-5 pt-5 pb-3">
          {header ? (
            <Heading slot="title" className="m-0 text-xl font-semibold text-content">
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
        <div className="min-h-0 flex-1 overflow-y-auto px-5 pt-2 pb-5">{children}</div>
      </Dialog>
    </AriaModal>
  </ModalOverlay>
);
