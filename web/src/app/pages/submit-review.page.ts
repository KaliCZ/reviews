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

      @if (showSuccess()) {
        <div class="modal-backdrop" role="presentation" (click)="goBack()">
          <div
            class="modal"
            role="dialog"
            aria-modal="true"
            [attr.aria-labelledby]="'submit-success-title'"
            (click)="$event.stopPropagation()"
          >
            <h2 id="submit-success-title">{{ 'submit.successTitle' | t }}</h2>
            <p>{{ 'submit.successBody' | t }}</p>
            <p class="muted">{{ 'submit.successModeration' | t }}</p>
            <button type="button" (click)="goBack()">
              {{ 'submit.successBack' | t: { name: p.name } }}
            </button>
          </div>
        </div>
      }

      <form (submit)="submit($event)">
        <label
          >{{ 'submit.rating' | t }}
          <app-star-rating [value]="rating" [interactive]="true" (valueChange)="rating = $event" />
        </label>

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
      .modal-backdrop {
        position: fixed;
        inset: 0;
        background: rgba(0, 0, 0, 0.5);
        display: flex;
        align-items: center;
        justify-content: center;
        padding: 1rem;
        z-index: 100;
      }
      .modal {
        background: var(--color-surface, #fff);
        color: var(--color-on-surface, inherit);
        border-radius: 8px;
        padding: 1.5rem;
        max-width: 28rem;
        width: 100%;
        box-shadow: 0 10px 25px rgba(0, 0, 0, 0.25);
      }
      .modal h2 {
        margin: 0 0 0.75rem;
      }
      .modal p {
        margin: 0 0 0.75rem;
        line-height: 1.5;
      }
      .modal button {
        margin-top: 0.5rem;
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
  protected readonly showSuccess = signal(false);
  // Submit stays disabled while > 0 so we don't post before URLs are known.
  protected readonly uploadsInFlight = signal(0);

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
          // Don't auto-navigate — the product page reads from a cache that
          // may not be invalidated yet, and Pending reviews aren't in the
          // shared listing at all. Show a confirmation so the user knows
          // it landed and clicks through deliberately; the product page
          // overlays their own review (any status) regardless of cache.
          this.showSuccess.set(true);
          this.submitting.set(false);
        },
        error: (err) => {
          this.error.set(err.error ?? err.message ?? this.i18n.t('submit.submitFailed'));
          this.submitting.set(false);
        },
      });
  }

  goBack(): void {
    const p = this.product();
    if (!p) return;
    this.router.navigate(['/products', p.slug]);
  }
}
