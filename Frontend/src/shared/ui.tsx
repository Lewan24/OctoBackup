import { cloneElement, useId, useState } from "react";
import type { ReactElement, ReactNode, FormEvent } from "react";
import { AlertCircle, Database, LoaderCircle } from "lucide-react";
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
    <form onSubmit={submit}>
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
