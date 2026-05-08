import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { StarRating } from '../components/star-rating';
import { ApiService } from '../services/api.service';
import { ReviewItem } from '../models';

@Component({
  imports: [FormsModule, RouterLink, StarRating],
  template: `
    @if (review(); as r) {
      <p><a [routerLink]="['/products', slug()]" class="link">← Back</a></p>
      <h1>Edit your review</h1>
      <p class="muted">
        Edits to reviews older than an hour go through moderator approval before they apply. The
        Temporal UI is where moderators send approve/reject signals.
      </p>

      <form (submit)="save($event)">
        <label
          >Your rating
          <app-star-rating [value]="rating" [interactive]="true" (valueChange)="rating = $event" />
        </label>
        <label
          >Title (optional)
          <input type="text" [(ngModel)]="title" name="title" maxlength="120" />
        </label>
        <label
          >Review
          <textarea
            [(ngModel)]="body"
            name="body"
            rows="6"
            required
            minlength="10"
            maxlength="4000"
          ></textarea>
        </label>
        <label
          >Image URLs (one per line, optional)
          <textarea [(ngModel)]="imageUrlsRaw" name="imageUrls" rows="3"></textarea>
        </label>

        @if (error(); as e) {
          <p class="error">{{ e }}</p>
        }

        <button type="submit" [disabled]="saving() || body.trim().length < 10">
          {{ saving() ? 'Saving...' : 'Save changes' }}
        </button>
      </form>
    } @else if (notFound()) {
      <p>Review not found.</p>
    }
  `,
  styles: [
    `
      label {
        display: block;
        margin: 0.75rem 0;
      }
      input[type='text'],
      textarea {
        display: block;
        width: 100%;
        padding: 0.5rem;
        margin-top: 0.25rem;
        border: 1px solid #ccc;
        border-radius: 4px;
        font: inherit;
      }
      button {
        padding: 0.5rem 1rem;
        background: #2563eb;
        color: #fff;
        border: none;
        border-radius: 4px;
        cursor: pointer;
        font: inherit;
      }
      button:disabled {
        background: #93c5fd;
        cursor: not-allowed;
      }
      .muted {
        color: #666;
        font-size: 0.9rem;
      }
      .error {
        color: #b91c1c;
      }
      .link {
        color: #2563eb;
        text-decoration: none;
      }
    `,
  ],
})
export class EditReviewPage {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  readonly slug = input.required<string>();
  readonly reviewId = input.required<string>();

  protected readonly review = signal<ReviewItem | null>(null);
  protected readonly notFound = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected rating = 5;
  protected title = '';
  protected body = '';
  protected imageUrlsRaw = '';

  constructor() {
    effect(() => {
      const s = this.slug();
      const id = this.reviewId();
      if (!s || !id) return;
      // Hydrate from the product's first-page review list. Acceptable
      // because the only way a user reaches this page is from their own
      // review on the product page (where it's already loaded). For deep-
      // links from a notification etc., a /api/reviews/:id endpoint would
      // be the right add — out of scope for the kickoff.
      this.api.listReviews(s, { sort: 'newest' }).subscribe((pg) => {
        const found = pg.items.find((r) => r.id === id);
        if (!found) {
          // Try a few more pages before giving up.
          this.lookupDeep(s, id, pg.nextCursor);
          return;
        }
        this.fillFrom(found);
      });
    });
  }

  private lookupDeep(slug: string, id: string, cursor: string | null) {
    if (!cursor) {
      this.notFound.set(true);
      return;
    }
    this.api.listReviews(slug, { sort: 'newest', cursor }).subscribe((pg) => {
      const found = pg.items.find((r) => r.id === id);
      if (found) this.fillFrom(found);
      else this.lookupDeep(slug, id, pg.nextCursor);
    });
  }

  private fillFrom(r: ReviewItem) {
    this.review.set(r);
    this.rating = r.rating;
    this.title = r.title ?? '';
    this.body = r.body;
    this.imageUrlsRaw = r.imageUrls.join('\n');
  }

  save(e: Event) {
    e.preventDefault();
    const r = this.review();
    if (!r) return;
    this.saving.set(true);
    this.error.set(null);
    const imageUrls = this.imageUrlsRaw
      .split('\n')
      .map((s) => s.trim())
      .filter(Boolean);
    this.api
      .editReview(r.id, {
        rating: this.rating,
        title: this.title.trim() || undefined,
        body: this.body.trim(),
        imageUrls,
      })
      .subscribe({
        next: () => this.router.navigate(['/products', this.slug()]),
        error: (err) => {
          this.error.set(err.error ?? err.message ?? 'Save failed');
          this.saving.set(false);
        },
      });
  }
}
