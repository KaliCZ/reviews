// @angular/compiler must load before any Angular DI metadata is read so that
// JIT can compile partial-compiled libraries (e.g. @angular/common's
// PlatformLocation). Without it, importing the service indirectly pulls in
// unresolved JIT-only metadata and the test file fails to load.
import '@angular/compiler';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ThemeService } from './theme.service';

// Mock matchMedia globally — jsdom doesn't ship with one. Tests that need
// a specific result (system → dark vs light) override this within the test
// itself via vi.stubGlobal.
function stubMatchMedia(matches: boolean): void {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches,
    media: query,
    onchange: null,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    addListener: vi.fn(),
    removeListener: vi.fn(),
    dispatchEvent: vi.fn(),
  }));
}

function makeService(): ThemeService {
  return new ThemeService(document, 'browser');
}

describe('ThemeService', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.className = '';
    stubMatchMedia(false);
  });

  it("defaults to 'system' when nothing is stored", () => {
    const svc = makeService();
    expect(svc.choice()).toBe('system');
  });

  it("set('light') resolves isDark to false", () => {
    const svc = makeService();
    svc.set('light');
    expect(svc.choice()).toBe('light');
    expect(svc.isDark()).toBe(false);
  });

  it("set('dark') resolves isDark to true", () => {
    const svc = makeService();
    svc.set('dark');
    expect(svc.choice()).toBe('dark');
    expect(svc.isDark()).toBe(true);
  });

  it("set('system') with prefers-color-scheme: dark resolves isDark to true", () => {
    stubMatchMedia(true);
    const svc = makeService();
    svc.set('system');
    expect(svc.choice()).toBe('system');
    expect(svc.isDark()).toBe(true);
  });

  it("set('system') with prefers-color-scheme: light resolves isDark to false", () => {
    stubMatchMedia(false);
    const svc = makeService();
    svc.set('system');
    expect(svc.choice()).toBe('system');
    expect(svc.isDark()).toBe(false);
  });

  it("persists 'light' / 'dark' to localStorage on set()", () => {
    const svc = makeService();
    svc.set('dark');
    expect(localStorage.getItem('theme')).toBe('dark');
    svc.set('light');
    expect(localStorage.getItem('theme')).toBe('light');
  });

  it("set('system') removes the stored value (system is the default)", () => {
    localStorage.setItem('theme', 'dark');
    const svc = makeService();
    svc.set('system');
    expect(localStorage.getItem('theme')).toBeNull();
  });

  it('reads the initial choice from localStorage on construction', () => {
    localStorage.setItem('theme', 'dark');
    const svc = makeService();
    expect(svc.choice()).toBe('dark');
    expect(svc.isDark()).toBe(true);
  });
});
