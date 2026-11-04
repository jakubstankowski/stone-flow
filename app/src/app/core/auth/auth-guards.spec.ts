import { HttpTestingController } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import {
  nextRequest,
  provideTestHttp,
  respondProblem,
  signIn,
  signOut,
} from '../../../testing/auth-testing';
import { authGuard, guestGuard } from './auth-guards';
import { AuthSession } from './auth-session';

@Component({ template: '' })
class Blank {}

describe('auth guards', () => {
  let router: Router;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'logowanie', component: Blank, canActivate: [guestGuard] },
          {
            path: '',
            canActivateChild: [authGuard],
            children: [
              { path: 'pulpit', component: Blank },
              { path: 'zamowienia', component: Blank },
            ],
          },
        ]),
        provideTestHttp(),
      ],
    });
    router = TestBed.inject(Router);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('waits for the session check before deciding', async () => {
    const navigation = router.navigateByUrl('/zamowienia');
    const request = await nextRequest('/api/auth/session');
    expect(TestBed.inject(AuthSession).checking()).toBe(true);
    expect(router.url).toBe('/');

    request.flush({ email: 'owner@example.com' });

    expect(await navigation).toBe(true);
    expect(router.url).toBe('/zamowienia');
  });

  it('redirects to the login screen with the path without the query', async () => {
    await signOut();

    await router.navigateByUrl('/zamowienia?strona=2');

    expect(router.url).toBe('/logowanie?powrot=%2Fzamowienia');
  });

  it('redirects to the login screen when the API is unreachable', async () => {
    const navigation = router.navigateByUrl('/pulpit');
    respondProblem(await nextRequest('/api/auth/session'), 503);
    await navigation;

    expect(router.url).toBe('/logowanie?powrot=%2Fpulpit');
  });

  it('sends a signed-in user from the login screen to the dashboard', async () => {
    await signIn();

    await router.navigateByUrl('/logowanie');

    expect(router.url).toBe('/pulpit');
  });

  it('shows the login screen to an anonymous user', async () => {
    await signOut();

    await router.navigateByUrl('/logowanie');

    expect(router.url).toBe('/logowanie');
  });
});
