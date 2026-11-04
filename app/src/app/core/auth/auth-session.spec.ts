import { Component } from '@angular/core';
import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import {
  nextRequest,
  provideTestHttp,
  respondNetworkError,
  respondNoContent,
  respondProblem,
  signIn,
} from '../../../testing/auth-testing';
import { AuthSession } from './auth-session';

@Component({ template: '' })
class Blank {}

describe('AuthSession', () => {
  let session: AuthSession;
  let http: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: '**', component: Blank }]), provideTestHttp()],
    });
    session = TestBed.inject(AuthSession);
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => http.verify());

  describe('ensureKnown', () => {
    it('shares one request between concurrent callers and exposes the email', async () => {
      const first = session.ensureKnown();
      const second = session.ensureKnown();
      expect(session.checking()).toBe(true);

      http.expectOne('/api/auth/session').flush({ email: 'owner@example.com' });
      await Promise.all([first, second]);

      expect(session.status()).toBe('authenticated');
      expect(session.email()).toBe('owner@example.com');
      expect(session.checking()).toBe(false);

      await session.ensureKnown();
      http.expectNone('/api/auth/session');
    });

    it('treats 401 as anonymous without a notice', async () => {
      const known = session.ensureKnown();
      respondProblem(http.expectOne('/api/auth/session'), 401);
      await known;

      expect(session.status()).toBe('anonymous');
      expect(session.email()).toBeNull();
      expect(session.takeNotice()).toBeNull();
    });

    it.each([0, 503])(
      'treats status %i as anonymous with the unreachable notice',
      async (status) => {
        const known = session.ensureKnown();
        const request = http.expectOne('/api/auth/session');
        if (status === 0) {
          respondNetworkError(request);
        } else {
          respondProblem(request, status);
        }
        await known;

        expect(session.status()).toBe('anonymous');
        expect(session.takeNotice()).toBe('unreachable');
      },
    );
  });

  it('logs in with csrf, login, csrf and session in this order', async () => {
    const result = session.login('owner@example.com', 'secret-password');

    respondNoContent(await nextRequest({ method: 'GET', url: '/api/auth/csrf' }));
    const login = await nextRequest({ method: 'POST', url: '/api/auth/login' });
    expect(login.request.body).toEqual({ email: 'owner@example.com', password: 'secret-password' });
    respondNoContent(login);
    respondNoContent(await nextRequest({ method: 'GET', url: '/api/auth/csrf' }));
    (await nextRequest('/api/auth/session')).flush({ email: 'owner@example.com' });

    expect(await result).toBeNull();
    expect(session.status()).toBe('authenticated');
  });

  it('returns the login error and stays anonymous', async () => {
    const result = session.login('owner@example.com', 'wrong');
    respondNoContent(await nextRequest('/api/auth/csrf'));
    respondProblem(await nextRequest('/api/auth/login'), 401, { code: 'invalid_credentials' });

    expect(await result).toEqual({
      kind: 'problem',
      status: 401,
      detail: null,
      code: 'invalid_credentials',
    });
    expect(session.status()).not.toBe('authenticated');
  });

  it('logs out with csrf, logout and csrf, then shows the login screen once', async () => {
    await signIn();
    const result = session.logout();

    respondNoContent(await nextRequest('/api/auth/csrf'));
    respondNoContent(await nextRequest({ method: 'POST', url: '/api/auth/logout' }));
    respondNoContent(await nextRequest('/api/auth/csrf'));

    expect(await result).toBeNull();
    expect(session.status()).toBe('anonymous');
    expect(router.url).toBe('/logowanie');
    expect(session.takeNotice()).toBe('logged-out');
    expect(session.takeNotice()).toBeNull();
  });

  it('expires once, keeping the return path and refreshing the token', async () => {
    await signIn();
    await router.navigateByUrl('/wydatki?strona=2');
    const navigate = vi.spyOn(router, 'navigate');

    session.expire();
    session.expire();

    expect(session.status()).toBe('anonymous');
    expect(navigate).toHaveBeenCalledTimes(1);
    expect(navigate).toHaveBeenCalledWith(['/logowanie'], { queryParams: { powrot: '/wydatki' } });
    respondNoContent(http.expectOne('/api/auth/csrf'));
    expect(session.takeNotice()).toBe('expired');
  });

  it('ignores expire when nobody is signed in', () => {
    const navigate = vi.spyOn(router, 'navigate');
    session.expire();

    expect(navigate).not.toHaveBeenCalled();
    expect(session.takeNotice()).toBeNull();
  });

  it('becomes anonymous with a notice after the password is set', async () => {
    await signIn();
    await session.passwordSet();

    expect(session.status()).toBe('anonymous');
    expect(router.url).toBe('/logowanie');
    expect(session.takeNotice()).toBe('password-set');
  });
});
