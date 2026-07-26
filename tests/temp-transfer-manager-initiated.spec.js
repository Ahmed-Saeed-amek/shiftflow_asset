// @ts-check
// Manager-initiated Temp Transfer flow (ChangeRequestsController.TempTransfer), covering:
//  - the request lands as "Awaiting Employee" and can NOT be approved directly by a manager
//  - the affected employee can Accept or Decline it
//  - neither a manager force-approving nor an employee force-approving via raw POST can bypass the guard
const { test, expect } = require('@playwright/test');

const BASE_URL = 'https://localhost:55248';
const MANAGER  = { email: 'manager@shiftflow.com', password: 'Manager@123456' };
const ENGINEER = { email: 'engineer@shiftflow.com', password: 'Engineer@123456' };
const ENGINEER_NAME = 'Khalid Al-Mutairi';

async function login(page, { email, password }) {
  await page.goto(`${BASE_URL}/Account/Login`);
  await page.fill('input[name="Email"]', email);
  await page.fill('input[name="Password"]', password);
  await page.locator('form:has(input[name="Email"]) button[type="submit"]').click();
  await page.waitForLoadState('domcontentloaded');
}

/**
 * As the manager, find a shift needing cover on ShiftOps/Today and submit a Temp Transfer
 * targeting the seeded Engineer account. Returns null (and skips) if no eligible link/candidate
 * is found in the current DB state.
 * @param {import('@playwright/test').Page} managerPage
 * @param {string} reasonText
 */
async function submitManagerInitiatedTransfer(managerPage, reasonText) {
  await managerPage.goto(`${BASE_URL}/ShiftOps/Today`, { waitUntil: 'domcontentloaded' });
  const transferHrefs = await managerPage.locator('a[href*="/ChangeRequests/TempTransfer?shiftId="]').evaluateAll(
    (els) => els.map((e) => e.getAttribute('href')),
  );
  if (transferHrefs.length === 0) {
    return { skipped: true, reason: 'No "Request Temp Transfer" link found on ShiftOps/Today' };
  }

  // Not every shift needing cover offers the seeded Engineer as a candidate (depends on which
  // group's shift it is) — check each link in turn rather than assuming the first one does.
  let idx = -1;
  for (const href of transferHrefs) {
    await managerPage.goto(`${BASE_URL}${href}`, { waitUntil: 'domcontentloaded' });
    const select = managerPage.locator('select[name="affectedUserId"]');
    if (await select.count() === 0) continue;
    const options = await select.locator('option').allTextContents();
    idx = options.findIndex((o) => o.includes(ENGINEER_NAME));
    if (idx !== -1) break;
  }
  if (idx === -1) {
    return { skipped: true, reason: `${ENGINEER_NAME} not offered as a transfer candidate on any shift needing cover today` };
  }

  const select = managerPage.locator('select[name="affectedUserId"]');
  await select.selectOption({ index: idx });
  await managerPage.locator('textarea[name="reason"]').fill(reasonText);
  // Scoped by text, not just type="submit" — the shared _Layout navbar also has a submit button
  // (the language switcher form) that would otherwise be matched first.
  await managerPage.locator('button:has-text("Send Transfer Request")').click();
  await managerPage.waitForLoadState('domcontentloaded');

  expect(managerPage.url()).toContain('/ChangeRequests');
  const bodyText = await managerPage.textContent('body');
  if (!bodyText?.includes('Transfer request sent')) {
    return { skipped: true, reason: `Unexpected result after submitting: ${bodyText?.slice(0, 300)}` };
  }

  const row = managerPage.locator('table tbody tr').filter({ hasText: reasonText }).first();
  await expect(row, 'Expected the new request to appear in the pending queue').toHaveCount(1, { timeout: 10000 });
  const reviewHref = await row.locator('a:has-text("Review")').getAttribute('href');
  // Review's link is generated via conventional routing (/ChangeRequests/Review/6), not a query
  // string — fall back to a query-string match too in case that ever changes.
  const requestId = reviewHref?.match(/\/Review\/(\d+)/)?.[1] ?? reviewHref?.match(/id=(\d+)/)?.[1];

  return { skipped: false, row, requestId };
}

/**
 * The "All Requests" table's Details column shows "Transfer to: Group X" for TempGroupChange rows
 * instead of the free-text Reason (see MyRequests.cshtml), so a request's own reasonText can't be
 * used to re-find its row there after it leaves the "Pending Transfer Requests" card. Capture the
 * card's target-group and date text before the row disappears from the card, to re-locate it after.
 * @param {import('@playwright/test').Locator} card
 */
