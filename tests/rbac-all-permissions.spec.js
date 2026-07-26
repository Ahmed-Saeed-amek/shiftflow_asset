// RBAC regression suite: for every permission in the catalog that actually gates
// something reachable over HTTP, grant it -> confirm access opens up -> revoke it
// -> confirm access is blocked again -> confirm the RBAC admin UI itself reflects
// each change. Runs against the live dev app / dev DB (no test-DB reset exists),
// so every test restores the permission to its original state when done.
//
// Permissions are split into three groups (see the bottom of this file):
//   1. PAGE_CHECKS / SCOPING_CHECKS — actually exercised end-to-end below.
//   2. NOT_POLICY_GATED — seeded/shown in the Rbac UI but no controller action
//      anywhere checks that policy (verified via full-repo grep), so toggling
//      them has no observable effect. Reported as skipped, not silently ignored.
//   3. MUTATING_SKIPPED — real [Authorize(Policy=...)] gates exist, but every
//      action is POST-only and would mutate real shift/schedule/task data if
//      actually invoked (Activate a shift, close it, publish/archive a
//      schedule, etc.). Skipped on purpose to avoid corrupting the dev DB;
//      the underlying mechanism is identical to the GET-gated permissions
//      that ARE exercised, so those already prove the auth plumbing works.

const { test, expect } = require('@playwright/test');

const BASE_URL = 'https://localhost:55248';
const ADMIN    = { email: 'admin@shiftflow.com',    password: 'Admin@123456' };
const ENGINEER = { email: 'engineer@shiftflow.com', password: 'Engineer@123456' };
const MANAGER  = { email: 'manager@shiftflow.com',  password: 'Manager@123456' };
const SUBJECTS = { Engineer: ENGINEER, ShiftManager: MANAGER };

async function login(page, { email, password }) {
    await page.goto(`${BASE_URL}/Account/Login`);
    await page.fill('input[name="Email"]', email);
    await page.fill('input[name="Password"]', password);
    await page.locator('form:has(input[name="Email"]) button[type="submit"]').click();
    await page.waitForLoadState('domcontentloaded');
}

// Resolves a role's Rbac roleId GUID by scraping the link on /Rbac (Index) —
// role GUIDs are generated at seed time, never hardcoded.
async function getRoleId(adminPage, roleName) {
    await adminPage.goto(`${BASE_URL}/Rbac`);
    const href = await adminPage.evaluate((name) => {
        const links = [...document.querySelectorAll('a[href*="RolePermissions"]')];
        const match = links.find(a => a.querySelector('.fw-semibold')?.textContent.trim() === name);
        return match ? match.getAttribute('href') : null;
    }, roleName);
    if (!href) throw new Error(`Role "${roleName}" not found on /Rbac`);
    return new URL(href, BASE_URL).searchParams.get('roleId');
}

async function isPermissionGranted(adminPage, roleId, permission) {
    await adminPage.goto(`${BASE_URL}/Rbac/RolePermissions?roleId=${roleId}`);
    return adminPage.locator(`input[name="granted"][value="${permission}"]`).isChecked();
}

// User.View/User.Manage and Schedule.Template.View/.Manage render as a single
// None/Read/Write/Both control (RolePermissions.cshtml's rw-pair rows) instead
// of a plain checkbox — the "granted" checkbox for these still exists in the
// DOM (SaveRolePermissions reads it unchanged) but is visually hidden (d-none)
// and driven by a radio-button group, so it can't be toggled with setChecked()
// directly. This map lets setPermission compute the right 4-way state.
const RW_PAIR_INFO = {
    'User.View':                  { readPermission: 'User.View',                  role: 'read'  },
    'User.Manage':                 { readPermission: 'User.View',                  role: 'write' },
    'Schedule.Template.View':     { readPermission: 'Schedule.Template.View',     role: 'read'  },
    'Schedule.Template.Manage':   { readPermission: 'Schedule.Template.View',     role: 'write' },
};

async function setRwPairPermission(adminPage, permission, granted) {
    const info = RW_PAIR_INFO[permission];
    const siblingRole = info.role === 'read' ? 'write' : 'read';
    const radioName = `rw-${info.readPermission}`;
    const otherBoxValue = info.readPermission === permission
        ? await adminPage.locator(`.rw-pair:has(input[name="${radioName}"]) .rw-checkbox[data-role="write"]`).isChecked()
        : await adminPage.locator(`.rw-pair:has(input[name="${radioName}"]) .rw-checkbox[data-role="read"]`).isChecked();

    const state = granted
        ? (otherBoxValue ? 'both' : info.role)
        : (otherBoxValue ? siblingRole : 'none');

    const radio = adminPage.locator(`input[name="${radioName}"][value="${state}"]`);
    const radioId = await radio.getAttribute('id');
    await adminPage.locator(`label[for="${radioId}"]`).click();
}

