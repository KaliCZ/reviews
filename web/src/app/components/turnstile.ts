import { isPlatformBrowser } from '@angular/common';
import {
  AfterViewInit,
  Component,
  ElementRef,
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
        opts: {
          sitekey: string;
          callback: (t: string) => void;
          'error-callback'?: () => void;
          'expired-callback'?: () => void;
        },
      ) => string;
      remove: (id: string) => void;
      reset: (id: string) => void;
    };
  }
}

// `defer`-loaded turnstile/api.js may not be ready at Angular bootstrap, so
// we poll briefly for window.turnstile.
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

  // Tokens are single-use; callers (vote/delete) reset after each consumption
  // to issue a fresh token for the next click.
  reset(): void {
    if (this.widgetId && window.turnstile) {
      window.turnstile.reset(this.widgetId);
      this.tokenChange.emit('');
    }
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
      setTimeout(() => this.tryRender(attempt + 1), 100); // ~5s ceiling
    } else {
      console.warn('[turnstile] api.js never loaded');
    }
  }
}
