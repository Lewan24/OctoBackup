import { cpSync, mkdirSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { randomBytes, randomUUID } from "node:crypto";
import { spawn } from "node:child_process";
const repo = process.env.OCTOBACKUP_REPO;
if (!repo) throw new Error("Run through npm run test:e2e.");
const port = Number(process.argv[2] ?? 5087);
if (![5087, 5088].includes(port))
  throw new Error("Use a reserved browser-test port (5087 or 5088).");
const webroot = join(repo, "Backend/src/Server.Api/wwwroot");
mkdirSync(webroot, { recursive: true });
cpSync(join(repo, "Frontend/dist"), webroot, { recursive: true, force: true });
const child = spawn(
  "dotnet",
  [
    "run",
    "--no-build",
    "--no-launch-profile",
    "--project",
    join(repo, "Backend/src/Server.Api"),
    "--urls",
    `http://127.0.0.1:${port}`,
  ],
  {
    stdio: "inherit",
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: "Development",
      Hosting__ServeFrontend: "true",
      OCTOBACKUP_DATA: join(tmpdir(), `octobackup-e2e-${randomUUID()}`),
      CRYPTO_KEY: randomBytes(32).toString("base64"),
      Bootstrap__AdminEmail: "admin@e2e.example",
      Bootstrap__AdminPassword: "E2eTestPassword123!",
      Security__AllowedDatabaseHosts__0: "db.test.internal",
    },
  },
);
process.on("SIGTERM", () => child.kill());
process.on("SIGINT", () => child.kill());
child.on("exit", (code) => {
  process.exitCode = code ?? 1;
});
