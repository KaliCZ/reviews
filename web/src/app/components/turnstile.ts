import { isPlatformBrowser } from '@angular/common';
import {
  AfterViewInit,
  Component,
  ElementRef,
  Inject,
  OnDestroy,
  PLATFORM_ID,
  ViewChild,
  inject,
  input,
  output,
} from '@angular/core';

declare global {
  interface Window {
    turnstile?: {
      render: (
        el: HTMLElement,
        opts: { sitekey: string; callback: (t: string) => void; 'error-callback'?: () => void; 'expired-callback'?: () => void },
      ) => string;
      remove: (id: string) => void;
    };
  }
}

// Renders Cloudflare Turnstile and emits the resulting token. The widget
// script is loaded in index.html with `defer`; we poll briefly for
// `window.turnstile` since `defer` makes the load order non-deterministic
// relative to Angular bootstrap. Browser-only — SSR skips rendering and
// the form blocks submission until the token arrives client-side.
@Component({
  selector: 'app-turnstile',
  template: `<div #host></div>`,
})
export class TurnstileComponent implements AfterViewInit, OnDestroy {
  readonly siteKey = input.required<string>();
  readonly tokenChange = output<string>();

  @ViewChild('host', { static: true }) host!: ElementRef<HTMLDivElement>;

  private widgetId?: string;
  private readonly platformId = inject(PLATFORM_ID);

  ngAfterViewInit(): void {
    if (!isPlatformBrowser(this.platformId)) return;
    this.tryRender(0);
  }

  ngOnDestroy(): void {
    if (this.widgetId && window.turnstile) window.turnstile.remove(this.widgetId);
  }

  private tryRender(attempt: number): void {
    if (window.turnstile) {
      this.widgetId = window.turnstile.render(this.host.nativeElement, {
        sitekey: this.siteKey(),
        callback: (token) => this.tokenChange.emit(token),
        'expired-callback': () => this.tokenChange.emit(''),
        'error-callback': () => this.tokenChange.emit(''),
      });
    } else if (attempt < 50) {
      // ~5s of polling at 100ms — Turnstile's api.js usually loads inside
      // half a second, so this is generous.
      setTimeout(() => this.tryRender(attempt + 1), 100);
    } else {
      console.warn('[turnstile] api.js never loaded');
    }
  }
}
