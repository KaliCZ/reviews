import { test, expect } from '@playwright/test';

// Verifies /auth/login → ZITADEL → /auth/callback → /auth/me → SPA header.
test.use({ storageState: '.auth/storage-state.json' });

test('signed-in header reflects the user from /auth/me', async ({ page, request }) => {
  const me = await request.get('/auth/me');
  expect(me.ok()).toBeTruthy();
  const body = await me.json();
  expect(body.authenticated).toBe(true);
  expect(body.user.sub).toMatch(/\S+/);

  await page.goto('/');
  await expect(page.getByText(/Hi,\s*Alice/i)).toBeVisible({ timeout: 15_000 });
  await expect(page.getByRole('link', { name: /Sign out/i })).toBeVisible();
});
