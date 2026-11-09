import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ServerForm } from "./ServerForm";
import { api } from "../../shared/api";
import type { Server } from "../../shared/types";

vi.mock("../../shared/api", () => ({ api: vi.fn() }));
const clients: QueryClient[] = [];
beforeEach(() => {
  vi.mocked(api).mockReset();
  vi.mocked(api).mockImplementation(
    async (path) =>
      (path === "backup-encryption/availability"
        ? { globalEnabled: false }
        : path.startsWith("backup-encryption/servers/")
          ? {
              keyId: "individual-key",
              usesGlobalKey: false,
              isEncrypted: true,
              globalEnabled: false,
              revision: "policy-1",
            }
          : undefined) as never,
  );
});
afterEach(() => {
  cleanup();
  clients.splice(0).forEach((client) => client.clear());
});
const server: Server = {
  id: "server-1",
  connectionName: "Fixture",
  serverHost: "db.internal",
  serverPort: 3306,
  dbName: "fixture",
  dbUser: "backup",
  dbType: 0,
  ssL: true,
  isDisabled: false,
  isTunnelRequired: false,
};
function mount(existing?: Server) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  clients.push(client);
  render(
    <QueryClientProvider client={client}>
      <ServerForm
        server={existing}
        onSaved={async () => {}}
        onCancel={() => {}}
      />
    </QueryClientProvider>,
  );
}

it.each([false, true])(
  "creates a server with individual encryption %j while global encryption is disabled",
  async (individual) => {
    mount();
    const user = userEvent.setup();
    await waitFor(() =>
      expect(screen.getByLabelText("Backup encryption mode")).toBeEnabled(),
    );
    expect(
      screen.queryByLabelText("Enable global backup encryption"),
    ).not.toBeInTheDocument();
    await user.type(screen.getByLabelText("Connection name"), "Fixture");
    await user.type(screen.getByLabelText("Database host"), "db.internal");
    await user.type(screen.getByLabelText("Database name"), "fixture");
    await user.type(screen.getByLabelText("Database user"), "backup");
    await user.type(
      screen.getByLabelText("Database password", { exact: true }),
      "DatabaseFixture123!",
    );
    if (individual) {
      await user.selectOptions(
        screen.getByLabelText("Backup encryption mode"),
        "individual",
      );
      await user.type(
        screen.getByLabelText("Individual encryption password", {
          exact: true,
        }),
        "IndividualFixture123!",
      );
      await user.type(
        screen.getByLabelText("Confirm individual encryption password"),
        "IndividualFixture123!",
      );
    }
    await user.click(
      screen.getByRole("button", { name: "Connect server" }),
    );
    await waitFor(() =>
      expect(api).toHaveBeenCalledWith(
        "servers/CreateServer",
        expect.objectContaining({
          encryption: expect.objectContaining({
            mode: individual ? "individual" : "inherit",
            password: individual ? "IndividualFixture123!" : null,
          }),
        }),
      ),
    );
  },
);

it("preserves the current key on edit and requires its password when replacing it", async () => {
  mount(server);
  const user = userEvent.setup();
  await waitFor(() =>
    expect(screen.getByLabelText("Backup encryption mode")).toBeEnabled(),
  );
  expect(screen.getByLabelText("Backup encryption mode")).toHaveValue("keep");
  expect(
    screen.queryByLabelText("Individual encryption password", { exact: true }),
  ).not.toBeInTheDocument();
  await user.selectOptions(
    screen.getByLabelText("Backup encryption mode"),
    "individual",
  );
  expect(
    screen.getByLabelText("Current individual encryption password"),
  ).toBeRequired();
  await user.type(
    screen.getByLabelText("Current individual encryption password"),
    "OriginalFixture123!",
  );
  await user.type(
    screen.getByLabelText("Individual encryption password", { exact: true }),
    "ReplacementFixture123!",
  );
  await user.type(
    screen.getByLabelText("Confirm individual encryption password"),
    "ReplacementFixture123!",
  );
  await user.click(
    screen.getByRole("button", { name: "Save connection" }),
  );
  await waitFor(() =>
    expect(api).toHaveBeenCalledWith(
      "servers/EditServer",
      expect.objectContaining({
        dbPasswd: null,
        encryption: {
          mode: "individual",
          password: "ReplacementFixture123!",
          confirmPassword: "ReplacementFixture123!",
          currentPassword: "OriginalFixture123!",
          expectedKeyId: "individual-key",
        },
      }),
    ),
  );
});
