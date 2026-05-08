// Mirror the API DTOs in backend/api/Models/Dtos.cs. Keep the shapes in
// sync; if the API changes, tests catch the type errors first.

export interface ProductSummary {
  id: number;
  slug: string;
  name: string;
  imageUrl: string | null;
  averageRating: number;
  reviewCount: number;
}

export interface ProductDetail extends ProductSummary {
  description: string;
  myReviewId: string | null;
}

export interface ReviewItem {
  id: string;
  productId: number;
  authorId: string;
  authorName: string;
  rating: number;
  title: string | null;
  body: string;
  imageUrls: string[];
  score: number;
  createdAt: string;
  updatedAt: string;
  myVote: number | null;
  mine: boolean;
}

export interface ReviewsPage {
  items: ReviewItem[];
  nextCursor: string | null;
}

export interface AcceptedResponse {
  workflowId: string;
  status: string;
}

export interface ConfigResponse {
  turnstileSiteKey: string;
}

export interface AuthMe {
  authenticated: boolean;
  user?: { sub: string; name?: string; email?: string };
}
