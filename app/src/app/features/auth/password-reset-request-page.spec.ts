import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import {
  nextRequest,
  provideTestHttp,
  respondNetworkError,
  respondNoContent,
  respondProblem,
  typeInto,
} from '../../../testing/auth-testing';
import { PasswordResetRequestPage } from './password-reset-request-page';

describe('PasswordResetRequestPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'reset-hasla', component: PasswordResetRequestPage }]),
        provideTestHttp(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    document.body.innerHTML = '';
  });

  async function open() {
    const harness = await RouterTestingHarness.create('/reset-hasla');
    const element = harness.routeNativeElement as HTMLElement;
    document.body.appendChild(harness.fixture.nativeElement);
    return {
      element,
      stable: () => harness.fixture.whenStable(),
      email: element.querySelector('#reset-email') as HTMLInputElement,
      submit: element.querySelector('button[type="submit"]') as HTMLButtonElement,
    };
  }

  type Page = Awaited<ReturnType<typeof open>>;

  async function send(page: Page, email = 'owner@example.com') {
    typeInto(page.email, email);
    page.submit.click();
    await page.stable();
  }

  async function message(page: Page, role: 'status' | 'alert'): Promise<HTMLElement> {
    return vi.waitFor(async () => {
      await page.stable();
      const element = page.element.querySelector(`[role="${role}"]`) as HTMLElement | null;
      expect(element).not.toBeNull();
      return element as HTMLElement;
    });
  }

  it('has a labelled email field, the main action and a way back', async () => {
    const { element, email, submit } = await open();

    expect(element.querySelector('label')?.textContent?.trim()).toBe('E-mail (wymagane)');
    expect(email.getAttribute('autocomplete')).toBe('username');
    expect(submit.textContent?.trim()).toBe('Wyślij link');
    expect(submit.classList).toContain('btn-primary');
    expect(element.querySelector('a[href="/logowanie"]')?.textContent?.trim()).toBe(
      'Wróć do logowania',
    );
  });

  it.each([
    ['', 'Podaj adres e-mail.'],
    ['to-nie-jest-adres', 'Podaj poprawny adres e-mail.'],
  ])('validates %j without sending a request', async (value, error) => {
    const page = await open();
    await send(page, value);

    expect(page.element.querySelector('#reset-email-blad')?.textContent).toContain(error);
    expect(document.activeElement).toBe(page.email);
    http.expectNone('/api/auth/csrf');
  });

  it('fetches the CSRF token, sends the trimmed email and confirms with focus', async () => {
    const page = await open();
    await send(page, ' owner@example.com ');

    expect(page.submit.textContent).toContain('Wysyłanie…');
    respondNoContent(await nextRequest('/api/auth/csrf'));
    const request = await nextRequest({ method: 'POST', url: '/api/auth/password-reset/request' });
    expect(request.request.body).toEqual({ email: 'owner@example.com' });
    respondNoContent(request, 202);

    const status = await message(page, 'status');
    expect(status.textContent?.trim()).toBe(
      'Jeśli konto istnieje, wysłaliśmy link do ustawienia hasła. Link jest ważny 30 minut.',
    );
    await vi.waitFor(() => expect(document.activeElement).toBe(status));
    expect(page.email.value).toBe('owner@example.com');
  });

  it('blocks a second submission while sending', async () => {
    const page = await open();
    await send(page);
    page.submit.click();

    respondNoContent(await nextRequest('/api/auth/csrf'));
    respondNoContent(await nextRequest('/api/auth/password-reset/request'), 202);
    await message(page, 'status');
  });

  it('shows the rate limit message on 429', async () => {
    const page = await open();
    await send(page);

    respondNoContent(await nextRequest('/api/auth/csrf'));
    respondProblem(await nextRequest('/api/auth/password-reset/request'), 429, {
      code: 'rate_limited',
    });

    const alert = await message(page, 'alert');
    expect(alert.textContent?.trim()).toBe('Zbyt wiele prób. Spróbuj ponownie za chwilę.');
    await vi.waitFor(() => expect(document.activeElement).toBe(alert));
  });

  it('shows the network message when the server is unreachable', async () => {
    const page = await open();
    await send(page);

    respondNetworkError(await nextRequest('/api/auth/csrf'));

    expect((await message(page, 'alert')).textContent?.trim()).toBe(
      'Nie udało się połączyć z serwerem.',
    );
  });

  it('shows API field errors next to the field', async () => {
    const page = await open();
    await send(page);

    respondNoContent(await nextRequest('/api/auth/csrf'));
    respondProblem(await nextRequest('/api/auth/password-reset/request'), 400, {
      errors: { email: ['Podaj poprawny adres e-mail.'] },
    });

    await vi.waitFor(async () => {
      await page.stable();
      expect(page.element.querySelector('#reset-email-blad')?.textContent).toContain(
        'Podaj poprawny adres e-mail.',
      );
    });
  });
});
