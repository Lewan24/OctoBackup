import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api } from "../../shared/api";
import type { Server } from "../../shared/types";
import { BackupEncryptionEditor } from "../../shared/BackupEncryptionEditor";
import { Dialog, ErrorMessage, Field } from "../../shared/ui";

export function BackupEncryptionSettings() {
  const [editing, setEditing] = useState(false);
  const [serverId, setServerId] = useState("");
  const servers = useQuery({
    queryKey: ["servers"],
    queryFn: () => api<Server[]>("servers/GetMyServers"),
  });
  return (
    <>
      <section className="panel action-card">
        <h2>Backup encryption</h2>
        <p>
          Manage global encryption policy or an individual connection’s
          encryption key. Existing backup keys are preserved.
        </p>
        <button className="secondary" onClick={() => setEditing(true)}>
          Manage backup encryption
        </button>
      </section>
      {editing && (
        <Dialog
          title="Manage backup encryption"
          onClose={() => setEditing(false)}
        >
          <Field label="Encryption key scope">
            <select
              value={serverId}
              onChange={(event) => setServerId(event.target.value)}
            >
              <option value="">Global backup encryption key</option>
              {servers.data?.map((server) => (
                <option key={server.id} value={server.id}>
                  {server.connectionName} · {server.dbName}
                </option>
              ))}
            </select>
          </Field>
          <ErrorMessage error={servers.error} />
          <BackupEncryptionEditor
            key={serverId || "global"}
            serverId={serverId || undefined}
            admin
            onClose={() => setEditing(false)}
          />
        </Dialog>
      )}
    </>
  );
}
