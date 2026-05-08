import { defineConfig, devices } from '@playwright/test';

// CI runs against the full docker-compose stack (web on :4000, brought up
// before playwright starts, so no `webServer` block needed). Locally, we
// fall back to spawning `npm run dev` which uses :4200 — fast inner loop
// against native dotnet watches and `ng serve`.
const isCi = !!process.env.CI;
const baseURL = isCi ? 'http://localhost:4000' : 'http://localhost:4200';

export default defineConfig({
  testDir: './tests',
  // Auth flows mutate global state (sign-in, review submission), so order
  // matters across tests within the same project. fullyParallel only on
  // anonymous reads.
  fullyParallel: false,
  forbidOnly: isCi,
  retries: isCi ? 1 : 0,
  workers: 1,
  reporter: isCi ? [['github'], ['html', { open: 'never' }]] : 'list',

  // Sign Alice in once at suite start; tests opt into the authed session via
  // `test.use({ storageState: '.auth/storage-state.json' })`. Removing the
  // global setup is a one-line edit if a future test needs to re-auth fresh.
  globalSetup: './global-setup.ts',

  use: {
    baseURL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
    // Self-signed / no-TLS dev: don't choke on cert oddities.
    ignoreHTTPSErrors: true,
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],

  webServer: isCi
    ? undefined
    : {
        command: 'npm run dev',
        cwd: '..',
        url: 'http://localhost:4200',
        timeout: 5 * 60_000,
        reuseExistingServer: true,
        stdout: 'pipe',
        stderr: 'pipe',
      },
});
