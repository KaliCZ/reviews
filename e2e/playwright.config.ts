import { defineConfig, devices } from '@playwright/test';

// CI: docker-compose stack on :4000 brought up beforehand. Local: npm run dev on :4200.
const isCi = !!process.env.CI;
const baseURL = isCi ? 'http://localhost:4000' : 'http://localhost:4200';

export default defineConfig({
  testDir: './tests',
  // Sign-in + review submission mutate global state.
  fullyParallel: false,
  forbidOnly: isCi,
  retries: isCi ? 1 : 0,
  workers: 1,
  reporter: isCi ? [['github'], ['html', { open: 'never' }]] : 'list',

  globalSetup: './global-setup.ts',

  use: {
    baseURL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
    ignoreHTTPSErrors: true, // dev TLS oddities
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
