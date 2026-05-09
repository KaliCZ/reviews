// Hand-written wire DTOs that mirror the API. Kept in sync with the .NET DTOs
// in `backend/api/Models/Dtos.cs`. Previously generated from the OpenAPI spec
// via openapi-typescript; the generated `schema.d.ts` still ships for callers
// that want the raw spec shape, but the SPA imports from this file for the
// trimmed/named surface its components actually use.

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

// Wire enum for the reviews listing sort key. Backend serialises the C#
// enum via JsonStringEnumConverter; case-insensitive on read so lowercase
// also works.
export type ReviewSort = 'Newest' | 'Helpful' | 'Highest' | 'Lowest';

// Star rating on the wire — integer 1..5 enforced by RatingJsonConverter on
// the API side. SPA uses a plain number.
export type Rating = 1 | 2 | 3 | 4 | 5;

export interface ProductSummary {
  id: number;
  slug: string;
  name: string;
  imageUrl: string | null;
  averageRating: number;
  reviewCount: number;
}

export interface ProductDetail {
  id: number;
  slug: string;
  name: string;
  description: string;
  imageUrl: string | null;
  averageRating: number;
  reviewCount: number;
  myReviewId: string | null;
}

export interface ReviewItem {
  id: string;
  productId: number;
  authorId: string;
  authorName: string;
  rating: Rating;
  title: string;
  body: string;
  imageUrls: string[];
  // BCP-47 tag of the title + body, supplied by the submitter. The SPA
  // shows a "Translate" affordance only when this differs from the viewer's
  // UI locale.
  language: string;
  score: number;
  createdAt: string;
  updatedAt: string;
  // True = upvote, false = downvote, null = no vote from current viewer.
  myVote: boolean | null;
  mine: boolean;
}

// Offset-pagination payload — the SPA can render a real "page X of Y" UI
// instead of forward-only cursor links.
export interface ReviewsPage {
  items: ReviewItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}

// Request DTOs leave rating as `number` so SPA forms (which already track
// 0..5 via the star widget) don't need a cast at every submit. The API
// enforces 1..5 at the wire layer via RatingJsonConverter — invalid
// integers come back as 400 before the controller runs.
export interface SubmitReviewRequest {
  productId: number;
  rating: number;
  title: string;
  body: string;
  imageUrls?: string[];
  // BCP-47 language tag of the review text. The SPA fills it from the
  // current UI locale.
  language: string;
  turnstileToken: string;
}

export interface EditReviewRequest {
  rating: number;
  title: string;
  body: string;
  imageUrls?: string[];
  language: string;
}

export interface VoteRequest {
  isUpvote: boolean;
}

export interface AcceptedResponse {
  workflowId: string;
  status: string;
}

export interface ConfigResponse {
  turnstileSiteKey: string;
}

export interface UploadedImage {
  url: string;
}

// SPA-only state (auth metadata from the BFF, not the upstream API) — no
// OpenAPI counterpart. Lives here so consumers have one import.
export interface AuthMe {
  authenticated: boolean;
  user?: { sub: string; name?: string; email?: string };
}
