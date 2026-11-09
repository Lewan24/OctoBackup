import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { Play, RefreshCw, Trash2, X } from "lucide-react";
import { api } from "../../shared/api";
import { date } from "../../shared/types";
import {
  ConfirmDialog,
  Empty,
  ErrorMessage,
  Loading,
  PageHeading,
} from "../../shared/ui";
import { useBackupQueue, useQueueActions } from "./useBackupQueue";

export function QueuePage() {
  const client = useQueryClient();
  const [confirmation, setConfirmation] = useState<{
    id: string;
    kind: "Cancel" | "Delete" | "Retry";
    serverId?: string;
    name: string;
  } | null>(null);
  const [page, setPage] = useState(0);
  const query = useBackupQueue(page * 25);
  const act = useQueueActions();
  return (
    <>
      <PageHeading
        title="Backup queue"
        description="Manual and scheduled requests, with time to cancel before they start."
        action={
          <button className="secondary" onClick={() => void query.refetch()}>
            <RefreshCw size={15} />
            Refresh queue
          </button>
        }
      />
      <ErrorMessage error={query.error} />
      {confirmation && (
        <ConfirmDialog
          title={`${confirmation.kind === "Cancel" ? "Cancel backup" : confirmation.kind === "Retry" ? "Queue another backup" : "Remove request"} · ${confirmation.name}`}
          label={
            confirmation.kind === "Cancel"
              ? "Cancel backup"
              : confirmation.kind === "Retry"
                ? "Queue another backup"
                : "Remove request"
          }
          onClose={() => setConfirmation(null)}
          onConfirm={async () => {
            if (confirmation.kind === "Retry") {
              await api("backups/PerformBackup", confirmation.serverId);
              await client.invalidateQueries({ queryKey: ["backup-queue"] });
            } else await act(confirmation.id, confirmation.kind);
          }}
        >
          <p>Request {confirmation.id}</p>
          <p>
            {confirmation.kind === "Cancel"
              ? "Stop this queued request or ask its running export to stop. A scheduled occurrence will be skipped."
              : confirmation.kind === "Retry"
                ? "Save a new delayed backup request for this connection. The previous request is retained."
                : "Hide this request from the queue. Waiting work will be cancelled. Existing backup files remain available."}
          </p>
        </ConfirmDialog>
      )}
      <p className="queue-help">
        Cancel stops a queued request immediately or asks a running export to
        stop. Remove cancels a waiting request and hides it from the list.
        Running requests must finish stopping before removal. Cancelling a
        scheduled request skips that occurrence.
      </p>
      {query.isPending ? (
        <Loading />
      ) : (
        query.data && (
          <section className="panel">
            {query.data.requests.length ? (
              <div className="table-scroll">
                <table className="queue-table">
                  <thead>
                    <tr>
                      <th scope="col">Database / request</th>
                      <th scope="col">Source</th>
                      <th scope="col">Status</th>
                      <th scope="col">Timing</th>
                      <th scope="col">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {query.data.requests.map((request) => (
                      <tr key={request.id}>
                        <td>
                          <strong>{request.serverName}</strong>
                          <small className="block">
                            {request.databaseName}
                          </small>
                          <span className="subtle request-code">
                            {request.id.slice(0, 8)}
                          </span>
                        </td>
                        <td>
                          {request.source}
                          <small className="block">{request.requestedBy}</small>
                        </td>
                        <td>
                          <span
                            className={`queue-state state-${request.status.toLowerCase()}`}
                          >
                            {request.status === "Cancelling"
                              ? "Stopping…"
                              : request.status}
                          </span>
                          {request.message && (
                            <small className="block queue-outcome">
                              {request.message}
                            </small>
                          )}
                        </td>
                        <td>
                          <span>Requested {date(request.createdAt)}</span>
                          <small className="block">
                            {request.finishedAt
                              ? `Finished ${date(request.finishedAt)}`
                              : request.startedAt
                                ? `Started ${date(request.startedAt)}`
                                : `Not before ${date(request.eligibleAt)}`}
                          </small>
                        </td>
                        <td>
                          <div className="inline-actions">
                            {request.canCancel && (
                              <button
                                className="secondary small"
                                onClick={() =>
                                  setConfirmation({
                                    id: request.id,
                                    kind: "Cancel",
                                    name: request.serverName,
                                  })
                                }
                                aria-label={`Cancel backup for ${request.serverName}`}
                              >
                                <X size={14} />
                                Cancel
                              </button>
                            )}
                            {request.canDelete && (
                              <button
                                className="icon-button danger-text"
                                onClick={() =>
                                  setConfirmation({
                                    id: request.id,
                                    kind: "Delete",
                                    name: request.serverName,
                                  })
                                }
                                aria-label={`Remove request for ${request.serverName}`}
                                title="Cancel if queued and remove from this list"
                              >
                                <Trash2 size={15} />
                              </button>
                            )}
                            {request.backupId && (
                              <Link className="text-link" to="/backups">
                                View backup
                              </Link>
                            )}
                            {request.canDelete &&
                              ["Failed", "Cancelled", "Interrupted"].includes(
                                request.status,
                              ) && (
                                <button
                                  className="icon-button"
                                  aria-label={`Queue another backup for ${request.serverName}`}
                                  title="Create a new delayed request"
                                  onClick={() =>
                                    setConfirmation({
                                      id: request.id,
                                      serverId: request.serverId,
                                      kind: "Retry",
                                      name: request.serverName,
                                    })
                                  }
                                >
                                  <Play size={15} />
                                </button>
                              )}
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : (
              <Empty title="No queue requests">
                Request a backup from Servers. Manual and scheduled requests
                will appear here.
              </Empty>
            )}
            <div className="pagination">
              <span>
                {query.data.totalCount} requests · Page {page + 1}
              </span>
              <div>
                <button
                  className="secondary"
                  disabled={page === 0}
                  onClick={() => setPage(page - 1)}
                >
                  Previous
                </button>
                <button
                  className="secondary"
                  disabled={(page + 1) * 25 >= query.data.totalCount}
                  onClick={() => setPage(page + 1)}
                >
                  Next
                </button>
              </div>
            </div>
          </section>
        )
      )}
    </>
  );
}
