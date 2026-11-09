import {
  existsSync,
  mkdirSync,
  readFileSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { randomBytes } from "node:crypto";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { spawn, spawnSync } from "node:child_process";

const repo = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const envFile = join(repo, ".env");
const command = process.argv[2] ?? "all";
if (command === "init") {
  if (existsSync(envFile)) {
    console.log(
      ".env already exists; existing keys and passwords were preserved.",
    );
  } else {
    const password = () => `Aa1!${randomBytes(24).toString("base64url")}`;
    const secrets = {
      CRYPTO_KEY: randomBytes(32).toString("base64"),
      ADMIN_PASSWORD: password(),
      POSTGRES_PASSWORD: password(),
      MYSQL_PASSWORD: password(),
      MYSQL_ROOT_PASSWORD: password(),
      MSSQL_SA_PASSWORD: password(),
    };
    let template = readFileSync(join(repo, ".env.example"), "utf8");
    for (const [key, value] of Object.entries(secrets))
      template = template.replace(
        new RegExp(`^${key}=.*$`, "m"),
        `${key}=${value}`,
      );
    writeFileSync(envFile, template, { flag: "wx", mode: 0o600 });
    console.log(
      "Created private .env with random keys/passwords. Read ADMIN_EMAIL and ADMIN_PASSWORD there to sign in.",
    );
  }
  const configDirectory = join(repo, "config");
  mkdirSync(configDirectory, { recursive: true, mode: 0o755 });
  const runtimeFile = join(configDirectory, "appsettings.Runtime.json");
  if (!existsSync(runtimeFile))
    writeFileSync(runtimeFile, "{}\n", { flag: "wx", mode: 0o644 });

  // Add only the deployment socket group; never replace saved keys/passwords.
  const saved = readFileSync(envFile, "utf8");
  if (!/^RESTORE_DOCKER_GID=.+$/m.test(saved)) {
    const socket =
      process.env.RESTORE_DOCKER_SOCKET ||
      saved.match(/^RESTORE_DOCKER_SOCKET=(.+)$/m)?.[1]?.trim() ||
      "/var/run/docker.sock";
    try {
      const socketInfo = statSync(socket);
      if (!socketInfo.isSocket()) throw new Error("Not a Docker socket");
      const updated = /^RESTORE_DOCKER_GID=.*$/m.test(saved)
        ? saved.replace(
            /^RESTORE_DOCKER_GID=.*$/m,
            `RESTORE_DOCKER_GID=${socketInfo.gid}`,
          )
        : `${saved.trimEnd()}\nRESTORE_DOCKER_GID=${socketInfo.gid}\n`;
      writeFileSync(envFile, updated, { mode: 0o600 });
      console.log("Configured the Docker socket group for restore tests.");
    } catch {
      console.log(
        "Docker socket not found locally. Before production deployment, set RESTORE_DOCKER_GID in .env to its group ID on the Docker host.",
      );
    }
  }
  process.exit(0);
}
if (!["all", "api", "frontend"].includes(command))
  throw new Error("Usage: node scripts/dev.mjs [init|all|api|frontend]");
if (!existsSync(envFile))
  throw new Error("Run node scripts/dev.mjs init first.");
const settings = {};
for (const line of readFileSync(envFile, "utf8").split(/\r?\n/)) {
  const match = line.match(/^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*?)\s*$/);
  if (!match) continue;
  let value = match[2];
  if (
    (value.startsWith('"') && value.endsWith('"')) ||
    (value.startsWith("'") && value.endsWith("'"))
  )
    value = value.slice(1, -1);
  settings[match[1]] = value;
}
const env = { ...settings, ...process.env };
env.ASPNETCORE_ENVIRONMENT = "Development";
env.OCTOBACKUP_DATA ||= join(repo, "Backend/data");
env.Bootstrap__AdminEmail ||= env.ADMIN_EMAIL;
env.Bootstrap__AdminPassword ||= env.ADMIN_PASSWORD;
env.Security__AllowedDatabaseHosts__0 = "localhost";
env.Security__AllowedDatabaseHosts__1 = "127.0.0.1";
env.Security__AllowedDatabaseHosts__2 = "host.docker.internal";
if (env.DATABASE_HOST)
  env.Security__AllowedDatabaseHosts__3 = env.DATABASE_HOST;
const apiUrl = env.DEV_API_URL || "http://127.0.0.1:5080";
env.VITE_API_PROXY_TARGET = apiUrl;
env.ReverseProxy__Enabled = "false";
env.Hosting__ServeFrontend = "false";
if (
  !env.CRYPTO_KEY ||
  !env.Bootstrap__AdminEmail ||
  !env.Bootstrap__AdminPassword
)
  throw new Error(
    "Configure CRYPTO_KEY, ADMIN_EMAIL and ADMIN_PASSWORD in .env.",
  );
const children = [];
let stopping = false;
function stop(code = 0) {
  if (stopping) return;
  stopping = true;
  for (const child of children) {
    if (child.exitCode !== null || child.signalCode !== null || !child.pid)
      continue;
    if (process.platform === "win32")
      spawnSync("taskkill", ["/PID", String(child.pid), "/T", "/F"], {
        stdio: "ignore",
      });
    else {
      try {
        process.kill(-child.pid, "SIGTERM");
      } catch {
        /* already stopped */
      }
    }
  }
  process.exitCode = code;
}
function start(binary, args) {
  const child = spawn(binary, args, {
    cwd: repo,
    env,
    stdio: "inherit",
    detached: process.platform !== "win32",
  });
  children.push(child);
  child.on("error", (error) => {
    console.error(error.message);
    stop(1);
  });
  child.on("exit", (code) => {
    if (!stopping) stop(code ?? 1);
  });
}
process.on("SIGINT", () => stop());
process.on("SIGTERM", () => stop());
if (command !== "frontend")
  start("dotnet", [
    "run",
    "--no-launch-profile",
    "--project",
    join(repo, "Backend/src/Server.Api"),
    "--urls",
    apiUrl,
  ]);
if (command !== "api")
  start(process.execPath, [join(repo, "Frontend/scripts/run-tool.mjs"), "dev"]);
