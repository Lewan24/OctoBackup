import { useState } from "react";
import { api, resetCsrf } from "../../shared/api";
import { Dialog, PageHeading, Field, SubmitForm } from "../../shared/ui";
export function AccountPage() {
  const [editing, setEditing] = useState<"password" | "email" | null>(null);
  const [message, setMessage] = useState("");
  return (
    <>
      <PageHeading
        title="Account settings"
        description="Keep your sign-in details up to date."
      />
      {message && (
        <p className="notice" role="status">
          {message}
        </p>
      )}
      <div className="settings-grid">
        <section className="panel action-card">
          <h2>Password & security</h2>
          <p>
            Choose a strong password to protect your account. Updating it signs
            out existing sessions.
          </p>
          <button onClick={() => setEditing("password")}>
            Change password
          </button>
        </section>
        <section className="panel action-card">
          <h2>Email address</h2>
          <p>
            Update your sign-in address. Your current password is required to
            confirm the change.
          </p>
          <button className="secondary" onClick={() => setEditing("email")}>
            Change email
          </button>
        </section>
      </div>
      {editing === "password" && (
        <Dialog title="Change password" onClose={() => setEditing(null)}>
          <p>
            Use at least 12 characters, including uppercase, lowercase, a
            number, and a symbol.
          </p>
          <SubmitForm
            label="Update password"
            onCancel={() => setEditing(null)}
            onSubmit={async (d) => {
              if (d.get("password") !== d.get("confirm"))
                throw new Error("Passwords do not match.");
              await api("auth/ChangePassword", {
                currentPassword: d.get("current"),
                newPassword: d.get("password"),
                confirmNewPassword: d.get("confirm"),
              });
              resetCsrf();
              window.location.assign("/");
            }}
          >
            <Field label="Current password">
              <input
                name="current"
                type="password"
                autoComplete="current-password"
                required
              />
            </Field>
            <Field label="New password">
              <input
                name="password"
                type="password"
                autoComplete="new-password"
                minLength={12}
                maxLength={128}
                required
              />
            </Field>
            <Field label="Confirm new password">
              <input
                name="confirm"
                type="password"
                autoComplete="new-password"
                minLength={12}
                required
              />
            </Field>
            <p className="hint">
              Changing your password signs you out and invalidates existing
              sessions.
            </p>
          </SubmitForm>
        </Dialog>
      )}
      {editing === "email" && (
        <Dialog title="Change email" onClose={() => setEditing(null)}>
          <p>Your email is also your sign-in username.</p>
          <SubmitForm
            label="Update email"
            onCancel={() => setEditing(null)}
            onSubmit={async (d) => {
              await api("auth/ChangeEmail", {
                newEmail: d.get("email"),
                confirmNewEmail: d.get("confirmEmail"),
                currentPassword: d.get("emailPassword"),
              });
              resetCsrf();
              setMessage("Email updated. Sign in with your new address.");
              window.location.assign("/");
            }}
          >
            <Field label="Current password">
              <input
                name="emailPassword"
                type="password"
                autoComplete="current-password"
                required
              />
            </Field>
            <Field label="New email address">
              <input
                name="email"
                type="email"
                autoComplete="email"
                maxLength={100}
                required
              />
            </Field>
            <Field label="Confirm new email">
              <input
                name="confirmEmail"
                type="email"
                maxLength={100}
                required
              />
            </Field>
          </SubmitForm>
        </Dialog>
      )}
    </>
  );
}
