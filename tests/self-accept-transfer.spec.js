// @ts-check
// Verifies an engineer cannot approve their own self-submitted transfer request.
const { test, expect } = require('@playwright/test');

const BASE_URL = 'https://localhost:55248';
const ENGINEER = { email: 'engineer@shiftflow.com', password: 'Engineer@123456' };

async function login(page, { email, password }) {
  await page.goto(`${BASE_URL}/Account/Login`);
  await page.fill('input[name="Email"]', email);
  await page.fill('input[name="Password"]', password);
  await page.click('form:has(input[name="Password"]) button[type="submit"]');
  await page.waitForLoadState('domcontentloaded');
}

test('engineer cannot accept/decline their own self-submitted transfer request', async ({ page }) => {
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
  await expect(modal).toBeVisible({ timeout: 5000 });

  // Fill and submit the form for a temp transfer (default selected)
  await modal.locator('select[name="DailyGroupShiftId"]').selectOption({ index: 1 });
  await modal.locator('select[name="TargetGroupId"]').selectOption({ index: 1 });
  await modal.locator('textarea[name="Reason"]').fill('Automated self-accept regression test');
  await modal.locator('button[type="submit"]').click();
  await page.waitForLoadState('domcontentloaded');

  // Should land back on MyRequests with a success message
  expect(page.url()).toContain('/ChangeRequests/MyRequests');

  // The "Pending Transfer Requests" card (with Accept/Decline) must NOT show this
  // self-submitted request — that card is only for manager-initiated transfers.
  const pendingTransferCard = page.locator('.card.border-info:has-text("Pending Transfer Requests")');
  const acceptButtonInCard = pendingTransferCard.locator('button:has-text("Accept")');
  expect(await acceptButtonInCard.count(), 'Self-submitted transfer must not show an Accept button').toBe(0);

  // In the "All Requests" table, the newest Temp Transfer row (Pending status, top of
  // list since sorted by CreatedAt desc from the service) must show Cancel, never Accept.
  const row = page.locator('table tbody tr').filter({ hasText: 'Temp Transfer' }).filter({ hasText: 'Pending' }).first();
  await expect(row, 'Expected at least one Pending Temp Transfer row after submission').toHaveCount(1);

  const hasCancelBtn  = await row.locator('button:has-text("Cancel")').count() > 0;
  const hasAcceptForm = await row.locator('form[action*="AcceptTransfer"]').count() > 0;

  console.log('Row has Cancel button:', hasCancelBtn);
  console.log('Row has AcceptTransfer form (should be false):', hasAcceptForm);

  expect(hasCancelBtn, 'Self-submitted transfer row must show a Cancel button').toBe(true);
  expect(hasAcceptForm, 'Self-submitted transfer row must never expose an Accept action to the requester').toBe(false);
});
