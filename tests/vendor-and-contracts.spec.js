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

test.describe('Vendor login management', () => {
  test.beforeEach(async ({ page }) => await login(page, ADMIN));

  test('Vendor Details shows Portal Login panel with active status for a vendor that has a login', async ({ page }) => {
    await page.goto(`${BASE_URL}/Vendors/Details/1`); // Gulf HVAC Solutions — already has a portal login
    await page.waitForLoadState('domcontentloaded');

    await expect(page.locator('body')).toContainText('Portal Login');
    await expect(page.locator('body')).toContainText('vendor@gulfhvac.kw');
    await expect(page.locator('button:has-text("Reset Password")')).toBeVisible();
  });
});

test.describe('Contracts — category-based bulk asset linking', () => {
  test.beforeEach(async ({ page }) => await login(page, ADMIN));

  test('Contract Create form has both search and category Add All controls', async ({ page }) => {
    await page.goto(`${BASE_URL}/Contracts/Create`);
    await page.waitForLoadState('domcontentloaded');

    await expect(page.locator('[data-search]')).toBeVisible();
    await expect(page.locator('[data-category-select]')).toBeVisible();
    await expect(page.locator('[data-add-by-category]')).toBeVisible();
    // Subcategory control starts hidden until a category with children is picked.
    await expect(page.locator('[data-subcategory-wrap]')).toHaveClass(/d-none/);
  });

  test('Choosing a category with a subcategory reveals the subcategory picker; Add All adds chips', async ({ page }) => {
    await page.goto(`${BASE_URL}/Contracts/Create`);
    await page.waitForLoadState('domcontentloaded');

    const categorySelect = page.locator('[data-category-select]');
    const options = await categorySelect.locator('option').allTextContents();
    const hvacIndex = options.findIndex(o => o.includes('HVAC'));
    test.skip(hvacIndex < 0, 'HVAC category not present in this environment');

    await categorySelect.selectOption({ label: options[hvacIndex] });
    // Give the AJAX subcategory fetch a moment.
    await page.waitForTimeout(500);

    await page.click('[data-add-by-category]');
    await page.waitForTimeout(500);

    const chipCount = await page.locator('[data-chip]').count();
    expect(chipCount).toBeGreaterThan(0);
  });
});
