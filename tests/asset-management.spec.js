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

test.describe('Asset Management', () => {
  test.beforeEach(async ({ page }) => {
    await login(page, ADMIN);
  });

  test('Assets index lists seeded assets across categories', async ({ page }) => {
    await page.goto(`${BASE_URL}/Assets`);
    await page.waitForLoadState('domcontentloaded');
    expect(page.url()).not.toContain('AccessDenied');

    const rows = await page.locator('table tbody tr').count();
    expect(rows).toBeGreaterThan(1);
    await expect(page.locator('body')).toContainText('AST-0001');
  });

  test('Category filter shows a directly selectable bold parent, no dead optgroup label', async ({ page }) => {
    await page.goto(`${BASE_URL}/Assets`);
    await page.waitForLoadState('domcontentloaded');

    const categorySelect = page.locator('select[name="categoryId"]');
    await expect(categorySelect).toBeVisible();

    // The redesigned picker uses plain bold <option> elements, not <optgroup> (whose label
    // can never be selected natively) — assert there's no optgroup at all in this specific select.
    const optgroupCount = await categorySelect.locator('optgroup').count();
    expect(optgroupCount).toBe(0);

    // The top-level "HVAC" option itself must be selectable (has a real value).
    const hvacOption = categorySelect.locator('option', { hasText: 'HVAC' }).first();
    await expect(hvacOption).toBeAttached();
    const hvacValue = await hvacOption.getAttribute('value');
    expect(hvacValue).toBeTruthy();

    // The select's onchange="this.form.submit()" triggers a real navigation — wait for the
    // URL itself to change rather than racing waitForLoadState against the submit.
    await Promise.all([
      page.waitForURL(new RegExp(`categoryId=${hvacValue}`), { timeout: 10000 }),
      categorySelect.selectOption(hvacValue),
    ]);
    expect(page.url()).toContain(`categoryId=${hvacValue}`);
  });

  test('Zone filter still groups by governorate via optgroup (unaffected regression check)', async ({ page }) => {
    await page.goto(`${BASE_URL}/Assets`);
    await page.waitForLoadState('domcontentloaded');
    const zoneSelect = page.locator('select[name="zoneId"]');
    await expect(zoneSelect).toBeVisible();
    const optgroupCount = await zoneSelect.locator('optgroup').count();
    expect(optgroupCount).toBeGreaterThan(0);
  });

  test('Asset Details shows QR code, barcode, Print Label link, and Inspection History card', async ({ page }) => {
    await page.goto(`${BASE_URL}/Assets/Details/1`);
    await page.waitForLoadState('domcontentloaded');
    expect(page.url()).not.toContain('AccessDenied');

    await expect(page.locator('img[src*="QrCode"]')).toBeVisible();
    await expect(page.locator('img[src*="Barcode"]')).toBeVisible();
    await expect(page.locator('a[href*="/Assets/Label/1"]').first()).toBeVisible();
    await expect(page.locator('body')).toContainText('Inspection History');
  });

  test('QR code and barcode endpoints return real PNG images', async ({ page, request }) => {
    const cookies = await page.context().cookies();
    const cookieHeader = cookies.map(c => `${c.name}=${c.value}`).join('; ');

    const qr = await request.get(`${BASE_URL}/Assets/QrCode/1`, { headers: { Cookie: cookieHeader } });
    expect(qr.ok()).toBeTruthy();
    expect(qr.headers()['content-type']).toContain('image/png');
    const qrBody = await qr.body();
    expect(qrBody.length).toBeGreaterThan(50);

    const barcode = await request.get(`${BASE_URL}/Assets/Barcode/1`, { headers: { Cookie: cookieHeader } });
    expect(barcode.ok()).toBeTruthy();
    expect(barcode.headers()['content-type']).toContain('image/png');
    const barcodeBody = await barcode.body();
    expect(barcodeBody.length).toBeGreaterThan(50);
  });

  test('Print Label page renders both codes and a working Print button', async ({ page }) => {
    await page.goto(`${BASE_URL}/Assets/Label/1`);
    await page.waitForLoadState('domcontentloaded');

    await expect(page.locator('.label .tag')).toContainText('AST-0001');
    await expect(page.locator('img.qr')).toBeVisible();
    await expect(page.locator('img.barcode')).toBeVisible();
    await expect(page.locator('button:has-text("Print")')).toBeVisible();
  });

  test('Zones index has a working List/Map view toggle', async ({ page }) => {
    await page.goto(`${BASE_URL}/Zones`);
    await page.waitForLoadState('domcontentloaded');

    await expect(page.locator('#listView')).toBeVisible();
    await expect(page.locator('#mapView')).toHaveClass(/d-none/);

    await page.click('#mapViewBtn');
    await expect(page.locator('#mapView')).not.toHaveClass(/d-none/);
    await expect(page.locator('#listView')).toHaveClass(/d-none/);
    await expect(page.locator('#zonesOverviewMap')).toBeVisible();
  });
});
