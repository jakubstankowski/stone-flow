import { HttpTestingController } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  nextRequest,
  provideTestHttp,
  respondNetworkError,
  respondNoContent,
  respondProblem,
  typeInto,
} from '../../../testing/auth-testing';
import { orderDetails, paymentEntry, refundEntry } from '../../../testing/orders-testing';
import { formatPln } from '../../shared/format/pl-format';
import { OrderDetails } from './order-models';
import { OrderPayments } from './order-payments';

@Component({
  imports: [OrderPayments],
  template: `<app-order-payments
    [order]="order()"
    [locked]="locked()"
    [reload]="reload"
    (formOpenChange)="formOpen.set($event)"
  />`,
})
class Host {
  readonly order = signal<OrderDetails>(orderDetails());
  readonly locked = signal(false);
  readonly formOpen = signal(false);
  reloads = 0;
  next: OrderDetails | null = null;
  readonly reload = async (): Promise<OrderDetails | null> => {
    this.reloads++;
    if (this.next) {
      this.order.set(this.next);
      this.next = null;
    }
    return this.order();
  };
}

function buttonByText(root: ParentNode, text: string): HTMLButtonElement {
  const button = Array.from(root.querySelectorAll('button')).find(
    (candidate) => candidate.textContent?.trim() === text,
  );
  if (!button) {
    throw new Error(`Brak przycisku „${text}”`);
  }
  return button;
}

function choose(element: HTMLElement, selector: string, value: string): void {
  const select = element.querySelector(selector) as HTMLSelectElement;
  select.value = value;
  select.dispatchEvent(new Event('input', { bubbles: true }));
  select.dispatchEvent(new Event('change', { bubbles: true }));
}

const PAID: OrderDetails['payment'] = {
  paid: '300.00',
  state: 'partiallyPaid',
  amountDue: '934.50',
  overpayment: null,
  toRefund: null,
};

