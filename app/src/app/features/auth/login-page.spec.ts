import { HttpTestingController } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import {
  nextRequest,
  provideTestHttp,
  respondNetworkError,
  respondNoContent,
  respondProblem,
  typeInto,
} from '../../../testing/auth-testing';
import { AuthNotice, AuthSession } from '../../core/auth/auth-session';
import { LoginPage } from './login-page';

@Component({ template: '' })
class Blank {}

describe('LoginPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'logowanie', component: LoginPage },
          { path: '**', component: Blank },
        ]),
        provideTestHttp(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    document.body.innerHTML = '';
  });

  async function open(url = '/logowanie', notice: AuthNotice | null = null) {
    vi.spyOn(TestBed.inject(AuthSession), 'takeNotice').mockReturnValue(notice);
    const harness = await RouterTestingHarness.create(url);
    const element = harness.routeNativeElement as HTMLElement;
    document.body.appendChild(harness.fixture.nativeElement);
    const stable = () => harness.fixture.whenStable();
    return {
      element,
      stable,
      email: element.querySelector('#logowanie-email') as HTMLInputElement,
      password: element.querySelector('#logowanie-haslo') as HTMLInputElement,
      submit: element.querySelector('button[type="submit"]') as HTMLButtonElement,
    };
  }

  async function fillAndSubmit(page: Awaited<ReturnType<typeof open>>, email: string) {
    typeInto(page.email, email);
    typeInto(page.password, 'secret-password');
    page.submit.click();
    await page.stable();
  }

  async function failLogin(respond: (request: Awaited<ReturnType<typeof nextRequest>>) => void) {
    respondNoContent(await nextRequest('/api/auth/csrf'));
    respond(await nextRequest({ method: 'POST', url: '/api/auth/login' }));
  }

  async function alertText(page: Awaited<ReturnType<typeof open>>): Promise<string | undefined> {
    return vi.waitFor(async () => {
      await page.stable();
      const alert = page.element.querySelector('form [role="alert"]');
      expect(alert).not.toBeNull();
      return alert?.textContent?.trim();
    });
  }

  it('has labelled fields with autocomplete and a link to the password reset', async () => {
    const { element, email, password, submit } = await open();
    const labels = Array.from(element.querySelectorAll('label')).map((label) =>
      label.textContent?.trim(),
    );

    expect(labels).toEqual(['E-mail (wymagane)', 'Hasło (wymagane)']);
    expect(email.type).toBe('email');
    expect(email.getAttribute('autocomplete')).toBe('username');
    expect(email.getAttribute('autocapitalize')).toBe('none');
    expect(email.getAttribute('spellcheck')).toBe('false');
    expect(email.getAttribute('aria-required')).toBe('true');
    expect(password.type).toBe('password');
    expect(password.getAttribute('autocomplete')).toBe('current-password');
    expect(submit.textContent?.trim()).toBe('Zaloguj');
    expect(submit.classList).toContain('btn-primary');
    expect(element.querySelector('a[href="/reset-hasla"]')?.textContent?.trim()).toBe(
      'Nie pamiętam hasła',
    );
  });

  it('validates required fields without sending a request', async () => {
    const page = await open();
    page.submit.click();
    await page.stable();

    expect(page.element.querySelector('#logowanie-email-blad')?.textContent).toContain(
      'Podaj adres e-mail.',
    );
    expect(page.element.querySelector('#logowanie-haslo-blad')?.textContent).toContain(
      'Podaj hasło.',
    );
    expect(page.email.getAttribute('aria-invalid')).toBe('true');
    expect(document.activeElement).toBe(page.email);
    http.expectNone('/api/auth/csrf');
  });

  it('logs in with the trimmed email and returns to the requested section', async () => {
    const page = await open('/logowanie?powrot=/wydatki');
    await fillAndSubmit(page, '  owner@example.com ');

    respondNoContent(await nextRequest('/api/auth/csrf'));
    const login = await nextRequest({ method: 'POST', url: '/api/auth/login' });
    expect(login.request.body).toEqual({ email: 'owner@example.com', password: 'secret-password' });
    respondNoContent(login);
    respondNoContent(await nextRequest('/api/auth/csrf'));
    (await nextRequest('/api/auth/session')).flush({ email: 'owner@example.com' });

    const router = TestBed.inject(Router);
    await vi.waitFor(() => expect(router.url).toBe('/wydatki'));
  });

  it.each(['/logowanie', '/logowanie?powrot=%2F%2Fevil.com'])(
    'goes to the dashboard from %s',
    async (url) => {
      const page = await open(url);
      await fillAndSubmit(page, 'owner@example.com');

      respondNoContent(await nextRequest('/api/auth/csrf'));
      respondNoContent(await nextRequest('/api/auth/login'));
      respondNoContent(await nextRequest('/api/auth/csrf'));
      (await nextRequest('/api/auth/session')).flush({ email: 'owner@example.com' });

      const router = TestBed.inject(Router);
      await vi.waitFor(() => expect(router.url).toBe('/pulpit'));
    },
  );

  it('shows the general message on 401, clears the password and keeps the email', async () => {
    const page = await open();
    await fillAndSubmit(page, 'owner@example.com');

    await failLogin((request) =>
      respondProblem(request, 401, {
        code: 'invalid_credentials',
        detail: 'Nieprawidłowy e-mail lub hasło.',
      }),
    );

    expect(await alertText(page)).toBe(
      'Nieprawidłowy e-mail lub hasło. Po 5 nieudanych próbach logowanie jest blokowane na 15 minut.',
    );
    expect(page.password.value).toBe('');
    expect(page.element.querySelector('#logowanie-haslo-blad')).toBeNull();
    expect(page.email.value).toBe('owner@example.com');
    await vi.waitFor(() =>
      expect(document.activeElement).toBe(page.element.querySelector('form [role="alert"]')),
    );
    expect(TestBed.inject(Router).url).toBe('/logowanie');
  });

  it('shows the rate limit message on 429', async () => {
    const page = await open();
    await fillAndSubmit(page, 'owner@example.com');

    await failLogin((request) => respondProblem(request, 429, { code: 'rate_limited' }));

    expect(await alertText(page)).toBe('Zbyt wiele prób. Spróbuj ponownie za chwilę.');
  });

  it.each([0, 503])('shows the network message for status %i', async (status) => {
    const page = await open();
    await fillAndSubmit(page, 'owner@example.com');

    await failLogin((request) =>
      status === 0 ? respondNetworkError(request) : respondProblem(request, status),
    );

    expect(await alertText(page)).toBe('Nie udało się połączyć z serwerem.');
  });

  it('asks to refresh on a CSRF rejection', async () => {
    const page = await open();
    await fillAndSubmit(page, 'owner@example.com');

    await failLogin((request) => respondProblem(request, 400, { code: 'csrf_invalid' }));

    expect(await alertText(page)).toBe('Odśwież stronę i spróbuj ponownie.');
  });

  it('shows API field errors next to the field', async () => {
    const page = await open();
    await fillAndSubmit(page, 'owner@example.com');

    await failLogin((request) =>
      respondProblem(request, 400, { errors: { Email: ['Podaj poprawny adres e-mail.'] } }),
    );

    await vi.waitFor(async () => {
      await page.stable();
      expect(page.element.querySelector('#logowanie-email-blad')?.textContent).toContain(
        'Podaj poprawny adres e-mail.',
      );
    });
    await vi.waitFor(() => expect(document.activeElement).toBe(page.email));
  });

  it('shows the busy state and blocks a second submission', async () => {
    const page = await open();
    await fillAndSubmit(page, 'owner@example.com');

    expect(page.submit.getAttribute('aria-busy')).toBe('true');
    expect(page.submit.textContent).toContain('Logowanie…');
    page.submit.click();
    page.element.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));

    await failLogin((request) => respondProblem(request, 401));
    await alertText(page);
    expect(page.submit.hasAttribute('aria-busy')).toBe(false);
  });

  it.each<[AuthNotice, string, string]>([
    ['expired', 'status', 'Sesja wygasła. Zaloguj się ponownie.'],
    ['logged-out', 'status', 'Wylogowano.'],
    ['password-set', 'status', 'Hasło ustawione. Zaloguj się.'],
    ['unreachable', 'alert', 'Nie udało się połączyć z serwerem.'],
  ])('shows the %s notice with role %s', async (notice, role, text) => {
    const { element } = await open('/logowanie', notice);

    expect(element.querySelector(`[role="${role}"]`)?.textContent?.trim()).toBe(text);
  });
});
