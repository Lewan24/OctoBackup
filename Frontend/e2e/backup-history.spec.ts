import { readFileSync } from "node:fs";
import { join } from "node:path";
import { expect, test } from "@playwright/test";

test("backup verification labels and download actions stay separate", async ({
  page,
}, testInfo) => {
  // UI fixtures only; the workspace workflow separately exercises real API access.
  const backups = [
    {
      id: "encrypted",
      name: "Encrypted production",
      isEncrypted: true,
      test: { status: 1 },
    },
    {
      id: "failed",
      name: "Failed restore",
      isEncrypted: true,
      test: { status: 2 },
    },
    { id: "plain", name: "Plain archive", isEncrypted: false },
    {
      id: "queued",
      name: "Queued restore",
      isEncrypted: true,
      test: { status: 3 },
    },
    {
      id: "expired",
      name: "Expired archive",
      isEncrypted: true,
      isDisabled: true,
    },
    { id: "running", name: "Running restore", test: { status: 0 } },
    { id: "interrupted", name: "Interrupted restore", test: { status: 4 } },
    { id: "skipped", name: "Skipped restore", test: { status: 5 } },
    { id: "unknown", name: "Unknown restore", test: { status: 99 } },
  ].map((b) => ({
    createdOn: "2026-10-24T12:00:00Z",
    isDisabled: false,
    ...b,
  }));
  let decryptedRequests = 0;
  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith("GetCurrentUser"))
      return route.fulfill({
        json: {
          isAuthenticated: true,
          userName: "operator@example.test",
          claims: {},
        },
      });
    if (url.pathname.endsWith("GetAllBackups"))
      return route.fulfill({ json: backups });
    if (url.pathname.endsWith("/csrf"))
      return route.fulfill({ json: { token: "fixture-csrf" } });
    if (url.pathname.endsWith("DownloadDecrypted")) {
      decryptedRequests++;
      expect(url.searchParams.get("id")).toBe("encrypted");
      expect(route.request().method()).toBe("POST");
      expect(route.request().headers()["x-csrf-token"]).toBe("fixture-csrf");
      if (
        route.request().postDataJSON().password !== "CorrectArchivePassword123!"
      )
        return route.fulfill({
          status: 400,
          json: { title: "Incorrect encryption password." },
        });
      return route.fulfill({
        contentType: "application/octet-stream",
        headers: {
          "content-disposition": 'attachment; filename="decrypted.zip"',
        },
        body: "fixture decrypted archive",
      });
    }
    if (url.pathname.endsWith("/Download"))
      return route.fulfill({
        contentType: "application/octet-stream",
        headers: {
          "content-disposition": 'attachment; filename="encrypted.backup"',
        },
        body: "fixture encrypted archive",
      });
    return route.fulfill({
      json: {
        requests: [],
        totalCount: 0,
        activeServerIds: [],
        queuedCount: 0,
        runningCount: 0,
        workerBusy: false,
        runningRequest: null,
        nextRequest: null,
        cancellationDelaySeconds: 20,
        spacingSeconds: 5,
        serverTimeUtc: new Date().toISOString(),
      },
    });
  });
  await page.route("**/backuphub/**", (route) =>
    route.fulfill({ status: 503, body: "UI fixture" }),
  );
  // Serve the fixture document without spending the real API's per-IP budget.
  await page.route("**/backups", (route) =>
    route.fulfill({
      contentType: "text/html",
      body: readFileSync(
        join(process.env.OCTOBACKUP_REPO!, "Frontend/dist/index.html"),
        "utf8",
      ),
      headers: {
        "Content-Security-Policy":
          "default-src 'self'; script-src 'self'; style-src 'self'; font-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'",
      },
    }),
  );
  await page.goto("/backups");
  await expect(
    page.getByRole("columnheader", { name: "Actions", exact: true }),
  ).toBeVisible();
  const row = page.getByRole("row").filter({ hasText: "Encrypted production" });
  const cells = row.getByRole("cell");
  await expect(cells.nth(3)).toHaveText("Restore passed");
  await expect(cells.nth(3).getByRole("link")).toHaveCount(0);
  await expect(
    cells.nth(4).getByRole("link", { name: "Test / preview" }),
  ).toHaveAttribute("href", "/backups/encrypted/restore-test");
  for (const [name, label] of [
    ["Failed restore", "Failed"],
    ["Plain archive", "Not tested"],
    ["Queued restore", "Queued"],
    ["Running restore", "Running"],
    ["Interrupted restore", "Interrupted"],
    ["Skipped restore", "Skipped"],
    ["Unknown restore", "Unknown"],
  ]) {
    await expect(
      page.getByRole("row").filter({ hasText: name }).getByRole("cell").nth(3),
    ).toHaveText(label);
  }
  const trigger = row.getByRole("button", {
    name: "Download Encrypted production",
    exact: true,
  });
  await trigger.click();
  const menu = page.getByRole("menu", {
    name: "Download options for Encrypted production",
  });
  await expect(menu).toBeVisible();
  await expect(menu.getByRole("menuitem")).toHaveCount(2);
  await expect(
    menu.getByRole("menuitem", { name: "Download encrypted" }),
  ).toBeFocused();
  const bounds = await menu.boundingBox();
  expect(bounds!.x).toBeGreaterThanOrEqual(0);
  expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(
    page.viewportSize()!.width,
  );
  await page.screenshot({
    path: testInfo.outputPath("backup-download-menu.png"),
    fullPage: true,
  });
  await page.keyboard.press("Escape");
  await expect(menu).toBeHidden();
  await expect(trigger).toBeFocused();
  await trigger.press("ArrowDown");
  await page.keyboard.press("ArrowDown");
  await expect(
    menu.getByRole("menuitem", { name: "Download decrypted" }),
  ).toBeFocused();
  await page.keyboard.press("Enter");
  const dialog = page.getByRole("dialog", {
    name: "Download Encrypted production",
  });
  await expect(dialog).toBeVisible();
  expect(decryptedRequests).toBe(0);
  await dialog
    .getByLabel("Backup encryption password / key")
    .fill("wrong-password");
  await dialog.getByRole("button", { name: "Decrypt and download" }).click();
  await expect(dialog.getByRole("alert")).toContainText(
    "Incorrect encryption password",
  );
  await dialog
    .getByLabel("Backup encryption password / key")
    .fill("CorrectArchivePassword123!");
  const download = page.waitForEvent("download");
  await dialog.getByRole("button", { name: "Decrypt and download" }).click();
  expect((await download).suggestedFilename()).toBe("decrypted.zip");
  await expect(dialog).toHaveCount(0);
  await trigger.click();
  await expect(
    menu.getByRole("menuitem", { name: "Download encrypted" }),
  ).toHaveAttribute("href", "/api/backups/Download?id=encrypted");
  await expect(
    menu.getByRole("menuitem", { name: "Download encrypted" }),
  ).toHaveAttribute("download", "");
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await page
    .getByRole("button", { name: "Download Plain archive", exact: true })
    .click();
  const plainMenu = page.getByRole("menu", {
    name: "Download options for Plain archive",
  });
  await expect(
    plainMenu.getByRole("menuitem", { name: "Download unencrypted" }),
  ).toBeVisible();
  await expect(plainMenu.getByRole("menuitem")).toHaveCount(1);
  await page.keyboard.press("Escape");
  await expect(
    page
      .getByRole("row")
      .filter({ hasText: "Expired archive" })
      .getByRole("button"),
  ).toHaveCount(0);
  await page.getByLabel("Search backups").click();
  await expect(page.getByRole("menu")).toHaveCount(0);
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
});
