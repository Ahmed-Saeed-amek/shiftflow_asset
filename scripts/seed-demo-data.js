// Demo-data seeding script — drives the real UI end-to-end (not direct DB
// inserts) so everything goes through the app's actual validation and
// business logic, the same way a human admin would.
//
// Flow: create a Work Area -> add 2 employees to its "A" group -> create a
// 5-day rotation schedule -> publish it -> find today's shift for group A
// -> activate it -> add tasks -> mark attendance -> log an incident ->
// close the shift (submits the shift report and hands over open tasks).
//
// Usage:
//   1. Make sure the app is running: dotnet run --project ShiftFlow.Web --no-build
//   2. node scripts/seed-demo-data.js
//   (or: npm run seed:demo)
//
// Not run automatically — this is a reusable tool, run it whenever you need
// fresh demo data. Safe to re-run: the work area / schedule names get a
// timestamp suffix so repeated runs don't collide.

const { chromium } = require('playwright');

const BASE_URL = 'https://localhost:55248';
const ADMIN = { email: 'admin@shiftflow.com', password: 'Admin@123456' };
// Real seeded accounts (see DbSeeder.cs) — reused here rather than creating
// new users, so this script has no dependency on the Users feature working.
const EMPLOYEES = [
    { email: 'engineer@shiftflow.com', password: 'Engineer@123456' },
    { email: 'manager@shiftflow.com', password: 'Manager@123456' },
];

const RUN_TAG = new Date().toISOString().replace(/[:.]/g, '-');
const WORK_AREA_NAME = `Demo Area ${RUN_TAG}`;
const SCHEDULE_NAME = `Demo Schedule ${RUN_TAG}`;

function log(step, msg) {
    console.log(`[${step}] ${msg}`);
}

async function loginAsAdmin(page) {
    await page.goto(`${BASE_URL}/Account/Login`);
    await page.fill('input[name="Email"]', ADMIN.email);
    await page.fill('input[name="Password"]', ADMIN.password);
    await page.click('form:has(input[name="Email"]) button[type="submit"]');
    await page.waitForLoadState('networkidle');
    log('login', `signed in as ${ADMIN.email}`);
}

// Creates a work area (auto-seeded with 5 fixed groups: A, B, C, D, F).
async function createWorkArea(page, name) {
    await page.goto(`${BASE_URL}/ShiftMaker/Areas`);
    await page.fill('input[name="name"]', name);
    await page.fill('input[name="description"]', 'Created by seed-demo-data.js');
    await page.click('.card-body form button[type="submit"]:has-text("Create Work Area")');
    await page.waitForLoadState('networkidle');
    log('work-area', `created "${name}"`);
}

// Uses the _EmployeePicker typeahead (hidden UserId input + AJAX search) to
// assign an employee to a shift group via the Groups page's Assign modal.
// groupLabel is the group's display name, e.g. "A" — resolved against the
// group card that belongs to areaName (work areas share the same 5 group
// names, so scoping by area is required to hit the right one).
async function assignEmployeeToGroup(page, areaName, groupLabel, employeeEmail) {
    await page.goto(`${BASE_URL}/ShiftMaker/Groups`);

    // Find the "Assign" trigger button for <areaName>'s <groupLabel> group.
    // Groups.cshtml's exact card layout wasn't verified against a live page
    // for this script — if the site layout changes, adjust this selector.
    const groupId = await page.evaluate(({ areaName, groupLabel }) => {
        const triggers = [...document.querySelectorAll('[data-bs-target="#assignModal"]')];
        for (const btn of triggers) {
            const card = btn.closest('.card') || btn.closest('[data-area-name]');
            if (!card) continue;
            const text = card.textContent || '';
            if (text.includes(areaName) && new RegExp(`\\b${groupLabel}\\b`).test(text)) {
                return btn.getAttribute('data-group-id');
            }
        }
        return null;
    }, { areaName, groupLabel });

    if (!groupId) {
        throw new Error(`Could not find group "${groupLabel}" for area "${areaName}" on /ShiftMaker/Groups — check the page layout / selector in assignEmployeeToGroup().`);
    }

    await page.click(`[data-bs-target="#assignModal"][data-group-id="${groupId}"]`);
    await page.waitForSelector('#assignModal.show');

    await page.fill('#assignModal input[data-ep-search]', employeeEmail);
    await page.waitForSelector('#assignModal [data-ep-results] .list-group-item-action');
    await page.click(`#assignModal [data-ep-results] .list-group-item-action:has-text("${employeeEmail}")`);

    // EffectiveFrom defaults to today; leave as-is.
    await page.click('#assignModal .modal-footer button[type="submit"]:has-text("Assign")');
    await page.waitForLoadState('networkidle');
    log('group-membership', `assigned ${employeeEmail} to group ${groupLabel} (${areaName})`);
}