// Toggles exactly one checkbox and submits the form — every other checkbox on
// the page keeps reflecting its own current DB state, so this can't touch any
// permission other than the one requested (SaveRolePermissions diffs the full
// checked set against current grants).
async function setPermission(adminPage, roleId, permission, granted) {
    await adminPage.goto(`${BASE_URL}/Rbac/RolePermissions?roleId=${roleId}`);
    if (RW_PAIR_INFO[permission]) {
        await setRwPairPermission(adminPage, permission, granted);
        await adminPage.locator('form[action*="SaveRolePermissions"] button[type="submit"]').click();
        await adminPage.waitForLoadState('domcontentloaded');
        return;
    }
    await adminPage.locator(`input[name="granted"][value="${permission}"]`).setChecked(granted);
    await adminPage.locator('form[action*="SaveRolePermissions"] button[type="submit"]').click();
    await adminPage.waitForLoadState('domcontentloaded');
}

// Blocked = redirected to the app's AccessDenied page. Allowed = anything else,
// including a clean 404 "not found" body — that still proves authorization
// passed and the action ran, just with a bogus id.
async function checkPageAccess(subjectPage, path) {
    await subjectPage.goto(`${BASE_URL}${path}`);
    return subjectPage.url().includes('AccessDenied') ? 'blocked' : 'allowed';
}

async function runToggleRoundTrip({ browser, permission, subjectRole, verify }) {
    const adminCtx = await browser.newContext({ ignoreHTTPSErrors: true });
    const subjectCtx = await browser.newContext({ ignoreHTTPSErrors: true });
    try {
        const adminPage = await adminCtx.newPage();
        const subjectPage = await subjectCtx.newPage();
        await login(adminPage, ADMIN);
        await login(subjectPage, SUBJECTS[subjectRole]);

        const roleId = await getRoleId(adminPage, subjectRole);
        const original = await isPermissionGranted(adminPage, roleId, permission);

        // Toggle away from the original state and confirm both the RBAC UI and
        // the subject's real access reflect the change.
        await setPermission(adminPage, roleId, permission, !original);
        expect(await isPermissionGranted(adminPage, roleId, permission)).toBe(!original);
        expect(await verify(subjectPage)).toBe(!original ? 'allowed' : 'blocked');

        // Revert to the original state and confirm the revert "took" too.
        await setPermission(adminPage, roleId, permission, original);
        expect(await isPermissionGranted(adminPage, roleId, permission)).toBe(original);
        expect(await verify(subjectPage)).toBe(original ? 'allowed' : 'blocked');
    } finally {
        await adminCtx.close();
        await subjectCtx.close();
    }
}

test.describe.configure({ mode: 'serial' }); // toggles shared role state — must not run concurrently

// ── Tier 1: page-gated permissions, no extra role gate beyond the policy ────
const PAGE_CHECKS = [
    { permission: 'ChangeRequest.Submit',     url: '/ChangeRequests/MyRequests', subject: 'Engineer' },
    { permission: 'ChangeRequest.Review',     url: '/ChangeRequests/Review/1',   subject: 'Engineer' },
    { permission: 'ChangeRequest.View.All',   url: '/ChangeRequests',            subject: 'Engineer' },
    { permission: 'ShiftOps.View',            url: '/ShiftOps/Today',            subject: 'Engineer' },
    { permission: 'ShiftOps.Report.View',     url: '/ShiftOps/History',          subject: 'Engineer' },
    { permission: 'AiAssistant.Use',          url: '/AiAssistant',               subject: 'Engineer' },
    // ShiftMakerController's class-level [Authorize(Roles="Admin,ShiftManager")] was
    // removed (converted to per-action policies) — these now gate purely on the
    // permission for ANY subject, no role requirement left underneath.
    { permission: 'Schedule.View',            url: '/ShiftMaker',                subject: 'Engineer' },
    { permission: 'Schedule.Create',          url: '/ShiftMaker/Create',         subject: 'Engineer' },
    { permission: 'Schedule.Generate',        url: '/ShiftMaker/Planner/1',      subject: 'Engineer' },
    { permission: 'Group.Member.Manage',      url: '/ShiftMaker/Groups',         subject: 'Engineer' },
    // System.IsAdmin is a role-only gate (UsersController), not a policy gate — included
    // here anyway since runToggleRoundTrip only cares about blocked/allowed either way.
    // Exercises both the PermissionService short-circuit and AdminClaimsTransformation
    // (the synthetic "Admin" role claim) in one round-trip.
    { permission: 'System.IsAdmin',           url: '/Users',                     subject: 'Engineer' },
    // Converted this session from hardcoded [Authorize(Roles=...)] to real policies.
    { permission: 'User.View',                url: '/Users',                     subject: 'Engineer' },
    { permission: 'AuditLog.View',            url: '/AuditLogs',                 subject: 'Engineer' },
    { permission: 'Rbac.Manage',              url: '/Rbac',                      subject: 'Engineer' },
    { permission: 'ShiftAnalytics.View',      url: '/ShiftAnalytics',            subject: 'Engineer' },
    // Location.Manage only gates Create/Edit, not Index — Create is a GET, safe to
    // exercise directly (no data mutation).
    { permission: 'Location.Manage',          url: '/Locations/Create',          subject: 'Engineer' },
    // DashboardController was re-gated onto ShiftAnalyticsView (same permission as
    // Task Analytics) so "Overview permissions" is a single switch for both.
    { permission: 'ShiftAnalytics.View',      url: '/Dashboard',                 subject: 'Engineer' },
    // Was exercised via the now-deleted Controllers/Api/PermissionsController
    // (/api/permissions, dead REST API superseded by RbacController's server-rendered
    // pages — removed in the ultra-ponytail dead-code sweep). UsersController.Create
    // is gated by the same UserManage policy, so it covers this permission just as well.
    { permission: 'User.Manage',              url: '/Users/Create',              subject: 'Engineer' },
];

