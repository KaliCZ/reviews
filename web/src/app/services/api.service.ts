import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AcceptedResponse,
  ConfigResponse,
  ProductDetail,
  ProductSummary,
  ReviewsPage,
  SubmitReviewRequest,
  EditReviewRequest,
  UploadedImage,
} from '../models';

// Thin wrapper around HttpClient — pure URL/payload shape, no business logic.
// All calls go through `/api/*`, which the BFF proxies to the upstream API
// after attaching the user's Bearer token.
//
// Request/response shapes are imported from `web/src/api`, which is generated
// from the OpenAPI spec — no hand-written DTO drift between the SPA and the
// upstream API.
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

  listReviews(slug: string, params: ReviewListParams = {}): Observable<ReviewsPage> {
    const q = new URLSearchParams();
    if (params.sort) q.set('sort', params.sort);
    if (params.rating != null) q.set('rating', String(params.rating));
    if (params.hasPhotos) q.set('hasPhotos', 'true');
    if (params.cursor) q.set('cursor', params.cursor);
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

  voteReview(id: string, value: 1 | -1): Observable<AcceptedResponse> {
    return this.http.post<AcceptedResponse>(`/api/reviews/${encodeURIComponent(id)}/vote`, {
      value,
    });
  }

  // Uploads a single image and returns the URL to embed in a review. The API
  // gates content type and size; this wrapper just drops the file into a
  // FormData payload (no body type — multipart is built by the browser).
  uploadImage(file: File): Observable<UploadedImage> {
    const fd = new FormData();
    fd.append('file', file);
    return this.http.post<UploadedImage>('/api/images', fd);
  }
}

export interface ReviewListParams {
  sort?: 'newest' | 'helpful' | 'highest' | 'lowest';
  rating?: number | null;
  hasPhotos?: boolean;
  cursor?: string;
}
