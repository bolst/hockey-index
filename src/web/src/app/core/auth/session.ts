import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthApi, Me } from '../api/auth-api';

/**
 * Host session state. Loaded lazily (host routes only) so public pages never send a credentialed,
 * preflighted request to the API.
 */
@Injectable({ providedIn: 'root' })
export class Session {
  private readonly authApi = inject(AuthApi);
  private readonly state = signal<Me | null | undefined>(undefined);
  private pendingLoad: Promise<Me | null> | null = null;

  readonly me = this.state.asReadonly();
  readonly isLoaded = computed(() => this.state() !== undefined);
  readonly isSignedIn = computed(() => !!this.state());
  readonly isAdmin = computed(() => this.state()?.isAdmin === true);

  ensureLoaded(): Promise<Me | null> {
    const current = this.state();
    if (current !== undefined) {
      return Promise.resolve(current);
    }
    this.pendingLoad ??= firstValueFrom(this.authApi.getMe())
      .then(
        (me) => me,
        (error: unknown) => {
          if (error instanceof HttpErrorResponse && error.status === 401) {
            return null;
          }
          throw error;
        },
      )
      .then((me) => {
        this.state.set(me);
        return me;
      })
      .finally(() => (this.pendingLoad = null));
    return this.pendingLoad;
  }

  signedIn(me: Me): void {
    this.state.set(me);
  }

  async signOut(): Promise<void> {
    await firstValueFrom(this.authApi.signOut());
    this.state.set(null);
  }
}
