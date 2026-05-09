import { Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ReviewCard } from './review-card';
import { StarRating } from '../components/star-rating';
import { ApiService } from '../services/api.service';
import { AuthService } from '../services/auth.service';
import { ProductDetail, ReviewsPage } from '../models';
import { TPipe } from '../pipes/t.pipe';
import { I18nService } from '../services/i18n.service';

@Component({
  imports: [RouterLink, StarRating, ReviewCard, TPipe],
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
                {{ 'products.editYourReview' | t }}
              </a>
            } @else {
              <a [routerLink]="['/products', p.slug, 'review', 'new']" class="btn">{{
                'products.writeReview' | t
              }}</a>
            }
          } @else {
            <a [href]="loginHref()" class="btn">{{ 'products.signInToReview' | t }}</a>
          }
        </div>
      </article>

      <h2>{{ 'products.reviewsHeading' | t }}</h2>
      @if (actionError(); as msg) {
        <p class="error" role="alert">{{ msg }}</p>
      }
      @if (page(); as pg) {
        @if (pg.items.length === 0) {
          <p class="muted">{{ 'products.noReviews' | t }}</p>
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
              <a [routerLink]="['/products', p.slug, 'reviews']" class="btn">{{
                'products.moreReviews' | t
              }}</a>
            </p>
          }
        }
      }
    } @else if (notFound()) {
      <p>{{ 'products.notFound' | t }}</p>
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
      .error {
        background: var(--color-error-container, #fee2e2);
        color: var(--color-on-error-container, #991b1b);
        padding: 0.5rem 0.75rem;
        border-radius: 4px;
        margin: 0.5rem 0;
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
  private readonly i18n = inject(I18nService);

  readonly slug = input.required<string>();

  protected readonly product = signal<ProductDetail | null>(null);
  protected readonly page = signal<ReviewsPage | null>(null);
  protected readonly notFound = signal(false);
  protected readonly busy = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);

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
    this.actionError.set(null);
    this.api.voteReview(e.id, e.isUpvote).subscribe({
      next: () => {
        // Workflow runs async; the worker invalidates the cache so a quick
        // re-fetch usually catches the updated score.
        setTimeout(() => this.fetchAll(this.slug()), 400);
        this.busy.set(null);
      },
      error: (err) => {
        this.actionError.set(this.errorMessage(err, 'vote.voteFailed'));
        this.busy.set(null);
      },
    });
  }

  onDelete(id: string) {
    if (!confirm(this.i18n.t('vote.deleteConfirm'))) return;
    this.busy.set(id);
    this.actionError.set(null);
    this.api.deleteReview(id).subscribe({
      next: () => {
        setTimeout(() => this.fetchAll(this.slug()), 400);
        this.busy.set(null);
      },
      error: (err) => {
        this.actionError.set(this.errorMessage(err, 'vote.deleteFailed'));
        this.busy.set(null);
      },
    });
  }

  private errorMessage(
    err: { status?: number; error?: unknown; message?: string },
    fallback: string,
  ): string {
    const body = typeof err.error === 'string' ? err.error : null;
    const status = err.status ? `${err.status}` : '';
    const detail = body || err.message || '';
    const base = this.i18n.t(fallback);
    return detail ? `${base} (${[status, detail].filter(Boolean).join(' ')})` : base;
  }
}
