import { afterEach, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ConfirmDialog } from "./ui";

afterEach(cleanup);

it("keeps a failed action open and lets the user retry", async () => {
  const user = userEvent.setup();
  const onConfirm = vi
    .fn()
    .mockRejectedValueOnce(new Error("Request failed"))
    .mockResolvedValueOnce(undefined);
  const onClose = vi.fn();
  render(
    <ConfirmDialog
      title="Remove production"
      label="Remove server"
      onConfirm={onConfirm}
      onClose={onClose}
    >
      <p>Existing backups remain.</p>
    </ConfirmDialog>,
  );
  expect(
    screen.getByRole("dialog", { name: "Remove production" }),
  ).toBeVisible();
  await user.click(screen.getByRole("button", { name: "Remove server" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("Request failed");
  expect(onClose).not.toHaveBeenCalled();
  await user.click(screen.getByRole("button", { name: "Remove server" }));
  await waitFor(() => expect(onClose).toHaveBeenCalledOnce());
});

it("does not dismiss or duplicate an action while it is pending", async () => {
  const user = userEvent.setup();
  let finish!: () => void;
  const onConfirm = vi.fn(
    () =>
      new Promise<void>((resolve) => {
        finish = resolve;
      }),
  );
  const onClose = vi.fn();
  render(
    <ConfirmDialog
      title="Queue database"
      onConfirm={onConfirm}
      onClose={onClose}
    >
      <p>Production database</p>
    </ConfirmDialog>,
  );
  await user.click(screen.getByRole("button", { name: "Confirm" }));
  await user.click(screen.getByRole("button", { name: "Close dialog" }));
  expect(onClose).not.toHaveBeenCalled();
  expect(screen.getByRole("button", { name: "Working…" })).toBeDisabled();
  finish();
  await waitFor(() => expect(onClose).toHaveBeenCalledOnce());
});
