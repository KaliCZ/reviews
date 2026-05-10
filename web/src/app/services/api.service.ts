import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AcceptedResponse,
  ConfigResponse,
  ProductDetail,
  ProductSummary,
  ReviewItem,
  ReviewSort,
  SortDirection,
  ReviewsPage,
  SubmitReviewRequest,
  EditReviewRequest,
  UploadedImage,
  VoteResponse,
} from '../models';

// Calls go through `/api/*` so the BFF proxy attaches the Bearer token.
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

  // Multi-rating filter repeats the param: ?rating=4&rating=5.
  listReviews(slug: string, params: ReviewListParams = {}): Observable<ReviewsPage> {
    const q = new URLSearchParams();
    if (params.sort) q.set('sort', params.sort);
    if (params.direction) q.set('direction', params.direction);
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

  getReview(id: string): Observable<ReviewItem> {
    return this.http.get<ReviewItem>(`/api/reviews/${encodeURIComponent(id)}`);
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

  voteReview(id: string, isUpvote: boolean): Observable<VoteResponse> {
    return this.http.post<VoteResponse>(`/api/reviews/${encodeURIComponent(id)}/vote`, {
      isUpvote,
    });
  }

  removeVote(id: string): Observable<VoteResponse> {
    return this.http.delete<VoteResponse>(`/api/reviews/${encodeURIComponent(id)}/vote`);
  }

  uploadImage(file: File): Observable<UploadedImage> {
    const fd = new FormData();
    fd.append('file', file);
    return this.http.post<UploadedImage>('/api/images', fd);
  }
}

export interface ReviewListParams {
  sort?: ReviewSort;
  direction?: SortDirection;
  ratings?: number[];
  hasPhotos?: boolean;
  page?: number;
  pageSize?: number;
}
