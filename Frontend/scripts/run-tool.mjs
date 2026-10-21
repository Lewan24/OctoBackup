import {
  cpSync,
  existsSync,
  mkdirSync,
  readFileSync,
  writeFileSync,
} from "node:fs";
import { createHash } from "node:crypto";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

// Vite treats # as a URL fragment, including when it appears in a Windows folder.
// Stage a copy in the OS temp directory; never rename the user's checkout.
const source = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const command = process.argv[2];
process.env.OCTOBACKUP_REPO = resolve(source, "..");
let root = source;
if (source.includes("#")) {
  const identity = createHash("sha256")
    .update(source)
    .digest("hex")
    .slice(0, 12);
  root = join(tmpdir(), `octobackup-frontend-${identity}`);
  mkdirSync(root, { recursive: true });
  const lock = readFileSync(join(source, "package-lock.json"), "utf8");
  const stamp = join(root, ".dependencies");
  if (!existsSync(stamp) || readFileSync(stamp, "utf8") !== lock) {
    cpSync(join(source, "node_modules"), join(root, "node_modules"), {
      recursive: true,
      force: true,
    });
    writeFileSync(stamp, lock);
  }
  for (const entry of [
    "src",
    "public",
    "e2e",
    "scripts",
    "package.json",
    "tsconfig.json",
    "index.html",
    "vite.config.ts",
    "playwright.config.ts",
  ]) {
    if (existsSync(join(source, entry)))
      cpSync(join(source, entry), join(root, entry), {
        recursive: true,
        force: true,
      });
  }
}
function run(binary, args) {
  const result = spawnSync(
    process.execPath,
    [join(root, "node_modules", binary), ...args],
    { cwd: root, stdio: "inherit", env: process.env },
  );
  if (result.error) throw result.error;
  return result.status ?? 1;
}
let status;
if (command === "build") {
  status = run("typescript/bin/tsc", ["-b"]);
  if (status === 0) status = run("vite/bin/vite.js", ["build"]);
  if (status === 0 && root !== source)
    cpSync(join(root, "dist"), join(source, "dist"), {
      recursive: true,
      force: true,
    });
} else if (command === "test") status = run("vitest/vitest.mjs", ["run"]);
else if (command === "e2e") {
  status = run("@playwright/test/cli.js", ["test"]);
  if (root !== source && existsSync(join(root, "test-results")))
    cpSync(join(root, "test-results"), join(source, "test-results"), {
      recursive: true,
      force: true,
    });
} else
  status = run("vite/bin/vite.js", [
    command === "preview" ? "preview" : "--host",
    ...(command === "preview" ? [] : ["127.0.0.1"]),
    ...process.argv.slice(3),
  ]);
process.exitCode = status;
