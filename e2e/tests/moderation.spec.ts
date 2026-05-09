import { test, expect } from "@playwright/test";
import { Connection, Client } from "@temporalio/client";
import { submitReview, waitForReviewVisible } from "./helpers/submit";

test.use({ storageState: ".auth/storage-state.json" });

// Picks a product Alice hasn't reviewed yet for the moderation flow. This
// test is self-contained; we'll re-enter it at most once per stack lifetime
// because the unique-author-per-product index would block the second submit.
// To re-run: docker compose down -v && up.
const productSlug = "travelpro-tripod";

// 5-star reviews go through human moderation per docs/flows.md §3. The
// workflow waits on an Approve/Reject signal and only persists on Approve.
// The Temporal UI is the operator's surface for sending that signal; we
// drive it programmatically via the Node client to make the test
// reproducible without depending on a UI version-specific DOM.
test("5-star review waits for moderation, appears after Approve signal", async ({
  page,
}) => {
  await page.goto(`/products/${productSlug}`);

  const writeCta = page.getByRole("link", { name: /Write a review/i });
  test.skip(
    (await writeCta.count()) === 0,
    `Alice already has a review on ${productSlug} — wipe state with \`docker compose down -v\` and rerun`,
  );

  const body = `Moderation e2e ${Date.now()} — workflow should pause for an Approve signal.`;
  const { workflowId } = await submitReview(page, {
    productSlug,
    rating: 5,
    title: "Pending moderation",
    body,
  });
  expect(workflowId).toMatch(/^submit-review-/);

  // The moderation gate runs before persist, so the review should NOT be on
  // the product page yet. Wait a beat to make sure the worker had time to
  // run if it was going to (it isn't), then assert absence.
  await page.waitForTimeout(2_000);
  await page.goto(`/products/${productSlug}`);
  await expect(
    page.locator("article.review .body", { hasText: body }),
  ).toHaveCount(0);

  // Send the Approve signal directly via the Temporal client — this is what
  // a moderator would do in the Temporal UI. The workflow's WaitConditionAsync
  // resumes, persists, and refreshes the cache.
  const conn = await Connection.connect({ address: "localhost:7233" });
  try {
    const client = new Client({ connection: conn });
    const handle = client.workflow.getHandle(workflowId);
    await handle.signal("Approve", null);
  } finally {
    await conn.close();
  }

  // After the signal, the workflow finishes the persist + cache-refresh
  // activities. Reload until the review shows up.
  await waitForReviewVisible(page, productSlug, body);
});
