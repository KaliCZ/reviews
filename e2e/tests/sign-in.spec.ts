import { test, expect } from '@playwright/test';

// Verifies the BFF auth round-trip end-to-end: /auth/login → ZITADEL form →
// /auth/callback → session cookie set → /auth/me returns the user → header
// shows the signed-in name. Reuses the storage state populated by global-
// setup.ts so we're not re-running the OIDC dance per test.
test.use({ storageState: '.auth/storage-state.json' });

test('signed-in header reflects the user from /auth/me', async ({ page, request }) => {
  // /auth/me speaks for the BFF session; the SPA reads it on bootstrap.
  const me = await request.get('/auth/me');
  expect(me.ok()).toBeTruthy();
  const body = await me.json();
  expect(body.authenticated).toBe(true);
  expect(body.user.sub).toMatch(/\S+/);

  await page.goto('/');
  await expect(page.getByText(/Hi,\s*Alice/i)).toBeVisible({ timeout: 15_000 });
  await expect(page.getByRole('link', { name: /Sign out/i })).toBeVisible();
});
