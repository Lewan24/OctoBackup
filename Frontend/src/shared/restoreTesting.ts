import { useQuery } from "@tanstack/react-query";
import { api } from "./api";

export const restoreRuntimeHelp =
  "Restore testing is disabled in this deployment. An administrator must enable RestoreTests:Enabled, configure Docker and restart the API.";

export function useRestoreAvailability() {
  return useQuery({
    queryKey: ["restore-availability"],
    queryFn: ({ signal }) =>
      api<{ enabled: boolean }>(
        "backups/RestoreAvailability",
        undefined,
        signal,
      ),
  });
}
