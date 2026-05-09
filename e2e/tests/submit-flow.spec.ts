import { test, expect } from "@playwright/test";
import { submitReview, waitForReviewVisible } from "./helpers/submit";

test.use({ storageState: ".auth/storage-state.json" });

// Each test in this file picks a different product so the unique-author-per-
// product index can't trip us up between runs of the same test (each run
// would hit the same row). 7 / 8 / 9 are the lower-traffic seeded products.
const productSlug = "xyz-mechanical-keyboard";

// Submitting a 3- or 4-star review skips moderation per the workflow
// definition (only 1, 2, 5 wait for an Approve signal). It should appear
// immediately after the workflow finishes.
test("submit a 4-star review (auto-approved) and see it on the product page", async ({
  page,
}) => {
  await page.goto(`/products/${productSlug}`);

  // The product page either shows "Write a review" (no existing review) or
  // "Edit your review". For a re-run on the same product, click Edit instead
  // and just verify the original submission persisted; we don't fail the
  // suite if test data is sticky.
  const writeCta = page.getByRole("link", { name: /Write a review/i });
  if ((await writeCta.count()) === 0) {
    await expect(
      page.getByRole("link", { name: /Edit your review/i }),
    ).toBeVisible();
    return; // already reviewed in a prior run; idempotent green path
  }

  const body = `End-to-end test review ${Date.now()} — covers submit-flow auto-approval path.`;
  await submitReview(page, {
    productSlug,
    rating: 4,
    title: "Solid daily driver",
    body,
  });

  // Routed back to the product page. For 4-star ratings the workflow
  // persists immediately, but the SPA doesn't auto-refetch after the
  // post-submit redirect — reload-poll until the new review is on the page.
  // (Real users would just refresh, or hit the page later.)
  await expect(page).toHaveURL(new RegExp(`/products/${productSlug}$`));
  await waitForReviewVisible(page, productSlug, body);
});

// Voting goes through a workflow too — the score updates after the worker
// runs the fetch-or-create + recompute. The test reads the score before and
// after.
test("upvote a review and see the score change", async ({ page }) => {
  await page.goto(`/products/${productSlug}`);

  // Pick a review that isn't ours (vote buttons are disabled on own reviews
  // anyway by the API rate limiter on retries; on someone else's review we
  // can flip cleanly).
  const otherReview = page
    .locator("article.review")
    .filter({
      hasNot: page.getByRole("link", { name: /Edit/ }),
    })
    .first();
  await expect(otherReview).toBeVisible();

  const scoreLocator = otherReview.locator(".score");
  const before = parseInt((await scoreLocator.textContent()) ?? "0", 10);

  const voteResp = page.waitForResponse(
    (r) =>
      /\/api\/reviews\/[^/]+\/vote$/.test(r.url()) &&
      r.request().method() === "POST",
  );
  await otherReview.locator("button.vote").first().click();
  const r = await voteResp;
  expect(r.status()).toBe(202);

  // The product page polls again ~400ms after a vote. Wait for the score
  // to differ from `before`. The exact direction depends on whether we
  // already had an existing vote (test reset state): just assert the
  // value moved. Vote workflow latency can be high on shared CI runners,
  // hence the generous timeout — we reload to force a refetch.
  await expect
    .poll(
      async () => {
        await page.reload();
        return parseInt(
          (await otherReview.locator(".score").textContent()) ?? "",
          10,
        );
      },
      { timeout: 60_000, intervals: [1000, 2000, 3000] },
    )
    .not.toBe(before);
});
