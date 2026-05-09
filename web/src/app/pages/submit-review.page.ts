import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { StarRating } from '../components/star-rating';
import { TurnstileComponent } from '../components/turnstile';
import { ApiService } from '../services/api.service';
import { I18nService } from '../services/i18n.service';
import { Limits, ProductDetail } from '../models';

@Component({
  imports: [FormsModule, RouterLink, StarRating, TurnstileComponent],
  template: `
    @if (product(); as p) {
      <p>
        <a [routerLink]="['/products', p.slug]" class="link">← Back to {{ p.name }}</a>
      </p>
      <h1>Write a review for {{ p.name }}</h1>

      <form (submit)="submit($event)">
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
          <legend>Photos (optional, up to {{ Limits.maxImages }})</legend>
          <input
            type="file"
            accept="image/png,image/jpeg,image/webp,image/gif"
            multiple
            (change)="onFiles($event)"
            [disabled]="uploadedUrls.length >= Limits.maxImages"
          />
          <p class="muted">
            Each image must be {{ Limits.maxImageBytes / (1024 * 1024) }} MB or less.
          </p>
          @if (uploadError(); as ue) {
            <p class="error">{{ ue }}</p>
          }
          @if (uploadedUrls.length > 0) {
            <ul class="uploaded">
              @for (u of uploadedUrls; track u) {
                <li>
                  <img [src]="u" alt="uploaded" />
                  <button type="button" (click)="removeUploaded(u)">Remove</button>
                </li>
              }
            </ul>
          }
        </fieldset>

        @if (siteKey(); as sk) {
          <app-turnstile [siteKey]="sk" (tokenChange)="turnstileToken = $event" />
        }

        @if (error(); as e) {
          <p class="error">{{ e }}</p>
        }

        <button type="submit" [disabled]="!canSubmit() || submitting() || uploadsInFlight() > 0">
          @if (submitting()) {
            Submitting…
          } @else if (uploadsInFlight() > 0) {
            Waiting for {{ uploadsInFlight() }} upload(s)…
          } @else {
            Submit review
          }
        </button>
        @if (rating === 1 || rating === 2 || rating === 5) {
          <p class="muted">
            Reviews at this rating go through moderation before they appear. You can track approval
            in the Temporal UI.
          </p>
        }
      </form>
    } @else if (notFound()) {
      <p>Product not found.</p>
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
        margin: 0.5rem 0 0;
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
      .error {
        color: #b91c1c;
      }
      .muted {
        color: #666;
        font-size: 0.9rem;
      }
      .link {
        color: #2563eb;
        text-decoration: none;
      }
    `,
  ],
})
export class SubmitReviewPage {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18nService);

  readonly slug = input.required<string>();
  protected readonly Limits = Limits;

  protected readonly product = signal<ProductDetail | null>(null);
  protected readonly notFound = signal(false);
  protected readonly siteKey = signal<string | null>(null);
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly uploadError = signal<string | null>(null);
  // Counts in-flight upload calls. The submit button stays disabled while
  // this is > 0 — submitting before an upload finishes would either drop
  // the image (URL not yet known) or race the form's image-list state.
  protected readonly uploadsInFlight = signal(0);

  // SPA-side keeps the rating as a plain number (1..5) — the API's
  // RatingJsonConverter rejects out-of-range integers at the wire boundary,
  // so no client-side narrowing buys us anything but TypeScript ceremony at
  // every star-click handler.
  protected rating = 5;
  protected title = '';
  protected body = '';
  protected uploadedUrls: string[] = [];
  protected turnstileToken = '';

  constructor() {
    this.api.config().subscribe((c) => this.siteKey.set(c.turnstileSiteKey));
    effect(() => {
      const s = this.slug();
      if (!s) return;
      this.api.getProduct(s).subscribe({
        next: (p) => this.product.set(p),
        error: (err) => {
          if (err.status === 404) this.notFound.set(true);
        },
      });
    });
  }

  canSubmit(): boolean {
    return (
      this.title.trim().length > 0 &&
      this.title.length <= Limits.titleMax &&
      this.body.trim().length >= Limits.bodyMin &&
      this.body.length <= Limits.bodyMax &&
      this.rating >= 1 &&
      this.rating <= 5 &&
      this.turnstileToken.length > 0 &&
      this.uploadsInFlight() === 0
    );
  }

  onFiles(e: Event) {
    const input = e.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    if (!files.length) return;
    this.uploadError.set(null);

    const slotsRemaining = Limits.maxImages - this.uploadedUrls.length;
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
      this.uploadsInFlight.update((n) => n + 1);
      this.api.uploadImage(f).subscribe({
        next: (res) => {
          this.uploadedUrls.push(res.url);
          this.uploadsInFlight.update((n) => n - 1);
        },
        error: (err) => {
          this.uploadError.set(err.error ?? err.message ?? 'Upload failed');
          this.uploadsInFlight.update((n) => n - 1);
        },
      });
    }
  }

  removeUploaded(url: string) {
    this.uploadedUrls = this.uploadedUrls.filter((u) => u !== url);
  }

  submit(e: Event): void {
    e.preventDefault();
    const p = this.product();
    if (!p || !this.canSubmit()) return;

    this.submitting.set(true);
    this.error.set(null);

    this.api
      .submitReview({
        productId: p.id,
        rating: this.rating,
        title: this.title.trim(),
        body: this.body.trim(),
        imageUrls: this.uploadedUrls,
        language: this.i18n.locale(),
        turnstileToken: this.turnstileToken,
      })
      .subscribe({
        next: () => {
          this.router.navigate(['/products', p.slug]);
        },
        error: (err) => {
          this.error.set(err.error ?? err.message ?? 'Submit failed');
          this.submitting.set(false);
        },
      });
  }
}
