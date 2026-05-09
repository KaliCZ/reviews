import { DOCUMENT, Inject, Injectable, PLATFORM_ID, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

// Three-state theme: system follows prefers-color-scheme, light/dark are
// explicit overrides. Stored in localStorage under `theme` so the choice
// survives reloads. SSR returns the default ("system") and the browser
// hydrates from storage on bootstrap — we don't try to flash the right theme
// during SSR (no cookie / no client hint), so first paint may briefly show
// the system default before settling. Acceptable trade-off; cookie-driven
// SSR theming is a follow-up.
export type ThemeChoice = 'system' | 'light' | 'dark';

const STORAGE_KEY = 'theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly isBrowser: boolean;
  // Public read-only signal so components can subscribe to the user's pick.
  readonly choice = signal<ThemeChoice>('system');
  // Whether the resolved (system-aware) theme is dark — useful for swapping
  // theme-dependent assets (icons, screenshots).
  readonly isDark = signal<boolean>(false);

  constructor(
    @Inject(DOCUMENT) private readonly doc: Document,
    @Inject(PLATFORM_ID) platformId: object,
  ) {
    this.isBrowser = isPlatformBrowser(platformId);
    if (!this.isBrowser) return;

    const stored = (localStorage.getItem(STORAGE_KEY) as ThemeChoice | null) ?? 'system';
    this.choice.set(stored);
    this.apply(stored);

    // Track OS theme changes so `system` stays in sync without a reload.
    const mq = window.matchMedia('(prefers-color-scheme: dark)');
    mq.addEventListener('change', () => {
      if (this.choice() === 'system') this.apply('system');
    });
  }

  set(choice: ThemeChoice): void {
    this.choice.set(choice);
    if (!this.isBrowser) return;
    if (choice === 'system') localStorage.removeItem(STORAGE_KEY);
    else localStorage.setItem(STORAGE_KEY, choice);
    this.applyWithViewTransition(choice);
  }

  private applyWithViewTransition(choice: ThemeChoice): void {
    type DocVT = Document & { startViewTransition?: (cb: () => void) => unknown };
    const doc = this.doc as DocVT;
    if (typeof doc.startViewTransition === 'function') {
      doc.startViewTransition(() => this.apply(choice));
      return;
    }
    // Fallback — kill transitions for the swap so colours don't crossfade
    // mid-flip on browsers without View Transitions.
    this.doc.documentElement.classList.add('no-transitions');
    this.apply(choice);
    // Force reflow before stripping the suppression class.
    void this.doc.documentElement.offsetHeight;
    this.doc.documentElement.classList.remove('no-transitions');
  }

  private apply(choice: ThemeChoice): void {
    if (!this.isBrowser) return;
    const dark =
      choice === 'dark' ||
      (choice === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
    this.doc.documentElement.classList.toggle('dark', dark);
    this.isDark.set(dark);
  }
}
