import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { I18nService } from '../services/i18n.service';

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
  private readonly i18n = inject(I18nService);

  readonly value = input(0);
  readonly interactive = input(false);
  readonly valueChange = output<number>();

  readonly stars = [1, 2, 3, 4, 5];
  readonly rounded = computed(() => Math.round(this.value()));
  // Pure: false isn't on the host; the locale signal is read here so the
  // computed re-runs when locale changes.
  readonly ariaLabel = computed(() => {
    this.i18n.locale();
    return this.i18n.t('starRating.ariaLabel', { n: this.value().toFixed(1) });
  });

  onPick(n: number) {
    if (this.interactive()) this.valueChange.emit(n);
  }
}
