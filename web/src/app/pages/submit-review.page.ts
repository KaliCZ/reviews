import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { StarRating } from '../components/star-rating';
import { TurnstileComponent } from '../components/turnstile';
import { ApiService } from '../services/api.service';
import { ProductDetail } from '../models';

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
          <textarea
            [(ngModel)]="imageUrlsRaw"
            name="imageUrls"
            rows="3"
            placeholder="https://..."
          ></textarea>
        </label>

        @if (siteKey(); as sk) {
          <app-turnstile [siteKey]="sk" (tokenChange)="turnstileToken = $event" />
        }

        @if (error(); as e) {
          <p class="error">{{ e }}</p>
        }

        <button type="submit" [disabled]="!canSubmit() || submitting()">
          {{ submitting() ? 'Submitting...' : 'Submit review' }}
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

  readonly slug = input.required<string>();

  protected readonly product = signal<ProductDetail | null>(null);
  protected readonly notFound = signal(false);
  protected readonly siteKey = signal<string | null>(null);
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  protected rating = 5;
  protected title = '';
  protected body = '';
  protected imageUrlsRaw = '';
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
      this.body.trim().length >= 10 &&
      this.rating >= 1 &&
      this.rating <= 5 &&
      this.turnstileToken.length > 0
    );
  }

  submit(e: Event): void {
    e.preventDefault();
    const p = this.product();
    if (!p || !this.canSubmit()) return;

    this.submitting.set(true);
    this.error.set(null);
    const imageUrls = this.imageUrlsRaw
      .split('\n')
      .map((s) => s.trim())
      .filter(Boolean);

    this.api
      .submitReview({
        productId: p.id,
        rating: this.rating,
        title: this.title.trim() || undefined,
        body: this.body.trim(),
        imageUrls: imageUrls.length ? imageUrls : undefined,
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
