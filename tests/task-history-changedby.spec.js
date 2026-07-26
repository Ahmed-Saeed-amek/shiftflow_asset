// @ts-check
// Verifies the task history modal shows who changed a task's status.
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

test('task history modal shows changedBy for a known task', async ({ page }) => {
  await login(page, ADMIN);

  // DailyGroupShiftId 32314 has known ShiftTaskCompletions rows (task 5029)
  // with users "System Administrator" and "Khalid Al-Mutairi" (confirmed via SQL).
  const apiRes = await page.request.get(`${BASE_URL}/ShiftOps/TaskHistory?taskId=5029`);
  expect(apiRes.ok()).toBeTruthy();
  const history = await apiRes.json();
  console.log('API response:', JSON.stringify(history));

  expect(history.length).toBeGreaterThan(0);
  for (const h of history) {
    expect(h.changedBy).toBeTruthy();
  }
  const names = history.map(h => h.changedBy);
  expect(names).toContain('System Administrator');
  expect(names).toContain('Khalid Al-Mutairi');
});
