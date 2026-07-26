// @ts-check
// Verifies that after a Temp Transfer is approved:
//  1. The engineer's calendar shows the transfer icon + correct group on that date
//  2. The engineer can access the runbook for the shift they were transferred into
const { test, expect } = require('@playwright/test');

const BASE_URL = 'https://localhost:55248';
const ENGINEER = { email: 'engineer@shiftflow.com', password: 'Engineer@123456' };
const MANAGER  = { email: 'manager@shiftflow.com', password: 'Manager@123456' };

// Known from DB: engineer's Group B shift on 2026-07-09 (Night), transferring to Group A (Evening)
const TARGET_DGS_ID = 32328; // Group A shift on 2026-07-09
const TARGET_GROUP_NAME = 'A';

async function login(page, { email, password }) {
  await page.goto(`${BASE_URL}/Account/Login`);
  await page.fill('input[name="Email"]', email);
  await page.fill('input[name="Password"]', password);
  await page.click('form:has(input[name="Password"]) button[type="submit"]');
  await page.waitForLoadState('domcontentloaded');
  await page.waitForLoadState('load');
}

test('temp transfer updates calendar and grants runbook access', async ({ browser }) => {
  test.setTimeout(120000); // multi-step E2E flow: 2 logins, submit, approve, calendar + runbook checks
  const engineerCtx = await browser.newContext({ ignoreHTTPSErrors: true });
  const engineerPage = await engineerCtx.newPage();

  // ── Step 1: Engineer submits a temp transfer for their 2026-07-09 shift ──
  await login(engineerPage, ENGINEER);
  await engineerPage.goto(`${BASE_URL}/ChangeRequests/MyRequests`);
  await engineerPage.waitForLoadState('domcontentloaded');

  const enabledBtn = engineerPage.locator('button[data-bs-target="#transferModal"]:not([disabled])');
  await expect(enabledBtn).toHaveCount(1, { timeout: 10000 });
  await enabledBtn.click();

  const modal = engineerPage.locator('#transferModal');
  await expect(modal).toBeVisible();

  // Select the 2026-07-09 shift option (must match the visible text format)
  const shiftSelect = modal.locator('select[name="DailyGroupShiftId"]');
  const options = await shiftSelect.locator('option').allTextContents();
  const julyIndex = options.findIndex(o => o.includes('09 Jul 2026'));
  console.log('Shift options:', options);
  test.skip(julyIndex === -1, 'Expected shift for 09 Jul 2026 not found in dropdown — DB state may have changed');
  await shiftSelect.selectOption({ index: julyIndex });

  await modal.locator('select[name="TargetGroupId"]').selectOption({ label: TARGET_GROUP_NAME });
  await modal.locator('textarea[name="Reason"]').fill('E2E temp-transfer effects test');
  await modal.locator('button[type="submit"]').click();
  await engineerPage.waitForLoadState('domcontentloaded');
  expect(engineerPage.url()).toContain('/ChangeRequests/MyRequests');

  // Close the engineer session before opening the manager one — avoids concurrent
  // navigations against the dev server causing ERR_ABORTED.
  await engineerCtx.close();

  // ── Step 2: Manager approves it ──
  const managerCtx = await browser.newContext({ ignoreHTTPSErrors: true });
  const managerPage = await managerCtx.newPage();
  await login(managerPage, MANAGER);
  await managerPage.goto(`${BASE_URL}/ChangeRequests`, { waitUntil: 'domcontentloaded' });
  await managerPage.waitForLoadState('domcontentloaded');

  const row = managerPage.locator('table tbody tr').filter({ hasText: 'E2E temp-transfer effects test' }).first();
  await expect(row).toHaveCount(1, { timeout: 10000 });

  // Self-submitted transfer must show a real Approve button, not "Awaiting Employee"
  const approveForm = row.locator('form[action*="/ChangeRequests/Approve"]');
  await expect(approveForm).toHaveCount(1);
  await approveForm.locator('button').click();
  await managerPage.waitForLoadState('domcontentloaded');

  const successMsg = await managerPage.textContent('body');
  console.log('Approve result contains "approved":', successMsg?.includes('approved'));
  await managerCtx.close();

  // ── Step 3: Engineer's calendar shows the transfer ──
  const engineerCtx2 = await browser.newContext({ ignoreHTTPSErrors: true });
  const engineerPage2 = await engineerCtx2.newPage();
  await login(engineerPage2, ENGINEER);
  await engineerPage2.goto(`${BASE_URL}/ShiftOps/MySchedule?year=2026&month=7`);
  await engineerPage2.waitForLoadState('domcontentloaded');

  // Find the day-9 calendar cell in-browser (day-number span text trims to exactly "9")
  // and confirm it shows the transfer icon and the target group's shift ("Evening").
  const dayCellInfo = await engineerPage2.evaluate(() => {
    const spans = Array.from(document.querySelectorAll('td span.fw-semibold'));
    const span = spans.find(s => s.textContent?.trim() === '9');
    const td = span?.closest('td');
    if (!td) return null;
    return {
      hasTransferIcon: !!td.querySelector('i.bi-arrow-left-right'),
      badgeText: td.querySelector('.badge')?.textContent?.trim() ?? null,
    };
  });

  console.log('Day-9 cell info:', dayCellInfo);
  expect(dayCellInfo, 'Day-9 calendar cell should exist').not.toBeNull();
  expect(dayCellInfo.hasTransferIcon, 'Calendar should show the transfer icon on 09 Jul').toBe(true);
  expect(dayCellInfo.badgeText, 'Shift badge on 09 Jul should reflect the target group\'s shift').toContain('Evening');

  // ── Step 4: Engineer can access the runbook they were transferred into ──
  await engineerPage2.goto(`${BASE_URL}/ShiftOps/Shift?id=${TARGET_DGS_ID}`);
  await engineerPage2.waitForLoadState('domcontentloaded');

  expect(engineerPage2.url()).not.toContain('AccessDenied');
  const bodyText = await engineerPage2.textContent('body');
  expect(bodyText).not.toContain('Access Denied');
  console.log('Runbook page URL after transfer:', engineerPage2.url());

  // The engineer should appear in the roster for this shift
  const engineerInRoster = bodyText?.includes('Khalid Al-Mutairi');
  console.log('Engineer name found in roster:', engineerInRoster);
  expect(engineerInRoster, 'Transferred engineer should appear in the target shift roster').toBeTruthy();
});
