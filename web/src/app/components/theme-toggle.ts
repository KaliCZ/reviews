import { Component, inject } from '@angular/core';
import { ThemeService, ThemeChoice } from '../services/theme.service';

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

  cycle(): void {
    const next: Record<ThemeChoice, ThemeChoice> = {
      system: 'light',
      light: 'dark',
      dark: 'system',
    };
    this.theme.set(next[this.theme.choice()]);
  }

  title(): string {
    if (this.theme.choice() === 'system') return 'Theme: system (click for light)';
    if (this.theme.choice() === 'light') return 'Theme: light (click for dark)';
    return 'Theme: dark (click for system)';
  }
}
