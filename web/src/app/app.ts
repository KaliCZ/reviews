import { Component, afterNextRender, inject } from '@angular/core';
import { RouterLink, RouterOutlet, Router } from '@angular/router';
import { AuthService } from './services/auth.service';
import { ThemeToggle } from './components/theme-toggle';
import { LanguageToggle } from './components/language-toggle';
import { ConfirmDialog } from './components/confirm-dialog';
import { TPipe } from './pipes/t.pipe';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, ThemeToggle, LanguageToggle, ConfirmDialog, TPipe],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  constructor() {
    // Browser-only: SSR has no session cookie, and the in-flight request
    // would block Angular's PendingTasks from settling.
    afterNextRender(() => this.auth.refresh());
  }

  loginHref(): string {
    const returnTo = encodeURIComponent(this.router.url || '/');
    return `/auth/login?returnTo=${returnTo}`;
  }
}
