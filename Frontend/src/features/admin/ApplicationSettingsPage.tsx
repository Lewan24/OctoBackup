import { BackupEncryptionSettings } from "./BackupEncryptionSettings";
import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Database,
  ShieldCheck,
  Archive,
  LockKeyhole,
  RefreshCw,
} from "lucide-react";
import { api } from "../../shared/api";
import {
  useRestoreAvailability,
  restoreRuntimeHelp,
} from "../../shared/restoreTesting";
import {
  ErrorMessage,
  Field,
  Loading,
  PageHeading,
  SubmitForm,
} from "../../shared/ui";

export type ApplicationSettings = {
  revision: string;
  allowedDatabaseHosts: string[];
  registrationEnabled: boolean;
  autoConfirmAccounts: boolean;
  defaultRetentionDays: number;
  schedulePollSeconds: number;
  backupQueueDelaySeconds: number;
  backupQueueSpacingSeconds: number;
  automaticRestoreTests: boolean;
  updatedAt: string | null;
  updatedBy: string | null;
};

const queryKey = ["application-settings"];

export function ApplicationSettingsPage() {
  const client = useQueryClient();
  const [saved, setSaved] = useState(false);
  const [editorVersion, setEditorVersion] = useState(0);
  const query = useQuery({
    queryKey,
    queryFn: ({ signal }) =>
      api<ApplicationSettings>("administration/settings", undefined, signal),
    // Never replace a dirty editor on a background focus/reconnect refresh.
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
  });
  return (
    <>
      <PageHeading
        title="Application settings"
        description="Manage backup encryption, network access, account registration, and backup defaults."
      />
      <ErrorMessage error={query.error} />
      {query.isPending ? (
        <Loading />
      ) : (
        query.data && (
          <div className="admin-settings-layout">
            <div>
              {saved && (
                <p className="notice" role="status">
                  Settings saved. Changes apply without restarting the API.
                </p>
              )}
              <BackupEncryptionSettings />
              <SettingsEditor
                key={`${query.data.revision}-${editorVersion}`}
                settings={query.data}
                onSave={(settings) => {
                  client.setQueryData(queryKey, settings);
                  void client.invalidateQueries({
                    queryKey: ["registration-policy"],
                  });
                  setSaved(true);
                }}
                onReload={() => {
                  setSaved(false);
                  void query.refetch().then((result) => {
                    if (!result.isError)
                      setEditorVersion((version) => version + 1);
                  });
                }}
              />
            </div>
            <aside
              className="panel settings-context"
              aria-label="Settings information"
            >
              <LockKeyhole size={20} />
              <h2>Managed at deployment</h2>
              <p>
                These settings require host access and an API restart. They are
                kept outside this panel.
              </p>
              <ul>
                <li>Data Protection key ring and bootstrap secrets</li>
                <li>Storage locations and cookie keys</li>
                <li>HTTPS, trusted proxies and incoming hostnames</li>
                <li>Verified SSH host-key fingerprints</li>
                <li>Password, session and rate-limit security rules</li>
              </ul>
              <h2>Changes and persistence</h2>
              <p>
                Saved values override the initial deployment defaults. They
                persist with application data across restarts.
              </p>
              <p>
                Changes are logged with your administrator ID. A stale save is
                rejected if another administrator changed settings.
              </p>
              {query.data.updatedAt && (
                <p className="settings-saved-at">
                  Last saved {new Date(query.data.updatedAt).toLocaleString()}
                </p>
              )}
            </aside>
          </div>
        )
      )}
    </>
  );
}

