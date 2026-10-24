import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import {
  ArrowUpRight,
  CheckCheck,
  Clock3,
  ListOrdered,
  LoaderCircle,
  X,
} from "lucide-react";
import { ConfirmDialog, ErrorMessage } from "../../shared/ui";
import {
  eligibilityText,
  useBackupQueue,
  useQueueActions,
} from "./useBackupQueue";

export function QueueHero() {
  const query = useBackupQueue();
  const act = useQueueActions();
  const [now, setNow] = useState(Date.now());
  const [cancelling, setCancelling] = useState<{
    id: string;
    name: string;
  } | null>(null);
  const data = query.data;
  const offset = useMemo(
    () => (data ? new Date(data.serverTimeUtc).getTime() - Date.now() : 0),
    [data?.serverTimeUtc],
  );
  const next = data?.nextRequest;
  useEffect(() => {
    if (!next) return;
    setNow(Date.now());
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [next?.id]);
  const running = data?.runningRequest;
  const title = query.isPending
    ? "Loading backup queue"
    : query.isError
      ? "Queue status unavailable"
      : running?.status === "Cancelling"
        ? "Stopping backup"
        : data?.workerBusy
          ? "Backup worker is running"
          : data?.queuedCount
            ? "Backups are queued"
            : "Backup queue is clear";
  return (
    <section
      className={`queue-hero ${data?.workerBusy ? "is-running" : ""}`}
      aria-label="Backup queue status"
    >
      <div className="queue-hero-main">
        <span className="queue-symbol" aria-hidden="true">
          {query.isPending || query.isError ? (
            <ListOrdered size={22} />
          ) : data?.workerBusy ? (
            <LoaderCircle className="spin" size={22} />
          ) : data?.queuedCount ? (
            <Clock3 size={22} />
          ) : (
            <CheckCheck size={22} />
          )}
        </span>
        <div>
          <h2>{title}</h2>
          <p>
            {query.isPending
              ? "Checking saved requests."
              : running
                ? `${running.serverName} / ${running.databaseName}`
                : data?.workerBusy
                  ? "Another authorized request is using the worker."
                  : next
                    ? `${next.serverName} / ${next.databaseName}`
                    : query.isError
                      ? "Refresh the queue to check pending requests."
                      : `Requests wait ${data?.cancellationDelaySeconds ?? "…"}s before execution so you have time to cancel.`}
          </p>
        </div>
        <Link to="/queue" className="button secondary small">
          <ListOrdered size={15} />
          View queue
          <ArrowUpRight size={14} />
        </Link>
      </div>
      {data && (
        <div className="queue-hero-details">
          <span>
            <strong>{data.queuedCount}</strong> queued
          </span>
          <span>
            <strong>{data.runningCount}</strong> running in your view
          </span>
          <span>
            {next
              ? eligibilityText(next.eligibleAt, now + offset)
              : `One request at a time · ${data.spacingSeconds}s between jobs`}
          </span>
          {next?.canCancel && (
            <button
              className="queue-cancel"
              onClick={() =>
                setCancelling({ id: next.id, name: next.serverName })
              }
            >
              <X size={13} />
              Cancel next request
            </button>
          )}
        </div>
      )}
      {cancelling && (
        <ConfirmDialog
          title={`Cancel backup · ${cancelling.name}`}
          label="Cancel backup"
          onClose={() => setCancelling(null)}
          onConfirm={() => act(cancelling.id, "Cancel")}
        >
          <p>
            Cancel request {cancelling.id}? Waiting work stops immediately. A
            running export will be asked to stop.
          </p>
        </ConfirmDialog>
      )}
      <ErrorMessage error={query.error} />
    </section>
  );
}
