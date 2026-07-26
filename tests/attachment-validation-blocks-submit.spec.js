// @ts-check
const { test, expect } = require('@playwright/test');
const path = require('node:path');
const fs = require('node:fs');
const os = require('node:os');

const BASE_URL = 'https://localhost:55248';
const VENDOR = { email: 'vendor@gulfhvac.kw', password: 'Vendor@123456' };

async function login(page, { email, password }) {
  await page.goto(`${BASE_URL}/Account/Login`);
  await page.fill('input[name="Email"]', email);
  await page.fill('input[name="Password"]', password);
  await page.click('form:has(input[name="Password"]) button[type="submit"]');
  await page.waitForLoadState('domcontentloaded');
}

test.describe('Vendor Fix attachment validation blocks the whole submission', () => {
  test('Submitting a Fix report with a rejected file type does not advance the work order', async ({ page, request }) => {
    await login(page, VENDOR);
    await page.goto(`${BASE_URL}/VendorPortal`);
    await page.waitForLoadState('domcontentloaded');

    const sentRow = page.locator('table tbody tr').filter({ hasText: 'Sent to Vendor' }).first();
    const hasSent = await sentRow.count() > 0;
    test.skip(!hasSent, 'No "Sent to Vendor" work order available for this vendor to test against');

    await sentRow.click();
    await page.waitForLoadState('domcontentloaded');
    const url = page.url();
    const idMatch = url.match(/Details\/(\d+)/);
    const workOrderId = idMatch ? idMatch[1] : null;
    expect(workOrderId).toBeTruthy();

    // Build a throwaway .sql file — an extension the allowlist rejects outright.
    const badFile = path.join(os.tmpdir(), `pw-bad-${Date.now()}.sql`);
    fs.writeFileSync(badFile, 'SELECT * FROM Users;');

    await page.fill('textarea[name="Description"]', 'Playwright attempted fix with a bad attachment');
    await page.setInputFiles('input[name="Files"]', badFile);
    await page.click('button:has-text("Submit Fix")');
    await page.waitForLoadState('domcontentloaded');

    await expect(page.locator('body')).toContainText('invalid attachment');

    // The work order must still be Sent to Vendor, not advanced to Fixed - Pending Confirmation.
    const cookies = await page.context().cookies();
    const cookieHeader = cookies.map(c => `${c.name}=${c.value}`).join('; ');
    const detailResp = await request.get(`${BASE_URL}/VendorPortal/Details/${workOrderId}`, { headers: { Cookie: cookieHeader } });
    const html = await detailResp.text();
    expect(html).toContain('Sent to Vendor');

    fs.unlinkSync(badFile);
  });
});
