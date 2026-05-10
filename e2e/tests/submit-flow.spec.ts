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

test("clicking your active vote removes it", async ({ page }) => {
  await page.goto(`/products/${productSlug}`);

  const otherReview = page
    .locator("article.review")
    .filter({ hasNot: page.getByRole("link", { name: /Edit/ }) })
    .first();
  await expect(otherReview).toBeVisible();

  const upBtn = otherReview.locator("button.vote").first();
  const downBtn = otherReview.locator("button.vote").last();
  const score = otherReview.locator(".score");

  // Normalise to "upvote active" regardless of any prior test's state. One
  // click on ▲ goes down→up or none→up (both POST) or up→none (DELETE);
  // a follow-up click recovers the up state from the none case.
  const firstClick = page.waitForResponse((r) =>
    /\/api\/reviews\/[^/]+\/vote$/.test(r.url()),
  );
  await upBtn.click();
  await firstClick;
  if (!(await upBtn.evaluate((el) => el.classList.contains("active")))) {
    const recast = page.waitForResponse(
      (r) =>
        /\/api\/reviews\/[^/]+\/vote$/.test(r.url()) &&
        r.request().method() === "POST",
    );
    await upBtn.click();
    await recast;
    await expect(upBtn).toHaveClass(/active/);
  }
  const withUpvote = parseInt((await score.textContent()) ?? "", 10);

  // Removal: same button, but the SPA emits null and the client switches to
  // DELETE /api/reviews/{id}/vote. Score drops by 1, no button stays active.
  const removeResp = page.waitForResponse(
    (r) =>
      /\/api\/reviews\/[^/]+\/vote$/.test(r.url()) &&
      r.request().method() === "DELETE",
  );
  await upBtn.click();
  const r = await removeResp;
  expect(r.status()).toBe(200);

  await expect
    .poll(
      async () =>
        parseInt((await otherReview.locator(".score").textContent()) ?? "", 10),
      { timeout: 5_000 },
    )
    .toBe(withUpvote - 1);
  await expect(upBtn).not.toHaveClass(/active/);
  await expect(downBtn).not.toHaveClass(/active/);
});
