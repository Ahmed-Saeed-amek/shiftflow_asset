// @ts-check
// Employee-side negative paths when submitting a Temp Transfer request.
const { test, expect } = require('@playwright/test');

const BASE_URL = 'https://localhost:55248';
const ENGINEER = { email: 'engineer@shiftflow.com', password: 'Engineer@123456' };

async function login(page, { email, password }) {
  await page.goto(`${BASE_URL}/Account/Login`);
  await page.fill('input[name="Email"]', email);
  await page.fill('input[name="Password"]', password);
  await page.locator('form:has(input[name="Email"]) button[type="submit"]').click();
  await page.waitForLoadState('domcontentloaded');
}

test('Request Transfer button is disabled with a tooltip when the employee has no eligible shifts/groups', async ({ page }) => {
  await login(page, ENGINEER);
  await page.goto(`${BASE_URL}/ChangeRequests/MyRequests`);
  await page.waitForLoadState('domcontentloaded');

  const disabledBtn = page.locator('button:has-text("Request Transfer")[disabled]');
  if (await disabledBtn.count() === 0) {
    console.log('Engineer currently has upcoming shifts and available groups — button is enabled, nothing to assert here.');
    return;
  }

  await expect(disabledBtn).toHaveCount(1);
  const title = await disabledBtn.getAttribute('title');
  console.log('Disabled button tooltip:', title);
  expect(title, 'Disabled button must explain why via a title tooltip').toBeTruthy();
  expect(title === 'No other groups available in your work area'
      || title === 'No upcoming shifts in published schedules').toBe(true);
});

test('submitting a temp transfer with an empty Reason is blocked client-side (required field)', async ({ page }) => {
  await login(page, ENGINEER);
  await page.goto(`${BASE_URL}/ChangeRequests/MyRequests`);
  await page.waitForLoadState('domcontentloaded');

  const enabledBtn = page.locator('button[data-bs-target="#transferModal"]:not([disabled])');
  if (await enabledBtn.count() === 0) {
    console.log('No enabled Request Transfer button (no upcoming shifts / no other groups) — skipping');
    return;
  }
  await enabledBtn.click();

  const modal = page.locator('#transferModal');
  await expect(modal).toBeVisible();

  await modal.locator('select[name="DailyGroupShiftId"]').selectOption({ index: 1 });
  await modal.locator('select[name="TargetGroupId"]').selectOption({ index: 1 });
  // Deliberately leave Reason empty and try to submit.
  const reasonField = modal.locator('textarea[name="Reason"]');
  await expect(reasonField).toHaveValue('');
  await modal.locator('button[type="submit"]').click();

  // HTML5 required validation should keep the browser from submitting the form at all.
  const isValid = await reasonField.evaluate((el) => /** @type {HTMLTextAreaElement} */ (el).checkValidity());
  expect(isValid, 'Empty required Reason field should fail native form validation').toBe(false);
  await expect(modal, 'Modal should still be open — no navigation should have occurred').toBeVisible();
  expect(page.url()).toContain('/ChangeRequests/MyRequests');
});

test('submitting a temp transfer to a group with no shift scheduled that day is rejected at submission (no Pending row created)', async ({ page }) => {
  await login(page, ENGINEER);
  await page.goto(`${BASE_URL}/ChangeRequests/MyRequests`);
  await page.waitForLoadState('domcontentloaded');

  const enabledBtn = page.locator('button[data-bs-target="#transferModal"]:not([disabled])');
  if (await enabledBtn.count() === 0) {
    console.log('No enabled Request Transfer button — skipping (need at least one upcoming shift to drive this test)');
    return;
  }
  await enabledBtn.click();
  const modal = page.locator('#transferModal');
  await expect(modal).toBeVisible();

  const shiftOption = modal.locator('select[name="DailyGroupShiftId"] option').nth(1);
  const dailyGroupShiftId = await shiftOption.getAttribute('value');
  test.skip(!dailyGroupShiftId, 'No upcoming shift option available');

  const token = await modal.locator('input[name="__RequestVerificationToken"]').getAttribute('value');
  await page.locator('#transferModal .btn-close').click();

  // A TargetGroupId that cannot possibly have a DailyGroupShift row (no ShiftGroup with this id
  // exists) deterministically triggers the "target group has no shift scheduled" guard —
  // regardless of which real shifts/groups exist in the current DB state.
  const BOGUS_TARGET_GROUP_ID = 999999;
  const response = await page.request.post(`${BASE_URL}/ChangeRequests/Submit`, {
    form: {
      __RequestVerificationToken: token ?? '',
      DailyGroupShiftId: dailyGroupShiftId ?? '',
      RequestType: 'TempGroupChange',
      TargetGroupId: String(BOGUS_TARGET_GROUP_ID),
      Reason: 'E2E submit-validation: bogus target group',
    },
  });

  expect(response.ok(), 'Submit should render 200 with inline validation error, not a server error').toBe(true);
  const body = await response.text();
  expect(body).toContain('Cannot submit transfer');
  expect(body).toContain('the target group has no shift scheduled on');

  // Confirm no request was actually created for this bogus attempt.
  await page.goto(`${BASE_URL}/ChangeRequests/MyRequests`);
  await page.waitForLoadState('domcontentloaded');
  const badRow = page.locator('table tbody tr').filter({ hasText: 'E2E submit-validation: bogus target group' });
  await expect(badRow, 'Rejected submission must not leave a Pending row behind').toHaveCount(0);
});
