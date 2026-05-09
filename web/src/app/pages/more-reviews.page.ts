import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ReviewCard } from './review-card';
import { ApiService } from '../services/api.service';
import { ReviewSort, ReviewsPage } from '../models';

const PAGE_SIZE = 20;
const RATING_OPTIONS: ReadonlyArray<1 | 2 | 3 | 4 | 5> = [5, 4, 3, 2, 1];

@Component({
  imports: [FormsModule, RouterLink, ReviewCard],
  template: `
    <p><a [routerLink]="['/products', slug()]" class="link">← Back to product</a></p>
    <h1>All reviews</h1>

    <div class="controls">
      <label>
        Sort
        <select [(ngModel)]="sort" (ngModelChange)="reload()">
          <option value="Newest">Newest</option>
          <option value="Helpful">Most helpful</option>
          <option value="Highest">Highest rated</option>
          <option value="Lowest">Lowest rated</option>
        </select>
      </label>
      <fieldset class="ratings">
        <legend>Filter by rating</legend>
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
        With photos only
      </label>
    </div>

    @if (page(); as pg) {
      @for (r of pg.items; track r.id) {
        <app-review-card
          [review]="r"
          [productSlug]="slug()"
          (vote)="onVote($event)"
          (del)="onDelete($event)"
        />
      } @empty {
        <p class="muted">No reviews match.</p>
      }

      @if (pg.totalCount > 0) {
        <nav class="pager">
          <button type="button" class="link" (click)="goTo(pg.page - 1)" [disabled]="pg.page <= 1">
            ← Prev
          </button>
          <span class="muted">Page {{ pg.page }} of {{ totalPages(pg) }}</span>
          <button
            type="button"
            class="link"
            (click)="goTo(pg.page + 1)"
            [disabled]="pg.page >= totalPages(pg)"
          >
            Next →
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
    `,
  ],
})
export class MoreReviewsPage {
  private readonly api = inject(ApiService);
  readonly slug = input.required<string>();

  protected readonly ratingOptions = RATING_OPTIONS;
  protected sort: ReviewSort = 'Helpful';
  protected selectedRatings = new Set<number>();
  protected hasPhotos = false;

  protected readonly page = signal<ReviewsPage | null>(null);

  constructor() {
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
    this.api
      .listReviews(this.slug(), {
        sort: this.sort,
        ratings: Array.from(this.selectedRatings),
        hasPhotos: this.hasPhotos,
        page,
        pageSize: PAGE_SIZE,
      })
      .subscribe((pg) => this.page.set(pg));
  }

  onVote(e: { id: string; isUpvote: boolean }) {
    this.api.voteReview(e.id, e.isUpvote).subscribe(() => setTimeout(() => this.reload(), 400));
  }

  onDelete(id: string) {
    if (!confirm('Delete this review? Reviews older than an hour need moderator approval.')) return;
    this.api.deleteReview(id).subscribe(() => setTimeout(() => this.reload(), 400));
  }
}
