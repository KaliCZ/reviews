import { Component, afterNextRender, inject } from '@angular/core';
import { RouterLink, RouterOutlet, Router } from '@angular/router';
import { AuthService } from './services/auth.service';
import { ThemeToggle } from './components/theme-toggle';
import { LanguageToggle } from './components/language-toggle';
import { TPipe } from './pipes/t.pipe';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, ThemeToggle, LanguageToggle, TPipe],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  constructor() {
    // Browser-only: SSR fetches don't carry the session cookie, so /auth/me
    // would always come back 401 — and the in-flight HttpClient request
    // keeps Angular's PendingTasks from settling, blocking SSR stabilization.
    afterNextRender(() => this.auth.refresh());
  }

  loginHref(): string {
    const returnTo = encodeURIComponent(this.router.url || '/');
    return `/auth/login?returnTo=${returnTo}`;
  }
}
