// @ts-check
const { test, expect } = require('@playwright/test');
const { execSync } = require('node:child_process');

const BASE_URL = 'https://localhost:55248';
const ADMIN = { email: 'admin@shiftflow.com', password: 'Admin@123456' };

// AddTask/AddInspectionTask/UpdateInspectionItem all require Status=Active — resolve whichever
// shift is currently Active at runtime instead of hardcoding an id (ids shift whenever the dev
// DB is dropped/recreated or schedules are regenerated). Falls back to activating the earliest
// Draft shift if nothing is Active yet.
let ACTIVE_SHIFT_ID;
test.beforeAll(() => {
  const sql = (query) => execSync(
    `sqlcmd -S "(localdb)\\MSSQLLocalDB" -d ShiftFlowAssetsDB -Q "${query}" -h -1 -W`,
    { encoding: 'utf8' },
  ).trim();
  let id = sql('SET NOCOUNT ON; SELECT TOP 1 Id FROM DailyGroupShifts WHERE Status=\'Active\';');
  if (!id) {
    id = sql('SET NOCOUNT ON; SELECT TOP 1 Id FROM DailyGroupShifts WHERE Status=\'Draft\' ORDER BY Id;');
    if (id) sql(`UPDATE DailyGroupShifts SET Status='Active' WHERE Id=${id};`);
  }
  ACTIVE_SHIFT_ID = Number(id);
});

async function login(page, { email, password }) {
  await page.goto(`${BASE_URL}/Account/Login`);
  await page.fill('input[name="Email"]', email);
  await page.fill('input[name="Password"]', password);
  await page.click('form:has(input[name="Password"]) button[type="submit"]');
  await page.waitForLoadState('domcontentloaded');
}

