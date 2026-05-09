// Hand-written DTOs mirroring backend/api/Models/Dtos.cs.

// Mirror ReviewsDbContext + ImagesController. Backend still rejects anything past these.
export const Limits = {
  titleMax: 200,
  bodyMax: 4000,
  bodyMin: 10,
  maxImages: 5,
  imageUrlMax: 1000,
  maxImageBytes: 2 * 1024 * 1024,
  allowedImageTypes: ['image/jpeg', 'image/png', 'image/webp', 'image/gif'] as const,
} as const;

export type ReviewSort = 'Newest' | 'Helpful' | 'Highest' | 'Lowest';

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
  score: number;
  createdAtUtc: string;
  updatedAtUtc: string;
  myVote: boolean | null;
  mine: boolean;
}

export interface ReviewsPage {
  items: ReviewItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}

// rating stays as `number` here so SPA forms don't need a cast at submit.
// The API's RatingJsonConverter enforces 1..5 at parse time.
export interface SubmitReviewRequest {
  productId: number;
  rating: number;
  title: string;
  body: string;
  imageUrls?: string[];
  turnstileToken: string;
}

export interface EditReviewRequest {
  rating: number;
  title: string;
  body: string;
  imageUrls?: string[];
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

// BFF-only (no OpenAPI counterpart on the upstream API).
export interface AuthMe {
  authenticated: boolean;
  user?: { sub: string; name?: string; email?: string };
}
