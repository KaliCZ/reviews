import { DOCUMENT, Inject, Injectable, PLATFORM_ID, computed, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import en from '../i18n/en.json';
import cs from '../i18n/cs.json';

// Two-locale runtime i18n. The bundles are imported as JSON, picked at
// startup based on a localStorage key. Switching locale flips a signal so
// templates re-render via the t() lookup. Keep the surface tiny — anything
// fancier (per-locale routes, static prerendering) should go through
// @angular/localize, which is heavier than what we need here.
//
// Lookup format: dotted path with optional `{var}` placeholders that get
// replaced from the params object. Missing keys return the key itself —
// makes regressions visible in the UI rather than silent.
export type Locale = 'en' | 'cs';

export const LOCALES: readonly Locale[] = ['en', 'cs'] as const;

const STORAGE_KEY = 'locale';

type Bundle = Record<string, unknown>;

const BUNDLES: Record<Locale, Bundle> = { en, cs };

@Injectable({ providedIn: 'root' })
export class I18nService {
  private readonly isBrowser: boolean;
  readonly locale = signal<Locale>('en');
  readonly bundle = computed<Bundle>(() => BUNDLES[this.locale()]);

  constructor(
    @Inject(DOCUMENT) private readonly doc: Document,
    @Inject(PLATFORM_ID) platformId: object,
  ) {
    this.isBrowser = isPlatformBrowser(platformId);
    if (!this.isBrowser) {
      this.applyHtmlLang(this.locale());
      return;
    }

    const stored = localStorage.getItem(STORAGE_KEY) as Locale | null;
    const initial = stored && LOCALES.includes(stored) ? stored : this.detect();
    this.locale.set(initial);
    this.applyHtmlLang(initial);
  }

  set(locale: Locale): void {
    if (!LOCALES.includes(locale)) return;
    this.locale.set(locale);
    if (this.isBrowser) localStorage.setItem(STORAGE_KEY, locale);
    this.applyHtmlLang(locale);
  }

  // Templates use `{{ t('common.submit') }}` — params is optional.
  t(key: string, params?: Record<string, string | number>): string {
    const value = this.lookup(this.bundle(), key);
    if (typeof value !== 'string') return key;
    if (!params) return value;
    return value.replace(/\{(\w+)\}/g, (_, name: string) =>
      name in params ? String(params[name]) : `{${name}}`,
    );
  }

  private lookup(bundle: Bundle, dottedKey: string): unknown {
    const parts = dottedKey.split('.');
    let cur: unknown = bundle;
    for (const part of parts) {
      if (cur && typeof cur === 'object' && part in (cur as Bundle)) {
        cur = (cur as Bundle)[part];
      } else {
        return undefined;
      }
    }
    return cur;
  }

  private detect(): Locale {
    if (!this.isBrowser) return 'en';
    const nav = navigator.language?.toLowerCase() ?? 'en';
    if (nav.startsWith('cs')) return 'cs';
    return 'en';
  }

  private applyHtmlLang(locale: Locale): void {
    this.doc.documentElement.setAttribute('lang', locale);
  }
}
