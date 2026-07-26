// @ts-check
// Verifies all UI entry points for the off-day-groups toggle are gone from
// the Live Shift Dashboard: the header button and the "Groups with no
// shifts are hidden. Include all groups" footer text/link.
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

test('Today page has no Include/Hide Off-Day Groups header button or footer link', async ({ page }) => {
  await login(page, ADMIN);
  await page.goto(`${BASE_URL}/ShiftOps/Today`);
  await page.waitForLoadState('domcontentloaded');

  await expect(page.locator('a:has-text("Include Off-Day Groups")')).toHaveCount(0);
  await expect(page.locator('a:has-text("Hide Off-Day Groups")')).toHaveCount(0);
  await expect(page.locator('a:has-text("Include all groups")')).toHaveCount(0);
  await expect(page.locator('text=Groups with no shifts are hidden')).toHaveCount(0);

  // The showAll query param still works via direct navigation, even with no UI entry point.
  await page.goto(`${BASE_URL}/ShiftOps/Today?showAll=true`);
  await page.waitForLoadState('domcontentloaded');
  expect(page.url()).toContain('showAll=true');
});
