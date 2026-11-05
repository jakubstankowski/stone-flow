import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import {
  nextRequest,
  provideTestHttp,
  respondProblem,
  typeInto,
} from '../../../testing/auth-testing';
import { orderDetails, paymentEntry, refundEntry } from '../../../testing/orders-testing';
import { formatPln } from '../../shared/format/pl-format';
import { OrderDetails } from './order-models';
import { OrderDetailsPage } from './order-details-page';
import { OrdersService } from './orders.service';

function buttonByText(root: ParentNode, text: string): HTMLButtonElement {
  const button = Array.from(root.querySelectorAll('button')).find(
    (candidate) => candidate.textContent?.trim() === text,
  );
  if (!button) {
    throw new Error(`Brak przycisku „${text}”`);
  }
  return button;
}

function dialogWith(root: ParentNode, heading: string): HTMLDialogElement {
  const dialog = Array.from(root.querySelectorAll('dialog')).find((candidate) =>
    candidate.querySelector('h2')?.textContent?.includes(heading),
  );
  if (!dialog) {
    throw new Error(`Brak dialogu „${heading}”`);
  }
  return dialog;
}

describe('OrderDetailsPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'zamowienia/:id', component: OrderDetailsPage }]),
        provideTestHttp(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    document.body.innerHTML = '';
  });

  async function open(order: OrderDetails | null = orderDetails()) {
    const harness = await RouterTestingHarness.create();
    document.body.appendChild(harness.fixture.nativeElement);
    const navigation = harness.navigateByUrl('/zamowienia/a1');
    const element = () => harness.routeNativeElement as HTMLElement;
    const stable = () => harness.fixture.whenStable();
    const settle = async (check: () => void) =>
      vi.waitFor(async () => {
        await stable();
        check();
      });
    const request = await nextRequest({ method: 'GET', url: '/api/orders/a1' });
    if (order) {
      request.flush(order);
    } else {
      respondProblem(request, 404, { code: 'order_not_found' });
    }
    await navigation;
    await settle(() => expect(element().querySelector('.skeleton')).toBeNull());
    return { element, stable, settle };
  }

  it('shows all fields, the status and source markers, and no order delete action', async () => {
    const { element } = await open(orderDetails({ shopStatus: null }));
    const text = element().textContent ?? '';

    expect(element().querySelector('h1')?.textContent?.trim()).toBe('Zamówienie ZR-2026-0001');
    expect(text).toContain('W trakcie');
    expect(text).toContain('Źródło: Ręczne');
    expect(text).toContain('28.10.2026');
    expect(text).toContain('OLX');
    expect(text).toContain(formatPln('1234.50'));
    expect(text).toContain('Jan Testowy');
    expect(text).toContain('Testowa 1, 00-001 Testowo');
    expect(text).toContain('Dostawca A, Dostawca B');
    expect(text).toContain('28.10.2026, 11:00');
    expect(text).toContain('28.10.2026, 12:30');
    expect(text).not.toContain('Status w sklepie');
    const labels = Array.from(element().querySelectorAll('button'))
      .filter((button) => !button.closest('dialog'))
      .map((button) => button.textContent?.trim());
    expect(labels).toContain('Edytuj');
    expect(labels).toContain('Anuluj zamówienie');
    expect(labels).not.toContain('Przywróć zamówienie');
    expect(labels.some((label) => label?.includes('Usuń'))).toBe(false);
    expect(text).toContain('Brak wpłat.');
  });

  it('shows the shop status separately when present', async () => {
    const { element } = await open(
      orderDetails({ source: 'wooCommerce', externalId: '77', shopStatus: 'processing' }),
    );
    expect(element().textContent).toContain('Status w sklepie');
    expect(element().textContent).toContain('processing');
    expect(element().textContent).toContain('Źródło: Sklep');
  });

  it('shows a missing order', async () => {
    const { element } = await open(null);
    expect(element().textContent).toContain('Nie znaleziono zamówienia');
  });

  it('shows the notice left by the create page', async () => {
    vi.spyOn(TestBed.inject(OrdersService), 'takeNotice').mockReturnValueOnce(
      'Zamówienie zapisane.',
    );
    const { element } = await open();
    expect(element().querySelector('[role="status"]')?.textContent).toContain(
      'Zamówienie zapisane.',
    );
  });

  it('saves an edit with the version and reloads the order', async () => {
    const page = await open();
    buttonByText(page.element(), 'Edytuj').click();
    await page.stable();

    const value = page.element().querySelector('#zamowienie-wartosc') as HTMLInputElement;
    expect(value.value).toBe('1234,50');
    typeInto(value, '1300');
    buttonByText(page.element(), 'Zapisz zmiany').click();

    const put = await nextRequest({ method: 'PUT', url: '/api/orders/a1' });
    expect(put.request.body.version).toBe('AAAAAAAAB9E=');
    expect(put.request.body.value).toBe('1300.00');
    expect(put.request.body.customer.name).toBe('Jan Testowy');
    put.flush({ version: 'v2' });
    (await nextRequest({ method: 'GET', url: '/api/orders/a1' })).flush(
      orderDetails({ value: '1300.00', version: 'v2' }),
    );

    await page.settle(() =>
      expect(page.element().querySelector('[role="status"]')?.textContent).toContain(
        'Zmiany zapisane.',
      ),
    );
    expect(page.element().querySelector('#zamowienie-wartosc')).toBeNull();
    expect(page.element().textContent).toContain(formatPln('1300.00'));
  });

  it('keeps the entered data on a conflict and reloads the current data on request', async () => {
    const page = await open();
    buttonByText(page.element(), 'Edytuj').click();
    await page.stable();
    const name = () => page.element().querySelector('#zamowienie-klient-nazwa') as HTMLInputElement;
    typeInto(name(), 'Jan Zmieniony');
    buttonByText(page.element(), 'Zapisz zmiany').click();
    respondProblem(await nextRequest({ method: 'PUT', url: '/api/orders/a1' }), 409, {
      code: 'order_conflict',
    });

    await page.settle(() =>
      expect(page.element().querySelector('[role="alert"]')?.textContent).toContain(
        'Zamówienie zostało zmienione w międzyczasie.',
      ),
    );
    expect(name().value).toBe('Jan Zmieniony');

    buttonByText(page.element(), 'Wczytaj aktualne dane').click();
    (await nextRequest({ method: 'GET', url: '/api/orders/a1' })).flush(
      orderDetails({ customer: { ...orderDetails().customer, name: 'Jan Inny' }, version: 'v9' }),
    );
    await page.settle(() => expect(name().value).toBe('Jan Inny'));
    expect(page.element().textContent).not.toContain(
      'Zamówienie zostało zmienione w międzyczasie.',
    );

    buttonByText(page.element(), 'Zapisz zmiany').click();
    const retry = await nextRequest({ method: 'PUT', url: '/api/orders/a1' });
    expect(retry.request.body.version).toBe('v9');
    retry.flush({ version: 'v10' });
    (await nextRequest({ method: 'GET', url: '/api/orders/a1' })).flush(
      orderDetails({ version: 'v10' }),
    );
    await page.settle(() =>
      expect(page.element().querySelector('#zamowienie-klient-nazwa')).toBeNull(),
    );
  });

  it('asks before cancelling and sends nothing when the dialog is dismissed', async () => {
    const page = await open();
    const dialog = () => dialogWith(page.element(), 'Anulować zamówienie');

    buttonByText(page.element(), 'Anuluj zamówienie').click();
    await page.stable();
    expect(dialog().open).toBe(true);
    expect(dialog().textContent).toContain('Anulować zamówienie ZR-2026-0001?');
    expect(dialog().textContent).toContain('wypadnie ze sprzedaży');
    expect(dialog().textContent).not.toContain('Zamówienie ma wpłaty');
    buttonByText(dialog(), 'Anuluj').click();
    await page.stable();
    http.expectNone('/api/orders/a1/cancel');

    buttonByText(page.element(), 'Anuluj zamówienie').click();
    await page.stable();
    buttonByText(dialog(), 'Anuluj zamówienie').click();
    const cancel = await nextRequest({ method: 'POST', url: '/api/orders/a1/cancel' });
    expect(cancel.request.body).toEqual({ version: 'AAAAAAAAB9E=' });
    cancel.flush({ version: 'v2' });
    (await nextRequest({ method: 'GET', url: '/api/orders/a1' })).flush(
      orderDetails({ fulfillmentStatus: 'cancelled', version: 'v2' }),
    );

    await page.settle(() =>
      expect(page.element().querySelector('[role="status"]')?.textContent).toContain(
        'Zamówienie anulowane.',
      ),
    );
    expect(page.element().textContent).toContain('Anulowane');
    expect(buttonByText(page.element(), 'Przywróć zamówienie')).toBeTruthy();
  });

  it('restores a cancelled order after confirmation', async () => {
    const page = await open(orderDetails({ fulfillmentStatus: 'cancelled' }));
    buttonByText(page.element(), 'Przywróć zamówienie').click();
    await page.stable();
    const dialog = Array.from(page.element().querySelectorAll('dialog')).find(
      (candidate) => candidate.open,
    ) as HTMLDialogElement;
    expect(dialog.textContent).toContain('Przywrócić zamówienie ZR-2026-0001?');
    buttonByText(dialog, 'Przywróć zamówienie').click();

    const restore = await nextRequest({ method: 'POST', url: '/api/orders/a1/restore' });
    expect(restore.request.body).toEqual({ version: 'AAAAAAAAB9E=' });
    restore.flush({ version: 'v2' });
    (await nextRequest({ method: 'GET', url: '/api/orders/a1' })).flush(
      orderDetails({ version: 'v2' }),
    );
    await page.settle(() =>
      expect(page.element().querySelector('[role="status"]')?.textContent).toContain(
        'Zamówienie przywrócone.',
      ),
    );
  });

  it('shows a stale-view conflict on cancel with the reload action', async () => {
    const page = await open();
    buttonByText(page.element(), 'Anuluj zamówienie').click();
    await page.stable();
    const dialog = dialogWith(page.element(), 'Anulować zamówienie');
    buttonByText(dialog, 'Anuluj zamówienie').click();
    respondProblem(await nextRequest({ method: 'POST', url: '/api/orders/a1/cancel' }), 409, {
      code: 'order_conflict',
    });

    await page.settle(() =>
      expect(page.element().querySelector('[role="alert"]')?.textContent).toContain(
        'Zamówienie zostało zmienione w międzyczasie.',
      ),
    );
    expect(buttonByText(page.element(), 'Wczytaj aktualne dane')).toBeTruthy();
  });

  it('edits a cancelled order with the status read-only', async () => {
    const page = await open(orderDetails({ fulfillmentStatus: 'cancelled' }));
    buttonByText(page.element(), 'Edytuj').click();
    await page.stable();

    expect(page.element().querySelector('#zamowienie-status')).toBeNull();
    buttonByText(page.element(), 'Zapisz zmiany').click();
    const put = await nextRequest({ method: 'PUT', url: '/api/orders/a1' });
    expect(put.request.body.fulfillmentStatus).toBe('cancelled');
    put.flush({ version: 'v2' });
    (await nextRequest({ method: 'GET', url: '/api/orders/a1' })).flush(
      orderDetails({ fulfillmentStatus: 'cancelled', version: 'v2' }),
    );
    await page.settle(() => expect(page.element().querySelector('form')).toBeNull());
  });

  it('warns in the cancel dialog when the order has payments, with the paid amount', async () => {
    // Wpłata 500 i zwrot 500: Zapłacono 0, ale zamówienie ma wpłaty (kryterium bez arytmetyki).
    const page = await open(
      orderDetails({
        payment: {
          paid: '0.00',
          state: 'unpaid',
          amountDue: '1234.50',
          overpayment: null,
          toRefund: null,
        },
        payments: [paymentEntry({ amount: '500.00' })],
        refunds: [refundEntry({ amount: '500.00' })],
      }),
    );
    buttonByText(page.element(), 'Anuluj zamówienie').click();
    await page.stable();

    const dialog = dialogWith(page.element(), 'Anulować zamówienie');
    expect(dialog.textContent).toContain(
      `Zamówienie ma wpłaty: Zapłacono ${formatPln('0.00')}. Wpłaty pozostaną, a pieniądze rozlicz zwrotem.`,
    );
    buttonByText(dialog, 'Anuluj').click();
    await page.stable();
  });

  it('disables entry actions while the order is being edited', async () => {
    const page = await open(orderDetails({ payments: [paymentEntry()] }));
    buttonByText(page.element(), 'Edytuj').click();
    await page.stable();

    const section = page.element().querySelector('app-order-payments') as HTMLElement;
    expect(section.textContent).toContain('Zapisz albo zamknij edycję zamówienia');
    expect(buttonByText(section, 'Dodaj wpłatę').disabled).toBe(true);
    expect(buttonByText(section, 'Dodaj zwrot').disabled).toBe(true);
    expect(buttonByText(section, 'Popraw wpłatę z 29.10.2026').disabled).toBe(true);
    expect(buttonByText(section, 'Usuń wpłatę z 29.10.2026').disabled).toBe(true);

    buttonByText(page.element(), 'Zamknij bez zapisu').click();
    await page.stable();
    expect(buttonByText(section, 'Dodaj wpłatę').disabled).toBe(false);
  });

  it('disables order editing while an entry form is open', async () => {
    const page = await open();
    buttonByText(page.element(), 'Dodaj wpłatę').click();
    await page.stable();

    expect(buttonByText(page.element(), 'Edytuj').disabled).toBe(true);
    expect(page.element().textContent).toContain(
      'Zapisz albo zamknij formularz wpłaty lub zwrotu, aby edytować zamówienie.',
    );

    buttonByText(page.element(), 'Zamknij bez zapisu').click();
    await page.stable();
    expect(buttonByText(page.element(), 'Edytuj').disabled).toBe(false);
  });

  it('reloads the order after an entry is saved and shows the new summary', async () => {
    const page = await open();
    buttonByText(page.element(), 'Dodaj wpłatę').click();
    await page.stable();
    typeInto(page.element().querySelector('#wplata-kwota') as HTMLInputElement, '300');
    const method = page.element().querySelector('#wplata-metoda') as HTMLSelectElement;
    method.value = 'transfer';
    method.dispatchEvent(new Event('input', { bubbles: true }));
    method.dispatchEvent(new Event('change', { bubbles: true }));
    buttonByText(page.element(), 'Zapisz wpłatę').click();

    const post = await nextRequest({ method: 'POST', url: '/api/orders/a1/payments' });
    expect(post.request.body.amount).toBe('300.00');
    post.flush({ id: 'p1', version: 'v1' }, { status: 201, statusText: 'Created' });
    (await nextRequest({ method: 'GET', url: '/api/orders/a1' })).flush(
      orderDetails({
        payment: {
          paid: '300.00',
          state: 'partiallyPaid',
          amountDue: '934.50',
          overpayment: null,
          toRefund: null,
        },
        payments: [paymentEntry()],
      }),
    );

    await page.settle(() => expect(page.element().textContent).toContain('Wpłata zapisana.'));
    expect(page.element().textContent).toContain('Częściowo opłacone');
    expect(page.element().textContent).toContain(formatPln('934.50'));
    expect(buttonByText(page.element(), 'Edytuj').disabled).toBe(false);
  });
});
