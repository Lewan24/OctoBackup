import { useState } from "react";
import { api, resetCsrf } from "../../shared/api";
import { PageHeading, Field, SubmitForm } from "../../shared/ui";
export function AccountPage() {
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
        <section className="panel editor">
          <h2>Change password</h2>
          <p>
            Use at least 12 characters, including uppercase, lowercase, a
            number, and a symbol.
          </p>
          <SubmitForm
            label="Update password"
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
        </section>
        <section className="panel editor">
          <h2>Change email</h2>
          <p>Your email is also your sign-in username.</p>
          <SubmitForm
            label="Update email"
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
        </section>
      </div>
    </>
  );
}
