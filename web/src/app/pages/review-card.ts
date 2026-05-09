import { DatePipe } from '@angular/common';
import { Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { StarRating } from '../components/star-rating';
import { AuthService } from '../services/auth.service';
import { ReviewItem } from '../models';
import { TPipe } from '../pipes/t.pipe';

@Component({
  selector: 'app-review-card',
  imports: [DatePipe, RouterLink, StarRating, TPipe],
  template: `
    <article class="review">
      <header>
        <app-star-rating [value]="review().rating" />
        <span class="title">{{ review().title }}</span>
      </header>
      <div class="meta">
        <span>{{ review().authorName }}</span>
        <span>·</span>
        <time>{{ review().createdAtUtc | date: 'mediumDate' }}</time>
      </div>
      <p class="body">{{ review().body }}</p>
      @if (review().imageUrls.length > 0) {
        <div class="thumbs">
          @for (url of review().imageUrls; track url) {
            <img [src]="url" [alt]="'reviewCard.imageAlt' | t" loading="lazy" />
          }
        </div>
      }
      <footer>
        <div class="votes">
          <button
            type="button"
            class="vote"
            [class.active]="review().myVote === true"
            [disabled]="!auth.authenticated() || busy()"
            [title]="!auth.authenticated() ? ('vote.signInToVote' | t) : null"
            (click)="cast(true)"
          >
            ▲
          </button>
          <span class="score">{{ review().score }}</span>
          <button
            type="button"
            class="vote"
            [class.active]="review().myVote === false"
            [disabled]="!auth.authenticated() || busy()"
            [title]="!auth.authenticated() ? ('vote.signInToVote' | t) : null"
            (click)="cast(false)"
          >
            ▼
          </button>
        </div>
        @if (isMine()) {
          <div class="own">
            <a
              [routerLink]="['/products', productSlug(), 'review', review().id, 'edit']"
              class="link"
              >{{ 'common.edit' | t }}</a
            >
            <button
              type="button"
              class="link danger"
              (click)="del.emit(review().id)"
              [disabled]="busy()"
            >
              {{ 'common.delete' | t }}
            </button>
          </div>
        }
      </footer>
    </article>
  `,
  styles: [
    `
      .review {
        padding: 1rem 0;
        border-bottom: 1px solid var(--color-outline-variant);
      }
      .review header {
        display: flex;
        align-items: center;
        gap: 0.75rem;
      }
      .title {
        font-weight: 600;
      }
      .meta {
        color: var(--color-on-surface-muted);
        font-size: 0.85rem;
        margin: 0.25rem 0 0.5rem;
        display: flex;
        gap: 0.4rem;
      }
      .body {
        margin: 0.5rem 0;
        line-height: 1.5;
        white-space: pre-wrap;
      }
      .thumbs {
        display: flex;
        gap: 0.5rem;
        flex-wrap: wrap;
        margin-bottom: 0.5rem;
      }
      .thumbs img {
        width: 110px;
        height: 80px;
        object-fit: cover;
        border-radius: 4px;
      }
      footer {
        display: flex;
        justify-content: space-between;
        align-items: center;
      }
      .votes {
        display: flex;
        align-items: center;
        gap: 0.4rem;
      }
      .vote {
        background: var(--color-surface);
        border: 1px solid var(--color-outline);
        color: var(--color-on-surface);
        padding: 0.15rem 0.5rem;
        border-radius: 4px;
        cursor: pointer;
        font-size: 1rem;
      }
      .vote.active {
        background: var(--color-primary);
        color: var(--color-on-primary);
        border-color: var(--color-primary);
      }
      .vote:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }
      .score {
        min-width: 1.5rem;
        text-align: center;
        font-weight: 600;
      }
      .own {
        display: flex;
        gap: 0.75rem;
      }
      .link {
        background: none;
        border: none;
        padding: 0;
        color: var(--color-link);
        cursor: pointer;
        font-size: 0.9rem;
        text-decoration: none;
      }
      .link:hover {
        text-decoration: underline;
      }
      .danger {
        color: var(--color-error);
      }
    `,
  ],
})
export class ReviewCard {
  protected readonly auth = inject(AuthService);

  readonly review = input.required<ReviewItem>();
  readonly productSlug = input.required<string>();
  readonly busy = input(false);

  readonly vote = output<{ id: string; isUpvote: boolean }>();
  readonly del = output<string>();

  // UI affordance only — destructive actions are gated server-side.
  readonly isMine = computed(() => this.review().mine);

  cast(isUpvote: boolean) {
    if (!this.auth.authenticated()) return;
    this.vote.emit({ id: this.review().id, isUpvote });
  }
}
