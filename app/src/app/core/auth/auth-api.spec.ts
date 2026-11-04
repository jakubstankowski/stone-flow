import { HttpClient } from '@angular/common/http';
import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { provideTestHttp, respondNoContent } from '../../../testing/auth-testing';
import { AuthApi } from './auth-api';

function setXsrfCookie(value: string): void {
  document.cookie = `XSRF-TOKEN=${value}; path=/`;
}

describe('AuthApi', () => {
  let api: AuthApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideTestHttp()] });
    api = TestBed.inject(AuthApi);
    http = TestBed.inject(HttpTestingController);
    setXsrfCookie('token-1');
  });

  afterEach(() => {
    http.verify();
    document.cookie = 'XSRF-TOKEN=; path=/; expires=Thu, 01 Jan 1970 00:00:00 GMT';
  });

  it('calls the six endpoints with relative URLs, methods and camelCase bodies', async () => {
    const calls: Promise<unknown>[] = [
      api.csrf(),
      api.login({ email: 'owner@example.com', password: 'secret' }),
      api.logout(),
      api.session(),
      api.requestPasswordReset({ email: 'owner@example.com' }),
      api.confirmPasswordReset({ accountId: 'a1', token: 't1', newPassword: 'long-password-1' }),
    ];

    const expected: [string, string, unknown][] = [
      ['GET', '/api/auth/csrf', null],
      ['POST', '/api/auth/login', { email: 'owner@example.com', password: 'secret' }],
      ['POST', '/api/auth/logout', null],
      ['GET', '/api/auth/session', null],
      ['POST', '/api/auth/password-reset/request', { email: 'owner@example.com' }],
      [
        'POST',
        '/api/auth/password-reset/confirm',
        { accountId: 'a1', token: 't1', newPassword: 'long-password-1' },
      ],
    ];
    for (const [method, url, body] of expected) {
      const request = http.expectOne({ method, url });
      expect(request.request.body).toEqual(body);
      if (url === '/api/auth/session') {
        request.flush({ email: 'owner@example.com' });
      } else {
        respondNoContent(request, url.includes('request') ? 202 : 204);
      }
    }

    await Promise.all(calls);
    await expect(calls[3]).resolves.toEqual({ email: 'owner@example.com' });
  });

  it('sends X-XSRF-TOKEN on POST but not on GET', async () => {
    const csrf = api.csrf();
    const login = api.login({ email: 'owner@example.com', password: 'secret' });

    const get = http.expectOne('/api/auth/csrf');
    const post = http.expectOne('/api/auth/login');
    expect(get.request.headers.has('X-XSRF-TOKEN')).toBe(false);
    expect(post.request.headers.get('X-XSRF-TOKEN')).toBe('token-1');

    respondNoContent(get);
    respondNoContent(post);
    await Promise.all([csrf, login]);
  });

  it('uses a refreshed cookie in the next POST', async () => {
    const first = api.logout();
    const logout = http.expectOne('/api/auth/logout');
    expect(logout.request.headers.get('X-XSRF-TOKEN')).toBe('token-1');
    respondNoContent(logout);
    await first;

    setXsrfCookie('token-2');
    const second = api.requestPasswordReset({ email: 'owner@example.com' });
    const request = http.expectOne('/api/auth/password-reset/request');
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('token-2');

    respondNoContent(request, 202);
    await second;
  });

  it('never sends the token to another origin', async () => {
    const result = firstValueFrom(
      TestBed.inject(HttpClient).post('https://evil.example.com/api/auth/login', {}),
    );
    const request = http.expectOne('https://evil.example.com/api/auth/login');

    expect(request.request.headers.has('X-XSRF-TOKEN')).toBe(false);
    request.flush({});
    await result;
  });
});