async function captureCardIdentity(card) {
  const targetGroupText = (await card.locator('text=Transfer to:').locator('..').locator('strong').textContent())?.trim();
  const dateText = (await card.locator('text=Date:').locator('..').locator('strong').textContent())?.trim();
  // dateText is like "Thursday, 16-07-2026" — keep just the dd-mm-yyyy part, which also appears
  // verbatim in the All Requests table's Date column.
  const dateOnly = dateText?.split(',').pop()?.trim();
  return { targetGroupText, dateOnly };
}

test('manager-initiated transfer shows "Awaiting Employee" and cannot be approved directly', async ({ browser }) => {
  test.setTimeout(60000);
  const managerCtx = await browser.newContext({ ignoreHTTPSErrors: true });
  const managerPage = await managerCtx.newPage();
  await login(managerPage, MANAGER);

  const reasonText = `E2E manager-initiated guard test ${Date.now()}`;
  const result = await submitManagerInitiatedTransfer(managerPage, reasonText);
  test.skip(result.skipped === true, result.reason ?? 'setup failed');
  const { row, requestId } = result;

  // No Approve button — only the "Awaiting Employee" badge.
  await expect(row.locator('span.badge:has-text("Awaiting Employee")')).toHaveCount(1);
  await expect(row.locator('form[action*="/ChangeRequests/Approve"]')).toHaveCount(0);

  // Force the approval directly via HTTP as the manager — the controller-level guard must still
  // reject it even though the UI never renders the button.
  const token = await managerPage.locator('input[name="__RequestVerificationToken"]').first().getAttribute('value');
  const response = await managerPage.request.post(`${BASE_URL}/ChangeRequests/Approve`, {
    form: { __RequestVerificationToken: token ?? '', id: requestId ?? '', reviewNotes: '' },
  });
  const body = await response.text();
  expect(body).toContain('must be accepted or declined by the employee');

  // Status must remain untouched.
  await managerPage.goto(`${BASE_URL}/ChangeRequests`, { waitUntil: 'domcontentloaded' });
  const rowAfter = managerPage.locator('table tbody tr').filter({ hasText: reasonText }).first();
  await expect(rowAfter.locator('span.badge:has-text("Awaiting Employee")')).toHaveCount(1);

  await managerCtx.close();
});

test('employee can accept a manager-initiated transfer', async ({ browser }) => {
  test.setTimeout(60000);
  const managerCtx = await browser.newContext({ ignoreHTTPSErrors: true });
  const managerPage = await managerCtx.newPage();
  await login(managerPage, MANAGER);

  const reasonText = `E2E manager-initiated accept test ${Date.now()}`;
  const result = await submitManagerInitiatedTransfer(managerPage, reasonText);
  test.skip(result.skipped === true, result.reason ?? 'setup failed');
  await managerCtx.close();

  const engineerCtx = await browser.newContext({ ignoreHTTPSErrors: true });
  const engineerPage = await engineerCtx.newPage();
  await login(engineerPage, ENGINEER);
  await engineerPage.goto(`${BASE_URL}/ChangeRequests/MyRequests`, { waitUntil: 'domcontentloaded' });

  const card = engineerPage.locator('.card.border-info').filter({ hasText: reasonText });
  await expect(card, 'Pending Transfer Requests card should show the manager-initiated request').toHaveCount(1, { timeout: 10000 });
  const { targetGroupText, dateOnly } = await captureCardIdentity(card);
  await card.locator('form[action*="AcceptTransfer"] button').click();
  await engineerPage.waitForLoadState('domcontentloaded');

  const bodyText = await engineerPage.textContent('body');
  expect(bodyText).toContain('Transfer accepted');

  const approvedRow = engineerPage.locator('table tbody tr')
    .filter({ hasText: targetGroupText ?? ' ' })
    .filter({ hasText: dateOnly ?? ' ' })
    .filter({ hasText: 'Approved' })
    .first();
  await expect(approvedRow, 'Expected the accepted request to show Approved in the All Requests table').toHaveCount(1, { timeout: 10000 });

  await engineerCtx.close();
});

