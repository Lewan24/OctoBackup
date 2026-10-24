import { expect, test } from "@playwright/test";

test("brand and account dialogs support keyboard focus and separate actions", async ({
  page,
}, testInfo) => {
  // UI-only fixture: the separate workspace workflow exercises real authentication.
  await page.route("**/api/**", (route) => {
    const path = new URL(route.request().url()).pathname;
    return route.fulfill({
      json: path.endsWith("GetCurrentUser")
        ? {
            isAuthenticated: true,
            userName: "operator@example.test",
            claims: {},
          }
        : {
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
  await page.goto("/account");
  await expect(
    page.getByRole("heading", { name: "Account settings" }),
  ).toBeVisible();
  if (testInfo.project.name === "mobile")
    await page.getByRole("button", { name: "Toggle navigation" }).click();
  const brand = page.getByRole("link", { name: "OctoBackup overview" });
  await expect(brand.locator("img")).toBeVisible();
  if (testInfo.project.name === "desktop") {
    await page.mouse.move(1000, 400);
    await expect(brand.locator(".brand-name")).toHaveCSS("opacity", "0");
    await brand.hover();
    await expect(brand.locator(".brand-name")).toHaveCSS("opacity", "1");
  }
  await brand.focus();
  await expect(brand.locator(".brand-name")).toHaveCSS("opacity", "1");
  await page
    .getByRole("link", { name: "Account settings", exact: true })
    .click();
  const changePassword = page.getByRole("button", {
    name: "Change password",
    exact: true,
  });
  await changePassword.click();
  const passwordDialog = page.getByRole("dialog", {
    name: "Change password",
    exact: true,
  });
  await expect(passwordDialog.getByLabel("Current password")).toBeVisible();
  await expect(
    passwordDialog.getByLabel("New password", { exact: true }),
  ).toBeVisible();
  // Native modal focus must remain inside the active dialog.
  for (let index = 0; index < 8; index++) {
    await page.keyboard.press("Tab");
    expect(
      await passwordDialog.evaluate((el) =>
        el.contains(document.activeElement),
      ),
    ).toBe(true);
  }
  await page.keyboard.press("Escape");
  await expect(passwordDialog).toHaveCount(0);
  await expect(changePassword).toBeFocused();
  await page.getByRole("button", { name: "Change email", exact: true }).click();
  const emailDialog = page.getByRole("dialog", {
    name: "Change email",
    exact: true,
  });
  await expect(emailDialog.getByLabel("New email address")).toBeVisible();
  await expect(
    emailDialog.getByLabel("New password", { exact: true }),
  ).toHaveCount(0);
  await page.screenshot({
    path: testInfo.outputPath("account-dialog.png"),
    fullPage: true,
  });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
  await emailDialog
    .getByRole("button", { name: "Cancel", exact: true })
    .click();
  await expect(emailDialog).toHaveCount(0);
});
