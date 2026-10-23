import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";
import { ErrorMessage, Field, Loading, SubmitForm } from "./ui";

export type EncryptionStatus = {
  keyId: string | null;
  usesGlobalKey: boolean;
  canExportGeneratedKey: boolean;
  globalEnabled: boolean;
  isEncrypted: boolean;
  revision: string;
};

export function BackupEncryptionEditor({
  serverId,
  admin,
  onClose,
}: {
  serverId?: string;
  admin: boolean;
  onClose?: () => void;
}) {
  const path = `backup-encryption/${serverId ? `servers/${serverId}` : "global"}`;
  const query = useQuery({
    queryKey: ["backup-encryption", serverId ?? "global"],
    queryFn: () => api<EncryptionStatus>(path),
    refetchOnWindowFocus: false,
  });
  const client = useQueryClient();
  const [saved, setSaved] = useState(false);
  return (
    <section className="panel editor">
      <h2>
        {serverId ? "Connection backup encryption" : "Global backup encryption"}
      </h2>
      <ErrorMessage error={query.error} />
      {query.isPending ? (
        <Loading />
      ) : (
        query.data && (
          <>
            <p>
              {!query.data.isEncrypted
                ? "New backups are not encrypted."
                : query.data.usesGlobalKey
                  ? "Uses the global encryption password."
                  : "Uses an individual encryption password."}
            </p>
            <p className="hint">
              Changes apply to future backups. Existing encrypted backups still
              require their original password; their saved key versions remain
              available for restore tests.
            </p>
            {saved && (
              <p className="notice" role="status">
                Encryption settings saved.
              </p>
            )}
            <EncryptionForm
              key={`${query.data.keyId ?? "none"}:${query.data.revision}`}
              status={query.data}
              serverId={serverId}
              admin={admin}
              onClose={onClose}
              onSave={async (body) => {
                await api(path, body);
                setSaved(true);
                await client.invalidateQueries({
                  queryKey: ["backup-encryption"],
                });
                await client.invalidateQueries({
                  queryKey: ["encryption-availability"],
                });
              }}
            />
            {!serverId && query.data.canExportGeneratedKey && (
              <LegacyRecovery />
            )}
          </>
        )
      )}
    </section>
  );
}

function EncryptionForm({
  status,
  serverId,
  admin,
  onClose,
  onSave,
}: {
  status: EncryptionStatus;
  serverId?: string;
  admin: boolean;
  onClose?: () => void;
  onSave: (body: unknown) => Promise<void>;
}) {
  const [enabled, setEnabled] = useState(status.globalEnabled);
  const [mode, setMode] = useState(
    status.usesGlobalKey ? "inherit" : "individual",
  );
  const [reset, setReset] = useState(false);
  const needsPassword = serverId ? mode === "individual" : enabled;
  const needsOld =
    status.isEncrypted &&
    !reset &&
    (serverId ? !status.usesGlobalKey : enabled);
  return (
    <SubmitForm
      label="Save encryption settings"
      onCancel={onClose}
      onSubmit={async (data) => {
        await onSave({
          expectedKeyId: status.keyId,
          expectedRevision: status.revision,
          oldPassword: data.get("oldPassword"),
          newPassword: data.get("newPassword"),
          confirmPassword: data.get("confirmPassword"),
          useGlobal: !!serverId && mode === "inherit",
          reset,
          enabled: serverId ? undefined : enabled,
        });
      }}
    >
      {!serverId && (
        <label className="check">
          <input
            type="checkbox"
            checked={enabled}
            onChange={(event) => setEnabled(event.target.checked)}
          />
          Enable global backup encryption
        </label>
      )}
      {!serverId && (
        <p className="hint">
          Disabled by default. Enabling requires a custom password and
          confirmation. Individual server passwords work independently of this
          switch.
        </p>
      )}
      {serverId && (
        <Field label="Encryption for future backups">
          <select
            value={mode}
            onChange={(event) => setMode(event.target.value)}
          >
            <option value="inherit">
              {status.globalEnabled
                ? "Use global encryption"
                : "Follow global setting (encryption disabled)"}
            </option>
            <option value="individual">Individual password</option>
          </select>
        </Field>
      )}
      {admin &&
        status.isEncrypted &&
        (serverId ? !status.usesGlobalKey : enabled) && (
          <label className="check">
            <input
              type="checkbox"
              checked={reset}
              onChange={(event) => setReset(event.target.checked)}
            />
            Administrator reset without the old key
          </label>
        )}
      {needsOld && (
        <Field label="Current encryption password">
          <input
            name="oldPassword"
            type="password"
            autoComplete="off"
            required
            maxLength={512}
          />
        </Field>
      )}
      {needsPassword && (
        <div className="form-grid">
          <Field label="New encryption password">
            <input
              name="newPassword"
              type="password"
              autoComplete="new-password"
              minLength={16}
              maxLength={512}
              required
            />
          </Field>
          <Field label="Confirm new encryption password">
            <input
              name="confirmPassword"
              type="password"
              autoComplete="new-password"
              minLength={16}
              maxLength={512}
              required
            />
          </Field>
        </div>
      )}
    </SubmitForm>
  );
}

function LegacyRecovery() {
  const [password, setPassword] = useState("");
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<unknown>();
  return (
    <div>
      <p className="hint">
        A previous release generated a recovery password. Save it to recover
        backups from that version. New global encryption requires your own
        password.
      </p>
      <button
        type="button"
        className="secondary"
        disabled={pending}
        onClick={async () => {
          if (password) {
            setPassword("");
            return;
          }
          setPending(true);
          setError(undefined);
          try {
            setPassword(
              (
                await api<{ password: string }>(
                  "backup-encryption/global/export",
                  {},
                )
              ).password,
            );
          } catch (error) {
            setError(error);
          } finally {
            setPending(false);
          }
        }}
      >
        {password
          ? "Hide previous generated key"
          : pending
            ? "Loading…"
            : "Reveal previous generated key"}
      </button>
      <ErrorMessage error={error} />
      {password && (
        <Field label="Previous generated encryption key">
          <input
            value={password}
            readOnly
            autoComplete="off"
            spellCheck={false}
          />
        </Field>
      )}
    </div>
  );
}
