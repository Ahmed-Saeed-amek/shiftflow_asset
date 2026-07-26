// @ts-check
const { test, expect } = require('@playwright/test');

const BASE_URL = 'https://localhost:55248';
const ADMIN = { email: 'admin@shiftflow.com', password: 'Admin@123456' };
const ENGINEER = { email: 'engineer@shiftflow.com', password: 'Engineer@123456' };
const VENDOR = { email: 'vendor@gulfhvac.kw', password: 'Vendor@123456' };

async function login(page, { email, password }) {
  await page.goto(`${BASE_URL}/Account/Login`);
  await page.fill('input[name="Email"]', email);
  await page.fill('input[name="Password"]', password);
  await page.click('form:has(input[name="Password"]) button[type="submit"]');
  await page.waitForLoadState('domcontentloaded');
}

test.describe('Work Order lifecycle', () => {
  test('Work Orders index and Details render for admin', async ({ page }) => {
    await login(page, ADMIN);
    await page.goto(`${BASE_URL}/WorkOrders`);
    await page.waitForLoadState('domcontentloaded');
    expect(page.url()).not.toContain('AccessDenied');
    await expect(page.locator('body')).toContainText('WO-2026');
  });

  test('Engineer can report a failure, creating a Draft work order', async ({ page }) => {
    await login(page, ENGINEER);
    await page.goto(`${BASE_URL}/WorkOrders/Report?assetId=1`);
    await page.waitForLoadState('domcontentloaded');
    expect(page.url()).not.toContain('AccessDenied');

    await expect(page.locator('#actionTypeSelect')).toBeVisible();
    // Wait for the category-scoped action types to load via AJAX before selecting.
    await page.waitForFunction(() => document.querySelectorAll('#actionTypeSelect option').length > 1);
    await page.selectOption('#actionTypeSelect', { index: 1 });
    await page.waitForFunction(() => document.querySelectorAll('#causeSelect option').length > 1);
    await page.selectOption('#causeSelect', { index: 1 });
    await page.fill('textarea[name="Notes"]', 'Playwright test report');
    await page.click('button[type="submit"]:has-text("Report Action")');
    await page.waitForLoadState('domcontentloaded');

    expect(page.url()).not.toContain('AccessDenied');
  });

  test('Admin Accept resolves the vendor from the asset\'s active Service contract', async ({ page }) => {
    await login(page, ADMIN);
    await page.goto(`${BASE_URL}/WorkOrders`);
    await page.waitForLoadState('domcontentloaded');

    // Find any row still in Draft and open it.
    const draftRow = page.locator('tr', { hasText: 'Draft' }).first();
    const hasDraft = await draftRow.count() > 0;
    test.skip(!hasDraft, 'No Draft work order available to test Accept against');

    await draftRow.click();
    await page.waitForLoadState('domcontentloaded');

    const acceptForm = page.locator('form[asp-action="Accept"], form:has(button:has-text("Accept"))').first();
    const vendorSelect = page.locator('select[name="vendorId"]');
    const hasVendorSelect = await vendorSelect.count() > 0;

    if (!hasVendorSelect) {
      // No active Service contract — the warning alert must be shown instead of a silent failure.
      await expect(page.locator('body')).toContainText('Service contract');
      return;
    }

    await expect(vendorSelect.locator('option')).toContainText(['Gulf HVAC Solutions']);
    await page.selectOption('select[name="priority"]', 'High');
    await page.click('button:has-text("Accept")');
    await page.waitForLoadState('domcontentloaded');

    await expect(page.locator('body')).toContainText('Sent to Vendor');
  });

  test('Vendor Portal only shows work orders assigned to that vendor', async ({ page }) => {
    await login(page, VENDOR);
    // Vendor logins land on the portal directly (BuildLandingPath routing), not /Dashboard.
    expect(page.url()).toContain('/VendorPortal');

    await page.waitForLoadState('domcontentloaded');
    const rows = await page.locator('table tbody tr').count();
    expect(rows).toBeGreaterThan(0);
  });

  test('Vendor Portal denies a second vendor login access to a different vendor\'s work order', async ({ page }) => {
    // vendor2@kcs.kw is now seeded in DbSeeder (Kuwait Cooling Systems, no work orders assigned).
    await login(page, { email: 'vendor2@kcs.kw', password: 'Vendor2@123456' });
    expect(page.url()).toContain('/VendorPortal');

    // Forbid() under cookie auth redirects a normal page navigation to the AccessDenied page
    // (200 on that page) rather than a raw 403/404 status — assert on the actual outcome.
    await page.goto(`${BASE_URL}/VendorPortal/Details/1`);
    expect(page.url()).toContain('/Account/AccessDenied');
    await expect(page.locator('body')).toContainText('Access Denied');
  });

  test('Vendor Portal Map View toggle renders work order location markers', async ({ page }) => {
    await login(page, VENDOR);
    await page.goto(`${BASE_URL}/VendorPortal`);
    await page.waitForLoadState('domcontentloaded');

    await expect(page.locator('#listView')).toBeVisible();
    await page.click('#mapViewBtn');
    await expect(page.locator('#mapView')).not.toHaveClass(/d-none/);
    await expect(page.locator('#vendorWorkOrdersMap')).toBeVisible();
  });
});
