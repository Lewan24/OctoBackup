import { useQuery } from "@tanstack/react-query";
import { api } from "./api";

export const restoreRuntimeHelp =
  "Restore testing is disabled in this deployment. An administrator must enable RestoreTests:Enabled and configure Docker access.";

export function useRestoreAvailability(backupId?: string) {
  return useQuery({
    refetchInterval: 5000,
    queryKey: ["restore-availability", backupId ?? "runtime"],
    queryFn: ({ signal }) =>
      api<{ enabled: boolean }>(
        `backups/RestoreAvailability${backupId ? `?id=${encodeURIComponent(backupId)}` : ""}`,
        undefined,
        signal,
      ),
  });
}
