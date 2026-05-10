import { test, expect } from "@playwright/test";
import {
  submitReview,
  waitForReviewVisible,
  waitForTurnstile,
} from "./helpers/submit";

test.use({ storageState: ".auth/storage-state.json" });

// single-origin-coffee has no Alice seed review, so on a clean stack
// (CI brings up `docker compose down -v` between runs) we exercise the
// full submit → delete path. Self-cleaning: delete is the last step, so
// the unique-author index frees up the slot for the next run. If a
// prior run failed mid-flight we just delete whatever's already there.
const productSlug = "single-origin-coffee";

test("delete own review — synchronous, removes the row and frees the author slot", async ({
  page,
}) => {
  await page.goto(`/products/${productSlug}`);
  const writeCta = page.getByRole("link", { name: /Write a review/i });
  if ((await writeCta.count()) > 0) {
    const body = `Delete e2e ${Date.now()} — covers synchronous DELETE + cache invalidation.`;
    await submitReview(page, {
      productSlug,
      rating: 4,
      title: "About to delete",
      body,
    });
    await waitForReviewVisible(page, productSlug, body);
  } else {
    // Pre-existing row from a failed prior run — fine, just delete it.
    await expect(
      page.getByRole("link", { name: /Edit your review/i }),
    ).toBeVisible();
  }

  // Single Turnstile widget at the bottom of the reviews list; wait for it
  // to populate before clicking delete or the button check disables it.
  await waitForTurnstile(page);

  const myCard = page
    .locator("article.review")
    .filter({ has: page.getByRole("link", { name: /^Edit$/ }) })
    .first();
  await expect(myCard).toBeVisible();

  const deleteResp = page.waitForResponse(
    (r) =>
      /\/api\/reviews\/[0-9a-f-]+$/.test(r.url()) &&
      r.request().method() === "DELETE",
  );
  await myCard.getByRole("button", { name: /^Delete$/ }).click();

  // SPA confirm dialog — click its Delete (also accessible name "Delete",
  // but scoped to the dialog so it doesn't collide with the row button).
  await page
    .locator("dialog.confirm")
    .getByRole("button", { name: /^Delete$/ })
    .click();

  const resp = await deleteResp;
  expect(resp.status()).toBe(204);

  // Listing endpoints filter Status != Deleted, so myReviewId on the
  // product detail goes back to null and the SPA re-renders the Write CTA.
  await expect
    .poll(
      async () => {
        await page.goto(`/products/${productSlug}`);
        return await page
          .getByRole("link", { name: /Write a review/i })
          .count();
      },
      { timeout: 15_000, intervals: [500, 1000, 2000] },
    )
    .toBeGreaterThan(0);
});
