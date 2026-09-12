// @ts-check
// Verifies the Users page filter bar. Users/Index filters server-side via the
// query params `role`, `search` and `page`, so these tests assert on the URL and
// on generic table rows rather than on any specific row markup — keep selectors
// generic so a re-skin of Users/Index doesn't break them.
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

const rowsOf = (page) => page.locator('table tbody tr');

test.describe('Users page filters', () => {
  test.beforeEach(async ({ page }) => {
    await login(page, ADMIN);
    await page.goto(`${BASE_URL}/Users`);
    await page.waitForLoadState('domcontentloaded');
  });

  test('search by email narrows the list to the matching user', async ({ page }) => {
    const email = 'engineer@shiftflow.com';
    await page.fill('input[name="search"]', email);
    await page.click('button:has-text("Filter")');
    await page.waitForLoadState('domcontentloaded');

    expect(decodeURIComponent(page.url())).toContain(`search=${email}`);
    const rows = rowsOf(page);
    const count = await rows.count();
    expect(count).toBeGreaterThan(0);
    for (let i = 0; i < count; i++) {
      await expect(rows.nth(i)).toContainText(email);
    }
  });

  test('search with no matches returns an empty result set', async ({ page }) => {
    await page.fill('input[name="search"]', 'zzz-no-such-user');
    await page.click('button:has-text("Filter")');
    await page.waitForLoadState('domcontentloaded');

    // The redesigned list renders an empty-state card instead of a placeholder table row.
    const rowCount = await rowsOf(page).count();
    const body = (await page.locator('main, body').first().textContent() ?? '');
    expect(rowCount === 0 && /no users|no records|no results/i.test(body)).toBeTruthy();
  });

  test('role filter narrows the list and every row carries that role', async ({ page }) => {
    const totalRows = await rowsOf(page).count();

    await page.selectOption('select[name="role"]', 'Admin');
    await page.click('button:has-text("Filter")');
    await page.waitForLoadState('domcontentloaded');

    expect(page.url()).toContain('role=Admin');
    const rows = rowsOf(page);
    const count = await rows.count();
    expect(count).toBeGreaterThan(0);
    expect(count).toBeLessThanOrEqual(totalRows);
    for (let i = 0; i < count; i++) {
      await expect(rows.nth(i)).toContainText('Admin');
    }
  });

  test('clear link resets all filters', async ({ page }) => {
    await page.fill('input[name="search"]', 'zzz-no-match');
    await page.click('button:has-text("Filter")');
    await page.waitForLoadState('domcontentloaded');

    const clearLink = page.locator('a:has-text("Clear")').first();
    await expect(clearLink).toBeVisible();
    await clearLink.click();
    await page.waitForLoadState('domcontentloaded');

    expect(page.url()).not.toContain('search=');
    expect(page.url()).not.toContain('role=');
    const searchInput = await page.locator('input[name="search"]').inputValue();
    expect(searchInput).toBe('');
  });
});
