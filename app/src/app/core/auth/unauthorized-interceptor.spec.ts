import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { HttpTestingController } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  provideTestHttp,
  respondNoContent,
  respondProblem,
  signIn,
  signOut,
} from '../../../testing/auth-testing';
import { AuthSession } from './auth-session';

@Component({ template: '' })
class Blank {}

describe('unauthorizedInterceptor', () => {
  let http: HttpTestingController;
  let client: HttpClient;
  let session: AuthSession;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: '**', component: Blank }]), provideTestHttp()],
    });
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
    session = TestBed.inject(AuthSession);
  });

  afterEach(() => http.verify());

  async function failWith(url: string, status: number): Promise<HttpErrorResponse> {
    const result = firstValueFrom(client.get(url));
    respondProblem(http.expectOne(url), status);
    try {
      await result;
    } catch (error) {
      return error as HttpErrorResponse;
    }
    throw new Error('Żądanie powinno zakończyć się błędem.');
  }

  it('expires the session on 401 from the API and passes the error on', async () => {
    await signIn();

    const error = await failWith('/api/x', 401);

    expect(error.status).toBe(401);
    expect(session.status()).toBe('anonymous');
    respondNoContent(http.expectOne('/api/auth/csrf'));
    expect(session.takeNotice()).toBe('expired');
  });

  it('ignores 401 from the login itself', async () => {
    await signIn();
    const result = firstValueFrom(client.post('/api/auth/login', {}));
    respondProblem(http.expectOne('/api/auth/login'), 401, { code: 'invalid_credentials' });
    await expect(result).rejects.toBeInstanceOf(HttpErrorResponse);

    expect(session.status()).toBe('authenticated');
  });

  it('ignores 401 while the session is unknown or anonymous', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate');
    await failWith('/api/x', 401);
    expect(session.status()).toBe('unknown');

    await signOut();
    await failWith('/api/x', 401);

    expect(navigate).not.toHaveBeenCalled();
    expect(session.takeNotice()).toBeNull();
  });

  it('leaves other statuses unchanged', async () => {
    await signIn();

    expect((await failWith('/api/x', 403)).status).toBe(403);
    expect((await failWith('/api/x', 400)).status).toBe(400);
    expect(session.status()).toBe('authenticated');
  });

  it('navigates once for two parallel 401 responses', async () => {
    await signIn();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate');
    const first = firstValueFrom(client.get('/api/a'));
    const second = firstValueFrom(client.get('/api/b'));

    respondProblem(http.expectOne('/api/a'), 401);
    respondProblem(http.expectOne('/api/b'), 401);
    await Promise.allSettled([first, second]);

    expect(navigate).toHaveBeenCalledTimes(1);
    respondNoContent(http.expectOne('/api/auth/csrf'));
  });
});
