import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AcceptedResponse,
  ConfigResponse,
  ProductDetail,
  ProductSummary,
  ReviewSort,
  ReviewsPage,
  SubmitReviewRequest,
  EditReviewRequest,
  UploadedImage,
} from '../models';

// Thin wrapper around HttpClient — pure URL/payload shape, no business logic.
// All calls go through `/api/*`, which the BFF proxies to the upstream API
// after attaching the user's Bearer token.
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  config(): Observable<ConfigResponse> {
    return this.http.get<ConfigResponse>('/api/config');
  }

  listProducts(): Observable<ProductSummary[]> {
    return this.http.get<ProductSummary[]>('/api/products');
  }

  getProduct(slug: string): Observable<ProductDetail> {
    return this.http.get<ProductDetail>(`/api/products/${encodeURIComponent(slug)}`);
  }

  // Offset pagination + multi-rating filter (rating params repeat: ?rating=4&rating=5).
  // The sort enum lands as its C# member name (Newest / Helpful / Highest /
  // Lowest); the API is case-insensitive on read.
  listReviews(slug: string, params: ReviewListParams = {}): Observable<ReviewsPage> {
    const q = new URLSearchParams();
    if (params.sort) q.set('sort', params.sort);
    if (params.ratings && params.ratings.length > 0) {
      for (const r of params.ratings) q.append('rating', String(r));
    }
    if (params.hasPhotos) q.set('hasPhotos', 'true');
    if (params.page != null) q.set('page', String(params.page));
    if (params.pageSize != null) q.set('pageSize', String(params.pageSize));
    const qs = q.toString();
    return this.http.get<ReviewsPage>(
      `/api/products/${encodeURIComponent(slug)}/reviews${qs ? `?${qs}` : ''}`,
    );
  }

  submitReview(body: SubmitReviewRequest): Observable<AcceptedResponse> {
    return this.http.post<AcceptedResponse>('/api/reviews', body);
  }

  editReview(id: string, body: EditReviewRequest): Observable<AcceptedResponse> {
    return this.http.put<AcceptedResponse>(`/api/reviews/${encodeURIComponent(id)}`, body);
  }

  deleteReview(id: string): Observable<AcceptedResponse> {
    return this.http.delete<AcceptedResponse>(`/api/reviews/${encodeURIComponent(id)}`);
  }

  // True = upvote, false = downvote — matches the boolean wire shape the API
  // settled on after dropping the prior tri-state ±1 short.
  voteReview(id: string, isUpvote: boolean): Observable<AcceptedResponse> {
    return this.http.post<AcceptedResponse>(`/api/reviews/${encodeURIComponent(id)}/vote`, {
      isUpvote,
    });
  }

  uploadImage(file: File): Observable<UploadedImage> {
    const fd = new FormData();
    fd.append('file', file);
    return this.http.post<UploadedImage>('/api/images', fd);
  }
}

export interface ReviewListParams {
  sort?: ReviewSort;
  ratings?: number[];
  hasPhotos?: boolean;
  page?: number;
  pageSize?: number;
}
