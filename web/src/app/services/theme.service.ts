import { DOCUMENT, Inject, Injectable, PLATFORM_ID, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

// SSR has no client hint, so first paint may briefly show the system default
// before localStorage hydrates the user's choice.
export type ThemeChoice = 'system' | 'light' | 'dark';

const STORAGE_KEY = 'theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly isBrowser: boolean;
  readonly choice = signal<ThemeChoice>('system');
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
    // Suppress transitions so colours don't crossfade mid-flip on browsers
    // without View Transitions.
    this.doc.documentElement.classList.add('no-transitions');
    this.apply(choice);
    void this.doc.documentElement.offsetHeight; // force reflow
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
