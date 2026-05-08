import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ReviewCard } from './review-card';
import { ApiService } from '../services/api.service';
import { ReviewItem } from '../models';

type Sort = 'newest' | 'helpful' | 'highest' | 'lowest';

@Component({
  imports: [FormsModule, RouterLink, ReviewCard],
  template: `
    <p><a [routerLink]="['/products', slug()]" class="link">← Back to product</a></p>
    <h1>All reviews</h1>

    <div class="controls">
      <label>
        Sort
        <select [(ngModel)]="sort" (ngModelChange)="reload()">
          <option value="newest">Newest</option>
          <option value="helpful">Most helpful</option>
          <option value="highest">Highest rated</option>
          <option value="lowest">Lowest rated</option>
        </select>
      </label>
      <label>
        Rating
        <select [(ngModel)]="rating" (ngModelChange)="reload()">
          <option [ngValue]="null">All</option>
          @for (n of [5, 4, 3, 2, 1]; track n) {
            <option [ngValue]="n">{{ n }} stars</option>
          }
        </select>
      </label>
      <label>
        <input type="checkbox" [(ngModel)]="hasPhotos" (ngModelChange)="reload()" />
        With photos only
      </label>
    </div>

    @for (r of items(); track r.id) {
      <app-review-card [review]="r" [productSlug]="slug()" (vote)="onVote($event)" (del)="onDelete($event)" />
    } @empty {
      @if (loaded()) { <p class="muted">No reviews match.</p> }
    }

    @if (cursor()) {
      <p><button type="button" class="link" (click)="loadMore()">Load more</button></p>
    }
  `,
  styles: [`
    h1 { margin: 0 0 1rem; }
    .controls { display: flex; gap: 1rem; margin-bottom: 1rem; align-items: center; }
    .controls label { display: flex; align-items: center; gap: 0.4rem; font-size: 0.9rem; }
    .controls select { padding: 0.25rem; }
    .muted { color: #666; }
    .link { background: none; border: none; padding: 0; color: #2563eb; cursor: pointer;
      font-size: 0.9rem; text-decoration: none; }
    .link:hover { text-decoration: underline; }
  `],
})
export class MoreReviewsPage {
  private readonly api = inject(ApiService);
  readonly slug = input.required<string>();

  protected sort: Sort = 'newest';
  protected rating: number | null = null;
  protected hasPhotos = false;

  protected readonly items = signal<ReviewItem[]>([]);
  protected readonly cursor = signal<string | null>(null);
  protected readonly loaded = signal(false);

  constructor() {
    effect(() => {
      const s = this.slug();
      if (s) this.reload();
    });
  }

  reload() {
    this.items.set([]);
    this.cursor.set(null);
    this.loaded.set(false);
    this.api
      .listReviews(this.slug(), { sort: this.sort, rating: this.rating, hasPhotos: this.hasPhotos })
      .subscribe((pg) => {
        this.items.set(pg.items);
        this.cursor.set(pg.nextCursor);
        this.loaded.set(true);
      });
  }

  loadMore() {
    const c = this.cursor();
    if (!c) return;
    this.api
      .listReviews(this.slug(), {
        sort: this.sort,
        rating: this.rating,
        hasPhotos: this.hasPhotos,
        cursor: c,
      })
      .subscribe((pg) => {
        this.items.update((cur) => [...cur, ...pg.items]);
        this.cursor.set(pg.nextCursor);
      });
  }

  onVote(e: { id: string; value: 1 | -1 }) {
    this.api.voteReview(e.id, e.value).subscribe(() => setTimeout(() => this.reload(), 400));
  }

  onDelete(id: string) {
    if (!confirm('Delete this review? Reviews older than an hour need moderator approval.')) return;
    this.api.deleteReview(id).subscribe(() => setTimeout(() => this.reload(), 400));
  }
}
