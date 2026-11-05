import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  nextRequest,
  provideTestHttp,
  respondNetworkError,
  respondNoContent,
  respondProblem,
} from '../../../testing/auth-testing';
import { listItem, listPage, orderDetails } from '../../../testing/orders-testing';
import { formatPln } from '../../shared/format/pl-format';
import { OrderInput } from './order-models';
import { LIST_ERROR_MESSAGE, OrdersService } from './orders.service';

const INPUT = { fulfillmentStatus: 'inProgress' } as OrderInput;

function listRequest(): Promise<TestRequest> {
  return nextRequest((request) => request.url === '/api/orders' && request.method === 'GET');
}

describe('OrdersService', () => {
  let service: OrdersService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    service = TestBed.inject(OrdersService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function loadPage(page: number, total = 60): Promise<void> {
    const loading = service.setPage(page);
    (await listRequest()).flush(listPage([listItem()], total, page));
    await loading;
  }

  it('loads the first page sorted by order date descending', async () => {
    const loading = service.load();
    expect(service.loading()).toBe(true);
    const request = await listRequest();

    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('25');
    expect(request.request.params.get('sort')).toBe('orderDate');
    expect(request.request.params.get('direction')).toBe('desc');
    request.flush(listPage([listItem()], 1));
    await loading;

    expect(service.loading()).toBe(false);
    expect(service.items()).toHaveLength(1);
    expect(service.total()).toBe(1);
    expect(service.registryEmpty()).toBe(false);
  });

  it('resets the page to 1 when a filter changes', async () => {
    await loadPage(3);
    expect(service.page()).toBe(3);

    const filtering = service.updateFilters({
      channel: 'olx',
      statuses: ['inProgress', 'problem'],
    });
    const request = await listRequest();
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('channel')).toBe('olx');
    expect(request.request.params.getAll('status')).toEqual(['inProgress', 'problem']);
    request.flush(listPage([], 0));
    await filtering;

    expect(service.page()).toBe(1);
    expect(service.activeFilters().map((filter) => filter.label)).toEqual([
      'Kanał: OLX',
      'Status: W trakcie',
      'Status: Problem / reklamacja',
    ]);
    expect(service.registryEmpty()).toBe(false);
  });

  it('removes one filter or clears all, each time from the first page', async () => {
    const filtering = service.updateFilters({
      statuses: ['completed', 'problem'],
      search: ' Jan ',
    });
    (await listRequest()).flush(listPage([], 0));
    await filtering;
    await loadPage(2);

    const removing = service.removeFilter('status:completed');
    const removed = await listRequest();
    expect(removed.request.params.get('page')).toBe('1');
    expect(removed.request.params.getAll('status')).toEqual(['problem']);
    expect(removed.request.params.get('search')).toBe('Jan');
    removed.flush(listPage([], 0));
    await removing;

    const clearing = service.clearFilters();
    const cleared = await listRequest();
    expect(cleared.request.params.has('status')).toBe(false);
    expect(cleared.request.params.has('search')).toBe(false);
    cleared.flush(listPage([], 0));
    await clearing;
    expect(service.activeFilters()).toEqual([]);
    expect(service.registryEmpty()).toBe(true);
  });

  it('sorts through the API and goes back to the first page', async () => {
    await loadPage(2);
    const sorting = service.setSort({ key: 'value', direction: 'asc' });
    const request = await listRequest();
    expect(request.request.params.get('sort')).toBe('value');
    expect(request.request.params.get('direction')).toBe('asc');
    expect(request.request.params.get('page')).toBe('1');
    request.flush(listPage([], 0));
    await sorting;
  });

  it('does not ask the API for a reversed date range', async () => {
    await service.updateFilters({ dateFrom: '2026-10-31', dateTo: '2026-10-01' });

    expect(service.dateRangeInvalid()).toBe(true);
    http.expectNone((request) => request.url === '/api/orders');
  });

  it('keeps only the latest response', async () => {
    const first = service.updateFilters({ search: 'A' });
    const firstRequest = await listRequest();
    const second = service.updateFilters({ search: 'B' });
    const secondRequest = http.expectOne(
      (request) => request.url === '/api/orders' && request.params.get('search') === 'B',
    );
    secondRequest.flush(listPage([listItem({ id: 'b' })], 1));
    firstRequest.flush(listPage([listItem({ id: 'a' })], 1));
    await Promise.all([first, second]);

    expect(service.items().map((item) => item.id)).toEqual(['b']);
  });

  it('shows the list error', async () => {
    const loading = service.load();
    respondNetworkError(await listRequest());
    await loading;

    expect(service.error()).toBe(LIST_ERROR_MESSAGE);
    expect(service.registryEmpty()).toBe(false);
  });

  it('maps a reused idempotency key to the existing order', async () => {
    const creating = service.create(INPUT, 'key-1');
    respondProblem(await nextRequest('/api/orders'), 409, {
      code: 'idempotency_key_reused',
      orderId: 'a1',
      orderNumber: 'ZR-2026-0001',
    });

    expect(await creating).toEqual({
      kind: 'keyReused',
      orderId: 'a1',
      orderNumber: 'ZR-2026-0001',
    });
    expect(service.takeNotice()).toBeNull();
  });

  it('treats 201 and 200 as saved and leaves a notice for the details', async () => {
    const creating = service.create(INPUT, 'key-1');
    (await nextRequest('/api/orders')).flush(
      { id: 'a1', number: 'ZR-2026-0001', version: 'v1' },
      { status: 200, statusText: 'OK' },
    );

    expect(await creating).toEqual({ kind: 'saved', id: 'a1', number: 'ZR-2026-0001' });
    expect(service.takeNotice()).toBe('Zamówienie zapisane.');
    expect(service.takeNotice()).toBeNull();
  });

  it('passes validation errors from create through', async () => {
    const creating = service.create(INPUT, 'key-1');
    respondProblem(await nextRequest('/api/orders'), 400, {
      errors: { 'customer.name': ['Podaj nazwę.'] },
    });

    const outcome = await creating;
    expect(outcome.kind).toBe('error');
    expect(outcome.kind === 'error' && outcome.error.kind).toBe('validation');
  });

  it.each([
    [409, 'order_conflict', 'conflict'],
    [409, 'order_already_cancelled', 'stateChanged'],
    [409, 'order_not_cancelled', 'stateChanged'],
    [404, 'order_not_found', 'notFound'],
    [422, 'field_owned_by_shop', 'error'],
  ])('maps %i %s on cancel to %s', async (status, code, kind) => {
    const cancelling = service.cancel('a1', 'v1');
    respondProblem(await nextRequest('/api/orders/a1/cancel'), status, { code });

    expect((await cancelling).kind).toBe(kind);
  });

  it('maps a version conflict on update', async () => {
    const updating = service.update('a1', INPUT, 'v1');
    const request = await nextRequest('/api/orders/a1');
    expect(request.request.body).toEqual({ ...INPUT, version: 'v1' });
    respondProblem(request, 409, { code: 'order_conflict' });

    expect(await updating).toEqual({ kind: 'conflict' });
  });

  it('loads details or reports a missing order', async () => {
    const loading = service.loadOrder('a1');
    (await nextRequest('/api/orders/a1')).flush(orderDetails());
    expect((await loading).kind).toBe('loaded');

    const missing = service.loadOrder('zz');
    respondProblem(await nextRequest('/api/orders/zz'), 404, { code: 'order_not_found' });
    expect(await missing).toEqual({ kind: 'notFound' });
  });

  it('filters by payment state, including orders without a state', async () => {
    const filtering = service.updateFilters({ paymentStates: ['partiallyPaid', 'none'] });
    const request = await listRequest();
    expect(request.request.params.getAll('paymentState')).toEqual(['partiallyPaid', 'none']);
    request.flush(listPage([], 0));
    await filtering;
    expect(service.activeFilters().map((filter) => filter.label)).toEqual([
      'Stan płatności: Częściowo opłacone',
      'Stan płatności: Bez stanu (anulowane)',
    ]);

    const removing = service.removeFilter('paymentState:none');
    const removed = await listRequest();
    expect(removed.request.params.getAll('paymentState')).toEqual(['partiallyPaid']);
    removed.flush(listPage([], 0));
    await removing;
  });

  it('adds an entry with the key and treats 200 as saved', async () => {
    const adding = service.addEntry(
      'payment',
      'a1',
      { amount: '300.00', date: '2026-10-29', method: 'transfer', receivedBy: null },
      'key-1',
    );
    const request = await nextRequest('/api/orders/a1/payments');
    expect(request.request.headers.get('Idempotency-Key')).toBe('key-1');
    request.flush({ id: 'p1', version: 'v1' }, { status: 200, statusText: 'OK' });
    expect(await adding).toEqual({ kind: 'saved' });
  });

  it.each([
    [409, { code: 'entry_conflict' }, { kind: 'conflict' }],
    [409, { code: 'idempotency_key_reused', refundId: 'r9' }, { kind: 'keyReused' }],
    [404, { code: 'entry_not_found' }, { kind: 'entryNotFound' }],
    [404, { code: 'order_not_found' }, { kind: 'orderNotFound' }],
    [
      422,
      { code: 'refund_exceeds_paid', available: '1000.00' },
      {
        kind: 'rejected',
        message: `Zwrot nie może przekroczyć kwoty Zapłacono (${formatPln('1000.00')}).`,
      },
    ],
    [
      422,
      { code: 'refund_exceeds_paid' },
      { kind: 'rejected', message: 'Zwrot nie może przekroczyć kwoty Zapłacono.' },
    ],
    [
      422,
      { code: 'paid_would_be_negative' },
      {
        kind: 'rejected',
        message: 'Zapłacono spadłoby poniżej 0 zł. Najpierw popraw lub usuń zwrot.',
      },
    ],
  ])('maps %i %o on an entry change', async (status, body, expected) => {
    const updating = service.updateEntry(
      'refund',
      'a1',
      'r1',
      { kind: 'moneyReturn', amount: '1200.00', date: '2026-10-30', method: 'cash', reason: 'X' },
      'v1',
    );
    const request = await nextRequest('/api/orders/a1/refunds/r1');
    expect(request.request.body.version).toBe('v1');
    respondProblem(request, status, body);
    expect(await updating).toEqual(expected);
  });

  it('maps the sales reduction limit on a correction or a refund with the available amount', async () => {
    const adding = service.addEntry(
      'salesCorrection',
      'a1',
      { amount: '1000.01', date: '2026-10-31', reason: 'X' },
      'key-k',
    );
    respondProblem(await nextRequest('/api/orders/a1/sales-corrections'), 422, {
      code: 'sales_reduction_limit_exceeded',
      available: '1000.00',
    });
    expect(await adding).toEqual({
      kind: 'rejected',
      message: `Suma zwrotów sprzedaży i korekt nie może przekroczyć wartości zamówienia. Dostępna kwota: ${formatPln('1000.00')}.`,
    });
  });

  it('attaches the sales reduction limit on an order edit to the value field', async () => {
    const updating = service.update('a1', INPUT, 'v1');
    respondProblem(await nextRequest('/api/orders/a1'), 422, {
      code: 'sales_reduction_limit_exceeded',
      minimumValue: '500.00',
    });
    expect(await updating).toEqual({
      kind: 'error',
      error: {
        kind: 'validation',
        fieldErrors: {
          value: [
            `Wartość nie może być niższa niż suma zwrotów sprzedaży i korekt (${formatPln('500.00')}).`,
          ],
        },
        detail: null,
      },
    });
  });

  it('deletes an entry with the version in the body', async () => {
    const deleting = service.deleteEntry('payment', 'a1', 'p1', 'v1');
    const request = await nextRequest('/api/orders/a1/payments/p1');
    expect(request.request.method).toBe('DELETE');
    expect(request.request.body).toEqual({ version: 'v1' });
    respondNoContent(request);
    expect(await deleting).toEqual({ kind: 'saved' });
  });
});
