// @ts-check
const { defineConfig } = require('@playwright/test');

module.exports = defineConfig({
  testDir: './tests',
  timeout: 30000,
  // One worker: the specs share one dev database and the RBAC spec mutates role permissions
  // mid-run, so parallel workers see each other's side effects and fail spuriously.
  workers: 1,
  use: {
    baseURL: 'https://localhost:55248',
    ignoreHTTPSErrors: true,   // self-signed dev cert
    headless: true,
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    { name: 'chromium', use: { browserName: 'chromium' } },
  ],
});
