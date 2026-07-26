// @ts-check
// Employee self-service Permanent Transfer (RequestType=PermanentGroupChange) submitted through the
// "Request Transfer" modal on MyRequests. Permanent transfers have no manager-initiated UI entry
// point (ChangeRequestsController.TempTransfer only ever creates TempGroupChange), so every
// PermanentGroupChange request is self-submitted and — unlike TempGroupChange — always gets a real
// Approve button for the manager (the "must be accepted/declined by employee" guard is scoped to
// RequestType=="TempGroupChange" only).
const { test, expect } = require('@playwright/test');

const BASE_URL = 'https://localhost:55248';
const ENGINEER = { email: 'engineer@shiftflow.com', password: 'Engineer@123456' };
const MANAGER  = { email: 'manager@shiftflow.com', password: 'Manager@123456' };

async function login(page, { email, password }) {
  await page.goto(`${BASE_URL}/Account/Login`);
  await page.fill('input[name="Email"]', email);
  await page.fill('input[name="Password"]', password);
  await page.locator('form:has(input[name="Email"]) button[type="submit"]').click();
  await page.waitForLoadState('domcontentloaded');
}

test('permanent-transfer radio toggles the membership-change warning banner', async ({ page }) => {
  await login(page, ENGINEER);
  await page.goto(`${BASE_URL}/ChangeRequests/MyRequests`);
  await page.waitForLoadState('domcontentloaded');

  const enabledBtn = page.locator('button[data-bs-target="#transferModal"]:not([disabled])');
  if (await enabledBtn.count() === 0) {
    console.log('No enabled Request Transfer button — skipping (no upcoming shifts / no other groups)');
    return;
  }
  await enabledBtn.click();
  const modal = page.locator('#transferModal');
  await expect(modal).toBeVisible();

  const warning = modal.locator('#permWarning');
  await expect(warning, 'Temp is selected by default — warning must be hidden').toBeHidden();

  await modal.locator('label[for="typePerm"]').click();
  await expect(warning, 'Selecting Permanent must reveal the warning banner').toBeVisible();

  await modal.locator('label[for="typeTemp"]').click();
  await expect(warning, 'Switching back to Temporary must hide the warning again').toBeHidden();
});

test('employee submits a permanent transfer, manager approves it with a real Approve button, and it applies', async ({ browser }) => {
  test.setTimeout(90000);
  const engineerCtx = await browser.newContext({ ignoreHTTPSErrors: true });
  const engineerPage = await engineerCtx.newPage();
  await login(engineerPage, ENGINEER);
  await engineerPage.goto(`${BASE_URL}/ChangeRequests/MyRequests`);
  await engineerPage.waitForLoadState('domcontentloaded');

  const enabledBtn = engineerPage.locator('button[data-bs-target="#transferModal"]:not([disabled])');
  test.skip(await enabledBtn.count() === 0, 'No enabled Request Transfer button — need an upcoming shift + another group');
  await enabledBtn.click();

  const modal = engineerPage.locator('#transferModal');
  await expect(modal).toBeVisible();

  const targetGroupOption = modal.locator('select[name="TargetGroupId"] option').nth(1);
  const targetGroupName = (await targetGroupOption.textContent())?.trim();
  test.skip(!targetGroupName, 'No target group available to transfer into');

  await modal.locator('select[name="DailyGroupShiftId"]').selectOption({ index: 1 });
  await modal.locator('label[for="typePerm"]').click();
  await modal.locator('select[name="TargetGroupId"]').selectOption({ label: targetGroupName ?? '' });
  const reasonText = `E2E permanent-transfer approve test ${Date.now()}`;
  await modal.locator('textarea[name="Reason"]').fill(reasonText);
  await modal.locator('button[type="submit"]').click();
  await engineerPage.waitForLoadState('domcontentloaded');
  expect(engineerPage.url()).toContain('/ChangeRequests/MyRequests');

  const submittedRow = engineerPage.locator('table tbody tr').filter({ hasText: reasonText }).first();
  await expect(submittedRow, 'Permanent transfer must land in the requester\'s own table, not the Accept/Decline card').toHaveCount(1, { timeout: 10000 });
  // Permanent transfers are self-submitted, so they must show a Cancel button (requester == affected user),
  // never an Accept/Decline pair — those are exclusive to manager-initiated TempGroupChange.
  await expect(submittedRow.locator('form[action*="AcceptTransfer"]')).toHaveCount(0);
  await engineerCtx.close();

  const managerCtx = await browser.newContext({ ignoreHTTPSErrors: true });
  const managerPage = await managerCtx.newPage();
  await login(managerPage, MANAGER);
  await managerPage.goto(`${BASE_URL}/ChangeRequests`, { waitUntil: 'domcontentloaded' });

  const managerRow = managerPage.locator('table tbody tr').filter({ hasText: reasonText }).first();
  await expect(managerRow, 'Manager should see the pending permanent transfer').toHaveCount(1, { timeout: 10000 });

  // Unlike a manager-initiated TempGroupChange, a self-submitted PermanentGroupChange must show a
  // real, clickable Approve button — never "Awaiting Employee".
  await expect(managerRow.locator('span.badge:has-text("Awaiting Employee")')).toHaveCount(0);
  const approveForm = managerRow.locator('form[action*="/ChangeRequests/Approve"]');
  await expect(approveForm).toHaveCount(1);
  await approveForm.locator('button').click();
  await managerPage.waitForLoadState('domcontentloaded');
  const approveResultText = await managerPage.textContent('body');
  expect(approveResultText).toContain('Request approved and applied');
  await managerCtx.close();

  const engineerCtx2 = await browser.newContext({ ignoreHTTPSErrors: true });
  const engineerPage2 = await engineerCtx2.newPage();
  await login(engineerPage2, ENGINEER);
  await engineerPage2.goto(`${BASE_URL}/ChangeRequests/MyRequests`, { waitUntil: 'domcontentloaded' });
  const finalRow = engineerPage2.locator('table tbody tr').filter({ hasText: reasonText }).first();
  await expect(finalRow.locator('span.badge:has-text("Approved")'), 'Permanent transfer should now show Approved').toHaveCount(1);
  await engineerCtx2.close();
});

