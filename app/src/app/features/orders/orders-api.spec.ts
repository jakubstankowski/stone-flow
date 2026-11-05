import { HttpErrorResponse } from '@angular/common/http';
import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTestHttp, respondProblem } from '../../../testing/auth-testing';
import { toApiError } from '../../core/http/api-error';
import { OrderInput, OrderListQuery } from './order-models';
import { OrdersApi } from './orders-api';

const QUERY: OrderListQuery = {
  page: 2,
  pageSize: 25,
  sort: 'orderDate',
  direction: 'desc',
  dateFrom: '2026-10-01',
  dateTo: '2026-10-31',
  channel: 'olx',
  source: 'manual',
  statuses: ['inProgress', 'problem'],
  search: 'Jan',
};

const INPUT: OrderInput = {
  orderDate: '2026-10-28',
  channel: 'phone',
  value: '1234.50',
  fulfillmentStatus: 'inProgress',
  customer: {
    kind: 'person',
    name: 'Jan Testowy',
    phone: null,
    email: null,
    street: null,
    postalCode: null,
    city: null,
    taxId: null,
  },
  operational: {
    suppliers: [],
    zoReferences: [],
    invoiceNumber: null,
    hasPhotos: false,
    hasReview: false,
    notes: null,
  },
  comment: null,
};

describe('OrdersApi', () => {
  let api: OrdersApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    api = TestBed.inject(OrdersApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists with a relative URL and a repeated status parameter', async () => {
    const result = api.list(QUERY);
    const request = http.expectOne((candidate) => candidate.url === '/api/orders');

    expect(request.request.method).toBe('GET');
    expect(request.request.urlWithParams).toBe(
      '/api/orders?page=2&pageSize=25&sort=orderDate&direction=desc&dateFrom=2026-10-01' +
        '&dateTo=2026-10-31&channel=olx&source=manual&search=Jan&status=inProgress&status=problem',
    );
    request.flush({ items: [], page: 2, pageSize: 25, total: 30 });
    expect((await result).total).toBe(30);
  });

  it('omits empty filters', async () => {
    const result = api.list({
      ...QUERY,
      page: 1,
      dateFrom: null,
      dateTo: null,
      channel: null,
      source: null,
      statuses: [],
      search: null,
    });
    const request = http.expectOne((candidate) => candidate.url === '/api/orders');

    expect(request.request.urlWithParams).toBe(
      '/api/orders?page=1&pageSize=25&sort=orderDate&direction=desc',
    );
    request.flush({ items: [], page: 1, pageSize: 25, total: 0 });
    await result;
  });

  it('gets the details of an order', async () => {
    const result = api.get('a1');
    const request = http.expectOne('/api/orders/a1');
    expect(request.request.method).toBe('GET');
    request.flush({ id: 'a1' });
    expect((await result).id).toBe('a1');
  });

  it('creates with the Idempotency-Key header and tells 201 from 200', async () => {
    const first = api.create(INPUT, 'key-1');
    const created = http.expectOne('/api/orders');
    expect(created.request.method).toBe('POST');
    expect(created.request.headers.get('Idempotency-Key')).toBe('key-1');
    expect(created.request.body).toEqual(INPUT);
    created.flush(
      { id: 'a1', number: 'ZR-2026-0001', version: 'AAA=' },
      { status: 201, statusText: 'Created' },
    );
    expect(await first).toEqual({
      created: true,
      order: { id: 'a1', number: 'ZR-2026-0001', version: 'AAA=' },
    });

    const repeated = api.create(INPUT, 'key-1');
    http
      .expectOne('/api/orders')
      .flush(
        { id: 'a1', number: 'ZR-2026-0001', version: 'AAE=' },
        { status: 200, statusText: 'OK' },
      );
    expect((await repeated).created).toBe(false);
  });

  it('updates, cancels and restores with the version', async () => {
    const update = api.update('a1', { ...INPUT, version: 'v1' });
    const put = http.expectOne('/api/orders/a1');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ ...INPUT, version: 'v1' });
    put.flush({ version: 'v2' });
    expect(await update).toEqual({ version: 'v2' });

    const cancel = api.cancel('a1', 'v2');
    const cancelRequest = http.expectOne('/api/orders/a1/cancel');
    expect(cancelRequest.request.method).toBe('POST');
    expect(cancelRequest.request.body).toEqual({ version: 'v2' });
    cancelRequest.flush({ version: 'v3' });
    expect(await cancel).toEqual({ version: 'v3' });

    const restore = api.restore('a1', 'v3');
    const restoreRequest = http.expectOne('/api/orders/a1/restore');
    expect(restoreRequest.request.body).toEqual({ version: 'v3' });
    restoreRequest.flush({ version: 'v4' });
    expect(await restore).toEqual({ version: 'v4' });
  });

  it('rejects with an error that toApiError maps to the problem code', async () => {
    const update = api.update('a1', { ...INPUT, version: 'v1' });
    respondProblem(http.expectOne('/api/orders/a1'), 409, {
      code: 'order_conflict',
      detail: 'Zamówienie zostało zmienione.',
    });

    const error = await update.catch((caught: unknown) => caught);
    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect(toApiError(error)).toEqual({
      kind: 'problem',
      status: 409,
      code: 'order_conflict',
      detail: 'Zamówienie zostało zmienione.',
    });
  });
});
