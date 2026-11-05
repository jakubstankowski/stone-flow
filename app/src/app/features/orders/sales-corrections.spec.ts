import { HttpTestingController } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  nextRequest,
  provideTestHttp,
  respondNoContent,
  respondProblem,
  typeInto,
} from '../../../testing/auth-testing';
import { orderDetails, paymentEntry, salesCorrectionEntry } from '../../../testing/orders-testing';
import { formatPln } from '../../shared/format/pl-format';
import { OrderDetails } from './order-models';
import { OrderPayments } from './order-payments';

/** Sekcja „Korekty sprzedaży” renderowana przez wspólny stan wpisów (`app-order-payments`). */
@Component({
  imports: [OrderPayments],
  template: `<app-order-payments
    [order]="order()"
    [locked]="locked()"
    [reload]="reload"
    (activeChange)="active.set($event)"
  />`,
})
class Host {
  readonly order = signal<OrderDetails>(orderDetails());
  readonly locked = signal(false);
  readonly active = signal(false);
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

const WITH_CORRECTION = orderDetails({
  salesCorrections: [salesCorrectionEntry()],
  salesReductions: {
    returnsTotal: '0.00',
    correctionsTotal: '500.00',
    total: '500.00',
    excess: null,
  },
});

describe('SalesCorrections', () => {
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
    const section = () => element.querySelector('app-sales-corrections') as HTMLElement;
    const stable = () => fixture.whenStable();
    const settle = async (check: () => void) =>
      vi.waitFor(async () => {
        await stable();
        check();
      });
    return { host: fixture.componentInstance, element, section, stable, settle };
  }

  it('shows the section below the payments with its description and empty state', async () => {
    const { element, section } = await setup();
    const headings = Array.from(element.querySelectorAll('section > h2')).map((h2) =>
      h2.textContent?.trim(),
    );
    expect(headings).toEqual(['Płatności', 'Korekty sprzedaży']);
    expect(section().textContent).toContain('Nie zmienia kwoty Zapłacono');
    expect(section().textContent).toContain('Brak korekt sprzedaży.');
    expect(section().querySelector('table')).toBeNull();
    expect(section().textContent).not.toContain('przekraczają wartość');
  });

  it('lists corrections with the reduction totals from the backend', async () => {
    const { section } = await setup((host) => host.order.set(WITH_CORRECTION));
    const table = section().querySelector('table') as HTMLTableElement;
    expect(table.querySelector('caption')?.textContent).toContain('Korekty sprzedaży');
    expect(Array.from(table.querySelectorAll('th')).map((th) => th.textContent?.trim())).toEqual([
      'Data',
      'Kwota',
      'Powód',
      'Akcje',
    ]);
    expect(table.querySelector('tbody')?.textContent).toContain('31.10.2026');
    expect(table.querySelector('tbody')?.textContent).toContain(formatPln('500.00'));
    expect(table.querySelector('tbody')?.textContent).toContain('Oddany towar bez zwrotu');
    expect(section().textContent).toContain('Obniżenia sprzedaży razem');
    expect(buttonByText(section(), 'Popraw korektę z 31.10.2026')).toBeTruthy();
  });

  it('warns about an excess above the table, also from older data', async () => {
    const { section } = await setup((host) =>
      host.order.set(
        orderDetails({
          value: '300.00',
          salesCorrections: [salesCorrectionEntry()],
          salesReductions: {
            returnsTotal: '0.00',
            correctionsTotal: '500.00',
            total: '500.00',
            excess: '200.00',
          },
        }),
      ),
    );
    const warning = Array.from(section().querySelectorAll('p')).find((p) =>
      p.textContent?.includes('przekraczają wartość'),
    ) as HTMLElement;
    // Zwykłe odstępy z szablonu; twarde spacje kwoty (U+00A0) zostają.
    expect(warning.textContent?.replace(/[ \n]+/g, ' ').trim()).toBe(
      `Obniżenia sprzedaży przekraczają wartość o ${formatPln('200.00')}. Popraw zwroty sprzedaży lub korekty.`,
    );
    expect(
      warning.compareDocumentPosition(section().querySelector('table') as HTMLTableElement) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
    // Reguła N1: UI nie blokuje poprawy przy nadwyżce, decyduje backend.
    expect(buttonByText(section(), 'Popraw korektę z 31.10.2026').disabled).toBe(false);
  });

  it('adds a correction with an idempotency key and blocks other entry actions meanwhile', async () => {
    const page = await setup((host) =>
      host.order.set(orderDetails({ payments: [paymentEntry()] })),
    );
    buttonByText(page.section(), 'Dodaj korektę sprzedaży').click();
    await page.settle(() => expect(document.activeElement?.id).toBe('korekta-kwota'));
    expect(page.host.active()).toBe(true);
    expect(buttonByText(page.element, 'Dodaj wpłatę').disabled).toBe(true);
    expect(buttonByText(page.element, 'Usuń wpłatę z 29.10.2026').disabled).toBe(true);
    expect(page.section().querySelector('h3')?.textContent).toContain('Dodaj korektę sprzedaży');

    typeInto(page.element.querySelector('#korekta-kwota') as HTMLInputElement, '500');
    const reason = page.element.querySelector('#korekta-powod') as HTMLTextAreaElement;
    reason.value = 'Oddany towar';
    reason.dispatchEvent(new Event('input', { bubbles: true }));
    buttonByText(page.element, 'Zapisz korektę').click();

    const post = await nextRequest({
      method: 'POST',
      url: '/api/orders/a1/sales-corrections',
    });
    expect(post.request.headers.get('Idempotency-Key')).toBeTruthy();
    expect(post.request.body).toEqual({
      amount: '500.00',
      date: expect.any(String),
      reason: 'Oddany towar',
    });
    page.host.next = WITH_CORRECTION;
    post.flush({ id: 'k1', version: 'v1' }, { status: 201, statusText: 'Created' });

    await page.settle(() =>
      expect(page.section().querySelector('[role="status"]')?.textContent).toContain(
        'Korekta sprzedaży zapisana.',
      ),
    );
    expect(page.host.reloads).toBe(1);
    expect(page.host.active()).toBe(false);
    expect(page.element.querySelector('#korekta-kwota')).toBeNull();
    // Zapłacono się nie zmienia: sekcja „Płatności” pokazuje wartości z backendu.
    expect(page.element.querySelector('app-status-badge')?.textContent).toContain('Nieopłacone');
  });

  it('shows the limit with the available amount in the correction form', async () => {
    const page = await setup((host) => host.order.set(WITH_CORRECTION));
    buttonByText(page.section(), 'Popraw korektę z 31.10.2026').click();
    await page.stable();
    typeInto(page.element.querySelector('#korekta-kwota') as HTMLInputElement, '1000,01');
    buttonByText(page.element, 'Zapisz zmiany').click();

    const put = await nextRequest({
      method: 'PUT',
      url: '/api/orders/a1/sales-corrections/k1',
    });
    expect(put.request.headers.has('Idempotency-Key')).toBe(false);
    expect(put.request.body).toEqual({
      amount: '1000.01',
      date: '2026-10-31',
      reason: 'Oddany towar bez zwrotu pieniędzy',
      version: 'AAAAAAAAE1E=',
    });
    respondProblem(put, 422, { code: 'sales_reduction_limit_exceeded', available: '1000.00' });

    await page.settle(() =>
      expect(page.element.querySelector('[role="alert"][tabindex="-1"]')?.textContent).toContain(
        `Suma zwrotów sprzedaży i korekt nie może przekroczyć wartości zamówienia. Dostępna kwota: ${formatPln('1000.00')}.`,
      ),
    );
    expect(page.host.reloads).toBe(0);
  });

  it('shows an edit conflict in the corrections section', async () => {
    const page = await setup((host) => host.order.set(WITH_CORRECTION));
    buttonByText(page.section(), 'Popraw korektę z 31.10.2026').click();
    await page.stable();
    buttonByText(page.element, 'Zapisz zmiany').click();
    respondProblem(
      await nextRequest({ method: 'PUT', url: '/api/orders/a1/sales-corrections/k1' }),
      409,
      { code: 'entry_conflict' },
    );
    await page.settle(() =>
      expect(page.section().querySelector('[role="alert"]')?.textContent).toContain(
        'Wpis został zmieniony w międzyczasie.',
      ),
    );
    expect(page.element.querySelector('section [role="alert"]')?.textContent?.trim()).toBe('');
  });

  it('deletes a correction after a confirmation naming it and its effect', async () => {
    const page = await setup((host) => host.order.set(WITH_CORRECTION));
    buttonByText(page.section(), 'Usuń korektę z 31.10.2026').click();
    await page.stable();
    expect(page.host.active()).toBe(true);
    const dialog = page.element.querySelector('dialog') as HTMLDialogElement;
    expect(dialog.open).toBe(true);
    expect(dialog.textContent).toContain('Usunąć korektę sprzedaży?');
    expect(dialog.textContent).toContain(
      `Korekta sprzedaży ${formatPln('500.00')} z 31.10.2026 zostanie usunięta z zamówienia ZR-2026-0001.`,
    );
    expect(dialog.textContent).toContain('Zmieni się suma obniżeń sprzedaży.');
    expect(dialog.textContent).not.toContain('Zapłacono');

    buttonByText(dialog, 'Usuń').click();
    const request = await nextRequest({
      method: 'DELETE',
      url: '/api/orders/a1/sales-corrections/k1',
    });
    expect(request.request.body).toEqual({ version: 'AAAAAAAAE1E=' });
    page.host.next = orderDetails();
    respondNoContent(request);

    await page.settle(() =>
      expect(page.section().textContent).toContain('Korekta sprzedaży usunięta.'),
    );
    expect(page.section().textContent).toContain('Brak korekt sprzedaży.');
    expect(page.host.active()).toBe(false);
  });

  it('returns focus to the opener after adding a correction', async () => {
    const page = await setup();
    const opener = buttonByText(page.section(), 'Dodaj korektę sprzedaży');
    opener.focus();
    opener.click();
    await page.settle(() => expect(document.activeElement?.id).toBe('korekta-kwota'));
    typeInto(page.element.querySelector('#korekta-kwota') as HTMLInputElement, '500');
    const reason = page.element.querySelector('#korekta-powod') as HTMLTextAreaElement;
    reason.value = 'Oddany towar';
    reason.dispatchEvent(new Event('input', { bubbles: true }));
    buttonByText(page.element, 'Zapisz korektę').click();
    page.host.next = WITH_CORRECTION;
    (await nextRequest({ method: 'POST', url: '/api/orders/a1/sales-corrections' })).flush(
      { id: 'k1', version: 'v1' },
      { status: 201, statusText: 'Created' },
    );
    await page.settle(() => expect(document.activeElement).toBe(opener));
  });

  it('moves focus to the corrections heading when the edited row is gone after saving', async () => {
    const page = await setup((host) => host.order.set(WITH_CORRECTION));
    const opener = buttonByText(page.section(), 'Popraw korektę z 31.10.2026');
    opener.focus();
    opener.click();
    await page.settle(() => expect(document.activeElement?.id).toBe('korekta-kwota'));
    buttonByText(page.element, 'Zapisz zmiany').click();
    // Po odświeżeniu wiersza z przyciskiem „Popraw” już nie ma (np. inny wpis po zmianie w drugiej karcie).
    page.host.next = orderDetails();
    (await nextRequest({ method: 'PUT', url: '/api/orders/a1/sales-corrections/k1' })).flush({
      version: 'v2',
    });
    await page.settle(() => expect(document.activeElement?.id).toBe('korekty-naglowek'));
  });

  it('moves focus to the corrections heading after deleting a correction', async () => {
    const page = await setup((host) => host.order.set(WITH_CORRECTION));
    const opener = buttonByText(page.section(), 'Usuń korektę z 31.10.2026');
    opener.focus();
    opener.click();
    await page.stable();
    buttonByText(page.element.querySelector('dialog') as HTMLDialogElement, 'Usuń').click();
    page.host.next = orderDetails();
    respondNoContent(
      await nextRequest({ method: 'DELETE', url: '/api/orders/a1/sales-corrections/k1' }),
    );
    await page.settle(() => expect(document.activeElement?.id).toBe('korekty-naglowek'));
    expect(page.section().textContent).toContain('Korekta sprzedaży usunięta.');
  });

  it('keeps the form and all entry actions locked while a POST is pending', async () => {
    const page = await setup((host) =>
      host.order.set(orderDetails({ payments: [paymentEntry()] })),
    );
    buttonByText(page.section(), 'Dodaj korektę sprzedaży').click();
    await page.stable();
    typeInto(page.element.querySelector('#korekta-kwota') as HTMLInputElement, '500');
    const reason = page.element.querySelector('#korekta-powod') as HTMLTextAreaElement;
    reason.value = 'Oddany towar';
    reason.dispatchEvent(new Event('input', { bubbles: true }));
    buttonByText(page.element, 'Zapisz korektę').click();

    const post = await nextRequest({ method: 'POST', url: '/api/orders/a1/sales-corrections' });
    await page.stable();
    const close = buttonByText(page.element, 'Zamknij bez zapisu');
    expect(close.disabled).toBe(true);
    close.click();
    await page.stable();
    expect(page.element.querySelector('#korekta-kwota')).not.toBeNull();
    expect(page.host.active()).toBe(true);
    expect(buttonByText(page.section(), 'Dodaj korektę sprzedaży').disabled).toBe(true);
    expect(buttonByText(page.element, 'Dodaj wpłatę').disabled).toBe(true);
    expect(buttonByText(page.element, 'Usuń wpłatę z 29.10.2026').disabled).toBe(true);

    page.host.next = WITH_CORRECTION;
    post.flush({ id: 'k1', version: 'v1' }, { status: 201, statusText: 'Created' });
    await page.settle(() => expect(page.host.active()).toBe(false));
    expect(page.host.reloads).toBe(1);
    expect(page.element.querySelector('#korekta-kwota')).toBeNull();
    expect(buttonByText(page.section(), 'Dodaj korektę sprzedaży').disabled).toBe(false);
    expect(buttonByText(page.element, 'Dodaj wpłatę').disabled).toBe(false);
  });

  it('disables correction actions while the order is being edited', async () => {
    const { section } = await setup((host) => {
      host.locked.set(true);
      host.order.set(WITH_CORRECTION);
    });
    expect(section().textContent).toContain('Zapisz albo zamknij edycję zamówienia');
    expect(buttonByText(section(), 'Dodaj korektę sprzedaży').disabled).toBe(true);
    expect(buttonByText(section(), 'Usuń korektę z 31.10.2026').disabled).toBe(true);
  });
});
