import { Component, ViewChild, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ReviewCard } from './review-card';
import { TurnstileComponent } from '../components/turnstile';
import { ApiService } from '../services/api.service';
import { AuthService } from '../services/auth.service';
import { ReviewSort, SortDirection, ReviewsPage } from '../models';
import { TPipe } from '../pipes/t.pipe';
import { I18nService } from '../services/i18n.service';
import { handleReauthRequired } from '../services/reauth';

const PAGE_SIZE = 20;
const RATING_OPTIONS: ReadonlyArray<1 | 2 | 3 | 4 | 5> = [5, 4, 3, 2, 1];

// Each option compiles down to (sort, direction). Keeping the UI as one
// dropdown matches the prior shape; the orthogonal API is exposed below.
type SortOption =
  | 'date-desc'
  | 'date-asc'
  | 'helpful-desc'
  | 'helpful-asc'
  | 'rating-desc'
  | 'rating-asc';
const SORT_OPTIONS: ReadonlyArray<{
  value: SortOption;
  sort: ReviewSort;
  direction: SortDirection;
}> = [
  { value: 'helpful-desc', sort: 'Helpful', direction: 'Desc' },
  { value: 'helpful-asc', sort: 'Helpful', direction: 'Asc' },
  { value: 'date-desc', sort: 'Date', direction: 'Desc' },
  { value: 'date-asc', sort: 'Date', direction: 'Asc' },
  { value: 'rating-desc', sort: 'Rating', direction: 'Desc' },
  { value: 'rating-asc', sort: 'Rating', direction: 'Asc' },
];

@Component({
  imports: [FormsModule, RouterLink, ReviewCard, TurnstileComponent, TPipe],
  template: `
    <p>
      <a [routerLink]="['/products', slug()]" class="link">{{ 'reviewList.backToProduct' | t }}</a>
    </p>
    <h1>{{ 'reviewList.heading' | t }}</h1>

    <div class="controls">
      <label>
        {{ 'reviewList.sort' | t }}
        <select [(ngModel)]="sortOption" (ngModelChange)="reload()">
          <option value="helpful-desc">{{ 'reviewList.sortHelpfulDesc' | t }}</option>
          <option value="helpful-asc">{{ 'reviewList.sortHelpfulAsc' | t }}</option>
          <option value="date-desc">{{ 'reviewList.sortDateDesc' | t }}</option>
          <option value="date-asc">{{ 'reviewList.sortDateAsc' | t }}</option>
          <option value="rating-desc">{{ 'reviewList.sortRatingDesc' | t }}</option>
          <option value="rating-asc">{{ 'reviewList.sortRatingAsc' | t }}</option>
        </select>
      </label>
      <fieldset class="ratings">
        <legend>{{ 'reviewList.filterRating' | t }}</legend>
        @for (n of ratingOptions; track n) {
          <label class="rating-pick">
            <input
              type="checkbox"
              [checked]="selectedRatings.has(n)"
              (change)="toggleRating(n, $event)"
            />
            {{ n }}★
          </label>
        }
      </fieldset>
      <label>
        <input type="checkbox" [(ngModel)]="hasPhotos" (ngModelChange)="reload()" />
        {{ 'reviewList.withPhotosOnly' | t }}
      </label>
    </div>

    @if (actionError(); as msg) {
      <p class="error" role="alert">{{ msg }}</p>
    }

    @if (page(); as pg) {
      @for (r of pg.items; track r.id) {
        <app-review-card
          [review]="r"
          [productSlug]="slug()"
          [busy]="busy() === r.id"
          (vote)="onVote($event)"
          (del)="onDelete($event)"
        />
      } @empty {
        <p class="muted">{{ 'reviewList.noMatches' | t }}</p>
      }

      @if (auth.authenticated() && pg.items.length > 0 && siteKey(); as sk) {
        <div class="turnstile-row">
          <app-turnstile [siteKey]="sk" (tokenChange)="turnstileToken = $event" />
        </div>
      }

      @if (pg.totalCount > 0) {
        <nav class="pager">
          <button type="button" class="link" (click)="goTo(pg.page - 1)" [disabled]="pg.page <= 1">
            {{ 'common.prev' | t }}
          </button>
          <span class="muted"
            >{{ 'common.page' | t }} {{ pg.page }} {{ 'common.of' | t }} {{ totalPages(pg) }}</span
          >
          <button
            type="button"
            class="link"
            (click)="goTo(pg.page + 1)"
            [disabled]="pg.page >= totalPages(pg)"
          >
            {{ 'common.next' | t }}
          </button>
        </nav>
      }
    }
  `,
  styles: [
    `
      h1 {
        margin: 0 0 1rem;
      }
      .controls {
        display: flex;
        gap: 1rem;
        margin-bottom: 1rem;
        align-items: center;
        flex-wrap: wrap;
      }
      .controls label {
        display: flex;
        align-items: center;
        gap: 0.4rem;
        font-size: 0.9rem;
      }
      .controls select {
        padding: 0.25rem;
      }
      .ratings {
        display: flex;
        gap: 0.6rem;
        align-items: center;
        border: 1px solid #ccc;
        border-radius: 4px;
        padding: 0.25rem 0.5rem;
        margin: 0;
      }
      .ratings legend {
        padding: 0 0.25rem;
        font-size: 0.8rem;
        color: #666;
      }
      .rating-pick {
        font-size: 0.9rem;
      }
      .pager {
        display: flex;
        gap: 1rem;
        align-items: center;
        margin: 1rem 0;
      }
      .muted {
        color: #666;
      }
      .link {
        background: none;
        border: none;
        padding: 0;
        color: #2563eb;
        cursor: pointer;
        font-size: 0.9rem;
        text-decoration: none;
      }
      .link:hover:not(:disabled) {
        text-decoration: underline;
      }
      .link:disabled {
        color: #999;
        cursor: not-allowed;
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
    `,
  ],
})
export class MoreReviewsPage {
  private readonly api = inject(ApiService);
  private readonly i18n = inject(I18nService);
  protected readonly auth = inject(AuthService);
  readonly slug = input.required<string>();

