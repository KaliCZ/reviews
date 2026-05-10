import { test, expect } from "@playwright/test";
import {
  submitReview,
  waitForReviewVisible,
  waitForTurnstile,
} from "./helpers/submit";

test.use({ storageState: ".auth/storage-state.json" });

// Different product per spec to dodge the unique-author-per-product index;
// boombox-mini has no Alice seed review, so on a clean stack we exercise the
// full submit → edit path. Self-cleaning shape: we don't delete after the edit,
// the next run takes the "already has a review" branch and re-edits in place.
const productSlug = "boombox-mini";

// Edits within 1h of the original CreatedAt auto-apply (no moderation gate);
// since the test submits the review moments before editing we exercise that
// fast path. The workflow is still asynchronous (Temporal), so the assertion
// after the success modal polls until the new body lands on the listing.
test("edit own review within the 1h window — auto-applies, edited body appears on the product page", async ({
  page,
}) => {
  await page.goto(`/products/${productSlug}`);

  const writeCta = page.getByRole("link", { name: /Write a review/i });
  if ((await writeCta.count()) > 0) {
    const seedBody = `Edit e2e seed ${Date.now()} — gets overwritten by the edit step.`;
    await submitReview(page, {
      productSlug,
      rating: 4,
      title: "About to edit",
      body: seedBody,
    });
    await waitForReviewVisible(page, productSlug, seedBody);
  } else {
    // Pre-existing row from a prior run — fine, we just edit it again.
    await expect(
      page.getByRole("link", { name: /Edit your review/i }),
    ).toBeVisible();
  }

  await page.getByRole("link", { name: /Edit your review/i }).click();
  await expect(
    page.getByRole("heading", { name: /Edit your review/i }),
  ).toBeVisible();

  // Unique marker so the post-edit assertion can't be satisfied by the seed
  // body or by a leftover review from a prior run.
  const editedBody = `Edit e2e applied ${Date.now()} — verifies the within-1h auto-apply path.`;
  const bodyField = page.getByLabel("Review", { exact: false });
  await bodyField.fill(editedBody);

  // Edit also requires Turnstile (same gate as submit/vote/delete); without
  // waiting for the widget to populate the Save button stays disabled.
  await waitForTurnstile(page);

  const editResp = page.waitForResponse(
    (r) =>
      /\/api\/reviews\/[0-9a-f-]+$/.test(r.url()) &&
      r.request().method() === "PUT",
  );
  await page.getByRole("button", { name: /Save changes/i }).click();
  const resp = await editResp;
  expect(resp.status()).toBe(202);

  // The page swaps the form for a success modal instead of auto-navigating;
  // confirm it rendered, then click through to the product page.
  await expect(
    page.getByRole("heading", { name: /your edit was submitted/i }),
  ).toBeVisible({ timeout: 5_000 });
  await page.getByRole("button", { name: /Back to product/i }).click();

  // EditReviewWorkflow runs through Temporal and invalidates the cache when it
  // commits — poll the product page until the edited body materialises.
  await waitForReviewVisible(page, productSlug, editedBody);
});
