import { useSyncExternalStore } from "react";
import { Button } from "./Button";
import { Modal } from "./Modal";
import { getSnapshot, setCurrent, subscribe } from "./confirmDialogStore";

/** Mount once on a screen that calls confirmDialog(). */
export const ConfirmDialog: React.FC = () => {
  const options = useSyncExternalStore(subscribe, getSnapshot);
  const close = () => setCurrent(null);

  return (
    <Modal open={options !== null} header={options?.header ?? "Confirm"} width="min(26rem, 95vw)" onClose={close}>
      <p className="mb-4 text-content">{options?.message}</p>
      <div className="flex justify-end gap-2">
        <Button
          label={options?.rejectLabel ?? "No"}
          variant="outlined"
          onClick={() => {
            options?.reject?.();
            close();
          }}
        />
        <Button
          label={options?.acceptLabel ?? "Yes"}
          severity="danger"
          onClick={() => {
            void options?.accept?.();
            close();
          }}
        />
      </div>
    </Modal>
  );
};
