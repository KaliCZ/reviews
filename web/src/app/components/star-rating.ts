import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

@Component({
  selector: 'app-star-rating',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="stars" [class.interactive]="interactive()" [attr.aria-label]="ariaLabel()">
      @for (s of stars; track s) {
        <button
          type="button"
          class="star"
          [class.filled]="s <= rounded()"
          [disabled]="!interactive()"
          (click)="onPick(s)"
        >
          ★
        </button>
      }
    </span>
  `,
  styles: [
    `
      .stars {
        display: inline-flex;
        gap: 2px;
      }
      .star {
        background: none;
        border: none;
        padding: 0;
        font-size: 1.2rem;
        color: #ccc;
        cursor: default;
        line-height: 1;
      }
      .star.filled {
        color: #f5a623;
      }
      .interactive .star {
        cursor: pointer;
      }
      .interactive .star:disabled {
        cursor: default;
      }
    `,
  ],
})
export class StarRating {
  readonly value = input(0);
  readonly interactive = input(false);
  readonly valueChange = output<number>();

  readonly stars = [1, 2, 3, 4, 5];
  readonly rounded = computed(() => Math.round(this.value()));
  readonly ariaLabel = computed(() => `${this.value().toFixed(1)} out of 5 stars`);

  onPick(n: number) {
    if (this.interactive()) this.valueChange.emit(n);
  }
}
