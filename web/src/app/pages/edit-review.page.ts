import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { StarRating } from '../components/star-rating';
import { TurnstileComponent } from '../components/turnstile';
import { ApiService } from '../services/api.service';
import { Limits, ReviewItem } from '../models';
import { TPipe } from '../pipes/t.pipe';
import { I18nService } from '../services/i18n.service';

@Component({
  imports: [FormsModule, RouterLink, StarRating, TurnstileComponent, TPipe],
  template: `
    @if (review(); as r) {
      <p>
        <a [routerLink]="['/products', slug()]" class="link">{{ 'common.back' | t }}</a>
      </p>
      <h1>{{ 'edit.heading' | t }}</h1>
      <p class="muted">{{ 'edit.moderationNotice' | t }}</p>

      <form (submit)="save($event)">
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
          <legend>{{ 'edit.photos' | t: { n: Limits.maxImages } }}</legend>
          @if (imageUrls.length > 0) {
            <ul class="uploaded">
              @for (u of imageUrls; track u) {
                <li>
                  <img [src]="u" [alt]="'reviewCard.imageAlt' | t" />
                  <button type="button" (click)="removeImage(u)">
                    {{ 'common.remove' | t }}
                  </button>
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
            {{ 'submit.photosLimit' | t: { mb: Limits.maxImageBytes / (1024 * 1024) } }}
          </p>
          @if (uploadError(); as ue) {
            <p class="error">{{ ue }}</p>
          }
        </fieldset>

        @if (siteKey(); as sk) {
          <app-turnstile [siteKey]="sk" (tokenChange)="turnstileToken = $event" />
        }

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
            body.length > Limits.bodyMax ||
            turnstileToken.length === 0
          "
        >
          {{ saving() ? ('common.saving' | t) : ('common.save' | t) }}
        </button>
      </form>
    } @else if (notFound()) {
      <p>{{ 'edit.notFound' | t }}</p>
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
  protected readonly siteKey = signal<string | null>(null);

  protected rating = 5;
  protected title = '';
  protected body = '';
  protected imageUrls: string[] = [];
  protected turnstileToken = '';

  constructor() {
    this.api.config().subscribe((c) => this.siteKey.set(c.turnstileSiteKey));
    effect(() => {
      const id = this.reviewId();
      if (!id) return;
      this.api.getReview(id).subscribe({
        next: (r) => this.fillFrom(r),
        error: () => this.notFound.set(true),
      });
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
      this.api.uploadImage(f).subscribe({
        next: (res) => this.imageUrls.push(res.url),
        error: (err) =>
          this.uploadError.set(err.error ?? err.message ?? this.i18n.t('submit.uploadFailed')),
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
        turnstileToken: this.turnstileToken,
      })
      .subscribe({
        next: () => this.router.navigate(['/products', this.slug()]),
        error: (err) => {
          this.error.set(err.error ?? err.message ?? this.i18n.t('edit.saveFailed'));
          this.saving.set(false);
        },
      });
  }
}
