// @ts-check
// Cross-cutting state-machine edge cases for ShiftChangeRequest (both TempGroupChange and, where
// applicable, any request type), exercised via raw authenticated POSTs alongside the UI so the
// underlying service guards are covered even when the UI already hides the offending button.
//
// Two real behaviors were discovered while writing these tests (not injected bugs — this is what
// the current code does) and are asserted/documented here rather than "fixed" quietly:
//   1. ChangeRequestsController.Reject has no try/catch (unlike Approve/Cancel), so rejecting an
//      already-non-Pending request throws uncaught -> the request fails at the transport level
//      instead of showing a friendly TempData error.
//   2. ShiftChangeRequestService.CancelAsync never checks that the caller is the original
//      requester — any account holding the "ChangeRequest.Submit" policy can cancel ANY pending
//      request by id, not just their own. The Cancel button is hidden for non-owners in the UI,
//      but nothing stops a direct POST.
const { test, expect } = require('@playwright/test');

const BASE_URL = 'https://localhost:55248';
const ENGINEER = { email: 'engineer@shiftflow.com', password: 'Engineer@123456' };
const MANAGER  = { email: 'manager@shiftflow.com', password: 'Manager@123456' };
const HR       = { email: 'hr@shiftflow.com', password: 'HR@123456' };

async function login(page, { email, password }) {
  await page.goto(`${BASE_URL}/Account/Login`);
  await page.fill('input[name="Email"]', email);
  await page.fill('input[name="Password"]', password);
  await page.locator('form:has(input[name="Email"]) button[type="submit"]').click();
  await page.waitForLoadState('domcontentloaded');
}

/**
 * As the engineer, self-submit a Temp Transfer via the MyRequests modal and return its request id.
 * @param {import('@playwright/test').Page} engineerPage
 * @param {string} reasonText
 */
async function submitSelfTempTransfer(engineerPage, reasonText) {
  await engineerPage.goto(`${BASE_URL}/ChangeRequests/MyRequests`, { waitUntil: 'domcontentloaded' });
  const enabledBtn = engineerPage.locator('button[data-bs-target="#transferModal"]:not([disabled])');
  if (await enabledBtn.count() === 0) {
    return { skipped: true, reason: 'No enabled Request Transfer button (no upcoming shifts / no other groups)' };
  }
  await enabledBtn.click();
  const modal = engineerPage.locator('#transferModal');
  await expect(modal).toBeVisible();
  await modal.locator('select[name="DailyGroupShiftId"]').selectOption({ index: 1 });
  await modal.locator('select[name="TargetGroupId"]').selectOption({ index: 1 });
  await modal.locator('textarea[name="Reason"]').fill(reasonText);
  await modal.locator('button[type="submit"]').click();
  await engineerPage.waitForLoadState('domcontentloaded');

  const bodyText = await engineerPage.textContent('body');
  if (!bodyText?.includes('Change request submitted')) {
    return { skipped: true, reason: `Submission did not succeed: ${bodyText?.slice(0, 300)}` };
  }
  // The "All Requests" table's Details column shows "Transfer to: Group X" for TempGroupChange
  // rows instead of the Reason (see MyRequests.cshtml), so reasonText can't be used to find this
  // row here. The table is sorted by CreatedAt desc and nothing else creates rows for this user
  // mid-test (workers=1), so the row we just created is the top one.
  const row = engineerPage.locator('table tbody tr').first();
  return { skipped: false, row, reasonText };
}

/** Resolve a request id from the manager's pending queue by matching the reason text — Index.cshtml
 * shows a dedicated, unconditional Reason column for every request type, unlike MyRequests.cshtml. */
async function findRequestIdByReason(managerPage, reasonText) {
  await managerPage.goto(`${BASE_URL}/ChangeRequests`, { waitUntil: 'domcontentloaded' });
  const row = managerPage.locator('table tbody tr').filter({ hasText: reasonText }).first();
  await expect(row).toHaveCount(1, { timeout: 10000 });
  const reviewHref = await row.locator('a:has-text("Review")').getAttribute('href');
  return reviewHref?.match(/\/Review\/(\d+)/)?.[1] ?? reviewHref?.match(/id=(\d+)/)?.[1];
}

