import { type Page, expect } from '@playwright/test';

// Multiple selector candidates because ZITADEL templates shift between
// releases (v1 named the inputs; v2 uses autocomplete hints).
export async function signInAsAlice(page: Page, baseURL: string): Promise<void> {
  await page.goto(`${baseURL}/auth/login`);

  await expect(page).toHaveURL(/localhost:8080\//, { timeout: 30_000 });

  await fillFirst(page, [
    'input[name="loginName"]',
    'input[autocomplete="username"]',
    'input[type="email"]',
    'input[type="text"]',
  ], 'alice');
  await clickPrimary(page);

  await fillFirst(page, [
    'input[name="password"]',
    'input[type="password"]',
    'input[autocomplete="current-password"]',
  ], 'Password1!');
  await clickPrimary(page);

  // First-login MFA prompt is optional.
  await skipMfaPromptIfPresent(page);

  // Don't pin a path — ZITADEL may interleave session-created pages.
  await page.waitForURL((url) => url.host === new URL(baseURL).host, { timeout: 30_000 });
  await expect(page.locator('app-root')).toBeVisible({ timeout: 30_000 });
}

async function fillFirst(page: Page, selectors: string[], value: string): Promise<void> {
  // Wait for ANY candidate to handle the navigation race after submit.
  const combined = page.locator(selectors.join(', '));
  await combined.first().waitFor({ state: 'visible', timeout: 15_000 });
  for (const sel of selectors) {
    const loc = page.locator(sel);
    if ((await loc.count()) > 0 && await loc.first().isVisible()) {
      await loc.first().fill(value);
      return;
    }
  }
  throw new Error(`No matching input for ${value}; tried ${selectors.join(', ')}`);
}

async function skipMfaPromptIfPresent(page: Page): Promise<void> {
  const skip = page.locator('button[name="skip"][value="true"]');
  try {
    await skip.waitFor({ state: 'visible', timeout: 5_000 });
    await skip.click();
  } catch {
    /* already navigated past */
  }
}

async function clickPrimary(page: Page): Promise<void> {
  const candidates = [
    page.locator('button[type="submit"]'),
    page.getByRole('button', { name: /next|continue|sign\s*in|log\s*in/i }),
  ];
  for (const cand of candidates) {
    if ((await cand.count()) > 0 && await cand.first().isVisible()) {
      await cand.first().click();
      return;
    }
  }
  throw new Error('No primary submit button found on ZITADEL form');
}