test('employee can cancel a permanent transfer before it is approved', async ({ browser }) => {
  test.setTimeout(60000);
  const engineerCtx = await browser.newContext({ ignoreHTTPSErrors: true });
  const engineerPage = await engineerCtx.newPage();
  await login(engineerPage, ENGINEER);
  await engineerPage.goto(`${BASE_URL}/ChangeRequests/MyRequests`);
  await engineerPage.waitForLoadState('domcontentloaded');

  const enabledBtn = engineerPage.locator('button[data-bs-target="#transferModal"]:not([disabled])');
  test.skip(await enabledBtn.count() === 0, 'No enabled Request Transfer button');
  await enabledBtn.click();
  const modal = engineerPage.locator('#transferModal');
  await expect(modal).toBeVisible();

  await modal.locator('select[name="DailyGroupShiftId"]').selectOption({ index: 1 });
  await modal.locator('label[for="typePerm"]').click();
  await modal.locator('select[name="TargetGroupId"]').selectOption({ index: 1 });
  const reasonText = `E2E permanent-transfer cancel test ${Date.now()}`;
  await modal.locator('textarea[name="Reason"]').fill(reasonText);
  await modal.locator('button[type="submit"]').click();
  await engineerPage.waitForLoadState('domcontentloaded');

  const row = engineerPage.locator('table tbody tr').filter({ hasText: reasonText }).first();
  await expect(row).toHaveCount(1, { timeout: 10000 });

  engineerPage.once('dialog', (dialog) => dialog.accept());
  await row.locator('form[action*="/ChangeRequests/Cancel"] button').click();
  await engineerPage.waitForLoadState('domcontentloaded');

  const bodyText = await engineerPage.textContent('body');
  expect(bodyText).toContain('Request cancelled');

  const cancelledRow = engineerPage.locator('table tbody tr').filter({ hasText: reasonText }).first();
  await expect(cancelledRow.locator('span.badge:has-text("Cancelled")')).toHaveCount(1);

  // Must also disappear from the manager's pending queue.
  await engineerCtx.close();
  const managerCtx = await browser.newContext({ ignoreHTTPSErrors: true });
  const managerPage = await managerCtx.newPage();
  await login(managerPage, MANAGER);
  await managerPage.goto(`${BASE_URL}/ChangeRequests`, { waitUntil: 'domcontentloaded' });
  const managerRow = managerPage.locator('table tbody tr').filter({ hasText: reasonText });
  await expect(managerRow, 'Cancelled request must not appear in the pending queue').toHaveCount(0);
  await managerCtx.close();
});