test('double-approve fails with "Request is already Approved."', async ({ browser }) => {
  test.setTimeout(60000);
  const engineerCtx = await browser.newContext({ ignoreHTTPSErrors: true });
  const engineerPage = await engineerCtx.newPage();
  await login(engineerPage, ENGINEER);
  const reasonText = `E2E double-approve test ${Date.now()}`;
  const setup = await submitSelfTempTransfer(engineerPage, reasonText);
  test.skip(setup.skipped === true, setup.reason ?? 'setup failed');
  await engineerCtx.close();

  const managerCtx = await browser.newContext({ ignoreHTTPSErrors: true });
  const managerPage = await managerCtx.newPage();
  await login(managerPage, MANAGER);
  const requestId = await findRequestIdByReason(managerPage, reasonText);

  const row = managerPage.locator('table tbody tr').filter({ hasText: reasonText }).first();
  await row.locator('form[action*="/ChangeRequests/Approve"] button').click();
  await managerPage.waitForLoadState('domcontentloaded');
  expect(await managerPage.textContent('body')).toContain('Request approved and applied');

  const token = await managerPage.locator('input[name="__RequestVerificationToken"]').first().getAttribute('value');
  const response = await managerPage.request.post(`${BASE_URL}/ChangeRequests/Approve`, {
    form: { __RequestVerificationToken: token ?? '', id: requestId ?? '', reviewNotes: '' },
  });
  const body = await response.text();
  expect(body).toContain('Request is already Approved.');

  await managerCtx.close();
});

test('rejecting an already-Rejected request throws uncaught (Reject has no try/catch)', async ({ browser }) => {
  test.setTimeout(60000);
  const engineerCtx = await browser.newContext({ ignoreHTTPSErrors: true });
  const engineerPage = await engineerCtx.newPage();
  await login(engineerPage, ENGINEER);
  const reasonText = `E2E double-reject test ${Date.now()}`;
  const setup = await submitSelfTempTransfer(engineerPage, reasonText);
  test.skip(setup.skipped === true, setup.reason ?? 'setup failed');

  const managerCtx = await browser.newContext({ ignoreHTTPSErrors: true });
  const managerPage = await managerCtx.newPage();
  await login(managerPage, MANAGER);
  const requestId = await findRequestIdByReason(managerPage, reasonText);

  const row = managerPage.locator('table tbody tr').filter({ hasText: reasonText }).first();
  managerPage.once('dialog', (dialog) => dialog.accept());
  await row.locator('form[action*="/ChangeRequests/Reject"] button').click();
  await managerPage.waitForLoadState('domcontentloaded');
  expect(await managerPage.textContent('body')).toContain('Request rejected.');

  const token = await managerPage.locator('input[name="__RequestVerificationToken"]').first().getAttribute('value');
  const response = await managerPage.request.post(`${BASE_URL}/ChangeRequests/Reject`, {
    form: { __RequestVerificationToken: token ?? '', id: requestId ?? '', reviewNotes: 'second reject attempt' },
  });
  // RejectAsync throws InvalidOperationException("Request is already Rejected.") and the controller
  // action has no try/catch around it, so this surfaces as a server error rather than a friendly
  // TempData banner — documenting the actual (inconsistent) behavior rather than assuming otherwise.
  console.log('Second Reject attempt status:', response.status());
  expect(response.status()).toBeGreaterThanOrEqual(500);

  // Employee then tries to Cancel the already-Rejected request — this path DOES have a try/catch
  // and must fail cleanly.
  await managerCtx.close();
  await engineerPage.goto(`${BASE_URL}/ChangeRequests/MyRequests`, { waitUntil: 'domcontentloaded' });
  const empToken = await engineerPage.locator('input[name="__RequestVerificationToken"]').first().getAttribute('value');
  const cancelResponse = await engineerPage.request.post(`${BASE_URL}/ChangeRequests/Cancel`, {
    form: { __RequestVerificationToken: empToken ?? '', id: requestId ?? '' },
  });
  const cancelBody = await cancelResponse.text();
  expect(cancelBody).toContain('Only pending requests can be cancelled.');

  await engineerCtx.close();
});

