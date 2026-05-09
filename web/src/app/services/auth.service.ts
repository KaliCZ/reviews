import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { catchError, of, tap } from 'rxjs';
import { AuthMe } from '../models';

// Tokens never reach JavaScript; this only sees the BFF-derived profile shape.
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly state = signal<AuthMe>({ authenticated: false });

  readonly authenticated = computed(() => this.state().authenticated);
  readonly user = computed(() => this.state().user ?? null);
  readonly displayName = computed(
    () => this.state().user?.name ?? this.state().user?.email ?? null,
  );

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
