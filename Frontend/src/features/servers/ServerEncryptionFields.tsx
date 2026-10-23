import { useQuery } from "@tanstack/react-query";
import { api } from "../../shared/api";
import type { EncryptionStatus } from "../../shared/BackupEncryptionEditor";
import { ErrorMessage, Field, Loading } from "../../shared/ui";

export function useServerEncryption(serverId?: string) {
  return useQuery({
    queryKey: serverId
      ? ["backup-encryption", serverId]
      : ["encryption-availability"],
    queryFn: () =>
      serverId
        ? api<EncryptionStatus>(`backup-encryption/servers/${serverId}`)
        : api<{ globalEnabled: boolean }>("backup-encryption/availability"),
    refetchOnWindowFocus: false,
  });
}

export function ServerEncryptionFields({
  editing,
  globalEnabled,
  individual,
  ready,
  error,
  mode,
  onMode,
}: {
  editing: boolean;
  globalEnabled?: boolean;
  individual: boolean;
  ready: boolean;
  error: unknown;
  mode: string;
  onMode: (mode: string) => void;
}) {
  return (
    <section className="settings-section">
      <h3>Backup encryption</h3>
      <ErrorMessage error={error} />
      {!ready && !error && <Loading />}
      <Field label="Backup encryption mode">
        <select
          disabled={!ready}
          value={mode}
          onChange={(event) => {
            onMode(event.target.value);
          }}
        >
          {editing && (
            <option value="keep">Keep current encryption settings</option>
          )}
          <option value="inherit">
            {globalEnabled
              ? "Use global encryption"
              : "Follow global setting (encryption disabled)"}
          </option>
          <option value="individual">Individual password</option>
        </select>
      </Field>
      <p className="hint">
        {globalEnabled
          ? "Global encryption is enabled by an administrator."
          : "Global encryption is disabled. An individual password still encrypts this server's new backups."}{" "}
        Changes affect future backups.
      </p>
      {mode === "individual" && (
        <div className="form-grid">
          <Field label="Individual encryption password">
            <input
              name="encryptionPassword"
              type="password"
              autoComplete="new-password"
              required
              minLength={16}
              maxLength={512}
            />
          </Field>
          <Field label="Confirm individual encryption password">
            <input
              name="encryptionConfirmation"
              type="password"
              autoComplete="new-password"
              required
              minLength={16}
              maxLength={512}
            />
          </Field>
        </div>
      )}
      {editing && individual && mode !== "keep" && (
        <>
          <Field label="Current individual encryption password">
            <input
              name="currentEncryptionPassword"
              type="password"
              autoComplete="off"
              required
              maxLength={512}
            />
          </Field>
          <p className="hint">
            Forgot the old password? An administrator can reset this
            connection's key in Application settings.
          </p>
        </>
      )}
    </section>
  );
}
