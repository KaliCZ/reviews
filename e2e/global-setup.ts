import { chromium, type FullConfig } from '@playwright/test';
import { signInAsAlice } from './helpers/auth';

// Sign in once per invocation; tests reuse via test.use({ storageState }).
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
