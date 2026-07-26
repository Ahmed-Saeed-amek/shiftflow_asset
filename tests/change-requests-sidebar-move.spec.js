// @ts-check
// Verifies the Change Requests manager queue moved from being embedded in
// the internal shift pages (Today, Shift runbook) into the sidebar nav.
const { test, expect } = require('@playwright/test');

const BASE_URL = 'https://localhost:55248';
const ADMIN = { email: 'admin@shiftflow.com', password: 'Admin@123456' };

async function login(page, { email, password }) {
  await page.goto(`${BASE_URL}/Account/Login`);
  await page.fill('input[name="Email"]', email);
  await page.fill('input[name="Password"]', password);
  await page.click('form:has(input[name="Password"]) button[type="submit"]');
  await page.waitForLoadState('domcontentloaded');
}

test.describe('Change Requests moved to sidebar', () => {
  test.beforeEach(async ({ page }) => {
    await login(page, ADMIN);
  });

  test('sidebar has a Change Requests link to the manager queue', async ({ page }) => {
    // ChangeRequestsController.Index is known to be slow (~25s) on this dev DB;
    // give this check enough headroom independent of that pre-existing issue.
    test.setTimeout(60000);
    await page.goto(`${BASE_URL}/ShiftOps/Today`);
    await page.waitForLoadState('domcontentloaded');

    const navLink = page.locator('#sidebar a:has-text("Change Requests")');
    await expect(navLink).toBeVisible();
    await expect(navLink).toHaveAttribute('href', '/ChangeRequests');

    const res = await page.request.get(`${BASE_URL}/ChangeRequests`);
    expect(res.ok()).toBeTruthy();
  });

  test('Today page has exactly one Change Requests link, and it lives in the sidebar', async ({ page }) => {
    await page.goto(`${BASE_URL}/ShiftOps/Today`);
    await page.waitForLoadState('domcontentloaded');

    // Only the sidebar nav link should say "Change Requests" - the inline
    // header button that used to duplicate it must be gone.
    await expect(page.locator('a:has-text("Change Requests")')).toHaveCount(1);
    await expect(page.locator('#sidebar a:has-text("Change Requests")')).toHaveCount(1);
  });

  test('Shift runbook no longer has inline Change Requests buttons', async ({ page }) => {
    // Find any DailyGroupShift to open via the Today page's runbook links.
    await page.goto(`${BASE_URL}/ShiftOps/Today?showAll=true`);
    await page.waitForLoadState('domcontentloaded');
    const runbookLink = page.locator('a:has-text("Runbook")').first();
    await runbookLink.click();
    await page.waitForLoadState('domcontentloaded');

    // Only the sidebar's nav link should remain - none inline on the runbook page.
    await expect(page.locator('a:has-text("Change Requests")')).toHaveCount(1);
    await expect(page.locator('#sidebar a:has-text("Change Requests")')).toHaveCount(1);
    await expect(page.locator('a:has-text("View / Submit Change Requests")')).toHaveCount(0);

    // Temp Transfer button (a distinct feature) should still be present for managers.
    await expect(page.locator('a:has-text("Temp Transfer")')).toBeVisible();
  });
});
