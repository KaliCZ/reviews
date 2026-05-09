// JIT compiler load order: must come before any partial-compiled @angular/* import.
import '@angular/compiler';
import { describe, it, expect, beforeEach } from 'vitest';
import { I18nService } from './i18n.service';

function makeService(): I18nService {
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
    localStorage.setItem('locale', 'xx');
    const svc = new I18nService(document, 'browser');
    // jsdom's navigator.language defaults to 'en-US'.
    expect(svc.locale()).toBe('en');
  });
});