test.describe('Inspection tasks in Shift Operations', () => {
  test.beforeEach(async ({ page }) => await login(page, ADMIN));

  test('Tasks tab shows the Regular/Inspect toggle and zone/asset target controls', async ({ page }) => {
    await page.goto(`${BASE_URL}/ShiftOps/Shift?id=${ACTIVE_SHIFT_ID}&tab=tasks`);
    await page.waitForLoadState('domcontentloaded');
    expect(page.url()).not.toContain('AccessDenied');

    await expect(page.locator('#taskTypeRegular')).toBeAttached();
    await expect(page.locator('#taskTypeInspect')).toBeAttached();
    await expect(page.locator('#inspectTargetRow')).toHaveClass(/d-none/);

    await page.locator('label[for="taskTypeInspect"]').click();
    await expect(page.locator('#inspectTargetRow')).not.toHaveClass(/d-none/);
    await expect(page.locator('#inspectZone')).toBeVisible();
  });

  test('Creating a Zone-based inspection task builds a checklist with every asset in that zone', async ({ page }) => {
    await page.goto(`${BASE_URL}/ShiftOps/Shift?id=${ACTIVE_SHIFT_ID}&tab=tasks`);
    await page.waitForLoadState('domcontentloaded');

    await page.locator('label[for="taskTypeInspect"]').click();
    await page.fill('#taskTitle', 'PW Zone Inspection ' + Date.now());
    const zoneSelect = page.locator('#inspectZone');
    const zoneOptions = await zoneSelect.locator('option').allTextContents();
    const blockZoneIndex = zoneOptions.findIndex(o => o.includes('Block 3 Substation'));
    test.skip(blockZoneIndex < 0, 'Block 3 Substation zone not present');
    await zoneSelect.selectOption({ label: zoneOptions[blockZoneIndex] });

    await page.click('#addTaskBtn');
    // Inspect-task creation redirects straight to its checklist page.
    await page.waitForURL(/InspectionChecklist/, { timeout: 10000 });

    const rowCount = await page.locator('table tbody tr[data-item-id]').count();
    expect(rowCount).toBeGreaterThanOrEqual(2); // AST-0001 and AST-0002 both live in Block 3 Substation
    await expect(page.locator('body')).toContainText('AST-0001');
    await expect(page.locator('body')).toContainText('AST-0002');
  });

  test('Marking an item OK saves without requiring Action Type/Cause', async ({ page }) => {
    await page.goto(`${BASE_URL}/ShiftOps/Shift?id=${ACTIVE_SHIFT_ID}&tab=tasks`);
    await page.waitForLoadState('domcontentloaded');
    await page.locator('label[for="taskTypeInspect"]').click();
    await page.fill('#taskTitle', 'PW OK Test ' + Date.now());
    const zoneSelect = page.locator('#inspectZone');
    const zoneOptions = await zoneSelect.locator('option').allTextContents();
    const idx = zoneOptions.findIndex(o => o.includes('Block 3 Substation'));
    test.skip(idx < 0, 'Zone not present');
    await zoneSelect.selectOption({ label: zoneOptions[idx] });
    await page.click('#addTaskBtn');
    await page.waitForURL(/InspectionChecklist/, { timeout: 10000 });

    const firstRow = page.locator('table tbody tr[data-item-id]').first();
    await firstRow.locator('button:has-text("OK")').click();

    const itemId = await firstRow.getAttribute('data-item-id');
    const formRow = page.locator(`tr.inline-form-row[data-form-for="${itemId}"]`);
    await expect(formRow).toBeVisible();
    // No defect-only fields should be required/visible for an OK outcome.
    await expect(formRow.locator('.defect-only').first()).toHaveClass(/d-none/);

    await formRow.locator('.save-outcome-btn').click();
    await page.waitForLoadState('domcontentloaded');

    // Row now shows the OK badge instead of the outcome buttons.
    const updatedRow = page.locator(`tr[data-item-id="${itemId}"]`);
    await expect(updatedRow.locator('.outcome-cell')).toContainText('OK');
  });

  test('Marking an item Defective requires Action Type and Cause, then auto-creates a work order', async ({ page }) => {
    await page.goto(`${BASE_URL}/ShiftOps/Shift?id=${ACTIVE_SHIFT_ID}&tab=tasks`);
    await page.waitForLoadState('domcontentloaded');
    await page.locator('label[for="taskTypeInspect"]').click();
    await page.fill('#taskTitle', 'PW Defective Test ' + Date.now());

    // Hand-pick AST-0002 specifically via the multi-asset picker instead of a zone.
    const assetSearch = page.locator('#inspectAssetsCol [data-search]');
    await assetSearch.fill('AST-0002');
    await page.waitForTimeout(400);
    const result = page.locator('#inspectAssetsCol [data-results] .list-group-item', { hasText: 'AST-0002' }).first();
    const hasResult = await result.count() > 0;
    test.skip(!hasResult, 'AST-0002 not found in picker results');
    await result.click();

    await page.click('#addTaskBtn');
    await page.waitForURL(/InspectionChecklist/, { timeout: 10000 });

    const row = page.locator('table tbody tr[data-item-id]').first();
    await row.locator('button:has-text("Defective")').click();

    const itemId = await row.getAttribute('data-item-id');
    const formRow = page.locator(`tr.inline-form-row[data-form-for="${itemId}"]`);
    await expect(formRow.locator('.defect-only').first()).not.toHaveClass(/d-none/);

    // Try saving with no Action Type/Cause selected — must be rejected client-side.
    await formRow.locator('.save-outcome-btn').click();
    await expect(page.locator('#inspectAlert')).toContainText('Action Type and Cause are required');

    // Now fill both and save for real.
    await page.waitForFunction(() => {
      const sel = document.querySelector('tr.inline-form-row:not(.d-none) .action-type-select');
      return sel && sel.options.length > 1;
    });
    await formRow.locator('.action-type-select').selectOption({ index: 1 });
    await page.waitForFunction(() => {
      const sel = document.querySelector('tr.inline-form-row:not(.d-none) .cause-select');
      return sel && sel.options.length > 1;
    });
    await formRow.locator('.cause-select').selectOption({ index: 1 });
    await formRow.locator('.notes-input').fill('Playwright-reported defect');
    await formRow.locator('.save-outcome-btn').click();
    await page.waitForLoadState('domcontentloaded');

    // Task had only this one item, so it auto-completes to Done, and a work order link appears.
    await expect(page.locator('body')).toContainText('Work Order');
  });
});
