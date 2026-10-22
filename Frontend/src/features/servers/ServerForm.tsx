import { useState } from "react";
import { api } from "../../shared/api";
import type { Server } from "../../shared/types";
import { engines } from "../../shared/types";
import { Field, SubmitForm } from "../../shared/ui";
export function ServerForm({
  server,
  onSaved,
  onCancel,
}: {
  server?: Server;
  onSaved: () => Promise<unknown>;
  onCancel: () => void;
}) {
  const [tunnel, setTunnel] = useState(server?.isTunnelRequired ?? false);
  const [engine, setEngine] = useState(server?.dbType ?? 0);
  return (
    <SubmitForm
      label={server ? "Save connection" : "Connect server"}
      onCancel={onCancel}
      onSubmit={async (d) => {
        const text = (key: string) => String(d.get(key) ?? "");
        const payload = {
          id: server?.id ?? crypto.randomUUID(),
          connectionName: text("name"),
          serverHost: text("host"),
          serverPort: Number(d.get("port")),
          dbName: text("database"),
          dbType: engine,
          dbUser: text("user"),
          dbPasswd: text("password") || null,
          ssL: d.has("tls"),
          isTunnelRequired: tunnel,
          tunnel: tunnel
            ? {
                id: server?.tunnel?.id ?? crypto.randomUUID(),
                serverHost: text("sshHost"),
                sshPort: Number(d.get("sshPort")),
                username: text("sshUser"),
                password: text("sshPassword") || null,
                privateKeyContent: text("sshKey") || null,
                privateKeyPassphrase: text("sshPassphrase") || null,
                overridePasswordsAndPem: d.has("override"),
                localPort: Number(d.get("localPort")),
                remoteHost: text("remoteHost"),
                remotePort: Number(d.get("remotePort")),
                description: text("description"),
              }
            : null,
        };
        await api(`servers/${server ? "EditServer" : "CreateServer"}`, payload);
        await onSaved();
      }}
    >
      <div className="form-grid">
        <Field label="Connection name">
          <input
            name="name"
            defaultValue={server?.connectionName}
            placeholder="Production database"
            maxLength={120}
            required
          />
        </Field>
        <Field label="Database engine">
          <select
            value={engine}
            onChange={(e) => setEngine(Number(e.target.value))}
          >
            {engines.map((e, i) => (
              <option value={i} key={e}>
                {e}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Database host">
          <input
            name="host"
            defaultValue={server?.serverHost}
            placeholder="db.internal"
            maxLength={253}
            required
          />
        </Field>
        <Field label="Port">
          <input
            name="port"
            type="number"
            min={1}
            max={65535}
            key={engine}
            defaultValue={server?.serverPort ?? [3306, 5432, 1433][engine]}
            required
          />
        </Field>
        <Field label="Database name">
          <input
            name="database"
            defaultValue={server?.dbName}
            required
            pattern="[A-Za-z0-9_][A-Za-z0-9_.\-]*"
            maxLength={128}
          />
        </Field>
        <Field label="Database user">
          <input
            name="user"
            defaultValue={server?.dbUser}
            required
            maxLength={128}
            autoComplete="off"
          />
        </Field>
        <Field
          label={
            server ? "New database password (optional)" : "Database password"
          }
        >
          <input
            name="password"
            type="password"
            autoComplete="new-password"
            required={!server}
            maxLength={512}
          />
        </Field>
        <div className="form-checks">
          <label className="check">
            <input
              name="tls"
              type="checkbox"
              defaultChecked={server?.ssL ?? true}
            />
            Require verified TLS
          </label>
          <label className="check">
            <input
              type="checkbox"
              checked={tunnel}
              onChange={(e) => setTunnel(e.target.checked)}
            />
            Connect through SSH
          </label>
        </div>
      </div>
      {engine === 2 && (
        <p className="notice">
          SQL Server writes .bak files on the database host. The backup path
          must be mounted at the same location in both environments.
        </p>
      )}
      <p className="hint">
        Connection hosts must be approved in the backend host allowlist.
      </p>
      {tunnel && (
        <>
          <h3>SSH tunnel</h3>
          <div className="form-grid">
            <Field label="SSH host">
              <input
                name="sshHost"
                defaultValue={server?.tunnel?.serverHost}
                required
              />
            </Field>
            <Field label="SSH port">
              <input
                name="sshPort"
                type="number"
                min={1}
                max={65535}
                defaultValue={server?.tunnel?.sshPort ?? 22}
                required
              />
            </Field>
            <Field label="SSH username">
              <input
                name="sshUser"
                defaultValue={server?.tunnel?.username}
                required
              />
            </Field>
            <Field label="SSH password">
              <input
                name="sshPassword"
                type="password"
                autoComplete="new-password"
              />
            </Field>
            <Field label="Local forwarded port">
              <input
                name="localPort"
                type="number"
                min={1024}
                max={65535}
                defaultValue={server?.tunnel?.localPort ?? 15432}
                required
              />
            </Field>
            <Field label="Remote database host">
              <input
                name="remoteHost"
                defaultValue={server?.tunnel?.remoteHost}
                required
              />
            </Field>
            <Field label="Remote database port">
              <input
                name="remotePort"
                type="number"
                min={1}
                max={65535}
                defaultValue={
                  server?.tunnel?.remotePort ?? [3306, 5432, 1433][engine]
                }
                required
              />
            </Field>
            <Field label="Private key passphrase">
              <input
                name="sshPassphrase"
                type="password"
                autoComplete="new-password"
              />
            </Field>
          </div>
          <Field label="Private key (alternative to password)">
            <textarea name="sshKey" rows={4} autoComplete="off" />
          </Field>
          <Field label="Tunnel description">
            <input
              name="description"
              defaultValue={server?.tunnel?.description}
            />
          </Field>
          {server && (
            <label className="check">
              <input name="override" type="checkbox" />
              Replace existing SSH password and key with the values above
            </label>
          )}
          <p className="hint">
            SSH host keys must be pinned by the administrator before connecting.
          </p>
        </>
      )}
    </SubmitForm>
  );
}