  protected readonly ratingOptions = RATING_OPTIONS;
  protected sortOption: SortOption = 'helpful-desc';
  protected selectedRatings = new Set<number>();
  protected hasPhotos = false;

  protected readonly page = signal<ReviewsPage | null>(null);
  protected readonly busy = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly siteKey = signal<string | null>(null);

  protected turnstileToken = '';
  @ViewChild(TurnstileComponent) private turnstileWidget?: TurnstileComponent;

  constructor() {
    this.api.config().subscribe((c) => this.siteKey.set(c.turnstileSiteKey));
    effect(() => {
      const s = this.slug();
      if (s) this.loadPage(1);
    });
  }

  reload() {
    this.loadPage(1);
  }

  goTo(page: number) {
    this.loadPage(page);
  }

  toggleRating(n: number, evt: Event) {
    const checked = (evt.target as HTMLInputElement).checked;
    if (checked) this.selectedRatings.add(n);
    else this.selectedRatings.delete(n);
    this.reload();
  }

  totalPages(pg: ReviewsPage): number {
    return Math.max(1, Math.ceil(pg.totalCount / pg.pageSize));
  }

  private loadPage(page: number) {
    const opt = SORT_OPTIONS.find((o) => o.value === this.sortOption)!;
    this.api
      .listReviews(this.slug(), {
        sort: opt.sort,
        direction: opt.direction,
        ratings: Array.from(this.selectedRatings),
        hasPhotos: this.hasPhotos,
        page,
        pageSize: PAGE_SIZE,
      })
      .subscribe((pg) => this.page.set(pg));
  }

  async onVote(e: { id: string; isUpvote: boolean | null }) {
    this.busy.set(e.id);
    this.actionError.set(null);
    let token = '';
    if (e.isUpvote !== null) {
      const t = await this.waitForTurnstileToken();
      if (!t) {
        this.actionError.set(this.i18n.t('vote.turnstileRequired'));
        this.busy.set(null);
        return;
      }
      token = t;
    }
    const req$ =
      e.isUpvote === null
        ? this.api.removeVote(e.id)
        : this.api.voteReview(e.id, e.isUpvote, token);
    req$.subscribe({
      next: (res) => {
        // Sync write — patch the affected row in place. Sort order on the
        // current page may now be slightly stale until the user re-sorts /
        // navigates, which is acceptable for a single click.
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

  async onDelete(id: string) {
    if (!confirm(this.i18n.t('vote.deleteConfirm'))) return;
    this.busy.set(id);
    this.actionError.set(null);
    const token = await this.waitForTurnstileToken();
    if (!token) {
      this.actionError.set(this.i18n.t('vote.turnstileRequired'));
      this.busy.set(null);
      return;
    }
    this.api.deleteReview(id, token).subscribe({
      next: () => {
        setTimeout(() => this.reload(), 400);
        this.turnstileWidget?.reset();
        this.busy.set(null);
      },
      error: (err) => {
        if (
          handleReauthRequired(
            err,
            `/products/${this.slug()}/reviews`,
            this.i18n.t('vote.reauthPrompt'),
          )
        )
          return;
        this.actionError.set(this.errorMessage(err, 'vote.deleteFailed'));
        this.turnstileWidget?.reset();
        this.busy.set(null);
      },
    });
  }

  // Turnstile renders async; wait briefly for the token rather than failing
  // a click that races widget mount + Cloudflare token issuance.
  private async waitForTurnstileToken(timeoutMs = 8000): Promise<string> {
    const start = Date.now();
    while (!this.turnstileToken && Date.now() - start < timeoutMs) {
      await new Promise((r) => setTimeout(r, 100));
    }
    return this.turnstileToken;
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
