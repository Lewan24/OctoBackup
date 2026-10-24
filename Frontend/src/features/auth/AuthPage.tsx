import logo from "../../media/OctoBackupLogo.png";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ArrowUpRight, ShieldCheck, CalendarClock, Layers } from "lucide-react";
import { api, resetCsrf } from "../../shared/api";
import { Field, SubmitForm } from "../../shared/ui";
export function AuthPage({ onLogin }: { onLogin: () => Promise<unknown> }) {
  const [register, setRegister] = useState(false);
  const [message, setMessage] = useState("");
  const policy = useQuery({
    queryKey: ["registration-policy"],
    queryFn: () =>
      api<{ enabled: boolean; autoConfirm: boolean }>(
        "auth/registration-policy",
      ),
  });
  return (
    <main className="auth-layout">
      <section className="auth-story">
        <a className="brand" href="/">
          <img className="auth-logo" src={logo} alt="" /> OctoBackup
        </a>
        <div>
          <h1>
            Your databases.
            <br />
            Your next copy.
            <br />
            <span>Your control.</span>
          </h1>
          <p>
            Database backup operations, without the noise. Connections,
            schedules, and history in one workspace.
          </p>
          <div className="auth-benefits">
            <span>
              <Layers /> Multiple database engines
            </span>
            <span>
              <CalendarClock /> Backups on your schedule
            </span>
            <span>
              <ShieldCheck /> Access for the right people
            </span>
          </div>
        </div>
        <small>MySQL / PostgreSQL / SQL Server</small>
      </section>
      <section className="auth-panel">
        <div className="auth-form">
          <h2>{register ? "Create your account" : "Welcome back."}</h2>
          <p>
            {register
              ? "Registration must be enabled by your administrator."
              : "Sign in to your backup workspace."}
          </p>
          <SubmitForm
            label={register ? "Create account" : "Sign in"}
            onSubmit={async (d) => {
              await api(`auth/${register ? "Register" : "Login"}`, {
                email: d.get("email"),
                password: d.get("password"),
                passwordConfirm: d.get("confirmation"),
                rememberMe: d.has("remember"),
              });
              resetCsrf();
              if (register) {
                setMessage(
                  policy.data?.autoConfirm
                    ? "Account created. You can now sign in."
                    : "Account created. Ask your administrator to confirm access before signing in.",
                );
                setRegister(false);
              } else await onLogin();
            }}
          >
            <Field label="Email address">
              <input
                name="email"
                type="email"
                autoComplete="username"
                placeholder="you@company.com"
                required
                maxLength={254}
              />
            </Field>
            <Field label="Password">
              <input
                name="password"
                type="password"
                autoComplete={register ? "new-password" : "current-password"}
                required
                minLength={register ? 12 : 1}
                maxLength={128}
              />
            </Field>
            {register && (
              <Field label="Confirm password">
                <input
                  name="confirmation"
                  type="password"
                  autoComplete="new-password"
                  required
                  minLength={12}
                />
              </Field>
            )}
            {!register && (
              <label className="check">
                <input type="checkbox" name="remember" /> Keep me signed in
              </label>
            )}
          </SubmitForm>
          {message && (
            <p role="status" className="notice">
              {message}
            </p>
          )}
          {(policy.data?.enabled || register) && (
            <button
              className="text-button"
              onClick={() => {
                setRegister(!register);
                setMessage("");
              }}
            >
              {register ? "Back to sign in" : "Need an account? Register"}
              <ArrowUpRight size={16} />
            </button>
          )}
          <p className="auth-note">
            Self-hosted. Your data stays in your infrastructure.
          </p>
        </div>
      </section>
    </main>
  );
}
