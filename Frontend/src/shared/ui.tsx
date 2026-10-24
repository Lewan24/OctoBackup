import { cloneElement, useEffect, useId, useRef, useState } from "react";
import type { ReactElement, ReactNode, FormEvent } from "react";
import { AlertCircle, Database, LoaderCircle, X } from "lucide-react";
export function Empty({
  title,
  children,
}: {
  title: string;
  children?: ReactNode;
}) {
  return (
    <div className="empty">
      <Database size={32} aria-hidden="true" />
      <h3>{title}</h3>
      <p>{children}</p>
    </div>
  );
}
export function ErrorMessage({ error }: { error: unknown }) {
  return error ? (
    <div className="notice error" role="alert">
      <AlertCircle size={18} />
      <span>{error instanceof Error ? error.message : String(error)}</span>
    </div>
  ) : null;
}
export function Loading() {
  return (
    <p className="loading" role="status">
      <LoaderCircle className="spin" size={18} /> Loading your workspace…
    </p>
  );
}
export function Field({
  label,
  children,
}: {
  label: string;
  children: ReactElement<{ id?: string }>;
}) {
  const id = useId();
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      {cloneElement(children, { id })}
    </div>
  );
}
export function SubmitForm({
  onSubmit,
  children,
  label = "Save changes",
  onCancel,
  submitDisabled = false,
  cancelLabel = "Cancel",
}: {
  onSubmit: (data: FormData) => Promise<unknown>;
  children: ReactNode;
  label?: string;
  onCancel?: () => void;
  submitDisabled?: boolean;
  cancelLabel?: string;
}) {
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<unknown>();
  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const data = new FormData(e.currentTarget);
    setPending(true);
    setError(undefined);
    try {
      await onSubmit(data);
    } catch (e) {
      setError(e);
    } finally {
      setPending(false);
    }
  }
  return (
    <form onSubmit={submit} aria-busy={pending}>
      <fieldset disabled={pending}>{children}</fieldset>
      <ErrorMessage error={error} />
      <div className="form-actions">
        {onCancel && (
          <button
            type="button"
            className="secondary"
            disabled={pending}
            onClick={onCancel}
          >
            {cancelLabel}
          </button>
        )}
        <button disabled={pending || submitDisabled}>
          {pending && <LoaderCircle className="spin" size={16} />}{" "}
          {pending ? "Working…" : label}
        </button>
      </div>
    </form>
  );
}
export function PageHeading({
  title,
  description,
  action,
}: {
  title: string;
  description: string;
  action?: ReactNode;
}) {
  return (
    <div className="page-heading">
      <div>
        <h1>{title}</h1>
        <p>{description}</p>
      </div>
      {action}
    </div>
  );
}
export function Status({
  children,
  muted = false,
}: {
  children: ReactNode;
  muted?: boolean;
}) {
  return (
    <span className={`status ${muted ? "muted" : ""}`}>
      <span />
      {children}
    </span>
  );
}

export function Dialog({
  title,
  children,
  onClose,
}: {
  title: string;
  children: ReactNode;
  onClose: () => void;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  useEffect(() => {
    const dialog = ref.current!;
    const previous = document.activeElement as HTMLElement | null;
    dialog.showModal();
    const oldOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      dialog.close();
      document.body.style.overflow = oldOverflow;
      previous?.focus();
    };
  }, []);
  return (
    <dialog
      ref={ref}
      className="action-dialog"
      aria-labelledby={titleId}
      onKeyDown={(event) => {
        if (
          event.key !== "Tab" ||
          (event.target as HTMLElement).closest("dialog") !==
            event.currentTarget
        )
          return;
        const controls = Array.from(
          event.currentTarget.querySelectorAll<HTMLElement>(
            'button:not(:disabled), a[href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])',
          ),
        ).filter((element) => element.getClientRects().length > 0);
        const first = controls[0];
        const last = controls.at(-1);
        if (event.shiftKey && document.activeElement === first) {
          event.preventDefault();
          last?.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
          event.preventDefault();
          first?.focus();
        }
      }}
      onCancel={(e) => {
        e.preventDefault();
        if (!ref.current?.querySelector('form[aria-busy="true"]')) onClose();
      }}
    >
      <header className="dialog-heading">
        <div>
          <span className="dialog-eyebrow">Workspace action</span>
          <h2 id={titleId}>{title}</h2>
        </div>
        <button
          type="button"
          className="icon-button"
          aria-label="Close dialog"
          onClick={() => {
            if (!ref.current?.querySelector('form[aria-busy="true"]'))
              onClose();
          }}
        >
          <X />
        </button>
      </header>
      <div className="dialog-body">{children}</div>
    </dialog>
  );
}

export function ConfirmDialog({
  title,
  children,
  label = "Confirm",
  onConfirm,
  onClose,
}: {
  title: string;
  children: ReactNode;
  label?: string;
  onConfirm: () => Promise<unknown>;
  onClose: () => void;
}) {
  return (
    <Dialog title={title} onClose={onClose}>
      <SubmitForm
        label={label}
        onCancel={onClose}
        onSubmit={async () => {
          await onConfirm();
          onClose();
        }}
      >
        <div className="confirmation-details">{children}</div>
      </SubmitForm>
    </Dialog>
  );
}
