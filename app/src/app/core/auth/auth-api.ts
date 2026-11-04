import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface LoginRequest {
  readonly email: string;
  readonly password: string;
}

export interface SessionResponse {
  readonly email: string;
}

export interface PasswordResetRequest {
  readonly email: string;
}

export interface PasswordResetConfirmRequest {
  readonly accountId: string;
  readonly token: string;
  readonly newPassword: string;
}

const BASE = '/api/auth';

/** Endpointy `/api/auth` (względne adresy, JSON camelCase). Błędy jako `HttpErrorResponse`. */
@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly http = inject(HttpClient);

  /** Odświeża ciasteczko `XSRF-TOKEN` dla bieżącej tożsamości. */
  async csrf(): Promise<void> {
    await firstValueFrom(this.http.get<void>(`${BASE}/csrf`));
  }

  async login(request: LoginRequest): Promise<void> {
    await firstValueFrom(this.http.post<void>(`${BASE}/login`, request));
  }

  async logout(): Promise<void> {
    await firstValueFrom(this.http.post<void>(`${BASE}/logout`, null));
  }

  session(): Promise<SessionResponse> {
    return firstValueFrom(this.http.get<SessionResponse>(`${BASE}/session`));
  }

  async requestPasswordReset(request: PasswordResetRequest): Promise<void> {
    await firstValueFrom(this.http.post<void>(`${BASE}/password-reset/request`, request));
  }

  async confirmPasswordReset(request: PasswordResetConfirmRequest): Promise<void> {
    await firstValueFrom(this.http.post<void>(`${BASE}/password-reset/confirm`, request));
  }
}
