import { Component, ViewChild, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ReviewCard } from './review-card';
import { StarRating } from '../components/star-rating';
import { TurnstileComponent } from '../components/turnstile';
import { ApiService } from '../services/api.service';
import { AuthService } from '../services/auth.service';
import { ProductDetail, ReviewsPage } from '../models';
import { TPipe } from '../pipes/t.pipe';
import { I18nService } from '../services/i18n.service';
import { handleReauthRequired } from '../services/reauth';

@Component({
  imports: [RouterLink, StarRating, ReviewCard, TurnstileComponent, TPipe],
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
        @if (pg.myReview; as mine) {
          <section class="my-review" aria-label="{{ 'products.yourReview' | t }}">
            <h3>{{ 'products.yourReview' | t }}</h3>
            @if (mine.status === 'Pending') {
              <p class="status pending">{{ 'products.statusPending' | t }}</p>
            } @else if (mine.status === 'Rejected') {
              <p class="status rejected">{{ 'products.statusRejected' | t }}</p>
            }
            <app-review-card
              [review]="mine"
              [productSlug]="p.slug"
              [busy]="busy() === mine.id"
              (vote)="onVote($event)"
              (del)="onDelete($event)"
            />
          </section>
        }
        @if (pg.items.length === 0 && !pg.myReview) {
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
        @if (auth.authenticated() && pg.items.length > 0 && siteKey(); as sk) {
          <div class="turnstile-row">
            <app-turnstile [siteKey]="sk" (tokenChange)="turnstileToken = $event" />
          </div>
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
        background: var(--color-error-container);
        color: var(--color-on-error-container);
        padding: 0.5rem 0.75rem;
        border-radius: 4px;
        margin: 0.5rem 0;
      }
      .turnstile-row {
        margin-top: 1rem;
      }
      .my-review {
        border: 1px solid var(--color-outline-variant);
        border-radius: 6px;
        padding: 0.75rem 1rem;
        margin: 0.5rem 0 1rem;
        background: var(--color-surface-container, transparent);
      }
      .my-review h3 {
        margin: 0;
        font-size: 0.95rem;
        color: var(--color-on-surface-muted);
      }
      .status {
        margin: 0.4rem 0 0;
        padding: 0.35rem 0.6rem;
        border-radius: 4px;
        font-size: 0.85rem;
      }
      .status.pending {
        background: var(--color-warning-container, #fef3c7);
        color: var(--color-on-warning-container, #92400e);
      }
      .status.rejected {
        background: var(--color-error-container, #fee2e2);
        color: var(--color-on-error-container, #991b1b);
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
  protected readonly siteKey = signal<string | null>(null);

  protected turnstileToken = '';
  @ViewChild(TurnstileComponent) private turnstileWidget?: TurnstileComponent;

  constructor() {
    this.api.config().subscribe((c) => this.siteKey.set(c.turnstileSiteKey));
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

  onVote(e: { id: string; isUpvote: boolean | null }) {
    if (e.isUpvote !== null && !this.turnstileToken) {
      this.actionError.set(this.i18n.t('vote.turnstileRequired'));
      return;
    }
    const token = this.turnstileToken;
    this.busy.set(e.id);
    this.actionError.set(null);
    const req$ =
      e.isUpvote === null
        ? this.api.removeVote(e.id)
        : this.api.voteReview(e.id, e.isUpvote, token);
    req$.subscribe({
      next: (res) => {
        // Sync write — patch the affected row in place. No refetch needed.
        const pg = this.page();
        if (pg) {
          this.page.set({
            ...pg,
            items: pg.items.map((r) =>
              r.id === e.id ? { ...r, score: res.score, myVote: res.myVote } : r,
            ),
          });
        }
        this.turnstileWidget?.reset();
        this.busy.set(null);
      },
      error: (err) => {
        this.actionError.set(this.errorMessage(err, 'vote.voteFailed'));
        this.turnstileWidget?.reset();
        this.busy.set(null);
      },
    });
  }

  onDelete(id: string) {
    if (!confirm(this.i18n.t('vote.deleteConfirm'))) return;
    if (!this.turnstileToken) {
      this.actionError.set(this.i18n.t('vote.turnstileRequired'));
      return;
    }
    const token = this.turnstileToken;
    this.busy.set(id);
    this.actionError.set(null);
    this.api.deleteReview(id, token).subscribe({
      next: () => {
        setTimeout(() => this.fetchAll(this.slug()), 400);
        this.turnstileWidget?.reset();
        this.busy.set(null);
      },
      error: (err) => {
        if (handleReauthRequired(err, `/products/${this.slug()}`)) return;
        this.actionError.set(this.errorMessage(err, 'vote.deleteFailed'));
        this.turnstileWidget?.reset();
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
