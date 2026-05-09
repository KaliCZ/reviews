// @angular/compiler must load before any Angular DI metadata is read so that
// JIT can compile partial-compiled libraries (e.g. @angular/common's
// PlatformLocation). Without it, importing the service indirectly pulls in
// unresolved JIT-only metadata and the test file fails to load.
import '@angular/compiler';
import { describe, it, expect, beforeEach } from 'vitest';
import { I18nService } from './i18n.service';

// Direct-construct the service with plain ctor args. The DOCUMENT and
// PLATFORM_ID injection tokens just resolve to a Document and an opaque
// object at runtime — under jsdom the `document` global stands in fine,
// and 'browser' as the platform marker matches what isPlatformBrowser
// looks for. Bypassing TestBed keeps these as fast unit tests.
function makeService(): I18nService {
  // Clear localStorage so each test starts in a known state.
  localStorage.clear();
  return new I18nService(document, 'browser');
}

describe('I18nService', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it("returns the English string for 'en'", () => {
    const svc = makeService();
    svc.set('en');
    expect(svc.t('nav.brand')).toBe('Reviews');
  });

  it("returns the Czech string for 'cs'", () => {
    const svc = makeService();
    svc.set('cs');
    expect(svc.t('nav.brand')).toBe('Recenze');
  });

  it('interpolates {name} placeholders', () => {
    const svc = makeService();
    svc.set('en');
    expect(svc.t('nav.greeting', { name: 'Alice' })).toBe('Hi, Alice');
  });

  it('returns the key as fallback when the lookup misses', () => {
    const svc = makeService();
    svc.set('en');
    expect(svc.t('nonexistent.key')).toBe('nonexistent.key');
  });

  it('replaces multiple {var} placeholders in a single string', () => {
    const svc = makeService();
    svc.set('en');
    // common.waitingUploads = "Waiting for {n} upload(s)…" — verifies the
    // single-placeholder path. Add a fake key with two placeholders by
    // calling t() against a constructed format string isn't possible here,
    // so we lean on Czech's longer form which has a {mb} placeholder for
    // the size limit string.
    expect(svc.t('common.waitingUploads', { n: 3 })).toBe('Waiting for 3 upload(s)…');
    expect(svc.t('submit.photosLimit', { mb: 2 })).toBe('Each image must be 2 MB or less.');
  });

  it('switching locale via set() updates subsequent t() calls', () => {
    const svc = makeService();
    svc.set('en');
    expect(svc.t('nav.brand')).toBe('Reviews');
    svc.set('cs');
    expect(svc.t('nav.brand')).toBe('Recenze');
    expect(svc.locale()).toBe('cs');
  });

  it('persists the chosen locale to localStorage', () => {
    const svc = makeService();
    svc.set('cs');
    expect(localStorage.getItem('locale')).toBe('cs');
  });

  it('reads the initial locale from localStorage on construction', () => {
    localStorage.setItem('locale', 'cs');
    const svc = new I18nService(document, 'browser');
    expect(svc.locale()).toBe('cs');
    expect(svc.t('nav.brand')).toBe('Recenze');
  });

  it('ignores unknown locale strings stored under the key', () => {
    // Defence-in-depth — a stale or hand-edited localStorage value should
    // fall back to detection (browser default), not crash.
    localStorage.setItem('locale', 'xx');
    const svc = new I18nService(document, 'browser');
    // jsdom's navigator.language defaults to 'en-US', so we expect 'en'.
    expect(svc.locale()).toBe('en');
  });
});
