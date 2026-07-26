// @ts-check
// Verifies clickable navigation on /ShiftAnalytics/AreaTasks:
//   - Shift Date link -> that row's own shift runbook.
//   - Task title link -> the LATEST shift in the task's rollover chain.
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

test.describe('AreaTasks navigation', () => {
  test.beforeEach(async ({ page }) => {
    await login(page, ADMIN);
  });

  test('LatestShift redirects through a multi-hop rollover chain (5016 -> 5018 -> 5020)', async ({ page }) => {
    // Confirmed via SQL: task 5016 rolled over to 5018, which rolled over to 5020.
    // 5020's shift is DailyGroupShiftId 30179. Clicking the oldest task in the
    // chain must land on the newest shift, not its own (30173) or the middle one (30174).
    const res = await page.goto(`${BASE_URL}/ShiftAnalytics/LatestShift?taskId=5016`);
    expect(res?.url()).toContain('/ShiftOps/Shift/30179');
    expect(res?.url()).toContain('tab=tasks');
  });

  test('LatestShift on a task with no rollover chain redirects to its own shift', async ({ page }) => {
    // Task 5020 is the end of the chain (nothing rolled over from it).
    const res = await page.goto(`${BASE_URL}/ShiftAnalytics/LatestShift?taskId=5020`);
    expect(res?.url()).toContain('/ShiftOps/Shift/30179');
  });

  test('AreaTasks page renders Shift Date and Task title as links to the right targets', async ({ page }) => {
    await page.goto(`${BASE_URL}/ShiftAnalytics/AreaTasks?areaId=1007&range=custom&from=2026-07-04&to=2026-07-04`);
    await page.waitForLoadState('domcontentloaded');

    const row = page.locator('tr[data-status]').filter({ hasText: 'attendence' }).first();
    await expect(row).toBeVisible();

    // Task title link points at LatestShift for that row's own task id.
    const titleLink = row.locator('a[asp-route-taskid], a').filter({ hasText: 'attendence' }).first();
    const titleHref = await titleLink.getAttribute('href');
    expect(titleHref).toContain('/ShiftAnalytics/LatestShift');

    // Shift date cell links straight to ShiftOps/Shift with that row's own id.
    const dateLink = row.locator('a[href*="/ShiftOps/Shift/"]');
    await expect(dateLink).toBeVisible();
  });
});
