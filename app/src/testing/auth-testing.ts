import {
  HttpTestingController,
  RequestMatch,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { EnvironmentProviders, Provider } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthSession } from '../app/core/auth/auth-session';
import { provideAppHttp } from '../app/core/http/app-http';

/**
 * Pomocnicze funkcje testów (tylko specyfikacje; wyłączone z `tsconfig.app.json`).
 * HTTP aplikacji z XSRF i interceptorem `401`, ale z backendem `HttpTestingController`.
 */
export function provideTestHttp(): (Provider | EnvironmentProviders)[] {
  return [provideAppHttp(), provideHttpClientTesting()];
}

/** Czeka na kolejne żądanie wieloetapowego przepływu (kroki dzielą mikrozadania). */
export function nextRequest(match: string | RequestMatch): Promise<TestRequest> {
  const http = TestBed.inject(HttpTestingController);
  return vi.waitFor(() => http.expectOne(match), { interval: 1, timeout: 1000 });
}

export function respondNoContent(request: TestRequest, status = 204): void {
  request.flush(null, { status, statusText: 'No Content' });
}

export function respondProblem(
  request: TestRequest,
  status: number,
  body: Record<string, unknown> = {},
): void {
  request.flush({ status, traceId: '00-test', ...body }, { status, statusText: 'Error' });
}

export function respondNetworkError(request: TestRequest): void {
  request.error(new ProgressEvent('error'));
}

/** Ustala sesję jako zalogowaną przez odpowiedź `200` na `GET /api/auth/session`. */
export async function signIn(email = 'owner@example.com'): Promise<void> {
  const known = TestBed.inject(AuthSession).ensureKnown();
  (await nextRequest('/api/auth/session')).flush({ email });
  await known;
}

/** Ustala sesję jako anonimową (`401`). */
export async function signOut(): Promise<void> {
  const known = TestBed.inject(AuthSession).ensureKnown();
  respondProblem(await nextRequest('/api/auth/session'), 401);
  await known;
}

/** Wpisuje wartość w pole tak jak użytkownik. */
export function typeInto(input: HTMLInputElement, value: string): void {
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
}
