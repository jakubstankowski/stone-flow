import { HttpTestingController } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import {
  nextRequest,
  provideTestHttp,
  respondNoContent,
  respondProblem,
  typeInto,
} from '../../../testing/auth-testing';
import { AuthSession } from '../../core/auth/auth-session';
import { SetPasswordPage } from './set-password-page';

@Component({ template: '' })
class Blank {}

const ACCOUNT_ID = '3f2b8c1e-0000-4000-8000-000000000001';
const TOKEN = 'tajny_TOKEN-resetu-123';
const LINK = `/reset-hasla/nowe#konto=${ACCOUNT_ID}&token=${TOKEN}`;
const PASSWORD = 'bardzo-dlugie-haslo';

describe('SetPasswordPage', () => {
  let http: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'reset-hasla/nowe', component: SetPasswordPage },
          { path: '**', component: Blank },
        ]),
        provideTestHttp(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    http.verify();
    document.body.innerHTML = '';
  });

  async function open(url = LINK) {
    const harness = await RouterTestingHarness.create(url);
    await harness.fixture.whenStable();
    const element = harness.routeNativeElement as HTMLElement;
    document.body.appendChild(harness.fixture.nativeElement);
    return {
      element,
      stable: () => harness.fixture.whenStable(),
      newPassword: element.querySelector('#nowe-haslo') as HTMLInputElement,
      repeatPassword: element.querySelector('#powtorz-haslo') as HTMLInputElement,
      submit: element.querySelector('button[type="submit"]') as HTMLButtonElement,
    };
  }

  type Page = Awaited<ReturnType<typeof open>>;

  async function send(page: Page, password = PASSWORD, repeat = password) {
    typeInto(page.newPassword, password);
    typeInto(page.repeatPassword, repeat);
    page.submit.click();
    await page.stable();
  }

  async function confirmRequest() {
    respondNoContent(await nextRequest('/api/auth/csrf'));
    return nextRequest({ method: 'POST', url: '/api/auth/password-reset/confirm' });
  }

  async function invalidLinkMessage(page: Page): Promise<HTMLElement> {
    return vi.waitFor(async () => {
      await page.stable();
      const alert = page.element.querySelector('[role="alert"]') as HTMLElement | null;
      expect(alert?.textContent?.trim()).toBe(
        'Link wygasł lub został już użyty. Poproś o nowy link.',
      );
      return alert as HTMLElement;
    });
  }

  it('removes the fragment from the address before any request', async () => {
    const page = await open();

    expect(router.url).toBe('/reset-hasla/nowe');
    expect(page.element.innerHTML).not.toContain(TOKEN);
    http.expectNone(() => true);
  });

  it('sends the account, the token and the new password, then shows the login screen', async () => {
    const log = vi.spyOn(console, 'log');
    const page = await open();
    await send(page);

    expect(page.submit.textContent).toContain('Zapisywanie…');
    const request = await confirmRequest();
    expect(request.request.body).toEqual({
      accountId: ACCOUNT_ID,
      token: TOKEN,
      newPassword: PASSWORD,
    });
    respondNoContent(request);

    await vi.waitFor(() => expect(router.url).toBe('/logowanie'));
    const session = TestBed.inject(AuthSession);
    expect(session.status()).toBe('anonymous');
    expect(session.takeNotice()).toBe('password-set');
    expect(JSON.stringify(log.mock.calls)).not.toContain(TOKEN);
  });

  it('shows the invalid link state without a request when the fragment is missing', async () => {
    const page = await open('/reset-hasla/nowe');

    await invalidLinkMessage(page);
    expect(page.element.querySelector('form')).toBeNull();
    expect(page.element.querySelector('a[href="/reset-hasla"]')?.textContent?.trim()).toBe(
      'Poproś o nowy link',
    );
  });

  it.each([
    ['', '', '#nowe-haslo-blad', 'Podaj nowe hasło.'],
    ['krotkie', 'krotkie', '#nowe-haslo-blad', 'Hasło musi mieć co najmniej 12 znaków.'],
    [PASSWORD, 'inne-haslo-dlugie', '#powtorz-haslo-blad', 'Hasła nie są takie same.'],
  ])('validates %j / %j without a request', async (password, repeat, errorId, message) => {
    const page = await open();
    await send(page, password, repeat);

    expect(page.element.querySelector(errorId)?.textContent).toContain(message);
    http.expectNone('/api/auth/csrf');
  });

  it('shows the invalid link state when the API rejects the link', async () => {
    const page = await open();
    await send(page);

    respondProblem(await confirmRequest(), 400, {
      code: 'reset_link_invalid',
      detail: 'Link wygasł lub został już użyty. Poproś o nowy link.',
    });

    const alert = await invalidLinkMessage(page);
    await vi.waitFor(() => expect(document.activeElement).toBe(alert));
    expect(router.url).toBe('/reset-hasla/nowe');
  });

  it('asks to refresh on a CSRF rejection and keeps the form', async () => {
    const page = await open();
    await send(page);

    respondProblem(await confirmRequest(), 400, { code: 'csrf_invalid' });

    await vi.waitFor(async () => {
      await page.stable();
      expect(page.element.querySelector('form [role="alert"]')?.textContent?.trim()).toBe(
        'Odśwież stronę i spróbuj ponownie.',
      );
    });
    expect(page.element.querySelector('form')).not.toBeNull();
  });

  it('shows the API password error next to the field', async () => {
    const page = await open();
    await send(page);

    respondProblem(await confirmRequest(), 400, {
      errors: { NewPassword: ['Hasło nie może być takie samo jak adres e-mail.'] },
    });

    await vi.waitFor(async () => {
      await page.stable();
      expect(page.element.querySelector('#nowe-haslo-blad')?.textContent).toContain(
        'Hasło nie może być takie samo jak adres e-mail.',
      );
    });
    await vi.waitFor(() => expect(document.activeElement).toBe(page.newPassword));
  });

  it('blocks a second submission while saving', async () => {
    const page = await open();
    await send(page);
    page.submit.click();

    respondNoContent(await confirmRequest());
    await vi.waitFor(() => expect(router.url).toBe('/logowanie'));
  });
});
