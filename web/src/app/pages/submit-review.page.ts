import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { StarRating } from '../components/star-rating';
import { TurnstileComponent } from '../components/turnstile';
import { ApiService } from '../services/api.service';
import { Limits, ProductDetail } from '../models';
import { TPipe } from '../pipes/t.pipe';
import { I18nService } from '../services/i18n.service';

@Component({
  imports: [FormsModule, RouterLink, StarRating, TurnstileComponent, TPipe],
  template: `
    @if (product(); as p) {
      <p>
        <a [routerLink]="['/products', p.slug]" class="link">{{
          'submit.backToProduct' | t: { name: p.name }
        }}</a>
      </p>
      <h1>{{ 'submit.heading' | t: { product: p.name } }}</h1>

      <form (submit)="submit($event)">
        <div class="rating-block">
          <div class="rating-label">{{ 'submit.rating' | t }}</div>
          <app-star-rating
            [value]="rating"
            [interactive]="true"
            size="large"
            (valueChange)="rating = $event"
          />
        </div>

        <label class="field">
          {{ 'submit.title' | t }}
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
          {{ 'submit.body' | t }}
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
          <legend>{{ 'submit.photos' | t: { n: Limits.maxImages } }}</legend>
          <input
            type="file"
            accept="image/png,image/jpeg,image/webp,image/gif"
            multiple
            (change)="onFiles($event)"
            [disabled]="uploadedUrls.length >= Limits.maxImages"
          />
          <p class="muted">
            {{ 'submit.photosLimit' | t: { mb: Limits.maxImageBytes / (1024 * 1024) } }}
          </p>
          @if (uploadError(); as ue) {
            <p class="error">{{ ue }}</p>
          }
          @if (uploadedUrls.length > 0) {
            <ul class="uploaded">
              @for (u of uploadedUrls; track u) {
                <li>
                  <img [src]="u" [alt]="'submit.uploadedImageAlt' | t" />
                  <button type="button" (click)="removeUploaded(u)">
                    {{ 'common.remove' | t }}
                  </button>
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
            {{ 'common.submitting' | t }}
          } @else if (uploadsInFlight() > 0) {
            {{ 'common.waitingUploads' | t: { n: uploadsInFlight() } }}
          } @else {
            {{ 'submit.button' | t }}
          }
        </button>
        @if (rating === 1 || rating === 2 || rating === 5) {
          <p class="muted">{{ 'submit.moderationNotice' | t }}</p>
        }
      </form>
    } @else if (notFound()) {
      <p>{{ 'products.notFound' | t }}</p>
    }
  `,
  styles: [
    `
      label {
        display: block;
        margin: 0.75rem 0;
      }
      .rating-block {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: 0.5rem;
        margin: 1.5rem 0 2rem;
        padding: 1.25rem;
        background: #fffaf0;
        border: 1px solid #f5d99a;
        border-radius: 8px;
      }
      .rating-label {
        font-size: 1.1rem;
        font-weight: 600;
        color: #333;
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
  // Submit stays disabled while > 0 so we don't post before URLs are known.
  protected readonly uploadsInFlight = signal(0);

  protected rating = 0;
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
        this.i18n.t('submit.tooManyImages', { max: Limits.maxImages, remaining: slotsRemaining }),
      );
      return;
    }

    for (const f of files) {
      if (f.size > Limits.maxImageBytes) {
        this.uploadError.set(
          this.i18n.t('submit.imageTooLarge', {
            name: f.name,
            mb: Limits.maxImageBytes / (1024 * 1024),
          }),
        );
        return;
      }
      if (!Limits.allowedImageTypes.includes(f.type as (typeof Limits.allowedImageTypes)[number])) {
        this.uploadError.set(
          this.i18n.t('submit.imageBadType', { name: f.name, type: f.type || 'unknown' }),
        );
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
          this.uploadError.set(err.error ?? err.message ?? this.i18n.t('submit.uploadFailed'));
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
        turnstileToken: this.turnstileToken,
      })
      .subscribe({
        next: () => {
          this.router.navigate(['/products', p.slug]);
        },
        error: (err) => {
          this.error.set(err.error ?? err.message ?? this.i18n.t('submit.submitFailed'));
          this.submitting.set(false);
        },
      });
  }
}
