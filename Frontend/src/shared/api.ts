export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}
let csrfToken: string | undefined;
export function resetCsrf() {
  csrfToken = undefined;
}
export async function api<T = void>(
  path: string,
  body?: unknown,
  signal?: AbortSignal,
): Promise<T> {
  const headers: Record<string, string> = {};
  if (body !== undefined) {
    if (!csrfToken) {
      const result = await fetch("/api/auth/csrf", {
        credentials: "same-origin",
        signal,
      });
      if (!result.ok)
        throw new ApiError(
          result.status,
          "Could not initialize a secure session. Refresh and try again.",
        );
      csrfToken = ((await result.json()) as { token: string }).token;
    }
    headers["Content-Type"] = "application/json";
    headers["X-CSRF-TOKEN"] = csrfToken!;
  }
  const response = await fetch(`/api/${path}`, {
    method: body === undefined ? "GET" : "POST",
    credentials: "same-origin",
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  });
  if (!response.ok) {
    const data: unknown = await response.json().catch(() => null);
    const message =
      typeof data === "string"
        ? data
        : data && typeof data === "object" && "title" in data
          ? String(data.title)
          : response.status === 401
            ? "Your session has ended. Sign in again."
            : response.status === 403
              ? "You do not have permission for this action."
              : response.status === 429
                ? "Too many requests. Please wait a minute and try again."
                : "The request failed. Please try again.";
    throw new ApiError(response.status, message);
  }
  return (
    response.status === 204 || response.headers.get("content-length") === "0"
      ? undefined
      : await response.text().then((t) => (t ? JSON.parse(t) : undefined))
  ) as T;
}
