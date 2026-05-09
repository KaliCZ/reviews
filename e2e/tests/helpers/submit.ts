import { expect, type Page } from "@playwright/test";

export async function setRating(page: Page, value: number): Promise<void> {
  const stars = page.locator("span.stars.interactive button.star");
  await expect(stars).toHaveCount(5);
  await stars.nth(value - 1).click();
}

export async function waitForTurnstile(page: Page): Promise<void> {
  await page.waitForFunction(
    () =>
      Array.from(
        document.querySelectorAll('input[name="cf-turnstile-response"]'),
      ).some((i) => (i as HTMLInputElement).value.length > 0),
    null,
    { timeout: 30_000 },
  );
}

export interface SubmitOptions {
  productSlug: string;
  rating: number;
  title: string;
  body: string;
}

export interface SubmitResult {
  workflowId: string;
}

// Navigates to the product, opens Write-a-review, fills + submits, returns
// the workflowId from the 202. Caller asserts post-submit visibility, which
// differs between the auto-approve and moderation-gated paths.
export async function submitReview(
  page: Page,
  opts: SubmitOptions,
): Promise<SubmitResult> {
  await page.goto(`/products/${opts.productSlug}`);
  await page.getByRole("link", { name: /Write a review/i }).click();
  await expect(
    page.getByRole("heading", { name: /Write a review/i }),
  ).toBeVisible();
  await setRating(page, opts.rating);
  await page.getByLabel("Title", { exact: true }).fill(opts.title);
  await page.getByLabel("Review", { exact: false }).fill(opts.body);
  await waitForTurnstile(page);

  const submitResponse = page.waitForResponse(
    (r) => r.url().endsWith("/api/reviews") && r.request().method() === "POST",
  );
  await page.getByRole("button", { name: /Submit review/i }).click();
  const resp = await submitResponse;
  expect(resp.status()).toBe(202);
  const accepted = (await resp.json()) as {
    workflowId: string;
    status: string;
  };
  return { workflowId: accepted.workflowId };
}

// Polls the product page (with reload) until a review with the given body is
// visible. Generous timeout for shared CI runners.
export async function waitForReviewVisible(
  page: Page,
  productSlug: string,
  body: string,
): Promise<void> {
  await expect
    .poll(
      async () => {
        await page.goto(`/products/${productSlug}`);
        return await page
          .locator("article.review .body", { hasText: body })
          .count();
      },
      { timeout: 30_000, intervals: [1000, 2000, 3000] },
    )
    .toBeGreaterThan(0);
}
