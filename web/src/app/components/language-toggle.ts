import { Component, inject } from '@angular/core';
import { I18nService, LOCALES, Locale } from '../services/i18n.service';

// Two-locale segmented toggle. Tiny enough to render inline in the header
// next to the theme toggle. Keeps language switching one click away — no
// dropdown overhead for two options.
@Component({
  selector: 'app-language-toggle',
  template: `
    <div class="lang-toggle" role="group" [attr.aria-label]="i18n.t('language.label')">
      @for (l of locales; track l) {
        <button
          type="button"
          [class.active]="i18n.locale() === l"
          (click)="i18n.set(l)"
          [attr.aria-pressed]="i18n.locale() === l"
        >
          {{ labelFor(l) }}
        </button>
      }
    </div>
  `,
  styles: [
    `
      .lang-toggle {
        display: inline-flex;
        border: 1px solid var(--color-outline-variant);
        border-radius: 6px;
        overflow: hidden;
      }
      button {
        background: transparent;
        border: none;
        color: var(--color-on-surface-muted);
        padding: 0.25rem 0.5rem;
        font-size: 0.8rem;
        cursor: pointer;
        font-family: inherit;
      }
      button.active {
        background: var(--color-primary);
        color: var(--color-on-primary);
      }
      button:hover:not(.active) {
        background: var(--color-surface-container);
        color: var(--color-on-surface);
      }
    `,
  ],
})
export class LanguageToggle {
  protected readonly i18n = inject(I18nService);
  protected readonly locales = LOCALES;

  labelFor(l: Locale): string {
    return l === 'cs' ? 'CS' : 'EN';
  }
}
