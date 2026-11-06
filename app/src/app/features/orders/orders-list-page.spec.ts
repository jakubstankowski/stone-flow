import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { nextRequest, provideTestHttp, respondNetworkError } from '../../../testing/auth-testing';
import { listItem, listPage } from '../../../testing/orders-testing';
import { formatPln } from '../../shared/format/pl-format';
import { OrdersListPage } from './orders-list-page';

@Component({ template: '' })
class Blank {}

function listRequest(): Promise<TestRequest> {
  return nextRequest((request) => request.url === '/api/orders');
}

function buttonByText(element: HTMLElement, text: string): HTMLButtonElement {
  const button = Array.from(element.querySelectorAll('button')).find(
    (candidate) => candidate.textContent?.trim() === text,
  );
  if (!button) {
    throw new Error(`Brak przycisku „${text}”`);
  }
  return button;
}

describe('OrdersListPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'zamowienia', component: OrdersListPage },
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

  async function open() {
    const harness = await RouterTestingHarness.create('/zamowienia');
    const element = harness.routeNativeElement as HTMLElement;
    const stable = async () => {
      await harness.fixture.whenStable();
    };
    return { element, stable };
  }

  async function respond(
    page: Awaited<ReturnType<typeof open>>,
    request: TestRequest,
    body: object,
  ) {
    request.flush(body);
    await vi.waitFor(async () => {
      await page.stable();
      expect(page.element.querySelector('[role="status"] .skeleton')).toBeNull();
    });
  }

  it('shows loading and then one page with formatted columns', async () => {
    const page = await open();
    expect(page.element.querySelector('[role="status"]')?.textContent).toContain('Ładowanie');

    await respond(
      page,
      await listRequest(),
      listPage(
        [
          listItem(),
          listItem({
            id: 'a2',
            number: 'ZR-2026-0002',
            fulfillmentStatus: 'cancelled',
            channel: 'shop',
            paid: '500.00',
            paymentState: null,
          }),
        ],
        2,
      ),
    );

    const rows = Array.from(page.element.querySelectorAll('tbody tr'));
    expect(rows).toHaveLength(2);
    const cells = Array.from(rows[0].querySelectorAll('td')).map((cell) =>
      cell.textContent?.trim(),
    );
    expect(cells).toEqual([
      'ZR-2026-0001',
      '28.10.2026',
      'Jan Testowy',
      'OLX',
      'Ręczne',
      formatPln('1234.50'),
      formatPln('0.00'),
      'Nieopłacone',
      'W trakcie',
    ]);
    expect(rows[0].querySelector('a')?.getAttribute('href')).toBe('/zamowienia/a1');
    expect(rows[1].textContent).toContain('Anulowane');
    expect(rows[1].textContent).toContain('Sklep');
    expect(rows[1].textContent).toContain('Ręczne');
    expect(rows[1].textContent).toContain(formatPln('500.00'));
    expect(rows[1].textContent).toContain('brak stanu');
    expect(rows[1].querySelector('app-status-badge + app-status-badge')).toBeNull();
    const headers = Array.from(page.element.querySelectorAll('th')).map((th) =>
      th.textContent?.trim(),
    );
    expect(headers.join('|')).toContain('Zapłacono|Stan płatności|Status realizacji');
  });

  it('shows the empty registry with an action instead of the table', async () => {
    const page = await open();
    await respond(page, await listRequest(), listPage([], 0));

    expect(page.element.querySelector('table')).toBeNull();
    expect(page.element.textContent).toContain('Brak zamówień');
    const buttons = Array.from(page.element.querySelectorAll('button')).filter(
      (button) => button.textContent?.trim() === 'Dodaj zamówienie',
    );
    expect(buttons).toHaveLength(1);
    buttons[0].click();
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/zamowienia/nowe'));
  });

  it('shows the error with a retry', async () => {
    const page = await open();
    respondNetworkError(await listRequest());
    await vi.waitFor(async () => {
      await page.stable();
      expect(page.element.querySelector('[role="alert"]')?.textContent).toContain(
        'Nie udało się wczytać zamówień.',
      );
    });

    buttonByText(page.element, 'Spróbuj ponownie').click();
    await respond(page, await listRequest(), listPage([listItem()], 1));
    expect(page.element.querySelectorAll('tbody tr')).toHaveLength(1);
  });

  it('sorts and pages through the API', async () => {
    const page = await open();
    await respond(page, await listRequest(), listPage([listItem()], 60));

    const valueHeader = Array.from(page.element.querySelectorAll('th')).find((th) =>
      th.textContent?.includes('Wartość brutto'),
    );
    expect(page.element.querySelector('th[aria-sort="descending"]')?.textContent).toContain('Data');
    valueHeader?.querySelector('button')?.click();
    const sorted = await listRequest();
    expect(sorted.request.params.get('sort')).toBe('value');
    expect(sorted.request.params.get('direction')).toBe('asc');
    await respond(page, sorted, listPage([listItem()], 60));
    expect(valueHeader?.getAttribute('aria-sort')).toBe('ascending');

    buttonByText(page.element, 'Następna').click();
    const next = await listRequest();
    expect(next.request.params.get('page')).toBe('2');
    expect(next.request.params.get('sort')).toBe('value');
    await respond(page, next, listPage([listItem()], 60, 2));
  });

  it('filters from the first page and shows the filtered empty state from the table', async () => {
    const page = await open();
    await respond(page, await listRequest(), listPage([listItem()], 60));
    buttonByText(page.element, 'Następna').click();
    await respond(page, await listRequest(), listPage([listItem()], 60, 2));

    const channel = page.element.querySelector('#zamowienia-kanal') as HTMLSelectElement;
    channel.value = 'phone';
    channel.dispatchEvent(new Event('change', { bubbles: true }));
    const filtered = await listRequest();
    expect(filtered.request.params.get('page')).toBe('1');
    expect(filtered.request.params.get('channel')).toBe('phone');
    await respond(page, filtered, listPage([], 0));

    expect(page.element.textContent).toContain('Brak wyników dla filtrów');
    expect(page.element.textContent).toContain('Kanał: Telefon');
    expect(page.element.textContent).not.toContain('Brak zamówień');

    const status = Array.from(page.element.querySelectorAll('label')).find(
      (label) => label.textContent?.trim() === 'Problem / reklamacja',
    );
    (status?.querySelector('input') as HTMLInputElement).click();
    const withStatus = await listRequest();
    expect(withStatus.request.params.getAll('status')).toEqual(['problem']);
    await respond(page, withStatus, listPage([], 0));

    const checkState = async (labelText: string) => {
      const label = Array.from(page.element.querySelectorAll('label')).find(
        (candidate) => candidate.textContent?.trim() === labelText,
      );
      (label?.querySelector('input') as HTMLInputElement).click();
      return listRequest();
    };
    const withNone = await checkState('Bez stanu (anulowane)');
    expect(withNone.request.params.getAll('paymentState')).toEqual(['none']);
    await respond(page, withNone, listPage([], 0));
    const withUnpaid = await checkState('Nieopłacone');
    expect(withUnpaid.request.params.get('page')).toBe('1');
    expect(withUnpaid.request.params.getAll('paymentState')).toEqual(['unpaid', 'none']);
    await respond(page, withUnpaid, listPage([], 0));
    expect(page.element.textContent).toContain('Stan płatności: Nieopłacone');
    expect(page.element.textContent).toContain('Stan płatności: Bez stanu (anulowane)');

    buttonByText(page.element, 'Wyczyść filtry').click();
    const cleared = await listRequest();
    expect(cleared.request.params.has('channel')).toBe(false);
    expect(cleared.request.params.has('status')).toBe(false);
    expect(cleared.request.params.has('paymentState')).toBe(false);
    await respond(page, cleared, listPage([listItem()], 1));
  });

  it('asks for hidden shop statuses only while the toggle is on', async () => {
    const page = await open();
    const first = await listRequest();
    expect(first.request.params.has('includeHiddenShopStatuses')).toBe(false);
    await respond(page, first, listPage([listItem()], 1));

    const label = Array.from(page.element.querySelectorAll('label')).find(
      (candidate) =>
        candidate.textContent?.trim() === 'Pokaż oczekujące, nieudane i szkice ze sklepu',
    );
    const toggle = label?.querySelector('input') as HTMLInputElement;
    expect(toggle.type).toBe('checkbox');
    expect(toggle.checked).toBe(false);

    toggle.click();
    const shown = await listRequest();
    expect(shown.request.params.get('includeHiddenShopStatuses')).toBe('true');
    expect(shown.request.params.get('page')).toBe('1');
    await respond(page, shown, listPage([listItem()], 1));
    expect(toggle.checked).toBe(true);
    expect(page.element.textContent).toContain('Ze sklepu także: oczekujące, nieudane i szkice');

    toggle.click();
    const hidden = await listRequest();
    expect(hidden.request.params.has('includeHiddenShopStatuses')).toBe(false);
    await respond(page, hidden, listPage([listItem()], 1));
    expect(toggle.checked).toBe(false);
  });

  it('shows the Polish shop status under the fulfillment status for shop orders', async () => {
    const page = await open();
    await respond(
      page,
      await listRequest(),
      listPage(
        [
          listItem({ source: 'wooCommerce', channel: 'shop', shopStatus: 'pending' }),
          listItem({
            id: 'a2',
            number: 'ZR-2026-0002',
            source: 'wooCommerce',
            shopStatus: 'x-own',
          }),
          listItem({ id: 'a3', number: 'ZR-2026-0003' }),
        ],
        3,
      ),
    );

    const statusCells = Array.from(page.element.querySelectorAll('tbody tr')).map((row) =>
      Array.from(row.querySelectorAll('td')).at(-1),
    );
    const shopBadge = statusCells[0]?.querySelector('.badge-ghost');
    expect(shopBadge?.textContent?.trim()).toBe('Status w sklepie: Oczekuje na płatność');
    expect(shopBadge?.querySelector('.sr-only')?.textContent).toBe('Status w sklepie: ');
    expect(statusCells[0]?.textContent).toContain('W trakcie');
    expect(statusCells[1]?.querySelector('.badge-ghost')?.textContent).toContain('x-own');
    expect(statusCells[2]?.querySelector('.badge-ghost')).toBeNull();
  });

  it('searches on change and does not query a reversed date range', async () => {
    const page = await open();
    await respond(page, await listRequest(), listPage([listItem()], 1));

    const search = page.element.querySelector('#zamowienia-szukaj') as HTMLInputElement;
    search.value = '50%_';
    search.dispatchEvent(new Event('change', { bubbles: true }));
    const searched = await listRequest();
    expect(searched.request.params.get('search')).toBe('50%_');
    await respond(page, searched, listPage([], 0));

    const from = page.element.querySelector('#zamowienia-data-od') as HTMLInputElement;
    from.value = '2026-10-31';
    from.dispatchEvent(new Event('change', { bubbles: true }));
    await respond(page, await listRequest(), listPage([], 0));
    const to = page.element.querySelector('#zamowienia-data-do') as HTMLInputElement;
    to.value = '2026-10-01';
    to.dispatchEvent(new Event('change', { bubbles: true }));
    await page.stable();

    http.expectNone((request) => request.url === '/api/orders');
    expect(page.element.querySelector('#zamowienia-data-od-blad')?.textContent).toContain(
      'Data „od” nie może być późniejsza niż data „do”.',
    );
  });
});
