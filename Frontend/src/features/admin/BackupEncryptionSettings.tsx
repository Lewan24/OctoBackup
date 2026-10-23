import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api } from "../../shared/api";
import type { Server } from "../../shared/types";
import { BackupEncryptionEditor } from "../../shared/BackupEncryptionEditor";
import { ErrorMessage, Field } from "../../shared/ui";

export function BackupEncryptionSettings() {
  const [serverId, setServerId] = useState("");
  const servers = useQuery({
    queryKey: ["servers"],
    queryFn: () => api<Server[]>("servers/GetMyServers"),
  });
  return (
    <>
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
      />
    </>
  );
}
