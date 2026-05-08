import { DatePipe } from '@angular/common';
import { Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { StarRating } from '../components/star-rating';
import { AuthService } from '../services/auth.service';
import { ReviewItem } from '../models';

// Reused on the product detail page and the more-reviews list. Vote buttons
// switch on the viewer's existing vote (highlighted), and edit/delete links
// appear only on the viewer's own review (matched via author_id derived from
// OIDC sub on the API side).
@Component({
  selector: 'app-review-card',
  imports: [DatePipe, RouterLink, StarRating],
  template: `
    <article class="review">
      <header>
        <app-star-rating [value]="review().rating" />
        @if (review().title) { <span class="title">{{ review().title }}</span> }
      </header>
      <div class="meta">
        <span>{{ review().authorName }}</span>
        <span>·</span>
        <time>{{ review().createdAt | date: 'mediumDate' }}</time>
      </div>
      <p class="body">{{ review().body }}</p>
      @if (review().imageUrls.length > 0) {
        <div class="thumbs">
          @for (url of review().imageUrls; track url) {
            <img [src]="url" alt="review photo" loading="lazy" />
          }
        </div>
      }
      <footer>
        <div class="votes">
          <button
            type="button"
            class="vote"
            [class.active]="review().myVote === 1"
            [disabled]="!auth.authenticated() || busy()"
            (click)="cast(1)">▲</button>
          <span class="score">{{ review().score }}</span>
          <button
            type="button"
            class="vote"
            [class.active]="review().myVote === -1"
            [disabled]="!auth.authenticated() || busy()"
            (click)="cast(-1)">▼</button>
        </div>
        @if (isMine()) {
          <div class="own">
            <a [routerLink]="['/products', productSlug(), 'review', review().id, 'edit']" class="link">Edit</a>
            <button type="button" class="link danger" (click)="del.emit(review().id)" [disabled]="busy()">Delete</button>
          </div>
        }
      </footer>
    </article>
  `,
  styles: [`
    .review { padding: 1rem 0; border-bottom: 1px solid #eee; }
    .review header { display: flex; align-items: center; gap: 0.75rem; }
    .title { font-weight: 600; }
    .meta { color: #666; font-size: 0.85rem; margin: 0.25rem 0 0.5rem; display: flex; gap: 0.4rem; }
    .body { margin: 0.5rem 0; line-height: 1.5; white-space: pre-wrap; }
    .thumbs { display: flex; gap: 0.5rem; flex-wrap: wrap; margin-bottom: 0.5rem; }
    .thumbs img { width: 110px; height: 80px; object-fit: cover; border-radius: 4px; }
    footer { display: flex; justify-content: space-between; align-items: center; }
    .votes { display: flex; align-items: center; gap: 0.4rem; }
    .vote { background: #fff; border: 1px solid #ccc; padding: 0.15rem 0.5rem;
      border-radius: 4px; cursor: pointer; font-size: 1rem; }
    .vote.active { background: #2563eb; color: #fff; border-color: #2563eb; }
    .vote:disabled { opacity: 0.5; cursor: not-allowed; }
    .score { min-width: 1.5rem; text-align: center; font-weight: 600; }
    .own { display: flex; gap: 0.75rem; }
    .link { background: none; border: none; padding: 0; color: #2563eb; cursor: pointer;
      font-size: 0.9rem; text-decoration: none; }
    .link:hover { text-decoration: underline; }
    .danger { color: #dc2626; }
  `],
})
export class ReviewCard {
  protected readonly auth = inject(AuthService);

  readonly review = input.required<ReviewItem>();
  readonly productSlug = input.required<string>();
  readonly busy = input(false);

  readonly vote = output<{ id: string; value: 1 | -1 }>();
  readonly del = output<string>();

  // API computes this server-side (current viewer's hashed `sub` vs review
  // author_id). Destructive actions are still gated by the API independently;
  // this flag is purely about UI affordances.
  readonly isMine = computed(() => this.review().mine);

  cast(value: 1 | -1) {
    if (!this.auth.authenticated()) return;
    this.vote.emit({ id: this.review().id, value });
  }
}
