// Re-export the generated OpenAPI types as the canonical DTO surface for the
// SPA. This file is the only place outside `schema.d.ts` that imports the
// generated module — components import the named aliases below so a future
// regen affects exactly one file.

import type { components } from './schema';

// Server-emitted required strings come back as plain `string`. Optional
// strings come back as `string | undefined` (the spec marks them `nullable:
// true`); we widen to `string | null | undefined` for ergonomic narrowing in
// templates that already treat `null` as "absent".
type S = components['schemas'];

// Limits the API enforces — re-stated here so the SPA can size form fields
// and validate before submitting. Keep in sync with ReviewsDbContext on the
// backend; the API still rejects anything past these.
export const Limits = {
  titleMax: 200,
  bodyMax: 4000,
  bodyMin: 10,
  maxImages: 5,
  // 2 MiB; matches ImagesController.MaxImageBytes.
  maxImageBytes: 2 * 1024 * 1024,
  allowedImageTypes: ['image/jpeg', 'image/png', 'image/webp', 'image/gif'] as const,
} as const;

export type ProductSummary = S['ProductSummary'];
export type ProductDetail = S['ProductDetail'];
export type ReviewItem = S['ReviewItem'];
export type ReviewsPage = S['ReviewsPage'];
export type SubmitReviewRequest = S['SubmitReviewRequest'];
export type EditReviewRequest = S['EditReviewRequest'];
export type VoteRequest = S['VoteRequest'];
export type AcceptedResponse = S['AcceptedResponse'];
export type ConfigResponse = S['ConfigResponse'];
export type UploadedImage = S['UploadedImage'];

// Shapes used by SPA-only state (auth metadata from the BFF, not the upstream
// API) — no OpenAPI counterpart. Lives here so consumers have one import.
export interface AuthMe {
  authenticated: boolean;
  user?: { sub: string; name?: string; email?: string };
}
