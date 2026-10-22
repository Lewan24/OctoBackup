import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../shared/api";

export type BackupRequestStatus =
  | "Queued"
  | "Running"
  | "Cancelling"
  | "Succeeded"
  | "Failed"
  | "Cancelled"
  | "Interrupted";
export type BackupRequest = {
  id: string;
  serverId: string;
  serverName: string;
  databaseName: string;
  source: string;
  requestedBy: string;
  status: BackupRequestStatus;
  createdAt: string;
  eligibleAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  message: string | null;
  backupId: string | null;
  canCancel: boolean;
  canDelete: boolean;
};
export type BackupQueue = {
  requests: BackupRequest[];
  totalCount: number;
  activeServerIds: string[];
  queuedCount: number;
  runningCount: number;
  workerBusy: boolean;
  runningRequest: BackupRequest | null;
  nextRequest: BackupRequest | null;
  cancellationDelaySeconds: number;
  spacingSeconds: number;
  serverTimeUtc: string;
};
export function useBackupQueue(offset = 0) {
  return useQuery({
    queryKey: ["backup-queue", offset],
    queryFn: ({ signal }) =>
      api<BackupQueue>(
        `backup-queue?offset=${offset}&limit=25`,
        undefined,
        signal,
      ),
    refetchInterval: (query) =>
      query.state.data &&
      (query.state.data.queuedCount || query.state.data.runningCount)
        ? 5000
        : 20000,
    staleTime: 2000,
  });
}
export function useQueueActions() {
  const client = useQueryClient();
  return async (id: string, action: "Cancel" | "Delete") => {
    try {
      await api(`backup-queue/${action}`, id);
    } finally {
      await client.invalidateQueries({ queryKey: ["backup-queue"] });
    }
  };
}

export function eligibilityText(eligibleAt: string, now: number) {
  const seconds = Math.ceil((new Date(eligibleAt).getTime() - now) / 1000);
  return seconds > 0 ? `Eligible in ${seconds}s` : "Ready · waiting for worker";
}
