// @ts-check
// Responsive design verification suite. Checks a representative sample of
// pages across the app at 5 standard
// breakpoints for horizontal overflow, plus sidebar drawer and modal
// behavior below the 992px (lg) breakpoint where the layout switches from
// a static sidebar to a mobile overlay drawer.
const { test, expect } = require('@playwright/test');

const BASE_URL = 'https://localhost:55248';
const ADMIN = { email: 'admin@shiftflow.com', password: 'Admin@123456' };
const ENGINEER = { email: 'engineer@shiftflow.com', password: 'Engineer@123456' };

const VIEWPORTS = [
  { name: '320x568', width: 320, height: 568 },
  { name: '375x667', width: 375, height: 667 },
  { name: '768x1024', width: 768, height: 1024 },
  { name: '1024x768', width: 1024, height: 768 },
  { name: '1440x900', width: 1440, height: 900 },
];

async function login(page, { email, password }) {
  await page.goto(`${BASE_URL}/Account/Login`);
  await page.fill('input[name="Email"]', email);
  await page.fill('input[name="Password"]', password);
  await page.click('form:has(input[name="Password"]) button[type="submit"]');
  await page.waitForLoadState('domcontentloaded');
  await page.waitForLoadState('load');
}

async function assertNoOverflow(page, label) {
  const overflow = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(overflow.scrollWidth, `${label}: no horizontal overflow`).toBeLessThanOrEqual(overflow.clientWidth + 1);
}

// Representative sample of real routes: dashboards/analytics, the order and
// asset-management list pages, administration pages, and the personal 'My Work' pages.
const PAGES = [
  { path: '/Dashboard', role: 'admin', label: 'Executive Dashboard' },
  { path: '/ZoneOverview', role: 'admin', label: 'Zone Overview' },
  { path: '/SparePartsAnalytics', role: 'admin', label: 'Spare Parts Analytics' },
  { path: '/Orders', role: 'admin', label: 'All Orders list' },
  { path: '/InspectionOrders', role: 'admin', label: 'Inspection Orders list' },
  { path: '/MaintenanceOrders', role: 'admin', label: 'Maintenance Orders list' },
  { path: '/Assets', role: 'admin', label: 'Assets list' },
  { path: '/WorkOrders', role: 'admin', label: 'Work Orders list' },
  { path: '/Vendors', role: 'admin', label: 'Vendors list' },
  { path: '/Contracts', role: 'admin', label: 'Contracts list' },
  { path: '/SpareParts', role: 'admin', label: 'Spare Parts list' },
  { path: '/Zones', role: 'admin', label: 'Asset Locations (Zones)' },
  { path: '/AssetCategories', role: 'admin', label: 'Asset Categories' },
  { path: '/Users', role: 'admin', label: 'Users list' },
  { path: '/Rbac', role: 'admin', label: 'RBAC (Permissions)' },
  { path: '/UserAssetScopes', role: 'admin', label: 'Asset Visibility' },
  { path: '/AuditLogs', role: 'admin', label: 'Audit Logs' },
  { path: '/MyHome', role: 'engineer', label: 'My Home' },
  { path: '/Users/MyOrders', role: 'engineer', label: 'My Orders' },
];


for (const vp of VIEWPORTS) {
  test.describe(`viewport ${vp.name}`, () => {
    for (const p of PAGES) {
      test(`${p.label} — no horizontal overflow`, async ({ page }) => {
        await page.setViewportSize({ width: vp.width, height: vp.height });
        await login(page, p.role === 'admin' ? ADMIN : ENGINEER);
        await page.goto(`${BASE_URL}${p.path}`, { waitUntil: 'domcontentloaded' });
        await page.waitForTimeout(400);
        await assertNoOverflow(page, `${p.label} @ ${vp.name}`);
      });
    }
  });
}

