import { BackupEncryptionEditor } from "../../shared/BackupEncryptionEditor";
import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Search, Play, Cable, Pencil, Power, Trash2 } from "lucide-react";
import { Link, useSearchParams } from "react-router-dom";
import { api } from "../../shared/api";
import type { Server } from "../../shared/types";
import { engineCodes, engines } from "../../shared/types";
import {
  Empty,
  ErrorMessage,
  Loading,
  PageHeading,
  Status,
  Field,
  SubmitForm,
} from "../../shared/ui";
import { ServerForm } from "./ServerForm";
export function ServersPage({
  admin,
  activeServerIds,
  queueUnavailable,
}: {
  admin: boolean;
  activeServerIds: string[];
  queueUnavailable: boolean;
}) {
  const client = useQueryClient();
  const [params] = useSearchParams();
  const [editing, setEditing] = useState<Server | "new" | null>(
    params.has("new") && admin ? "new" : null,
  );
  const [search, setSearch] = useState("");
  const [encryption, setEncryption] = useState<Server | null>(null);
  const [access, setAccess] = useState<Server | null>(null);
  const [notice, setNotice] = useState("");
  const [error, setError] = useState<unknown>();
  const [pending, setPending] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<string | null>(null);
  const query = useQuery({
    queryKey: ["servers"],
    queryFn: ({ signal }) =>
      api<Server[]>("servers/GetMyServers", undefined, signal),
  });
  async function action(id: string, path: string, message: string) {
    setPending(id);
    setError(undefined);
    setNotice("");
    try {
      await api(path, id);
      setNotice(message);
      await client.invalidateQueries();
    } catch (e) {
      setError(e);
    } finally {
      setPending(null);
      setDeleting(null);
    }
  }
  const filtered =
    query.data?.filter((s) =>
      `${s.connectionName} ${s.dbName} ${s.serverHost}`
        .toLowerCase()
        .includes(search.toLowerCase()),
    ) ?? [];
  return (
    <>
      <PageHeading
        title="Servers"
        description="Connections, credentials, and on-demand backups."
        action={
          admin && (
            <button
              onClick={() => {
                setEditing("new");
                setAccess(null);
              }}
            >
              <Plus size={17} />
              Connect a server
            </button>
          )
        }
      />
      <ErrorMessage error={error} />
      {admin && (
        <p className="host-policy-hint">
          Host blocked?{" "}
          <Link to="/settings">
            Manage allowed database and SSH hosts in Application settings{" "}
            <Plus size={12} />
          </Link>
        </p>
      )}
      {notice && (
        <div className="notice" role="status">
          {notice}
        </div>
      )}
      {editing && (
        <section className="panel editor">
          <h2>
            {editing === "new" ? "Connect a database" : "Edit connection"}
          </h2>
          <p>
            Database and SSH secrets are write-only. Leave existing secrets
            blank to keep them.
          </p>
          <ServerForm
            server={editing === "new" ? undefined : editing}
            onCancel={() => setEditing(null)}
            onSaved={async () => {
              setEditing(null);
              await client.invalidateQueries();
            }}
          />
        </section>
      )}
      {encryption && (
        <BackupEncryptionEditor
          key={encryption.id}
          serverId={encryption.id}
          admin={admin}
          onClose={() => setEncryption(null)}
        />
      )}
      {access && (
        <AccessPanel server={access} onClose={() => setAccess(null)} />
      )}
      <div className="toolbar">
        <label className="search">
          <Search size={18} />
          <input
            aria-label="Search servers"
            placeholder="Search servers, databases, or hosts…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </label>
        <span>{filtered.length} connections</span>
      </div>
      {query.isPending ? (
        <Loading />
      ) : query.isError ? (
        <ErrorMessage error={query.error} />
      ) : !filtered.length ? (
        <section className="panel">
          <Empty
            title={search ? "No matching connections" : "No servers connected"}
          >
            {search
              ? "Try another name, database, or host."
              : admin
                ? "Connect your first database to start making backups."
                : "Ask an administrator to grant access to a server."}
          </Empty>
        </section>
      ) : (
        <div className="server-grid">
          {filtered.map((s) => (
            <article className="panel server-panel" key={s.id}>
              <div className="server-top">
                <span className={`engine-icon large engine-${s.dbType}`}>
                  {engineCodes[s.dbType]}
                </span>
                <Status muted={s.isDisabled}>
                  {s.isDisabled ? "Disabled" : "Enabled"}
                </Status>
              </div>
              <h2>{s.connectionName}</h2>
              <p>
                {engines[s.dbType]} · {s.dbName}
              </p>
              <dl>
                <div>
                  <dt>Host</dt>
                  <dd>
                    {s.serverHost}:{s.serverPort}
                  </dd>
                </div>
                <div>
                  <dt>Database user</dt>
                  <dd>{s.dbUser}</dd>
                </div>
                <div>
                  <dt>Transport</dt>
                  <dd>
                    {s.dbType === 3
                      ? "Mounted SQLite file"
                      : s.ssL
                        ? "TLS requested"
                        : "TLS disabled"}
                    {s.isTunnelRequired ? " · SSH tunnel" : ""}
                  </dd>
                </div>
              </dl>
              <div className="server-actions">
                <button
                  disabled={
                    pending !== null ||
                    s.isDisabled ||
                    queueUnavailable ||
                    activeServerIds.includes(s.id)
                  }
                  onClick={() =>
                    void action(
                      s.id,
                      "backups/PerformBackup",
                      "Backup request saved. You can cancel it from the queue before execution.",
                    )
                  }
                >
                  <Play size={15} />
                  {pending === s.id
                    ? "Requesting…"
                    : activeServerIds.includes(s.id)
                      ? "In queue"
                      : "Queue backup"}
                </button>
                <button
                  className="secondary"
                  disabled={pending !== null || s.isDisabled}
                  onClick={() =>
                    void action(
                      s.id,
                      "servers/TestServerConnection",
                      "Connection test succeeded.",
                    )
                  }
                >
                  <Cable size={16} />
                  Test
                </button>
                {admin && (
                  <button
                    className="icon-button"
                    aria-label={`Edit ${s.connectionName}`}
                    onClick={() => setEditing(s)}
                  >
                    <Pencil size={16} />
                  </button>
                )}
              </div>
              <button
                type="button"
                className="text-button"
                disabled={!admin && s.isDisabled}
                onClick={() => setEncryption(s)}
              >
                Backup encryption
              </button>
              {admin && (
                <div className="server-admin">
                  <button className="text-button" onClick={() => setAccess(s)}>
                    Manage access
                  </button>
                  <button
                    className="icon-button"
                    aria-label={`${s.isDisabled ? "Enable" : "Disable"} ${s.connectionName}`}
                    disabled={pending !== null}
                    onClick={() =>
                      void action(
                        s.id,
                        "servers/ToggleServerDisabledStatus",
                        "Server state updated.",
                      )
                    }
                  >
                    <Power size={16} />
                  </button>
                  <button
                    className="icon-button danger-text"
                    aria-label={`Delete ${s.connectionName}`}
                    onClick={() => setDeleting(s.id)}
                  >
                    <Trash2 size={16} />
                  </button>
                </div>
              )}
              {deleting === s.id && (
                <div className="delete-confirm">
                  <p>
                    Remove this connection, its schedules, and access
                    assignments? Existing backup records remain.
                  </p>
                  <button
                    className="danger"
                    disabled={pending !== null}
                    onClick={() =>
                      void action(
                        s.id,
                        "servers/CascadeDeleteServer",
                        "Server removed.",
                      )
                    }
                  >
                    Remove server
                  </button>
                  <button
                    className="secondary"
                    onClick={() => setDeleting(null)}
                  >
                    Keep server
                  </button>
                </div>
              )}
            </article>
          ))}
        </div>
      )}
    </>
  );
}
function AccessPanel({
  server,
  onClose,
}: {
  server: Server;
  onClose: () => void;
}) {
  const client = useQueryClient();
  const [error, setError] = useState<unknown>();
  const [busy, setBusy] = useState(false);
  const query = useQuery({
    queryKey: ["access", server.id],
    queryFn: () => api<string[]>("servers/GetUsersThatAccessServer", server.id),
  });
  return (
    <section className="panel editor">
      <h2>Access to {server.connectionName}</h2>
      <p>
        Administrators have access to all servers. Grant operators access by
        email.
      </p>
      <ErrorMessage error={query.error ?? error} />
      {query.isPending ? (
        <Loading />
      ) : (
        <ul className="access-list">
          {query.data?.map((email) => (
            <li key={email}>
              <span>{email}</span>
              <button
                className="secondary"
                disabled={busy}
                onClick={async () => {
                  setBusy(true);
                  setError(undefined);
                  try {
                    await api("servers/RemoveUserAccessFromServer", {
                      serverId: server.id,
                      userEmail: email,
                    });
                    await client.invalidateQueries();
                  } catch (e) {
                    setError(e);
                  } finally {
                    setBusy(false);
                  }
                }}
              >
                Revoke access
              </button>
            </li>
          ))}
        </ul>
      )}
      <SubmitForm
        label="Grant access"
        onCancel={onClose}
        onSubmit={async (d) => {
          await api("servers/GiveUserAccessToServer", {
            serverId: server.id,
            userEmail: d.get("email"),
          });
          await client.invalidateQueries();
        }}
      >
        <Field label="User email">
          <input
            name="email"
            type="email"
            required
            placeholder="operator@company.com"
          />
        </Field>
      </SubmitForm>
    </section>
  );
}
