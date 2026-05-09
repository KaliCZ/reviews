import { Component, inject } from '@angular/core';
import { ThemeService, ThemeChoice } from '../services/theme.service';
import { I18nService } from '../services/i18n.service';

// system → light → dark → system. Icon reflects the resolved (system-aware) theme.
@Component({
  selector: 'app-theme-toggle',
  template: `
    <button
      type="button"
      class="theme-toggle"
      (click)="cycle()"
      [title]="title()"
      [attr.aria-label]="title()"
    >
      @if (theme.choice() === 'system') {
        <span aria-hidden="true">⚙</span>
      } @else if (theme.isDark()) {
        <span aria-hidden="true">☀</span>
      } @else {
        <span aria-hidden="true">☾</span>
      }
    </button>
  `,
  styles: [
    `
      .theme-toggle {
        background: transparent;
        border: 1px solid var(--color-outline-variant);
        border-radius: 6px;
        padding: 0.25rem 0.55rem;
        font-size: 1rem;
        line-height: 1;
        color: var(--color-on-surface);
        cursor: pointer;
      }
      .theme-toggle:hover {
        background: var(--color-surface-container);
      }
    `,
  ],
})
export class ThemeToggle {
  protected readonly theme = inject(ThemeService);
  private readonly i18n = inject(I18nService);

  cycle(): void {
    const next: Record<ThemeChoice, ThemeChoice> = {
      system: 'light',
      light: 'dark',
      dark: 'system',
    };
    this.theme.set(next[this.theme.choice()]);
  }

  title(): string {
    if (this.theme.choice() === 'system') return this.i18n.t('theme.titleSystem');
    if (this.theme.choice() === 'light') return this.i18n.t('theme.titleLight');
    return this.i18n.t('theme.titleDark');
  }
}