test('a Submit-policy user can cancel a request they did not submit — CancelAsync has no ownership check', async ({ browser }) => {
  test.setTimeout(60000);
  const engineerCtx = await browser.newContext({ ignoreHTTPSErrors: true });
  const engineerPage = await engineerCtx.newPage();
  await login(engineerPage, ENGINEER);
  const reasonText = `E2E cancel-ownership-gap test ${Date.now()}`;
  const setup = await submitSelfTempTransfer(engineerPage, reasonText);
  test.skip(setup.skipped === true, setup.reason ?? 'setup failed');
  await engineerCtx.close();

  const managerCtx = await browser.newContext({ ignoreHTTPSErrors: true });
  const managerPage = await managerCtx.newPage();
  await login(managerPage, MANAGER);
  const requestId = await findRequestIdByReason(managerPage, reasonText);
  await managerCtx.close();

  // HR is not the requester and not the affected user for this request. If HR's role even holds
  // the ChangeRequest.Submit policy, confirm whether they can still cancel someone else's request.
  const hrCtx = await browser.newContext({ ignoreHTTPSErrors: true });
  const hrPage = await hrCtx.newPage();
  await login(hrPage, HR);
  await hrPage.goto(`${BASE_URL}/ChangeRequests/MyRequests`, { waitUntil: 'domcontentloaded' });
  if (hrPage.url().includes('AccessDenied') || hrPage.url().includes('Login')) {
    test.skip(true, 'HR role does not hold the ChangeRequest.Submit policy — cannot probe this gap with the seeded accounts');
  }

  const hrToken = await hrPage.locator('input[name="__RequestVerificationToken"]').first().getAttribute('value');
  test.skip(!hrToken, 'No antiforgery token available on HR\'s MyRequests page to drive the POST');

  const response = await hrPage.request.post(`${BASE_URL}/ChangeRequests/Cancel`, {
    form: { __RequestVerificationToken: hrToken ?? '', id: requestId ?? '' },
  });
  const body = await response.text();
  console.log('HR cancelling engineer\'s request — response mentions "Request cancelled":', body.includes('Request cancelled'));

  // Document actual behavior: this currently succeeds (no ownership check in CancelAsync).
  const managerCtx2 = await browser.newContext({ ignoreHTTPSErrors: true });
  const managerPage2 = await managerCtx2.newPage();
  await login(managerPage2, MANAGER);
  await managerPage2.goto(`${BASE_URL}/ChangeRequests`, { waitUntil: 'domcontentloaded' });
  const stillPending = managerPage2.locator('table tbody tr').filter({ hasText: reasonText });
  const stillPendingCount = await stillPending.count();
  console.log('Request still in pending queue after HR\'s cancel attempt:', stillPendingCount > 0);
  // Whatever the current behavior is, assert it explicitly so a future ownership-check fix shows
  // up here as a deliberate, visible test change rather than a silent regression either way.
  expect(stillPendingCount).toBe(0);

  await managerCtx2.close();
  await hrCtx.close();
});

test('an employee cannot Accept or Decline their own self-submitted transfer', async ({ page }) => {
  test.setTimeout(60000);
  await login(page, ENGINEER);
  const reasonText = `E2E self-accept-decline-guard test ${Date.now()}`;
  const setup = await submitSelfTempTransfer(page, reasonText);
  test.skip(setup.skipped === true, setup.reason ?? 'setup failed');

  // Newest row (top of the CreatedAt-desc "All Requests" table) is the one we just submitted —
  // reasonText itself isn't rendered for TempGroupChange rows here, see submitSelfTempTransfer.
  const row = page.locator('table tbody tr').first();
  // The requester's own row must never expose Accept/Decline — those are only for manager-initiated
  // transfers where AffectedUserId == me && RequestedByUserId != me.
  await expect(row.locator('form[action*="AcceptTransfer"]')).toHaveCount(0);
  await expect(row.locator('form[action*="DeclineTransfer"]')).toHaveCount(0);

  // Confirm the service-level guard too, via a direct POST (bypassing the UI's own omission of the
  // button) using an id resolved from the manager's queue.
  const managerCtx = await page.context().browser()?.newContext({ ignoreHTTPSErrors: true });
  if (!managerCtx) { test.skip(true, 'Could not open a second browser context'); return; }
  const managerPage = await managerCtx.newPage();
  await login(managerPage, MANAGER);
  const requestId = await findRequestIdByReason(managerPage, reasonText);
  await managerCtx.close();

  const token = await page.locator('input[name="__RequestVerificationToken"]').first().getAttribute('value');
  const acceptResponse = await page.request.post(`${BASE_URL}/ChangeRequests/AcceptTransfer`, {
    form: { __RequestVerificationToken: token ?? '', id: requestId ?? '' },
  });
  expect(await acceptResponse.text()).toContain('You cannot accept your own request');

  const declineResponse = await page.request.post(`${BASE_URL}/ChangeRequests/DeclineTransfer`, {
    form: { __RequestVerificationToken: token ?? '', id: requestId ?? '' },
  });
  expect(await declineResponse.text()).toContain('You cannot decline your own request');
});