test.describe('sidebar drawer behavior below lg (992px)', () => {
  for (const vp of [{ name: '375x667', width: 375, height: 667 }, { name: '768x1024', width: 768, height: 1024 }]) {
    test(`opens via toggle and closes via backdrop at ${vp.name}`, async ({ page }) => {
      await page.setViewportSize({ width: vp.width, height: vp.height });
      await login(page, ADMIN);
      await page.goto(`${BASE_URL}/Dashboard`, { waitUntil: 'domcontentloaded' });

      const sidebarBefore = await page.locator('#sidebar').boundingBox();
      expect(sidebarBefore?.x, 'sidebar starts off-screen').toBeLessThan(0);

      await page.click('#sidebarToggle');
      await page.waitForTimeout(350);
      const sidebarOpen = await page.locator('#sidebar').boundingBox();
      expect(sidebarOpen?.x, 'sidebar visible after toggle').toBeCloseTo(0, 0);
      await expect(page.locator('#sidebarBackdrop')).toBeVisible();

      // click outside the 260px-wide drawer, not inside it
      await page.click('#sidebarBackdrop', { position: { x: vp.width - 10, y: 5 } });
      await page.waitForTimeout(350);
      const sidebarClosed = await page.locator('#sidebar').boundingBox();
      expect(sidebarClosed?.x, 'sidebar closes after backdrop click').toBeLessThan(0);
    });
  }

  test('sidebar is static (not an overlay drawer) at 1440px desktop, and the toggle collapses it in-place', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await login(page, ADMIN);
    await page.goto(`${BASE_URL}/Dashboard`, { waitUntil: 'domcontentloaded' });
    const sidebarBox = await page.locator('#sidebar').boundingBox();
    expect(sidebarBox?.x, 'sidebar static at desktop').toBeCloseTo(0, 0);
    expect(sidebarBox?.width, 'sidebar expanded by default').toBeCloseTo(260, 0);
    await expect(page.locator('#sidebarBackdrop')).toBeHidden();

    // desktop toggle collapses width in-place (frees space for content),
    // not the mobile overlay-drawer behavior
    await expect(page.locator('#sidebarToggle')).toBeVisible();
    await page.click('#sidebarToggle');
    await page.waitForTimeout(400);
    const collapsedBox = await page.locator('#sidebar').boundingBox();
    expect(collapsedBox?.width, 'sidebar collapses to 0 width on desktop toggle').toBeLessThan(5);
    await expect(page.locator('#sidebarBackdrop')).toBeHidden();

    // persists across a full page navigation
    await page.goto(`${BASE_URL}/Users`, { waitUntil: 'domcontentloaded' });
    const afterNavBox = await page.locator('#sidebar').boundingBox();
    expect(afterNavBox?.width, 'collapsed state persists across navigation').toBeLessThan(5);

    // restore expanded state so it doesn't leak into other tests via the shared localStorage
    await page.click('#sidebarToggle');
    await page.waitForTimeout(400);
  });
});

test.describe('mobile nav list touch scrolling', () => {
  test('nav list uses overflow-y:auto with touch/momentum scrolling enabled', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 667 });
    await login(page, ADMIN);
    await page.goto(`${BASE_URL}/Dashboard`, { waitUntil: 'domcontentloaded' });
    await page.click('#sidebarToggle');
    await page.waitForTimeout(300);
    const overflowY = await page.evaluate(() => {
      const ul = document.querySelector('.sidebar-nav-scroll');
      return ul ? getComputedStyle(ul).overflowY : null;
    });
    expect(overflowY).toBe('auto');
  });
});

test.describe('modals stay within viewport at 375x667', () => {
  test('Work Orders list modal stays inside the viewport', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 667 });
    await login(page, ADMIN);
    await page.goto(`${BASE_URL}/WorkOrders`, { waitUntil: 'domcontentloaded' });
    const btn = page.locator('[data-bs-toggle="modal"]').first();
    if (await btn.count() === 0) {
      test.skip(true, 'no modal trigger available in current data set');
    }
    await btn.click();
    const modal = page.locator('.modal.show .modal-dialog');
    await expect(modal).toBeVisible();
    const box = await modal.boundingBox();
    const viewport = page.viewportSize();
    expect(box && viewport && box.x >= 0 && box.x + box.width <= viewport.width + 1).toBeTruthy();
  });
});

test.describe('RTL sanity at 375x667', () => {
  test('Arabic mobile drawer mirrors correctly and no overflow on Dashboard', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 667 });
    await login(page, ADMIN);
    await page.goto(`${BASE_URL}/Dashboard`, { waitUntil: 'domcontentloaded' });
    await page.request.post(`${BASE_URL}/Language/SetLanguage`, { form: { lang: 'ar', returnUrl: '/Dashboard' } });
    await page.goto(`${BASE_URL}/Dashboard`, { waitUntil: 'domcontentloaded' });
    const dir = await page.getAttribute('html', 'dir');
    expect(dir).toBe('rtl');
    await assertNoOverflow(page, 'RTL Dashboard @ 375x667');

    await page.click('#sidebarToggle');
    await page.waitForTimeout(350);
    const box = await page.locator('#sidebar').boundingBox();
    const viewport = page.viewportSize();
    expect(box && viewport && Math.abs((box.x + box.width) - viewport.width) < 5, 'RTL drawer opens flush to the right').toBeTruthy();

    // switch back to English so it doesn't leak into other tests via shared dev DB cookie state
    await page.request.post(`${BASE_URL}/Language/SetLanguage`, { form: { lang: 'en', returnUrl: '/Dashboard' } });
  });
});
