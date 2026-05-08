import { test, expect } from '@playwright/test';

test.describe('hello workflow', () => {
  test('runs the workflow end-to-end and increments the counter', async ({ page }) => {
    await page.goto('/');

    await expect(page.getByRole('heading', { name: /Reviews/i })).toBeVisible();

    const input = page.getByLabel('Increment by:');
    const button = page.getByRole('button', { name: 'Run workflow' });
    const result = page.locator('.result');

    await input.fill('3');
    await button.click();

    await expect(result).toBeVisible({ timeout: 30_000 });
    await expect(result).toContainText('Incremented via Temporal');

    const firstCount = parseCount(await result.textContent());

    // Second click with the same input — count must grow by 3.
    await button.click();
    await expect(result).toContainText(`count: ${firstCount + 3}`, { timeout: 30_000 });
  });
});

function parseCount(text: string | null): number {
  const match = /count:\s*(\d+)/.exec(text ?? '');
  if (!match) throw new Error(`No count found in result: ${text}`);
  return parseInt(match[1], 10);
}
