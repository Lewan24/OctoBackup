import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { api } from "../../shared/api";
import { QueueHero } from "./QueueHero";
import type { BackupQueue, BackupRequest } from "./useBackupQueue";

vi.mock("../../shared/api", () => ({ api: vi.fn() }));
const request: BackupRequest = {
  id: "request-1",
  serverId: "server-1",
  serverName: "Operations database",
  databaseName: "app",
  source: "Manual",
  requestedBy: "You",
  status: "Queued",
  createdAt: new Date().toISOString(),
  eligibleAt: new Date(Date.now() + 20000).toISOString(),
  startedAt: null,
  finishedAt: null,
  message: null,
  backupId: null,
  canCancel: true,
  canDelete: true,
};
function snapshot(): BackupQueue {
  return {
    requests: [request],
    totalCount: 1,
    activeServerIds: [request.serverId],
    queuedCount: 1,
    runningCount: 0,
    workerBusy: false,
    runningRequest: null,
    nextRequest: request,
    cancellationDelaySeconds: 20,
    spacingSeconds: 5,
    serverTimeUtc: new Date().toISOString(),
  };
}
const clients: QueryClient[] = [];
afterEach(() => {
  cleanup();
  clients.splice(0).forEach((client) => client.clear());
});
function mount() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, throwOnError: false } },
  });
  clients.push(client);
  render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <QueueHero />
      </QueryClientProvider>
    </MemoryRouter>,
  );
}
beforeEach(() => {
  vi.mocked(api).mockReset();
});

it("shows a saved queued request and cancels it through the protected action", async () => {
  let current = snapshot();
  vi.mocked(api).mockImplementation(async (path) => {
    if (path === "backup-queue/Cancel") {
      current = {
        ...current,
        requests: [],
        activeServerIds: [],
        queuedCount: 0,
        nextRequest: null,
      };
      return undefined as never;
    }
    return current as never;
  });
  mount();
  await screen.findByRole("heading", { name: "Backups are queued" });
  expect(screen.getByText("Operations database / app")).toBeInTheDocument();
  await userEvent
    .setup()
    .click(screen.getByRole("button", { name: "Cancel next request" }));
  expect(api).not.toHaveBeenCalledWith("backup-queue/Cancel", "request-1");
  await userEvent.click(
    screen.getByRole("button", { name: /^Cancel backup$/ }),
  );
  expect(api).toHaveBeenCalledWith("backup-queue/Cancel", "request-1");
  await screen.findByRole("heading", { name: "Backup queue is clear" });
});

it("reports another active worker without revealing an inaccessible request", async () => {
  vi.mocked(api).mockResolvedValue({
    ...snapshot(),
    requests: [],
    activeServerIds: [],
    queuedCount: 0,
    runningCount: 0,
    workerBusy: true,
    nextRequest: null,
    runningRequest: null,
  } as never);
  mount();
  await screen.findByRole("heading", { name: "Backup worker is running" });
  expect(
    screen.getByText("Another authorized request is using the worker."),
  ).toBeInTheDocument();
  expect(
    screen.queryByText("Operations database / app"),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "Cancel next request" }),
  ).not.toBeInTheDocument();
});

it("shows status failure instead of claiming the queue is empty", async () => {
  vi.mocked(api).mockRejectedValue(new Error("Queue could not be loaded."));
  mount();
  await waitFor(() =>
    expect(
      screen.getByRole("heading", { name: "Queue status unavailable" }),
    ).toBeInTheDocument(),
  );
  expect(screen.getByRole("alert")).toHaveTextContent(
    "Queue could not be loaded.",
  );
  expect(
    screen.queryByRole("heading", { name: "Backup queue is clear" }),
  ).not.toBeInTheDocument();
});
