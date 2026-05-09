import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { catchError, of, tap } from 'rxjs';
import { AuthMe } from '../models';

// Talks to the BFF's /auth/me endpoint to find out if the user is signed in
// and who they are. Tokens never reach JavaScript — only the profile shape
// returned here, which the BFF derives from the OIDC id_token claims.
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly state = signal<AuthMe>({ authenticated: false });

  readonly authenticated = computed(() => this.state().authenticated);
  readonly user = computed(() => this.state().user ?? null);
  readonly displayName = computed(
    () => this.state().user?.name ?? this.state().user?.email ?? null,
  );

  /** Refresh from /auth/me. Cheap (single Redis lookup at the BFF) so safe to
   *  call on every component that wants to gate UI on auth. */
  refresh(): void {
    this.http
      .get<AuthMe>('/auth/me')
      .pipe(
        catchError(() => of<AuthMe>({ authenticated: false })),
        tap((me) => this.state.set(me)),
      )
      .subscribe();
  }
}
