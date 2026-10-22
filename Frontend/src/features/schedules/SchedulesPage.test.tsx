import { expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SchedulesPage } from "./SchedulesPage";
import { api } from "../../shared/api";
vi.mock("../../shared/api", () => ({ api: vi.fn() }));
it("requires a valid time and sends selected days and server when creating a schedule", async () => {
  vi.mocked(api).mockImplementation(
    async (path) =>
      (path === "schedules/GetMySchedules"
        ? []
        : path === "servers/GetMyServersForSchedule"
          ? [{ id: "server-1", name: "Production" }]
          : undefined) as never,
  );
  const user = userEvent.setup();
  render(
    <QueryClientProvider
      client={
        new QueryClient({ defaultOptions: { queries: { retry: false } } })
      }
    >
      <SchedulesPage />
    </QueryClientProvider>,
  );
  await user.click(screen.getByRole("button", { name: "Create schedule" }));
  await user.type(screen.getByLabelText("Schedule name"), "Daily");
  await user.selectOptions(await screen.findByLabelText("Server"), "server-1");
  const times = screen.getByLabelText(/Run times/);
  await user.clear(times);
  await user.type(times, "27:99");
  await user.click(
    screen.getAllByRole("button", { name: "Create schedule" }).at(-1)!,
  );
  expect(await screen.findByRole("alert")).toHaveTextContent("HH:MM");
  await user.clear(times);
  await user.type(times, "02:00, 14:30");
  await user.click(
    screen.getAllByRole("button", { name: "Create schedule" }).at(-1)!,
  );
  expect(api).toHaveBeenCalledWith(
    "schedules/CreateSchedule",
    expect.objectContaining({
      name: "Daily",
      dbConnectionId: "server-1",
      selectedDays: [1, 2, 3, 4, 5],
      selectedTimes: ["02:00:00", "14:30:00"],
    }),
  );
});
