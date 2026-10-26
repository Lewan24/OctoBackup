import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { api, ApiError, resetCsrf } from "./api";
beforeEach(() => {
  resetCsrf();
  vi.stubGlobal("localStorage", {
    setItem: vi.fn(),
    getItem: vi.fn(),
    removeItem: vi.fn(),
    clear: vi.fn(),
  });
});
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
  expect(localStorage.setItem).not.toHaveBeenCalled();
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

it("explains a rejected login instead of reporting an expired session", async () => {
  vi.stubGlobal(
    "fetch",
    vi
      .fn()
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ token: "csrf-token" })),
      )
      .mockResolvedValueOnce(new Response(null, { status: 401 })),
  );
  await expect(
    api("auth/Login", {
      email: "user@example.com",
      password: "wrong-password",
    }),
  ).rejects.toEqual(
    new ApiError(
      401,
      "Sign-in failed. Check your email and password, or ask an administrator to check account access.",
    ),
  );
});

it("still reports an expired session for protected requests", async () => {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockResolvedValue(new Response(null, { status: 401 })),
  );
  await expect(api("servers/GetMyServers")).rejects.toEqual(
    new ApiError(401, "Your session has ended. Sign in again."),
  );
});

it("returns a binary response through the same CSRF-protected transport", async () => {
  const fetch = vi
    .fn()
    .mockResolvedValueOnce(
      new Response(JSON.stringify({ token: "csrf-token" })),
    )
    .mockResolvedValueOnce(
      new Response("encrypted archive", {
        headers: { "Content-Type": "application/octet-stream" },
      }),
    );
  vi.stubGlobal("fetch", fetch);
  const response = await api<Response>(
    "backups/DownloadDecrypted?id=fixture",
    { password: "private-password" },
    undefined,
    "response",
  );
  expect(await response.text()).toBe("encrypted archive");
  expect(fetch.mock.calls[1][1]).toMatchObject({
    method: "POST",
    headers: { "X-CSRF-TOKEN": "csrf-token" },
    body: JSON.stringify({ password: "private-password" }),
  });
  expect(localStorage.setItem).not.toHaveBeenCalled();
});
