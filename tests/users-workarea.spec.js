// @ts-check
// Verifies the Users page shows a real, membership-derived Work Area instead
// of the dead "— None —" dropdown.
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

test('Users page shows derived work area, no dropdown', async ({ page }) => {
  await login(page, ADMIN);
  await page.goto(`${BASE_URL}/Users`);
  await page.waitForLoadState('domcontentloaded');

  // No SetWorkArea dropdown should remain anywhere on the page
  const dropdownCount = await page.locator('select[name="workAreaId"]').count();
  expect(dropdownCount, 'Work Area dropdown must be fully removed').toBe(0);

  // The engineer row should show a real work area name, not "None" or "—"
  const engineerRow = page.locator('tr', { hasText: 'engineer@shiftflow.com' });
  await expect(engineerRow).toHaveCount(1);
  const rowText = await engineerRow.textContent();
  console.log('Engineer row text:', rowText?.replace(/\s+/g, ' ').trim());

  expect(rowText).not.toContain('None');
});
