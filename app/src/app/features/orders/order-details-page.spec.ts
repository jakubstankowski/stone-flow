import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import {
  nextRequest,
  provideTestHttp,
  respondNoContent,
  respondProblem,
  typeInto,
} from '../../../testing/auth-testing';
import {
  orderDetails,
  paymentEntry,
  refundEntry,
  salesCorrectionEntry,
} from '../../../testing/orders-testing';
import { formatPln } from '../../shared/format/pl-format';
import { OrderDetails } from './order-models';
import { OrderDetailsPage } from './order-details-page';
import { OrderPayments } from './order-payments';
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

function dialogsWith(root: ParentNode, heading: string): number {
  return Array.from(root.querySelectorAll('dialog')).filter((candidate) =>
    candidate.querySelector('h2')?.textContent?.includes(heading),
  ).length;
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
    return { harness, element, stable, settle };
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
    expect(element().textContent).toContain('W realizacji');
    expect(element().textContent).not.toContain('processing');
    expect(element().textContent).toContain('Źródło: Sklep');
  });

  it('shows no shop warnings when the API reports none', async () => {
    const { element } = await open(
      orderDetails({ source: 'wooCommerce', externalId: '77', shopStatus: 'processing' }),
    );
    expect(element().querySelector('[aria-label="Ostrzeżenia ze sklepu"]')).toBeNull();
    expect(element().querySelector('[data-shop-warning]')).toBeNull();
    expect(element().textContent).not.toContain('w sklepie:');
  });

  it('shows the shop warnings in a fixed order as a status region', async () => {
    const { element } = await open(
      orderDetails({
        source: 'wooCommerce',
        externalId: '77',
        shopStatus: 'trash',
        fulfillmentStatus: 'cancelled',
        shopDeletedAtUtc: '2026-11-02T08:15:00Z',
        shopRefundedAmount: '150.50',
        shopWarnings: ['refundedInShop', 'cancelledInShop', 'activeInShop', 'deletedInShop'],
      }),
    );
    const region = element().querySelector('[aria-label="Ostrzeżenia ze sklepu"]');
    expect(region?.getAttribute('role')).toBe('status');
    const warnings = Array.from(element().querySelectorAll('[data-shop-warning]'));
    expect(warnings.map((warning) => warning.getAttribute('data-shop-warning'))).toEqual([
      'deletedInShop',
      'activeInShop',
      'cancelledInShop',
      'refundedInShop',
    ]);
    expect(warnings.every((warning) => region?.contains(warning))).toBe(true);
    expect(warnings.map((warning) => warning.textContent?.trim())).toEqual([
      'Zamówienie usunięte w sklepie (02.11.2026, 09:15).',
      'Zamówienie aktywne w sklepie — ERP nie zmienia niczego w sklepie.',
      'Zamówienie anulowane w sklepie.',
      `Zwrot w sklepie: ${formatPln('150.50')} — informacja, nie tworzy wypływu.`,
    ]);
    expect(element().textContent).toContain('W koszu');
  });

  it.each([
    ['deletedInShop', 'Zamówienie usunięte w sklepie.'],
    ['activeInShop', 'Zamówienie aktywne w sklepie — ERP nie zmienia niczego w sklepie.'],
    ['cancelledInShop', 'Zamówienie anulowane w sklepie.'],
    ['refundedInShop', `Zwrot w sklepie: ${formatPln('0.00')} — informacja, nie tworzy wypływu.`],
  ] as const)('shows only the %s warning when it is the only one', async (code, text) => {
    const { element } = await open(
      orderDetails({ source: 'wooCommerce', externalId: '77', shopWarnings: [code] }),
    );
    const warnings = Array.from(element().querySelectorAll('[data-shop-warning]'));
    expect(warnings.map((warning) => warning.textContent?.trim())).toEqual([text]);
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

  it('explains the value correction and attaches the reduction limit to the value', async () => {
    const page = await open(orderDetails({ salesCorrections: [salesCorrectionEntry()] }));
    buttonByText(page.element(), 'Edytuj').click();
    await page.stable();
    expect(page.element().querySelector('#zamowienie-wartosc-pomoc')?.textContent).toContain(
      'Korekta wartości zmienia wartość zamówienia w okresie jego daty. Oddany towar bez zwrotu pieniędzy zapisz korektą sprzedaży, a zwrócone pieniądze zwrotem.',
    );

    typeInto(page.element().querySelector('#zamowienie-wartosc') as HTMLInputElement, '400');
    buttonByText(page.element(), 'Zapisz zmiany').click();
    respondProblem(await nextRequest({ method: 'PUT', url: '/api/orders/a1' }), 422, {
      code: 'sales_reduction_limit_exceeded',
      minimumValue: '500.00',
    });

    await page.settle(() =>
      expect(page.element().querySelector('#zamowienie-wartosc-blad')?.textContent).toContain(
        `Wartość nie może być niższa niż suma zwrotów sprzedaży i korekt (${formatPln('500.00')}).`,
      ),
    );
    expect((page.element().querySelector('#zamowienie-wartosc') as HTMLInputElement).value).toBe(
      '400',
    );
  });

  it('disables order editing while a sales correction form is open', async () => {
    const page = await open();
    buttonByText(page.element(), 'Dodaj korektę sprzedaży').click();
    await page.stable();
    expect(buttonByText(page.element(), 'Edytuj').disabled).toBe(true);
    buttonByText(page.element(), 'Zamknij bez zapisu').click();
    await page.stable();
    expect(buttonByText(page.element(), 'Edytuj').disabled).toBe(false);
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
      `Zamówienie ma wpłaty (Zapłacono ${formatPln('0.00')}). Wpłaty pozostaną; ewentualną kwotę rozlicz zwrotem.`,
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
      'Edycja zamówienia będzie dostępna po zamknięciu formularza wpłaty lub zwrotu i zakończeniu zapisu albo usuwania wpisu.',
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
    // Żądanie w toku: formularza nie da się zamknąć, a „Edytuj” pozostaje wyłączone.
    await page.stable();
    expect(buttonByText(page.element(), 'Zamknij bez zapisu').disabled).toBe(true);
    expect(buttonByText(page.element(), 'Edytuj').disabled).toBe(true);
    post.flush({ id: 'p1', version: 'v1' }, { status: 201, statusText: 'Created' });
    const reload = await nextRequest({ method: 'GET', url: '/api/orders/a1' });
    // Formularz wpisu jest już zamknięty, ale do końca odświeżenia „Edytuj” pozostaje wyłączone.
    await page.settle(() => expect(page.element().querySelector('#wplata-kwota')).toBeNull());
    expect(buttonByText(page.element(), 'Edytuj').disabled).toBe(true);
    reload.flush(
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

  it('keeps „Edytuj” disabled from the delete confirmation until the reload ends', async () => {
    const page = await open(orderDetails({ payments: [paymentEntry()] }));
    const edit = () => buttonByText(page.element(), 'Edytuj');
    const dialog = () => dialogWith(page.element(), 'Usunąć wpłatę?');

    buttonByText(page.element(), 'Usuń wpłatę z 29.10.2026').click();
    await page.settle(() => expect(edit().disabled).toBe(true));
    expect(dialog().open).toBe(true);
    buttonByText(dialog(), 'Anuluj').click();
    await page.settle(() => expect(edit().disabled).toBe(false));

    buttonByText(page.element(), 'Usuń wpłatę z 29.10.2026').click();
    await page.stable();
    buttonByText(dialog(), 'Usuń').click();
    const removal = await nextRequest({ method: 'DELETE', url: '/api/orders/a1/payments/p1' });
    await page.stable();
    expect(edit().disabled).toBe(true);
    respondNoContent(removal);

    const reload = await nextRequest({ method: 'GET', url: '/api/orders/a1' });
    await page.stable();
    expect(edit().disabled).toBe(true);
    reload.flush(orderDetails());

    await page.settle(() => expect(page.element().textContent).toContain('Wpłata usunięta.'));
    expect(edit().disabled).toBe(false);
  });

  it('never resets an open order edit when the payments section reloads the order', async () => {
    const page = await open();
    buttonByText(page.element(), 'Edytuj').click();
    await page.stable();
    const name = () => page.element().querySelector('#zamowienie-klient-nazwa') as HTMLInputElement;
    typeInto(name(), 'Jan Niezapisany');
    await page.stable();

    const payments = page.harness.fixture.debugElement.query(By.directive(OrderPayments))
      .componentInstance as OrderPayments;
    const reloading = payments.reload()();
    (await nextRequest({ method: 'GET', url: '/api/orders/a1' })).flush(
      orderDetails({
        customer: { ...orderDetails().customer, name: 'Jan Z Serwera' },
        payments: [paymentEntry()],
      }),
    );
    await reloading;
    await page.stable();

    expect(name().value).toBe('Jan Niezapisany');
    expect(page.element().querySelector('app-order-payments')?.textContent).toContain(
      formatPln('300.00'),
    );
  });

  describe('customer anonymization (SF-22)', () => {
    const available = orderDetails({
      anonymization: {
        status: 'available',
        retentionStartDate: '2021-03-15',
        availableFrom: '2026-03-15',
        anonymizedAtUtc: null,
      },
    });
    const anonymizedCustomer = {
      kind: 'person' as const,
      name: '[zanonimizowano]',
      phone: null,
      email: null,
      street: null,
      postalCode: null,
      city: null,
      taxId: null,
    };
    const anonymized = orderDetails({
      customer: anonymizedCustomer,
      comment: null,
      version: 'v2',
      anonymization: {
        status: 'anonymized',
        retentionStartDate: null,
        availableFrom: null,
        anonymizedAtUtc: '2026-11-13T10:15:00Z',
      },
    });

    const block = (root: HTMLElement) => root.querySelector<HTMLElement>('[data-anonymization]');
    const actionButton = (root: HTMLElement) =>
      block(root)?.querySelector<HTMLButtonElement>('button') ?? null;
    const description = (root: HTMLElement) => root.querySelector('#anonimizacja-opis');

    it.each([
      ['disabled', 'Funkcja wyłączona do decyzji o okresie retencji.'],
      [
        'retentionPending',
        'Dostępne po upływie okresu retencji, od 05.12.2026. Okres liczy się od 05.11.2026',
      ],
    ] as const)('disables the action with a visible explanation when %s', async (status, text) => {
      const page = await open(
        orderDetails({
          anonymization: {
            status,
            retentionStartDate: status === 'retentionPending' ? '2026-11-05' : null,
            availableFrom: status === 'retentionPending' ? '2026-12-05' : null,
            anonymizedAtUtc: null,
          },
        }),
      );

      const button = actionButton(page.element());
      expect(button?.textContent?.trim()).toBe('Anonimizuj dane klienta');
      expect(button?.disabled).toBe(true);
      expect(button?.getAttribute('aria-describedby')).toBe('anonimizacja-opis');
      expect(description(page.element())?.textContent).toContain(text);
      expect(block(page.element())?.querySelector('h3')?.textContent?.trim()).toBe(
        'Dane osobowe klienta',
      );
      expect(page.element().textContent).not.toContain('Dane klienta zanonimizowane');
    });

    it('enables the action when the retention period has passed', async () => {
      const page = await open(available);

      expect(actionButton(page.element())?.disabled).toBe(false);
      expect(description(page.element())?.textContent).toContain(
        'Okres retencji upłynął 15.03.2026.',
      );
    });

    it('shows the anonymized state without the action and with the header marker', async () => {
      const page = await open(anonymized);
      const text = page.element().textContent ?? '';

      expect(actionButton(page.element())).toBeNull();
      expect(description(page.element())?.textContent).toContain(
        'Dane klienta zanonimizowano 13.11.2026, 11:15 (Europe/Warsaw). Operacji nie można cofnąć.',
      );
      expect(page.element().querySelector('header')?.textContent).toContain(
        'Dane klienta zanonimizowane',
      );
      expect(text).toContain('[zanonimizowano]');
      expect(text).toContain('Brak danych');
      expect(dialogsWith(page.element(), 'Zanonimizować')).toBe(0);
    });

    it('asks with the irreversibility text and sends nothing when dismissed', async () => {
      const page = await open(available);
      const dialog = () => dialogWith(page.element(), 'Zanonimizować dane klienta');

      actionButton(page.element())?.click();
      await page.stable();
      expect(dialog().open).toBe(true);
      expect(dialog().querySelector('h2')?.textContent).toContain(
        'Zanonimizować dane klienta zamówienia ZR-2026-0001?',
      );
      expect(dialog().textContent).toContain('Operacji nie można cofnąć.');
      expect(dialog().textContent).toContain('a raporty się nie zmienią');
      expect(dialog().textContent).toContain('Synchronizacja ze sklepem nie przywróci danych');
      buttonByText(dialog(), 'Anuluj').click();
      await page.stable();

      http.expectNone('/api/orders/a1/anonymize');
      expect(dialog().open).toBe(false);
    });

    it('confirms, blocks a second click, reloads and marks the order', async () => {
      const page = await open(available);
      actionButton(page.element())?.click();
      await page.stable();
      buttonByText(dialogWith(page.element(), 'Zanonimizować'), 'Anonimizuj dane klienta').click();

      const request = await nextRequest({ method: 'POST', url: '/api/orders/a1/anonymize' });
      expect(request.request.body).toEqual({ version: 'AAAAAAAAB9E=' });
      await page.stable();
      const busy = actionButton(page.element());
      expect(busy?.getAttribute('aria-busy')).toBe('true');
      expect(busy?.textContent).toContain('Anonimizowanie…');
      busy?.click();
      http.expectNone('/api/orders/a1/anonymize');

      request.flush({ version: 'v2' });
      (await nextRequest({ method: 'GET', url: '/api/orders/a1' })).flush(anonymized);
      await page.settle(() =>
        expect(page.element().querySelector('[role="status"]')?.textContent).toContain(
          'Dane klienta zanonimizowane.',
        ),
      );
      expect(page.element().querySelector('header')?.textContent).toContain(
        'Dane klienta zanonimizowane',
      );
      expect(actionButton(page.element())).toBeNull();
    });

    async function confirmAnd(respond: (request: TestRequest) => void) {
      const page = await open(available);
      actionButton(page.element())?.click();
      await page.stable();
      buttonByText(dialogWith(page.element(), 'Zanonimizować'), 'Anonimizuj dane klienta').click();
      respond(await nextRequest({ method: 'POST', url: '/api/orders/a1/anonymize' }));
      return page;
    }

    it('reloads and shows the API detail when the action is no longer available', async () => {
      const page = await confirmAnd((request) =>
        respondProblem(request, 422, {
          code: 'retention_not_elapsed',
          detail: 'Anonimizacja będzie dostępna od 20.12.2026, po upływie okresu retencji.',
        }),
      );
      (await nextRequest({ method: 'GET', url: '/api/orders/a1' })).flush(
        orderDetails({
          anonymization: {
            status: 'retentionPending',
            retentionStartDate: '2026-11-20',
            availableFrom: '2026-12-20',
            anonymizedAtUtc: null,
          },
        }),
      );

      await page.settle(() =>
        expect(page.element().querySelector('[role="alert"]')?.textContent).toContain(
          'Anonimizacja będzie dostępna od 20.12.2026',
        ),
      );
      expect(actionButton(page.element())?.disabled).toBe(true);
    });

    it('shows the conflict with the reload action', async () => {
      const page = await confirmAnd((request) =>
        respondProblem(request, 409, { code: 'order_conflict' }),
      );

      await page.settle(() =>
        expect(page.element().querySelector('[role="alert"]')?.textContent).toContain(
          'Zamówienie zostało zmienione w międzyczasie.',
        ),
      );
      expect(buttonByText(page.element(), 'Wczytaj aktualne dane')).toBeTruthy();
    });

    it('shows a retry message on a network failure', async () => {
      const page = await confirmAnd((request) => request.error(new ProgressEvent('error')));

      await page.settle(() =>
        expect(page.element().querySelector('[role="alert"]')?.textContent).toContain(
          'Nie udało się zanonimizować danych. Spróbuj ponownie.',
        ),
      );
    });

    it('is unavailable while an entry form is open', async () => {
      const page = await open(available);
      buttonByText(page.element(), 'Dodaj wpłatę').click();
      await page.stable();

      expect(actionButton(page.element())?.disabled).toBe(true);

      buttonByText(page.element(), 'Zamknij bez zapisu').click();
      await page.stable();
      expect(actionButton(page.element())?.disabled).toBe(false);
    });

    it('is unavailable while cancelling is in progress', async () => {
      const page = await open(available);
      buttonByText(page.element(), 'Anuluj zamówienie').click();
      await page.stable();
      buttonByText(dialogWith(page.element(), 'Anulować zamówienie'), 'Anuluj zamówienie').click();
      const cancel = await nextRequest({ method: 'POST', url: '/api/orders/a1/cancel' });
      await page.stable();

      expect(actionButton(page.element())?.disabled).toBe(true);

      cancel.flush({ version: 'v2' });
      (await nextRequest({ method: 'GET', url: '/api/orders/a1' })).flush(
        orderDetails({ ...available, fulfillmentStatus: 'cancelled', version: 'v2' }),
      );
      await page.settle(() => expect(actionButton(page.element())?.disabled).toBe(false));
    });

    it('passes the anonymized customer to the edit form as read-only', async () => {
      const page = await open(anonymized);
      buttonByText(page.element(), 'Edytuj').click();
      await page.stable();

      expect(page.element().querySelector('#zamowienie-klient-nazwa')).toBeNull();
      expect(
        page.element().querySelector('#zamowienie-klient-zanonimizowany')?.textContent,
      ).toContain('Dane klienta zanonimizowano; nie można ich zmienić.');
      expect(actionButton(page.element())).toBeNull();
    });
  });
});
