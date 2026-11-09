import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { randomBytes } from "node:crypto";
import {
  cpSync,
  chmodSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { createServer } from "node:net";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const repo = dirname(dirname(fileURLToPath(import.meta.url)));

test("initialization preserves secrets/settings and configures the actual socket group", async (t) => {
  const root = mkdtempSync(join(tmpdir(), "octobackup-init-"));
  const server = createServer();
  const socket = join(root, "docker.sock");
  await new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(socket, resolve);
  });
  t.after(async () => {
    await new Promise((resolve) => server.close(resolve));
    rmSync(root, { recursive: true, force: true });
  });
  mkdirSync(join(root, "scripts"));
  cpSync(join(repo, "scripts/dev.mjs"), join(root, "scripts/dev.mjs"));
  cpSync(join(repo, ".env.example"), join(root, ".env.example"));
  const init = () => {
    const result = spawnSync(
      process.execPath,
      [join(root, "scripts/dev.mjs"), "init"],
      {
        env: { ...process.env, RESTORE_DOCKER_SOCKET: socket },
        encoding: "utf8",
      },
    );
    assert.equal(result.status, 0, result.stderr);
    return result.stdout;
  };
  init();
  const generated = readFileSync(join(root, ".env"), "utf8");
  assert.match(
    generated,
    new RegExp(`^RESTORE_DOCKER_GID=${statSync(socket).gid}$`, "m"),
  );
  assert.ok((statSync(join(root, ".env")).mode & 0o077) === 0);
  const runtime = join(root, "config/appsettings.Runtime.json");
  assert.ok(
    statSync(runtime).mode & 0o004,
    "non-root container must read runtime settings",
  );
  writeFileSync(runtime, '{"RestoreTests":{"TimeoutSeconds":900}}\n');
  init();
  assert.equal(readFileSync(join(root, ".env"), "utf8"), generated);
  assert.equal(
    readFileSync(runtime, "utf8"),
    '{"RestoreTests":{"TimeoutSeconds":900}}\n',
  );

  // Upgrade an existing installation without replacing its credentials.
  const old = generated.replace(/^RESTORE_DOCKER_GID=.*\n/m, "");
  writeFileSync(join(root, ".env"), old);
  const output = init();
  assert.equal(
    readFileSync(join(root, ".env"), "utf8"),
    `${old.trimEnd()}\nRESTORE_DOCKER_GID=${statSync(socket).gid}\n`,
  );
  for (const key of ["CRYPTO_KEY", "ADMIN_PASSWORD"]) {
    const secret = generated.match(new RegExp(`^${key}=(.+)$`, "m"))[1];
    assert.ok(
      !output.includes(secret),
      "initialization must not print secrets",
    );
  }
});

test("production Compose requires socket permissions and includes image preparation before API startup", () => {
  const env = {
    ...process.env,
    CRYPTO_KEY: randomBytes(32).toString("base64"),
    ADMIN_EMAIL: "deployment@test.example",
    ADMIN_PASSWORD: randomBytes(24).toString("base64url"),
    RESTORE_DOCKER_GID: "1234",
    RESTORE_DOCKER_SOCKET: "/tmp/test-docker.sock",
  };
  const config = spawnSync(
    "docker",
    ["compose", "--env-file", "/dev/null", "config", "--format", "json"],
    {
      cwd: repo,
      env,
      encoding: "utf8",
    },
  );
  assert.equal(config.status, 0, config.stderr);
  const { services } = JSON.parse(config.stdout);
  assert.equal(services.api.environment.RestoreTests__Enabled, "true");
  assert.equal(
    services.api.depends_on["restore-images"].condition,
    "service_completed_successfully",
  );
  for (const service of [services.api, services["restore-images"]]) {
    assert.deepEqual(service.group_add, ["1234"]);
    assert.equal(
      service.environment.Configuration__ReloadFile,
      "/config/appsettings.Runtime.json",
    );
    const socket = service.volumes.find(
      (volume) => volume.target === "/var/run/docker.sock",
    );
    assert.equal(socket.source, "/tmp/test-docker.sock");
    assert.equal(socket.bind?.create_host_path ?? false, false);
    assert.equal(service.read_only, true);
    assert.deepEqual(service.cap_drop, ["ALL"]);
  }
  assert.equal(services["restore-images"].environment.CRYPTO_KEY, undefined);
  assert.ok(
    !services["restore-images"].volumes.some(
      (volume) => volume.target === "/data",
    ),
  );
  delete env.RESTORE_DOCKER_GID;
  const missing = spawnSync(
    "docker",
    ["compose", "--env-file", "/dev/null", "config", "--quiet"],
    {
      cwd: repo,
      env,
      encoding: "utf8",
    },
  );
  assert.notEqual(missing.status, 0);
  assert.match(missing.stderr, /RESTORE_DOCKER_GID/);
});

test(
  "restore image preparation merges configured versions, honors overrides and skips cached pulls",
  { skip: process.platform === "win32" },
  (t) => {
    const root = mkdtempSync(join(tmpdir(), "octobackup-images-"));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const settings = join(root, "settings.json");
    const runtime = join(root, "runtime.json");
    const calls = join(root, "calls");
    writeFileSync(
      settings,
      JSON.stringify({
        RestoreTests: {
          Enabled: true,
          MemoryMiB: 4096,
          PostgresImage: "cached:1",
          MySqlImage: "mysql:8.4",
          PostgresImages: { 16: "postgres:16" },
        },
      }),
    );
    writeFileSync(
      runtime,
      JSON.stringify({
        RestoreTests: {
          MySqlImage: "mysql:custom",
          PostgresImages: { 18: "postgres:18" },
        },
      }),
    );
    const docker = join(root, "docker");
    writeFileSync(
      docker,
      `#!/bin/sh
printf '%s\\n' "$*" >> "$DOCKER_CALLS"
case "$*" in
  info|"image inspect cached:1") exit 0 ;;
  "image inspect "*) exit 1 ;;
  "image pull fail:1") exit 17 ;;
  "image pull "*) exit 0 ;;
  *) exit 99 ;;
esac
`,
    );
    chmodSync(docker, 0o755);
    const env = {
      ...process.env,
      PATH: `${root}:${process.env.PATH}`,
      DOCKER_CALLS: calls,
      Configuration__ReloadFile: runtime,
      RestoreTests__PostgresImages__16: "postgres:override",
    };
    const run = () =>
      spawnSync(
        "sh",
        [join(repo, "Backend/docker/prepare-restore-images.sh"), settings],
        { env, encoding: "utf8" },
      );
    const result = run();
    assert.equal(result.status, 0, result.stderr);
    const commands = readFileSync(calls, "utf8").trim().split("\n");
    assert.deepEqual(
      commands.filter((command) => command.startsWith("image pull ")).sort(),
      [
        "image pull mysql:custom",
        "image pull postgres:18",
        "image pull postgres:override",
      ],
    );
    env.RestoreTests__MySqlImage = "fail:1";
    const failed = run();
    assert.equal(
      failed.status,
      17,
      "failed pulls must prevent successful preparation",
    );
    assert.ok(!failed.stdout.includes("Restore images are available"));
  },
);
