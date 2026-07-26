// @ts-check
// Verifies the Users page filter bar: search by name/email, role filter, work area filter.
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

test.describe('Users page filters', () => {
  test.beforeEach(async ({ page }) => {
    await login(page, ADMIN);
    await page.goto(`${BASE_URL}/Users`);
    await page.waitForLoadState('domcontentloaded');
  });

  test('search by name filters to matching user', async ({ page }) => {
    await page.fill('input[name="search"]', 'Khalid');
    await page.click('button:has-text("Filter")');
    await page.waitForLoadState('domcontentloaded');

    const rows = page.locator('table tbody tr');
    await expect(rows).toHaveCount(1);
    await expect(rows.first()).toContainText('Khalid Al-Mutairi');
  });

  test('search by email filters to matching user', async ({ page }) => {
    await page.fill('input[name="search"]', 'engineer@shiftflow.com');
    await page.click('button:has-text("Filter")');
    await page.waitForLoadState('domcontentloaded');

    const rows = page.locator('table tbody tr');
    await expect(rows).toHaveCount(1);
    await expect(rows.first()).toContainText('engineer@shiftflow.com');
  });

  test('role filter narrows the list', async ({ page }) => {
    const totalRows = await page.locator('table tbody tr').count();

    await page.selectOption('select[name="role"]', 'Admin');
    await page.click('button:has-text("Filter")');
    await page.waitForLoadState('domcontentloaded');

    const rows = page.locator('table tbody tr');
    const count = await rows.count();
    console.log('Total rows:', totalRows, '| Rows with role=Admin:', count);
    expect(count).toBeGreaterThan(0);
    expect(count).toBeLessThan(totalRows);
    for (let i = 0; i < count; i++) {
      await expect(rows.nth(i)).toContainText('Admin');
    }
  });

  test('work area filter matches users in that work area and excludes others', async ({ page }) => {
    // "test 7" is known (from DB inspection) to have every current user's active
    // membership; "ECR" is an active work area with zero active memberships in this
    // dev DB. This checks both a real match and a genuine empty-result case.
    await page.selectOption('select[name="workArea"]', { label: 'test 7' });
    await page.click('button:has-text("Filter")');
    await page.waitForLoadState('domcontentloaded');

    const matchRows = page.locator('table tbody tr');
    const matchCount = await matchRows.count();
    console.log('Rows with workArea="test 7":', matchCount);
    expect(matchCount).toBeGreaterThan(0);
    for (let i = 0; i < matchCount; i++) {
      await expect(matchRows.nth(i)).toContainText('test 7');
    }

    await page.selectOption('select[name="workArea"]', { label: 'ECR' });
    await page.click('button:has-text("Filter")');
    await page.waitForLoadState('domcontentloaded');

    const emptyText = await page.locator('table tbody tr').first().textContent();
    console.log('Rows with workArea="ECR" (expected: no matches):', emptyText?.trim());
    expect(emptyText).toContain('No records found');
  });

  test('clear link resets all filters', async ({ page }) => {
    await page.fill('input[name="search"]', 'zzz-no-match');
    await page.click('button:has-text("Filter")');
    await page.waitForLoadState('domcontentloaded');

    const noMatchRows = await page.locator('table tbody tr').count();
    console.log('Rows for non-matching search:', noMatchRows);

    const clearLink = page.locator('a:has-text("Clear")');
    await expect(clearLink).toBeVisible();
    await clearLink.click();
    await page.waitForLoadState('domcontentloaded');

    expect(page.url()).not.toContain('search=');
    const searchInput = await page.locator('input[name="search"]').inputValue();
    expect(searchInput).toBe('');
  });
});
