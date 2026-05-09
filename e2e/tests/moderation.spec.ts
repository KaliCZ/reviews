import { test, expect } from "@playwright/test";
import { Connection, Client } from "@temporalio/client";
import { submitReview, waitForReviewVisible } from "./helpers/submit";

test.use({ storageState: ".auth/storage-state.json" });

// Reruns require `docker compose down -v` (unique-author index blocks resubmit).
const productSlug = "travelpro-tripod";

// 5-star → moderation gate. Drive the Approve via Temporal client to avoid
// coupling the test to ZITADEL/Temporal UI versions.
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

  // Moderation gate runs before persist — verify the row is NOT visible yet.
  await page.waitForTimeout(2_000);
  await page.goto(`/products/${productSlug}`);
  await expect(
    page.locator("article.review .body", { hasText: body }),
  ).toHaveCount(0);

  const conn = await Connection.connect({ address: "localhost:7233" });
  try {
    const client = new Client({ connection: conn });
    const handle = client.workflow.getHandle(workflowId);
    await handle.signal("Approve", null);
  } finally {
    await conn.close();
  }

  await waitForReviewVisible(page, productSlug, body);
});
