import { HttpTestingController } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import {
  nextRequest,
  provideTestHttp,
  respondNetworkError,
  respondProblem,
  typeInto,
} from '../../../testing/auth-testing';
import { OrderCreatePage } from './order-create-page';

@Component({ template: '' })
class Blank {}

type Key = `${string}-${string}-${string}-${string}-${string}`;

describe('OrderCreatePage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    const keys: Key[] = [
      '11111111-1111-4111-8111-111111111111',
      '22222222-2222-4222-8222-222222222222',
    ];
    vi.spyOn(crypto, 'randomUUID').mockImplementation(() => keys.shift() ?? keys[0]);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'zamowienia/nowe', component: OrderCreatePage },
          { path: '**', component: Blank },
        ]),
        provideTestHttp(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
    document.body.innerHTML = '';
  });

  async function open() {
    const harness = await RouterTestingHarness.create('/zamowienia/nowe');
    const element = harness.routeNativeElement as HTMLElement;
    document.body.appendChild(harness.fixture.nativeElement);
    const stable = () => harness.fixture.whenStable();
    const field = (id: string) => element.querySelector(`#${id}`) as HTMLInputElement;
    const fill = () => {
      typeInto(field('zamowienie-klient-nazwa'), 'Jan Testowy');
      const channel = element.querySelector('#zamowienie-kanal') as HTMLSelectElement;
      channel.value = 'olx';
      channel.dispatchEvent(new Event('input', { bubbles: true }));
      channel.dispatchEvent(new Event('change', { bubbles: true }));
      typeInto(field('zamowienie-wartosc'), '250');
    };
    const submitButton = () => element.querySelector('button[type="submit"]') as HTMLButtonElement;
    return { harness, element, stable, field, fill, submitButton };
  }

  const created = { id: 'a1', number: 'ZR-2026-0001', version: 'v1' };

  it('sends one request on a double click and opens the saved order', async () => {
    const page = await open();
    page.fill();
    page.submitButton().click();
    page.submitButton().click();
    await page.stable();

    expect(page.submitButton().getAttribute('aria-busy')).toBe('true');
    expect(page.submitButton().textContent).toContain('Zapisywanie…');
    const request = await nextRequest({ method: 'POST', url: '/api/orders' });
    expect(request.request.headers.get('Idempotency-Key')).toBe(
      '11111111-1111-4111-8111-111111111111',
    );
    expect(request.request.body.value).toBe('250.00');
    request.flush(created, { status: 201, statusText: 'Created' });

    const router = TestBed.inject(Router);
    await vi.waitFor(() => expect(router.url).toBe('/zamowienia/a1'));
  });

  it('retries with the same key after a lost response', async () => {
    const page = await open();
    page.fill();
    page.submitButton().click();
    respondNetworkError(await nextRequest({ method: 'POST', url: '/api/orders' }));
    await vi.waitFor(async () => {
      await page.stable();
      expect(page.element.textContent).toContain('Nie udało się połączyć z serwerem.');
    });

    page.submitButton().click();
    const retry = await nextRequest({ method: 'POST', url: '/api/orders' });
    expect(retry.request.headers.get('Idempotency-Key')).toBe(
      '11111111-1111-4111-8111-111111111111',
    );
    retry.flush(created, { status: 200, statusText: 'OK' });

    const router = TestBed.inject(Router);
    await vi.waitFor(() => expect(router.url).toBe('/zamowienia/a1'));
  });

  it('links to the order saved with a reused key and starts the next one with a new key', async () => {
    const page = await open();
    page.fill();
    page.submitButton().click();
    respondProblem(await nextRequest({ method: 'POST', url: '/api/orders' }), 409, {
      code: 'idempotency_key_reused',
      orderId: 'a1',
      orderNumber: 'ZR-2026-0007',
    });

    await vi.waitFor(async () => {
      await page.stable();
      expect(page.element.textContent).toContain('Ten formularz zapisał już zamówienie');
    });
    const link = page.element.querySelector('a[href="/zamowienia/a1"]');
    expect(link?.textContent?.trim()).toBe('ZR-2026-0007');
    expect(page.field('zamowienie-klient-nazwa').value).toBe('Jan Testowy');

    const next = Array.from(page.element.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === 'Dodaj kolejne zamówienie',
    ) as HTMLButtonElement;
    next.click();
    await page.stable();

    expect(page.element.textContent).not.toContain('Ten formularz zapisał już zamówienie');
    expect(page.field('zamowienie-klient-nazwa').value).toBe('');
    expect(page.element.querySelector('#zamowienie-klient-nazwa-blad')).toBeNull();

    page.fill();
    page.submitButton().click();
    const request = await nextRequest({ method: 'POST', url: '/api/orders' });
    expect(request.request.headers.get('Idempotency-Key')).toBe(
      '22222222-2222-4222-8222-222222222222',
    );
    request.flush(created, { status: 201, statusText: 'Created' });
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/zamowienia/a1'));
  });
});