// Creates a 5-day rotation schedule for the work area, cycling through its
// 5 groups (A/B/C/D/F) across Morning/Evening/Night each day. Create's POST
// action auto-generates assignments — no separate Planner/Generate step.
async function createSchedule(page, areaName, scheduleName, startDate) {
    await page.goto(`${BASE_URL}/ShiftMaker/Create`);
    await page.selectOption('select[name="WorkAreaId"]', { label: areaName });
    await page.fill('input[name="Name"]', scheduleName);
    await page.fill('input[name="StartDate"]', startDate);

    const groups = ['A', 'B', 'C', 'D', 'F'];
    for (let day = 0; day < 5; day++) {
        // Rotate which group covers which shift each day so no group repeats
        // within a day (the form blocks duplicate group selections per row).
        const morning = groups[day % 5];
        const evening = groups[(day + 1) % 5];
        const night = groups[(day + 2) % 5];
        await page.selectOption(`select[name="RotationDays[${day}].MorningGroup"]`, morning);
        await page.selectOption(`select[name="RotationDays[${day}].EveningGroup"]`, evening);
        await page.selectOption(`select[name="RotationDays[${day}].NightGroup"]`, night);
    }

    await page.click('#createForm button[type="submit"]:has-text("Create")');
    await page.waitForLoadState('networkidle');

    const scheduleId = new URL(page.url()).pathname.split('/').filter(Boolean).pop();
    log('schedule', `created "${scheduleName}" (id ${scheduleId}), redirected to ${page.url()}`);
    return scheduleId;
}

// Publish has a client-side confirm() dialog — must be handled or Playwright hangs.
async function publishSchedule(page, scheduleId) {
    page.once('dialog', d => d.accept());
    await page.goto(`${BASE_URL}/ShiftMaker/Details/${scheduleId}`);
    await page.click('form[action*="Publish"] button:has-text("Publish")');
    await page.waitForLoadState('networkidle');
    log('publish', `published schedule ${scheduleId}`);
}

// /ShiftOps/Today exposes a "Runbook" link (href has the DailyGroupShift id)
// for every group's current shift, regardless of Draft/Active status.
async function findShiftId(page, groupLabel, date) {
    await page.goto(`${BASE_URL}/ShiftOps/Today?showAll=true&date=${date}`);
    const href = await page.evaluate((groupLabel) => {
        const rows = [...document.querySelectorAll('*')].filter(el =>
            el.textContent?.trim() === groupLabel && el.tagName !== 'A');
        for (const row of rows) {
            const container = row.closest('tr') || row.closest('.card') || row.parentElement;
            const link = container?.querySelector('a[href*="/ShiftOps/Shift/"]');
            if (link) return link.getAttribute('href');
        }
        return null;
    }, groupLabel);

    if (!href) {
        throw new Error(`Could not find a Runbook link for group "${groupLabel}" on /ShiftOps/Today?date=${date} — check the page layout / selector in findShiftId().`);
    }
    const shiftId = href.split('/').filter(Boolean).pop();
    log('find-shift', `group ${groupLabel} on ${date} -> shift id ${shiftId}`);
    return shiftId;
}

