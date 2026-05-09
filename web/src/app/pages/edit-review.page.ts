import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { StarRating } from '../components/star-rating';
import { ApiService } from '../services/api.service';
import { I18nService } from '../services/i18n.service';
import { Limits, ReviewItem } from '../models';

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
        <label class="field">
          Title
          <input
            type="text"
            [(ngModel)]="title"
            name="title"
            required
            [maxlength]="Limits.titleMax"
          />
        </label>
        <small class="counter" [class.over]="title.length > Limits.titleMax">
          {{ title.length }}/{{ Limits.titleMax }}
        </small>
        <label class="field">
          Review
          <textarea
            [(ngModel)]="body"
            name="body"
            rows="6"
            required
            [minlength]="Limits.bodyMin"
            [maxlength]="Limits.bodyMax"
          ></textarea>
        </label>
        <small
          class="counter"
          [class.over]="body.length > Limits.bodyMax"
          [class.under]="body.trim().length > 0 && body.trim().length < Limits.bodyMin"
        >
          {{ body.length }}/{{ Limits.bodyMax }}
          @if (body.trim().length > 0 && body.trim().length < Limits.bodyMin) {
            · {{ Limits.bodyMin }} min
          }
        </small>

        <fieldset>
          <legend>Photos (up to {{ Limits.maxImages }})</legend>
          @if (imageUrls.length > 0) {
            <ul class="uploaded">
              @for (u of imageUrls; track u) {
                <li>
                  <img [src]="u" alt="review image" />
                  <button type="button" (click)="removeImage(u)">Remove</button>
                </li>
              }
            </ul>
          }
          <input
            type="file"
            accept="image/png,image/jpeg,image/webp,image/gif"
            multiple
            (change)="onFiles($event)"
            [disabled]="imageUrls.length >= Limits.maxImages"
          />
          <p class="muted">
            Each image must be {{ Limits.maxImageBytes / (1024 * 1024) }} MB or less.
          </p>
          @if (uploadError(); as ue) {
            <p class="error">{{ ue }}</p>
          }
        </fieldset>

        @if (error(); as e) {
          <p class="error">{{ e }}</p>
        }

        <button
          type="submit"
          [disabled]="
            saving() ||
            title.trim().length === 0 ||
            title.length > Limits.titleMax ||
            body.trim().length < Limits.bodyMin ||
            body.length > Limits.bodyMax
          "
        >
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
      .counter {
        display: block;
        margin-top: 0.25rem;
        color: #666;
        font-size: 0.8rem;
        text-align: right;
      }
      .counter.over {
        color: #b91c1c;
      }
      .counter.under {
        color: #b45309;
      }
      fieldset {
        margin: 0.75rem 0;
        padding: 0.75rem;
        border: 1px solid #ccc;
        border-radius: 4px;
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
      .uploaded {
        list-style: none;
        padding: 0;
        margin: 0 0 0.5rem;
        display: flex;
        gap: 0.5rem;
        flex-wrap: wrap;
      }
      .uploaded li {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: 0.25rem;
      }
      .uploaded img {
        width: 96px;
        height: 96px;
        object-fit: cover;
        border-radius: 4px;
        border: 1px solid #eee;
      }
      .uploaded button {
        background: transparent;
        color: #b91c1c;
        padding: 0.125rem 0.5rem;
        font-size: 0.85rem;
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
  private readonly i18n = inject(I18nService);

  readonly slug = input.required<string>();
  readonly reviewId = input.required<string>();
  protected readonly Limits = Limits;

  protected readonly review = signal<ReviewItem | null>(null);
  protected readonly notFound = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly uploadError = signal<string | null>(null);

  protected rating = 5;
  protected title = '';
  protected body = '';
  protected imageUrls: string[] = [];

  constructor() {
    effect(() => {
      const s = this.slug();
      const id = this.reviewId();
      if (!s || !id) return;
      // Hydrate from the product's review list — paged forward by page
      // number until the target review id appears. Acceptable because the
      // only way a user reaches this page is from their own review on the
      // product page (where it's already loaded). For deep-links from a
      // notification etc., a /api/reviews/:id endpoint would be the right
      // add — out of scope for the kickoff.
      this.lookupOnPage(s, id, 1);
    });
  }

  private lookupOnPage(slug: string, id: string, page: number) {
    this.api.listReviews(slug, { sort: 'Newest', page }).subscribe((pg) => {
      const found = pg.items.find((r) => r.id === id);
      if (found) {
        this.fillFrom(found);
        return;
      }
      const totalPages = Math.max(1, Math.ceil(pg.totalCount / pg.pageSize));
      if (page >= totalPages) {
        this.notFound.set(true);
        return;
      }
      this.lookupOnPage(slug, id, page + 1);
    });
  }

  private fillFrom(r: ReviewItem) {
    this.review.set(r);
    this.rating = r.rating;
    this.title = r.title;
    this.body = r.body;
    this.imageUrls = [...r.imageUrls];
  }

  onFiles(e: Event) {
    const input = e.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    if (!files.length) return;
    this.uploadError.set(null);

    const slotsRemaining = Limits.maxImages - this.imageUrls.length;
    if (files.length > slotsRemaining) {
      this.uploadError.set(
        `You can attach at most ${Limits.maxImages} images (${slotsRemaining} remaining).`,
      );
      return;
    }

    for (const f of files) {
      if (f.size > Limits.maxImageBytes) {
        this.uploadError.set(
          `${f.name} is too large (max ${Limits.maxImageBytes / (1024 * 1024)} MB).`,
        );
        return;
      }
      if (!Limits.allowedImageTypes.includes(f.type as (typeof Limits.allowedImageTypes)[number])) {
        this.uploadError.set(`${f.name}: unsupported type ${f.type || 'unknown'}.`);
        return;
      }
    }

    for (const f of files) {
      this.api.uploadImage(f).subscribe({
        next: (res) => this.imageUrls.push(res.url),
        error: (err) => this.uploadError.set(err.error ?? err.message ?? 'Upload failed'),
      });
    }
  }

  removeImage(url: string) {
    this.imageUrls = this.imageUrls.filter((u) => u !== url);
  }

  save(e: Event) {
    e.preventDefault();
    const r = this.review();
    if (!r) return;
    this.saving.set(true);
    this.error.set(null);
    this.api
      .editReview(r.id, {
        rating: this.rating,
        title: this.title.trim(),
        body: this.body.trim(),
        imageUrls: this.imageUrls,
        // Re-stamp the language with the editor's current UI locale: if you
        // switch to Czech and edit your review, the stored language reflects
        // that choice.
        language: this.i18n.locale(),
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
