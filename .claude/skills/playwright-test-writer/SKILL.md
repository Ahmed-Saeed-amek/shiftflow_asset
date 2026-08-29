---
name: playwright-test-writer
description: Scaffold a new Playwright spec file in tests/ following ShiftFlow's established pattern (role login helper, BASE_URL constant, test.describe blocks). Use when the user asks to add/write a new Playwright test or E2E test for a ShiftFlow feature.
---

# Playwright Test Writer

Generates a new `tests/<name>.spec.js` file matching the conventions already used by
the 23 existing specs in this repo, instead of writing Playwright boilerplate from scratch.

## Conventions observed across `tests/*.spec.js`

- `// @ts-check` as the first line, then a one/two-line comment describing what the spec verifies.
- `const { test, expect } = require('@playwright/test');`
- `const BASE_URL = 'https://localhost:55248';` (the HTTPS dev port from `launchSettings.json`).
- Seeded accounts, reused verbatim — do not invent new credentials:
  ```js
  const ADMIN    = { email: 'admin@shiftflow.com',    password: 'Admin@123456' };
  const MANAGER  = { email: 'manager@shiftflow.com',  password: 'Manager@123456' };
  const ENGINEER = { email: 'engineer@shiftflow.com', password: 'Engineer@123456' };
  const HR       = { email: 'hr@shiftflow.com',        password: 'HR@123456' };
  ```
- A local `login` helper (each spec defines its own — there is no shared fixture file):
  ```js
  async function login(page, { email, password }) {
    await page.goto(`${BASE_URL}/Account/Login`);
    await page.fill('input[name="Email"]', email);
    await page.fill('input[name="Password"]', password);
    await page.click('form:has(input[name="Password"]) button[type="submit"]');
    await page.waitForLoadState('domcontentloaded');
  }
  ```
- `test.describe('<feature being verified>', () => { test.beforeEach(async ({ page }) => { await login(page, ROLE); }); ... })`.
- Assertions favor `expect(locator).toBeVisible({ timeout: 15000 })` after navigation (Kestrel/EF warm-up can be slow), then behavior-specific checks.
- `console.log(...)` lines before key assertions to make failures readable in the HTML report — keep this style.
- File names are kebab-case describing the behavior under test (e.g. `draft-visibility.spec.js`, `rbac-all-permissions.spec.js`), not the controller name.

## Steps

1. Ask (or infer from the request) which role(s) the test needs to log in as, and which controller/action or page is under test.
2. Read 1-2 existing specs closest to the feature area (e.g. `rbac-all-permissions.spec.js` for permission checks, `draft-visibility.spec.js` for visibility/filtering checks) to match tone and locator style for that area.
3. Write `tests/<kebab-case-name>.spec.js` following the conventions above.
4. Do not add a shared login/fixture file — this repo intentionally keeps each spec self-contained.
5. Remind the user to run `npm test` (or `npx playwright test tests/<name>.spec.js` for just the new file) — the dev server must be running at `https://localhost:55248` first (`dotnet run --project ShiftFlow.Web`).
