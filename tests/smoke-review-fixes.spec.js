// @ts-check
// Smoke pass over the pages and flows touched by the September 2026 review fixes:
// every main page renders without a server error or JS error, the layout toast fires,
// typeahead Enter no longer submits the parent form, and an order can be created
// end-to-end (exercises the OrderNumberSequence allocator).
const { test, expect } = require('@playwright/test');

const ADMIN = { email: 'admin@shiftflow.com', password: 'Admin@123456' };

async function login(page, { email, password }) {
  await page.goto('/Account/Login');
  await page.fill('input[name="Email"]', email);
  await page.fill('input[name="Password"]', password);
  await page.click('form:has(input[name="Password"]) button[type="submit"]');
  await page.waitForLoadState('domcontentloaded');
}

function collectErrors(page) {
  const errors = [];
  page.on('pageerror', e => errors.push(`pageerror: ${e.message}`));
  page.on('console', m => { if (m.type() === 'error') errors.push(`console: ${m.text()}`); });
  page.on('response', r => { if (r.status() >= 500) errors.push(`HTTP ${r.status()} ${r.url()}`); });
  return errors;
}

const PAGES = [
  '/Dashboard', '/MyHome', '/Orders', '/Orders/Create', '/WorkOrders', '/Assets', '/Assets/Create',
  '/Zones', '/ZoneOverview', '/SpareParts', '/SparePartsAnalytics', '/Contracts', '/Vendors',
  '/Users', '/Users/MyOrders', '/Rbac', '/UserAssetScopes', '/Groups', '/Groups/Create',
  '/RecurringOrders', '/RecurringOrders/Create', '/AuditLogs', '/OrderTypes', '/AssetCategories', '/AiAssistant',
];

test.describe('Review-fix smoke', () => {
  test('main pages render with no server or JS errors (EN and AR)', async ({ page, context }) => {
    const errors = collectErrors(page);
    await login(page, ADMIN);
    for (const lang of ['en', 'ar']) {
      await context.addCookies([{ name: 'shiftflow_lang', value: lang, url: 'https://localhost:55248' }]);
      for (const url of PAGES) {
        await page.goto(url, { waitUntil: 'domcontentloaded' });
        expect(page.url(), url).not.toContain('AccessDenied');
        await expect(page.locator('body'), url).not.toContainText('An unhandled exception');
        const html = page.locator('html');
        await expect(html).toHaveAttribute('dir', lang === 'ar' ? 'rtl' : 'ltr');
      }
    }
    // Font/CDN blocked warnings are not app bugs; everything else is.
    const real = errors.filter(e => !/fonts\.g|net::ERR_|favicon/.test(e));
    expect(real, real.join('\n')).toEqual([]);
  });

  test('sidebar highlights the section on a Details page', async ({ page }) => {
    await login(page, ADMIN);
    await page.goto('/Assets');
    const firstLink = page.locator('table tbody tr a[href*="/Assets/Details/"]').first();
    await firstLink.click();
    await page.waitForLoadState('domcontentloaded');
    await expect(page.locator('#sidebar a.active, .sidebar a.active, nav a.active').first()).toContainText(/Assets|الأصول/);
  });

  test('typeahead Enter does not submit the form; order is created with a generated number', async ({ page }) => {
    const errors = collectErrors(page);
    await login(page, ADMIN);
    await page.goto('/Orders/Create');

    // Choose the first order type, then whichever asset picker that type shows.
    const firstType = page.locator('input[name="OrderTypeId"]').first();
    await firstType.check({ force: true });
    // applyType() swaps the single/multi asset wrappers on change; let it settle before choosing one.
    await page.waitForTimeout(500);
    const singleVisible = await page.locator('#assetSingleWrap').isVisible();
    const wrap = page.locator(singleVisible ? '#assetSingleWrap' : '#assetMultiWrap');
    const assetInput = wrap.locator(singleVisible ? 'input[type="text"]' : 'input[data-search]').first();
    await assetInput.fill('AST-000');
    const results = wrap.locator(singleVisible ? '[data-ap-results] .list-group-item' : '[data-results] .list-group-item-action');
    await expect(results.first()).toBeVisible({ timeout: 10000 });

    // Enter must never submit the form from inside a typeahead.
    await assetInput.press('ArrowDown');
    await assetInput.press('Enter');
    await page.waitForTimeout(300);
    expect(page.url()).toContain('/Orders/Create');
    await expect(page.locator('.alert-danger:visible')).toHaveCount(0);

    // Make sure an asset is actually selected (multi picker adds a chip, single picker fills the hidden id).
    const selected = singleVisible
      ? await wrap.locator('input[type="hidden"]').first().inputValue()
      : await wrap.locator('[data-chip]').count();
    if (!selected) {
      if (!(await results.first().isVisible())) await assetInput.fill('AST-000');
      await results.first().click();
    }

    // Assignee: first employee match.
    await page.locator('#assigneeUser').check({ force: true });
    const empInput = page.locator('#assigneeUserRow input[type="text"]').first();
    await empInput.fill('e');
    const empResults = page.locator('#assigneeUserRow .list-group-item, #assigneeUserRow [role="option"]');
    await expect(empResults.first()).toBeVisible({ timeout: 10000 });
    await empResults.first().click();

    await page.click('button[type="submit"]:has-text("Create Order"), button[type="submit"]:has-text("إنشاء")');
    await page.waitForLoadState('domcontentloaded');

    // Success toast from the layout and a generated order number on the page.
    const toast = page.locator('.toast');
    await expect(toast.first()).toBeVisible({ timeout: 10000 });
    await expect(page.locator('body')).toContainText(/[A-Z]{2,4}-\d{4}-\d{3,}/);
    const real = errors.filter(e => !/fonts\.g|net::ERR_|favicon/.test(e));
    expect(real, real.join('\n')).toEqual([]);
  });
});
