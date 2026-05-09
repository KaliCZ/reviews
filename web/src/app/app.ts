import { Component, OnInit, inject } from '@angular/core';
import { RouterLink, RouterOutlet, Router } from '@angular/router';
import { AuthService } from './services/auth.service';
import { ThemeToggle } from './components/theme-toggle';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, ThemeToggle],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  ngOnInit(): void {
    // BFF call — unauthenticated returns 401 which the service swallows. The
    // header re-renders reactively when the signal flips.
    this.auth.refresh();
  }

  // Sign-in deep-links back to the page the user was on.
  loginHref(): string {
    const returnTo = encodeURIComponent(this.router.url || '/');
    return `/auth/login?returnTo=${returnTo}`;
  }
}
