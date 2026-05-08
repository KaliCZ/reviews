import { defineConfig, devices } from '@playwright/test';

// CI runs against the full docker-compose stack (web on :4000, brought up
// before playwright starts, so no `webServer` block needed). Locally, we
// fall back to spawning `npm run dev` which uses :4200 — fast inner loop
// against native dotnet watches and `ng serve`.
const isCi = !!process.env.CI;
const baseURL = isCi ? 'http://localhost:4000' : 'http://localhost:4200';

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: isCi,
  retries: isCi ? 1 : 0,
  workers: isCi ? 1 : undefined,
  reporter: isCi ? [['github'], ['html', { open: 'never' }]] : 'list',

  use: {
    baseURL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
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