async function activateShiftIfDraft(page, shiftId) {
    await page.goto(`${BASE_URL}/ShiftOps/Shift/${shiftId}`);
    const activateBtn = page.locator('form[action*="Activate"] button:has-text("Activate")');
    if (await activateBtn.count() > 0) {
        await activateBtn.click();
        await page.waitForLoadState('networkidle');
        log('activate', `activated shift ${shiftId}`);
    } else {
        log('activate', `shift ${shiftId} already active (or Activate not available) — skipping`);
    }
}

// AddTask is AJAX (no <form> submit / page navigation).
async function addTask(page, title, description, mandatory = false) {
    await page.fill('#taskTitle', title);
    await page.fill('#taskDesc', description);
    if (mandatory) await page.check('#taskMandatory');
    await page.click('#addTaskBtn');
    await page.waitForSelector(`#taskList .task-row:has-text("${title}")`);
    log('add-task', `added task "${title}"`);
}

// Marks every visible roster member "Present" (only rendered once the shift is Active).
async function markAllPresent(page) {
    const presentButtons = page.locator('form:has(input[name="status"][value="Present"]) button[type="submit"]');
    const count = await presentButtons.count();
    for (let i = 0; i < count; i++) {
        await presentButtons.nth(0).click(); // list shrinks/reorders after each submit — always take index 0
        await page.waitForLoadState('networkidle');
    }
    log('attendance', `marked ${count} roster member(s) Present`);
}

// AddIncident is also AJAX.
async function addIncident(page, title, description, severity = 'Medium') {
    await page.goto(page.url().split('?')[0] + '?tab=incidents');
    await page.fill('#incTitle', title);
    await page.fill('#incDesc', description);
    await page.selectOption('#incSeverity', severity);
    await page.click('#addIncidentBtn');
    await page.waitForTimeout(500); // AJAX, no navigation to wait on
    log('incident', `logged "${title}" (${severity})`);
}

// CloseShift has a client-side confirm() dialog too, plus a required summary field.
async function closeShift(page, shiftId, summary) {
    await page.goto(`${BASE_URL}/ShiftOps/Shift/${shiftId}?tab=close`);
    await page.fill('textarea[name="summary"]', summary);
    // Leaves carry-over mode on its default ("auto") — open tasks hand over
    // to the next shift automatically.
    page.once('dialog', d => d.accept());
    await page.click('button[type="submit"]:has-text("Close Shift")');
    await page.waitForLoadState('networkidle');
    log('close-shift', `closed shift ${shiftId} and submitted the report`);
}

async function main() {
    const browser = await chromium.launch({ headless: false });
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
    const page = await ctx.newPage();

    try {
        await loginAsAdmin(page);
        await createWorkArea(page, WORK_AREA_NAME);

        for (const emp of EMPLOYEES) {
            await assignEmployeeToGroup(page, WORK_AREA_NAME, 'A', emp.email);
        }

        const today = new Date().toISOString().slice(0, 10);
        const scheduleId = await createSchedule(page, WORK_AREA_NAME, SCHEDULE_NAME, today);
        await publishSchedule(page, scheduleId);

        const shiftId = await findShiftId(page, 'A', today);
        await activateShiftIfDraft(page, shiftId);

        await addTask(page, 'Check coolant levels', 'Routine equipment check', true);
        await addTask(page, 'Update shift log', 'End-of-shift documentation');

        await markAllPresent(page);
        await addIncident(page, 'Minor voltage fluctuation', 'Observed brief fluctuation on Panel 3, self-resolved', 'Low');

        await closeShift(page, shiftId, 'Routine shift, all tasks handed over except log update. No major incidents.');

        console.log('\nDone. Seeded:');
        console.log(`  Work Area:  ${WORK_AREA_NAME}`);
        console.log(`  Schedule:   ${SCHEDULE_NAME} (id ${scheduleId})`);
        console.log(`  Shift:      id ${shiftId} (closed, report submitted)`);
    } finally {
        await browser.close();
    }
}

main().catch(e => { console.error('FATAL', e); process.exit(1); });