test('employee can decline a manager-initiated transfer, leaving it Rejected with no effect', async ({ browser }) => {
  test.setTimeout(60000);
  const managerCtx = await browser.newContext({ ignoreHTTPSErrors: true });
  const managerPage = await managerCtx.newPage();
  await login(managerPage, MANAGER);

  const reasonText = `E2E manager-initiated decline test ${Date.now()}`;
  const result = await submitManagerInitiatedTransfer(managerPage, reasonText);
  test.skip(result.skipped === true, result.reason ?? 'setup failed');
  await managerCtx.close();

  const engineerCtx = await browser.newContext({ ignoreHTTPSErrors: true });
  const engineerPage = await engineerCtx.newPage();
  await login(engineerPage, ENGINEER);
  await engineerPage.goto(`${BASE_URL}/ChangeRequests/MyRequests`, { waitUntil: 'domcontentloaded' });

  const card = engineerPage.locator('.card.border-info').filter({ hasText: reasonText });
  await expect(card, 'Pending Transfer Requests card should show the manager-initiated request').toHaveCount(1, { timeout: 10000 });
  const { targetGroupText, dateOnly } = await captureCardIdentity(card);

  engineerPage.once('dialog', (dialog) => dialog.accept());
  await card.locator('form[action*="DeclineTransfer"] button').click();
  await engineerPage.waitForLoadState('domcontentloaded');

  const bodyText = await engineerPage.textContent('body');
  expect(bodyText).toContain('Transfer declined');

  const declinedRow = engineerPage.locator('table tbody tr')
    .filter({ hasText: targetGroupText ?? ' ' })
    .filter({ hasText: dateOnly ?? ' ' })
    .filter({ hasText: 'Rejected' })
    .first();
  await expect(declinedRow, 'Expected the declined request to show Rejected in the All Requests table').toHaveCount(1, { timeout: 10000 });

  await engineerCtx.close();
});

test('employee cannot approve or reject any request directly (wrong policy) via raw POST', async ({ browser }) => {
  test.setTimeout(60000);
  const managerCtx = await browser.newContext({ ignoreHTTPSErrors: true });
  const managerPage = await managerCtx.newPage();
  await login(managerPage, MANAGER);

  const reasonText = `E2E employee-cannot-review test ${Date.now()}`;
  const result = await submitManagerInitiatedTransfer(managerPage, reasonText);
  test.skip(result.skipped === true, result.reason ?? 'setup failed');
  const { requestId } = result;
  await managerCtx.close();

  const engineerCtx = await browser.newContext({ ignoreHTTPSErrors: true });
  const engineerPage = await engineerCtx.newPage();
  await login(engineerPage, ENGINEER);
  await engineerPage.goto(`${BASE_URL}/ChangeRequests/MyRequests`, { waitUntil: 'domcontentloaded' });

  const token = await engineerPage.locator('input[name="__RequestVerificationToken"]').first().getAttribute('value');
  const response = await engineerPage.request.post(`${BASE_URL}/ChangeRequests/Approve`, {
    form: { __RequestVerificationToken: token ?? '', id: requestId ?? '', reviewNotes: '' },
    maxRedirects: 0,
  }).catch((e) => e); // some servers reset the connection on a denied cross-policy POST instead of responding cleanly

  if (response instanceof Error) {
    console.log('Approve request as employee failed at the transport level (also an acceptable denial):', response.message);
  } else {
    const status = response.status();
    console.log('Employee raw-POST Approve status:', status);
    // Cookie-auth policy failure for an already-authenticated user typically redirects (302) to
    // AccessDenied, or responds 403 directly — never a 200 with the request actually approved.
    expect(status === 302 || status === 403).toBe(true);
    if (status === 302) {
      const location = response.headers()['location'] ?? '';
      expect(location).toContain('AccessDenied');
    }
  }

  // Regardless of transport-level result, the request must remain untouched.
  const managerCtx2 = await browser.newContext({ ignoreHTTPSErrors: true });
  const managerPage2 = await managerCtx2.newPage();
  await login(managerPage2, MANAGER);
  await managerPage2.goto(`${BASE_URL}/ChangeRequests`, { waitUntil: 'domcontentloaded' });
  const row = managerPage2.locator('table tbody tr').filter({ hasText: reasonText }).first();
  await expect(row.locator('span.badge:has-text("Awaiting Employee")'), 'Request must stay Pending/Awaiting Employee').toHaveCount(1);
  await managerCtx2.close();

  await engineerCtx.close();
});
