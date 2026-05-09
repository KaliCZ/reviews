import { type Page, expect } from '@playwright/test';

// Drives the ZITADEL hosted login UI. Two-step (loginName → password), matches
// what zitadel-bootstrap provisions. We try several selector candidates for
// each input and the primary button because ZITADEL's UI templates have
// shifted between releases (v1 used `name="loginName"`, v2 dropped the name
// attribute and uses autocomplete hints).
export async function signInAsAlice(page: Page, baseURL: string): Promise<void> {
  await page.goto(`${baseURL}/auth/login`);

  // Now on http://localhost:8080/ui/v2/login/login?... (ZITADEL v2 UI).
  await expect(page).toHaveURL(/localhost:8080\//, { timeout: 30_000 });

  // Step 1: username
  await fillFirst(page, [
    'input[name="loginName"]',
    'input[autocomplete="username"]',
    'input[type="email"]',
    'input[type="text"]',
  ], 'alice');
  await clickPrimary(page);

  // Step 2: password (ZITADEL navigates after the username submit)
  await fillFirst(page, [
    'input[name="password"]',
    'input[type="password"]',
    'input[autocomplete="current-password"]',
  ], 'Password1!');
  await clickPrimary(page);

  // Optional step 3: ZITADEL prompts to set up 2FA on first login. Skip it.
  // The button is `<button name="skip" value="true">Skip</button>`. If it
  // doesn't appear (e.g. policy forced MFA off), the next navigation lands
  // us at the BFF and the click loop is a no-op.
  await skipMfaPromptIfPresent(page);

  // Wait for the browser to land back on the BFF host. We don't pin an
  // exact path because ZITADEL may insert intermediate "session created"
  // pages, and the BFF's /auth/callback ultimately redirects to /.
  await page.waitForURL((url) => url.host === new URL(baseURL).host, { timeout: 30_000 });
  // Confirm we ended up on the SPA root (the callback handler redirects
  // there once tokens are stored). If ZITADEL is sitting on an interstitial
  // we'll see it here and the test fails loudly.
  await expect(page.locator('app-root')).toBeVisible({ timeout: 30_000 });
}

async function fillFirst(page: Page, selectors: string[], value: string): Promise<void> {
  // Wait for *any* of the candidate selectors to become visible — covers
  // the race between clicking Next on the loginname form and ZITADEL's
  // navigation completing for the password form.
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
  // Race the wait — either the page is the MFA prompt and the Skip button
  // shows up within a beat, or the navigation has already left for the BFF
  // and we move on without clicking anything.
  const skip = page.locator('button[name="skip"][value="true"]');
  try {
    await skip.waitFor({ state: 'visible', timeout: 5_000 });
    await skip.click();
  } catch {
    // No prompt — already navigated past, nothing to do.
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
