import { test, expect } from "@playwright/test";

test("sign in, manage a connection and schedule, inspect history, and sign out", async ({
  page,
}, testInfo) => {
  const errors: string[] = [];
  page.on("console", (message) => {
    if (/violates.*Content Security Policy/.test(message.text()))
      errors.push("CSP violation");
  });
  page.on("pageerror", (error) => errors.push(error.message));
  await page.goto("/");
  await expect(
    page.getByRole("heading", { name: "Welcome back." }),
  ).toBeVisible();
  await page.screenshot({
    path: testInfo.outputPath("sign-in.png"),
    fullPage: true,
  });
  await page.getByLabel("Email address").fill("admin@e2e.example");
  await page
    .getByLabel("Password", { exact: true })
    .fill("E2eTestPassword123!");
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Backup overview" }),
  ).toBeVisible();
  if (testInfo.project.name === "mobile")
    await page.getByRole("button", { name: "Toggle navigation" }).click();
  await page
    .getByRole("link", { name: "Application settings", exact: true })
    .click();
  await expect(
    page.getByRole("heading", { name: "Application settings", exact: true }),
  ).toBeVisible();
  const approvedHost = `db.${testInfo.project.name}.internal`;
  const hosts = page.getByLabel("Allowed database hosts");
  const existingHosts = await hosts.inputValue();
  await hosts.fill(`${existingHosts}\n${approvedHost}`);
  await page.getByLabel("Default retention (days)").fill("9");
  await page.getByLabel("Schedule polling interval (seconds)").fill("60");
  await expect(
    page.getByLabel("Automatically restore-test successful backups"),
  ).toBeDisabled();
  await expect(
    page.getByText("Restore testing is disabled in this deployment.", {
      exact: false,
    }),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "Save settings", exact: true })
    .click();
  await expect(
    page.getByRole("status").filter({ hasText: "Settings saved." }),
  ).toBeVisible();
  await page.reload();
  await expect(hosts).toHaveValue(
    new RegExp(approvedHost.replace(/\./g, "\\.")),
  );
  await expect(page.getByLabel("Default retention (days)")).toHaveValue("9");
  await expect(
    page.getByLabel("Automatically restore-test successful backups"),
  ).not.toBeChecked();
  await page.screenshot({
    path: testInfo.outputPath("application-settings.png"),
    fullPage: true,
  });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
  await page.goto("/");
  await page.screenshot({
    path: testInfo.outputPath("dashboard.png"),
    fullPage: true,
  });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
  const name = `E2E ${testInfo.project.name}`;
  await page
    .getByRole("link", { name: "Connect a server", exact: true })
    .click();
  await page
    .getByRole("button", { name: "Connect a server", exact: true })
    .click();
  await page.getByLabel("Connection name").fill(name);
  await page.getByLabel("Database engine").selectOption("1");
  await page.getByLabel("Database host").fill(approvedHost);
  await page.getByLabel("Database name").fill("app");
  await page.getByLabel("Database user").fill("backup");
  await page
    .getByLabel("Database password", { exact: true })
    .fill("SecretDatabase123!");
  await page.screenshot({
    path: testInfo.outputPath("connection-form.png"),
    fullPage: true,
  });
  await page
    .getByRole("button", { name: "Connect server", exact: true })
    .click();
  await expect(page.getByRole("heading", { name, exact: true })).toBeVisible();
  const card = page
    .getByRole("article")
    .filter({ has: page.getByRole("heading", { name, exact: true }) });
  await card.getByRole("button", { name: `Edit ${name}`, exact: true }).click();
  await expect(page.getByLabel("New database password (optional)")).toHaveValue(
    "",
  );
  await page.getByRole("button", { name: "Cancel", exact: true }).click();
  await page.screenshot({
    path: testInfo.outputPath("connections.png"),
    fullPage: true,
  });
  await card.getByRole("button", { name: "Queue backup", exact: true }).click();
  await expect(
    card.getByRole("button", { name: "In queue", exact: true }),
  ).toBeDisabled();
  await expect(
    page.getByRole("heading", { name: "Backups are queued" }),
  ).toBeVisible();
  await page.getByRole("link", { name: "View queue", exact: true }).click();
  const queuedRow = page
    .getByRole("row")
    .filter({ has: page.getByText(name, { exact: true }) });
  await expect(queuedRow.getByText("Queued", { exact: true })).toBeVisible();
  await page.screenshot({
    path: testInfo.outputPath("backup-queue.png"),
    fullPage: true,
  });
  await page
    .getByRole("button", { name: `Cancel backup for ${name}`, exact: true })
    .click();
  await expect(queuedRow.getByText("Cancelled", { exact: true })).toBeVisible();
  await page
    .getByRole("button", { name: `Remove request for ${name}`, exact: true })
    .click();
  await expect(
    page.getByRole("heading", { name: "No queue requests" }),
  ).toBeVisible();
  await page.goto("/schedules");
  await page
    .getByRole("button", { name: "Create schedule", exact: true })
    .click();
  await page.getByLabel("Schedule name").fill(`${name} daily`);
  await page
    .getByLabel("Server", { exact: true })
    .selectOption({ label: name });
  await page
    .getByRole("button", { name: "Create schedule", exact: true })
    .last()
    .click();
  await expect(page.getByText(`${name} daily`, { exact: true })).toBeVisible();
  await page.screenshot({
    path: testInfo.outputPath("schedules.png"),
    fullPage: true,
  });
  await page.goto("/");
  await expect(
    page.getByRole("heading", { name: "Backup overview" }),
  ).toBeVisible();
  await expect(page.getByText(`${name} daily`, { exact: true })).toBeVisible();
  await expect(
    page.getByRole("status").filter({ hasText: "Live updates" }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Refresh overview" }).click();
  await expect(
    page.getByRole("button", { name: "Refresh overview" }),
  ).toBeEnabled();
  await page.screenshot({
    path: testInfo.outputPath("dashboard-configured.png"),
    fullPage: true,
  });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
  await page.emulateMedia({ reducedMotion: "reduce" });
  expect(
    await page
      .locator(".queue-symbol")
      .evaluate((element) => getComputedStyle(element).animationName),
  ).toBe("none");
  if (testInfo.project.name === "mobile") {
    const toggle = page.getByRole("button", { name: "Toggle navigation" });
    await toggle.click();
    await expect(toggle).toHaveAttribute("aria-expanded", "true");
    await page.keyboard.press("Escape");
    await expect(toggle).toHaveAttribute("aria-expanded", "false");
    await expect(toggle).toBeFocused();
  }
  await page.goto("/backups");
  await expect(
    page.getByRole("heading", { name: "No backup records found" }),
  ).toBeVisible();
  // UI fixture only: real queue/access/preview persistence are covered by API tests.
  let fixtureReport: object | null = null;
  await page.route("**/api/backups/RestoreAvailability", (route) =>
    route.fulfill({ json: { enabled: true } }),
  );
  const completedReport = {
    id: "fixture-test",
    backupId: "fixture-backup",
    status: 1,
    automatic: false,
    testedOn: new Date().toISOString(),
    finishedOn: new Date().toISOString(),
    preview: {
      tableCount: 2,
      catalog: [
        { schema: "public", name: "probe" },
        { schema: "public", name: "empty_probe" },
      ],
      notes: "Browser fixture: bounded restored sample.",
      tables: [
        {
          schema: "public",
          name: "probe",
          columns: ["id", "value", "nullable"],
          rows: [["1", "<script>sample</script>", null]],
        },
        { schema: "public", name: "empty_probe", columns: ["id"], rows: [] },
      ],
    },
  };
  await page.route("**/api/backups/RestoreReport?id=fixture-backup", (route) =>
    route.fulfill({ json: fixtureReport }),
  );
  await page.route("**/api/backups/TestBackup", async (route) => {
    expect(route.request().headers()["x-csrf-token"]).toBeTruthy();
    fixtureReport = completedReport;
    await route.fulfill({
      status: 202,
      json: { ...completedReport, status: 3, preview: null, finishedOn: null },
    });
  });
  await page.goto("/backups/fixture-backup/restore-test");
  await expect(
    page.getByRole("heading", { name: "No restore test recorded" }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Test backup", exact: true }).click();
  await expect(page.getByText("2 user tables restored")).toBeVisible();
  await expect(
    page.getByRole("cell", { name: "<script>sample</script>", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("cell", { name: "NULL", exact: true }),
  ).toBeVisible();
  await page.screenshot({
    path: testInfo.outputPath("restore-preview.png"),
    fullPage: true,
  });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
  await page.getByLabel("Sampled table").selectOption("1");
  await expect(
    page.getByText("This table is empty.", { exact: false }),
  ).toBeVisible();
  await page.unroute("**/api/backups/RestoreReport?id=fixture-backup");
  await page.unroute("**/api/backups/TestBackup");
  await page.unroute("**/api/backups/RestoreAvailability");
  await page.goto("/users");
  await expect(
    page.getByRole("heading", { name: "Users & access", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("cell", { name: "admin@e2e.example", exact: true }),
  ).toBeVisible();
  if (testInfo.project.name === "mobile")
    await page.getByRole("button", { name: "Toggle navigation" }).click();
  await page.getByRole("button", { name: "Sign out" }).click();
  await expect(
    page.getByRole("heading", { name: "Welcome back." }),
  ).toBeVisible();
  expect(errors).toEqual([]);
});