function SettingsEditor({
  settings,
  onSave,
  onReload,
}: {
  settings: ApplicationSettings;
  onSave: (settings: ApplicationSettings) => void;
  onReload: () => void;
}) {
  const availability = useRestoreAvailability();
  const [hosts, setHosts] = useState(settings.allowedDatabaseHosts.join("\n"));
  const [registration, setRegistration] = useState(
    settings.registrationEnabled,
  );
  const [autoConfirm, setAutoConfirm] = useState(settings.autoConfirmAccounts);
  const [automaticTests, setAutomaticTests] = useState(
    settings.automaticRestoreTests,
  );
  const [retention, setRetention] = useState(
    String(settings.defaultRetentionDays),
  );
  const [polling, setPolling] = useState(String(settings.schedulePollSeconds));
  const [queueDelay, setQueueDelay] = useState(
    String(settings.backupQueueDelaySeconds),
  );
  const [queueSpacing, setQueueSpacing] = useState(
    String(settings.backupQueueSpacingSeconds),
  );
  const dirty =
    automaticTests !== settings.automaticRestoreTests ||
    hosts !== settings.allowedDatabaseHosts.join("\n") ||
    registration !== settings.registrationEnabled ||
    autoConfirm !== settings.autoConfirmAccounts ||
    retention !== String(settings.defaultRetentionDays) ||
    polling !== String(settings.schedulePollSeconds) ||
    queueDelay !== String(settings.backupQueueDelaySeconds) ||
    queueSpacing !== String(settings.backupQueueSpacingSeconds);
  const reset = () => {
    setHosts(settings.allowedDatabaseHosts.join("\n"));
    setRegistration(settings.registrationEnabled);
    setAutoConfirm(settings.autoConfirmAccounts);
    setAutomaticTests(settings.automaticRestoreTests);
    setRetention(String(settings.defaultRetentionDays));
    setPolling(String(settings.schedulePollSeconds));
    setQueueDelay(String(settings.backupQueueDelaySeconds));
    setQueueSpacing(String(settings.backupQueueSpacingSeconds));
  };
  return (
    <section className="panel settings-editor">
      <SubmitForm
        label="Save settings"
        cancelLabel="Discard changes"
        submitDisabled={!dirty}
        onCancel={reset}
        onSubmit={async () => {
          const result = await api<ApplicationSettings>(
            "administration/settings",
            {
              revision: settings.revision,
              allowedDatabaseHosts: hosts
                .split(/\r?\n/)
                .map((host) => host.trim())
                .filter(Boolean),
              registrationEnabled: registration,
              autoConfirmAccounts: autoConfirm,
              automaticRestoreTests: automaticTests,
              defaultRetentionDays: Number(retention),
              schedulePollSeconds: Number(polling),
              backupQueueDelaySeconds: Number(queueDelay),
              backupQueueSpacingSeconds: Number(queueSpacing),
            },
          );
          onSave(result);
        }}
      >
        <section
          className="settings-section"
          aria-labelledby="network-settings"
        >
          <div className="settings-section-title">
            <Database size={18} />
            <h2 id="network-settings">Network access</h2>
          </div>
          <p>
            Approve the exact database hosts and SSH destinations this API may
            connect to. Add every database host, SSH gateway, and tunnel remote
            host used by a connection.
          </p>
          <Field label="Allowed database hosts">
            <textarea
              name="hosts"
              rows={6}
              value={hosts}
              onChange={(event) => setHosts(event.target.value)}
              maxLength={65536}
              spellCheck={false}
              autoCapitalize="none"
              placeholder={"db.internal\n192.168.1.50"}
              aria-describedby="host-help"
            />
          </Field>
          <p id="host-help" className="hint">
            One hostname or IPv4/IPv6 address per line, up to 256 hosts. No
            URLs, ports, paths, or wildcards. An empty list blocks all database
            operations. Removing a host blocks new tests and backups for
            existing connections; it does not interrupt a backup already in
            progress.
          </p>
        </section>
        <section
          className="settings-section"
          aria-labelledby="registration-settings"
        >
          <div className="settings-section-title">
            <ShieldCheck size={18} />
            <h2 id="registration-settings">Account registration</h2>
          </div>
          <p>
            Administrators can create and confirm operator accounts from Users &
            access regardless of public registration policy.
          </p>
          <label className="check">
            <input
              type="checkbox"
              checked={registration}
              onChange={(event) => setRegistration(event.target.checked)}
            />
            Allow public account registration
          </label>
          <label className="check">
            <input
              type="checkbox"
              checked={autoConfirm}
              disabled={!registration}
              onChange={(event) => setAutoConfirm(event.target.checked)}
            />
            Automatically confirm newly registered accounts
          </label>
          {registration && autoConfirm && (
            <p className="notice">
              Public sign-ups can sign in immediately. This does not verify
              mailbox ownership. Server access still requires administrator
              assignment.
            </p>
          )}
          {registration && !autoConfirm && (
            <p className="hint">
              New sign-ups must be confirmed by an administrator before they can
              sign in.
            </p>
          )}
        </section>
        <section className="settings-section" aria-labelledby="backup-settings">
          <div className="settings-section-title">
            <Archive size={18} />
            <h2 id="backup-settings">Backup defaults</h2>
          </div>
          <label className="check">
            <input
              type="checkbox"
              checked={automaticTests}
              disabled={!automaticTests && availability.data?.enabled !== true}
              onChange={(event) => setAutomaticTests(event.target.checked)}
            />
            Automatically restore-test successful backups
          </label>
          <ErrorMessage error={availability.error} />
          {availability.data?.enabled === false && (
            <p className="notice">{restoreRuntimeHelp}</p>
          )}
          <p className="hint">
            Requires the deployment restore worker, Docker access and preloaded
            engine images. When disabled, manual tests remain available; waiting
            automatic tests are skipped. A running test finishes normally. Saved
            previews contain sampled backup data.
          </p>
          <p>
            Set defaults for new connections and the interval for checking
            scheduled work.
          </p>
          <div className="form-grid">
            <Field label="Default retention (days)">
              <input
                type="number"
                min={1}
                max={3650}
                step={1}
                required
                value={retention}
                onChange={(event) => setRetention(event.target.value)}
              />
            </Field>
            <Field label="Schedule polling interval (seconds)">
              <input
                type="number"
                min={30}
                max={3600}
                step={1}
                required
                value={polling}
                onChange={(event) => setPolling(event.target.value)}
              />
            </Field>
          </div>
          <p className="hint">
            Retention applies to newly created server configurations; existing
            retention periods remain unchanged. Old copies are removed only
            after a successful replacement backup. Polling changes apply after
            the current polling wait or backup completes.
          </p>
          <div className="form-grid queue-settings-fields">
            <Field label="Backup cancellation window (seconds)">
              <input
                type="number"
                min={5}
                max={300}
                step={1}
                required
                value={queueDelay}
                onChange={(event) => setQueueDelay(event.target.value)}
              />
            </Field>
            <Field label="Delay between backup jobs (seconds)">
              <input
                type="number"
                min={1}
                max={300}
                step={1}
                required
                value={queueSpacing}
                onChange={(event) => setQueueSpacing(event.target.value)}
              />
            </Field>
          </div>
          <p className="hint">
            The cancellation window applies to newly queued manual and scheduled
            requests. Existing requests keep their original earliest start time.
            One backup runs at a time; spacing applies after each executed job
            finishes.
          </p>
        </section>
        <div className="settings-edit-state">
          <span>{dirty ? "Unsaved changes" : "All changes saved"}</span>
          <button className="text-button" type="button" onClick={onReload}>
            <RefreshCw size={13} />
            Reload saved settings
          </button>
        </div>
      </SubmitForm>
    </section>
  );
}
