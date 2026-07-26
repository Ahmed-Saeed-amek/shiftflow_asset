// @ts-check
// Verifies the Live Shift Dashboard's work-area filter survives day navigation.
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

test('work-area filter persists across next/prev day navigation', async ({ page }) => {
  await login(page, ADMIN);
  await page.goto(`${BASE_URL}/ShiftOps/Today`);
  await page.waitForLoadState('domcontentloaded');

  const toggles = page.locator('.area-toggle');
  const count = await toggles.count();
  test.skip(count < 2, 'Need at least one non-"All" work area toggle to test filtering');

  // Click the first real work-area toggle (index 1; index 0 is "All").
  const target = toggles.nth(1);
  const areaId = await target.getAttribute('data-area');
  await target.click();
  await expect(target).toHaveClass(/active/);

  // Next-day link should now carry the area param.
  const nextHref = await page.locator('#nextDayLink').getAttribute('href');
  expect(nextHref).toContain(`area=${areaId}`);

  await page.click('#nextDayLink');
  await page.waitForLoadState('domcontentloaded');

  // After navigating, the same toggle should still be active and sections
  // filtered - i.e. the filter was not reset to "All".
  const activeAfterNav = page.locator('.area-toggle.active');
  await expect(activeAfterNav).toHaveAttribute('data-area', areaId ?? '');
  expect(page.url()).toContain(`area=${areaId}`);

  // Navigate back a day - filter should still hold.
  await page.click('#prevDayLink');
  await page.waitForLoadState('domcontentloaded');
  const activeAfterBack = page.locator('.area-toggle.active');
  await expect(activeAfterBack).toHaveAttribute('data-area', areaId ?? '');
  expect(page.url()).toContain(`area=${areaId}`);
});
