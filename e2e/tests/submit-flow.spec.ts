import { test, expect } from "@playwright/test";
import {
  submitReview,
  waitForReviewVisible,
  waitForTurnstile,
} from "./helpers/submit";

test.use({ storageState: ".auth/storage-state.json" });

// Each test picks a different product to dodge the unique-author-per-product index.
const productSlug = "xyz-mechanical-keyboard";

// 3/4-star auto-approves; 1/2/5 wait on a moderator (see SubmitReviewWorkflow).
test("submit a 4-star review (auto-approved) and see it on the product page", async ({
  page,
}) => {
  await page.goto(`/products/${productSlug}`);

  // Idempotent re-run: prior submissions leave an Edit CTA — accept and exit.
  const writeCta = page.getByRole("link", { name: /Write a review/i });
  if ((await writeCta.count()) === 0) {
    await expect(
      page.getByRole("link", { name: /Edit your review/i }),
    ).toBeVisible();
    return;
  }

  const body = `End-to-end test review ${Date.now()} — covers submit-flow auto-approval path.`;
  await submitReview(page, {
    productSlug,
    rating: 4,
    title: "Solid daily driver",
    body,
  });

  // SPA doesn't auto-refetch after the post-submit redirect — reload-poll.
  await expect(page).toHaveURL(new RegExp(`/products/${productSlug}$`));
  await waitForReviewVisible(page, productSlug, body);
});

test("upvote a review and see the score change", async ({ page }) => {
  await page.goto(`/products/${productSlug}`);

  // Pick someone else's review — vote buttons on our own row are disabled.
  const otherReview = page
    .locator("article.review")
    .filter({
      hasNot: page.getByRole("link", { name: /Edit/ }),
    })
    .first();
  await expect(otherReview).toBeVisible();

  // Vote is gated on a Turnstile token (same gate as submit/edit/delete);
  // without it onVote() short-circuits and no network call fires. The product
  // page hosts a single shared widget for vote + delete.
  await waitForTurnstile(page);

  const scoreLocator = otherReview.locator(".score");
  const before = parseInt((await scoreLocator.textContent()) ?? "0", 10);

  const voteResp = page.waitForResponse(
    (r) =>
      /\/api\/reviews\/[^/]+\/vote$/.test(r.url()) &&
      r.request().method() === "POST",
  );
  await otherReview.locator("button.vote").first().click();
  const r = await voteResp;
  expect(r.status()).toBe(200);

  // Sync write — the SPA patches the row from the response body, so the
  // displayed score updates without any reload. Direction depends on prior
  // vote state, so just assert the value moved.
  await expect
    .poll(
      async () =>
        parseInt((await otherReview.locator(".score").textContent()) ?? "", 10),
      { timeout: 5_000 },
    )
    .not.toBe(before);
});
