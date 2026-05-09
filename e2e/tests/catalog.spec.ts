import { test, expect } from '@playwright/test';

// Anonymous read smoke — exercises SSR + cache + DB end-to-end.
test.describe('catalog browsing', () => {
  test('lists seeded products and shows reviews on the product page', async ({ page }) => {
    await page.goto('/');

    await expect(page.getByRole('heading', { name: 'Products' })).toBeVisible();

    const sonyLink = page.getByRole('link', { name: /Sony WH-1000XM5/i });
    await expect(sonyLink).toBeVisible({ timeout: 15_000 });
    await sonyLink.first().click();

    await expect(page).toHaveURL(/\/products\/sony-wh-1000xm5$/);
    await expect(page.getByRole('heading', { name: /Sony WH-1000XM5/i })).toBeVisible();

    await expect(page.getByRole('heading', { name: 'Reviews' })).toBeVisible();
    const reviewBodies = page.locator('article.review .body');
    await expect(reviewBodies.first()).toBeVisible({ timeout: 15_000 });
    expect(await reviewBodies.count()).toBeGreaterThan(0);

    await expect(page.getByRole('link', { name: /Sign in to write a review/i })).toBeVisible();
  });
});
