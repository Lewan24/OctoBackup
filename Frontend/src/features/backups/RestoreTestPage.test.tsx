import { afterEach, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Routes, Route } from "react-router-dom";
import { resetCsrf } from "../../shared/api";
import { RestoreTestPage } from "./RestoreTestPage";

const clients: QueryClient[] = [];
afterEach(() => {
  cleanup();
  clients.splice(0).forEach((c) => c.clear());
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  resetCsrf();
});
function mount() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  clients.push(client);
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={["/backups/backup-1/restore-test"]}>
        <Routes>
          <Route
            path="/backups/:id/restore-test"
            element={<RestoreTestPage />}
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

it.each(["null", ""])(
  "handles an untested backup response %j without undefined query data and can start a test",
  async (body) => {
    const errors = vi.spyOn(console, "error").mockImplementation(() => {});
    const fetch = vi.fn(async (url: string) => {
      if (url.endsWith("RestoreAvailability"))
        return new Response(JSON.stringify({ enabled: true }));
      if (url.includes("RestoreReport"))
        return new Response(body, { status: 200 });
      if (url.endsWith("/csrf"))
        return new Response(JSON.stringify({ token: "test-csrf" }));
      return new Response(
        JSON.stringify({
          id: "test-1",
          backupId: "backup-1",
          status: 2,
          automatic: false,
          testedOn: new Date().toISOString(),
          message: "The configured database image is not preloaded.",
        }),
        { status: 202 },
      );
    });
    vi.stubGlobal("fetch", fetch);
    mount();
    expect(
      await screen.findByRole("heading", { name: "No restore test recorded" }),
    ).toBeVisible();
    await userEvent.click(
      screen.getByRole("button", { name: "Test backup" }),
    );
    expect(
      await screen.findByText(
        "The configured database image is not preloaded.",
      ),
    ).toBeVisible();
    expect(errors.mock.calls.flat().join(" ")).not.toContain(
      "data is undefined",
    );
    expect(
      fetch.mock.calls.some(([path]) => path.endsWith("/TestBackup")),
    ).toBe(true);
  },
);

it("explains a disabled deployment and leaves saved failure reports visible", async () => {
  vi.stubGlobal(
    "fetch",
    vi.fn(
      async (url: string) =>
        new Response(
          JSON.stringify(
            url.endsWith("RestoreAvailability")
              ? { enabled: false }
              : {
                  id: "test-1",
                  backupId: "backup-1",
                  status: 2,
                  automatic: false,
                  testedOn: new Date().toISOString(),
                  message:
                    "Restore testing is not configured on this deployment.",
                },
          ),
        ),
    ),
  );
  mount();
  expect(
    await screen.findByText(
      "Restore testing is not configured on this deployment.",
    ),
  ).toBeVisible();
  expect(
    await screen.findByText(
      /An administrator must enable RestoreTests:Enabled/,
    ),
  ).toBeVisible();
  expect(
    screen.getByRole("button", { name: "Test backup" }),
  ).toBeDisabled();
});
