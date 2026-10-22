import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { api, ApiError, resetCsrf } from "./api";
beforeEach(() => resetCsrf());
afterEach(() => vi.unstubAllGlobals());
it("fetches a CSRF token and sends it with mutations without storing credentials", async () => {
  const fetch = vi
    .fn()
    .mockResolvedValueOnce(
      new Response(JSON.stringify({ token: "csrf-token" })),
    )
    .mockResolvedValueOnce(new Response(null, { status: 204 }));
  vi.stubGlobal("fetch", fetch);
  await api("auth/Login", {
    email: "user@example.com",
    password: "never-store",
  });
  expect(fetch.mock.calls[0][0]).toBe("/api/auth/csrf");
  expect(fetch.mock.calls[1][1]).toMatchObject({
    method: "POST",
    credentials: "same-origin",
    headers: { "X-CSRF-TOKEN": "csrf-token" },
  });
  expect(localStorage.length).toBe(0);
});
it("surfaces permission errors without treating the request as success", async () => {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockResolvedValue(new Response("", { status: 403 })),
  );
  await expect(api("servers/GetMyServers")).rejects.toEqual(
    new ApiError(403, "You do not have permission for this action."),
  );
});
it("does not submit a mutation if CSRF initialization fails", async () => {
  const fetch = vi.fn().mockResolvedValue(new Response("", { status: 500 }));
  vi.stubGlobal("fetch", fetch);
  await expect(api("schedules/CreateSchedule", {})).rejects.toThrow(
    "Could not initialize",
  );
  expect(fetch).toHaveBeenCalledTimes(1);
});
