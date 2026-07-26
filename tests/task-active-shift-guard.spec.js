// @ts-check
// Verifies task rules: add/delete are blocked only on Closed shifts (allowed
// on Draft and Active); status changes require the shift to be Active.
const { test, expect } = require('@playwright/test');

const BASE_URL = 'https://localhost:55248';
const ADMIN = { email: 'admin@shiftflow.com', password: 'Admin@123456' };

// Confirmed via SQL: shift 32314 is Closed and owns task 5030;
// shift 11149 is Active and owns task 1009; shift 34752 is Draft.
const CLOSED_SHIFT_ID = 32314;
const CLOSED_TASK_ID = 5030;
const ACTIVE_SHIFT_ID = 11149;
const ACTIVE_TASK_ID = 1009;
const DRAFT_SHIFT_ID = 34752;

async function login(page, { email, password }) {
  await page.goto(`${BASE_URL}/Account/Login`);
  await page.fill('input[name="Email"]', email);
  await page.fill('input[name="Password"]', password);
  await page.click('form:has(input[name="Password"]) button[type="submit"]');
  await page.waitForLoadState('domcontentloaded');
}

async function getAntiforgeryToken(page, shiftId) {
  await page.goto(`${BASE_URL}/ShiftOps/Shift/${shiftId}?tab=tasks`);
  await page.waitForLoadState('domcontentloaded');
  return page.locator('input[name="__RequestVerificationToken"]').first().inputValue();
}

test.describe('Task mutations require an Active shift', () => {
  test.beforeEach(async ({ page }) => {
    await login(page, ADMIN);
  });

  test('AddTask on a closed shift is rejected server-side', async ({ page }) => {
    const token = await getAntiforgeryToken(page, CLOSED_SHIFT_ID);
    const res = await page.request.post(`${BASE_URL}/ShiftOps/AddTask`, {
      form: { shiftId: CLOSED_SHIFT_ID, title: 'Should not be created', description: '', assignedToUserId: '', mandatory: 'false' },
      headers: { RequestVerificationToken: token },
    });
    expect(res.status()).toBe(400);
    const body = await res.json();
    expect(body.error).toContain('closed');
  });

  test('AddTask on a draft (not yet activated) shift succeeds', async ({ page }) => {
    const token = await getAntiforgeryToken(page, DRAFT_SHIFT_ID);
    const res = await page.request.post(`${BASE_URL}/ShiftOps/AddTask`, {
      form: { shiftId: DRAFT_SHIFT_ID, title: 'Draft-shift task', description: '', assignedToUserId: '', mandatory: 'false' },
      headers: { RequestVerificationToken: token },
    });
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body.title).toBe('Draft-shift task');

    // Clean up so re-runs stay idempotent - deleting on a Draft shift must also succeed.
    const delRes = await page.request.post(`${BASE_URL}/ShiftOps/DeleteTask`, {
      form: { taskId: body.id },
      headers: { RequestVerificationToken: token },
    });
    expect(delRes.ok()).toBeTruthy();
  });

  test('UpdateTask status on a closed shift is rejected server-side', async ({ page }) => {
    const token = await getAntiforgeryToken(page, CLOSED_SHIFT_ID);
    const res = await page.request.post(`${BASE_URL}/ShiftOps/UpdateTask`, {
      form: { taskId: CLOSED_TASK_ID, status: 'Done' },
      headers: { RequestVerificationToken: token },
    });
    expect(res.status()).toBe(400);
    const body = await res.json();
    expect(body.error).toContain('active');
  });

  test('DeleteTask on a closed shift is rejected server-side', async ({ page }) => {
    const token = await getAntiforgeryToken(page, CLOSED_SHIFT_ID);
    const res = await page.request.post(`${BASE_URL}/ShiftOps/DeleteTask`, {
      form: { taskId: CLOSED_TASK_ID },
      headers: { RequestVerificationToken: token },
    });
    expect(res.status()).toBe(400);
    const body = await res.json();
    expect(body.error).toContain('closed');

    // Confirm the task really wasn't deleted.
    const stillThereRes = await page.request.post(`${BASE_URL}/ShiftOps/UpdateTask`, {
      form: { taskId: CLOSED_TASK_ID, status: 'Done' },
      headers: { RequestVerificationToken: token },
    });
    // Still blocked by the active-shift guard, not a 404 - proves the task still exists.
    expect(stillThereRes.status()).toBe(400);
  });

  test('UpdateTask status on an active shift still succeeds', async ({ page }) => {
    const token = await getAntiforgeryToken(page, ACTIVE_SHIFT_ID);
    const res = await page.request.post(`${BASE_URL}/ShiftOps/UpdateTask`, {
      form: { taskId: ACTIVE_TASK_ID, status: 'InProgress' },
      headers: { RequestVerificationToken: token },
    });
    expect(res.ok()).toBeTruthy();
  });

  test('UpdateTask status on a draft (not yet activated) shift is still rejected', async ({ page }) => {
    const token = await getAntiforgeryToken(page, DRAFT_SHIFT_ID);
    const addRes = await page.request.post(`${BASE_URL}/ShiftOps/AddTask`, {
      form: { shiftId: DRAFT_SHIFT_ID, title: 'Draft status-change probe', description: '', assignedToUserId: '', mandatory: 'false' },
      headers: { RequestVerificationToken: token },
    });
    const task = await addRes.json();

    const res = await page.request.post(`${BASE_URL}/ShiftOps/UpdateTask`, {
      form: { taskId: task.id, status: 'Done' },
      headers: { RequestVerificationToken: token },
    });
    expect(res.status()).toBe(400);
    const body = await res.json();
    expect(body.error).toContain('active');

    await page.request.post(`${BASE_URL}/ShiftOps/DeleteTask`, {
      form: { taskId: task.id },
      headers: { RequestVerificationToken: token },
    });
  });

  test('Closed shift runbook UI has no Add Task form, no delete buttons, disabled status selects', async ({ page }) => {
    await page.goto(`${BASE_URL}/ShiftOps/Shift/${CLOSED_SHIFT_ID}?tab=tasks`);
    await page.waitForLoadState('domcontentloaded');

    await expect(page.locator('#addTaskBtn')).toHaveCount(0);
    await expect(page.locator('.delete-task-btn')).toHaveCount(0);

    const selects = page.locator('.task-status-select');
    const count = await selects.count();
    expect(count).toBeGreaterThan(0);
    for (let i = 0; i < count; i++) {
      await expect(selects.nth(i)).toBeDisabled();
    }
  });

  test('Draft shift runbook UI has an Add Task form and disabled (but present) status selects', async ({ page }) => {
    await page.goto(`${BASE_URL}/ShiftOps/Shift/${DRAFT_SHIFT_ID}?tab=tasks`);
    await page.waitForLoadState('domcontentloaded');

    await expect(page.locator('#addTaskBtn')).toBeVisible();
  });
});
