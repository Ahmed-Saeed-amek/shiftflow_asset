// @ts-check
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

test.describe('Employee picker vendor exclusion', () => {
  test.beforeEach(async ({ page }) => await login(page, ADMIN));

  test('/api/users/search never returns a Vendor-role account', async ({ page, request }) => {
    const cookies = await page.context().cookies();
    const cookieHeader = cookies.map(c => `${c.name}=${c.value}`).join('; ');

    const resp = await request.get(`${BASE_URL}/api/users/search?q=Gulf`, { headers: { Cookie: cookieHeader } });
    expect(resp.ok()).toBeTruthy();
    const results = await resp.json();
    expect(results.length).toBe(0);

    const resp2 = await request.get(`${BASE_URL}/api/users/search?q=vendor`, { headers: { Cookie: cookieHeader } });
    const results2 = await resp2.json();
    expect(results2.find(r => (r.email || '').includes('vendor@'))).toBeUndefined();
  });

  test('/api/users/search still returns real employees', async ({ page, request }) => {
    const cookies = await page.context().cookies();
    const cookieHeader = cookies.map(c => `${c.name}=${c.value}`).join('; ');

    const resp = await request.get(`${BASE_URL}/api/users/search?q=Khalid`, { headers: { Cookie: cookieHeader } });
    const results = await resp.json();
    expect(results.length).toBeGreaterThan(0);
  });

  test('Asset Visibility Create form uses the search picker, not a plain dropdown', async ({ page }) => {
    await page.goto(`${BASE_URL}/UserAssetScopes/Create`);
    await page.waitForLoadState('domcontentloaded');
    expect(page.url()).not.toContain('AccessDenied');

    await expect(page.locator('[data-employee-picker]')).toBeVisible();
    await expect(page.locator('select[asp-for="UserId"]')).toHaveCount(0);
  });
});
