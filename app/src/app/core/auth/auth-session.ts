import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ApiError, toApiError } from '../http/api-error';
import { AuthApi } from './auth-api';
import { pathOf, safeReturnUrl } from './return-url';

export type SessionState =
  | { readonly status: 'unknown' }
  | { readonly status: 'anonymous' }
  | { readonly status: 'authenticated'; readonly email: string };

/** Jednorazowy komunikat dla ekranu logowania; nie trafia do URL ani `history.state`. */
export type AuthNotice = 'expired' | 'logged-out' | 'password-set' | 'unreachable';

export interface ResetLink {
  readonly accountId: string;
  readonly token: string;
}

/**
 * Stan sesji właściciela na sygnałach. Sesję zna tylko API (ciasteczko HttpOnly); tu jest
 * jej odbicie dla interfejsu. Ochrona tras jest wygodą, dostęp wymusza API.
 */
@Injectable({ providedIn: 'root' })
export class AuthSession {
  private readonly api = inject(AuthApi);
  private readonly router = inject(Router);

  private readonly state = signal<SessionState>({ status: 'unknown' });
  private readonly checkingState = signal(false);
  private notice: AuthNotice | null = null;
  private pendingCheck: Promise<void> | null = null;

  readonly status = computed(() => this.state().status);
  readonly email = computed(() => {
    const state = this.state();
    return state.status === 'authenticated' ? state.email : null;
  });
  /** Trwa pierwsze sprawdzenie sesji (`GET /api/auth/session`). */
  readonly checking = this.checkingState.asReadonly();

  /** Ustala stan sesji; równoległe wywołania współdzielą jedno żądanie. */
  ensureKnown(): Promise<void> {
    if (this.state().status !== 'unknown') {
      return Promise.resolve();
    }
    this.pendingCheck ??= this.loadSession().finally(() => (this.pendingCheck = null));
    return this.pendingCheck;
  }

  /** Token CSRF → logowanie → nowy token CSRF → sesja. Zwraca błąd albo `null`. */
  async login(email: string, password: string): Promise<ApiError | null> {
    try {
      await this.api.csrf();
      await this.api.login({ email, password });
      await this.api.csrf();
      const session = await this.api.session();
      this.state.set({ status: 'authenticated', email: session.email });
      return null;
    } catch (error) {
      return toApiError(error);
    }
  }

  /** Token CSRF → wylogowanie → nowy anonimowy token → ekran logowania z „Wylogowano.”. */
  async logout(): Promise<ApiError | null> {
    try {
      await this.api.csrf();
      await this.api.logout();
    } catch (error) {
      return toApiError(error);
    }
    await this.refreshCsrfQuietly();
    this.state.set({ status: 'anonymous' });
    this.notice = 'logged-out';
    await this.router.navigateByUrl('/logowanie');
    return null;
  }

  /**
   * Sesja wygasła lub jest nieważna (`401`). Działa tylko raz dla zalogowanego stanu, więc
   * kilka równoległych `401` daje jedną nawigację.
   */
  expire(): void {
    if (this.state().status !== 'authenticated') {
      return;
    }
    const path = pathOf(this.router.url);
    this.state.set({ status: 'anonymous' });
    this.notice = 'expired';
    void this.refreshCsrfQuietly();
    void this.router.navigate(['/logowanie'], {
      queryParams: safeReturnUrl(path) === path ? { powrot: path } : {},
    });
  }

  async requestPasswordReset(email: string): Promise<ApiError | null> {
    try {
      await this.api.csrf();
      await this.api.requestPasswordReset({ email });
      return null;
    } catch (error) {
      return toApiError(error);
    }
  }

  async confirmPasswordReset(link: ResetLink, newPassword: string): Promise<ApiError | null> {
    try {
      await this.api.csrf();
      await this.api.confirmPasswordReset({ ...link, newPassword });
      return null;
    } catch (error) {
      return toApiError(error);
    }
  }

  /** Hasło ustawione: API unieważniło sesje, więc stan jest anonimowy. */
  async passwordSet(): Promise<void> {
    this.state.set({ status: 'anonymous' });
    this.notice = 'password-set';
    await this.router.navigateByUrl('/logowanie');
  }

  /** Odczytuje i usuwa komunikat, aby odświeżenie ekranu go nie powtórzyło. */
  takeNotice(): AuthNotice | null {
    const notice = this.notice;
    this.notice = null;
    return notice;
  }

  private async loadSession(): Promise<void> {
    this.checkingState.set(true);
    try {
      const session = await this.api.session();
      this.state.set({ status: 'authenticated', email: session.email });
    } catch (error) {
      this.state.set({ status: 'anonymous' });
      if (toApiError(error).kind === 'network') {
        this.notice = 'unreachable';
      }
    } finally {
      this.checkingState.set(false);
    }
  }

  private async refreshCsrfQuietly(): Promise<void> {
    try {
      await this.api.csrf();
    } catch {
      // Kolejne żądanie zmieniające stan i tak zaczyna od pobrania tokenu.
    }
  }
}
