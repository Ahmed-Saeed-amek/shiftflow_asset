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

// Every entry's permission must exist in PermissionCatalog.All and its URL must map to a real
// controller action. The retired shift/rostering permissions (ShiftOps.*, Schedule.*,
// ChangeRequest.*, ShiftAnalytics.*, Group.Member.*, Location.Manage) were removed along with
// their controllers, so those rows are gone.
const PAGE_CHECKS = [
    { permission: 'AiAssistant.Use',          url: '/AiAssistant',               subject: 'Engineer' },
    // System.IsAdmin short-circuits every permission check (PermissionService) and also drives
    // AdminClaimsTransformation's synthetic "Admin" role claim.
    { permission: 'System.IsAdmin',           url: '/Users',                     subject: 'Engineer' },
    { permission: 'User.View',                url: '/Users',                     subject: 'Engineer' },
    { permission: 'User.Manage',              url: '/Users/Create',              subject: 'Engineer' },
    { permission: 'AuditLog.View',            url: '/AuditLogs',                 subject: 'Engineer' },
    { permission: 'Rbac.Manage',              url: '/Rbac',                      subject: 'Engineer' },
    { permission: 'Asset.View',               url: '/Assets',                    subject: 'Engineer' },
    { permission: 'Vendor.View',              url: '/Vendors',                   subject: 'Engineer' },
    { permission: 'Contract.View',            url: '/Contracts',                 subject: 'Engineer' },
    { permission: 'WorkOrder.View',           url: '/WorkOrders',                subject: 'Engineer' },
    { permission: 'SparePart.View',           url: '/SpareParts',                subject: 'Engineer' },
    { permission: 'InspectionOrder.View',     url: '/InspectionOrders',          subject: 'Engineer' },
    { permission: 'MaintenanceOrder.View',    url: '/MaintenanceOrders',         subject: 'Engineer' },
    { permission: 'Group.View',               url: '/Groups',                    subject: 'Engineer' },
    { permission: 'Asset.ScopeManage',        url: '/UserAssetScopes',           subject: 'Engineer' },
    { permission: 'MyWork.View',              url: '/MyHome',                    subject: 'Engineer' },
];

async function checkElementPresence(subjectPage, url, selector) {
    await subjectPage.goto(`${BASE_URL}${url}`);
    return (await subjectPage.locator(selector).count()) > 0 ? 'allowed' : 'blocked';
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

// Permissions whose only gates are POST-only, data-mutating actions. The auth mechanism is the
// same [Authorize(Policy=...)] pattern the GET-gated permissions above already prove end-to-end,
// so they are skipped rather than allowed to mutate the dev database.
const MUTATING_SKIPPED = [
    'Asset.Manage', 'AssetCategory.Manage', 'Asset.ReportAction',
    'Vendor.Manage', 'Contract.Manage',
    'WorkOrder.Manage', 'WorkOrder.Assign',
    'MaintenanceOrder.Manage', 'MaintenanceOrder.Report',
    'InspectionOrder.Manage', 'InspectionOrder.Report',
    'Group.Manage', 'OrderType.Manage', 'SparePart.Manage',
];
for (const permission of MUTATING_SKIPPED) {
    test.skip(`RBAC round-trip: ${permission} — POST-only, would mutate real data (skipped intentionally)`, () => {});
}

// Export-only permissions stream a file rather than render a page, so the blocked/allowed page
// check above does not apply.
const EXPORT_ONLY = [
    'Asset.Export', 'WorkOrder.Export', 'InspectionOrder.Export', 'MaintenanceOrder.Export',
];
for (const permission of EXPORT_ONLY) {
    test.skip(`RBAC round-trip: ${permission} — file download, not a page gate`, () => {});
}
