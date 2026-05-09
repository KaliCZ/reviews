import { chromium, type FullConfig } from '@playwright/test';
import { signInAsAlice } from './helpers/auth';

// Signs Alice in once per `npx playwright test` invocation and writes the
// resulting cookies to .auth/storage-state.json. Tests that need an
// authenticated browser declare `test.use({ storageState: '...' })` and
// reuse the same session — saves a full ZITADEL round-trip per test.
export default async function (config: FullConfig): Promise<void> {
  const baseURL = (config.projects[0].use.baseURL as string | undefined) ?? 'http://localhost:4000';
  const browser = await chromium.launch();
  const context = await browser.newContext();
  const page = await context.newPage();
  try {
    await signInAsAlice(page, baseURL);
    await context.storageState({ path: '.auth/storage-state.json' });
  } finally {
    await browser.close();
  }
}