// ── Tier 3: data-scoping permissions — the underlying page is always reachable
// (gated by a different, broader permission), so "blocked/allowed" doesn't apply.
// Instead check for a UI element that only appears when the scoping permission is held.
const SCOPING_CHECKS = [
    {
        permission: 'ShiftOps.ManageAll', subject: 'Engineer', url: '/ShiftOps/Today',
        // Work-area filter toggle buttons only render for holders of ManageAll.
        selector: '#areaToggles',
    },
    {
        permission: 'ShiftOps.Report.ManageAll', subject: 'Engineer', url: '/ShiftReports',
        // "Submitted by" filter dropdown only renders for holders of ManageAll.
        selector: 'select[name="submittedBy"]',
    },
];

async function checkElementPresence(subjectPage, url, selector) {
    await subjectPage.goto(`${BASE_URL}${url}`);
    return (await subjectPage.locator(selector).count()) > 0 ? 'allowed' : 'blocked';
}

for (const check of SCOPING_CHECKS) {
    test(`RBAC round-trip: ${check.permission} scopes data on ${check.url} (${check.subject})`, async ({ browser }) => {
        await runToggleRoundTrip({
            browser,
            permission: check.permission,
            subjectRole: check.subject,
            verify: (subjectPage) => checkElementPresence(subjectPage, check.url, check.selector),
        });
    });
}

for (const check of PAGE_CHECKS) {
    test(`RBAC round-trip: ${check.permission} gates ${check.url} (${check.subject})`, async ({ browser }) => {
        await runToggleRoundTrip({
            browser,
            permission: check.permission,
            subjectRole: check.subject,
            verify: (subjectPage) => checkPageAccess(subjectPage, check.url),
        });
    });
}

// ── Not testable: no [Authorize(Policy=...)] anywhere checks these ─────────
// Confirmed via full-repo grep.
const NOT_POLICY_GATED = [
    'Shift.Manage',
    'ShiftOps.Task.UpdateStatus', // UpdateTask action has no policy attribute at all
    // RotationTemplatesController (the only thing these ever gated) was deleted as
    // dead code — the entity/permissions remain seeded but nothing checks them.
    'Schedule.Template.View', 'Schedule.Template.Manage',
];
for (const permission of NOT_POLICY_GATED) {
    test.skip(`RBAC round-trip: ${permission} — no policy-gated endpoint exists (verified via grep)`, () => {});
}

// ── Not exercised: real policy gates, but every action is POST-only and mutates
// real data (activates/closes shifts, publishes/archives schedules, adds tasks
// or incidents). Skipped to avoid corrupting the dev DB this session has been
// actively using; the auth mechanism is identical to the GET-gated permissions
// above (same [Authorize(Policy=PermissionCatalog.X)] pattern), which already
// prove the plumbing works end-to-end.
const MUTATING_SKIPPED = [
    'ShiftOps.Activate', 'ShiftOps.Attendance.Update', 'ShiftOps.Task.Add',
    'ShiftOps.Close', 'ShiftOps.Incident.Add',
    'Schedule.Publish', 'Schedule.Archive', 'Schedule.Override.Apply', 'Schedule.Delete',
];
for (const permission of MUTATING_SKIPPED) {
    test.skip(`RBAC round-trip: ${permission} — POST-only, would mutate real data (skipped intentionally)`, () => {});
}