describe('OrderPayments', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    document.body.innerHTML = '';
  });

  async function setup(configure: (host: Host) => void = () => undefined) {
    const fixture = TestBed.createComponent(Host);
    configure(fixture.componentInstance);
    document.body.appendChild(fixture.nativeElement);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const stable = () => fixture.whenStable();
    const settle = async (check: () => void) =>
      vi.waitFor(async () => {
        await stable();
        check();
      });
    return { host: fixture.componentInstance, element, stable, settle };
  }

  it('shows the summary from the backend and the empty lists', async () => {
    const { element } = await setup();
    const text = element.textContent ?? '';

    expect(element.querySelector('h2')?.textContent).toContain('Płatności');
    expect(text).toContain('Zapłacono');
    expect(text).toContain(formatPln('0.00'));
    expect(element.querySelector('app-status-badge')?.textContent).toContain('Nieopłacone');
    expect(text).toContain('Do zapłaty');
    expect(text).toContain(formatPln('1234.50'));
    expect(text).toContain('Brak wpłat.');
    expect(text).toContain('Brak zwrotów.');
    expect(text).not.toContain('Nadpłata');
    expect(element.querySelector('table')).toBeNull();
  });

  it('shows the overpayment warning as text', async () => {
    const { element } = await setup((host) =>
      host.order.set(
        orderDetails({
          payment: {
            paid: '1434.50',
            state: 'paid',
            amountDue: null,
            overpayment: '200.00',
            toRefund: null,
          },
        }),
      ),
    );
    expect(element.textContent).toContain(`Nadpłata ${formatPln('200.00')}`);
    expect(element.querySelector('app-status-badge')?.textContent).toContain('Opłacone');
    expect(element.textContent).not.toContain('Do zapłaty');
  });

  it('shows no state and the amount to refund for a cancelled order', async () => {
    const { element } = await setup((host) =>
      host.order.set(
        orderDetails({
          fulfillmentStatus: 'cancelled',
          payment: {
            paid: '500.00',
            state: null,
            amountDue: null,
            overpayment: null,
            toRefund: '500.00',
          },
        }),
      ),
    );
    expect(element.textContent).toContain('Brak stanu: zamówienie anulowane');
    expect(element.textContent).toContain(`Do zwrotu ${formatPln('500.00')}`);
    expect(element.querySelector('app-status-badge')).toBeNull();

    buttonByText(element, 'Dodaj wpłatę').click();
    await vi.waitFor(() =>
      expect(element.textContent).toContain(
        'Zamówienie jest anulowane. Wpłata zwiększy kwotę do zwrotu.',
      ),
    );
  });

  it('lists payments and refunds in captioned tables', async () => {
    const { element } = await setup((host) =>
      host.order.set(
        orderDetails({
          payment: PAID,
          payments: [paymentEntry({ method: 'cash', receivedBy: 'Anna' })],
          refunds: [refundEntry({ kind: 'salesReturn', reason: 'Oddany towar' })],
        }),
      ),
    );
    const [payments, refunds] = Array.from(element.querySelectorAll('table'));
    expect(payments.querySelector('caption')?.textContent).toContain('Wpłaty');
    expect(Array.from(payments.querySelectorAll('th')).map((th) => th.textContent?.trim())).toEqual(
      ['Data', 'Kwota', 'Metoda', 'Przyjął', 'Akcje'],
    );
    expect(payments.querySelector('tbody')?.textContent).toContain('29.10.2026');
    expect(payments.querySelector('tbody')?.textContent).toContain(formatPln('300.00'));
    expect(payments.querySelector('tbody')?.textContent).toContain('Gotówka');
    expect(payments.querySelector('tbody')?.textContent).toContain('Anna');
    expect(refunds.querySelector('caption')?.textContent).toContain('Zwroty');
    expect(refunds.querySelector('tbody')?.textContent).toContain('Zwrot sprzedaży');
    expect(refunds.querySelector('tbody')?.textContent).toContain('BLIK');
    expect(refunds.querySelector('tbody')?.textContent).toContain('Oddany towar');
    expect(buttonByText(element, 'Popraw wpłatę z 29.10.2026')).toBeTruthy();
    expect(buttonByText(element, 'Usuń zwrot z 30.10.2026')).toBeTruthy();
  });

  it('adds a payment, keeps the key for a retry and returns focus to the opener', async () => {
    const page = await setup();
    const opener = buttonByText(page.element, 'Dodaj wpłatę');
    opener.focus();
    opener.click();
    await page.settle(() => expect(document.activeElement?.id).toBe('wplata-kwota'));
    expect(page.host.formOpen()).toBe(true);
    expect(buttonByText(page.element, 'Dodaj zwrot').disabled).toBe(true);

    typeInto(page.element.querySelector('#wplata-kwota') as HTMLInputElement, '300');
    choose(page.element, '#wplata-metoda', 'transfer');
    buttonByText(page.element, 'Zapisz wpłatę').click();
    const first = await nextRequest({ method: 'POST', url: '/api/orders/a1/payments' });
    const key = first.request.headers.get('Idempotency-Key');
    expect(key).toBeTruthy();
    respondNetworkError(first);
    await page.settle(() =>
      expect(page.element.textContent).toContain('Nie udało się połączyć z serwerem.'),
    );

    page.host.next = orderDetails({ payment: PAID, payments: [paymentEntry()] });
    buttonByText(page.element, 'Zapisz wpłatę').click();
    const retry = await nextRequest({ method: 'POST', url: '/api/orders/a1/payments' });
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    retry.flush({ id: 'p1', version: 'v1' }, { status: 200, statusText: 'OK' });

    await page.settle(() =>
      expect(page.element.querySelector('[role="status"]')?.textContent).toContain(
        'Wpłata zapisana.',
      ),
    );
    expect(page.host.reloads).toBe(1);
    expect(page.host.formOpen()).toBe(false);
    expect(page.element.querySelector('#wplata-kwota')).toBeNull();
    expect(page.element.textContent).toContain('Częściowo opłacone');
    await vi.waitFor(() => expect(document.activeElement).toBe(opener));
  });

  it('shows the refund limit with the available amount in the form', async () => {
    const page = await setup((host) =>
      host.order.set(orderDetails({ payment: PAID, payments: [paymentEntry()] })),
    );
    buttonByText(page.element, 'Dodaj zwrot').click();
    await page.stable();
    (page.element.querySelector('#zwrot-rodzaj-moneyReturn') as HTMLInputElement).click();
    typeInto(page.element.querySelector('#zwrot-kwota') as HTMLInputElement, '400');
    choose(page.element, '#zwrot-metoda', 'transfer');
    const reason = page.element.querySelector('#zwrot-powod') as HTMLTextAreaElement;
    reason.value = 'Zwrot zaliczki';
    reason.dispatchEvent(new Event('input', { bubbles: true }));
    buttonByText(page.element, 'Zapisz zwrot').click();

    const post = await nextRequest({ method: 'POST', url: '/api/orders/a1/refunds' });
    expect(post.request.body).toEqual({
      kind: 'moneyReturn',
      amount: '400.00',
      date: expect.any(String),
      method: 'transfer',
      reason: 'Zwrot zaliczki',
    });
    respondProblem(post, 422, { code: 'refund_exceeds_paid', available: '300.00' });

    await page.settle(() =>
      expect(page.element.querySelector('[role="alert"][tabindex="-1"]')?.textContent).toContain(
        `Zwrot nie może przekroczyć kwoty Zapłacono (${formatPln('300.00')}).`,
      ),
    );
    expect(page.host.reloads).toBe(0);
    expect((page.element.querySelector('#zwrot-kwota') as HTMLInputElement).value).toBe('400');
  });

  it('shows an edit conflict and reloads the current entry into the form', async () => {
    const page = await setup((host) =>
      host.order.set(orderDetails({ payment: PAID, payments: [paymentEntry()] })),
    );
    buttonByText(page.element, 'Popraw wpłatę z 29.10.2026').click();
    await page.stable();
    const amount = () => page.element.querySelector('#wplata-kwota') as HTMLInputElement;
    expect(amount().value).toBe('300,00');
    typeInto(amount(), '250');
    buttonByText(page.element, 'Zapisz zmiany').click();

    const put = await nextRequest({ method: 'PUT', url: '/api/orders/a1/payments/p1' });
    expect(put.request.body).toEqual({
      amount: '250.00',
      date: '2026-10-29',
      method: 'transfer',
      receivedBy: null,
      version: 'AAAAAAAAC1E=',
    });
    respondProblem(put, 409, { code: 'entry_conflict' });
    await page.settle(() =>
      expect(page.element.textContent).toContain('Wpis został zmieniony w międzyczasie.'),
    );
    expect(amount().value).toBe('250');

    page.host.next = orderDetails({
      payment: PAID,
      payments: [paymentEntry({ amount: '350.00', version: 'v9' })],
    });
    buttonByText(page.element, 'Wczytaj aktualne dane').click();
    await page.settle(() => expect(amount().value).toBe('350,00'));
    expect(page.element.textContent).not.toContain('Wpis został zmieniony w międzyczasie.');

    buttonByText(page.element, 'Zapisz zmiany').click();
    const retry = await nextRequest({ method: 'PUT', url: '/api/orders/a1/payments/p1' });
    expect(retry.request.body.version).toBe('v9');
    retry.flush({ version: 'v10' });
    await page.settle(() => expect(page.element.textContent).toContain('Wpłata zapisana.'));
  });

  it('deletes an entry after a confirmation naming the entry and its effect', async () => {
    const page = await setup((host) =>
      host.order.set(orderDetails({ payment: PAID, refunds: [refundEntry()] })),
    );
    buttonByText(page.element, 'Usuń zwrot z 30.10.2026').click();
    await page.stable();
    const dialog = page.element.querySelector('dialog') as HTMLDialogElement;
    expect(dialog.open).toBe(true);
    expect(dialog.textContent).toContain('Usunąć zwrot?');
    expect(dialog.textContent).toContain(
      `Zwrot pieniędzy ${formatPln('100.00')} z 30.10.2026 (BLIK) zostanie usunięty z zamówienia ZR-2026-0001.`,
    );
    expect(dialog.textContent).toContain('Zmieni się kwota Zapłacono i stan płatności.');
    expect(dialog.textContent).toContain('historii zmian');

    buttonByText(dialog, 'Anuluj').click();
    await page.stable();
    http.expectNone('/api/orders/a1/refunds/r1');

    buttonByText(page.element, 'Usuń zwrot z 30.10.2026').click();
    await page.stable();
    buttonByText(dialog, 'Usuń').click();
    const request = await nextRequest({ method: 'DELETE', url: '/api/orders/a1/refunds/r1' });
    expect(request.request.body).toEqual({ version: 'AAAAAAAAD1E=' });
    page.host.next = orderDetails({ payment: PAID });
    respondNoContent(request);

    await page.settle(() => expect(page.element.textContent).toContain('Zwrot usunięty.'));
    expect(page.host.reloads).toBe(1);
    expect(page.element.textContent).toContain('Brak zwrotów.');
  });

  it('explains a rejected payment deletion and keeps the entry', async () => {
    const page = await setup((host) =>
      host.order.set(
        orderDetails({ payment: PAID, payments: [paymentEntry()], refunds: [refundEntry()] }),
      ),
    );
    buttonByText(page.element, 'Usuń wpłatę z 29.10.2026').click();
    await page.stable();
    const dialog = page.element.querySelector('dialog') as HTMLDialogElement;
    expect(dialog.textContent).toContain(
      `Wpłata ${formatPln('300.00')} z 29.10.2026 (Przelew) zostanie usunięta`,
    );
    buttonByText(dialog, 'Usuń').click();
    respondProblem(
      await nextRequest({ method: 'DELETE', url: '/api/orders/a1/payments/p1' }),
      422,
      { code: 'paid_would_be_negative' },
    );

    await page.settle(() =>
      expect(page.element.querySelector('[role="alert"]')?.textContent).toContain(
        'Zapłacono spadłoby poniżej 0 zł. Najpierw popraw lub usuń zwrot.',
      ),
    );
    expect(page.host.reloads).toBe(0);
    expect(buttonByText(page.element, 'Usuń wpłatę z 29.10.2026').disabled).toBe(false);
  });

  it('offers a reload when the entry no longer exists', async () => {
    const page = await setup((host) =>
      host.order.set(orderDetails({ payment: PAID, payments: [paymentEntry()] })),
    );
    buttonByText(page.element, 'Usuń wpłatę z 29.10.2026').click();
    await page.stable();
    buttonByText(page.element.querySelector('dialog') as HTMLDialogElement, 'Usuń').click();
    respondProblem(
      await nextRequest({ method: 'DELETE', url: '/api/orders/a1/payments/p1' }),
      404,
      { code: 'entry_not_found' },
    );
    await page.settle(() => expect(page.element.textContent).toContain('Nie znaleziono wpisu.'));

    page.host.next = orderDetails();
    buttonByText(page.element, 'Wczytaj aktualne dane').click();
    await page.settle(() => expect(page.element.textContent).toContain('Brak wpłat.'));
  });

  it('disables entry actions while the order is locked', async () => {
    const { element } = await setup((host) => {
      host.locked.set(true);
      host.order.set(orderDetails({ payments: [paymentEntry()] }));
    });
    expect(element.textContent).toContain('Zapisz albo zamknij edycję zamówienia');
    expect(buttonByText(element, 'Dodaj wpłatę').disabled).toBe(true);
    expect(buttonByText(element, 'Popraw wpłatę z 29.10.2026').disabled).toBe(true);
  });
});
