// @ts-check
const { test, expect } = require('@playwright/test');

const BASE_URL = 'https://localhost:55248';
const ENGINEER = { email: 'engineer@shiftflow.com', password: 'Engineer@123456' };

async function login(page, { email, password }) {
  await page.goto(`${BASE_URL}/Account/Login`);
  await page.fill('input[name="Email"]', email);
  await page.fill('input[name="Password"]', password);
  await page.click('form:has(input[name="Password"]) button[type="submit"]');
  // domcontentloaded is enough — networkidle hangs on pages with chat widgets / SignalR
  await page.waitForLoadState('domcontentloaded');
}

test.describe('Engineer view', () => {
  test.beforeEach(async ({ page }) => {
    await login(page, ENGINEER);
  });

  test('engineer can access Live Shift Dashboard', async ({ page }) => {
    await page.goto(`${BASE_URL}/ShiftOps/Today`);
    await page.waitForLoadState('domcontentloaded');

    expect(page.url()).not.toContain('AccessDenied');
    const bodyText = await page.textContent('body');
    expect(bodyText).not.toContain('Access Denied');
  });

  test('live shift dashboard renders correctly for engineer', async ({ page }) => {
    await page.goto(`${BASE_URL}/ShiftOps/Today`);
    // Wait for a reliable element rather than networkidle — SignalR/chat keeps network busy
    await expect(page.locator('h1, h4').first()).toBeVisible({ timeout: 15000 });

    expect(page.url()).not.toContain('AccessDenied');

    // Either shifts are shown or the "no shifts" messages — both are valid
    const cardCount   = await page.locator('.card').count();
    const noShiftsMsg = page.locator('text=No published shifts found');
    const notAssigned = page.locator('text=not assigned to any shift');

    const valid = cardCount > 0
      || await noShiftsMsg.isVisible()
      || await notAssigned.isVisible();

    expect(valid).toBeTruthy();
    console.log('Live Shift Dashboard OK. Cards:', cardCount);
  });

  test('My Requests page is accessible and shows Request Transfer button', async ({ page }) => {
    await page.goto(`${BASE_URL}/ChangeRequests/MyRequests`);
    await page.waitForLoadState('domcontentloaded');
    expect(page.url()).not.toContain('AccessDenied');

    // Both the enabled modal-trigger button and the disabled fallback are acceptable
    const transferBtn = page.locator('button:has-text("Request Transfer")');
    await expect(transferBtn.first()).toBeVisible();
  });

  test('Request Transfer opens modal when shifts and groups are available', async ({ page }) => {
    await page.goto(`${BASE_URL}/ChangeRequests/MyRequests`);
    await page.waitForLoadState('domcontentloaded');

    // The enabled button has data-bs-toggle="modal"; disabled button has [disabled]
    const enabledBtn = page.locator('button[data-bs-target="#transferModal"]:not([disabled])');
    const hasEnabled = await enabledBtn.count() > 0;

    if (!hasEnabled) {
      console.log('Request Transfer button is disabled (no upcoming shifts or no other groups) — skipping modal test');
      return;
    }

    await enabledBtn.click();

    // Modal should appear
    const modal = page.locator('#transferModal');
    await expect(modal).toBeVisible({ timeout: 5000 });

    // Shift selector must be present
    await expect(modal.locator('select[name="DailyGroupShiftId"]')).toBeVisible();

    // Both transfer-type radios must be present
    await expect(modal.locator('#typeTemp')).toBeAttached();
    await expect(modal.locator('#typePerm')).toBeAttached();

    // Permanent warning is hidden by default
    const permWarning = modal.locator('#permWarning');
    await expect(permWarning).toHaveClass(/d-none/);

    // Switch to Permanent — warning should appear
    await modal.locator('label[for="typePerm"]').click();
    await expect(permWarning).not.toHaveClass(/d-none/);

    // Target group selector must be present
    await expect(modal.locator('select[name="TargetGroupId"]')).toBeVisible();

    console.log('Transfer modal opened and Temp/Permanent toggle works');
  });

  test('engineer cannot access ShiftMaker (shift planner)', async ({ page }) => {
    await page.goto(`${BASE_URL}/ShiftMaker`);
    await page.waitForLoadState('domcontentloaded');

    const blocked = page.url().includes('AccessDenied') || page.url().includes('Login');
    if (!blocked) {
      console.warn('⚠ Engineer can still access ShiftMaker — restart the app to apply the [Authorize(Roles)] fix');
    }
    expect(blocked, 'Engineer should not access ShiftMaker').toBeTruthy();
  });

  test('Go to Dashboard button on AccessDenied goes to MyHome for engineer', async ({ page }) => {
    await page.goto(`${BASE_URL}/ShiftMaker`);
    await page.waitForLoadState('domcontentloaded');

    if (page.url().includes('AccessDenied')) {
      const btn = page.locator('a:has-text("Go to Dashboard"), a:has-text("Dashboard")');
      const href = await btn.getAttribute('href');
      expect(href).toContain('/MyHome');
    }
  });
});
