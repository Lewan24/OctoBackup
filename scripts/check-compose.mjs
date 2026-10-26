import { spawnSync } from "node:child_process";
// Smoke test only for the disposable project started with `-p octobackup-check`.
import { readFileSync, mkdirSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
const repo = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const require = createRequire(join(repo, "Frontend/package.json"));
const { chromium } = require("playwright");
function option(name, fallback) {
  const index = process.argv.indexOf(name);
  if (index < 0) return fallback;
  const value = process.argv[index + 1];
  if (!value || value.startsWith("--")) throw new Error(`${name} requires a value.`);
  return value;
}
const envFile = resolve(option("--env-file", join(repo, ".env")));
const baseURL = option("--base-url", "https://localhost:18443");
if (new URL(baseURL).protocol !== "https:")
  throw new Error("Use an HTTPS test reverse proxy; production cookies are Secure.");
const settings = Object.fromEntries(
  readFileSync(envFile, "utf8")
    .split(/\r?\n/)
    .filter((l) => /^[A-Z_]+=/.test(l))
    .map((l) => {
      const i = l.indexOf("=");
      return [l.slice(0, i), l.slice(i + 1)];
    }),
);
function docker(args, input) {
  const result = spawnSync(
    "docker",
    [
      "compose",
      "--env-file",
      envFile,
      "-p",
      "octobackup-check",
      "-f",
      "compose.yaml",
      "-f",
      "compose.dbs.yaml",
      ...args,
    ],
    { cwd: repo, input, encoding: "utf8" },
  );
  if (result.status !== 0)
    throw new Error(result.stderr || "Compose test command failed.");
}
docker([
  "exec",
  "-T",
  "mysql",
  "sh",
  "/docker-entrypoint-initdb.d/20-backup-user.sh",
]);
docker(
  [
    "exec",
    "-T",
    "postgres",
    "sh",
    "-c",
    'exec psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"',
  ],
  "CREATE TABLE IF NOT EXISTS compose_probe (id integer PRIMARY KEY, value text); INSERT INTO compose_probe VALUES (1, 'verified') ON CONFLICT DO NOTHING;\n",
);
docker(
  [
    "exec",
    "-T",
    "mysql",
    "sh",
    "-c",
    'MYSQL_PWD="$MYSQL_PASSWORD" exec mysql -u "$MYSQL_USER" "$MYSQL_DATABASE"',
  ],
  "CREATE TABLE IF NOT EXISTS compose_probe (id integer PRIMARY KEY, value text); INSERT IGNORE INTO compose_probe VALUES (1, 'verified');\n",
);
const browser = await chromium.launch();
try {
  const context = await browser.newContext({
    ignoreHTTPSErrors: process.argv.includes("--ignore-https-errors"),
    baseURL,
  });
  const page = await context.newPage();
  let hubSocket;
  page.on("websocket", (socket) => {
    if (new URL(socket.url()).pathname === "/backuphub") hubSocket = socket;
  });
  await page.goto("/");
  await page.getByLabel("Email address").fill(settings.ADMIN_EMAIL);
  await page
    .getByLabel("Password", { exact: true })
    .fill(settings.ADMIN_PASSWORD);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await page.getByRole("heading", { name: "Backup overview" }).waitFor();
  await page.getByRole("status").filter({ hasText: "Live updates" }).waitFor();
  if (!hubSocket || new URL(hubSocket.url()).protocol !== "wss:")
    throw new Error("Authenticated SignalR WebSocket did not connect through the edge.");
  const cookies = await context.cookies();
  if (
    !cookies.some(
      (c) => c.name === "OctoBackup.Session" && c.httpOnly && c.secure,
    )
  )
    throw new Error("Secure session cookie missing.");
  async function post(path, data) {
    const csrf = await (await context.request.get("/api/auth/csrf")).json();
    const response = await context.request.post(`/api/${path}`, {
      data: JSON.stringify(data),
      headers: {
        "Content-Type": "application/json",
        "X-CSRF-TOKEN": csrf.token,
      },
      timeout: 120000,
    });
    if (!response.ok())
      throw new Error(
        `${path} returned ${response.status()}: ${await response.text()}`,
      );
    return response;
  }
  if (
    (await context.request.post("/api/auth/Logout", { data: {} })).status() !==
    400
  )
    throw new Error("CSRF enforcement failed.");
  for (const [type, host, port, database, user, password] of [
    [
      1,
      "postgres",
      5432,
      settings.POSTGRES_DATABASE,
      settings.POSTGRES_USER,
      settings.POSTGRES_PASSWORD,
    ],
    [
      0,
      "mysql",
      3306,
      settings.MYSQL_DATABASE,
      settings.MYSQL_USER,
      settings.MYSQL_PASSWORD,
    ],
  ]) {
    const existing = await (
      await context.request.get("/api/servers/GetMyServers")
    ).json();
    if (existing.some((s) => s.connectionName === `Compose ${host}`)) continue;
    await post("servers/CreateServer", {
      connectionName: `Compose ${host}`,
      serverHost: host,
      serverPort: port,
      dbName: database,
      dbType: type,
      dbUser: user,
      dbPasswd: password,
      ssL: false,
    });
  }
  const servers = await (
    await context.request.get("/api/servers/GetMyServers")
  ).json();
  for (const server of servers.filter((s) =>
    s.connectionName.startsWith("Compose "),
  )) {
    await post("servers/TestServerConnection", server.id);
    console.log(`${server.connectionName}: connection OK`);
    if (process.argv.includes("--backup")) {
      const queued = await (
        await post("backups/PerformBackup", server.id)
      ).json();
      const deadline = Date.now() + 120000;
      let finished = false;
      while (Date.now() < deadline) {
        const snapshot = await (
          await context.request.get("/api/backup-queue")
        ).json();
        const request = snapshot.requests.find((item) => item.id === queued.id);
        if (request?.status === "Succeeded") {
          finished = true;
          break;
        }
        if (
          request &&
          ["Failed", "Cancelled", "Interrupted"].includes(request.status)
        )
          throw new Error(request.message || "Queued backup failed.");
        await new Promise((resolve) => setTimeout(resolve, 2000));
      }
      if (!finished) throw new Error("Queued export timed out.");
      console.log(`${server.connectionName}: export OK`);
    }
  }
  if (process.argv.includes("--backup")) {
    const history = await (
      await context.request.get("/api/backups/GetAllBackups")
    ).json();
    for (const backup of history) {
      const download = await context.request.get(
        `/api/backups/Download?id=${backup.id}`,
      );
      if (!download.ok()) throw new Error("Backup download failed.");
      const content = await download.body();
      if (content[0] !== 0x50 || content[1] !== 0x4b)
        throw new Error("Expected ZIP archive.");
      const folder = join(repo, "Backend/artifacts/compose-check");
      mkdirSync(folder, { recursive: true });
      writeFileSync(join(folder, `${backup.id}.zip`), content);
    }
  }
  const negotiate = await context.request.post(
    "/backuphub/negotiate?negotiateVersion=1",
    { data: {} },
  );
  if (!negotiate.ok())
    throw new Error("Authenticated SignalR negotiation failed.");
  console.log(
    "HTTPS frontend, API login, Secure/HttpOnly cookie, CSRF and SignalR WebSocket: OK",
  );
} finally {
  await browser.close();
}
