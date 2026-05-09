import { Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ReviewCard } from './review-card';
import { StarRating } from '../components/star-rating';
import { ApiService } from '../services/api.service';
import { AuthService } from '../services/auth.service';
import { ProductDetail, ReviewsPage } from '../models';

@Component({
  imports: [RouterLink, StarRating, ReviewCard],
  template: `
    @if (product(); as p) {
      <article class="product">
        @if (p.imageUrl) {
          <img [src]="p.imageUrl" [alt]="p.name" />
        }
        <div>
          <h1>{{ p.name }}</h1>
          <div class="meta">
            <app-star-rating [value]="p.averageRating" />
            <span class="muted"
              >{{ p.averageRating.toFixed(1) }} · {{ p.reviewCount }} reviews</span
            >
          </div>
          <p class="desc">{{ p.description }}</p>
          @if (auth.authenticated()) {
            @if (p.myReviewId) {
              <a [routerLink]="['/products', p.slug, 'review', p.myReviewId, 'edit']" class="btn">
                Edit your review
              </a>
            } @else {
              <a [routerLink]="['/products', p.slug, 'review', 'new']" class="btn"
                >Write a review</a
              >
            }
          } @else {
            <a [href]="loginHref()" class="btn">Sign in to write a review</a>
          }
        </div>
      </article>

      <h2>Reviews</h2>
      @if (page(); as pg) {
        @if (pg.items.length === 0) {
          <p class="muted">No reviews yet — be the first.</p>
        } @else {
          @for (r of pg.items; track r.id) {
            <app-review-card
              [review]="r"
              [productSlug]="p.slug"
              [busy]="busy() === r.id"
              (vote)="onVote($event)"
              (del)="onDelete($event)"
            />
          }
          @if (pg.totalCount > pg.items.length) {
            <p>
              <a [routerLink]="['/products', p.slug, 'reviews']" class="btn">More reviews →</a>
            </p>
          }
        }
      }
    } @else if (notFound()) {
      <p>Product not found.</p>
    }
  `,
  styles: [
    `
      .product {
        display: grid;
        grid-template-columns: 320px 1fr;
        gap: 1.5rem;
        margin-bottom: 2rem;
      }
      .product img {
        width: 100%;
        border-radius: 8px;
        object-fit: cover;
      }
      h1 {
        margin: 0 0 0.5rem;
      }
      .meta {
        display: flex;
        gap: 0.5rem;
        align-items: center;
        margin-bottom: 0.5rem;
      }
      .muted {
        color: #666;
      }
      .desc {
        line-height: 1.5;
        margin: 0.5rem 0 1.25rem;
      }
      h2 {
        margin-top: 1.5rem;
      }
      .btn {
        display: inline-block;
        padding: 0.5rem 1rem;
        background: #2563eb;
        color: #fff;
        border-radius: 4px;
        text-decoration: none;
        font-size: 0.95rem;
      }
      .btn:hover {
        background: #1d4ed8;
      }
      @media (max-width: 700px) {
        .product {
          grid-template-columns: 1fr;
        }
      }
    `,
  ],
})
export class ProductDetailPage {
  private readonly api = inject(ApiService);
  protected readonly auth = inject(AuthService);

  readonly slug = input.required<string>();

  protected readonly product = signal<ProductDetail | null>(null);
  protected readonly page = signal<ReviewsPage | null>(null);
  protected readonly notFound = signal(false);
  protected readonly busy = signal<string | null>(null);

  constructor() {
    effect(() => {
      const s = this.slug();
      if (s) this.fetchAll(s);
    });
  }

  private fetchAll(slug: string) {
    this.api.getProduct(slug).subscribe({
      next: (p) => this.product.set(p),
      error: (err) => {
        if (err.status === 404) this.notFound.set(true);
      },
    });
    this.api.listReviews(slug).subscribe((pg) => this.page.set(pg));
  }

  loginHref(): string {
    const ret = encodeURIComponent(`/products/${this.slug()}`);
    return `/auth/login?returnTo=${ret}`;
  }

  onVote(e: { id: string; isUpvote: boolean }) {
    this.busy.set(e.id);
    this.api.voteReview(e.id, e.isUpvote).subscribe({
      next: () => {
        // Workflow runs async; the worker invalidates the cache so a quick
        // re-fetch usually catches the updated score.
        setTimeout(() => this.fetchAll(this.slug()), 400);
        this.busy.set(null);
      },
      error: () => this.busy.set(null),
    });
  }

  onDelete(id: string) {
    if (!confirm('Delete this review? Reviews older than an hour need moderator approval.')) return;
    this.busy.set(id);
    this.api.deleteReview(id).subscribe({
      next: () => {
        setTimeout(() => this.fetchAll(this.slug()), 400);
        this.busy.set(null);
      },
      error: () => this.busy.set(null),
    });
  }
}
